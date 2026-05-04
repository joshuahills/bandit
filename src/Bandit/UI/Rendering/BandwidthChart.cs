using System.Text;
using Bandit.Data.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI.Rendering;

public sealed class BandwidthChart : View
{
    private const int AxisWidth = 8;
    private const int GridRows = 4;
    private const int XAxisRows = 1;
    private const int XAxisLabelCount = 5;

    public NetworkSample[] Samples { get; set; } = [];
    public int TimescaleSeconds { get; set; } = 60;

    public BandwidthChart()
    {
        CanFocus = false;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0) return true;

        int chartWidth = Math.Max(1, width - AxisWidth);
        int plotHeight = Math.Max(1, height - XAxisRows);

        if (Samples.Length == 0)
        {
            SetAttribute(Theme.DimAttr);
            string msg = " Waiting for data… ";
            DrawString(AxisWidth, plotHeight / 2, msg);
            return true;
        }

        double maxValue = 1000;
        foreach (var sample in Samples)
        {
            if (sample.BytesOut > maxValue) maxValue = sample.BytesOut;
            if (sample.BytesIn  > maxValue) maxValue = sample.BytesIn;
        }
        maxValue *= 1.15;

        DrawGrid(chartWidth, plotHeight);
        DrawYAxis(plotHeight, maxValue);
        DrawLineChart(chartWidth, plotHeight, maxValue, s => s.BytesIn,  Theme.DownloadAttr);
        DrawLineChart(chartWidth, plotHeight, maxValue, s => s.BytesOut, Theme.UploadAttr);
        DrawXAxis(chartWidth, height);
        return true;
    }

    private void DrawLineChart(
        int chartWidth,
        int chartHeight,
        double maxValue,
        Func<NetworkSample, long> selector,
        Attribute attribute)
    {
        var canvas = new BrailleCanvas(chartWidth, chartHeight);
        int dotW = canvas.DotWidth;
        int dotH = canvas.DotHeight;

        // Map each dot column to a real point in time relative to the right edge ("now").
        // Right edge = newest sample (age 0). Left edge = TimescaleSeconds ago. Samples
        // are 1-per-second, so dot columns that fall outside the available history stay
        // blank rather than stretching the data across the whole chart.
        double timescale = Math.Max(1, TimescaleSeconds);
        double secondsAvailable = Samples.Length - 1;

        int prevDotY = -1;
        bool prevValid = false;

        for (int dotX = 0; dotX < dotW; dotX++)
        {
            double frac = dotW <= 1 ? 1 : (double)dotX / (dotW - 1);
            double ageSeconds = (1 - frac) * timescale;
            if (ageSeconds > secondsAvailable)
            {
                prevValid = false;
                continue;
            }

            double sampleIdx = (Samples.Length - 1) - ageSeconds;
            int idx0 = Math.Max(0, (int)Math.Floor(sampleIdx));
            int idx1 = Math.Min(Samples.Length - 1, idx0 + 1);
            double t = sampleIdx - idx0;
            double value = selector(Samples[idx0]) * (1 - t) + selector(Samples[idx1]) * t;

            double valFrac = value / maxValue;
            if (valFrac < 0) valFrac = 0;
            if (valFrac > 1) valFrac = 1;
            int dotFromBottom = (int)Math.Round(valFrac * (dotH - 1));
            int dotY = dotH - 1 - dotFromBottom;
            if (dotY < 0) dotY = 0;
            if (dotY >= dotH) dotY = dotH - 1;

            if (!prevValid)
            {
                canvas.SetDot(dotX, dotY);
            }
            else
            {
                int yMin = Math.Min(prevDotY, dotY);
                int yMax = Math.Max(prevDotY, dotY);
                for (int y = yMin; y <= yMax; y++)
                    canvas.SetDot(dotX, y);
            }
            prevDotY = dotY;
            prevValid = true;
        }

        SetAttribute(attribute);
        for (int cy = 0; cy < chartHeight; cy++)
            for (int cx = 0; cx < chartWidth; cx++)
            {
                char? cell = canvas.GetCell(cx, cy);
                if (cell is null) continue;
                Move(AxisWidth + cx, cy);
                AddRune(new Rune(cell.Value));
            }
    }

    private void DrawGrid(int chartWidth, int chartHeight)
    {
        SetAttribute(Theme.GridAttr);
        var dot = new Rune('·');
        for (int row = 1; row < GridRows; row++)
        {
            int y = chartHeight - 1 - (row * (chartHeight - 1) / GridRows);
            for (int x = 0; x < chartWidth; x++)
            {
                Move(AxisWidth + x, y);
                AddRune(dot);
            }
        }
    }

    private void DrawYAxis(int chartHeight, double maxValue)
    {
        SetAttribute(Theme.AxisAttr);
        for (int row = 0; row <= GridRows; row++)
        {
            int y = chartHeight - 1 - (row * (chartHeight - 1) / GridRows);
            double value = maxValue * row / GridRows;
            string label = FormatBytesPerSec(value).PadLeft(AxisWidth - 1) + " ";
            DrawString(0, y, label);
        }
    }

    private void DrawXAxis(int chartWidth, int totalHeight)
    {
        if (TimescaleSeconds <= 0) return;
        SetAttribute(Theme.AxisAttr);
        int y = totalHeight - 1;

        for (int i = 0; i < XAxisLabelCount; i++)
        {
            int secondsAgo = (XAxisLabelCount - 1 - i) * TimescaleSeconds / (XAxisLabelCount - 1);
            string label = secondsAgo == 0 ? "now" : "-" + FormatDuration(secondsAgo);

            int tickX = i * (chartWidth - 1) / (XAxisLabelCount - 1);
            int x = i == 0
                ? AxisWidth + tickX
                : i == XAxisLabelCount - 1
                    ? AxisWidth + tickX - label.Length + 1
                    : AxisWidth + tickX - label.Length / 2;
            if (x < AxisWidth) x = AxisWidth;
            if (x + label.Length > AxisWidth + chartWidth) x = AxisWidth + chartWidth - label.Length;

            DrawString(x, y, label);
        }
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }

    public static string FormatBytesPerSec(double bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000:F1}G";
        if (bytes >= 1_000_000)     return $"{bytes / 1_000_000:F1}M";
        if (bytes >= 1_000)         return $"{bytes / 1_000:F1}K";
        return $"{bytes:F0}B";
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            return s == 0 ? $"{m}m" : $"{m}m{s}s";
        }
        if (seconds < 86400)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            return m == 0 ? $"{h}h" : $"{h}h{m}m";
        }
        int d = seconds / 86400;
        int hh = (seconds % 86400) / 3600;
        return hh == 0 ? $"{d}d" : $"{d}d{hh}h";
    }
}
