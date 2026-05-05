using Bandit.UI;
using Xunit;

namespace Bandit.Tests;

public class HelpContentTests
{
    [Fact]
    public void Lines_includes_global_processes_and_commands_sections()
    {
        var headers = HelpContent.Lines
            .Where(l => l.IsHeader)
            .Select(l => l.Keys)
            .ToArray();

        Assert.Contains("GLOBAL", headers);
        Assert.Contains("PROCESSES", headers);
        Assert.Contains("COMMANDS", headers);
    }

    [Fact]
    public void Every_registered_command_appears_in_help()
    {
        var commandLines = string.Join("\n", HelpContent.Lines.Select(l => l.Keys));

        foreach (var spec in CommandRegistry.All)
        {
            // Each command should appear in the help text under its slash form.
            Assert.Contains($"/{spec.Name}", commandLines);
        }
    }

    [Fact]
    public void Each_command_line_carries_its_description()
    {
        foreach (var spec in CommandRegistry.All)
        {
            var match = HelpContent.Lines.FirstOrDefault(l =>
            {
                if (l.IsHeader) return false;
                var trimmed = l.Keys.TrimStart();
                // Exact match (verb takes no args) or verb + space (has args).
                return trimmed == $"/{spec.Name}" || trimmed.StartsWith($"/{spec.Name} ");
            });

            Assert.NotNull(match);
            Assert.Equal(spec.Description, match!.Description);
        }
    }

    [Fact]
    public void Headers_have_no_description()
    {
        foreach (var line in HelpContent.Lines.Where(l => l.IsHeader))
            Assert.Equal("", line.Description);
    }
}
