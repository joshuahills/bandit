using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bandit.Platform.Windows;

internal static partial class IphlpapiInterop
{
    public const uint AF_INET  = 2;
    public const uint AF_INET6 = 23;

    // TCP_TABLE_OWNER_PID_ALL — table includes PID column.
    public const uint TCP_TABLE_OWNER_PID_ALL = 5;

    // UDP_TABLE_OWNER_PID — table includes PID column.
    public const uint UDP_TABLE_OWNER_PID = 1;

    public const uint ERROR_INSUFFICIENT_BUFFER = 122;
    public const uint NO_ERROR = 0;

    [LibraryImport("iphlpapi.dll", SetLastError = false)]
    public static partial uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref uint pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        uint ulAf,
        uint TableClass,
        uint Reserved);

    [LibraryImport("iphlpapi.dll", SetLastError = false)]
    public static partial uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref uint pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        uint ulAf,
        uint TableClass,
        uint Reserved);

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;        // IPv4 in network byte order
        public uint dwLocalPort;        // low 16 bits, network byte order
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [InlineArray(16)]
    public struct InlineByte16
    {
        private byte _;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCP6ROW_OWNER_PID
    {
        public InlineByte16 ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public InlineByte16 ucRemoteAddr;
        public uint dwRemoteScopeId;
        public uint dwRemotePort;
        public uint dwState;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_UDP6ROW_OWNER_PID
    {
        public InlineByte16 ucLocalAddr;
        public uint dwLocalScopeId;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }
}
