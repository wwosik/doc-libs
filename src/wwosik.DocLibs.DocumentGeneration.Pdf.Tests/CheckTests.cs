using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary><see cref="PdfGenerator.Check"/>: the style rules, image sources, errors as a result, pages, and no rendering.</summary>
public class CheckTests
{
    private static readonly PdfOptions Drawing = new() { CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }], TimeProvider = Samples.FixedTime };

    public static TheoryData<string> SampleFiles => ["report.md", "long.md", "layouts.md", "schedules.md", "steps-and-risks.md"];

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Check_ReturnsEveryNoteOfGenerate_AndItsPages_WithoutRendering(string name)
    {
        var document = Samples.Document(name);
        using var output = new MemoryStream();
        var generated = PdfGenerator.Generate(document, output, Drawing);
        var renders = PdfGenerator.Renders;

        var check = PdfGenerator.Check(document, Drawing);

        Assert.Equal(renders, PdfGenerator.Renders);
        Assert.Null(check.Error);
        Assert.Equal(generated.Pages, check.Statistics.Pages);
        Assert.Empty(check.Statistics.SlidesByKind);
        Assert.Null(check.Statistics.TalkTime);
        Assert.All(generated.Notes, note => Assert.Contains(note, check.Notes));
        Assert.All(check.Notes.Except(generated.Notes), note => Assert.Contains(note.Code, NoteCodes.CheckOnly));
    }

    [Fact]
    public void Report_GoldenResult()
    {
        Assert.Equal(GoldenReport, PdfGenerator.Check(Samples.Document("report.md"), Drawing).ToString());
    }

    [Fact]
    public void LongParagraph_FiresAboveTheLimit_AtItsLine_AfterFrontMatter()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 150));
        var markdown = $"---\ntitle: T\n---\n# Report\n\n{words}\n\n{words} more\n\n| A |\n| - |\n| {words} more |\n";

        var note = Assert.Single(PdfGenerator.Check(Samples.Markdown(markdown)).Notes);

        Assert.Equal((NoteCodes.LongParagraph, 8), (note.Code, note.Line));
        Assert.Equal("The paragraph 'word word word word word word word word…' has 151 words; keep paragraphs to 150.", note.Message);
        Assert.Empty(PdfGenerator.Check(Samples.Markdown(markdown), new PdfOptions { Checks = new CheckOptions { MaxWordsPerParagraph = null } }).Notes);
    }

    [Fact]
    public void TitleTooLong_HoldsForEveryHeading()
    {
        var title = new string('x', 61);
        var markdown = $"# Report\n\n## Short\n\nText.\n\n#### {title}\n\nText.\n";

        var note = Assert.Single(PdfGenerator.Check(Samples.Markdown(markdown)).Notes);

        Assert.Equal((NoteCodes.TitleTooLong, 7), (note.Code, note.Line));
        Assert.StartsWith("The heading 'xxx", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeckRules_DoNotApply()
    {
        var markdown = "# Report\n\n## Many\n\n" + string.Concat(Enumerable.Range(1, 9).Select(i => $"- item {i}\n"));

        Assert.True(PdfGenerator.Check(Samples.Markdown(markdown)).IsClean);
    }

    [Fact]
    public void MissingImagesAndAltText_AreAllReported_AndTheLayoutGoesOn()
    {
        var markdown = "# Report\n\n![](missing-1.png)\n\nText.\n\n![Second](https://example.org/b.png)\n\n![Fine](photo.png)\n";

        var check = PdfGenerator.Check(Samples.Markdown(markdown, Samples.Image("photo.png")));

        Assert.Null(check.Error);
        Assert.Equal([(NoteCodes.AltTextMissing, 3), (NoteCodes.ImageSource, 3), (NoteCodes.ImageSource, 7)], check.Notes.Select(n => (n.Code, n.Line)));
        Assert.True(check.Statistics.Pages > 0);
        Assert.Equal(3, check.Statistics.Images);
    }

    [Fact]
    public void ADocumentError_IsReturned_NotThrown()
    {
        var check = PdfGenerator.Check(Samples.Markdown("# Report\n\n::: unknown\nText.\n:::\n"));

        Assert.Contains("unknown", check.Error, StringComparison.Ordinal);
        Assert.Equal(0, check.Statistics.Pages);
        Assert.Equal(2, check.Statistics.Words);
    }

    [Fact]
    public void TheGuide_ListsTheLimits()
    {
        var guide = PdfGenerator.AuthoringGuide();

        Assert.Contains("## Limits", guide, StringComparison.Ordinal);
        Assert.Contains("at most 150 words in a paragraph", guide, StringComparison.Ordinal);
        Assert.Contains("at most 60 characters in a heading", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("bullets on a slide", guide, StringComparison.Ordinal);
        Assert.Contains("`long-paragraph`", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("`too-many-bullets`", guide, StringComparison.Ordinal);
    }

    private const string GoldenReport = """
        - line 56: The mermaid diagram cannot be drawn, so it stays code: Invalid mermaid header: "graph TD; A-->B;". Expected "graph TD", "flowchart LR", etc.
        4 pages, 115 words, 1 images, 1 tables, 0 diagrams.
        """;
}
