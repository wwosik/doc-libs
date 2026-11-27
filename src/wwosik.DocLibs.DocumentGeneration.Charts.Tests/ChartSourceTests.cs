using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Charts.Tests;

/// <summary>A chart's <c>source:</c>, and CSV separated by semicolons with a decimal comma.</summary>
public class ChartSourceTests
{
    private const string Orders = "title: Orders\nsource: Annual report\n---\nmonth, plan, actual\nJan, 120, 132\nFeb, 118, 121\n";

    [Fact]
    public void Source_IsASetting()
    {
        Assert.Equal("Annual report", ChartParser.Parse(Orders, 1).Source);
        Assert.Null(ChartParser.Parse("x, a\n1, 2\n", 1).Source);
        Assert.NotEqual(ChartParser.Parse(Orders, 1), ChartParser.Parse(Orders.Replace("Annual", "Monthly", StringComparison.Ordinal), 1));
        var unknown = Assert.Throws<ArgumentException>(() => ChartParser.Parse("colour: red\n---\nx, a\n1, 2\n", 1));
        Assert.Contains("Valid settings: type, title, unit, stacked, source, decimal.", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Drawing_GrowsByOneLine_ForTheSource_InTheDocumentLanguage()
    {
        var renderer = new ChartRenderer();
        var without = Assert.IsType<ChartGraphic>(renderer.Render(new CodeBlockRequest("chart", "x, a\n1, 2\n", Brand.Neutral))).Drawing;
        var english = Assert.IsType<ChartGraphic>(renderer.Render(new CodeBlockRequest("chart", Orders, Brand.Neutral))).Drawing;
        var german = Assert.IsType<ChartGraphic>(renderer.Render(new CodeBlockRequest("chart", Orders, Brand.Neutral) { Texts = GeneratedTexts.German })).Drawing;

        Assert.Equal((640, 380), (without.Width, without.Height));
        Assert.Equal((640, 398), (english.Width, english.Height));
        var source = english.Items.OfType<DrawingText>().Single(t => t.Spans[0].Text.StartsWith("Source", StringComparison.Ordinal));
        Assert.Equal("Source: Annual report", source.Spans[0].Text);
        Assert.True(source.Baseline > 380, "under the legend");
        Assert.Equal(TextAnchor.Start, source.Anchor);
        Assert.Contains(german.Items.OfType<DrawingText>(), t => t.Spans[0].Text == "Quelle: Annual report");
    }

    [Fact]
    public void Requests_DifferByTheirTexts()
    {
        var request = new CodeBlockRequest("chart", Orders, Brand.Neutral);

        Assert.Equal(GeneratedTexts.English, request.Texts);
        Assert.NotEqual(request, request with { Texts = GeneratedTexts.German });
        Assert.Equal(request, request with { Texts = GeneratedTexts.English with { } });
    }

    [Fact]
    public void Semicolons_SplitEveryRow_AndNumbersHaveADecimalComma()
    {
        var chart = ChartParser.Parse("Monat; Plan; Ist\nJan; 12,5; -0,25\n\"Feb; Mär\"; 1234,5;\n", 1);

        Assert.Equal(["Plan", "Ist"], chart.Series.Select(s => s.Name));
        Assert.Equal(["Jan", "Feb; Mär"], chart.Categories);
        Assert.Equal([12.5, 1234.5], chart.Series[0].Values);
        Assert.Equal([-0.25, null], chart.Series[1].Values);
    }

    [Fact]
    public void Commas_ReadAsBefore()
    {
        var chart = ChartParser.Parse("x, a\n1, 1250.5\n", 1);

        Assert.Equal([1250.5], chart.Series[0].Values);
    }

    [Fact]
    public void APointInADecimalCommaNumber_IsAnError_NamingTheCellAndTheFix()
    {
        var error = Assert.Throws<ArgumentException>(() => ChartParser.Parse("x; a\n1; 2\n2; 1.234,5\n", 9));

        Assert.StartsWith("The chart code block at line 9 cannot be drawn: '1.234,5' in row 3 of the data", error.Message, StringComparison.Ordinal);
        Assert.Contains("remove the thousands separator", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ChartParser.Parse("x; a\n1; 1.234\n", 1)); // never guessed as 1.234 or 1234
    }

    [Fact]
    public void Decimal_OverridesTheMark_ForEitherSeparator()
    {
        Assert.Equal([1.5], ChartParser.Parse("decimal: point\n---\nx; a\n1; 1.5\n", 1).Series[0].Values);
        Assert.Equal([1.5], ChartParser.Parse("decimal: comma\n---\nx, a\n1, \"1,5\"\n", 1).Series[0].Values);
        var error = Assert.Throws<ArgumentException>(() => ChartParser.Parse("decimal: dot\n---\nx, a\n1, 2\n", 1));
        Assert.Contains("decimal is comma or point, not 'dot'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Source: Annual report")]
    [InlineData("de", "Quelle: Annual report")]
    public void Decks_WriteTheSource_UnderTheChart(string language, string expected)
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(
            new MarkdownDocument($"---\nlang: {language}\n---\n## Orders\n\n```chart\n{Orders}```\n\nBelow.\n", [], new Dictionary<string, string>()),
            output,
            new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var package = PresentationDocument.Open(new MemoryStream(output.ToArray()), false);
        Assert.Empty(new OpenXmlValidator().Validate(package));
        var slide = package.PresentationPart!.SlideParts.Single().Slide!;
        var frame = slide.Descendants<P.GraphicFrame>().Single().Transform!;
        var source = slide.Descendants<P.Shape>().Single(s => s.InnerText == expected);
        var below = slide.Descendants<P.Shape>().Single(s => s.InnerText == "Below.");
        var top = source.ShapeProperties!.Transform2D!.Offset!.Y!.Value;
        Assert.Equal(frame.Offset!.Y!.Value + frame.Extents!.Cy!.Value, top);
        Assert.True(below.ShapeProperties!.Transform2D!.Offset!.Y!.Value > top, "the caption goes below the source");
        Assert.Equal(1000, source.Descendants<A.RunProperties>().Single().FontSize!.Value);
    }

    [Fact]
    public void Decks_WithoutASource_HaveNoSourceLine()
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(new MarkdownDocument("## Orders\n\n```chart\nx, a\n1, 2\n```\n", [], new Dictionary<string, string>()), output, new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var package = PresentationDocument.Open(new MemoryStream(output.ToArray()), false);
        var slide = package.PresentationPart!.SlideParts.Single().Slide!;
        Assert.DoesNotContain(slide.Descendants<P.Shape>(), s => s.InnerText.StartsWith("Source", StringComparison.Ordinal));
    }

    [Fact]
    public void Pdfs_DrawTheSource_InTheDocumentLanguage()
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(
            new MarkdownDocument($"---\nlang: de\n---\n## Orders\n\n```chart\n{Orders}```\n", [], new Dictionary<string, string>()),
            output,
            new PdfOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var document = PdfDocument.Open(output.ToArray());
        var text = string.Concat(document.GetPages().Select(p => p.Text)).Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Contains("Quelle:Annualreport", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("category: Month\nseries: Plan\n")]
    [InlineData("series: Plan\n---\nx, a\n1, 2\n")]
    [InlineData("placement: sheet\n---\nx, a\n1, 2\n")]
    public void WorkbookSettings_OutsideAWorkbook_NameTheWorkbookForm(string block)
    {
        var error = Assert.Throws<ArgumentException>(() => ChartParser.Parse(block, 4));

        Assert.Contains("line 4", error.Message, StringComparison.Ordinal);
        Assert.Contains("workbook form", error.Message, StringComparison.Ordinal);
    }
}
