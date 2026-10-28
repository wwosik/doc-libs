namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// Renders <c>chart</c> code blocks as bar, line and area charts: a <see cref="ChartGraphic"/> that decks write as a
/// native, editable PowerPoint chart and PDFs draw as vectors. Also renders <c>financial-chart</c> blocks, a
/// <see cref="FinancialChart"/> as JSON, as drawings. Register it in the generators' options
/// (<c>CodeBlockRenderers = [new ChartRenderer()]</c>), next to other renderers.
/// </summary>
/// <remarks>
/// The block holds optional settings, a line <c>---</c>, and CSV data: a header row (the category column, then one
/// column per series) and a row per category.
/// <code>
/// type: bar
/// title: Orders by month
/// unit: k
/// ---
/// month, plan, actual
/// Jan, 120, 132
/// Feb, 118, 121
/// </code>
/// </remarks>
public sealed class ChartRenderer : ICodeBlockRenderer
{
    /// <summary>The <c>## Charts</c> section of the generators' authoring guides: the syntax, with an example.</summary>
    public string AuthoringGuide => AuthoringGuides.Template(typeof(ChartRenderer).Assembly, "Charts");

    /// <summary>
    /// A <see cref="ChartGraphic"/> for a <c>chart</c> block, a <see cref="DrawingGraphic"/> for a <c>financial-chart</c>
    /// block (JSON, see <see cref="FinancialCharts"/>), both case-insensitive; <see langword="null"/> for other languages.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A setting or the data is malformed; the message names the block's line. The generators pass it on as the inner
    /// exception of a <c>DocumentConversionException</c>.
    /// </exception>
    public CodeBlockGraphic? Render(CodeBlockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var language = request.Language.Trim();
        if (language.Equals("financial-chart", StringComparison.OrdinalIgnoreCase))
        {
            // The block's brand is the slide's: on dark slides it is the dark one, so the chart is drawn for a dark background.
            var (financial, options) = FinancialChartJson.Parse(request.Code, request.Line, request.Brand);
            try
            {
                return new DrawingGraphic(FinancialCharts.Draw(financial, options));
            }
            catch (ArgumentException ex)
            {
                throw FinancialChartJson.Error(request.Line, ex.Message);
            }
        }

        if (!language.Equals("chart", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var brand = ResolvedBrand.Resolve(request.Brand);
        var chart = ChartParser.Parse(request.Code, request.Line) with { Colors = brand.Diagram.Palette };
        return new ChartGraphic(chart, ChartDrawing.Draw(chart, brand.Diagram));
    }
}
