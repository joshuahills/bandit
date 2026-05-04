using Bandit.Data.Models;

namespace Bandit.Data;

public enum ProcessSortKey
{
    TotalCombined,
    Pid,
    Name,
    LiveUp,
    LiveDown,
    TotalUp,
    TotalDown,
}

public static class ProcessSorter
{
    public static IEnumerable<ProcessNetworkRow> Sort(
        IEnumerable<ProcessNetworkRow> rows,
        ProcessSortKey key,
        bool descending) => key switch
    {
        ProcessSortKey.Pid       => descending ? rows.OrderByDescending(r => r.Pid)             : rows.OrderBy(r => r.Pid),
        ProcessSortKey.Name      => descending ? rows.OrderByDescending(r => r.Name)            : rows.OrderBy(r => r.Name),
        ProcessSortKey.LiveUp    => descending ? rows.OrderByDescending(r => r.LiveBytesOut)    : rows.OrderBy(r => r.LiveBytesOut),
        ProcessSortKey.LiveDown  => descending ? rows.OrderByDescending(r => r.LiveBytesIn)     : rows.OrderBy(r => r.LiveBytesIn),
        ProcessSortKey.TotalUp   => descending ? rows.OrderByDescending(r => r.TotalBytesOut)   : rows.OrderBy(r => r.TotalBytesOut),
        ProcessSortKey.TotalDown => descending ? rows.OrderByDescending(r => r.TotalBytesIn)    : rows.OrderBy(r => r.TotalBytesIn),
        _                        => descending ? rows.OrderByDescending(r => r.TotalBytesIn + r.TotalBytesOut)
                                               : rows.OrderBy(r => r.TotalBytesIn + r.TotalBytesOut),
    };
}
