using System.Text.Json.Serialization;

namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A chart of a financial review, drawn by <see cref="FinancialCharts"/>: <see cref="DevelopmentChart"/>,
/// <see cref="GrowthByRowChart"/>, <see cref="ActualVsReferenceChart"/>, <see cref="GrowthTrendChart"/>,
/// <see cref="TwoYearChart"/> or <see cref="SummaryTable"/>. Figures are plain numbers; growth rates and shares are
/// fractions (<c>0.052</c> is 5.2 %). The caller decides which months are actuals.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(DevelopmentChart), "development")]
[JsonDerivedType(typeof(GrowthByRowChart), "growth-by-row")]
[JsonDerivedType(typeof(ActualVsReferenceChart), "actual-vs-reference")]
[JsonDerivedType(typeof(GrowthTrendChart), "growth-trend")]
[JsonDerivedType(typeof(TwoYearChart), "two-year")]
[JsonDerivedType(typeof(SummaryTable), "summary-table")]
public abstract record FinancialChart
{
    private protected FinancialChart()
    {
    }

    /// <summary>The title, drawn bold at the top.</summary>
    public string Title { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>The period the chart is about, such as <c>July 2026</c>; drawn after the title in brackets. Optional.</summary>
    public string? Period { get; init; }

    /// <summary>The line under the title; <see langword="null"/>: the chart's own, empty: none.</summary>
    public string? Subtitle { get; init; }
}

/// <summary>Which form the lower panel of a <see cref="DevelopmentChart"/> takes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DevelopmentPanel>))]
public enum DevelopmentPanel
{
    /// <summary>The year cumulated: the actuals as an area, previous year, reference and projection as lines.</summary>
    Cumulative,

    /// <summary>The cumulative growth rate against the previous year, with the flat reference rate.</summary>
    Growth,
}

/// <summary>
/// A year month by month, in two panels over the same month slots: per month above (the previous year with the
/// reference increment stacked on it, the current year beside it, the projection as a pale dashed bar), cumulated or
/// as a growth rate below. A dashed rule marks where the actuals end.
/// </summary>
public sealed record DevelopmentChart : FinancialChart
{
    /// <summary>The months, usually twelve, in order.</summary>
    public IReadOnlyList<DevelopmentMonth> Months { get; init => field = value ?? []; } = [];

    /// <summary>The index into <see cref="Months"/> of the last month with actuals.</summary>
    public int LastActual { get; init; }

    /// <summary>
    /// The index of the first month whose actual bar also shows the projection for that month, overlapping it (the
    /// shorter bar in front); <see langword="null"/>: none. Months after <see cref="LastActual"/> show the projection
    /// alone either way.
    /// </summary>
    public int? ProjectionComparedFrom { get; init; }

    /// <summary>The annual reference growth rate both reference series come from, named in the legend.</summary>
    public double ReferenceRate { get; init; }

    /// <summary>The change against the previous year at the last actual month, stated in the subtitle.</summary>
    public double ChangeAtPeriod { get; init; }

    /// <summary>The projection's change against the previous year at year end, stated in the subtitle; optional.</summary>
    public double? ChangeAtYearEnd { get; init; }

    /// <summary>Estimates, drawn as diamonds on the cumulative panel at their months.</summary>
    public IReadOnlyList<EstimateMark> Estimates { get; init => field = value ?? []; } = [];

    /// <summary>The lower panel's form. Default <see cref="DevelopmentPanel.Cumulative"/>.</summary>
    public DevelopmentPanel LowerPanel { get; init; }

    /// <summary>The width of the cumulative lines in pixels. Default 2.</summary>
    public double CumulativeLineWidth { get; init => field = value > 0 ? value : 2; } = 2;

    /// <summary>Whether estimates are drawn as bare diamonds, without their label and figure. Default off.</summary>
    public bool HideEstimateLabels { get; init; }
}

/// <summary>One month of a <see cref="DevelopmentChart"/>: each figure for the month itself and cumulated to it.</summary>
/// <param name="Label">The month as the axis names it, such as <c>Jan</c>.</param>
/// <param name="Previous">The previous year's figure for the month.</param>
/// <param name="Current">The current year's actual; <see langword="null"/> for a month without actuals.</param>
/// <param name="Projection">The projection for the month; <see langword="null"/>: none.</param>
/// <param name="PreviousCumulative">The previous year to this month.</param>
/// <param name="CurrentCumulative">The current year to this month; <see langword="null"/> without actuals.</param>
/// <param name="ProjectionCumulative">The projection to this month; <see langword="null"/>: none.</param>
/// <param name="Reference">The reference for the month: the previous year grown by the reference rate.</param>
/// <param name="ReferenceCumulative">The reference to this month.</param>
/// <param name="Growth">The cumulative growth rate (actual, then projected) that a growth panel draws; optional.</param>
public sealed record DevelopmentMonth(
    string Label,
    double Previous,
    double? Current,
    double? Projection,
    double PreviousCumulative,
    double? CurrentCumulative,
    double? ProjectionCumulative,
    double Reference,
    double ReferenceCumulative,
    double? Growth = null);

/// <summary>An estimate: the month it lands on (an index into the months), its label and its cumulative figure.</summary>
/// <param name="Month">The index of the month.</param>
/// <param name="Label">What the estimate is called.</param>
/// <param name="Value">The cumulative figure it expects.</param>
public sealed record EstimateMark(int Month, string Label, double Value);

/// <summary>
/// Growth per row as horizontal bars diverging from zero: the actual change filled, the reference as an outline, two
/// dashed lines for the overall change and reference, and a table of the figures beside the plot.
/// </summary>
public sealed record GrowthByRowChart : FinancialChart
{
    /// <summary>The rows, top to bottom.</summary>
    public IReadOnlyList<GrowthRow> Rows { get; init => field = value ?? []; } = [];

    /// <summary>The figures of all rows together, drawn as the two dashed lines.</summary>
    public GrowthFigures Overall { get; init => field = value ?? new(0, 0); } = new(0, 0);
}

/// <summary>One row of a <see cref="GrowthByRowChart"/>.</summary>
/// <param name="Label">The row's name; wrapped by words.</param>
/// <param name="Change">The change against the previous year.</param>
/// <param name="Reference">The reference growth rate.</param>
/// <param name="Emphasised">Whether the row is the one to read: a stronger fill and a bold label.</param>
public sealed record GrowthRow(string Label, double Change, double Reference, bool Emphasised = false);

/// <summary>A change and the reference growth rate it is read against.</summary>
/// <param name="Change">The change against the previous year.</param>
/// <param name="Reference">The reference growth rate.</param>
public sealed record GrowthFigures(double Change, double Reference);

/// <summary>Whether an <see cref="ActualVsReferenceChart"/> draws growth rates or values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceDisplay>))]
public enum ReferenceDisplay
{
    /// <summary>Growth rates against the previous year, as percentages.</summary>
    Growth,

    /// <summary>Values at the options' scale and unit.</summary>
    Value,
}

/// <summary>
/// Measures per row against their references: a band per row, a bar per measure (the month solid, the year to date
/// pale, a hue per series), the reference as an outline over the year-to-date bars. A bar far beyond the others is
/// clipped at the edge with an arrowhead, and its label states the true figure.
/// </summary>
public sealed record ActualVsReferenceChart : FinancialChart
{
    /// <summary>The rows, top to bottom; every row has the same measures in the same order.</summary>
    public IReadOnlyList<MeasureRow> Rows { get; init => field = value ?? []; } = [];

    /// <summary>Growth rates or values. Default <see cref="ReferenceDisplay.Growth"/>.</summary>
    public ReferenceDisplay Display { get; init; }
}

/// <summary>One row of an <see cref="ActualVsReferenceChart"/>.</summary>
/// <param name="Label">The row's name; wrapped by words.</param>
/// <param name="Measures">The row's measures, in the order the bars are drawn.</param>
/// <param name="Aggregate">Whether the row adds up others: bold and flush left, while the rest read as indented.</param>
public sealed record MeasureRow(string Label, IReadOnlyList<Measure> Measures, bool Aggregate = false);

/// <summary>One bar of a <see cref="MeasureRow"/>.</summary>
/// <param name="Series">The series it belongs to, such as a metric; measures of one series share a hue.</param>
/// <param name="Label">The measure's name in the legend and tooltips.</param>
/// <param name="YearToDate">Whether it is a year-to-date figure: drawn pale, with its reference as an outline.</param>
/// <param name="Current">The current figure (value display).</param>
/// <param name="Change">The change against the previous year (growth display).</param>
/// <param name="Reference">The reference growth rate (growth display).</param>
/// <param name="ReferenceValue">The value the reference implies (value display).</param>
public sealed record Measure(string Series, string Label, bool YearToDate, double Current, double Change, double Reference, double ReferenceValue);

/// <summary>
/// One growth rate over the months: solid through the last actual, then a dashed projection continuing from the last
/// actual point, against the flat reference rate.
/// </summary>
public sealed record GrowthTrendChart : FinancialChart
{
    /// <summary>The months, usually twelve, in order.</summary>
    public IReadOnlyList<TrendMonth> Months { get; init => field = value ?? []; } = [];

    /// <summary>The index into <see cref="Months"/> of the last month with actuals.</summary>
    public int LastActual { get; init; }

    /// <summary>The flat reference growth rate.</summary>
    public double Reference { get; init; }
}

/// <summary>One month of a <see cref="GrowthTrendChart"/>.</summary>
/// <param name="Label">The month as the axis names it.</param>
/// <param name="Change">The cumulative growth rate: actual through the last actual month, projected after; <see langword="null"/>: none.</param>
public sealed record TrendMonth(string Label, double? Change);

/// <summary>
/// Two years month by month, one line per category over (usually) 24 slots, with a dashed rule at the year boundary.
/// Categories are coloured by how high their lines run, the highest in the first category colour, so neighbouring
/// lines get the most different colours. At most six categories.
/// </summary>
public sealed record TwoYearChart : FinancialChart
{
    /// <summary>The slot labels: the previous year's months, then the current year's.</summary>
    public IReadOnlyList<string> Months { get; init => field = value ?? []; } = [];

    /// <summary>The previous year's label, under the first half.</summary>
    public string PreviousYear { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>The current year's label, under the second half.</summary>
    public string CurrentYear { get; init => field = value ?? string.Empty; } = string.Empty;

    /// <summary>The categories; each has one value per slot, <see langword="null"/> for a month without one.</summary>
    public IReadOnlyList<TwoYearSeries> Series { get; init => field = value ?? []; } = [];
}

/// <summary>One category of a <see cref="TwoYearChart"/>.</summary>
/// <param name="Label">The category's name in the legend and tooltips.</param>
/// <param name="Values">One value per slot; <see langword="null"/> breaks the line.</param>
public sealed record TwoYearSeries(string Label, IReadOnlyList<double?> Values);

/// <summary>
/// A table of figures per column, in blocks of rows per metric: a header band, the total column emphasised, and only
/// the comparison rows coloured, by verdict. A rest column is drawn only when it holds something, and a remainder
/// too small to compare states its value alone.
/// </summary>
public sealed record SummaryTable : FinancialChart
{
    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<SummaryColumn> Columns { get; init => field = value ?? []; } = [];

    /// <summary>The blocks, top to bottom; every block has one cell per column.</summary>
    public IReadOnlyList<SummaryBlock> Blocks { get; init => field = value ?? []; } = [];
}

/// <summary>One column of a <see cref="SummaryTable"/>.</summary>
/// <param name="Label">The header.</param>
/// <param name="Total">Whether it totals the others: a darker header, a pale fill, bold figures and a gap after it.</param>
/// <param name="Rest">Whether it holds what no other column claimed: drawn only when it holds something.</param>
public sealed record SummaryColumn(string Label, bool Total = false, bool Rest = false);

/// <summary>
/// One metric's block of a <see cref="SummaryTable"/>. An amount block states the value, the change and its gap to
/// the reference; a rate block (<see cref="RateOf"/> set) states the value, both shares and their distances in points.
/// </summary>
/// <param name="Metric">The metric's name, the block's heading.</param>
/// <param name="Reference">The block's reference: a growth rate, or for a rate block points on the share.</param>
/// <param name="Cells">One cell per column.</param>
/// <param name="RateOf">The metric the shares are of; <see langword="null"/>: an amount block.</param>
public sealed record SummaryBlock(string Metric, double Reference, IReadOnlyList<SummaryCell> Cells, string? RateOf = null);

/// <summary>One cell of a <see cref="SummaryBlock"/>.</summary>
/// <param name="Current">The current figure.</param>
/// <param name="Previous">The previous year's figure.</param>
/// <param name="Change">The change against the previous year.</param>
/// <param name="Reference">The reference growth rate for this cell (amount blocks).</param>
public sealed record SummaryCell(double Current, double Previous, double Change, double Reference)
{
    /// <summary>The current share of the block's <see cref="SummaryBlock.RateOf"/>; <see langword="null"/>: none.</summary>
    public double? Rate { get; init; }

    /// <summary>The previous year's share.</summary>
    public double? PreviousRate { get; init; }

    /// <summary>The share the reference implies.</summary>
    public double? ReferenceRate { get; init; }
}
