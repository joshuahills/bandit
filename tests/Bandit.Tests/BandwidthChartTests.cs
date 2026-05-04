using Bandit.UI.Rendering;
using Xunit;

namespace Bandit.Tests;

public class BandwidthChartTests
{
    [Theory]
    [InlineData(0,             "0B")]
    [InlineData(1,             "1B")]
    [InlineData(999,           "999B")]
    [InlineData(1_000,         "1.0K")]
    [InlineData(1_500,         "1.5K")]
    [InlineData(999_999,       "1000.0K")]
    [InlineData(1_000_000,     "1.0M")]
    [InlineData(2_500_000,     "2.5M")]
    [InlineData(1_000_000_000, "1.0G")]
    [InlineData(7_300_000_000, "7.3G")]
    public void FormatBytesPerSec_picks_appropriate_unit(double bytes, string expected)
    {
        Assert.Equal(expected, BandwidthChart.FormatBytesPerSec(bytes));
    }
}
