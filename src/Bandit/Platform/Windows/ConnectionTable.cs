using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using Bandit.Data.Models;
using static Bandit.Platform.Windows.IphlpapiInterop;
using AddressFamily = Bandit.Data.Models.AddressFamily;
using Protocol = Bandit.Data.Models.Protocol;

namespace Bandit.Platform.Windows;

internal static class ConnectionTable
{
    // Cap on how many times we'll retry a table fetch when the kernel reports
    // ERROR_INSUFFICIENT_BUFFER mid-call (i.e. the table grew between probe
    // and fetch). 5 is generous — in practice one retry is enough; we just
    // want a hard ceiling so we never spin on a pathologically churny system.
    private const int MaxResizeRetries = 5;

    // Cache the most recent per-PID snapshot for a brief window so repeated
    // calls within a single UI tick (or within the natural ~1 Hz refresh
    // cadence) don't re-walk all four kernel tables. Each call walks the
    // entire system's connection set and only filters to PID afterwards, so
    // the cost scales with total host activity, not just this process.
    private const int CacheTtlMs = 750;
    private static readonly object CacheGate = new();
    private static int _cachedPid = -1;
    private static long _cachedAtTicks;
    private static IReadOnlyList<ProcessConnection>? _cachedSnapshot;

    /// <summary>
    /// Snapshot of every TCP/UDP × IPv4/IPv6 connection currently owned by
    /// <paramref name="pid"/>. Each table is queried independently; if one
    /// of the underlying iphlpapi calls fails (e.g. the kernel keeps growing
    /// the table faster than we can size and copy it) the rows from that
    /// table are skipped while the rest are still returned. Worst case is an
    /// empty list when every table fails. Results are cached briefly per PID
    /// to keep redraws cheap on hosts with large connection counts.
    /// </summary>
    public static IReadOnlyList<ProcessConnection> SnapshotForPid(int pid)
    {
        long now = Environment.TickCount64;
        lock (CacheGate)
        {
            if (_cachedPid == pid && _cachedSnapshot is not null && now - _cachedAtTicks < CacheTtlMs)
                return _cachedSnapshot;
        }

        var result = new List<ProcessConnection>();
        AppendTcp(result, AF_INET, pid);
        AppendTcp(result, AF_INET6, pid);
        AppendUdp(result, AF_INET, pid);
        AppendUdp(result, AF_INET6, pid);

        lock (CacheGate)
        {
            _cachedPid = pid;
            _cachedAtTicks = now;
            _cachedSnapshot = result;
        }
        return result;
    }

    /// <summary>Clears the per-PID snapshot cache. Test-only.</summary>
    internal static void ResetCache()
    {
        lock (CacheGate)
        {
            _cachedPid = -1;
            _cachedAtTicks = 0;
            _cachedSnapshot = null;
        }
    }

    private static unsafe void AppendTcp(List<ProcessConnection> list, uint family, int filterPid)
    {
        if (!FetchTable((IntPtr buf, ref uint sz) => GetExtendedTcpTable(buf, ref sz, false, family, TCP_TABLE_OWNER_PID_ALL, 0),
                         out var buffer))
            return;

        try
        {
            byte* p = (byte*)buffer;
            int entryCount = *(int*)p;

            if (family == AF_INET)
            {
                int rowSize = sizeof(MIB_TCPROW_OWNER_PID);
                for (int i = 0; i < entryCount; i++)
                {
                    var row = *(MIB_TCPROW_OWNER_PID*)(p + 4 + i * rowSize);
                    if ((int)row.dwOwningPid != filterPid) continue;
                    list.Add(BuildTcp4(row));
                }
            }
            else
            {
                int rowSize = sizeof(MIB_TCP6ROW_OWNER_PID);
                for (int i = 0; i < entryCount; i++)
                {
                    var row = *(MIB_TCP6ROW_OWNER_PID*)(p + 4 + i * rowSize);
                    if ((int)row.dwOwningPid != filterPid) continue;
                    list.Add(BuildTcp6(row));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static unsafe void AppendUdp(List<ProcessConnection> list, uint family, int filterPid)
    {
        if (!FetchTable((IntPtr buf, ref uint sz) => GetExtendedUdpTable(buf, ref sz, false, family, UDP_TABLE_OWNER_PID, 0),
                         out var buffer))
            return;

        try
        {
            byte* p = (byte*)buffer;
            int entryCount = *(int*)p;

            if (family == AF_INET)
            {
                int rowSize = sizeof(MIB_UDPROW_OWNER_PID);
                for (int i = 0; i < entryCount; i++)
                {
                    var row = *(MIB_UDPROW_OWNER_PID*)(p + 4 + i * rowSize);
                    if ((int)row.dwOwningPid != filterPid) continue;
                    list.Add(BuildUdp4(row));
                }
            }
            else
            {
                int rowSize = sizeof(MIB_UDP6ROW_OWNER_PID);
                for (int i = 0; i < entryCount; i++)
                {
                    var row = *(MIB_UDP6ROW_OWNER_PID*)(p + 4 + i * rowSize);
                    if ((int)row.dwOwningPid != filterPid) continue;
                    list.Add(BuildUdp6(row));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private delegate uint TableFetcher(IntPtr buffer, ref uint size);

    /// <summary>
    /// Probe-then-fetch with a bounded retry loop. The kernel can grow the
    /// connection table between the size probe and the populated read; when
    /// that happens the second call returns ERROR_INSUFFICIENT_BUFFER along
    /// with the new required size, and we re-allocate and try again. Without
    /// the loop, busy systems silently lose entire snapshots.
    /// </summary>
    private static bool FetchTable(TableFetcher fetch, out IntPtr buffer)
    {
        buffer = IntPtr.Zero;
        uint size = 0;

        // Initial probe — this is expected to return ERROR_INSUFFICIENT_BUFFER
        // and write the required size into `size`.
        fetch(IntPtr.Zero, ref size);
        if (size == 0) return false;

        for (int attempt = 0; attempt < MaxResizeRetries; attempt++)
        {
            buffer = Marshal.AllocHGlobal((int)size);
            uint rc = fetch(buffer, ref size);
            if (rc == NO_ERROR) return true;

            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;

            // Only retry if the kernel told us to grow the buffer. Anything
            // else is a real error and we give up.
            if (rc != ERROR_INSUFFICIENT_BUFFER) return false;
        }
        return false;
    }

    internal static ProcessConnection BuildTcp4(MIB_TCPROW_OWNER_PID row) =>
        new(
            Pid:        (int)row.dwOwningPid,
            Protocol:   Protocol.Tcp,
            Family:     AddressFamily.IPv4,
            Local:      new IPAddress((long)row.dwLocalAddr),
            LocalPort:  ExtractPort(row.dwLocalPort),
            Remote:     new IPAddress((long)row.dwRemoteAddr),
            RemotePort: ExtractPort(row.dwRemotePort),
            State:      ToTcpState((int)row.dwState));

    internal static ProcessConnection BuildTcp6(MIB_TCP6ROW_OWNER_PID row) =>
        new(
            Pid:        (int)row.dwOwningPid,
            Protocol:   Protocol.Tcp,
            Family:     AddressFamily.IPv6,
            Local:      ToIPv6(row.ucLocalAddr,  row.dwLocalScopeId),
            LocalPort:  ExtractPort(row.dwLocalPort),
            Remote:     ToIPv6(row.ucRemoteAddr, row.dwRemoteScopeId),
            RemotePort: ExtractPort(row.dwRemotePort),
            State:      ToTcpState((int)row.dwState));

    internal static ProcessConnection BuildUdp4(MIB_UDPROW_OWNER_PID row) =>
        new(
            Pid:        (int)row.dwOwningPid,
            Protocol:   Protocol.Udp,
            Family:     AddressFamily.IPv4,
            Local:      new IPAddress((long)row.dwLocalAddr),
            LocalPort:  ExtractPort(row.dwLocalPort),
            Remote:     null,
            RemotePort: 0,
            State:      TcpState.None);

    internal static ProcessConnection BuildUdp6(MIB_UDP6ROW_OWNER_PID row) =>
        new(
            Pid:        (int)row.dwOwningPid,
            Protocol:   Protocol.Udp,
            Family:     AddressFamily.IPv6,
            Local:      ToIPv6(row.ucLocalAddr, row.dwLocalScopeId),
            LocalPort:  ExtractPort(row.dwLocalPort),
            Remote:     null,
            RemotePort: 0,
            State:      TcpState.None);

    internal static int ExtractPort(uint dwPort) =>
        BinaryPrimitives.ReverseEndianness((ushort)(dwPort & 0xFFFF));

    internal static TcpState ToTcpState(int state) =>
        state is >= 1 and <= 12 ? (TcpState)state : TcpState.None;

    /// <summary>
    /// Builds an IPv6 <see cref="IPAddress"/> with the original scope ID
    /// preserved. Without this, link-local sockets (fe80::%N) collapse to a
    /// scope-less address and distinct connections on different interfaces
    /// can render as duplicates in the UI.
    /// </summary>
    internal static IPAddress ToIPv6(InlineByte16 inlineAddr, uint scopeId)
    {
        var bytes = new byte[16];
        for (int i = 0; i < 16; i++) bytes[i] = inlineAddr[i];
        return new IPAddress(bytes, scopeId);
    }
}
