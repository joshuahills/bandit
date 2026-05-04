using Xunit;

namespace Bandit.Tests;

public class AppStateTests
{
    [Fact]
    public void Default_timescale_is_one_minute()
    {
        var state = new AppState();

        Assert.Equal("1m", state.TimescaleLabel);
        Assert.Equal(60, state.TimescaleSeconds);
    }

    [Fact]
    public void CycleTimescaleUp_advances_to_next_option()
    {
        var state = new AppState { TimescaleIndex = 0 };

        state.CycleTimescaleUp();

        Assert.Equal(1, state.TimescaleIndex);
    }

    [Fact]
    public void CycleTimescaleUp_wraps_around_at_end()
    {
        var state = new AppState { TimescaleIndex = AppState.TimescaleLabels.Length - 1 };

        state.CycleTimescaleUp();

        Assert.Equal(0, state.TimescaleIndex);
    }

    [Fact]
    public void CycleTimescaleDown_wraps_around_at_zero()
    {
        var state = new AppState { TimescaleIndex = 0 };

        state.CycleTimescaleDown();

        Assert.Equal(AppState.TimescaleLabels.Length - 1, state.TimescaleIndex);
    }

    [Fact]
    public void Timescale_seconds_and_label_arrays_are_aligned()
    {
        Assert.Equal(AppState.Timescales.Length, AppState.TimescaleLabels.Length);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(6, 86400)]
    public void TimescaleSeconds_reflects_TimescaleIndex(int index, int expectedSeconds)
    {
        var state = new AppState { TimescaleIndex = index };

        Assert.Equal(expectedSeconds, state.TimescaleSeconds);
    }
}
