using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Bandit.UI;

internal sealed class HelpOverlay : Window
{
    private const int KeyColumnWidth = 24;
    private const int Padding = 4;          // chars of slack on the right of the longest line
    private const int Border = 2;           // window left + right border chars
    private const int BottomChrome = 2;     // border + the 'press ? or Esc' footer

    public event EventHandler? Closed;

    private readonly IReadOnlyList<HelpLine> _lines;

    public HelpOverlay(int screenCount)
    {
        _lines = HelpContent.Build(screenCount);

        // Size to the actual content so the longest description / verb hint
        // never crops or crowds the border.
        int longest = 0;
        foreach (var line in _lines)
        {
            int width = line.IsHeader
                ? line.Keys.Length
                : KeyColumnWidth + line.Description.Length;
            if (width > longest) longest = width;
        }

        Title = " help ";
        Width = longest + Padding + Border;
        Height = _lines.Count + BottomChrome + 1;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Rounded;
        SchemeName = "Bandit";
        CanFocus = true;
        KeyDown += OnHelpKey;
    }

    private void OnHelpKey(object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Esc)
        {
            Closed?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            int y = i;

            if (line.IsHeader)
            {
                SetAttribute(line.KeyAttribute);
                DrawString(1, y, line.Keys);
                continue;
            }

            if (string.IsNullOrEmpty(line.Keys) && string.IsNullOrEmpty(line.Description)) continue;

            SetAttribute(line.KeyAttribute);
            DrawString(1, y, line.Keys.PadRight(KeyColumnWidth));
            SetAttribute(Theme.StatusAttr);
            DrawString(1 + KeyColumnWidth, y, line.Description);
        }

        SetAttribute(Theme.DimAttr);
        DrawString(1, _lines.Count + 1, " press ? or Esc to close ");

        return true;
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}
