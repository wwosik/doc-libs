using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Footnotes, task lists, definition lists and the document language on slides.</summary>
public class MarkdownFoundationTests
{
    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, options);
        return (result, output.ToArray());
    }

    // The text box at the foot of the slide that holds its footnotes: the one whose first run is raised.
    private static P.Shape? FootnoteBox(SlidePart slide) => slide.Slide!.Descendants<P.Shape>()
        .FirstOrDefault(s => s.TextBody?.Elements<A.Paragraph>().FirstOrDefault()?.Elements<A.Run>().FirstOrDefault()?.RunProperties?.Baseline?.Value == 30000);

    private static long Top(P.Shape shape) => shape.ShapeProperties?.Transform2D?.Offset?.Y?.Value ?? 0;

    private static long Bottom(P.Shape shape) => Top(shape) + (shape.ShapeProperties?.Transform2D?.Extents?.Cy?.Value ?? 0);

    [Fact]
    public void Footnotes_AreNumberedPerSlide_AndSetAtTheFootOfTheSlideThatReferencesThem()
    {
        var (result, pptx) = Generate(
            "# Deck\n\n## One\n\n- Growth[^g] and costs[^c]\n\n## Two\n\n- Costs again[^c]\n\n[^g]: Source: annual report.\n[^c]: Source: controlling.\n");

        Assert.False(result.HasNotes, result.ToString());
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        var one = FootnoteBox(slides[1]);
        Assert.NotNull(one);
        Assert.Equal(["1 Source: annual report.", "2 Source: controlling."], one.TextBody!.Elements<A.Paragraph>().Select(p => p.InnerText));
        var two = FootnoteBox(slides[2]);
        Assert.Equal("1 Source: controlling.", two!.TextBody!.InnerText);

        // In the body: the references, raised; the body placeholder ends above the footnotes.
        var body = slides[1].Slide!.Descendants<P.Shape>().Single(s => s.InnerText.Contains("Growth", StringComparison.Ordinal));
        Assert.Equal(["1", "2"], body.Descendants<A.Run>().Where(r => r.RunProperties?.Baseline?.Value == 30000).Select(r => r.InnerText));
        Assert.True(Bottom(body) <= Top(one), "the content area leaves room for the footnotes");
    }

    [Fact]
    public void ASplitSlide_CarriesOnEachPart_TheFootnotesThatPartReferences()
    {
        var items = string.Concat(Enumerable.Range(1, 30).Select(i => i switch
        {
            1 => "- Point 1 with a source[^a]\n",
            30 => "- Point 30 with another source[^b]\n",
            _ => $"- Point {i} with a few more words to fill the slide\n",
        }));
        var (result, pptx) = Generate($"## Points\n\n{items}\n[^a]: First source.\n[^b]: Second source.\n");

        Assert.Contains(result.Notes, n => n.Code == NoteCodes.SlideSplit);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.True(slides.Count >= 2);
        Assert.Equal("1 First source.", FootnoteBox(slides[0])!.TextBody!.InnerText);
        Assert.Equal("2 Second source.", FootnoteBox(slides[^1])!.TextBody!.InnerText);
        Assert.All(slides.Skip(1).SkipLast(1), s => Assert.Null(FootnoteBox(s)));
    }

    [Fact]
    public void FootnotesOnAFlowSlide_GoBelowItsBlocks()
    {
        var (_, pptx) = Generate("## Status {kicker=\"Plan\"}\n\n::: panel\nDone[^1].\n:::\n\n[^1]: As of March.\n");

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0];
        Assert.Equal("1 As of March.", FootnoteBox(slide)!.TextBody!.InnerText);
    }

    [Fact]
    public void AReferenceWithoutDefinition_StaysText_AndIsReported()
    {
        var (result, pptx) = Generate("## Slide\n\n- Text[^nowhere]\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.FootnoteMissing, note.Code);
        Assert.Equal(3, note.Line);
        using var package = Samples.Open(pptx);
        Assert.Contains("[^nowhere]", Samples.Slides(package)[0].Slide!.InnerText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("## Tasks\n\n- [ ] Open\n- [x] Done\n- Plain\n")]
    [InlineData("## Tasks\n\nIntro.\n\n![Chart](diagram.png)\n\n- [ ] Open\n- [x] Done\n- Plain\n")]
    public void TaskLists_HaveWingdingsBoxesForBullets_InPlaceholdersAndTextBoxes(string markdown)
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(Samples.Markdown(markdown, Samples.Image("diagram.png")), output);
        var pptx = output.ToArray();

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var paragraphs = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Paragraph>()).ToList();
        string? Bullet(string text) => paragraphs.Single(p => p.InnerText == text).ParagraphProperties?.GetFirstChild<A.CharacterBullet>()?.Char?.Value;
        Assert.Equal("", Bullet("Open"));
        Assert.Equal("", Bullet("Done"));
        Assert.NotEqual("", Bullet("Plain"));
        Assert.Equal("Wingdings", paragraphs.Single(p => p.InnerText == "Open").ParagraphProperties!.GetFirstChild<A.BulletFont>()!.Typeface!.Value);
        Assert.DoesNotContain(paragraphs, p => p.InnerText.Contains("[ ]", StringComparison.Ordinal));
    }

    [Fact]
    public void DefinitionLists_AreRows_TermBoldBesideItsDescription()
    {
        var (_, pptx) = Generate("## Terms\n\nLatency\n:   The time a request takes.\n:   Measured at the edge.\n\nThroughput\n:   Requests per second.\n");

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var shapes = Samples.Slides(package)[0].Slide!.Descendants<P.Shape>().ToList();
        var term = shapes.Single(s => s.InnerText == "Latency");
        var description = shapes.Single(s => s.InnerText.StartsWith("The time", StringComparison.Ordinal));
        Assert.True(term.Descendants<A.RunProperties>().First().Bold?.Value);
        Assert.Equal(["The time a request takes.", "Measured at the edge."], description.Descendants<A.Paragraph>().Select(p => p.InnerText));
        Assert.True(description.ShapeProperties!.Transform2D!.Offset!.X!.Value > term.ShapeProperties!.Transform2D!.Offset!.X!.Value, "beside the term");
        Assert.Contains(shapes, s => s.InnerText == "Throughput");
    }

    [Fact]
    public void EveryRun_CarriesTheDocumentLanguage_DrawingsTablesAndFooterIncluded()
    {
        var drawing = new Drawing(100, 40, [new DrawingText(10, 20, "Label", 12, "000000")]);
        var options = new PptxOptions { CodeBlockRenderers = [Samples.Renderer(_ => new DrawingGraphic(drawing))] };
        var (result, pptx) = Generate("---\nlang: de-CH\n---\n# Deck\n\n## Text\n\n- Punkt\n\n## Table\n\n| A | B |\n| - | - |\n| 1 | 2 |\n\n## Drawing\n\n```x\ny\n```\n", options);

        Assert.False(result.HasNotes, result.ToString());
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var properties = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.TextCharacterPropertiesType>())
            .Where(p => p is A.RunProperties or A.EndParagraphRunProperties).ToList();
        Assert.True(properties.Count > 10);
        Assert.All(properties, p => Assert.Equal("de-CH", p.Language?.Value));
        using var core = package.CoreFilePropertiesPart!.GetStream();
        Assert.Contains("<dc:language>de-CH</dc:language>", new StreamReader(core).ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultLanguage_IsEnglish_AndAnUnknownOneIsReported()
    {
        var (english, plain) = Generate("## Slide\n\n- Point\n");
        var (unknown, _) = Generate("---\nlang: tlh\n---\n## Slide\n\n- Point\n");

        Assert.False(english.HasNotes);
        using var package = Samples.Open(plain);
        Assert.All(Samples.Slides(package)[0].Slide!.Descendants<A.RunProperties>(), p => Assert.Equal("en", p.Language?.Value));
        Assert.Equal(NoteCodes.LanguageUnknown, Assert.Single(unknown.Notes).Code);
    }

    [Fact]
    public void AnInvalidDrawing_Throws()
    {
        var drawing = new Drawing(100, 40, [new DrawingShape(DrawingPath.Rectangle(0, 0, double.NaN, 10), "1F4E79", null, 0)]);
        var options = new PptxOptions { CodeBlockRenderers = [Samples.Renderer(_ => new DrawingGraphic(drawing))] };

        var error = Assert.Throws<ArgumentException>(() => Generate("## Drawing\n\n```x\ny\n```\n", options));
        Assert.Contains("item 0", error.Message, StringComparison.Ordinal);
    }
}
