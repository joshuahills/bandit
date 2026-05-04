using System.Text;
using Bandit.Data;
using Bandit.Data.Collectors;
using Bandit.Data.Models;
using Bandit.UI.Rendering;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI.Screens;

public sealed class ProcessScreen(AppState state, ProcessNetworkCollector collector) : IScreen
{
    public int Index => 1;
    public string Title => "Processes";

    private View? _container;
    private ProcessTable? _table;
    private ProcessDetail? _detail;
    private int? _detailPid;

    public View Build()
    {
        _container = new View
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            CanFocus = true,
        };

        if (!collector.IsAvailable)
        {
            BuildElevationBanner();
            return _container;
        }

        if (_detailPid is { } pid) BuildDetail(pid);
        else BuildTable();
        return _container;
    }

    public void Refresh()
    {
        if (_table is not null) RefreshTable();
        if (_detail is not null) RefreshDetail();
    }

    /// <summary>
    /// Open the detail view for a specific PID. Used by the command palette.
    /// Returns false if the PID isn't currently visible in the snapshot history.
    /// </summary>
    public bool OpenDetail(int pid)
    {
        _detailPid = pid;
        if (_container is not null) BuildDetail(pid);
        return true;
    }

    /// <summary>Resolve a process name fragment to a PID via the snapshot history.</summary>
    public int? FindPidByName(string fragment) =>
        ProcessAggregator.FindPidByName(collector.Snapshots.TailN(state.TimescaleSeconds), fragment);

    private void BuildElevationBanner()
    {
        if (_container is null) return;
        (string text, Attribute attr)[] lines =
        [
            ("", Theme.StatusAttr),
            ("  Per-process network monitoring requires administrator privileges.", Theme.WarningAttr),
            ("", Theme.StatusAttr),
            ("  Re-launch Bandit from an elevated terminal:", Theme.StatusAttr),
            ("", Theme.StatusAttr),
            ("    Run as Administrator → bandit.exe", Theme.DimAttr),
        ];
        for (int i = 0; i < lines.Length; i++)
        {
            _container.Add(new ColoredLabel(lines[i].text, lines[i].attr)
            {
                X = 0, Y = i, Width = Dim.Fill(),
            });
        }
    }

    private void BuildTable()
    {
        if (_container is null) return;
        _container.RemoveAll();
        _detail = null;

        _table = new ProcessTable(collector)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _table.Activated += OnTableActivated;
        _container.Add(_table);
        _table.SetFocus();
        RefreshTable();
    }

    private void OnTableActivated(object? sender, int pid)
    {
        _detailPid = pid;
        BuildDetail(pid);
    }

    private void BuildDetail(int pid)
    {
        if (_container is null) return;
        _container.RemoveAll();
        _table = null;

        _detail = new ProcessDetail(pid)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _detail.Back += OnDetailBack;
        _container.Add(_detail);
        _detail.SetFocus();
        RefreshDetail();
    }

    private void OnDetailBack(object? sender, EventArgs e)
    {
        _detailPid = null;
        BuildTable();
    }

    private void RefreshTable()
    {
        if (_table is null) return;
        _table.Rows = BuildRows(state.TimescaleSeconds);
        _table.WindowLabel = state.TimescaleLabel;
        _table.SetNeedsDraw();
    }

    private void RefreshDetail()
    {
        if (_detail is null) return;
        var pid = _detail.Pid;
        var window = collector.Snapshots.TailN(state.TimescaleSeconds);

        // Find the most recent name for this PID.
        string? name = null;
        for (int i = window.Length - 1; i >= 0 && name is null; i--)
        {
            foreach (var p in window[i])
            {
                if (p.Pid == pid) { name = p.Name; break; }
            }
        }

        long liveIn = 0, liveOut = 0;
        if (window.Length > 0)
        {
            foreach (var p in window[^1])
            {
                if (p.Pid == pid) { liveIn = p.BytesIn; liveOut = p.BytesOut; break; }
            }
        }

        long totalIn = 0, totalOut = 0;
        var history = new NetworkSample[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            long bIn = 0, bOut = 0;
            foreach (var p in window[i])
            {
                if (p.Pid == pid) { bIn = p.BytesIn; bOut = p.BytesOut; break; }
            }
            totalIn += bIn;
            totalOut += bOut;
            history[i] = new NetworkSample(0, bIn, bOut, 0, 0);
        }

        _detail.UpdateData(name ?? $"pid:{pid}", liveIn, liveOut, totalIn, totalOut, history, state.TimescaleSeconds, state.TimescaleLabel);
    }

    private ProcessNetworkRow[] BuildRows(int seconds) =>
        ProcessAggregator.Aggregate(collector.Snapshots.TailN(seconds));
}

internal sealed class ProcessTable : View
{
    private const int PidWidth  = 6;
    private const int LiveWidth = 10;
    private const int TotalWidth = 10;
    private const int NameMin   = 16;

    public ProcessNetworkRow[] Rows { get; set; } = [];
    public string WindowLabel { get; set; } = "";
    public int? SelectedPid { get; set; }

    public event EventHandler<int>? Activated;

    private readonly ProcessNetworkCollector _collector;
    private ProcessSortKey _sortKey = ProcessSortKey.TotalCombined;
    private bool _sortDesc = true;

    public ProcessTable(ProcessNetworkCollector collector)
    {
        _collector = collector;
        CanFocus = true;
        MouseEvent += OnTableMouse;
        KeyDown += OnTableKey;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0) return true;

        int rateBlock = LiveWidth * 2 + TotalWidth * 2 + 4;
        int nameWidth = Math.Max(NameMin, width - PidWidth - rateBlock - 2);

        DrawHeader(nameWidth);

        if (Rows.Length == 0)
        {
            DrawDiagnostic();
            return true;
        }

        var sorted = ProcessSorter.Sort(Rows, _sortKey, _sortDesc)
            .Take(height - 2)
            .ToArray();

        // Default selection to the first row if nothing is selected, or if
        // the previously selected PID has fallen out of the visible set.
        if (SelectedPid is null || Array.FindIndex(sorted, r => r.Pid == SelectedPid) < 0)
            SelectedPid = sorted.Length > 0 ? sorted[0].Pid : null;

        for (int i = 0; i < sorted.Length; i++)
            DrawRow(2 + i, sorted[i], nameWidth, sorted[i].Pid == SelectedPid);

        return true;
    }

    private void OnTableKey(object? sender, Key key)
    {
        if (Rows.Length == 0) return;
        var sorted = ProcessSorter.Sort(Rows, _sortKey, _sortDesc).ToArray();
        if (sorted.Length == 0) return;

        int idx = SelectedPid is { } pid
            ? Math.Max(0, Array.FindIndex(sorted, r => r.Pid == pid))
            : 0;

        int newIdx = key.KeyCode switch
        {
            KeyCode.CursorUp   => Math.Max(0, idx - 1),
            KeyCode.CursorDown => Math.Min(sorted.Length - 1, idx + 1),
            KeyCode.Home       => 0,
            KeyCode.End        => sorted.Length - 1,
            KeyCode.PageUp     => Math.Max(0, idx - 10),
            KeyCode.PageDown   => Math.Min(sorted.Length - 1, idx + 10),
            _                  => -1,
        };

        if (newIdx >= 0)
        {
            SelectedPid = sorted[newIdx].Pid;
            SetNeedsDraw();
            key.Handled = true;
            return;
        }

        if (key.KeyCode == KeyCode.Enter && SelectedPid is { } selected)
        {
            Activated?.Invoke(this, selected);
            key.Handled = true;
        }
    }

    private void OnTableMouse(object? sender, Mouse e)
    {
        if (e.Position is not { } pos) return;

        // Header click → sort.
        if (e.IsSingleClicked && pos.Y == 0)
        {
            var clicked = ColumnAt(pos.X);
            if (clicked is null) return;

            if (_sortKey == clicked.Value) _sortDesc = !_sortDesc;
            else { _sortKey = clicked.Value; _sortDesc = clicked.Value is not (ProcessSortKey.Pid or ProcessSortKey.Name); }
            SetNeedsDraw();
            e.Handled = true;
            return;
        }

        // Row click → select; double-click → activate.
        if (pos.Y < 2) return;
        int rowIdx = pos.Y - 2;
        var sorted = ProcessSorter.Sort(Rows, _sortKey, _sortDesc).Take(Viewport.Height - 2).ToArray();
        if (rowIdx >= sorted.Length) return;

        SelectedPid = sorted[rowIdx].Pid;
        SetNeedsDraw();
        e.Handled = true;

        if (e.IsDoubleClicked)
            Activated?.Invoke(this, sorted[rowIdx].Pid);
    }

    private ProcessSortKey? ColumnAt(int x)
    {
        int width = Viewport.Width;
        int rateBlock = LiveWidth * 2 + TotalWidth * 2 + 4;
        int nameWidth = Math.Max(NameMin, width - PidWidth - rateBlock - 2);

        int start = 1;
        if (x >= start && x < start + PidWidth + 1) return ProcessSortKey.Pid;
        start += PidWidth + 1;
        if (x >= start && x < start + nameWidth) return ProcessSortKey.Name;
        start += nameWidth + 1;
        if (x >= start && x < start + LiveWidth) return ProcessSortKey.LiveUp;
        start += LiveWidth + 1;
        if (x >= start && x < start + LiveWidth) return ProcessSortKey.LiveDown;
        start += LiveWidth + 1;
        if (x >= start && x < start + TotalWidth) return ProcessSortKey.TotalUp;
        start += TotalWidth + 1;
        if (x >= start && x < start + TotalWidth) return ProcessSortKey.TotalDown;
        return null;
    }

    private void DrawDiagnostic()
    {
        if (_collector.Status == CollectorStatus.Failed && !string.IsNullOrEmpty(_collector.StartError))
        {
            SetAttribute(Theme.WarningAttr);
            DrawString(2, 2, $" ETW session failed: {_collector.StartError} ");
            return;
        }

        SetAttribute(Theme.DimAttr);
        DrawString(2, 2, " Waiting for traffic… ");
    }

    private void DrawHeader(int nameWidth)
    {
        int x = 1;
        DrawHeaderCell(x, " PID", PidWidth + 1, leftAlign: true,  ProcessSortKey.Pid);
        x += PidWidth + 1;

        DrawHeaderCell(x, "PROCESS", nameWidth, leftAlign: true, ProcessSortKey.Name);
        x += nameWidth + 1;

        DrawHeaderCell(x, "↑/s", LiveWidth, leftAlign: false, ProcessSortKey.LiveUp);
        x += LiveWidth + 1;

        DrawHeaderCell(x, "↓/s", LiveWidth, leftAlign: false, ProcessSortKey.LiveDown);
        x += LiveWidth + 1;

        string upLabel = string.IsNullOrEmpty(WindowLabel) ? "↑" : $"↑ {WindowLabel}";
        DrawHeaderCell(x, upLabel, TotalWidth, leftAlign: false, ProcessSortKey.TotalUp);
        x += TotalWidth + 1;

        string downLabel = string.IsNullOrEmpty(WindowLabel) ? "↓" : $"↓ {WindowLabel}";
        DrawHeaderCell(x, downLabel, TotalWidth, leftAlign: false, ProcessSortKey.TotalDown);
    }

    private void DrawHeaderCell(int x, string label, int width, bool leftAlign, ProcessSortKey key)
    {
        bool active = _sortKey == key;
        string content = active
            ? (leftAlign ? $"{label} {(_sortDesc ? '▾' : '▴')}" : $"{(_sortDesc ? '▾' : '▴')} {label}")
            : label;
        string padded = leftAlign ? content.PadRight(width) : content.PadLeft(width);
        if (padded.Length > width) padded = padded[..width];

        SetAttribute(active ? Theme.ActiveTabAttr : Theme.StatusAttr);
        DrawString(x, 0, padded);
    }

    private void DrawRow(int y, ProcessNetworkRow row, int nameWidth, bool selected)
    {
        string pid = row.Pid.ToString().PadLeft(PidWidth);
        string name = Truncate(row.Name, nameWidth);
        string liveUp = $"{BandwidthChart.FormatBytesPerSec(row.LiveBytesOut)}/s".PadLeft(LiveWidth);
        string liveDown = $"{BandwidthChart.FormatBytesPerSec(row.LiveBytesIn)}/s".PadLeft(LiveWidth);
        string totalUp = BandwidthChart.FormatBytesPerSec(row.TotalBytesOut).PadLeft(TotalWidth);
        string totalDown = BandwidthChart.FormatBytesPerSec(row.TotalBytesIn).PadLeft(TotalWidth);

        var pidAttr  = selected ? Theme.SelectedPidAttr      : Theme.StatusAttr;
        var nameAttr = selected ? Theme.SelectedNameAttr     : Theme.AccentAttr;
        var upAttr   = selected ? Theme.SelectedUploadAttr   : Theme.UploadAttr;
        var downAttr = selected ? Theme.SelectedDownloadAttr : Theme.DownloadAttr;

        // Paint the row background to the right edge so the highlight looks
        // contiguous rather than just behind the printed text.
        if (selected)
        {
            SetAttribute(Theme.SelectedPidAttr);
            DrawString(0, y, new string(' ', Viewport.Width));
        }

        int x = 1;
        SetAttribute(pidAttr);
        DrawString(x, y, pid);
        x += PidWidth + 1;

        SetAttribute(nameAttr);
        DrawString(x, y, name);
        x += nameWidth + 1;

        SetAttribute(upAttr);
        DrawString(x, y, liveUp);
        x += LiveWidth + 1;

        SetAttribute(downAttr);
        DrawString(x, y, liveDown);
        x += LiveWidth + 1;

        SetAttribute(upAttr);
        DrawString(x, y, totalUp);
        x += TotalWidth + 1;

        SetAttribute(downAttr);
        DrawString(x, y, totalDown);
    }

    private static string Truncate(string value, int width)
    {
        if (value.Length <= width) return value.PadRight(width);
        return value[..(width - 1)] + "…";
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}

internal sealed class ProcessDetail : View
{
    public int Pid { get; }
    public event EventHandler? Back;

    private string _name = "";
    private long _liveIn, _liveOut, _totalIn, _totalOut;
    private string _windowLabel = "";
    private readonly BandwidthChart _chart;

    public ProcessDetail(int pid)
    {
        Pid = pid;
        CanFocus = true;
        _chart = new BandwidthChart
        {
            X = 0, Y = 6,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Add(_chart);
        KeyDown += OnDetailKey;
    }

    public void UpdateData(
        string name,
        long liveIn, long liveOut,
        long totalIn, long totalOut,
        NetworkSample[] history,
        int timescaleSeconds,
        string windowLabel)
    {
        _name = name;
        _liveIn = liveIn;
        _liveOut = liveOut;
        _totalIn = totalIn;
        _totalOut = totalOut;
        _windowLabel = windowLabel;
        _chart.Samples = history;
        _chart.TimescaleSeconds = timescaleSeconds;
        _chart.SetNeedsDraw();
        SetNeedsDraw();
    }

    private void OnDetailKey(object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Esc || key.KeyCode == KeyCode.Backspace)
        {
            Back?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(Theme.AccentAttr);
        DrawString(2, 0, $" ◈ {_name}  ");
        SetAttribute(Theme.StatusAttr);
        DrawString(2 + 5 + _name.Length + 2, 0, $"PID {Pid}");
        SetAttribute(Theme.DimAttr);
        DrawString(2, 1, " [Esc] back ");

        SetAttribute(Theme.UploadAttr);
        DrawString(2, 3, $" ↑ live: {BandwidthChart.FormatBytesPerSec(_liveOut)}/s   ↑ over {_windowLabel}: {BandwidthChart.FormatBytesPerSec(_totalOut)}");
        SetAttribute(Theme.DownloadAttr);
        DrawString(2, 4, $" ↓ live: {BandwidthChart.FormatBytesPerSec(_liveIn)}/s   ↓ over {_windowLabel}: {BandwidthChart.FormatBytesPerSec(_totalIn)}");

        return base.OnDrawingContent(context);
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}
