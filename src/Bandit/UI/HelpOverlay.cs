using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Bandit.UI;

internal sealed class HelpOverlay : Window
{
    public event EventHandler? Closed;

    public HelpOverlay()
    {
        Title = " help ";
        Width = 64;
        Height = HelpContent.Lines.Count + 2 + 2;
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
        const int keyWidth = 24;

        var lines = HelpContent.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int y = i;

            if (line.IsHeader)
            {
                SetAttribute(line.KeyAttribute);
                DrawString(1, y, line.Keys);
                continue;
            }

            if (string.IsNullOrEmpty(line.Keys) && string.IsNullOrEmpty(line.Description)) continue;

            SetAttribute(line.KeyAttribute);
            DrawString(1, y, line.Keys.PadRight(keyWidth));
            SetAttribute(Theme.StatusAttr);
            DrawString(1 + keyWidth, y, line.Description);
        }

        SetAttribute(Theme.DimAttr);
        DrawString(1, lines.Count + 1, " press ? or Esc to close ");

        return true;
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}
