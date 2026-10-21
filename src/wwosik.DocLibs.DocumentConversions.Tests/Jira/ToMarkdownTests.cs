using wwosik.DocLibs.DocumentConversions.Jira;

namespace wwosik.DocLibs.DocumentConversions.Tests.Jira;

public class ToMarkdownTests
{
    private static string ToMarkdown(string wiki, int topLevel = 1, Func<string, string?>? attachment = null) =>
        JiraWikiMarkup.ToMarkdown(wiki, new JiraMarkdownOptions { TopLevel = topLevel, ResolveAttachment = attachment });

    [Fact]
    public void Headings_MoveBelowTheirSection()
    {
        Assert.Equal("## Title", ToMarkdown("h2. Title"));
        Assert.Equal("### Title", ToMarkdown("h1. Title", topLevel: 3));
        Assert.Equal("###### Deep", ToMarkdown("h5. Deep", topLevel: 4));
    }

    [Fact]
    public void DefaultOptions_KeepTheLevels()
    {
        Assert.Equal("## Title", JiraWikiMarkup.ToMarkdown("h2. Title"));
        Assert.Equal(string.Empty, JiraWikiMarkup.ToMarkdown(null));
    }

    [Fact]
    public void Lists_KeepTheirNesting()
    {
        var markdown = ToMarkdown("""
            * First
            ** Deeper
            *# Numbered inside
            # One
            # Two
            - Dash
            """);

        Assert.Equal("- First\n  - Deeper\n  1. Numbered inside\n1. One\n1. Two\n- Dash", markdown);
    }

    [Fact]
    public void Emphasis_FollowsTheWordBoundaries()
    {
        Assert.Equal("**bold** and *italic* and ~~gone~~", ToMarkdown("*bold* and _italic_ and -gone-"));
        Assert.Equal("end-to-end", ToMarkdown("end-to-end"));
        Assert.Equal("customfield_10682", ToMarkdown("customfield_10682"));
        Assert.Equal("underlined", ToMarkdown("+underlined+"));
        Assert.Equal("**Preconditions:**", ToMarkdown("+*Preconditions:*+"));
    }

    [Fact]
    public void ForcedMarkers_FromTheEditor_BecomeEmphasis()
    {
        Assert.Equal("Tab \\`**x**\\`s", ToMarkdown("Tab `{*}x{*}`s"));
        Assert.Equal("un**bold**ed word", ToMarkdown("un{*}bold{*}ed word"));
    }

    [Fact]
    public void Monospace_BecomesACodeSpan()
    {
        Assert.Equal("`doc-libs`", ToMarkdown("{{doc-libs}}"));
        Assert.Equal("`a*b`", ToMarkdown("{{{}a*b{}}}"));
        Assert.Equal("Value `NONE` set", ToMarkdown("Value {{{color:#ff8b00}NONE{color}}} set"));
    }

    [Fact]
    public void CodeAndNoformat_BecomeFences()
    {
        Assert.Equal("```java\nvar x = 1;\n```", ToMarkdown("{code:java}\nvar x = 1;\n{code}"));
        Assert.Equal("```sql\nselect 1\n```", ToMarkdown("{code:title=a.sql|language=sql}select 1{code}"));
        Assert.Equal("```\n*no* _markup_\n```", ToMarkdown("{noformat}*no* _markup_{noformat}"));
    }

    [Fact]
    public void Code_KeepsBlankLinesAndBraces()
    {
        var markdown = ToMarkdown("Before\r\n{code:java}\r\n{\r\n\r\n  \"a\": 1\r\n}   {code}\r\nAfter");

        Assert.Equal("Before\n\n```java\n{\n\n  \"a\": 1\n}\n```\n\nAfter", markdown);
    }

    [Fact]
    public void Quotes_BecomeQuoteBlocks()
    {
        Assert.Equal("> Quote", ToMarkdown("{quote}Quote{quote}"));
        Assert.Equal("> Short", ToMarkdown("bq. Short"));
        Assert.Equal("> First\n>\n> - Item", ToMarkdown("{quote}\nFirst\n\n* Item\n{quote}"));
    }

    [Fact]
    public void Tables_GetAHeaderRow()
    {
        Assert.Equal(
            "| Field | Id |\n| --- | --- |\n| Epic Link | 10682 |",
            ToMarkdown("||Field||Id||\n|Epic Link|10682|"));

        Assert.Equal(
            "|  |  |\n| --- | --- |\n| a | b |",
            ToMarkdown("|a|b|"));
    }

    [Fact]
    public void TableCells_KeepLinksAndEscapeBars()
    {
        Assert.Equal(
            "| [Manual](https://example.org) | `a\\|b` |\n| --- | --- |",
            ToMarkdown("||[Manual|https://example.org]||{{a|b}}||"));
    }

    [Fact]
    public void Links_TakeTheMarkdownForm()
    {
        Assert.Equal("[Manual](https://example.org)", ToMarkdown("[Manual|https://example.org]"));
        Assert.Equal("<https://example.org>", ToMarkdown("[https://example.org]"));
        Assert.Equal("see ABC-5260", ToMarkdown("see [ABC-5260]"));
        Assert.Equal("Hello @U12345", ToMarkdown("Hello [~U12345]"));
        Assert.Equal("\\[optional]", ToMarkdown("[optional]"));
    }

    [Fact]
    public void Images_PointAtTheAttachment()
    {
        static string? Resolve(string name) => name == "picture.png" ? "https://jira/secure/attachment/1/picture.png" : null;

        Assert.Equal(
            "![picture.png](https://jira/secure/attachment/1/picture.png)",
            ToMarkdown("!picture.png|thumbnail!", attachment: Resolve));

        Assert.Equal("Attention! Please!", ToMarkdown("Attention! Please!"));
    }

    [Fact]
    public void UnknownMacros_LoseTheirMarkup_EscapedBracesStay()
    {
        Assert.Equal("Field annual quantities", ToMarkdown("Field {color:#ff8b00}annual quantities{color}"));
        Assert.Equal("Content", ToMarkdown("{panel:title=Note}Content{panel}"));
        Assert.Equal("GET /api/v1/Request/{id}", ToMarkdown(@"GET /api/v1/Request/\{id}"));
    }

    [Fact]
    public void LineBreaks_AndRules()
    {
        Assert.Equal("One\nTwo", ToMarkdown(@"One\\Two"));
        Assert.Equal("Before\n\n---\n\nAfter", ToMarkdown("Before\n----\nAfter"));
    }

    [Fact]
    public void ProseThatLooksLikeMarkdown_IsEscaped()
    {
        Assert.Equal("\\# no title", ToMarkdown(@"\# no title"));
        Assert.Equal("2026\\. was a year", ToMarkdown("2026. was a year"));
        Assert.Equal("a \\* b and \\<b> and \\`x\\`", ToMarkdown("a \\* b and <b> and `x`"));
    }

    [Fact]
    public void Description_WithCrlfNonBreakingSpacesAndAGluedTable()
    {
        const string wiki =
            "The dummy values are to be replaced according to the table below.\r\n"
            + "||Division||Meter type||2027||\r\n"
            + "|Power|RLM|0,31|\r\n"
            + "|\u00A0|SLP|0,16|\r\n"
            + "\r\n"
            + "Value is in E/MWh\r\n"
            + "h2. Note:\r\n"
            + " * Applies to the B2B route only\r\n"
            + " * Values must be reviewed every 6 to 12 months (task assigned to the operations team)\r\n"
            + "\r\n"
            + "\u00A0\r\n"
            + "\r\n"
            + "\u00A0";

        const string expected = """
            The dummy values are to be replaced according to the table below.

            | Division | Meter type | 2027 |
            | --- | --- | --- |
            | Power | RLM | 0,31 |
            |  | SLP | 0,16 |

            Value is in E/MWh

            #### Note:

            - Applies to the B2B route only
            - Values must be reviewed every 6 to 12 months (task assigned to the operations team)
            """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), ToMarkdown(wiki, topLevel: 3));
    }

    [Fact]
    public void TestStep_WithArrowsThatAreNoStrikethrough()
    {
        const string wiki =
            "+*Check years:*+\nCheck for which years YearlyProfiles were created. "
            + "Main year YYYY and - because of the CET->UTC shift - the previous year YYYY-1.";

        Assert.Equal(
            "**Check years:**\nCheck for which years YearlyProfiles were created. "
            + "Main year YYYY and - because of the CET->UTC shift - the previous year YYYY-1.",
            ToMarkdown(wiki, topLevel: 4));
    }

    [Theory]
    [InlineData("## Acceptance criteria")]
    [InlineData("**bold** and *italic*")]
    [InlineData("~~gone~~ and `doc-libs`")]
    [InlineData("[Manual](https://example.org)")]
    [InlineData("- First\n  - Deeper\n- Second")]
    [InlineData("1. One\n1. Two")]
    [InlineData("> Quote")]
    [InlineData("---")]
    [InlineData("```cs\nvar x = 1;\n```")]
    [InlineData("| Field | Id |\n| --- | --- |\n| Epic Link | 10682 |")]
    [InlineData("A paragraph with accents: naïve café, façade, smörgåsbord.")]
    public void RoundTrip_ThroughFromMarkdown_KeepsTheSharedSubset(string markdown)
    {
        Assert.Equal(markdown, JiraWikiMarkup.ToMarkdown(JiraWikiMarkup.FromMarkdown(markdown)));
    }
}
