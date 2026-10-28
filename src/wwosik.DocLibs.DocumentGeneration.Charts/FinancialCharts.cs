namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// Draws the charts of a financial review — <see cref="DevelopmentChart"/>, <see cref="GrowthByRowChart"/>,
/// <see cref="ActualVsReferenceChart"/>, <see cref="GrowthTrendChart"/>, <see cref="TwoYearChart"/> and
/// <see cref="SummaryTable"/> — as a <see cref="Drawing"/> (shapes in decks, vectors in PDFs) or as SVG with hover
/// tooltips. The rules every chart follows: a colour means a series and nothing else; verdict colours go on figures,
/// never on bars; a dash means projection and only projection; every value axis includes zero and is shaped only by
/// what is drawn.
/// </summary>
public static class FinancialCharts
{
    /// <summary>The chart as a drawing, <see cref="FinancialChartOptions.Width"/> wide.</summary>
    /// <exception cref="ArgumentException">The chart's data does not fit together, such as rows of different lengths.</exception>
    public static Drawing Draw(FinancialChart chart, FinancialChartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var canvas = new FinancialCanvas(options ?? new FinancialChartOptions());
        return chart switch
        {
            DevelopmentChart development => DevelopmentDrawing.Draw(development, canvas),
            GrowthByRowChart rows => GrowthByRowDrawing.Draw(rows, canvas),
            ActualVsReferenceChart measures => ActualVsReferenceDrawing.Draw(measures, canvas),
            GrowthTrendChart trend => GrowthTrendDrawing.Draw(trend, canvas),
            TwoYearChart years => TwoYearDrawing.Draw(years, canvas),
            SummaryTable table => SummaryTableDrawing.Draw(table, canvas),
            _ => throw new ArgumentException($"Unknown chart type {chart.GetType().Name}.", nameof(chart)),
        };
    }

    /// <summary>
    /// The chart as a standalone SVG: hover text on every bar, point and line, as tooltips that need no script (see
    /// <see cref="DrawingSvg"/>), in the brand's table font (else its text font) and tooltip colours.
    /// </summary>
    /// <exception cref="ArgumentException">The chart's data does not fit together.</exception>
    public static string ToSvg(FinancialChart chart, FinancialChartOptions? options = null)
    {
        options ??= new FinancialChartOptions();
        var brand = ResolvedBrand.Resolve(options.Brand);
        var family = brand.TableFontFamily ?? brand.Fonts?.Family;
        return DrawingSvg.Write(Draw(chart, options), new DrawingSvgOptions
        {
            FontFamily = family is null ? new DrawingSvgOptions().FontFamily : $"{family}, Arial, sans-serif",
            TooltipFill = brand.Chart.TooltipFill,
            TooltipText = brand.Chart.TooltipText,
        });
    }

    /// <summary>
    /// The colour each category of a two-year chart is drawn in, in the order of <see cref="TwoYearChart.Series"/>: the
    /// category colours handed out by how high each line runs, highest first. For a legend or table elsewhere that names
    /// the categories the same way.
    /// </summary>
    /// <exception cref="ArgumentException">The chart has more than six categories.</exception>
    public static IReadOnlyList<string> CategoryColors(TwoYearChart chart, FinancialChartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return TwoYearDrawing.Colours(chart, ResolvedBrand.Resolve((options ?? new FinancialChartOptions()).Brand).Chart);
    }
}
