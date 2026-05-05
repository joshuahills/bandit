using System.Collections.Concurrent;
using System.Net;

namespace Bandit.Platform.Net;

/// <summary>
/// Looks up the ISO 3166-1 alpha-2 country code for a public IP using a
/// bundled MaxMind GeoLite2-Country database. Loads the database lazily on
/// first lookup and caches the result so the file isn't kept open longer
/// than the load. Per-IP results are cached for the lifetime of the process.
///
/// Returns null for private/loopback/multicast addresses (delegated to
/// <see cref="HostnameResolver.IsPrivate"/>) and for any address with no
/// match in the database. Failures are silent — the UI just renders nothing
/// in the country column.
/// </summary>
public sealed class GeoIpResolver
{
    private const string MmdbRelativePath = "Assets/GeoLite2/GeoLite2-Country.mmdb";

    private readonly ConcurrentDictionary<IPAddress, string?> _cache = new();
    private readonly Lazy<MmdbReader?> _reader;

    public GeoIpResolver()
        : this(Path.Combine(AppContext.BaseDirectory, MmdbRelativePath))
    {
    }

    internal GeoIpResolver(string mmdbPath)
    {
        _reader = new Lazy<MmdbReader?>(() =>
        {
            try
            {
                if (!File.Exists(mmdbPath)) return null;
                return new MmdbReader(File.ReadAllBytes(mmdbPath));
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>True if the bundled database loaded successfully.</summary>
    public bool IsAvailable => _reader.Value is not null;

    /// <summary>
    /// 2-character ISO country code, or null if private/unknown/db-missing.
    /// Synchronous and cheap once the db is loaded — a tree walk plus a few
    /// pointer derefs.
    /// </summary>
    public string? CountryCode(IPAddress ip)
    {
        if (HostnameResolver.IsPrivate(ip)) return null;
        if (_cache.TryGetValue(ip, out var cached)) return cached;

        var reader = _reader.Value;
        var code = reader?.FindString(ip, "country", "iso_code");
        _cache[ip] = code;
        return code;
    }

    /// <summary>
    /// Converts an ISO 3166-1 alpha-2 country code into its flag emoji by
    /// pairing the two Regional Indicator Symbols (U+1F1E6..U+1F1FF). Any
    /// code that isn't exactly two A–Z letters returns null — the UI then
    /// falls back to rendering the raw code or nothing.
    /// </summary>
    public static string? CountryFlag(string? isoCode)
    {
        if (isoCode is null || isoCode.Length != 2) return null;
        char a = char.ToUpperInvariant(isoCode[0]);
        char b = char.ToUpperInvariant(isoCode[1]);
        if (a is < 'A' or > 'Z' || b is < 'A' or > 'Z') return null;

        const int RegionalIndicatorBase = 0x1F1E6;
        return string.Concat(
            char.ConvertFromUtf32(RegionalIndicatorBase + (a - 'A')),
            char.ConvertFromUtf32(RegionalIndicatorBase + (b - 'A')));
    }
}
