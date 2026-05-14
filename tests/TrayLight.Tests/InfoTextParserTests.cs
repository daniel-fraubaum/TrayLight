using TrayLight.Services;
using Xunit;

namespace TrayLight.Tests;

public class InfoTextParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ReturnsEmpty(string? input)
    {
        var lines = InfoTextParser.Parse(input);
        Assert.Empty(lines);
    }

    [Fact]
    public void Parse_SingleLine_NoMarkup_OneRegularRun()
    {
        var lines = InfoTextParser.Parse("Hello world");

        var line = Assert.Single(lines);
        Assert.False(line.IsBlank);
        var run = Assert.Single(line.Runs);
        Assert.Equal("Hello world", run.Text);
        Assert.False(run.IsBold);
    }

    [Fact]
    public void Parse_BoldOnly_OneBoldRun()
    {
        var lines = InfoTextParser.Parse("*Bold*");

        var line = Assert.Single(lines);
        var run  = Assert.Single(line.Runs);
        Assert.Equal("Bold", run.Text);
        Assert.True(run.IsBold);
    }

    [Fact]
    public void Parse_MixedBoldAndNormal_ProducesAlternatingRuns()
    {
        var lines = InfoTextParser.Parse("*Title* details");

        var line = Assert.Single(lines);
        Assert.Equal(2, line.Runs.Count);
        Assert.Equal("Title", line.Runs[0].Text);
        Assert.True(line.Runs[0].IsBold);
        Assert.Equal(" details", line.Runs[1].Text);
        Assert.False(line.Runs[1].IsBold);
    }

    [Fact]
    public void Parse_PipeSeparator_ProducesMultipleLines()
    {
        var lines = InfoTextParser.Parse("Line 1 | Line 2 | Line 3");

        Assert.Equal(3, lines.Count);
        Assert.Equal("Line 1", lines[0].Runs[0].Text);
        Assert.Equal("Line 2", lines[1].Runs[0].Text);
        Assert.Equal("Line 3", lines[2].Runs[0].Text);
    }

    [Fact]
    public void Parse_DoublePipe_ProducesBlankLineBetween()
    {
        var lines = InfoTextParser.Parse("Block A || Block B");

        Assert.Equal(3, lines.Count);
        Assert.Equal("Block A", lines[0].Runs[0].Text);
        Assert.True(lines[1].IsBlank);
        Assert.Equal("Block B", lines[2].Runs[0].Text);
    }

    [Fact]
    public void Parse_TrailingDoublePipe_TrimsBlankLines()
    {
        var lines = InfoTextParser.Parse("Hello ||");

        var line = Assert.Single(lines);
        Assert.Equal("Hello", line.Runs[0].Text);
    }

    [Fact]
    public void Parse_OddAsteriskCount_LastIsLiteral()
    {
        var lines = InfoTextParser.Parse("*Bold* with stray *");

        var line = Assert.Single(lines);
        Assert.Equal(2, line.Runs.Count);
        Assert.Equal("Bold", line.Runs[0].Text);
        Assert.True(line.Runs[0].IsBold);
        Assert.Equal(" with stray *", line.Runs[1].Text);
        Assert.False(line.Runs[1].IsBold);
    }

    [Fact]
    public void Parse_FullExampleFromSpec_MatchesExpectedStructure()
    {
        const string input =
            "*IT-Office Hours* | Mon-Thu: 07:00-17:00 | Fri: 07:00-15:00 || " +
            "*IT-Hotline* | +43 1 234 5678 || " +
            "*Emergency outside office hours:* | Only incidents critical to production";

        var lines = InfoTextParser.Parse(input);

        Assert.Equal(9, lines.Count);
        Assert.True(lines[0].Runs[0].IsBold);
        Assert.Equal("IT-Office Hours", lines[0].Runs[0].Text);
        Assert.False(lines[1].IsBlank);
        Assert.Equal("Mon-Thu: 07:00-17:00", lines[1].Runs[0].Text);
        Assert.True(lines[3].IsBlank);
        Assert.True(lines[4].Runs[0].IsBold);
        Assert.Equal("IT-Hotline", lines[4].Runs[0].Text);
        Assert.True(lines[6].IsBlank);
        Assert.True(lines[7].Runs[0].IsBold);
        Assert.Equal("Emergency outside office hours:", lines[7].Runs[0].Text);
    }
}
