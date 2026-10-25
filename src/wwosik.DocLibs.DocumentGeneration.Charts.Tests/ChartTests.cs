using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Charts.Tests;

public class ChartTests
{
    private const string Orders = "type: bar\ntitle: Orders by month\nunit: k\n---\nmonth, plan, actual\nJan, 120, 132\nFeb, 118, 121\nMar, 125,\n";

    private static readonly Brand SampleBrand = BrandPack.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample"));

    [Fact]
    public void Parse_ReadsSettingsAndData_EmptyCellsAsGaps()
    {
        var chart = ChartParser.Parse(Orders, 4);

        Assert.Equal(ChartKind.Bar, chart.Kind);
        Assert.Equal("Orders by month", chart.Title);
        Assert.Equal("k", chart.Unit);
        Assert.Equal(["Jan", "Feb", "Mar"], chart.Categories);
        Assert.Equal(["plan", "actual"], chart.Series.Select(s => s.Name));
        Assert.Equal([132, 121, null], chart.Series[1].Values);
    }

    [Fact]
    public void Parse_TakesCsvAlone_QuotedCommas_AndComments()
    {
        var csv = ChartParser.Parse("region, \"sales, net\"\n\"North, East\", 1.5\n", 1);
        var settings = ChartParser.Parse("type: area   # filled\nstacked: true\n---\nx, a, b\n1, 2, 3\n", 1);

        Assert.Equal("North, East", csv.Categories.Single());
        Assert.Equal("sales, net", csv.Series.Single().Name);
        Assert.Equal(ChartKind.Area, settings.Kind);
        Assert.True(settings.Stacked);
        Assert.False(ChartParser.Parse("type: line\nstacked: true\n---\nx, a\n1, 2\n", 1).Stacked); // lines do not stack
    }

    [Theory]
    [InlineData("colour: red\n---\nx, a\n1, 2\n", "Unknown setting 'colour'")]
    [InlineData("type: pie\n---\nx, a\n1, 2\n", "The type 'pie' is not bar, line or area")]
    [InlineData("---\nx, a\n1, 1,200\n", "has 3 values; the header names 2 columns")]
    [InlineData("---\nx, a\n1, lots\n", "'lots' in row 2 of the data is not a number")]
    [InlineData("---\nx, a\n", "needs a header row and at least one row of values")]
    [InlineData("---\nx\n1\n", "names the category column and at least one series")]
    public void Parse_Mistakes_NameTheLineAndTheProblem(string code, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => ChartParser.Parse(code, 7));

        Assert.StartsWith("The chart code block at line 7 cannot be drawn: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scale_RunsFromZeroToARoundTop()
    {
        Assert.Equal((0.0, 150.0, 25.0), ChartDrawing.Scale(ChartParser.Parse(Orders, 1)));
        Assert.Equal((-20.0, 60.0, 20.0), ChartDrawing.Scale(ChartParser.Parse("x, a\n1, -15\n2, 55\n", 1)));
        Assert.Equal(300.0, ChartDrawing.Scale(ChartParser.Parse("stacked: true\n---\nx, a, b\n1, 120, 132\n", 1)).High); // the sum, 252
    }

    [Fact]
    public void Drawing_HasABarPerValue_TheTitle_AndALegend()
    {
        var chart = ChartParser.Parse(Orders, 1) with { Colors = ["111111", "222222"] };

        var drawing = ChartDrawing.Draw(chart, ResolvedBrand.Neutral.Diagram);

        var bars = drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Rectangle && s.Fill is "111111" or "222222").ToList();
        Assert.Equal(5 + 2, bars.Count); // five values (one empty) and two legend swatches
        var texts = drawing.Items.OfType<DrawingText>().Select(t => string.Concat(t.Spans.Select(s => s.Text))).ToList();
        Assert.Contains("Orders by month", texts);
        Assert.Contains("plan", texts);
        Assert.Contains("Mar", texts);
    }

    [Fact]
    public void Renderer_TakesOnlyChartBlocks_InTheBrandsDiagramPalette()
    {
        var renderer = new ChartRenderer();

        Assert.Null(renderer.Render(new CodeBlockRequest("mermaid", "x", Brand.Neutral)));
        var graphic = Assert.IsType<ChartGraphic>(renderer.Render(new CodeBlockRequest("Chart", Orders, SampleBrand)));
        Assert.Equal(ResolvedBrand.Resolve(SampleBrand).Diagram.Palette, graphic.Chart.Colors);
        Assert.Contains("```chart", renderer.AuthoringGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void Decks_GetANativeChart_WithItsDataInAnEmbeddedWorkbook()
    {
        var pptx = Pptx($"## Orders\n\n```chart\n{Orders}```\n\nSource: made up.\n", new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var package = PresentationDocument.Open(new MemoryStream(pptx), false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        var slide = package.PresentationPart!.SlideParts.Single();
        var chart = slide.ChartParts.Single();
        Assert.NotNull(chart.EmbeddedPackagePart);
        var bar = chart.ChartSpace!.Descendants<C.BarChart>().Single();
        Assert.Equal(C.BarGroupingValues.Clustered, bar.GetFirstChild<C.BarGrouping>()!.Val!.Value);
        var series = bar.Elements<C.BarChartSeries>().ToList();
        Assert.Equal(["plan", "actual"], series.Select(s => s.Descendants<C.NumericValue>().First().Text));
        Assert.Equal(["132", "121"], series[1].GetFirstChild<C.Values>()!.Descendants<C.NumericPoint>().Select(p => p.NumericValue!.Text));
        Assert.Equal("Orders by month", chart.ChartSpace.Descendants<C.Title>().First().InnerText);
        Assert.NotNull(slide.Slide!.Descendants<P.GraphicFrame>().Single().Descendants<C.ChartReference>().SingleOrDefault());
    }

    [Fact]
    public void TwoCharts_StandSideBySide_InColumns()
    {
        var markdown = $"## Two\n\n::: columns\n```chart\n{Orders}```\n\n---\n\n```chart\ntype: line\n---\nx, a\n1, 2\n2, 3\n```\n:::\n";
        var pptx = Pptx(markdown, new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var package = PresentationDocument.Open(new MemoryStream(pptx), false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        var frames = package.PresentationPart!.SlideParts.Single().Slide!.Descendants<P.GraphicFrame>().Select(f => f.Transform!).ToList();
        Assert.Equal(2, frames.Count);
        Assert.Equal(frames[0].Offset!.Y!.Value, frames[1].Offset!.Y!.Value);
        Assert.True(frames[1].Offset!.X!.Value >= frames[0].Offset!.X!.Value + frames[0].Extents!.Cx!.Value);
    }

    [Fact]
    public void Pdfs_DrawTheChart()
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(new MarkdownDocument($"## Orders\n\n```chart\n{Orders}```\n", [], new Dictionary<string, string>()), output, new PdfOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var document = PdfDocument.Open(output.ToArray());
        var text = string.Concat(document.GetPages().Select(p => p.Text));
        Assert.Contains("Ordersbymonth", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void ADataMistake_IsADocumentConversionException()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Pptx("## X\n\n```chart\n---\nx, a\n1, lots\n```\n", new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] }));

        Assert.IsType<ArgumentException>(error.InnerException);
        Assert.Contains("at line 3", error.Message, StringComparison.Ordinal);
    }

    private static byte[] Pptx(string markdown, PptxOptions options)
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(new MarkdownDocument(markdown, [], new Dictionary<string, string>()), output, options);
        return output.ToArray();
    }
}
