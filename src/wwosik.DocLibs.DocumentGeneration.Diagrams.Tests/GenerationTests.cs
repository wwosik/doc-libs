using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

public class GenerationTests
{
    [Theory]
    [MemberData(nameof(Samples.Brands), MemberType = typeof(Samples))]
    public void Pdf_DrawsEveryDiagram_AndLeavesOtherTypesAsCode(string brand)
    {
        var pdf = Samples.Pdf(Samples.Document("diagrams.md"), Samples.Named(brand));
        Samples.WriteForReview($"diagrams-{brand}.pdf", pdf);

        using var document = PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(p => p.Text));
        Assert.Contains("Markdown document", text, StringComparison.Ordinal);
        Assert.Contains("journey", text, StringComparison.Ordinal);
        Assert.DoesNotContain("flowchart TD", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_DiagramText_IsInTheTableFont()
    {
        using var document = PdfDocument.Open(Samples.Pdf(Samples.Markdown("## A\n\nBody\n\n```mermaid\nflowchart LR\n  A[Start] --> B[Stop]\n```\n"), Samples.Brand));

        var words = document.GetPage(1).GetWords().ToList();
        Assert.All(words.Where(w => w.Text is "Start" or "Stop").SelectMany(w => w.Letters), l => Assert.Contains("PTSans-Narrow", l.FontName, StringComparison.Ordinal));
        Assert.Equal(2, words.Count(w => w.Text is "Start" or "Stop"));
        Assert.Contains("LiberationSerif", words.Single(w => w.Text == "Body").Letters[0].FontName, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_RendererErrors_Propagate_OrKeepTheCode()
    {
        var markdown = Samples.Markdown("---\ntitle: T\n---\n## A\n\n```mermaid\nnot mermaid\n```\n");

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Pdf(markdown, Brand.Neutral));
        using var kept = PdfDocument.Open(Samples.Pdf(markdown, Brand.Neutral, new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }));

        Assert.IsType<ArgumentException>(error.InnerException);
        Assert.Contains("at line 6", error.Message, StringComparison.Ordinal); // front matter counted
        Assert.Contains("not mermaid", kept.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Samples.Brands), MemberType = typeof(Samples))]
    public void Pptx_DrawsEveryDiagramAsAValidGroupOfShapes(string brand)
    {
        var pptx = Samples.Pptx(Samples.Document("diagrams.md"), Samples.Named(brand));
        Samples.WriteForReview($"diagrams-{brand}.pptx", pptx);

        using var package = PresentationDocument.Open(new MemoryStream(pptx), false);
        Assert.Empty(new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package).Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}"));
        var groups = Slides(package).SelectMany(s => s.Descendants<P.GroupShape>()).Where(g => g.Parent is P.ShapeTree).ToList();
        Assert.Equal(8, groups.Count);
    }

    [Fact]
    public void Pptx_UsesPresetShapes_CustomGeometry_AndTheTableFamily()
    {
        var pptx = Samples.Pptx(Samples.Markdown("## A\n\n```mermaid\nflowchart LR\n  A[Start] --> B((Stop))\n```\n"), Samples.Brand);

        using var package = PresentationDocument.Open(new MemoryStream(pptx), false);
        var group = Slides(package).SelectMany(s => s.Descendants<P.GroupShape>()).Single();
        var presets = group.Descendants<A.PresetGeometry>().Select(g => g.Preset!.Value).ToList();
        Assert.Contains(A.ShapeTypeValues.RoundRectangle, presets);
        Assert.Contains(A.ShapeTypeValues.Ellipse, presets);
        Assert.NotEmpty(group.Descendants<A.CustomGeometry>()); // the edge and its arrowhead
        var runs = group.Descendants<A.Run>().ToList();
        Assert.Equal(["Start", "Stop"], runs.Select(r => r.Text!.Text));
        Assert.All(runs, r => Assert.Equal("PT Sans Narrow", r.RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value));
    }

    [Fact]
    public void Pptx_WithoutTableFonts_TextKeepsTheThemeFont()
    {
        var pptx = Samples.Pptx(Samples.Markdown("## A\n\n```mermaid\nflowchart LR\n  A[Start] --> B[Stop]\n```\n"), Brand.Neutral);

        using var package = PresentationDocument.Open(new MemoryStream(pptx), false);
        Assert.Empty(Slides(package).SelectMany(s => s.Descendants<P.GroupShape>()).SelectMany(g => g.Descendants<A.LatinFont>()));
    }

    private static IEnumerable<P.Slide> Slides(PresentationDocument package) =>
        package.PresentationPart!.SlideParts.Select(p => p.Slide!);
}
