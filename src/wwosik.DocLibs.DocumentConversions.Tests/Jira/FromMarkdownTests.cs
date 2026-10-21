using wwosik.DocLibs.DocumentConversions.Jira;

namespace wwosik.DocLibs.DocumentConversions.Tests.Jira;

public class FromMarkdownTests
{
    private static string ToWiki(string markdown) => JiraWikiMarkup.FromMarkdown(markdown);

    [Fact]
    public void Headings_BecomeWikiHeadings()
    {
        Assert.Equal("h2. Acceptance criteria", ToWiki("## Acceptance criteria"));
        Assert.Equal("h1. Title", ToWiki("# Title"));
    }

    [Fact]
    public void Lists_KeepTheirNesting()
    {
        var wiki = ToWiki("""
            - First
              - Deeper
            1. One
            2. Two
            """);

        Assert.Equal("* First\n** Deeper\n# One\n# Two", wiki);
    }

    [Fact]
    public void Emphasis_BecomesWikiEmphasis()
    {
        Assert.Equal("*bold* and _italic_", ToWiki("**bold** and *italic*"));
    }

    [Fact]
    public void Underscores_InsideAWordStayAsTheyAre()
    {
        Assert.Equal(
            "customfield_10682 and customfield_10683",
            ToWiki("customfield_10682 and customfield_10683"));
    }

    [Fact]
    public void Code_BecomesMonospaceAndCodeBlocks()
    {
        Assert.Equal("{{doc-libs}}", ToWiki("`doc-libs`"));

        var wiki = ToWiki("""
            ```cs
            var x = 1;
            ```
            """);

        Assert.Equal("{code:cs}\nvar x = 1;\n{code}", wiki);
    }

    [Fact]
    public void Links_TakeTheWikiForm()
    {
        Assert.Equal("[Manual|https://example.org]", ToWiki("[Manual](https://example.org)"));
    }

    [Fact]
    public void Tables_GetADoubleBarHeader()
    {
        var wiki = ToWiki("""
            | Field | Id |
            | --- | --- |
            | Epic Link | 10682 |
            """);

        Assert.Equal("||Field||Id||\n|Epic Link|10682|", wiki);
    }

    [Fact]
    public void QuotesAndRules_AreTranslated()
    {
        Assert.Equal("{quote}Quote{quote}", ToWiki("> Quote"));
        Assert.Equal("----", ToWiki("---"));
    }

    [Fact]
    public void PlainProse_SurvivesUntouched()
    {
        const string text = "A paragraph with accents: naïve café, façade, smörgåsbord.";

        Assert.Equal(text, ToWiki(text));
    }

    [Fact]
    public void SoftWraps_MakeNoDifference()
    {
        const string wrapped = """
            ## Heading

            A paragraph that is wrapped
            over several lines and still **stays
            one paragraph**.

            - An item whose text
              continues on the next line
            - Another item

            > A quote that is
            > wrapped as well.

            | Field | Id |
            | --- | --- |
            | Epic Link | 10682 |
            """;

        const string flat = """
            ## Heading

            A paragraph that is wrapped over several lines and still **stays one paragraph**.

            - An item whose text continues on the next line
            - Another item

            > A quote that is wrapped as well.

            | Field | Id |
            | --- | --- |
            | Epic Link | 10682 |
            """;

        Assert.Equal(ToWiki(flat), ToWiki(wrapped));
    }

    [Fact]
    public void Emphasis_SurvivesALineBreak()
    {
        var wiki = ToWiki("""
            **bold across
            a line break**
            """);

        Assert.Equal("*bold across a line break*", wiki);
    }

    [Fact]
    public void Quotes_BecomeOneBlock()
    {
        var wiki = ToWiki("""
            > First line
            > second line
            """);

        Assert.Equal("{quote}First line second line{quote}", wiki);
    }

    [Fact]
    public void ListItems_SwallowTheirFollowUpLines()
    {
        var wiki = ToWiki("""
            1. First item, which
               runs over two lines
            2. Second item
            """);

        Assert.Equal("# First item, which runs over two lines\n# Second item", wiki);
    }

    [Fact]
    public void BlankLines_DoNotBreakTheNumbering()
    {
        var wiki = ToWiki("""
            1. One

            2. Two
            """);

        Assert.Equal("# One\n# Two", wiki);
    }

    [Fact]
    public void IndentedLines_AreCodeOnlyOnTheirOwn()
    {
        var code = ToWiki("""
            Paragraph.

                var x = 1;
                var y = 2;

            End.
            """);

        Assert.Equal("Paragraph.\n\n{code}\nvar x = 1;\nvar y = 2;\n{code}\n\nEnd.", code);

        var item = ToWiki("""
            - Item
                Continuation
            """);

        Assert.Equal("* Item Continuation", item);
    }

    [Fact]
    public void Monospace_IsBraceEscapedWhereJiraNeedsIt()
    {
        Assert.Equal("{{{}*History{}}}", ToWiki("`*History`"));
        Assert.Equal("""{{{}{ "a": 1 }{}}}""", ToWiki("""`{ "a": 1 }`"""));
        Assert.Equal("{{simple}}", ToWiki("`simple`"));
    }

    [Fact]
    public void Backslashes_KeepGuardingJiraMarkup()
    {
        Assert.Equal(@"\*not bold\*", ToWiki(@"\*not bold\*"));
        Assert.Equal("Full stop.", ToWiki(@"Full stop\."));
    }

    [Fact]
    public void Images_Strikethrough_AndBoldItalic_AreTranslated()
    {
        Assert.Equal("!picture.png!", ToWiki("![A picture](picture.png)"));
        Assert.Equal("-gone-", ToWiki("~~gone~~"));
        Assert.Equal("*_bold italic_*", ToWiki("***bold italic***"));
    }

    [Fact]
    public void TableCells_KeepAnEscapedPipe()
    {
        Assert.Equal(@"|a \| b|c|", ToWiki(@"| a \| b | c |"));
    }

    [Fact]
    public void UnclosedFences_AreClosedAnyway()
    {
        var wiki = ToWiki("```cs\nvar x = 1;");

        Assert.Equal("{code:cs}\nvar x = 1;\n{code}", wiki);
    }

    [Fact]
    public void JiraMarkupInProse_IsEscaped()
    {
        Assert.Equal(@"\h2. Not a heading", ToWiki("h2. Not a heading"));
        Assert.Equal(@"\{code} in prose", ToWiki("{code} in prose"));
        Assert.Equal(@"\-strikethrough\-", ToWiki("-strikethrough-"));
    }

    [Fact]
    public void OrdinaryProse_IsNotEscaped()
    {
        const string text = "end-to-end, customfield_10682 and 2 * 3 = 6 — really?";

        Assert.Equal(text, ToWiki(text));
    }

    [Fact]
    public void HardLineBreaks_AreFoldedLikeSoftOnes()
    {
        Assert.Equal("line one line two", ToWiki("line one  \nline two"));
    }

    [Fact]
    public void UnsupportedMarkdown_PassesThroughUntranslated()
    {
        Assert.Equal("<b>html</b>", ToWiki("<b>html</b>"));
        Assert.Equal("Heading ===", ToWiki("Heading\n==="));
        Assert.Equal(@"Text\[^1]", ToWiki("Text[^1]"));
        Assert.Equal("{quote}> deep{quote}", ToWiki("> > deep"));
    }
}
