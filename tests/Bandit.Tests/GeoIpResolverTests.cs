using System.Net;
using Bandit.Platform.Net;
using Xunit;

namespace Bandit.Tests;

public class GeoIpResolverTests
{
    private static string MmdbPath => Path.Combine(AppContext.BaseDirectory, "Assets", "GeoLite2", "GeoLite2-Country.mmdb");

    [Fact]
    public void Database_is_present_next_to_test_assembly()
    {
        // If this fails the bundled .mmdb isn't being copied to the test
        // output directory — every other test below would also fail with a
        // less obvious message, so call it out directly.
        Assert.True(File.Exists(MmdbPath), $"Expected GeoLite2-Country.mmdb at: {MmdbPath}");
    }

    [Fact]
    public void Resolver_loads_the_bundled_database()
    {
        var resolver = new GeoIpResolver(MmdbPath);
        // Force lazy load via a lookup.
        _ = resolver.CountryCode(IPAddress.Parse("8.8.8.8"));
        Assert.True(resolver.IsAvailable);
    }

    [Theory]
    [InlineData("8.8.8.8",        "US")]   // Google DNS — stable US registration
    [InlineData("142.250.180.46", "US")]   // Google
    public void CountryCode_resolves_known_public_addresses(string addr, string expected)
    {
        var resolver = new GeoIpResolver(MmdbPath);
        var code = resolver.CountryCode(IPAddress.Parse(addr));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("1.1.1.1")]              // Cloudflare anycast — country varies/may be absent
    [InlineData("2606:4700:4700::1111")] // Cloudflare DNS v6 — exercises v6 walk path
    public void CountryCode_returns_short_code_or_null_for_anycast(string addr)
    {
        // We don't pin a specific country for anycast addresses (GeoLite2-Country
        // sometimes omits them). Just assert the lookup doesn't crash and any
        // result is a well-formed ISO code.
        var resolver = new GeoIpResolver(MmdbPath);
        var code = resolver.CountryCode(IPAddress.Parse(addr));
        Assert.True(code is null || code.Length == 2, $"Unexpected country code: {code}");
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public void CountryCode_returns_null_for_private_addresses(string addr)
    {
        var resolver = new GeoIpResolver(MmdbPath);
        Assert.Null(resolver.CountryCode(IPAddress.Parse(addr)));
    }

    [Fact]
    public void CountryCode_caches_repeated_lookups()
    {
        var resolver = new GeoIpResolver(MmdbPath);
        var ip = IPAddress.Parse("8.8.8.8");
        var first  = resolver.CountryCode(ip);
        var second = resolver.CountryCode(ip);
        Assert.Equal(first, second);
        Assert.Equal("US", first);
    }

    [Theory]
    [InlineData("US", "\U0001F1FA\U0001F1F8")]
    [InlineData("GB", "\U0001F1EC\U0001F1E7")]
    [InlineData("us", "\U0001F1FA\U0001F1F8")]   // accepts lowercase
    public void CountryFlag_pairs_regional_indicators(string code, string expected)
    {
        Assert.Equal(expected, GeoIpResolver.CountryFlag(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("U")]      // too short
    [InlineData("USA")]    // too long
    [InlineData("U1")]     // non-letter
    [InlineData("--")]
    public void CountryFlag_rejects_malformed_input(string? code)
    {
        Assert.Null(GeoIpResolver.CountryFlag(code));
    }

    [Fact]
    public void Missing_database_returns_null_without_throwing()
    {
        var resolver = new GeoIpResolver(Path.Combine(Path.GetTempPath(), "definitely-not-here.mmdb"));
        Assert.Null(resolver.CountryCode(IPAddress.Parse("8.8.8.8")));
        Assert.False(resolver.IsAvailable);
    }
}
