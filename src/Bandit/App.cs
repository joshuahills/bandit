namespace Bandit;

using Bandit.Data.Collectors;
using Bandit.UI;
using Bandit.UI.Rendering;
using Bandit.UI.Screens;
using System.Diagnostics.CodeAnalysis;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

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
    private CommandPalette? _palette;
    private HelpOverlay? _help;
    private IApplication? _app;

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

        using var app = CreateAndInitApp();
        _app = app;
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

    // Terminal.Gui's Application.Init walks reflection-y paths (its
    // ConfigurationManager, JSON converters, etc.) which we already preserve via
    // <TrimmerRootAssembly Include="Terminal.Gui" /> in the csproj. Tell the
    // trim/AOT analyzer that — narrowly, on this single call site, so any
    // future reflection calls we add elsewhere still get flagged.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Terminal.Gui rooted in csproj via TrimmerRootAssembly.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Terminal.Gui rooted in csproj via TrimmerRootAssembly.")]
    private static IApplication CreateAndInitApp() => Application.Create().Init();

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
            // Must be focusable so SetFocus() on nested views (Process detail,
            // command palette input, etc.) actually propagates.
            CanFocus = true,
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
            X = 0,
            Y = 0,
            Width = brandText.Length,
        });

        int x = brandText.Length;
        foreach (var screen in _screens)
        {
            string label = $" [{screen.Index + 1}] {screen.Title} ";
            int captured = screen.Index;
            var attr = screen.Index == state.ActiveScreenIndex ? Theme.ActiveTabAttr : Theme.InactiveTabAttr;
            var tab = new ColoredLabel(label, attr)
            {
                X = x,
                Y = 0,
                Width = label.Length,
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
            X = 0,
            Y = 0,
            Width = prefix.Length,
        });

        int x = prefix.Length;
        for (int i = 0; i < AppState.TimescaleLabels.Length; i++)
        {
            bool active = i == state.TimescaleIndex;
            string segment = active ? $"[{AppState.TimescaleLabels[i]}]" : $" {AppState.TimescaleLabels[i]} ";
            int captured = i;
            var seg = new ColoredLabel(segment, active ? Theme.ActiveTabAttr : Theme.InactiveTabAttr)
            {
                X = x,
                Y = 0,
                Width = segment.Length,
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
            ? "   [ / ] cycle   ?:help   q:quit "
            : "   [ / ] cycle   ?:help   q:quit   [!] Not elevated ";
        bar.Add(new ColoredLabel(hint, Theme.DimAttr)
        {
            X = x,
            Y = 0,
            Width = hint.Length,
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
            X = Pos.AnchorEnd(ratesLength),
            Y = 0,
            Width = up.Length,
        });
        bar.Add(new ColoredLabel(down, Theme.DownloadAttr)
        {
            X = Pos.AnchorEnd(down.Length),
            Y = 0,
            Width = down.Length,
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
        // TG v2 binds Esc to Application.Command.Quit by default. We use Esc
        // for in-app navigation (back out of Process detail, close the
        // command palette) and already have q / Ctrl+C for quitting, so
        // remove the global binding.
        var defaultQuit = Application.GetDefaultKey(Terminal.Gui.Input.Command.Quit);
        if (defaultQuit is not null)
            app.Keyboard.KeyBindings.Remove(defaultQuit);

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key is null) return;

            // When the command palette is open, leave keys alone — let the
            // focused TextField inside it consume them.
            if (_palette is not null) return;

            // ? toggles help even when the help overlay is itself focused, so
            // handle it before the help-open gate below.
            int rune = key.AsRune.Value;
            if (rune == '?')
            {
                if (_help is null) OpenHelp(); else CloseHelp();
                key.Handled = true;
                return;
            }

            // When the help overlay is open, only its own keys (? to close,
            // Esc handled by the overlay itself) should fire.
            if (_help is not null) return;

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
            else if (rune == '/')
            {
                OpenPalette();
                key.Handled = true;
            }
        };
    }

    private void OpenHelp()
    {
        if (_help is not null || _window is null) return;
        _help = new HelpOverlay(_screens.Length);
        _help.Closed += (_, _) => CloseHelp();
        _window.Add(_help);
        _help.SetFocus();
    }

    private void CloseHelp()
    {
        if (_help is null || _window is null) return;
        _window.Remove(_help);
        _help.Dispose();
        _help = null;
        _activeContent?.SetFocus();
    }

    private void OpenPalette()
    {
        if (_palette is not null || _window is null) return;
        _palette = new CommandPalette();
        _palette.Submitted += OnPaletteSubmitted;
        _palette.Cancelled += (_, _) => ClosePalette();
        _window.Add(_palette);
        _palette.FocusInput();
    }

    private void ClosePalette()
    {
        if (_palette is null || _window is null) return;
        _window.Remove(_palette);
        _palette.Dispose();
        _palette = null;
        _activeContent?.SetFocus();
    }

    private void OnPaletteSubmitted(object? sender, string text)
    {
        ClosePalette();
        DispatchCommand(text);
    }

    private void DispatchCommand(string raw)
    {
        var parsed = CommandParser.Parse(raw);
        if (parsed is null) return;

        switch (parsed.Verb)
        {
            case "process":
            case "p":
                OpenProcessByCommand(parsed.Arg);
                break;
            case "quit":
            case "q":
                _app?.RequestStop();
                break;
        }
    }

    private void OpenProcessByCommand(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return;

        var processScreen = _screens.OfType<ProcessScreen>().FirstOrDefault();
        if (processScreen is null) return;

        int? pid = int.TryParse(arg, out var p) ? p : processScreen.FindPidByName(arg);
        if (pid is null) return;

        SwitchScreen(processScreen.Index);
        processScreen.OpenDetail(pid.Value);
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
