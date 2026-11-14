using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.Svg;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using static wwosik.DocLibs.DocumentGeneration.Docx.Tests.DocxGeneratorTests;
using Extent = DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent;
using WordDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

public class GraphicsTests
{
    private const string Flowchart = "```mermaid\nflowchart LR\n    A[Request] --> B{Valid?}\n    B -->|yes| C[Process]\n```\n";

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"60\" viewBox=\"0 0 120 60\"><rect x=\"0\" y=\"0\" width=\"120\" height=\"60\" fill=\"#3366CC\"/></svg>";

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Asvg = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static readonly DocxOptions Drawn = new()
    {
        CodeBlockRenderers = [new MermaidRenderer(), new ChartRenderer()],
        ImageConverters = [new SvgImageConverter()],
    };

    public static TheoryData<string, bool> Charts => new()
    {
        { "bar", false }, { "bar", true }, { "line", false }, { "area", false }, { "area", true },
    };

    [Theory]
    [MemberData(nameof(Charts))]
    public void AChart_IsANativeChart_WithItsWorkbook(string type, bool stacked)
    {
        var markdown = $"## Sales\n\n```chart\ntype: {type}\nstacked: {(stacked ? "true" : "false")}\ntitle: Orders\n---\nmonth, plan, actual\nJan, 120, 132\nFeb, 118, 121\n```\n";
        var docx = Generate(markdown, new DocxOptions { CodeBlockRenderers = [new ChartRenderer()] });
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);

        var chart = Assert.Single(package.MainDocumentPart!.ChartParts);
        var space = XDocument.Load(chart.GetStream()).Root!;
        Assert.NotNull(space.Descendants(C + (type == "bar" ? "barChart" : type == "line" ? "lineChart" : "areaChart")).SingleOrDefault());
        Assert.Equal(2, space.Descendants(C + "ser").Count());
        var reference = Body(package).Descendants<WordDrawing>().Single().Descendants().Single(e => e.LocalName == "chart");
        Assert.Equal(package.MainDocumentPart!.GetIdOfPart(chart), reference.GetAttribute("id", R.NamespaceName).Value);

        using var workbook = SpreadsheetDocument.Open(chart.EmbeddedPackagePart!.GetStream(), false);
        var sheet = workbook.WorkbookPart!.Workbook!.Descendants<X.Sheet>().Single();
        Assert.Equal("Sheet1", sheet.Name!.Value);
        var cells = ((WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id!.Value!)).Worksheet!.Descendants<X.Cell>()
            .ToDictionary(c => c.CellReference!.Value!, c => c.InnerText);
        Assert.Equal(("plan", "Jan", "Feb", "132"), (cells["B1"], cells["A2"], cells["A3"], cells["C2"]));
    }

    [Fact]
    public void AChart_IsTheTextWidth_By9To16_InTheBrandColours_WithItsTitleAsAltText()
    {
        var docx = Generate("```chart\ntitle: Orders\n---\nmonth, plan\nJan, 1\n```\n", new DocxOptions { Template = Samples.Template("sample"), CodeBlockRenderers = [new ChartRenderer()] });
        using var package = Samples.Open(docx);

        var drawing = Body(package).Descendants<WordDrawing>().Single();
        var extent = drawing.Descendants<Extent>().Single();
        Assert.Equal((long)(extent.Cx!.Value * 9.0 / 16), extent.Cy!.Value);
        Assert.Equal(TextWidth(package) * 635, extent.Cx!.Value);
        Assert.Equal("Orders", XElement.Parse(drawing.OuterXml).Descendants(Wp + "docPr").Single().Attribute("descr")!.Value);
        Assert.Equal("center", ((Paragraph)drawing.Parent!.Parent!).ParagraphProperties!.Justification!.Val!.ToString());

        var space = XDocument.Load(package.MainDocumentPart!.ChartParts.Single().GetStream()).Root!;
        var brand = ResolvedBrand.Resolve(Samples.Brand);
        Assert.Equal(brand.Diagram.Palette[0], space.Descendants(C + "ser").First().Descendants(A + "srgbClr").First().Attribute("val")!.Value);
        Assert.Equal(brand.TableLine, space.Descendants(C + "majorGridlines").Single().Descendants(A + "srgbClr").Single().Attribute("val")!.Value);
        Assert.Equal(brand.Fonts!.Family, space.Descendants(A + "latin").First().Attribute("typeface")!.Value);
    }

    [Fact]
    public void AChartOnALandscapePage_IsCappedAt80PercentOfTheTextHeight()
    {
        var template = new BrandDocxTemplateOptions { Orientation = DocxOrientation.Landscape };
        using var package = Samples.Open(Generate("```chart\n---\nmonth, plan\nJan, 1\n```\n", new DocxOptions { Template = template, CodeBlockRenderers = [new ChartRenderer()] }));

        var extent = Body(package).Descendants<Extent>().Single();
        var margins = Section(package).GetFirstChild<PageMargin>()!;
        var height = (long)Section(package).GetFirstChild<PageSize>()!.Height!.Value - margins.Top!.Value - margins.Bottom!.Value;
        Assert.Equal((long)(height * 635 * 0.8), extent.Cy!.Value);
        Assert.True(extent.Cy!.Value < extent.Cx!.Value * 9 / 16);
    }

    [Fact]
    public void AChartOnACallerTemplate_TakesTheNeutralColours_AndTheThemeFont_AndChartAsAltText_InTheDocumentLanguage()
    {
        var docx = Generate("---\nlang: de\n---\n```chart\n---\nmonth, plan\nJan, 1\n```\n", new DocxOptions { Template = Samples.Template("custom"), CodeBlockRenderers = [new ChartRenderer()] });
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);

        var space = XDocument.Load(package.MainDocumentPart!.ChartParts.Single().GetStream()).Root!;
        Assert.Equal(ResolvedBrand.Neutral.Diagram.Palette[0], space.Descendants(C + "ser").First().Descendants(A + "srgbClr").First().Attribute("val")!.Value);
        Assert.Empty(space.Descendants(A + "latin"));
        var drawing = XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml);
        Assert.Equal("Diagramm", drawing.Descendants(Wp + "docPr").Single().Attribute("descr")!.Value);
    }

    [Fact]
    public void AChartsSourceAndCaption_FollowIt()
    {
        var docx = Generate("```chart\nsource: Sales report\n---\nmonth, plan\nJan, 1\n```\n\nFigure: Orders by month\n", new DocxOptions { CodeBlockRenderers = [new ChartRenderer()] });
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);

        var paragraphs = Body(package).Elements<Paragraph>().ToList();
        var chart = paragraphs.FindIndex(p => p.Descendants<WordDrawing>().Any());
        Assert.NotNull(paragraphs[chart].ParagraphProperties!.KeepNext);
        Assert.Equal(("Caption", "Source: Sales report"), (Style(paragraphs[chart + 1]), Texts(paragraphs[chart + 1])));
        Assert.Equal(("Caption", "Figure 1: Orders by month"), (Style(paragraphs[chart + 2]), Texts(paragraphs[chart + 2])));
    }

    [Fact]
    public void AMermaidDiagram_IsAPicture_WithItsPngAndSvg_AtItsNaturalSize()
    {
        var (docx, result) = GenerateWithResult(Flowchart, Drawn);
        Samples.AssertValid(docx);
        Assert.Empty(result.Notes);
        using var package = Samples.Open(docx);

        var drawing = XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml);
        var blip = drawing.Descendants(A + "blip").Single();
        var main = package.MainDocumentPart!;
        Assert.Equal("image/png", main.GetPartById(blip.Attribute(R + "embed")!.Value).ContentType);
        var svg = blip.Descendants(Asvg + "svgBlip").Single();
        Assert.Equal("{96DAC541-7B7A-43D3-8B79-37D633B846F1}", svg.Parent!.Attribute("uri")!.Value);
        var svgPart = main.GetPartById(svg.Attribute(R + "embed")!.Value);
        Assert.Equal("image/svg+xml", svgPart.ContentType);
        Assert.Contains("font-family=\"Arial\"", new StreamReader(svgPart.GetStream()).ReadToEnd(), StringComparison.Ordinal);
        Assert.Equal("flowchart LR", drawing.Descendants(Wp + "docPr").Single().Attribute("descr")!.Value);

        var natural = ((DrawingGraphic)new MermaidRenderer().Render(new CodeBlockRequest("mermaid", Flowchart.Split('\n', 2)[1].Replace("```\n", string.Empty, StringComparison.Ordinal), Brand.Neutral))!).Drawing;
        var extent = drawing.Descendants(Wp + "extent").Single();
        Assert.InRange(long.Parse(extent.Attribute("cx")!.Value, System.Globalization.CultureInfo.InvariantCulture), (long)(natural.Width * 9525) - 9525, (long)(natural.Width * 9525) + 9525);
    }

    [Fact]
    public void ADiagramWithoutAnImageConverter_StaysCode_WithAdviceNamingTheConverter()
    {
        var (docx, result) = GenerateWithResult(Flowchart, new DocxOptions { CodeBlockRenderers = [new MermaidRenderer()] });
        using var package = Samples.Open(docx);

        var note = Assert.Single(result.Notes);
        Assert.Equal((NoteCodes.CodeNotDrawn, 1), (note.Code, note.Line));
        Assert.Contains("Register SvgImageConverter from wwosik.DocLibs.DocumentConversions.Svg to draw diagrams in Word", note.Message, StringComparison.Ordinal);
        Assert.All(Body(package).Elements<Paragraph>(), p => Assert.Equal("Code", Style(p)));
        Assert.Empty(package.MainDocumentPart!.ImageParts);
    }

    [Fact]
    public void AFigureCaption_FollowsADrawnDiagram_AndIsIgnoredAfterCode()
    {
        var markdown = Flowchart + "\nFigure: Order flow\n\n```text\nplain\n```\n\nFigure: Not a figure\n";
        var (docx, result) = GenerateWithResult(markdown, Drawn);
        using var package = Samples.Open(docx);

        var captions = Body(package).Elements<Paragraph>().Where(p => Style(p) == "Caption").Select(Texts).ToList();
        Assert.Equal(["Figure 1: Order flow"], captions);
        Assert.Equal(NoteCodes.CaptionIgnored, Assert.Single(result.Notes).Code);
    }

    [Fact]
    public void AnSvgImage_KeepsItsSvg_BesideThePng_AsItCame()
    {
        var svg = new DocumentResource("shape.svg", "image/svg+xml", Encoding.UTF8.GetBytes(Svg));
        var docx = Samples.Generate(Samples.Markdown("![Shape](shape.svg)\n", svg), new DocxOptions { ImageConverters = [new SvgImageConverter()] });
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);

        var blip = XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml).Descendants(A + "blip").Single();
        var svgPart = package.MainDocumentPart!.GetPartById(blip.Descendants(Asvg + "svgBlip").Single().Attribute(R + "embed")!.Value);
        Assert.Equal(Svg, new StreamReader(svgPart.GetStream()).ReadToEnd());
        Assert.Equal(120 * 9525, long.Parse(XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml).Descendants(Wp + "extent").Single().Attribute("cx")!.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AnSvgImageWithoutAConverter_IsAnError()
    {
        var svg = new DocumentResource("shape.svg", "image/svg+xml", Encoding.UTF8.GetBytes(Svg));
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("![Shape](shape.svg)\n", svg)));
        Assert.Contains("is not a PNG, JPEG or GIF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFinancialChart_IsAPicture()
    {
        var markdown = "```financial-chart\n{ \"type\": \"growth-trend\", \"title\": \"Orders growth\", \"lastActual\": 1, \"reference\": 0.05, "
            + "\"months\": [ { \"label\": \"Jan\", \"change\": 0.05 }, { \"label\": \"Feb\", \"change\": 0.04 }, { \"label\": \"Mar\", \"change\": 0.06 } ] }\n```\n";
        var (docx, result) = GenerateWithResult(markdown, Drawn);
        Samples.AssertValid(docx);
        Assert.Empty(result.Notes);
        using var package = Samples.Open(docx);

        Assert.Single(Body(package).Descendants<WordDrawing>());
        var svg = package.MainDocumentPart!.ImageParts.Single(p => p.ContentType == "image/svg+xml");
        Assert.DoesNotContain("chart-tooltip", new StreamReader(svg.GetStream()).ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARenderersImage_IsPlacedLikeAnImage()
    {
        var options = new DocxOptions { CodeBlockRenderers = [new ImageRenderer(Samples.Image("diagram.png"))] };
        var docx = Generate("```picture\nanything\n```\n\nFigure: A picture\n", options);
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);

        Assert.Single(Body(package).Descendants<WordDrawing>());
        Assert.Contains(Body(package).Elements<Paragraph>(), p => Texts(p) == "Figure 1: A picture");
    }

    [Fact]
    public void AQrCode_IsAPicture_OrItsAltText_WithANote()
    {
        const string Markdown = "![Our site](qr:https://example.com)\n";
        var (drawn, none) = (GenerateWithResult(Markdown, Drawn), GenerateWithResult(Markdown));
        Samples.AssertValid(drawn.Docx);
        Assert.Empty(drawn.Result.Notes);
        using (var package = Samples.Open(drawn.Docx))
        {
            var drawing = XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml);
            Assert.Equal("Our site", drawing.Descendants(Wp + "docPr").Single().Attribute("descr")!.Value);
            Assert.Single(drawing.Descendants(Asvg + "svgBlip"));
        }

        using (var package = Samples.Open(none.Docx))
        {
            Assert.Equal("Our site", Texts(Body(package).Elements<Paragraph>().Single()));
        }

        Assert.Equal(NoteCodes.CodeNotDrawn, Assert.Single(none.Result.Notes).Code);
        Assert.Throws<DocumentConversionException>(() => Generate("![Code](qr:https://example.com){.round}\n", Drawn));
    }

    [Fact]
    public void Icons_AreDrawnInTheLine_OrLeftOutWithANote_AndUnknownNamesThrow()
    {
        const string Markdown = "Done :icon-check: and :icon-check: again.\n";
        var drawn = GenerateWithResult(Markdown, Drawn);
        Samples.AssertValid(drawn.Docx);
        Assert.Empty(drawn.Result.Notes);
        using (var package = Samples.Open(drawn.Docx))
        {
            var paragraph = Body(package).Elements<Paragraph>().Single();
            Assert.Equal("Done  and  again.", Texts(paragraph));
            var extents = paragraph.Descendants<Extent>().ToList();
            Assert.Equal(2, extents.Count);
            Assert.All(extents, e => Assert.Equal((133350L, 133350L), (e.Cx!.Value, e.Cy!.Value)));
            Assert.Equal(2, package.MainDocumentPart!.ImageParts.Count());
        }

        var none = GenerateWithResult(Markdown);
        using (var package = Samples.Open(none.Docx))
        {
            Assert.Equal("Done  and  again.", Texts(Body(package).Elements<Paragraph>().Single()));
        }

        Assert.Equal(NoteCodes.IconNotDrawn, Assert.Single(none.Result.Notes).Code);
        Assert.Contains("Did you mean", Assert.Throws<DocumentConversionException>(() => Generate(":icon-chek:\n")).Message, StringComparison.Ordinal);
        using var off = Samples.Open(Generate(":icon-chek:\n", new DocxOptions { Icons = null }));
        Assert.Equal(":icon-chek:", Texts(Body(off).Elements<Paragraph>().Single()));
    }

    [Fact]
    public void ADrawnContainer_IsAPicture_OrItsContentWithANote()
    {
        const string Markdown = "## Plan\n\n::: timeline today=2026-03-01\n- **2026-01** Start\n- **2026-06** Go-live\n:::\n";
        var drawn = GenerateWithResult(Markdown, Drawn);
        Samples.AssertValid(drawn.Docx);
        Assert.Empty(drawn.Result.Notes);
        using (var package = Samples.Open(drawn.Docx))
        {
            var drawing = XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml);
            Assert.Equal("2026-01 Start", drawing.Descendants(Wp + "docPr").Single().Attribute("descr")!.Value);
        }

        var none = GenerateWithResult(Markdown);
        using (var package = Samples.Open(none.Docx))
        {
            Assert.Empty(Body(package).Descendants<WordDrawing>());
            Assert.Contains(Body(package).Descendants<Paragraph>(), p => Texts(p).Contains("Go-live", StringComparison.Ordinal));
        }

        Assert.Equal((NoteCodes.CodeNotDrawn, 3), (Assert.Single(none.Result.Notes).Code, none.Result.Notes[0].Line));
    }

    [Fact]
    public void AScene_IsAPictureOfItsFinalState_OrLeftOutWithANote()
    {
        const string Markdown = "## Flow\n\n::: scene\nbox web 8 34 30 22 \"Browser\"\nbox api 65 34 30 22 \"API\" step=1\nbox old 110 34 30 22 \"Old\" exit=1\narrow web api\n:::\n";
        var drawn = GenerateWithResult(Markdown, Drawn);
        Samples.AssertValid(drawn.Docx);
        Assert.Empty(drawn.Result.Notes);
        using (var package = Samples.Open(drawn.Docx))
        {
            var drawing = XElement.Parse(Body(package).Descendants<WordDrawing>().Single().OuterXml);
            Assert.Equal("Browser, API", drawing.Descendants(Wp + "docPr").Single().Attribute("descr")!.Value);
        }

        var none = GenerateWithResult(Markdown);
        using (var package = Samples.Open(none.Docx))
        {
            Assert.Empty(Body(package).Descendants<WordDrawing>());
            Assert.DoesNotContain(Body(package).Descendants<Paragraph>(), p => Texts(p).Contains("box", StringComparison.Ordinal));
        }

        Assert.Equal((NoteCodes.CodeNotDrawn, 3), (Assert.Single(none.Result.Notes).Code, none.Result.Notes[0].Line));
    }

    [Theory]
    [InlineData("neutral")]
    [InlineData("sample")]
    public void TheReport_WithDiagrams_IsValid_AndRoundTripsThroughTheDocxConverter(string template)
    {
        // As the PDF tests' WithDiagrams: the report's `graph TD; A-->B;` is no Mermaid the renderer takes, so a stand-in draws it.
        var withDiagrams = new DocxOptions
        {
            CodeBlockRenderers = [new StandIn(), new ChartRenderer()],
            ImageConverters = [new SvgImageConverter()],
            Template = Samples.Template(template),
            TableOfContents = true,
        };
        var docx = Samples.Generate(Samples.Document("report.md"), withDiagrams);
        Samples.WriteForReview($"report-diagrams-{template}.docx", docx);

        Samples.AssertValid(docx);
        Samples.AssertMatchesExpected($"report-diagrams-{template}.roundtrip", Samples.RoundTrip(docx));
    }

    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void TheDrawingsSample_IsValid_AndRoundTripsThroughTheDocxConverter(string template)
    {
        var svg = new DocumentResource("shape.svg", "image/svg+xml", Encoding.UTF8.GetBytes(Svg));
        var document = Samples.Markdown(File.ReadAllText(Samples.Path("drawings.md")).ReplaceLineEndings("\n"), svg);
        var (docx, result) = Samples.GenerateWithResult(document, Drawn with { Template = Samples.Template(template) });
        Samples.WriteForReview($"drawings-{template}.docx", docx);

        Samples.AssertValid(docx);
        Assert.Empty(result.Notes);
        Samples.AssertMatchesExpected($"drawings-{template}.roundtrip", Samples.RoundTrip(docx));
    }

    private static byte[] Generate(string markdown, DocxOptions? options = null) => GenerateWithResult(markdown, options).Docx;

    private static (byte[] Docx, GenerationResult Result) GenerateWithResult(string markdown, DocxOptions? options = null) =>
        Samples.GenerateWithResult(Samples.Markdown(markdown), options);

    private static long TextWidth(WordprocessingDocument package)
    {
        var margins = Section(package).GetFirstChild<PageMargin>()!;
        return (long)Section(package).GetFirstChild<PageSize>()!.Width!.Value - (long)margins.Left!.Value - (long)margins.Right!.Value;
    }

    // Draws every `mermaid` block as a box with a word in it.
    private sealed class StandIn : ICodeBlockRenderer
    {
        public CodeBlockGraphic? Render(CodeBlockRequest request) => request.Language == "mermaid"
            ? new DrawingGraphic(new Drawing(240, 120, [
                new DrawingShape(DrawingPath.Rectangle(10, 10, 220, 100), "DBEAFE", "1D4ED8", 2),
                new DrawingText(120, 66, "Flow", 16, "1D4ED8") { Anchor = TextAnchor.Middle }]))
            : null;
    }

    private sealed class ImageRenderer(DocumentResource image) : ICodeBlockRenderer
    {
        public CodeBlockGraphic? Render(CodeBlockRequest request) => request.Language == "picture" ? new ImageGraphic(image) : null;
    }
}
