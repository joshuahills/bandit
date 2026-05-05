using Bandit.UI;
using Xunit;

namespace Bandit.Tests;

public class HelpContentTests
{
    [Fact]
    public void Lines_includes_global_processes_and_commands_sections()
    {
        var headers = HelpContent.Build(2)
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
        var commandLines = string.Join("\n", HelpContent.Build(2).Select(l => l.Keys));

        foreach (var spec in CommandRegistry.All)
        {
            // Each command should appear in the help text under its slash form.
            Assert.Contains($"/{spec.Name}", commandLines);
        }
    }

    [Fact]
    public void Each_command_line_carries_its_description()
    {
        var lines = HelpContent.Build(2);
        foreach (var spec in CommandRegistry.All)
        {
            var match = lines.FirstOrDefault(l =>
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
        foreach (var line in HelpContent.Build(2).Where(l => l.IsHeader))
            Assert.Equal("", line.Description);
    }

    [Theory]
    [InlineData(1, "  1")]
    [InlineData(2, "  1 · 2")]
    [InlineData(3, "  1 – 3")]
    [InlineData(7, "  1 – 7")]
    [InlineData(15, "  1 – 9")] // capped to 9 since the key handler only binds D1..D9
    public void Switch_screen_keys_reflect_actual_screen_count(int screenCount, string expectedKeys)
    {
        var lines = HelpContent.Build(screenCount);
        var switchLine = lines.First(l => l.Description == "Switch screen");

        Assert.Equal(expectedKeys, switchLine.Keys);
    }

    [Fact]
    public void Includes_PgUp_and_PgDn_in_processes_navigation()
    {
        var lines = HelpContent.Build(2);
        var allKeys = string.Join("\n", lines.Select(l => l.Keys));

        Assert.Contains("PgUp", allKeys);
        Assert.Contains("PgDn", allKeys);
    }
}
