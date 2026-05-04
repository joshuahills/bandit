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
        {
            if (sample.BytesOut > maxValue) maxValue = sample.BytesOut;
            if (sample.BytesIn  > maxValue) maxValue = sample.BytesIn;
        }
        maxValue *= 1.15;

        DrawGrid(chartWidth, height);
        DrawYAxis(height, maxValue);
        DrawLineChart(chartWidth, height, maxValue, s => s.BytesIn,  Theme.DownloadAttr);
        DrawLineChart(chartWidth, height, maxValue, s => s.BytesOut, Theme.UploadAttr);
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

        int prevDotY = -1;
        for (int dotX = 0; dotX < dotW; dotX++)
        {
            double sampleF = dotW <= 1
                ? 0
                : (double)dotX / (dotW - 1) * (Samples.Length - 1);
            int idx0 = (int)Math.Floor(sampleF);
            int idx1 = Math.Min(idx0 + 1, Samples.Length - 1);
            double t = sampleF - idx0;
            double value = selector(Samples[idx0]) * (1 - t) + selector(Samples[idx1]) * t;

            double frac = value / maxValue;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int dotFromBottom = (int)Math.Round(frac * (dotH - 1));
            int dotY = dotH - 1 - dotFromBottom;
            if (dotY < 0) dotY = 0;
            if (dotY >= dotH) dotY = dotH - 1;

            if (prevDotY < 0)
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
