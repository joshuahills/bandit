using System.Text;
using Bandit.Data.Models;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace Bandit.UI.Rendering;

public sealed class BandwidthChart : View
{
    private const int AxisWidth = 8;
    private const int GridRows = 4;

    public NetworkSample[] Samples { get; set; } = [];

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

        if (Samples.Length == 0)
        {
            SetAttribute(Theme.DimAttr);
            string msg = " Waiting for data… ";
            DrawString(AxisWidth, height / 2, msg);
            return true;
        }

        double maxValue = 1000;
        foreach (var sample in Samples)
            if (sample.TotalBytes > maxValue) maxValue = sample.TotalBytes;
        maxValue *= 1.15;

        DrawGrid(chartWidth, height);
        DrawYAxis(height, maxValue);
        DrawBrailleArea(chartWidth, height, maxValue);
        DrawTopRightLabel(width);
        return true;
    }

    private void DrawBrailleArea(int chartWidth, int chartHeight, double maxValue)
    {
        var canvas = new BrailleCanvas(chartWidth, chartHeight);
        int dotW = canvas.DotWidth;

        for (int dotX = 0; dotX < dotW; dotX++)
        {
            int idx = (int)((long)dotX * Samples.Length / dotW);
            if (idx >= Samples.Length) idx = Samples.Length - 1;

            double frac = Samples[idx].TotalBytes / maxValue;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int dotFromBottom = (int)(frac * canvas.DotHeight);
            int dotYTop = canvas.DotHeight - dotFromBottom;
            if (dotYTop < 0) dotYTop = 0;
            if (dotYTop >= canvas.DotHeight) continue;

            canvas.FillBelow(dotX, dotYTop);
        }

        SetAttribute(Theme.UploadAttr);
        for (int cy = 0; cy < chartHeight; cy++)
            for (int cx = 0; cx < chartWidth; cx++)
            {
                char? cell = canvas.GetCell(cx, cy);
                if (cell is null) continue;
                Move(AxisWidth + cx, cy);
                AddRune(new System.Text.Rune(cell.Value));
            }
    }

    private void DrawGrid(int chartWidth, int chartHeight)
    {
        SetAttribute(Theme.GridAttr);
        var dot = new System.Text.Rune('·');
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

    private void DrawTopRightLabel(int width)
    {
        var latest = Samples[^1];
        string label = $" ↑{FormatBytesPerSec(latest.BytesOut)}/s ↓{FormatBytesPerSec(latest.BytesIn)}/s ";
        int labelX = Math.Max(AxisWidth, width - label.Length);
        SetAttribute(Theme.StatusAttr);
        DrawString(labelX, 0, label);
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
}
