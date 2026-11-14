using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>What a PDF reports back: the pages written, and what was done to the Markdown.</summary>
public class GenerationNoteTests
{
    private static (GenerationResult Result, byte[] Pdf) Generate(string markdown, PdfOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown(markdown), output, (options ?? new PdfOptions()) with { TimeProvider = Samples.FixedTime });
        return (result, output.ToArray());
    }

    [Fact]
    public void AnOrdinaryDocument_HasNoNotes_AndCountsItsPages()
    {
        var (result, pdf) = Generate("# Report\n\nIntro.\n\n# Chapter\n\nText.\n");

        Assert.False(result.HasNotes, result.ToString());
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(document.NumberOfPages, result.Pages);
        Assert.True(result.Pages >= 2);
    }

    [Fact]
    public void RawHtml_ShownAsText_IsReported_WithItsLine_CommentsNot()
    {
        var (result, _) = Generate("---\ntitle: T\n---\n## Html\n\n<div>Box</div>\n\nA <b>bold</b> word.\n\n<!-- a comment -->\n");

        Assert.Equal([(NoteCodes.HtmlAsText, 6), (NoteCodes.HtmlAsText, 8)], result.Notes.Select(n => (n.Code, n.Line)).Distinct());
    }

    [Fact]
    public void AnUnsupportedMermaidType_IsReported()
    {
        var (result, _) = Generate("Text.\n\n```mermaid\njourney\n  title My day\n```\n", new PdfOptions { CodeBlockRenderers = [new MermaidRenderer()] });

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.CodeNotDrawn, note.Code);
        Assert.Equal(3, note.Line);
    }

    [Fact]
    public void ReplacedCharacters_AreReported_OncePerCharacter()
    {
        var (result, _) = Generate("Done 🎉 and 🎉 again, and ✨.\n", new PdfOptions { ReplaceUnsupported = _ => "*" });

        Assert.All(result.Notes, n => Assert.Equal(NoteCodes.CharacterReplaced, n.Code));
        Assert.Equal(2, result.Notes.Count);
        Assert.Contains(result.Notes, n => n.Message.Contains("'🎉' (U+1F389)", StringComparison.Ordinal) && n.Message.Contains("'*'", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutAReplacementHook_AMissingCharacter_StillThrows()
    {
        Assert.Throws<DocumentConversions.DocumentConversionException>(() => Generate("Done 🎉.\n"));
    }

    [Theory]
    [InlineData("report.md")]
    [InlineData("long.md")]
    public void TheSampleDocuments_GenerateWithoutNotes(string sample)
    {
        // Their diagrams drawn, so that long.md's `Figure:` is a caption.
        using var output = new MemoryStream();
        var diagrams = Samples.Renderer(request => request.Language == "mermaid" ? new ImageGraphic(Samples.Image("diagram.png")) : null);
        var result = PdfGenerator.Generate(Samples.Document(sample), output, new PdfOptions { TimeProvider = Samples.FixedTime, CodeBlockRenderers = [diagrams] });

        Assert.False(result.HasNotes, result.ToString());
    }
}
