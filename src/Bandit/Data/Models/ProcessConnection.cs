using System.Net;

namespace Bandit.Data.Models;

public sealed record ProcessConnection(
    int Pid,
    Protocol Protocol,
    AddressFamily Family,
    IPAddress Local,
    int LocalPort,
    IPAddress? Remote,
    int RemotePort,
    TcpState State);

public enum Protocol { Tcp, Udp }

public enum AddressFamily { IPv4, IPv6 }

/// <summary>
/// Subset of TCP states we surface. UDP rows always carry <see cref="None"/>.
/// Values match the Win32 MIB_TCP_STATE constants.
/// </summary>
public enum TcpState
{
    None = 0,
    Closed = 1,
    Listening = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12,
}
