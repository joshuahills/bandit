namespace Bandit.UI;

public sealed record ParsedCommand(string Verb, string Arg);

public static class CommandParser
{
    /// <summary>
    /// Parse a raw palette input like "/process 1234" into a verb (lowercased)
    /// + arg pair. Leading slash optional. Returns null for empty / whitespace
    /// / slash-only input.
    /// </summary>
    public static ParsedCommand? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim().TrimStart('/').Trim();
        if (text.Length == 0) return null;

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";
        return new ParsedCommand(verb, arg);
    }
}
