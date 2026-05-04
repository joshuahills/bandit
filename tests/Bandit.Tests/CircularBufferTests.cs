using Bandit.Data;
using Xunit;

namespace Bandit.Tests;

public class CircularBufferTests
{
    [Fact]
    public void New_buffer_is_empty()
    {
        var buf = new CircularBuffer<int>(8);

        Assert.Equal(0, buf.Count);
        Assert.Equal(8, buf.Capacity);
        Assert.Empty(buf.TailN(10));
        Assert.Equal(0, buf.Latest());
    }

    [Fact]
    public void Latest_returns_most_recently_added_item()
    {
        var buf = new CircularBuffer<int>(4);

        buf.Add(1);
        buf.Add(2);
        buf.Add(3);

        Assert.Equal(3, buf.Latest());
    }

    [Fact]
    public void TailN_returns_items_in_insertion_order()
    {
        var buf = new CircularBuffer<int>(8);
        for (int i = 1; i <= 5; i++) buf.Add(i);

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, buf.TailN(5));
    }

    [Fact]
    public void TailN_clamps_to_actual_count_when_more_requested_than_present()
    {
        var buf = new CircularBuffer<int>(8);
        buf.Add(7);
        buf.Add(8);

        var result = buf.TailN(100);

        Assert.Equal(new[] { 7, 8 }, result);
    }

    [Fact]
    public void TailN_returns_only_the_last_N_when_buffer_overfilled()
    {
        var buf = new CircularBuffer<int>(4);
        for (int i = 1; i <= 6; i++) buf.Add(i); // 1,2 evicted

        Assert.Equal(new[] { 3, 4, 5, 6 }, buf.TailN(4));
        Assert.Equal(new[] { 5, 6 }, buf.TailN(2));
        Assert.Equal(6, buf.Latest());
        Assert.Equal(4, buf.Count);
    }

    [Fact]
    public void TailN_handles_wraparound_across_internal_array_boundary()
    {
        var buf = new CircularBuffer<int>(3);
        buf.Add(1);
        buf.Add(2);
        buf.Add(3);
        buf.Add(4); // overwrites slot 0
        buf.Add(5); // overwrites slot 1

        Assert.Equal(new[] { 3, 4, 5 }, buf.TailN(3));
    }

    [Fact]
    public void TailN_zero_returns_empty_array()
    {
        var buf = new CircularBuffer<int>(4);
        buf.Add(1);
        buf.Add(2);

        Assert.Empty(buf.TailN(0));
    }

    [Fact]
    public void Count_never_exceeds_capacity()
    {
        var buf = new CircularBuffer<int>(3);
        for (int i = 0; i < 100; i++) buf.Add(i);

        Assert.Equal(3, buf.Count);
    }

    [Fact]
    public async Task Concurrent_Add_and_TailN_does_not_corrupt_or_throw()
    {
        const int capacity = 1024;
        var buf = new CircularBuffer<int>(capacity);
        var testCt = TestContext.Current.CancellationToken;

        // Hard-timeout linked to the test's own cancellation token so the
        // test stays responsive to xUnit shutdown / per-test timeouts.
        using var hardTimeout = CancellationTokenSource.CreateLinkedTokenSource(testCt);
        hardTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            int i = 0;
            while (!stop.IsCancellationRequested) buf.Add(i++);
        }, testCt);

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _ = buf.TailN(100);
                _ = buf.Latest();
            }
        }, testCt);

        // Wait until the writer has filled the buffer (deterministic), or bail
        // out after a generous overall timeout if something is wrong. The
        // try/finally guarantees stop.Cancel() runs even if Task.Delay throws,
        // so the worker tasks always wind down. The catch swallows our local
        // 5s timeout so the assertion below produces a clearer
        // 'expected N, got M' diagnostic — but propagates xUnit's own
        // cancellation as a normal cancel.
        try
        {
            while (buf.Count < capacity)
                await Task.Delay(10, hardTimeout.Token);
        }
        catch (OperationCanceledException) when (hardTimeout.IsCancellationRequested && !testCt.IsCancellationRequested)
        {
        }
        finally
        {
            stop.Cancel();
        }

        await Task.WhenAll(writer, reader).WaitAsync(testCt);
        Assert.Equal(capacity, buf.Count);
    }
}
