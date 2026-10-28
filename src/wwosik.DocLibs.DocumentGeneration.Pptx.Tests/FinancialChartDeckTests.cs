using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Charts.Tests;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>The financial charts on slides: shapes in the slide's brand, dark ones on a dark template.</summary>
public class FinancialChartDeckTests
{
    private const string Deck = """
        ## Development

        ```development
        ```

        ## Growth by row

        ```growth-by-row
        ```

        ## Actual vs reference

        ```actual-vs-reference
        ```

        ## Growth trend and two years

        ::: columns
        ```growth-trend
        ```

        ---

        ```two-year
        ```
        :::

        ## Summary

        ```summary-table
        ```
        """;

    // Draws each sample chart in the brand the slide asks for, as the financial-chart block does.
    private static readonly ICodeBlockRenderer Renderer = Samples.Renderer(request => (request.Language switch
    {
        "development" => (FinancialChart)FinancialSamples.Development(),
        "growth-by-row" => FinancialSamples.GrowthByRow(),
        "actual-vs-reference" => FinancialSamples.ActualVsReference(),
        "growth-trend" => FinancialSamples.GrowthTrend(),
        "two-year" => FinancialSamples.TwoYear(),
        "summary-table" => FinancialSamples.Summary(),
        _ => null,
    }) is { } chart ? new DrawingGraphic(FinancialCharts.Draw(chart, FinancialSamples.Options with { Brand = request.Brand })) : null);

    [Fact]
    public void OnALightTemplate_TheChartsAreShapesOnTheirOwnSurface()
    {
        var pptx = Samples.Generate(Samples.Markdown(Deck), new PptxOptions { Template = Samples.BuiltIn("sample"), CodeBlockRenderers = [Renderer] });
        Samples.WriteForReview("financial-charts-sample.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var texts = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Text>()).Select(t => t.Text).ToList();
        Assert.Contains("Orders (July 2026)", texts);
        Assert.Contains("Machines", texts);
    }

    [Fact]
    public void OnADarkTemplate_TheChartsHaveNoSurface_AndWhiteText()
    {
        var pptx = Samples.Generate(Samples.Markdown(Deck), DarkTemplate.Options with { CodeBlockRenderers = [Renderer] });
        Samples.WriteForReview("financial-charts-dark.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var title = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Run>()).First(r => r.Text?.Text == "Orders growth (July 2026)");
        Assert.Equal("FFFFFF", title.RunProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
        Assert.DoesNotContain(Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.SolidFill>()), f => f.RgbColorModelHex?.Val?.Value == "FFFFFF" && f.Parent is DocumentFormat.OpenXml.Presentation.ShapeProperties);
    }
}
