using Bandit.Data.Collectors;
using Hex1b;
using Hex1b.Widgets;

namespace Bandit.UI.Screens;

public sealed class ProcessScreen(AppState state, ProcessNetworkCollector collector) : IScreen
{
    public int Index => 1;
    public string Title => "Processes";

    public Hex1bWidget Build(RootContext ctx)
    {
        _ = state;

        if (!collector.IsAvailable)
        {
            return ctx.VStack(b =>
            [
                b.Text(""),
                b.Text("  Per-process network monitoring requires administrator privileges.", Theme.Warning),
                b.Text(""),
                b.Text("  Re-launch Bandit from an elevated terminal:", Theme.StatusFg),
                b.Text(""),
                b.Text("    Run as Administrator → bandit.exe", Theme.Dim),
            ]).Fill();
        }

        return ctx.VStack(b =>
        [
            b.Text(""),
            b.Text("  Per-process data collection coming soon.", Theme.StatusFg),
        ]).Fill();
    }
}
