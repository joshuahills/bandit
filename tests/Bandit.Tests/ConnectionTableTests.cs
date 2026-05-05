using System.Net;
using Bandit.Data.Models;
using Bandit.Platform.Windows;
using Xunit;
using static Bandit.Platform.Windows.IphlpapiInterop;

namespace Bandit.Tests;

public class ConnectionTableTests
{
    [Theory]
    [InlineData(0x5000u, 80)]    // 0x0050 in network order
    [InlineData(0xBB01u, 443)]   // 0x01BB
    [InlineData(0xFFFFu, 65535)]
    [InlineData(0u, 0)]
    public void ExtractPort_decodes_network_byte_order_low_16_bits(uint dwPort, int expected)
    {
        Assert.Equal(expected, ConnectionTable.ExtractPort(dwPort));
    }

    [Theory]
    [InlineData(0,  TcpState.None)]
    [InlineData(1,  TcpState.Closed)]
    [InlineData(2,  TcpState.Listening)]
    [InlineData(5,  TcpState.Established)]
    [InlineData(11, TcpState.TimeWait)]
    [InlineData(12, TcpState.DeleteTcb)]
    [InlineData(13, TcpState.None)]   // out of range
    [InlineData(99, TcpState.None)]
    public void ToTcpState_maps_known_codes_and_falls_back_to_None(int code, TcpState expected)
    {
        Assert.Equal(expected, ConnectionTable.ToTcpState(code));
    }

    [Fact]
    public void BuildTcp4_extracts_every_field()
    {
        // 127.0.0.1 in network order is bytes [0x7F, 0x00, 0x00, 0x01] which
        // reads as little-endian uint 0x0100007F.
        var row = new MIB_TCPROW_OWNER_PID
        {
            dwState      = 5,            // ESTABLISHED
            dwLocalAddr  = 0x0100007F,   // 127.0.0.1
            dwLocalPort  = 0x5000,       // 80
            dwRemoteAddr = 0x0101A8C0,   // 192.168.1.1
            dwRemotePort = 0xBB01,       // 443
            dwOwningPid  = 1234,
        };

        var conn = ConnectionTable.BuildTcp4(row);

        Assert.Equal(1234, conn.Pid);
        Assert.Equal(Protocol.Tcp, conn.Protocol);
        Assert.Equal(AddressFamily.IPv4, conn.Family);
        Assert.Equal(IPAddress.Parse("127.0.0.1"), conn.Local);
        Assert.Equal(80, conn.LocalPort);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), conn.Remote);
        Assert.Equal(443, conn.RemotePort);
        Assert.Equal(TcpState.Established, conn.State);
    }

    [Fact]
    public void BuildTcp6_decodes_full_ipv6_addresses()
    {
        var local = default(InlineByte16);
        // 2001:db8::1
        local[0] = 0x20; local[1] = 0x01;
        local[2] = 0x0D; local[3] = 0xB8;
        local[15] = 0x01;

        var remote = default(InlineByte16);
        // ::1 (loopback)
        remote[15] = 0x01;

        var row = new MIB_TCP6ROW_OWNER_PID
        {
            ucLocalAddr  = local,
            dwLocalPort  = 0x5000,     // 80
            ucRemoteAddr = remote,
            dwRemotePort = 0xBB01,     // 443
            dwState      = 2,          // LISTENING
            dwOwningPid  = 4321,
        };

        var conn = ConnectionTable.BuildTcp6(row);

        Assert.Equal(4321, conn.Pid);
        Assert.Equal(Protocol.Tcp, conn.Protocol);
        Assert.Equal(AddressFamily.IPv6, conn.Family);
        Assert.Equal(IPAddress.Parse("2001:db8::1"), conn.Local);
        Assert.Equal(80, conn.LocalPort);
        Assert.Equal(IPAddress.Parse("::1"), conn.Remote);
        Assert.Equal(443, conn.RemotePort);
        Assert.Equal(TcpState.Listening, conn.State);
    }

    [Fact]
    public void BuildUdp4_has_no_remote()
    {
        var row = new MIB_UDPROW_OWNER_PID
        {
            dwLocalAddr = 0x00000000,   // 0.0.0.0
            dwLocalPort = 0xE914,       // 5353 (mDNS)
            dwOwningPid = 99,
        };

        var conn = ConnectionTable.BuildUdp4(row);

        Assert.Equal(99, conn.Pid);
        Assert.Equal(Protocol.Udp, conn.Protocol);
        Assert.Equal(AddressFamily.IPv4, conn.Family);
        Assert.Equal(IPAddress.Any, conn.Local);
        Assert.Equal(5353, conn.LocalPort);
        Assert.Null(conn.Remote);
        Assert.Equal(0, conn.RemotePort);
        Assert.Equal(TcpState.None, conn.State);
    }

    [Fact]
    public void BuildTcp6_preserves_scope_ids_for_link_local_addresses()
    {
        // fe80::1 — link-local address. Without a scope ID the framework
        // can't distinguish two such sockets bound on different interfaces,
        // and they collapse to a single endpoint in the UI.
        var addr = default(InlineByte16);
        addr[0] = 0xFE; addr[1] = 0x80;
        addr[15] = 0x01;

        var row = new MIB_TCP6ROW_OWNER_PID
        {
            ucLocalAddr     = addr,
            dwLocalScopeId  = 17,
            dwLocalPort     = 0x5000,
            ucRemoteAddr    = addr,
            dwRemoteScopeId = 17,
            dwRemotePort    = 0xBB01,
            dwState         = 5,
            dwOwningPid     = 1,
        };

        var conn = ConnectionTable.BuildTcp6(row);

        Assert.Equal(17L, conn.Local.ScopeId);
        Assert.NotNull(conn.Remote);
        Assert.Equal(17L, conn.Remote!.ScopeId);
    }

    [Fact]
    public void BuildUdp6_preserves_scope_id()
    {
        var addr = default(InlineByte16);
        addr[0] = 0xFE; addr[1] = 0x80;
        addr[15] = 0x05;

        var row = new MIB_UDP6ROW_OWNER_PID
        {
            ucLocalAddr    = addr,
            dwLocalScopeId = 9,
            dwLocalPort    = 0xE914,
            dwOwningPid    = 42,
        };

        var conn = ConnectionTable.BuildUdp6(row);

        Assert.Equal(9L, conn.Local.ScopeId);
    }

    [Fact]
    public void BuildUdp6_has_no_remote()
    {
        var addr = default(InlineByte16);
        // All zeros = "::"

        var row = new MIB_UDP6ROW_OWNER_PID
        {
            ucLocalAddr = addr,
            dwLocalPort = 0xE914,       // 5353
            dwOwningPid = 77,
        };

        var conn = ConnectionTable.BuildUdp6(row);

        Assert.Equal(77, conn.Pid);
        Assert.Equal(Protocol.Udp, conn.Protocol);
        Assert.Equal(AddressFamily.IPv6, conn.Family);
        Assert.Equal(IPAddress.IPv6Any, conn.Local);
        Assert.Equal(5353, conn.LocalPort);
        Assert.Null(conn.Remote);
        Assert.Equal(0, conn.RemotePort);
        Assert.Equal(TcpState.None, conn.State);
    }
}
