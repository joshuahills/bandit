using System.Text;
using Bandit.Data.Collectors;
using Bandit.Data.Models;
using Bandit.UI.Rendering;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI.Screens;

public sealed class ProcessScreen(AppState state, ProcessNetworkCollector collector) : IScreen
{
    public int Index => 1;
    public string Title => "Processes";

    private ProcessTable? _table;

    public View Build()
    {
        _ = state;

        if (!collector.IsAvailable)
        {
            var container = new View
            {
                X = 0, Y = 0,
                Width = Dim.Fill(), Height = Dim.Fill(),
                CanFocus = false,
            };

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
                container.Add(new ColoredLabel(lines[i].text, lines[i].attr)
                {
                    X = 0, Y = i,
                    Width = Dim.Fill(),
                });
            }
            return container;
        }

        _table = new ProcessTable(collector)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Refresh();
        return _table;
    }

    public void Refresh()
    {
        if (_table is null) return;
        _table.Rows = BuildRows(state.TimescaleSeconds);
        _table.WindowLabel = state.TimescaleLabel;
        _table.SetNeedsDraw();
    }

    private ProcessNetworkRow[] BuildRows(int seconds)
    {
        var window = collector.Snapshots.TailN(seconds);
        if (window.Length == 0) return [];

        var totals = new Dictionary<int, (string Name, long In, long Out)>();
        foreach (var snapshot in window)
        {
            foreach (var p in snapshot)
            {
                totals.TryGetValue(p.Pid, out var t);
                totals[p.Pid] = (p.Name, t.In + p.BytesIn, t.Out + p.BytesOut);
            }
        }

        // Newest snapshot in the window is the live (last-second) delta.
        var live = new Dictionary<int, (long In, long Out)>();
        foreach (var p in window[^1])
            live[p.Pid] = (p.BytesIn, p.BytesOut);

        var result = new ProcessNetworkRow[totals.Count];
        int i = 0;
        foreach (var kv in totals)
        {
            live.TryGetValue(kv.Key, out var l);
            result[i++] = new ProcessNetworkRow(
                kv.Key, kv.Value.Name,
                l.In, l.Out,
                kv.Value.In, kv.Value.Out);
        }
        return result;
    }
}

internal sealed class ProcessTable : View
{
    private const int PidWidth  = 6;
    private const int LiveWidth = 10;
    private const int TotalWidth = 10;
    private const int NameMin   = 16;

    public enum SortKey { TotalCombined, Pid, Name, LiveUp, LiveDown, TotalUp, TotalDown }

    public ProcessNetworkRow[] Rows { get; set; } = [];
    public string WindowLabel { get; set; } = "";

    private readonly ProcessNetworkCollector _collector;
    private SortKey _sortKey = SortKey.TotalCombined;
    private bool _sortDesc = true;

    public ProcessTable(ProcessNetworkCollector collector)
    {
        _collector = collector;
        CanFocus = false;
        MouseEvent += OnTableMouse;
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

        var sorted = ApplySort(Rows)
            .Take(height - 2)
            .ToArray();

        for (int i = 0; i < sorted.Length; i++)
            DrawRow(2 + i, sorted[i], nameWidth);

        return true;
    }

    private IEnumerable<ProcessNetworkRow> ApplySort(IEnumerable<ProcessNetworkRow> rows) =>
        _sortKey switch
        {
            SortKey.Pid       => _sortDesc ? rows.OrderByDescending(r => r.Pid)             : rows.OrderBy(r => r.Pid),
            SortKey.Name      => _sortDesc ? rows.OrderByDescending(r => r.Name)            : rows.OrderBy(r => r.Name),
            SortKey.LiveUp    => _sortDesc ? rows.OrderByDescending(r => r.LiveBytesOut)    : rows.OrderBy(r => r.LiveBytesOut),
            SortKey.LiveDown  => _sortDesc ? rows.OrderByDescending(r => r.LiveBytesIn)     : rows.OrderBy(r => r.LiveBytesIn),
            SortKey.TotalUp   => _sortDesc ? rows.OrderByDescending(r => r.TotalBytesOut)   : rows.OrderBy(r => r.TotalBytesOut),
            SortKey.TotalDown => _sortDesc ? rows.OrderByDescending(r => r.TotalBytesIn)    : rows.OrderBy(r => r.TotalBytesIn),
            _                 => _sortDesc ? rows.OrderByDescending(r => r.TotalBytesIn + r.TotalBytesOut)
                                           : rows.OrderBy(r => r.TotalBytesIn + r.TotalBytesOut),
        };

    private void OnTableMouse(object? sender, Mouse e)
    {
        if (!e.IsSingleClicked || e.Position is not { } pos || pos.Y != 0) return;
        var clicked = ColumnAt(pos.X);
        if (clicked is null) return;

        if (_sortKey == clicked.Value)
        {
            _sortDesc = !_sortDesc;
        }
        else
        {
            _sortKey = clicked.Value;
            // Numeric columns default to descending (biggest first); text/id
            // columns default to ascending.
            _sortDesc = clicked.Value is not (SortKey.Pid or SortKey.Name);
        }
        SetNeedsDraw();
        e.Handled = true;
    }

    private SortKey? ColumnAt(int x)
    {
        int width = Viewport.Width;
        int rateBlock = LiveWidth * 2 + TotalWidth * 2 + 4;
        int nameWidth = Math.Max(NameMin, width - PidWidth - rateBlock - 2);

        int start = 1;
        if (x >= start && x < start + PidWidth + 1) return SortKey.Pid;
        start += PidWidth + 1;
        if (x >= start && x < start + nameWidth) return SortKey.Name;
        start += nameWidth + 1;
        if (x >= start && x < start + LiveWidth) return SortKey.LiveUp;
        start += LiveWidth + 1;
        if (x >= start && x < start + LiveWidth) return SortKey.LiveDown;
        start += LiveWidth + 1;
        if (x >= start && x < start + TotalWidth) return SortKey.TotalUp;
        start += TotalWidth + 1;
        if (x >= start && x < start + TotalWidth) return SortKey.TotalDown;
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
        DrawHeaderCell(x, " PID", PidWidth + 1, leftAlign: true,  SortKey.Pid);
        x += PidWidth + 1;

        DrawHeaderCell(x, "PROCESS", nameWidth, leftAlign: true, SortKey.Name);
        x += nameWidth + 1;

        DrawHeaderCell(x, "↑/s", LiveWidth, leftAlign: false, SortKey.LiveUp);
        x += LiveWidth + 1;

        DrawHeaderCell(x, "↓/s", LiveWidth, leftAlign: false, SortKey.LiveDown);
        x += LiveWidth + 1;

        string upLabel = string.IsNullOrEmpty(WindowLabel) ? "↑" : $"↑ {WindowLabel}";
        DrawHeaderCell(x, upLabel, TotalWidth, leftAlign: false, SortKey.TotalUp);
        x += TotalWidth + 1;

        string downLabel = string.IsNullOrEmpty(WindowLabel) ? "↓" : $"↓ {WindowLabel}";
        DrawHeaderCell(x, downLabel, TotalWidth, leftAlign: false, SortKey.TotalDown);
    }

    private void DrawHeaderCell(int x, string label, int width, bool leftAlign, SortKey key)
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

    private void DrawRow(int y, ProcessNetworkRow row, int nameWidth)
    {
        string pid = row.Pid.ToString().PadLeft(PidWidth);
        string name = Truncate(row.Name, nameWidth);
        string liveUp = $"{BandwidthChart.FormatBytesPerSec(row.LiveBytesOut)}/s".PadLeft(LiveWidth);
        string liveDown = $"{BandwidthChart.FormatBytesPerSec(row.LiveBytesIn)}/s".PadLeft(LiveWidth);
        string totalUp = BandwidthChart.FormatBytesPerSec(row.TotalBytesOut).PadLeft(TotalWidth);
        string totalDown = BandwidthChart.FormatBytesPerSec(row.TotalBytesIn).PadLeft(TotalWidth);

        int x = 1;
        SetAttribute(Theme.StatusAttr);
        DrawString(x, y, pid);
        x += PidWidth + 1;

        SetAttribute(Theme.AccentAttr);
        DrawString(x, y, name);
        x += nameWidth + 1;

        SetAttribute(Theme.UploadAttr);
        DrawString(x, y, liveUp);
        x += LiveWidth + 1;

        SetAttribute(Theme.DownloadAttr);
        DrawString(x, y, liveDown);
        x += LiveWidth + 1;

        SetAttribute(Theme.UploadAttr);
        DrawString(x, y, totalUp);
        x += TotalWidth + 1;

        SetAttribute(Theme.DownloadAttr);
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
