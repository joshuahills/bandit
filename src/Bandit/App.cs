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
            BindScreenSwitch(bindings, Hex1bKey.D1, 0);
            BindScreenSwitch(bindings, Hex1bKey.D2, 1);
            BindScreenSwitch(bindings, Hex1bKey.D3, 2);
            BindScreenSwitch(bindings, Hex1bKey.D4, 3);
            BindScreenSwitch(bindings, Hex1bKey.D5, 4);
            BindScreenSwitch(bindings, Hex1bKey.D6, 5);
            BindScreenSwitch(bindings, Hex1bKey.D7, 6);
            BindScreenSwitch(bindings, Hex1bKey.D8, 7);
            BindScreenSwitch(bindings, Hex1bKey.D9, 8);

            bindings.Key(Hex1bKey.Oem4).Global().Action(_ => state.CycleTimescaleDown(), "Timescale -");
            bindings.Key(Hex1bKey.Oem6).Global().Action(_ => state.CycleTimescaleUp(), "Timescale +");
            bindings.Key(Hex1bKey.Q).Global().Action(c => c.RequestStop(), "Quit");
            bindings.Ctrl().Key(Hex1bKey.C).Global().Action(c => c.RequestStop(), "Quit");
        });
    }

    private void BindScreenSwitch(InputBindingsBuilder bindings, Hex1bKey key, int index)
    {
        bindings.Key(key).Global().Action(_ =>
        {
            if (index < _screens.Length) state.ActiveScreenIndex = index;
        }, $"Screen {index + 1}");
    }

    private Hex1bWidget BuildHeader(WidgetContext<VStackWidget> b)
    {
        return b.HStack(h =>
        [
            h.Text(" ◈ BANDIT  ", Theme.Accent),
            .. _screens.Select(screen =>
            {
                bool active = screen.Index == state.ActiveScreenIndex;
                string label = $" [{screen.Index + 1}] {screen.Title} ";
                var color = active ? Theme.ActiveTab : Theme.InactiveTab;
                int captured = screen.Index;
                return (Hex1bWidget)h.Interactable(ic => ic.Text(label, color))
                    .OnClick(_ => state.ActiveScreenIndex = captured);
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
            h.Text($" [{state.TimescaleLabel}] timescale: [ ]   q:quit{elev}", Theme.StatusFg),
            h.Text(rates, Theme.Accent),
        ]);
    }
}
