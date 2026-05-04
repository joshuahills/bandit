using Bandit.Data.Collectors;
using Bandit.UI;
using Bandit.UI.Rendering;
using Bandit.UI.Screens;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
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

    private const string BanditScheme = "Bandit";

    private void BuildUi()
    {
        SchemeManager.AddScheme(BanditScheme, new Scheme(new Terminal.Gui.Drawing.Attribute(Theme.StatusFg, Theme.Background)));

        _window = new Window
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Title = string.Empty,
            BorderStyle = LineStyle.None,
            SchemeName = BanditScheme,
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

        const string prefix = " timescale ";
        bar.Add(new ColoredLabel(prefix, Theme.StatusAttr)
        {
            X = 0, Y = 0, Width = prefix.Length,
        });

        int x = prefix.Length;
        for (int i = 0; i < AppState.TimescaleLabels.Length; i++)
        {
            bool active = i == state.TimescaleIndex;
            string segment = active ? $"[{AppState.TimescaleLabels[i]}]" : $" {AppState.TimescaleLabels[i]} ";
            int captured = i;
            var seg = new ColoredLabel(segment, active ? Theme.ActiveTabAttr : Theme.InactiveTabAttr)
            {
                X = x, Y = 0, Width = segment.Length,
            };
            seg.MouseEvent += (_, m) =>
            {
                if (m.IsSingleClicked)
                {
                    state.TimescaleIndex = captured;
                    RefreshStatusBar();
                    _screens[state.ActiveScreenIndex].Refresh();
                    m.Handled = true;
                }
            };
            bar.Add(seg);
            x += segment.Length;
        }

        string hint = state.IsElevated
            ? "   [ / ] cycle   q:quit "
            : "   [ / ] cycle   q:quit   [!] Not elevated ";
        bar.Add(new ColoredLabel(hint, Theme.DimAttr)
        {
            X = x, Y = 0, Width = hint.Length,
        });

        var latest = _system.Samples.Latest();
        string up = latest is { } sUp
            ? $" ↑ {BandwidthChart.FormatBytesPerSec(sUp.BytesOut)}/s "
            : " ↑ --- ";
        string down = latest is { } sDown
            ? $" ↓ {BandwidthChart.FormatBytesPerSec(sDown.BytesIn)}/s "
            : " ↓ --- ";
        int ratesLength = up.Length + down.Length;
        bar.Add(new ColoredLabel(up, Theme.UploadAttr)
        {
            X = Pos.AnchorEnd(ratesLength), Y = 0, Width = up.Length,
        });
        bar.Add(new ColoredLabel(down, Theme.DownloadAttr)
        {
            X = Pos.AnchorEnd(down.Length), Y = 0, Width = down.Length,
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
                app.RequestStop();
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
                _screens[state.ActiveScreenIndex].Refresh();
                key.Handled = true;
            }
            else if (rune == ']')
            {
                state.CycleTimescaleUp();
                RefreshStatusBar();
                _screens[state.ActiveScreenIndex].Refresh();
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
