using Bandit.Data.Models;

namespace Bandit.Data;

public static class ProcessAggregator
{
    /// <summary>
    /// Combine a window of per-second per-process snapshots into a single row
    /// per PID, with totals summed across the window and live values taken
    /// from the newest snapshot.
    /// </summary>
    public static ProcessNetworkRow[] Aggregate(ProcessNetworkInfo[][] window)
    {
        if (window.Length == 0) return [];

        var totals = new Dictionary<int, (string Name, long In, long Out)>();
        foreach (var snapshot in window)
        {
            foreach (var p in snapshot)
            {
                totals.TryGetValue(p.Pid, out var t);
                totals[p.Pid] = (p.Name, t.In + p.BytesIn, t.Out + p.BytesOut);
            }
        }

        var live = new Dictionary<int, (long In, long Out)>();
        foreach (var p in window[^1])
            live[p.Pid] = (p.BytesIn, p.BytesOut);

        var result = new ProcessNetworkRow[totals.Count];
        int i = 0;
        foreach (var kv in totals)
        {
            live.TryGetValue(kv.Key, out var l);
            result[i++] = new ProcessNetworkRow(
                kv.Key, kv.Value.Name,
                l.In, l.Out,
                kv.Value.In, kv.Value.Out);
        }
        return result;
    }

    /// <summary>
    /// Find the first PID whose process name contains <paramref name="fragment"/>
    /// (case-insensitive), looking across all snapshots in the window.
    /// </summary>
    public static int? FindPidByName(ProcessNetworkInfo[][] window, string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return null;
        foreach (var snap in window)
        {
            foreach (var p in snap)
            {
                if (p.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return p.Pid;
            }
        }
        return null;
    }
}
