using Bandit.Data.Collectors;
using Bandit.UI;
using Bandit.UI.Rendering;
using Bandit.UI.Screens;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Bandit;

public sealed class App(AppState state) : IDisposable
{
    private readonly SystemNetworkCollector _system = new();
    private readonly ProcessNetworkCollector _process = new(state.IsElevated);
    private readonly CancellationTokenSource _cts = new();

    private IScreen[] _screens = [];
    private Window? _window;
    private View? _contentHost;
    private View? _header;
    private View? _statusBar;
    private View? _activeContent;

    public async Task RunAsync()
    {
        _screens =
        [
            new OverviewScreen(state, _system),
            new ProcessScreen(state, _process),
        ];

        var collectorTask = Task.WhenAll(
            _system.StartAsync(_cts.Token),
            _process.StartAsync(_cts.Token));

        using var app = Application.Create().Init();
        try
        {
            BuildUi();
            HookKeys(app);
            ScheduleRefresh(app);
            app.Run(_window!);
        }
        finally
        {
            _cts.Cancel();
            await collectorTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private void BuildUi()
    {
        _window = new Window
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Title = string.Empty,
            BorderStyle = LineStyle.None,
        };

        _header = BuildHeader();
        _contentHost = new View
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = false,
        };
        _statusBar = BuildStatusBar();

        _window.Add(_header, _contentHost, _statusBar);
        SwapToActiveScreen();
    }

    private View BuildHeader()
    {
        var header = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
        };

        const string brandText = " ◈ BANDIT  ";
        header.Add(new ColoredLabel(brandText, Theme.AccentAttr)
        {
            X = 0, Y = 0, Width = brandText.Length,
        });

        int x = brandText.Length;
        foreach (var screen in _screens)
        {
            string label = $" [{screen.Index + 1}] {screen.Title} ";
            int captured = screen.Index;
            var attr = screen.Index == state.ActiveScreenIndex ? Theme.ActiveTabAttr : Theme.InactiveTabAttr;
            var tab = new ColoredLabel(label, attr)
            {
                X = x, Y = 0, Width = label.Length,
            };
            tab.MouseEvent += (_, m) =>
            {
                if (m.IsSingleClicked)
                {
                    SwitchScreen(captured);
                    m.Handled = true;
                }
            };
            header.Add(tab);
            x += label.Length;
        }

        return header;
    }

    private View BuildStatusBar()
    {
        var bar = new View
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
        };

        var latest = _system.Samples.Latest();
        string rates = latest is { } s
            ? $" ↑ {BandwidthChart.FormatBytesPerSec(s.BytesOut)}/s  ↓ {BandwidthChart.FormatBytesPerSec(s.BytesIn)}/s "
            : " ↑ ---  ↓ --- ";
        string elev = state.IsElevated ? "" : "  [!] Not elevated";
        string left = $" [{state.TimescaleLabel}] timescale: [ ]   q:quit{elev}";

        bar.Add(new ColoredLabel(left, Theme.StatusAttr)
        {
            X = 0, Y = 0, Width = Dim.Fill(rates.Length),
        });
        bar.Add(new ColoredLabel(rates, Theme.AccentAttr)
        {
            X = Pos.AnchorEnd(rates.Length), Y = 0, Width = rates.Length,
        });
        return bar;
    }

    private void RefreshHeader()
    {
        if (_window is null || _header is null) return;
        _window.Remove(_header);
        _header = BuildHeader();
        _window.Add(_header);
        _window.SetNeedsDraw();
    }

    private void RefreshStatusBar()
    {
        if (_window is null || _statusBar is null) return;
        _window.Remove(_statusBar);
        _statusBar = BuildStatusBar();
        _window.Add(_statusBar);
        _window.SetNeedsDraw();
    }

    private void SwapToActiveScreen()
    {
        if (_contentHost is null) return;
        if (_activeContent is not null)
        {
            _contentHost.Remove(_activeContent);
            _activeContent.Dispose();
        }
        _activeContent = _screens[state.ActiveScreenIndex].Build();
        _contentHost.Add(_activeContent);
        _contentHost.SetNeedsDraw();
    }

    private void SwitchScreen(int index)
    {
        if (index < 0 || index >= _screens.Length) return;
        if (state.ActiveScreenIndex == index) return;
        state.ActiveScreenIndex = index;
        RefreshHeader();
        SwapToActiveScreen();
    }

    private void HookKeys(IApplication app)
    {
        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key is null) return;

            var code = key.KeyCode;
            if (code == KeyCode.Q || code == (KeyCode.Q | KeyCode.CtrlMask) || code == (KeyCode.C | KeyCode.CtrlMask))
            {
                Application.RequestStop();
                key.Handled = true;
                return;
            }

            for (int i = 0; i < _screens.Length && i < 9; i++)
            {
                if (code == (KeyCode)((int)KeyCode.D1 + i))
                {
                    SwitchScreen(i);
                    key.Handled = true;
                    return;
                }
            }

            int rune = key.AsRune.Value;
            if (rune == '[')
            {
                state.CycleTimescaleDown();
                RefreshStatusBar();
                key.Handled = true;
            }
            else if (rune == ']')
            {
                state.CycleTimescaleUp();
                RefreshStatusBar();
                key.Handled = true;
            }
        };
    }

    private void ScheduleRefresh(IApplication app)
    {
        app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            if (_cts.IsCancellationRequested) return false;
            _screens[state.ActiveScreenIndex].Refresh();
            RefreshStatusBar();
            return true;
        });
    }

    public void Dispose() => _cts.Dispose();
}
