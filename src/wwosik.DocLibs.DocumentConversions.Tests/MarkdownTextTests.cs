namespace wwosik.DocLibs.DocumentConversions.Tests;

public class MarkdownTextTests
{
    [Theory]
    [InlineData("customfield_10682", "customfield_10682")]
    [InlineData("_leading", "\\_leading")]
    [InlineData("*not bold*", "\\*not bold\\*")]
    [InlineData("[not a link]", "\\[not a link]")]
    [InlineData("<b> and a < b", "\\<b> and a < b")]
    [InlineData("C:\\path", "C:\\path")]
    [InlineData("2 * 3", "2 \\* 3")]
    public void Escape_GuardsOnlyWhatWouldFormat(string text, string expected)
    {
        Assert.Equal(expected, MarkdownText.Escape(text));
    }

    [Theory]
    [InlineData("# no heading", "\\# no heading")]
    [InlineData("- no item", "\\- no item")]
    [InlineData("2026. was a year", "2026\\. was a year")]
    [InlineData("---", "\\---")]
    [InlineData("end-to-end", "end-to-end")]
    public void EscapeLineStart_GuardsBlockMarkers(string line, string expected)
    {
        Assert.Equal(expected, MarkdownText.EscapeLineStart(line));
    }

    [Fact]
    public void Code_SurvivesBackticks()
    {
        Assert.Equal("`a`", MarkdownText.Code("a"));
        Assert.Equal("``a`b``", MarkdownText.Code("a`b"));
        Assert.Equal("`` `x ``", MarkdownText.Code("`x"));
    }

    [Fact]
    public void Destination_WrapsAwkwardUrls()
    {
        Assert.Equal("images/a.png", MarkdownText.Destination("images/a.png"));
        Assert.Equal("<images/a b.png>", MarkdownText.Destination("images/a b.png"));
    }

    [Fact]
    public void Table_HasAHeaderRow_AndEscapesCells()
    {
        var table = MarkdownText.Table([["Item", "Price"], ["Pear | green", "0.90\nper kg"], ["Short"]]);

        Assert.Equal("| Item | Price |\n| --- | --- |\n| Pear \\| green | 0.90<br>per kg |\n| Short |  |\n\n", table);
        Assert.Equal(string.Empty, MarkdownText.Table([]));
    }

    [Fact]
    public void Compact_DropsEmptyRowsAndColumns()
    {
        var grid = MarkdownText.Compact([["a", "", "b"], ["", " ", ""], ["c", "", "d"]]);

        Assert.Equal(new[] { new[] { "a", "b" }, new[] { "c", "d" } }, grid.Select(r => r.ToArray()));
    }

    [Fact]
    public void Normalize_CollapsesBlankLines_AndEndsWithOneNewline()
    {
        Assert.Equal("a\n\nb\n", MarkdownText.Normalize("\r\n\na  \r\n\r\n\r\n\nb\n\n\n"));
        Assert.Equal(string.Empty, MarkdownText.Normalize(" \n \n"));
    }
}
