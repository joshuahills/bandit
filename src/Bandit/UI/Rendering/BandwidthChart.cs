using Bandit.Data.Models;
using Hex1b.Surfaces;
using Hex1b.Widgets;

namespace Bandit.UI.Rendering;

public static class BandwidthChart
{
    private const int AxisWidth = 8;
    private const int GridRows = 4;

    public static SurfaceLayer BuildLayer(SurfaceLayerContext s, NetworkSample[] samples)
        => s.Layer(surface => DrawChart(surface, samples), 0, 0);

    private static void DrawChart(Surface surface, NetworkSample[] samples)
    {
        int chartWidth = Math.Max(1, surface.Width - AxisWidth);
        int chartHeight = Math.Max(1, surface.Height);

        if (samples.Length == 0)
        {
            surface.WriteText(AxisWidth, chartHeight / 2, " Waiting for data… ", Theme.Dim, null, default);
            return;
        }

        double maxValue = 1000;
        foreach (var sample in samples)
            if (sample.TotalBytes > maxValue) maxValue = sample.TotalBytes;
        maxValue *= 1.15;

        DrawGrid(surface, chartHeight);
        DrawYAxis(surface, chartHeight, maxValue);

        var canvas = new BrailleCanvas(chartWidth, chartHeight);
        int dotW = canvas.DotWidth;

        for (int dotX = 0; dotX < dotW; dotX++)
        {
            int idx = (int)((long)dotX * samples.Length / dotW);
            if (idx >= samples.Length) idx = samples.Length - 1;

            double frac = samples[idx].TotalBytes / maxValue;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int dotFromBottom = (int)(frac * canvas.DotHeight);
            int dotYTop = canvas.DotHeight - dotFromBottom;
            if (dotYTop < 0) dotYTop = 0;
            if (dotYTop >= canvas.DotHeight) continue;

            canvas.FillBelow(dotX, dotYTop);
        }

        for (int cy = 0; cy < chartHeight; cy++)
            for (int cx = 0; cx < chartWidth; cx++)
            {
                char? cell = canvas.GetCell(cx, cy);
                if (cell is not null)
                    surface.WriteText(AxisWidth + cx, cy, cell.Value.ToString(), Theme.Upload, null, default);
            }

        var latest = samples[^1];
        string label = $" ↑{FormatBytesPerSec(latest.BytesOut)}/s ↓{FormatBytesPerSec(latest.BytesIn)}/s ";
        int labelX = Math.Max(AxisWidth, surface.Width - label.Length);
        surface.WriteText(labelX, 0, label, Theme.StatusFg, null, default);
    }

    private static void DrawGrid(Surface surface, int chartHeight)
    {
        int chartWidth = surface.Width - AxisWidth;
        for (int row = 1; row < GridRows; row++)
        {
            int y = chartHeight - 1 - (row * (chartHeight - 1) / GridRows);
            for (int x = 0; x < chartWidth; x++)
                surface.WriteChar(AxisWidth + x, y, '·', Theme.Grid, null, default);
        }
    }

    private static void DrawYAxis(Surface surface, int chartHeight, double maxValue)
    {
        for (int row = 0; row <= GridRows; row++)
        {
            int y = chartHeight - 1 - (row * (chartHeight - 1) / GridRows);
            double value = maxValue * row / GridRows;
            string label = FormatBytesPerSec(value).PadLeft(AxisWidth - 1) + " ";
            surface.WriteText(0, y, label, Theme.Axis, null, default);
        }
    }

    public static string FormatBytesPerSec(double bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000:F1}G";
        if (bytes >= 1_000_000)     return $"{bytes / 1_000_000:F1}M";
        if (bytes >= 1_000)         return $"{bytes / 1_000:F1}K";
        return $"{bytes:F0}B";
    }
}
