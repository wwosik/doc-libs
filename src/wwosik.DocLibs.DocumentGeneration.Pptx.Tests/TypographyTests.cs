using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Typography in decks: <c>typography: true</c> or <see cref="PptxOptions.Typography"/>.</summary>
public class TypographyTests
{
    private const string Markdown = """
        # "Deck" title

        ## Section

        "Hello" -- it's 10 km...

        Code `"x" -- y` and [a "link"](https://example.com/a--b) stay, as do 2026-11-14 and 2020-2026.

        ## Table

        | Cell | Status |
        | ---- | ------ |
        | "quoted" | `Done` |
        """;

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void FrontMatter_SetsQuotesDashesEllipsisAndNoBreakSpaces_ButNotCode_Addresses_OrDates(string template)
    {
        var pptx = Samples.Generate(
            Samples.Markdown("---\ntypography: true\nlabels.success: Done\n---\n\n" + Markdown),
            new PptxOptions { Template = Samples.BuiltIn(template) });
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var text = Text(package);

        Assert.Contains("“Deck” title", text, StringComparison.Ordinal);
        Assert.Contains("“Hello” – it’s 10 km…", text, StringComparison.Ordinal);
        Assert.Contains("\"x\" -- y", text, StringComparison.Ordinal);
        Assert.Contains("a “link”", text, StringComparison.Ordinal);
        Assert.Contains("2026-11-14 and 2020-2026", text, StringComparison.Ordinal);
        Assert.Contains("“quoted”", text, StringComparison.Ordinal);
        Assert.Contains(Samples.Slides(package).SelectMany(s => s.HyperlinkRelationships), h => h.Uri == new Uri("https://example.com/a--b"));
    }

    [Fact]
    public void Off_ByDefault_AndTheKeyWinsOverTheOption()
    {
        static string Generated(string markdown, PptxOptions? options = null)
        {
            using var package = Samples.Open(Samples.Generate(Samples.Markdown(markdown), options));
            return Text(package);
        }

        Assert.Contains("\"Hello\" -- it's", Generated(Markdown), StringComparison.Ordinal);
        Assert.Contains("\"Hello\" -- it's", Generated("---\ntypography: false\n---\n\n" + Markdown, new PptxOptions { Typography = true }), StringComparison.Ordinal);
        Assert.Contains("“Hello”", Generated(Markdown, new PptxOptions { Typography = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void German_FollowsTheLanguage_AndTheTitleInThePropertiesIsTypeset()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(
            "---\ntypography: true\nlang: de\ntitle: \"Plan\" 2027\n---\n\n## S\n\n\"Hallo\" - z. B. am 13. November\n")));

        Assert.Contains("„Hallo“ – z. B. am 13. November", Text(package), StringComparison.Ordinal);
        Assert.Equal("„Plan“ 2027", package.PackageProperties.Title);
    }

    [Fact]
    public void ACharacterTheFontsLack_StaysAsTyped_WithANote()
    {
        var notes = new GenerationNotes();
        var typography = Typography.For(true, "en", new Dictionary<string, TypographyRules>(), c => c != '“', notes);

        var slides = DeckParser.Parse(Samples.Markdown("## S\n\nSay \"hi\" -- now.\n"), new PptxOptions(), notes: notes, typography: typography);

        Assert.Contains(slides.Single().AllRuns(), r => r.Text == "Say \"hi” – now.");
        Assert.Equal(NoteCodes.TypographySkipped, Assert.Single(notes.Result(0).Notes).Code);
    }

    private static string Text(DocumentFormat.OpenXml.Packaging.PresentationDocument package) => string.Join(
        "\n",
        Samples.Slides(package).Select(s => string.Join("\n", s.Slide!.Descendants<A.Paragraph>().Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text))))));
}
