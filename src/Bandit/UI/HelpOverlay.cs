using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI;

internal sealed class HelpOverlay : Window
{
    public event EventHandler? Closed;

    public HelpOverlay()
    {
        Title = " help ";
        Width = 64;
        Height = ContentLines.Length + 2 + 2;
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

    private record Line(string Keys, string Description, Attribute KeyAttr, bool IsHeader = false);

    private static readonly Line[] ContentLines = BuildLines();

    private static Line[] BuildLines()
    {
        var lines = new List<Line>
        {
            new("GLOBAL", "", Theme.AccentAttr, IsHeader: true),
            new("  1 – 9",          "Switch screen",        Theme.ActiveTabAttr),
            new("  [ / ]",          "Cycle timescale",      Theme.ActiveTabAttr),
            new("  /",              "Open command palette", Theme.ActiveTabAttr),
            new("  ?",              "Toggle this help",     Theme.ActiveTabAttr),
            new("  q · Ctrl+C",     "Quit",                 Theme.ActiveTabAttr),
            new("", "", Theme.AccentAttr),

            new("PROCESSES", "", Theme.AccentAttr, IsHeader: true),
            new("  ↑ ↓ Home End",   "Navigate selection",        Theme.ActiveTabAttr),
            new("  Enter · 2-click","Open process detail",       Theme.ActiveTabAttr),
            new("  Esc · Backspace","Back to table",             Theme.ActiveTabAttr),
            new("  Click header",   "Sort by column",            Theme.ActiveTabAttr),
            new("", "", Theme.AccentAttr),

            new("COMMANDS", "", Theme.AccentAttr, IsHeader: true),
        };

        foreach (var c in CommandRegistry.All)
        {
            string keys = string.IsNullOrEmpty(c.ArgHint) ? $"  /{c.Name}" : $"  /{c.Name} {c.ArgHint}";
            lines.Add(new(keys, c.Description, Theme.ActiveTabAttr));
        }

        return lines.ToArray();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        const int keyWidth = 24;

        for (int i = 0; i < ContentLines.Length; i++)
        {
            var line = ContentLines[i];
            int y = i;

            if (line.IsHeader)
            {
                SetAttribute(line.KeyAttr);
                DrawString(1, y, line.Keys);
                continue;
            }

            if (string.IsNullOrEmpty(line.Keys) && string.IsNullOrEmpty(line.Description)) continue;

            SetAttribute(line.KeyAttr);
            DrawString(1, y, line.Keys.PadRight(keyWidth));
            SetAttribute(Theme.StatusAttr);
            DrawString(1 + keyWidth, y, line.Description);
        }

        SetAttribute(Theme.DimAttr);
        DrawString(1, ContentLines.Length + 1, " press ? or Esc to close ");

        return true;
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}
