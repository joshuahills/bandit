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
    /// <summary>
    /// Snapshot of every TCP/UDP × IPv4/IPv6 connection currently owned by
    /// <paramref name="pid"/>. Returns an empty list if the process has no
    /// open sockets or any of the underlying iphlpapi calls fail.
    /// </summary>
    public static IReadOnlyList<ProcessConnection> SnapshotForPid(int pid)
    {
        var result = new List<ProcessConnection>();
        AppendTcp(result, AF_INET, pid);
        AppendTcp(result, AF_INET6, pid);
        AppendUdp(result, AF_INET, pid);
        AppendUdp(result, AF_INET6, pid);
        return result;
    }

    private static unsafe void AppendTcp(List<ProcessConnection> list, uint family, int filterPid)
    {
        uint size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_ALL, 0) != NO_ERROR) return;

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
        uint size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, UDP_TABLE_OWNER_PID, 0);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetExtendedUdpTable(buffer, ref size, false, family, UDP_TABLE_OWNER_PID, 0) != NO_ERROR) return;

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
            Local:      ToIPv6(row.ucLocalAddr),
            LocalPort:  ExtractPort(row.dwLocalPort),
            Remote:     ToIPv6(row.ucRemoteAddr),
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
            Local:      ToIPv6(row.ucLocalAddr),
            LocalPort:  ExtractPort(row.dwLocalPort),
            Remote:     null,
            RemotePort: 0,
            State:      TcpState.None);

    internal static int ExtractPort(uint dwPort) =>
        BinaryPrimitives.ReverseEndianness((ushort)(dwPort & 0xFFFF));

    internal static TcpState ToTcpState(int state) =>
        state is >= 1 and <= 12 ? (TcpState)state : TcpState.None;

    private static IPAddress ToIPv6(InlineByte16 inlineAddr)
    {
        var bytes = new byte[16];
        for (int i = 0; i < 16; i++) bytes[i] = inlineAddr[i];
        return new IPAddress(bytes);
    }
}
