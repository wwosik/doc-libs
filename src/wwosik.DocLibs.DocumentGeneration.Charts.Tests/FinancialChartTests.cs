using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Charts.Tests;

/// <summary>The six financial charts: their snapshots, the rules they share, their edge cases, and the JSON block.</summary>
public class FinancialChartTests
{
    private static readonly ResolvedChartColors Colors = ResolvedBrand.Resolve(Brand.Neutral).Chart;

    public static TheoryData<string> Snapshots => [.. Charts.Keys];

    private static Dictionary<string, FinancialChart> Charts => new()
    {
        ["development"] = FinancialSamples.Development(),
        ["development-growth"] = FinancialSamples.Development(DevelopmentPanel.Growth),
        ["growth-by-row"] = FinancialSamples.GrowthByRow(),
        ["actual-vs-reference"] = FinancialSamples.ActualVsReference(),
        ["actual-vs-reference-value"] = FinancialSamples.ActualVsReference(ReferenceDisplay.Value),
        ["growth-trend"] = FinancialSamples.GrowthTrend(),
        ["two-year"] = FinancialSamples.TwoYear(),
        ["summary-table"] = FinancialSamples.Summary(),
    };

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void EveryChart_MatchesItsSnapshot(string name) =>
        AssertMatchesExpected(name, FinancialCharts.ToSvg(Charts[name], FinancialSamples.Options));

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void EveryBarPointAndLine_HasHoverText_AndTheSvgHasNoScript(string name)
    {
        var drawing = FinancialCharts.Draw(Charts[name], FinancialSamples.Options);
        var svg = FinancialCharts.ToSvg(Charts[name], FinancialSamples.Options);

        // Every filled bar (a rectangle with an outline) and every hotspot says what it is.
        // Legend swatches are the squares; they need none.
        var bars = drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Rectangle && s.Stroke is not null && s.Path.Bounds.Height < 40
            && !(Math.Abs(s.Path.Bounds.Width - s.Path.Bounds.Height) < 0.01 && s.Path.Bounds.Width <= 12));
        Assert.All(bars, bar => Assert.NotNull(bar.Hover));
        Assert.All(drawing.Items.OfType<DrawingHotspot>(), spot => Assert.NotNull(spot.Hover));
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        if (name != "summary-table")
        {
            Assert.Contains("<g class=\"chart-hoverable\">", svg, StringComparison.Ordinal);
            Assert.Contains(".chart-hoverable:hover .chart-tooltip", svg, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0, 0.0004, 0.02)] // at least six points of headroom: a rate panel keeps room for its end labels
    [InlineData(-12, 140, 50)]
    [InlineData(3_000_000, 21_500_000, 5_000_000)]
    public void Axis_IncludesZero_InRoundSteps_PaddedBeforeRounding(double min, double max, double step)
    {
        var axis = FinancialCanvas.Axis(min, max);

        Assert.True(axis.Min <= 0 && axis.Max >= max * 1.0, $"{axis}");
        Assert.True(axis.Max > max, "a series ending on a round number gains a gridline above it");
        Assert.Equal(step, axis.Step, 6);
        var factor = axis.Step / Math.Pow(10, Math.Floor(Math.Log10(axis.Step)));
        Assert.Contains(Math.Round(factor, 6), new[] { 1, 2, 2.5, 5 });
    }

    [Fact]
    public void AColourMeansASeries_ABarBehindItsReference_KeepsItsSeriesColour()
    {
        var drawing = FinancialCharts.Draw(FinancialSamples.ActualVsReference(), FinancialSamples.Options);
        var bars = drawing.Items.OfType<DrawingShape>().Where(s => s.Hover?.Text.Contains("Orders (month)", StringComparison.Ordinal) == true).ToList();

        Assert.Equal(4, bars.Count); // ahead, behind, negative: all one fill
        Assert.Single(bars.Select(b => b.Fill).Distinct());
        Assert.Equal(Colors.Series[0].Fill, bars[0].Fill);
    }

    [Fact]
    public void VerdictColours_GoOnFigures_WithAConfigurableTolerance()
    {
        var chart = FinancialSamples.GrowthByRow();
        string FigureColour(FinancialChartOptions options, string figure) =>
            FinancialCharts.Draw(chart, options).Items.OfType<DrawingText>().Single(t => t.Text == figure).Color;

        Assert.Equal(Colors.Ahead, FigureColour(FinancialSamples.Options, "8,1%"));
        Assert.Equal(Colors.Behind, FigureColour(FinancialSamples.Options, "3,2%"));
        Assert.Equal(Colors.Level, FigureColour(FinancialSamples.Options, "4,9%")); // 0.1 points under: level
        Assert.Equal(Colors.Level, FigureColour(FinancialSamples.Options with { Tolerance = 0.05 }, "8,1%"));
        Assert.DoesNotContain(FinancialCharts.Draw(chart, FinancialSamples.Options).Items.OfType<DrawingShape>(), s => s.Fill == Colors.Behind);
    }

    [Fact]
    public void ADashMeansProjection_EveryDashedBarIsAProjection()
    {
        var drawing = FinancialCharts.Draw(FinancialSamples.Development(), FinancialSamples.Options);
        var dashed = drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Rectangle && s.Dash is not null && s.Hover is not null).ToList();

        Assert.NotEmpty(dashed);
        Assert.All(dashed, bar => Assert.Contains("Forecast", bar.Hover!.Text, StringComparison.Ordinal));
        Assert.All(drawing.Items.OfType<DrawingShape>().Where(s => s.Hover?.Text.Contains("Target", StringComparison.Ordinal) == true), s => Assert.Null(s.Dash));
    }

    [Fact]
    public void Numbers_FollowTheCultureScaleAndUnit()
    {
        var polish = Texts(FinancialCharts.Draw(FinancialSamples.Development(), FinancialSamples.Options));
        var english = Texts(FinancialCharts.Draw(FinancialSamples.Development(), FinancialSamples.Options with { Culture = CultureInfo.GetCultureInfo("en-US"), Unit = "m" }));

        Assert.Contains("Last year 206,4 M", polish);
        Assert.Contains("Last year 206.4 m", english);
        Assert.Contains(polish, t => t.StartsWith("This year vs Last year +6,4% at Jul", StringComparison.Ordinal));
    }

    [Fact]
    public void EndLabels_ArePushedApart_AndStayInsideThePanel()
    {
        var drawing = FinancialCharts.Draw(FinancialSamples.Development(), FinancialSamples.Options);
        var ends = drawing.Items.OfType<DrawingText>().Where(t => t.X > drawing.Width / 2 && (t.Text.StartsWith("Last year ", StringComparison.Ordinal) || t.Text.StartsWith("Target ", StringComparison.Ordinal) || t.Text.StartsWith("Forecast ", StringComparison.Ordinal)))
            .OrderBy(t => t.Baseline).ToList();

        Assert.Equal(3, ends.Count);
        Assert.All(ends.Zip(ends.Skip(1)), pair => Assert.True(pair.Second.Baseline - pair.First.Baseline >= 11 - 1e-9));
        Assert.All(ends, t => Assert.True(t.X + DrawingSvg.EstimateWidth(t.Text, t.Size) <= drawing.Width, $"'{t.Text}' runs off the canvas"));
    }

    [Fact]
    public void WithoutAProjection_NothingIsDashed_AndTheLegendHasNoEntry()
    {
        var chart = FinancialSamples.Development() with
        {
            Months = [.. FinancialSamples.Development().Months.Select(m => m with { Projection = null, ProjectionCumulative = null })],
            ChangeAtYearEnd = null,
        };

        var drawing = FinancialCharts.Draw(chart, FinancialSamples.Options);

        Assert.DoesNotContain(drawing.Items.OfType<DrawingShape>(), s => s.Dash == FinancialCanvas.ProjectionDash);
        Assert.DoesNotContain("Forecast", Texts(drawing));
    }

    [Fact]
    public void ADecemberChart_HasNoBoundaryRule()
    {
        var drawing = FinancialCharts.Draw(FinancialSamples.Development(lastActual: 6) with { LastActual = 11 }, FinancialSamples.Options);

        Assert.DoesNotContain(drawing.Items.OfType<DrawingShape>(), s => s.Hover?.Text.StartsWith("Actuals end", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AllValuesNegative_TheAxisRunsBelowZero_AndTopsAtZero()
    {
        var chart = FinancialSamples.GrowthTrend() with { Months = [.. FinancialSamples.Months.Select((m, i) => new TrendMonth(m, -0.02 - (i * 0.01)))], Reference = -0.01 };

        var labels = Texts(FinancialCharts.Draw(chart, FinancialSamples.Options));

        Assert.Contains("0%", labels);
        Assert.DoesNotContain(labels, t => t.StartsWith('+'));
    }

    [Fact]
    public void ABarFarBeyondTheOthers_IsClippedWithAnArrowhead_AndItsLabelStatesTheTrueFigure()
    {
        var drawing = FinancialCharts.Draw(FinancialSamples.ActualVsReference(), FinancialSamples.Options);
        var clipped = drawing.Items.OfType<DrawingShape>().Single(s => s.Hover?.Text.EndsWith("240% (clipped)", StringComparison.Ordinal) == true);
        var label = drawing.Items.OfType<DrawingText>().Single(t => t.Text == "240%");

        Assert.True(clipped.Path.Bounds.X + clipped.Path.Bounds.Width <= drawing.Width - 50);
        Assert.Equal(TextAnchor.End, label.Anchor); // turned inward
        Assert.Contains(drawing.Items.OfType<DrawingShape>(), s => s.Path.Segments.Count == 4 && s.Fill == Colors.Series[1].Stroke); // the arrowhead: three corners, closed
    }

    [Fact]
    public void AnEmptyRestColumn_IsNotDrawn_AndASmallOneStatesItsValueAlone()
    {
        var table = FinancialSamples.Summary();
        var empty = table with { Blocks = [.. table.Blocks.Select(b => b with { Cells = [.. b.Cells.Take(4), new SummaryCell(0, 0, 0, 0)] })] };

        var small = Texts(FinancialCharts.Draw(table, FinancialSamples.Options));
        var none = Texts(FinancialCharts.Draw(empty, FinancialSamples.Options));

        Assert.Contains("Other", small);
        Assert.Contains("0,2", small);
        Assert.DoesNotContain("Other", none);
    }

    [Fact]
    public void TwoYearCategories_AreColouredByHeight_SixAtMost()
    {
        var chart = FinancialSamples.TwoYear(6);
        var colours = FinancialCharts.CategoryColors(chart, FinancialSamples.Options);

        Assert.Equal(Colors.Categories[0], colours[0]); // machines run highest
        Assert.Equal(Colors.Categories[1], colours[2]); // then service contracts, listed third
        var error = Assert.Throws<ArgumentException>(() => FinancialCharts.Draw(chart with { Series = [.. chart.Series, chart.Series[0]] }));
        Assert.Contains("at most 6 categories", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMonthWithoutAValue_BreaksTheLine_AndALoneValueIsADot()
    {
        double?[] values = [.. Enumerable.Range(0, 24).Select(i => i is 3 or 10 or 11 ? (double?)1_000_000 : null)];
        var chart = FinancialSamples.TwoYear(1) with { Series = [new TwoYearSeries("Lone", values)] };

        var shapes = FinancialCharts.Draw(chart, FinancialSamples.Options).Items.OfType<DrawingShape>()
            .Where(s => (s.Stroke == Colors.Categories[0] || s.Fill == Colors.Categories[0]) && s.Path.Bounds.Y > 80) // not the legend
            .ToList();

        Assert.Single(shapes, s => s.Kind == DrawingShapeKind.Ellipse); // month 3 alone
        Assert.Single(shapes, s => s.Kind == DrawingShapeKind.Path && s.Path.Segments.Count == 2); // months 10–11
    }

    [Fact]
    public void ALabelNearAnEdge_SlidesBackInsideThePlot()
    {
        var chart = FinancialSamples.GrowthByRow() with { Overall = new GrowthFigures(0.124, 0.05) };

        var drawing = FinancialCharts.Draw(chart, FinancialSamples.Options);
        var label = drawing.Items.OfType<DrawingText>().Single(t => t.Text.StartsWith("Overall Change", StringComparison.Ordinal) && t.Anchor == TextAnchor.Middle);

        Assert.True(label.X + (DrawingSvg.EstimateWidth(label.Text, label.Size) / 2) <= drawing.Width - 8 - 12 - 76 + 0.01);
    }

    [Fact]
    public void OnDarkSlides_ThereIsNoSurface_AndTextIsWhite()
    {
        var dark = ResolvedBrand.DarkSlideBrand(Brand.Neutral);

        var drawing = FinancialCharts.Draw(FinancialSamples.GrowthTrend(), FinancialSamples.Options with { Brand = dark });

        Assert.NotEqual(DrawingShapeKind.Rectangle, (drawing.Items[0] as DrawingShape)?.Kind);
        Assert.Equal("FFFFFF", drawing.Items.OfType<DrawingText>().First().Color);
        Assert.Equal("FFFFFF", (FinancialCharts.Draw(FinancialSamples.GrowthTrend(), FinancialSamples.Options).Items[0] as DrawingShape)!.Fill);
    }

    [Fact]
    public void BrandChartColours_Override_TheDerivedOnes()
    {
        var brand = Brand.Neutral with { Name = "charted", Colors = BrandColors.Neutral with { Chart = new BrandChartColors { Series = ["#123456"], Ahead = "00AA00" } } };

        var chart = ResolvedBrand.Resolve(brand).Chart;

        Assert.Equal("123456", chart.Series[0].Stroke);
        Assert.Equal("00AA00", chart.Ahead);
        Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(brand with { Name = "bad", Colors = BrandColors.Neutral with { Chart = new BrandChartColors { Axis = "grey" } } }));
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void TheJsonBlock_DrawsWhatTheRecordsDraw(string name)
    {
        var json = JsonSerializer.Serialize(Charts[name], FinancialChartContext.Default.FinancialChart);
        var block = json[..^1] + ",\"options\":{\"culture\":\"pl-PL\",\"scale\":1000000,\"unit\":\"M\",\"labels\":{\"previous\":\"Last year\",\"current\":\"This year\",\"reference\":\"Target\",\"projection\":\"Forecast\"}}}";

        var graphic = Assert.IsType<DrawingGraphic>(new ChartRenderer().Render(new CodeBlockRequest("financial-chart", block, Brand.Neutral) { Line = 3 }));

        Assert.Equal(DrawingSvg.Write(FinancialCharts.Draw(Charts[name], FinancialSamples.Options)), DrawingSvg.Write(graphic.Drawing));
    }

    [Fact]
    public void TheAuthoringGuidesExample_Draws()
    {
        var guide = new ChartRenderer().AuthoringGuide;
        var example = System.Text.RegularExpressions.Regex.Match(guide, "```financial-chart\n(?<body>.*?)```", System.Text.RegularExpressions.RegexOptions.Singleline).Groups["body"].Value;

        var graphic = Assert.IsType<DrawingGraphic>(new ChartRenderer().Render(new CodeBlockRequest("financial-chart", example, Brand.Neutral)));

        Assert.Contains(graphic.Drawing.Items.OfType<DrawingText>(), t => t.Text == "Orders growth");
    }

    [Theory]
    [InlineData("{\"type\":\"pie\"}", "pie")]
    [InlineData("{\"title\":\"x\"}", "needs a \"type\"")]
    [InlineData("{\"type\":\"growth-trend\",\"lastActuals\":3}", "lastActuals")]
    [InlineData("{\"type\":\"growth-trend\",\"months\":[{\"label\":\"Jan\",\"change\":0.1}],\"options\":{\"culture\":\"xx-nowhere\"}}", "'xx-nowhere' is not a culture")]
    [InlineData("not json", "is not valid JSON")]
    [InlineData("{\"type\":\"two-year\",\"months\":[\"Jan\"],\"series\":[{\"label\":\"a\",\"values\":[1,2]}]}", "has 2 values for 1 months")]
    public void JsonMistakes_NameTheLineAndTheProblem(string block, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => new ChartRenderer().Render(new CodeBlockRequest("financial-chart", block, Brand.Neutral) { Line = 9 }));

        Assert.StartsWith("The financial-chart code block at line 9 cannot be drawn: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InADeck_TheChartBecomesEditableShapes()
    {
        var markdown = $"## Orders\n\n```financial-chart\n{JsonSerializer.Serialize<FinancialChart>(FinancialSamples.GrowthTrend(), FinancialChartContext.Default.FinancialChart)}\n```\n";
        using var output = new MemoryStream();
        PptxGenerator.Generate(new MarkdownDocument(markdown, [], new Dictionary<string, string>()), output, new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] });
        using var package = PresentationDocument.Open(new MemoryStream(output.ToArray()), false);

        Assert.Empty(new OpenXmlValidator().Validate(package));
        var slide = package.PresentationPart!.SlideParts.Single(p => p.Slide!.Descendants<A.Text>().Any(t => t.Text == "Orders growth (July 2026)"));
        Assert.Contains(slide.Slide!.Descendants<P.GroupShape>(), g => g.Descendants<A.Text>().Any(t => t.Text == "Reference 5%"));
    }

    [Fact]
    public void TheBlock_TakesTheSlidesBrand_SoDarkSlidesGetADarkChart()
    {
        var block = JsonSerializer.Serialize<FinancialChart>(FinancialSamples.GrowthTrend(), FinancialChartContext.Default.FinancialChart);

        var dark = Assert.IsType<DrawingGraphic>(new ChartRenderer().Render(new CodeBlockRequest("financial-chart", block, ResolvedBrand.DarkSlideBrand(Brand.Neutral))));

        Assert.Equal("FFFFFF", dark.Drawing.Items.OfType<DrawingText>().First().Color);
    }

    [Fact]
    public void InAPdf_TheChartIsDrawnWithItsText()
    {
        var markdown = $"## Orders\n\n```financial-chart\n{JsonSerializer.Serialize<FinancialChart>(FinancialSamples.Summary(), FinancialChartContext.Default.FinancialChart)}\n```\n";
        using var output = new MemoryStream();
        PdfGenerator.Generate(new MarkdownDocument(markdown, [], new Dictionary<string, string>()), output, new PdfOptions { CodeBlockRenderers = [new ChartRenderer()] });

        using var pdf = PdfDocument.Open(output.ToArray());
        var text = string.Concat(pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Machines", text, StringComparison.Ordinal);
        Assert.Contains("+5.2%", text, StringComparison.Ordinal);
    }

    private static List<string> Texts(Drawing drawing) => [.. drawing.Items.OfType<DrawingText>().Select(t => t.Text)];

    private static void AssertMatchesExpected(string name, string actual, [CallerFilePath] string caller = "")
    {
        var file = name + ".expected.svg";
        if (Environment.GetEnvironmentVariable("DOCLIBS_UPDATE_EXPECTED") == "1")
        {
            var folder = Path.Combine(Path.GetDirectoryName(caller)!, "Samples", "financial");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, file), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "financial", file)).ReplaceLineEndings("\n"), actual);
    }
}
