using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>What documents read and decks accept (E03-S02): the document keys, <c>{.unnumbered}</c>, caption paragraphs.</summary>
public class DocumentSyntaxTests
{
    private static readonly ICodeBlockRenderer Rectangles = Samples.Renderer(request => request.Language == "mermaid"
        ? new DrawingGraphic(new Drawing(400, 200, [new DrawingShape(DrawingPath.Rectangle(0, 0, 400, 200), "FF0000", null, 0)]))
        : null);

    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, options);
        return (result, output.ToArray());
    }

    private static List<string> Texts(byte[] pptx)
    {
        using var package = Samples.Open(pptx);
        return [.. Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Paragraph>()).Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text))).Where(t => t.Length > 0)];
    }

    [Fact]
    public void TheDocumentKeys_AreAccepted_CheckedAndIgnored()
    {
        const string body = "# Deck\n\n## A\n\nText.\n";
        var (result, with) = Generate("---\ntoc: true\ntocDepth: 3\nheadingNumbers: true\n---\n" + body);
        var (_, without) = Generate(body);

        Assert.False(result.HasNotes, result.ToString());
        Assert.Equal(Texts(without), Texts(with));
        var error = Assert.Throws<DocumentConversionException>(() => Generate("---\ntocDepth: 4\n---\n" + body));
        Assert.Contains("'tocDepth' has the value '4'", error.Message, StringComparison.Ordinal);
        Assert.Throws<DocumentConversionException>(() => Generate("---\ntoc: maybe\n---\n" + body));
    }

    [Fact]
    public void Unnumbered_IsAcceptedAndLeftOut_OnEveryHeading()
    {
        var (result, pptx) = Generate("# Deck\n\n## Preface {.unnumbered}\n\n### Detail {.unnumbered}\n\nText.\n\n### Other {.dark}\n");

        Assert.False(result.HasNotes, result.ToString());
        var texts = Texts(pptx);
        Assert.Contains("Preface", texts);
        Assert.Contains("Detail", texts);
        Assert.Contains("Other {.dark}", texts);
        Assert.DoesNotContain(texts, t => t.Contains("unnumbered", StringComparison.Ordinal));
    }

    [Fact]
    public void ACaptionUnderATableOrADrawnDiagram_LosesItsPrefix_WithoutANumber()
    {
        var markdown = "# Deck\n\n## Sales\n\n| Region | Sales |\n| --- | --: |\n| North | 120 |\n\nTable: Sales by region\n\n"
            + "## Flow\n\n```mermaid\ngraph TD\n```\n\nFigure: The flow\n";
        var (result, pptx) = Generate(markdown, new PptxOptions { CodeBlockRenderers = [Rectangles] });
        Samples.WriteForReview("captions.pptx", pptx);

        Samples.AssertValid(pptx);
        Assert.False(result.HasNotes, result.ToString());
        var texts = Texts(pptx);
        Assert.Contains("Sales by region", texts);
        Assert.Contains("The flow", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("Table", StringComparison.Ordinal) || t.StartsWith("Figure", StringComparison.Ordinal));
    }

    [Fact]
    public void ACaptionParagraph_ThatFollowsNothingItCaptions_StaysText_WithANote()
    {
        var (result, pptx) = Generate("# Deck\n\n## A\n\nTable: Not under a table\n\n```mermaid\ngraph TD\n```\n\nFigure: Not drawn\n");

        Assert.Equal([(NoteCodes.CaptionIgnored, 5), (NoteCodes.CaptionIgnored, 11)], result.Notes.Select(n => (n.Code, n.Line)));
        Assert.Contains("Table: Not under a table", Texts(pptx));
        Assert.Contains("Figure: Not drawn", Texts(pptx));
    }
}
