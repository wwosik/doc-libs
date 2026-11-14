using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>Typography in Word documents: <c>typography: true</c> or <see cref="DocxOptions.Typography"/>.</summary>
public class TypographyTests
{
    private const string Markdown = """
        # "Report" title

        A "subtitle" -- here.

        ## Section

        "Hello" -- it's 10 km...

        Code `"x" -- y` and [a "link"](https://example.com/a--b) stay, as do 2026-11-14 and 2020-2026.

        | Cell |
        | ---- |
        | "quoted" |

        Footnote[^1].

        [^1]: A "note".
        """;

    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void FrontMatter_SetsQuotesDashesEllipsisAndNoBreakSpaces_ButNotCode_Addresses_OrDates(string template)
    {
        var docx = Samples.Generate(
            Samples.Markdown("---\ntypography: true\nauthor: \"Team\" -- North\n---\n\n" + Markdown),
            new DocxOptions { Template = Samples.Template(template) });
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);
        var main = package.MainDocumentPart!;
        var text = Text(main.Document!.Body!);

        Assert.Contains("“Report” title", text, StringComparison.Ordinal);
        Assert.Contains("A “subtitle” – here.", text, StringComparison.Ordinal);
        Assert.Contains("“Team” – North", text, StringComparison.Ordinal);
        Assert.Contains("“Hello” – it’s 10 km…", text, StringComparison.Ordinal);
        Assert.Contains("\"x\" -- y", text, StringComparison.Ordinal);
        Assert.Contains("a “link”", text, StringComparison.Ordinal);
        Assert.Contains("2026-11-14 and 2020-2026", text, StringComparison.Ordinal);
        Assert.Contains("“quoted”", text, StringComparison.Ordinal);
        Assert.Contains("A “note”.", Text(main.FootnotesPart!.Footnotes!), StringComparison.Ordinal);
        Assert.Contains(main.HyperlinkRelationships, h => h.Uri == new Uri("https://example.com/a--b"));
        Assert.Equal("“Team” – North", package.PackageProperties.Creator);
    }

    [Fact]
    public void Off_ByDefault_AndTheKeyWinsOverTheOption()
    {
        static string Generated(string markdown, DocxOptions? options = null)
        {
            using var package = Samples.Open(Samples.Generate(Samples.Markdown(markdown), options));
            return Text(package.MainDocumentPart!.Document!.Body!);
        }

        Assert.Contains("\"Hello\" -- it's", Generated(Markdown), StringComparison.Ordinal);
        Assert.Contains("\"Hello\" -- it's", Generated("---\ntypography: false\n---\n\n" + Markdown, new DocxOptions { Typography = true }), StringComparison.Ordinal);
        Assert.Contains("“Hello”", Generated(Markdown, new DocxOptions { Typography = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void German_FollowsTheLanguage()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\ntypography: true\nlang: de\n---\n\n\"Hallo\" - z. B. am 13. November\n")));

        Assert.Contains("„Hallo“ – z. B. am 13. November", Text(package.MainDocumentPart!.Document!.Body!), StringComparison.Ordinal);
    }

    private static string Text(DocumentFormat.OpenXml.OpenXmlElement root) =>
        string.Join("\n", root.Descendants<Paragraph>().Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))));
}
