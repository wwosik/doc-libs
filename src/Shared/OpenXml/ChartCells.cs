namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Where a chart's data stands in the cells of a workbook, for <see cref="ChartWriter.Write"/>: the chart then refers to
/// those cells instead of carrying a workbook of its own. Shared source: a workbook's own sheets use it, and a deck or a
/// Word document can use it for a chart on cells of a workbook the caller passes.
/// </summary>
/// <param name="Sheet">The sheet's name, unquoted: <c>Sales 2026</c>.</param>
/// <param name="Categories">The range of the categories, absolute, without the sheet: <c>$A$6:$A$9</c>.</param>
/// <param name="Values">The range of each series' values, in the order of <see cref="ChartSpec.Series"/>: <c>$B$6:$B$9</c>.</param>
/// <param name="Names">The cell of each series' name, its column header: <c>$B$5</c>.</param>
internal sealed record ChartCells(string Sheet, string Categories, IReadOnlyList<string> Values, IReadOnlyList<string> Names)
{
    /// <summary>
    /// The categories as numbers, one per category (<see langword="null"/> where empty): dates as serial numbers. Set,
    /// the categories are a number reference in <see cref="CategoryFormat"/>; <see langword="null"/>: text, as
    /// <see cref="ChartSpec.Categories"/>.
    /// </summary>
    public IReadOnlyList<double?>? CategoryNumbers { get; init; }

    /// <summary>The number format of numeric or date categories, as in the cells; <see langword="null"/>: <c>General</c>.</summary>
    public string? CategoryFormat { get; init; }

    /// <summary>The number format of each series' values, as in the cells; a missing or <see langword="null"/> one is <c>General</c>.</summary>
    public IReadOnlyList<string?> ValueFormats { get; init; } = [];

    /// <summary>Bars run across, the categories down the left from top to bottom; only for <see cref="ChartKind.Bar"/>.</summary>
    public bool Horizontal { get; init; }

    /// <summary>
    /// A second, smaller line of the chart's title, such as "Source: Annual report", already worded; <see langword="null"/>:
    /// none.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>The sheet's name as a reference prefix: <c>'Sales 2026'!</c>, an apostrophe doubled.</summary>
    public string Prefix => "'" + this.Sheet.Replace("'", "''", StringComparison.Ordinal) + "'!";
}
