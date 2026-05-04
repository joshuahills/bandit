using Bandit.Data.Collectors;
using Bandit.UI.Rendering;
using Hex1b;
using Hex1b.Widgets;

namespace Bandit.UI.Screens;

public sealed class OverviewScreen(AppState state, SystemNetworkCollector collector) : IScreen
{
    public int Index => 0;
    public string Title => "Overview";

    public Hex1bWidget Build(RootContext ctx)
    {
        var samples = collector.Samples.TailN(state.TimescaleSeconds);

        return ctx.Surface(s => [BandwidthChart.BuildLayer(s, samples)])
            .RedrawAfter(1000)
            .Fill();
    }
}
