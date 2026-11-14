using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Typography in PDFs: <c>typography: true</c> or <see cref="PdfOptions.Typography"/>.</summary>
public class TypographyTests
{
    private const string Markdown = """
        # "Cover" title

        ## Section

        "Hello" -- it's 10 km...

        Code `"x" -- y` and [a "link"](https://example.com/a--b) stay, as do 2026-11-14 and 2020-2026.

        | Cell |
        | ---- |
        | "quoted" |

        ```text
        "block" -- code
        ```
        """;

    [Fact]
    public void FrontMatter_SetsQuotesDashesEllipsisAndNoBreakSpaces_ButNotCode_Addresses_OrDates()
    {
        var pdf = Samples.Generate(Samples.Markdown("---\ntypography: true\n---\n\n" + Markdown));
        var text = Text(pdf);

        Assert.Contains("“Cover” title", text, StringComparison.Ordinal);
        Assert.Contains("“Hello” – it’s 10 km…", text, StringComparison.Ordinal);
        Assert.Contains("\"x\" -- y", text, StringComparison.Ordinal);
        Assert.Contains("a “link”", text, StringComparison.Ordinal);
        Assert.Contains("2026-11-14 and 2020-2026", text, StringComparison.Ordinal);
        Assert.Contains("“quoted”", text, StringComparison.Ordinal);
        Assert.Contains("\"block\" -- code", text, StringComparison.Ordinal);
        Assert.Contains("https://example.com/a--b", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Off_ByDefault_AndTheKeyWinsOverTheOption()
    {
        Assert.Contains("\"Hello\" -- it's", Text(Samples.Generate(Samples.Markdown(Markdown))), StringComparison.Ordinal);
        Assert.Contains("\"Hello\" -- it's", Text(Samples.Generate(Samples.Markdown("---\ntypography: false\n---\n\n" + Markdown), new PdfOptions { Typography = true })), StringComparison.Ordinal);
        Assert.Contains("“Hello”", Text(Samples.Generate(Samples.Markdown(Markdown), new PdfOptions { Typography = true })), StringComparison.Ordinal);
    }

    [Fact]
    public void German_FollowsTheLanguage()
    {
        var pdf = Samples.Generate(Samples.Markdown("---\ntypography: true\nlang: de-CH\n---\n\n\"Hallo\" - z. B. am 13. November\n"));

        Assert.Contains("„Hallo“ – z. B. am 13. November", Text(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void AHostsRules_AreUsedForTheirLanguage()
    {
        var options = new PdfOptions
        {
            Typography = true,
            Language = "fr",
            TypographyRules = new Dictionary<string, TypographyRules> { ["fr"] = TypographyRules.English with { DoubleQuotes = ("« ", " »") } },
        };

        Assert.Contains("« Bonjour »", Text(Samples.Generate(Samples.Markdown("\"Bonjour\"\n"), options)), StringComparison.Ordinal);
    }

    [Fact]
    public void ACharacterTheFontsLack_StaysAsTyped_WithANote()
    {
        var notes = new GenerationNotes();
        var typography = Typography.For(true, "en", new Dictionary<string, TypographyRules>(), c => c != '“', notes);

        var model = MarkdownParser.Parse(Samples.Markdown("Say \"hi\" -- now.\n"), new PdfOptions(), notes: notes, typography: typography);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(model.Blocks));
        Assert.Equal("Say \"hi” – now.", string.Concat(paragraph.Runs.Select(r => r.Text)));
        var note = Assert.Single(notes.Result(0).Notes);
        Assert.Equal((NoteCodes.TypographySkipped, 1), (note.Code, note.Line));
        Assert.Contains("'“' (U+201C)", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTitleInTheFooterAndProperties_IsTypeset()
    {
        var pdf = Samples.Generate(Samples.Markdown("---\ntypography: true\ntitle: \"Plan\" -- 2027\n---\n\n## A\n\nText.\n"));

        using var document = PdfDocument.Open(pdf);
        Assert.Equal("“Plan” – 2027", document.Information.Title);
    }

    [Fact]
    public void DiagramsGetTheCodeAsTyped()
    {
        string? code = null;
        var options = new PdfOptions { Typography = true, CodeBlockRenderers = [Samples.Renderer(r => { code = r.Code; return null; })] };

        Samples.Generate(Samples.Markdown("```mermaid\ngraph LR\n  A[\"it's -- here...\"] --> B\n```\n"), options);

        Assert.Equal("graph LR\n  A[\"it's -- here...\"] --> B", code?.TrimEnd('\n'));
    }

    [Fact]
    public void WithTypography_RoundTripsThroughThePdfConverter()
    {
        var pdf = Samples.Generate(Samples.Markdown(Markdown), new PdfOptions { Typography = true });
        Samples.WriteForReview("typography.pdf", pdf);

        Samples.AssertMatchesExpected("typography.roundtrip", Samples.RoundTrip(pdf));
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => string.Concat(p.Letters.Select(l => l.Value))));
    }
}
