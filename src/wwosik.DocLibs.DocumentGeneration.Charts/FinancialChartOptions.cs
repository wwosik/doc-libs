using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>How <see cref="FinancialCharts"/> draws a chart: colours, number format, labels and tolerances.</summary>
public sealed record FinancialChartOptions
{
    /// <summary>The brand whose chart colours (<see cref="BrandColors.Chart"/>) are used. Default <see cref="Brand.Neutral"/>.</summary>
    public Brand Brand { get; init; } = Brand.Neutral;

    /// <summary>The culture numbers are written in, such as <c>pl-PL</c> for <c>10,2%</c>. Default: the invariant culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>What values are divided by before they are written, such as 1 000 000 for millions. Default 1.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>The unit written after values, such as <c>M</c>. Default: none.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>The words the charts print for their series.</summary>
    public FinancialChartLabels Labels { get; init; } = new();

    /// <summary>
    /// How far a figure may be from what it is measured against and still count as level, as a fraction: 0.005 (the
    /// default) is half a percentage point.
    /// </summary>
    public double Tolerance { get; init; } = 0.005;

    /// <summary>
    /// The share of the total column below which a summary table's rest column states its value but no comparisons:
    /// a growth rate of a remainder is arithmetic between two accidents. Default 0.005 (0.5 %).
    /// </summary>
    public double RestShare { get; init; } = 0.005;

    /// <summary>The width of the charts in pixels (the summary table sizes itself). Default 560: two fit side by side on a screen.</summary>
    public double Width { get; init; } = 560;
}

/// <summary>The words the financial charts print, in legends, labels and tooltips. Set them to the reader's terms or abbreviations.</summary>
public sealed record FinancialChartLabels
{
    /// <summary>The previous year. Default <c>Previous</c>.</summary>
    public string Previous { get; init => field = value ?? "Previous"; } = "Previous";

    /// <summary>The current year's actuals. Default <c>Current</c>.</summary>
    public string Current { get; init => field = value ?? "Current"; } = "Current";

    /// <summary>The reference (target). Default <c>Reference</c>.</summary>
    public string Reference { get; init => field = value ?? "Reference"; } = "Reference";

    /// <summary>The projection (forecast). Default <c>Projection</c>.</summary>
    public string Projection { get; init => field = value ?? "Projection"; } = "Projection";

    /// <summary>An estimate. Default <c>Estimate</c>.</summary>
    public string Estimate { get; init => field = value ?? "Estimate"; } = "Estimate";

    /// <summary>The change against the previous year. Default <c>Change</c>.</summary>
    public string Change { get; init => field = value ?? "Change"; } = "Change";

    /// <summary>The figures of all rows together. Default <c>Overall</c>.</summary>
    public string Overall { get; init => field = value ?? "Overall"; } = "Overall";

    /// <summary>A summary table's value row. Default <c>Value</c>.</summary>
    public string Value { get; init => field = value ?? "Value"; } = "Value";
}
