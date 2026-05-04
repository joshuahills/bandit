namespace Bandit.UI.Screens;

using Bandit.Data.Collectors;
using Bandit.UI.Rendering;
using Terminal.Gui.ViewBase;

public sealed class OverviewScreen(AppState state, SystemNetworkCollector collector) : IScreen
{
    public int Index => 0;
    public string Title => "Overview";

    private BandwidthChart? _chart;

    public View Build()
    {
        _chart = new BandwidthChart
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Refresh();
        return _chart;
    }

    public void Refresh()
    {
        if (_chart is null) return;
        _chart.Samples = collector.Samples.TailN(state.TimescaleSeconds);
        _chart.TimescaleSeconds = state.TimescaleSeconds;
        _chart.SetNeedsDraw();
    }
}
