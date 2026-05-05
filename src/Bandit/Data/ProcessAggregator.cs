using Bandit.Data.Models;

namespace Bandit.Data;

public static class ProcessAggregator
{
    /// <summary>
    /// Cap on per-PID history points. Long timescales (e.g. 24h) are
    /// downsampled to this many buckets so the per-row history arrays don't
    /// allocate hundreds of MB across many processes every second.
    /// </summary>
    public const int HistoryCap = 64;

    /// <summary>
    /// Combine a window of per-second per-process snapshots into a single row
    /// per PID, with totals summed across the window, live values from the
    /// newest snapshot, and per-PID history arrays for the sparkline column.
    ///
    /// History downsampling: if the window is longer than <see cref="HistoryCap"/>
    /// seconds, samples are accumulated into stride-sized buckets so the
    /// history array stays bounded.
    /// </summary>
    public static ProcessNetworkRow[] Aggregate(ProcessNetworkInfo[][] window)
    {
        if (window.Length == 0) return [];

        int stride = Math.Max(1, (window.Length + HistoryCap - 1) / HistoryCap);
        int historyLen = (window.Length + stride - 1) / stride;

        var data = new Dictionary<int, Accum>();

        for (int i = 0; i < window.Length; i++)
        {
            int historyIdx = i / stride;
            foreach (var p in window[i])
            {
                if (!data.TryGetValue(p.Pid, out var accum))
                {
                    accum = new Accum
                    {
                        Name = p.Name,
                        HistoryIn = new long[historyLen],
                        HistoryOut = new long[historyLen],
                    };
                    data[p.Pid] = accum;
                }

                // Refresh name in case it resolved later in the window.
                accum.Name = p.Name;
                accum.TotalIn += p.BytesIn;
                accum.TotalOut += p.BytesOut;
                accum.HistoryIn[historyIdx] += p.BytesIn;
                accum.HistoryOut[historyIdx] += p.BytesOut;
            }
        }

        // Live = newest snapshot's per-second value. Lookup separately so
        // PIDs that appeared earlier but not in the most recent snapshot
        // still show 0 for live (correct behaviour).
        var live = new Dictionary<int, (long In, long Out)>();
        foreach (var p in window[^1])
            live[p.Pid] = (p.BytesIn, p.BytesOut);

        var result = new ProcessNetworkRow[data.Count];
        int j = 0;
        foreach (var (pid, accum) in data)
        {
            live.TryGetValue(pid, out var l);
            result[j++] = new ProcessNetworkRow(
                pid, accum.Name,
                l.In, l.Out,
                accum.TotalIn, accum.TotalOut,
                accum.HistoryIn, accum.HistoryOut);
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

    private sealed class Accum
    {
        public required string Name { get; set; }
        public required long[] HistoryIn { get; init; }
        public required long[] HistoryOut { get; init; }
        public long TotalIn;
        public long TotalOut;
    }
}
