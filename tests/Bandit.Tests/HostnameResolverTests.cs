using System.Net;
using Bandit.Platform.Net;
using Xunit;

namespace Bandit.Tests;

public class HostnameResolverTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    public void IsPrivate_recognises_addresses_that_should_not_be_resolved(string addr)
    {
        Assert.True(HostnameResolver.IsPrivate(IPAddress.Parse(addr)),
            $"Expected {addr} to be classified as private/non-routable");
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("142.250.180.46")]
    [InlineData("2606:4700:4700::1111")]   // Cloudflare DNS v6
    public void IsPrivate_returns_false_for_routable_public_addresses(string addr)
    {
        Assert.False(HostnameResolver.IsPrivate(IPAddress.Parse(addr)),
            $"Expected {addr} to be classified as routable / public");
    }

    [Fact]
    public void TryGet_returns_null_for_private_addresses_without_cache()
    {
        var resolver = new HostnameResolver();

        Assert.Null(resolver.TryGet(IPAddress.Parse("192.168.1.1")));
        Assert.Null(resolver.TryGet(IPAddress.Parse("127.0.0.1")));
    }

    [Fact]
    public void TryGet_returns_null_for_uncached_public_addresses()
    {
        // No lookup fired, no cache entry — TryGet must be non-blocking and
        // return null so the UI falls back to the raw IP.
        var resolver = new HostnameResolver();
        Assert.Null(resolver.TryGet(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void Lookup_does_not_throw_for_private_addresses()
    {
        var resolver = new HostnameResolver();

        // Should be a no-op; never reach the DNS layer for private IPs.
        resolver.Lookup(IPAddress.Parse("192.168.1.1"));
        resolver.Lookup(IPAddress.Parse("127.0.0.1"));
        Assert.Null(resolver.TryGet(IPAddress.Parse("192.168.1.1")));
    }
}
