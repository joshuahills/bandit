using System.Text;
using Bandit.Data.Collectors;
using Bandit.Data.Models;
using Bandit.UI.Rendering;
using Terminal.Gui.Drawing;
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
    private const int LiveWidth = 9;
    private const int TotalWidth = 9;
    private const int NameMin   = 16;

    public ProcessNetworkRow[] Rows { get; set; } = [];
    public string WindowLabel { get; set; } = "";

    private readonly ProcessNetworkCollector _collector;

    public ProcessTable(ProcessNetworkCollector collector)
    {
        _collector = collector;
        CanFocus = false;
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

        var sorted = Rows
            .OrderByDescending(p => p.TotalBytesIn + p.TotalBytesOut)
            .Take(height - 2)
            .ToArray();

        for (int i = 0; i < sorted.Length; i++)
            DrawRow(2 + i, sorted[i], nameWidth);

        return true;
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
        SetAttribute(Theme.StatusAttr);
        int x = 1;
        DrawString(x, 0, " PID".PadRight(PidWidth + 1));
        x += PidWidth + 1;

        DrawString(x, 0, "PROCESS".PadRight(nameWidth));
        x += nameWidth + 1;

        DrawString(x, 0, "↑/s".PadLeft(LiveWidth));
        x += LiveWidth + 1;

        DrawString(x, 0, "↓/s".PadLeft(LiveWidth));
        x += LiveWidth + 1;

        string label = string.IsNullOrEmpty(WindowLabel) ? "↑" : $"↑ {WindowLabel}";
        DrawString(x, 0, label.PadLeft(TotalWidth));
        x += TotalWidth + 1;

        label = string.IsNullOrEmpty(WindowLabel) ? "↓" : $"↓ {WindowLabel}";
        DrawString(x, 0, label.PadLeft(TotalWidth));
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
