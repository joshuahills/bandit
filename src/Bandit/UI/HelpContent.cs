using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI;

public sealed record HelpLine(string Keys, string Description, bool IsHeader)
{
    internal Attribute KeyAttribute => IsHeader ? Theme.AccentAttr : Theme.ActiveTabAttr;
}

public static class HelpContent
{
    /// <summary>
    /// Section + key-binding rows shown in the <see cref="HelpOverlay"/>.
    /// Slash-command rows are sourced from <see cref="CommandRegistry"/> so a
    /// new verb registered there shows up in help automatically. The digit
    /// range in the GLOBAL section adapts to the actual number of registered
    /// screens so it never advertises shortcuts that no-op.
    /// </summary>
    public static IReadOnlyList<HelpLine> Build(int screenCount)
    {
        string digitKeys = screenCount switch
        {
            <= 1 => "  1",
            2    => "  1 · 2",
            _    => $"  1 – {Math.Min(screenCount, 9)}",
        };

        var lines = new List<HelpLine>
        {
            new("GLOBAL", "", IsHeader: true),
            new(digitKeys,           "Switch screen",        false),
            new("  [ / ]",           "Cycle timescale",      false),
            new("  /",               "Open command palette", false),
            new("  ?",               "Toggle this help",     false),
            new("  q · Ctrl+C",      "Quit",                 false),
            new("", "", false),

            new("PROCESSES", "", IsHeader: true),
            new("  ↑ ↓",                "Navigate selection",        false),
            new("  Home · End",         "Jump to first / last",      false),
            new("  PgUp · PgDn",        "Page through the list",     false),
            new("  Enter · 2-click",    "Open process detail",       false),
            new("  Esc · Backspace",    "Back to table",             false),
            new("  Click header",       "Sort by column",            false),
            new("", "", false),

            new("COMMANDS", "", IsHeader: true),
        };

        foreach (var c in CommandRegistry.All)
        {
            string keys = string.IsNullOrEmpty(c.ArgHint) ? $"  /{c.Name}" : $"  /{c.Name} {c.ArgHint}";
            lines.Add(new(keys, c.Description, IsHeader: false));
        }

        return lines;
    }
}
