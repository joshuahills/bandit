using Bandit.UI;
using Xunit;

namespace Bandit.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("///")]
    [InlineData("/   ")]
    public void Parse_returns_null_for_empty_or_slash_only(string? input)
    {
        Assert.Null(CommandParser.Parse(input));
    }

    [Theory]
    [InlineData("/process 1234", "process", "1234")]
    [InlineData("/p chrome",      "p",       "chrome")]
    [InlineData("/quit",          "quit",    "")]
    [InlineData("/q",             "q",       "")]
    public void Parse_extracts_verb_and_arg(string input, string expectedVerb, string expectedArg)
    {
        var parsed = CommandParser.Parse(input);

        Assert.NotNull(parsed);
        Assert.Equal(expectedVerb, parsed.Verb);
        Assert.Equal(expectedArg, parsed.Arg);
    }

    [Fact]
    public void Parse_lowercases_the_verb()
    {
        var parsed = CommandParser.Parse("/Process 42");

        Assert.NotNull(parsed);
        Assert.Equal("process", parsed.Verb);
        Assert.Equal("42", parsed.Arg);
    }

    [Fact]
    public void Parse_does_not_lowercase_the_arg()
    {
        var parsed = CommandParser.Parse("/process Chrome.EXE");

        Assert.NotNull(parsed);
        Assert.Equal("Chrome.EXE", parsed.Arg);
    }

    [Fact]
    public void Parse_preserves_internal_spaces_in_arg()
    {
        var parsed = CommandParser.Parse("/process   chrome  helper  ");

        Assert.NotNull(parsed);
        Assert.Equal("process", parsed.Verb);
        Assert.Equal("chrome  helper", parsed.Arg);
    }

    [Fact]
    public void Parse_works_without_leading_slash()
    {
        var parsed = CommandParser.Parse("process 1234");

        Assert.NotNull(parsed);
        Assert.Equal("process", parsed.Verb);
        Assert.Equal("1234", parsed.Arg);
    }

    [Fact]
    public void Parse_strips_surrounding_whitespace()
    {
        var parsed = CommandParser.Parse("   /process 1234   ");

        Assert.NotNull(parsed);
        Assert.Equal("process", parsed.Verb);
        Assert.Equal("1234", parsed.Arg);
    }
}
