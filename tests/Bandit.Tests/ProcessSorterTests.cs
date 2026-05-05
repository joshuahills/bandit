using Bandit.Data;
using Bandit.Data.Models;
using Xunit;

namespace Bandit.Tests;

public class ProcessSorterTests
{
    private static ProcessNetworkRow Row(int pid, string name, long liveIn, long liveOut, long totalIn, long totalOut)
        => new(pid, name, liveIn, liveOut, totalIn, totalOut, [], []);

    [Fact]
    public void Sort_by_TotalCombined_descending_ranks_biggest_first()
    {
        ProcessNetworkRow[] rows =
        [
            Row(1, "small", 0, 0, 10, 10),
            Row(2, "big",   0, 0, 500, 500),
            Row(3, "med",   0, 0, 100, 100),
        ];

        var sorted = ProcessSorter.Sort(rows, ProcessSortKey.TotalCombined, descending: true).ToArray();

        Assert.Equal([2, 3, 1], sorted.Select(r => r.Pid));
    }

    [Fact]
    public void Sort_by_Pid_ascending_orders_numerically()
    {
        ProcessNetworkRow[] rows =
        [
            Row(30, "c", 0, 0, 0, 0),
            Row(10, "a", 0, 0, 0, 0),
            Row(20, "b", 0, 0, 0, 0),
        ];

        var sorted = ProcessSorter.Sort(rows, ProcessSortKey.Pid, descending: false).ToArray();

        Assert.Equal([10, 20, 30], sorted.Select(r => r.Pid));
    }

    [Fact]
    public void Sort_by_Name_descending_uses_string_order()
    {
        ProcessNetworkRow[] rows =
        [
            Row(1, "alpha", 0, 0, 0, 0),
            Row(2, "gamma", 0, 0, 0, 0),
            Row(3, "beta",  0, 0, 0, 0),
        ];

        var sorted = ProcessSorter.Sort(rows, ProcessSortKey.Name, descending: true).ToArray();

        Assert.Equal(["gamma", "beta", "alpha"], sorted.Select(r => r.Name));
    }

    [Theory]
    [InlineData(ProcessSortKey.LiveUp,    new[] { 3, 1, 2 })]
    [InlineData(ProcessSortKey.LiveDown,  new[] { 2, 1, 3 })]
    [InlineData(ProcessSortKey.TotalUp,   new[] { 1, 2, 3 })]
    [InlineData(ProcessSortKey.TotalDown, new[] { 3, 2, 1 })]
    public void Sort_descending_by_each_numeric_key(ProcessSortKey key, int[] expectedOrder)
    {
        ProcessNetworkRow[] rows =
        [
            Row(1, "a", liveIn: 50,  liveOut: 200, totalIn: 100, totalOut: 1000),
            Row(2, "b", liveIn: 100, liveOut: 100, totalIn: 200, totalOut: 500),
            Row(3, "c", liveIn: 25,  liveOut: 300, totalIn: 300, totalOut: 200),
        ];

        var sorted = ProcessSorter.Sort(rows, key, descending: true).ToArray();

        Assert.Equal(expectedOrder, sorted.Select(r => r.Pid));
    }

    [Fact]
    public void Sort_ascending_reverses_descending_for_same_key()
    {
        ProcessNetworkRow[] rows =
        [
            Row(1, "a", 0, 0, 0, 100),
            Row(2, "b", 0, 0, 0, 200),
            Row(3, "c", 0, 0, 0, 50),
        ];

        var asc = ProcessSorter.Sort(rows, ProcessSortKey.TotalUp, descending: false).Select(r => r.Pid).ToArray();
        var desc = ProcessSorter.Sort(rows, ProcessSortKey.TotalUp, descending: true).Select(r => r.Pid).ToArray();

        Assert.Equal(asc.Reverse().ToArray(), desc);
    }

    [Fact]
    public void Sort_on_empty_input_returns_empty()
    {
        Assert.Empty(ProcessSorter.Sort([], ProcessSortKey.TotalCombined, descending: true));
    }
}
