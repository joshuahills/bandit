using Bandit.Data.Collectors;
using Bandit.UI;
using Bandit.UI.Rendering;
using Bandit.UI.Screens;
using Hex1b;
using Hex1b.Input;
using Hex1b.Widgets;

namespace Bandit;

public sealed class App(AppState state)
{
    private readonly SystemNetworkCollector _system = new();
    private readonly ProcessNetworkCollector _process = new(state.IsElevated);
    private IScreen[] _screens = [];

    public async Task RunAsync()
    {
        _screens =
        [
            new OverviewScreen(state, _system),
            new ProcessScreen(state, _process),
        ];

        using var cts = new CancellationTokenSource();

        var collectorTask = Task.WhenAll(
            _system.StartAsync(cts.Token),
            _process.StartAsync(cts.Token));

        using var app = new Hex1bApp(BuildRoot);
        try
        {
            await app.RunAsync(cts.Token);
        }
        finally
        {
            cts.Cancel();
            await collectorTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private Hex1bWidget BuildRoot(RootContext ctx)
    {
        var activeScreen = _screens[state.ActiveScreenIndex];

        return ctx.VStack(b =>
        [
            BuildHeader(b),
            activeScreen.Build(ctx).Fill(),
            BuildStatusBar(b),
        ])
        .Fill()
        .WithInputBindings(bindings =>
        {
            bindings.Character(t => t is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9")
                .Action(t =>
                {
                    int idx = int.Parse(t) - 1;
                    if (idx < _screens.Length) state.ActiveScreenIndex = idx;
                }, "Switch screen");

            bindings.Character(t => t == "[").Action(_ => state.CycleTimescaleDown(), "Timescale -");
            bindings.Character(t => t == "]").Action(_ => state.CycleTimescaleUp(), "Timescale +");
            bindings.Key(Hex1bKey.Q).Action(c => c.RequestStop(), "Quit");
            bindings.Ctrl().Key(Hex1bKey.C).Action(c => c.RequestStop(), "Quit");
        });
    }

    private Hex1bWidget BuildHeader(WidgetContext<VStackWidget> b)
    {
        return b.HStack(h =>
        [
            h.Text(" ◈ BANDIT  ", Theme.Accent),
            .. _screens.Select(s =>
            {
                bool active = s.Index == state.ActiveScreenIndex;
                string label = $" [{s.Index + 1}] {s.Title} ";
                return h.Text(label, active ? Theme.ActiveTab : Theme.InactiveTab);
            }),
        ]);
    }

    private Hex1bWidget BuildStatusBar(WidgetContext<VStackWidget> b)
    {
        var latest = _system.Samples.Latest();
        string rates = latest is { } s
            ? $" ↑ {BandwidthChart.FormatBytesPerSec(s.BytesOut)}/s  ↓ {BandwidthChart.FormatBytesPerSec(s.BytesIn)}/s "
            : " ↑ ---  ↓ --- ";

        string elev = state.IsElevated ? "" : "  [!] Not elevated";

        return b.HStack(h =>
        [
            h.Text($" [{state.TimescaleLabel}] [/] timescale  q:quit{elev}", Theme.StatusFg),
            h.Text(rates, Theme.Accent),
        ]);
    }
}
