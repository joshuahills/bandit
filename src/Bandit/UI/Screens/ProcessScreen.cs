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
        _table.Snapshot = collector.Snapshots.Latest() ?? [];
        _table.SetNeedsDraw();
    }
}

internal sealed class ProcessTable : View
{
    private const int PidWidth  = 6;
    private const int RateWidth = 11;
    private const int NameMin   = 16;

    public ProcessNetworkInfo[] Snapshot { get; set; } = [];

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

        int nameWidth = Math.Max(NameMin, width - PidWidth - RateWidth - RateWidth - 4);

        DrawHeader(nameWidth);

        if (Snapshot.Length == 0)
        {
            DrawDiagnostic();
            return true;
        }

        var rows = Snapshot
            .OrderByDescending(p => p.BytesIn + p.BytesOut)
            .Take(height - 2)
            .ToArray();

        for (int i = 0; i < rows.Length; i++)
            DrawRow(2 + i, rows[i], nameWidth);

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
        DrawString(1, 0, " PID".PadRight(PidWidth + 1));
        DrawString(1 + PidWidth + 1, 0, "PROCESS".PadRight(nameWidth));
        DrawString(1 + PidWidth + 1 + nameWidth + 1, 0, "↑".PadLeft(RateWidth));
        DrawString(1 + PidWidth + 1 + nameWidth + 1 + RateWidth + 1, 0, "↓".PadLeft(RateWidth));
    }

    private void DrawRow(int y, ProcessNetworkInfo row, int nameWidth)
    {
        string pid = row.Pid.ToString().PadLeft(PidWidth);
        string name = Truncate(row.Name, nameWidth);
        string up = $"{BandwidthChart.FormatBytesPerSec(row.BytesOut)}/s".PadLeft(RateWidth);
        string down = $"{BandwidthChart.FormatBytesPerSec(row.BytesIn)}/s".PadLeft(RateWidth);

        SetAttribute(Theme.StatusAttr);
        DrawString(1, y, pid);

        SetAttribute(Theme.AccentAttr);
        DrawString(1 + PidWidth + 1, y, name);

        SetAttribute(Theme.UploadAttr);
        DrawString(1 + PidWidth + 1 + nameWidth + 1, y, up);

        SetAttribute(Theme.DownloadAttr);
        DrawString(1 + PidWidth + 1 + nameWidth + 1 + RateWidth + 1, y, down);
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
