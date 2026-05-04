namespace Bandit.UI;

public sealed record CommandSpec(string Name, string ArgHint, string Description);

public static class CommandRegistry
{
    /// <summary>The full list of commands surfaced by the palette.</summary>
    public static readonly IReadOnlyList<CommandSpec> All =
    [
        new("process", "<pid|name>", "Open detail for a process"),
        new("p",       "<pid|name>", "Alias of /process"),
        new("quit",    "",           "Exit Bandit"),
        new("q",       "",           "Alias of /quit"),
    ];

    /// <summary>Return commands whose name starts with the given verb (case-insensitive).</summary>
    public static IReadOnlyList<CommandSpec> Match(string verb)
    {
        if (string.IsNullOrEmpty(verb)) return All;
        var matches = new List<CommandSpec>();
        foreach (var c in All)
        {
            if (c.Name.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                matches.Add(c);
        }
        return matches;
    }
}
