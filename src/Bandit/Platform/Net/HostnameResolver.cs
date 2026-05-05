using System.Collections.Concurrent;
using System.Net;

namespace Bandit.Platform.Net;

/// <summary>
/// Async-cached reverse-DNS lookup. <see cref="TryGet"/> never blocks; if the
/// hostname isn't cached yet, call <see cref="Lookup"/> to fire off a
/// background resolve and let a subsequent <see cref="TryGet"/> pick up the
/// result. Negative results (NXDOMAIN, timeout, error) are cached as empty
/// strings to suppress retries within the session.
/// </summary>
public sealed class HostnameResolver
{
    private readonly ConcurrentDictionary<IPAddress, string> _cache = new();
    private readonly ConcurrentDictionary<IPAddress, byte> _pending = new();

    public string? TryGet(IPAddress ip)
    {
        if (IsPrivate(ip)) return null;
        if (_cache.TryGetValue(ip, out var name) && !string.IsNullOrEmpty(name))
            return name;
        return null;
    }

    public void Lookup(IPAddress ip)
    {
        if (IsPrivate(ip)) return;
        if (_cache.ContainsKey(ip)) return;
        if (!_pending.TryAdd(ip, 0)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                // Dns.GetHostEntryAsync(IPAddress) has no CancellationToken
                // overload; use the string variant which does.
                var entry = await Dns.GetHostEntryAsync(ip.ToString(), cts.Token).ConfigureAwait(false);
                _cache[ip] = entry.HostName ?? "";
            }
            catch
            {
                // Negative cache: don't retry on failures within the session.
                _cache[ip] = "";
            }
            finally
            {
                _pending.TryRemove(ip, out _);
            }
        });
    }

    /// <summary>
    /// Returns true for addresses we don't want to send to a resolver:
    /// loopback, RFC1918 private ranges, link-local (APIPA), 0.0.0.0, and
    /// IPv6 link-local / site-local / unspecified.
    /// </summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) return true;                                     // 0.0.0.0/8
            if (b[0] == 10) return true;                                    // 10.0.0.0/8
            if (b[0] == 169 && b[1] == 254) return true;                    // 169.254.0.0/16
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;       // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;                    // 192.168.0.0/16
            if (b[0] >= 224) return true;                                   // multicast / reserved
            return false;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6Multicast
                || ip.Equals(IPAddress.IPv6Any);
        }

        return false;
    }
}
