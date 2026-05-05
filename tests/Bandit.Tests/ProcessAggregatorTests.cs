using Bandit.Data;
using Bandit.Data.Models;
using Xunit;

namespace Bandit.Tests;

public class ProcessAggregatorTests
{
    [Fact]
    public void Aggregate_returns_empty_for_empty_window()
    {
        Assert.Empty(ProcessAggregator.Aggregate([]));
    }

    [Fact]
    public void Aggregate_with_single_snapshot_makes_live_equal_total()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(42, "chrome", BytesIn: 100, BytesOut: 200)],
        ];

        var rows = ProcessAggregator.Aggregate(window);

        var row = Assert.Single(rows);
        Assert.Equal(42, row.Pid);
        Assert.Equal("chrome", row.Name);
        Assert.Equal(100, row.LiveBytesIn);
        Assert.Equal(200, row.LiveBytesOut);
        Assert.Equal(100, row.TotalBytesIn);
        Assert.Equal(200, row.TotalBytesOut);
    }

    [Fact]
    public void Aggregate_sums_totals_across_window_and_takes_live_from_newest()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 100, 200)],
            [new ProcessNetworkInfo(1, "chrome", 50,  150)],
            [new ProcessNetworkInfo(1, "chrome", 25,  75)],
        ];

        var row = Assert.Single(ProcessAggregator.Aggregate(window));

        Assert.Equal(175, row.TotalBytesIn);   // 100 + 50 + 25
        Assert.Equal(425, row.TotalBytesOut);  // 200 + 150 + 75
        Assert.Equal(25, row.LiveBytesIn);     // last
        Assert.Equal(75, row.LiveBytesOut);    // last
    }

    [Fact]
    public void Aggregate_surfaces_pids_present_in_any_snapshot()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 100, 200)],
            [new ProcessNetworkInfo(2, "firefox", 50, 60)],
        ];

        var rows = ProcessAggregator.Aggregate(window);

        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, r => r.Pid == 1);
        Assert.Contains(rows, r => r.Pid == 2);
    }

    [Fact]
    public void Aggregate_zeroes_live_for_pids_only_in_older_snapshots()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 100, 200)],
            [new ProcessNetworkInfo(2, "firefox", 50,  60)],   // chrome absent in last
        ];

        var rows = ProcessAggregator.Aggregate(window);
        var chrome = Assert.Single(rows, r => r.Pid == 1);

        Assert.Equal(100, chrome.TotalBytesIn);   // counted in totals
        Assert.Equal(200, chrome.TotalBytesOut);
        Assert.Equal(0, chrome.LiveBytesIn);      // but live is from last snapshot only
        Assert.Equal(0, chrome.LiveBytesOut);
    }

    [Fact]
    public void Aggregate_records_per_PID_history_per_second()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 100, 200)],
            [new ProcessNetworkInfo(1, "chrome", 50,  150)],
            [new ProcessNetworkInfo(1, "chrome", 25,  75)],
        ];

        var row = Assert.Single(ProcessAggregator.Aggregate(window));

        Assert.Equal(new long[] { 100, 50, 25 }, row.HistoryIn);
        Assert.Equal(new long[] { 200, 150, 75 }, row.HistoryOut);
    }

    [Fact]
    public void Aggregate_zeroes_history_buckets_where_pid_absent()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 100, 200)],
            [new ProcessNetworkInfo(2, "firefox", 50, 60)],          // no chrome
            [new ProcessNetworkInfo(1, "chrome", 25, 75)],
        ];

        var chrome = Assert.Single(ProcessAggregator.Aggregate(window), r => r.Pid == 1);

        Assert.Equal(new long[] { 100, 0, 25 }, chrome.HistoryIn);
        Assert.Equal(new long[] { 200, 0, 75 }, chrome.HistoryOut);
    }

    [Fact]
    public void Aggregate_caps_history_length_for_long_windows()
    {
        // 1000 snapshots for one PID — well past the cap.
        var window = new ProcessNetworkInfo[1000][];
        for (int i = 0; i < window.Length; i++)
            window[i] = [new ProcessNetworkInfo(1, "chrome", 1, 2)];

        var row = Assert.Single(ProcessAggregator.Aggregate(window));

        Assert.True(row.HistoryIn.Length <= ProcessAggregator.HistoryCap,
            $"HistoryIn length {row.HistoryIn.Length} exceeds cap {ProcessAggregator.HistoryCap}");
        Assert.Equal(row.HistoryIn.Length, row.HistoryOut.Length);
    }

    [Fact]
    public void Aggregate_buckets_accumulate_when_downsampling()
    {
        // 128 snapshots × 1 byte = 128 bytes total. Cap is 64 → stride = 2.
        // Each history bucket should hold the sum of 2 snapshots = 2 bytes,
        // and the sum of buckets must equal the scalar total.
        var window = new ProcessNetworkInfo[128][];
        for (int i = 0; i < window.Length; i++)
            window[i] = [new ProcessNetworkInfo(1, "chrome", 1, 0)];

        var row = Assert.Single(ProcessAggregator.Aggregate(window));

        Assert.Equal(64, row.HistoryIn.Length);
        Assert.All(row.HistoryIn, v => Assert.Equal(2, v));
        Assert.Equal(128, row.HistoryIn.Sum());
        Assert.Equal(128, row.TotalBytesIn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindPidByName_returns_null_for_empty_fragment(string? fragment)
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 0, 0)],
        ];

        Assert.Null(ProcessAggregator.FindPidByName(window, fragment!));
    }

    [Fact]
    public void FindPidByName_matches_substring_case_insensitive()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(42, "Chrome", 0, 0)],
        ];

        Assert.Equal(42, ProcessAggregator.FindPidByName(window, "chro"));
        Assert.Equal(42, ProcessAggregator.FindPidByName(window, "CHROME"));
    }

    [Fact]
    public void FindPidByName_returns_first_match_walking_oldest_to_newest()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome.helper", 0, 0)],
            [new ProcessNetworkInfo(2, "chrome", 0, 0)],
        ];

        Assert.Equal(1, ProcessAggregator.FindPidByName(window, "chrome"));
    }

    [Fact]
    public void FindPidByName_returns_null_for_non_matching()
    {
        ProcessNetworkInfo[][] window =
        [
            [new ProcessNetworkInfo(1, "chrome", 0, 0)],
        ];

        Assert.Null(ProcessAggregator.FindPidByName(window, "nope"));
    }
}
