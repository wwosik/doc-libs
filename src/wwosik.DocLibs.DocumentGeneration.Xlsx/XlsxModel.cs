using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// A workbook for <see cref="XlsxGenerator"/>: what its info sheet says, and its sheets, each one table. Everything but
/// <see cref="Sheets"/> is optional.
/// </summary>
public sealed record XlsxWorkbook
{
    /// <summary>The title: on the info sheet, in the printed page header and in the core properties.</summary>
    public string? Title { get; init; }

    /// <summary>The subtitle: on the info sheet under the title, and the subject in the core properties.</summary>
    public string? Subtitle { get; init; }

    /// <summary>What the workbook holds: on the info sheet, and the description in the core properties.</summary>
    public string? Description { get; init; }

    /// <summary>The author: on the info sheet, and the creator in the core properties.</summary>
    public string? Author { get; init; }

    /// <summary>The date shown on the info sheet and above every table. Default: today, from <see cref="XlsxOptions.TimeProvider"/>.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>
    /// The language, a BCP 47 tag such as <c>en</c>, <c>de</c> or <c>de-CH</c>: it picks the texts the generator writes
    /// itself (<see cref="XlsxOptions.Texts"/>) and, unless <see cref="XlsxOptions.Culture"/> is set, the culture of the
    /// number and date formats. Default <c>en</c>.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// The classification marking: on the info sheet and in the printed page header, never in the properties. It can
    /// raise <see cref="XlsxOptions.Marking"/> but not lower it, like front matter.
    /// </summary>
    public XlsxMarking? Marking { get; init; }

    /// <summary>
    /// The name of the brand: <c>neutral</c>, the brand of <see cref="XlsxOptions.Brand"/> or one of
    /// <see cref="XlsxOptions.Brands"/>. Default: <see cref="XlsxOptions.Brand"/>.
    /// </summary>
    public string? Brand { get; init; }

    /// <summary>The sheets, in order; the info sheet comes before them.</summary>
    public IReadOnlyList<XlsxSheet> Sheets { get; init; } = [];
}

/// <summary>A classification and whether the workbook is a draft: "Confidential · Draft".</summary>
/// <param name="Classification">How confidential the workbook is.</param>
/// <param name="Draft">Whether it is a draft.</param>
public sealed record XlsxMarking(Classification Classification, bool Draft = false);

/// <summary>A sheet holding one table: a title and a description above it, the header row, the rows.</summary>
public sealed record XlsxSheet
{
    /// <summary>
    /// The sheet's name on its tab, by Excel's rules: 1 to 31 characters, none of <c>[]:*?/\</c>, not starting or ending
    /// with <c>'</c>, and unique ignoring case.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The title above the table and in the info sheet's contents list.</summary>
    public string? Title { get; init; }

    /// <summary>The line under the title, and the text beside the sheet in the contents list.</summary>
    public string? Description { get; init; }

    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<XlsxColumn> Columns { get; init; } = [];

    /// <summary>The rows, each at most as many cells as there are columns; a short row is filled with empty cells.</summary>
    public IReadOnlyList<IReadOnlyList<XlsxCell>> Rows { get; init; } = [];

    /// <summary>
    /// The label of the totals row, in the first column without a <see cref="XlsxColumn.Total"/>. Default:
    /// <see cref="GeneratedTexts.Total"/> ("Total").
    /// </summary>
    public string? TotalsLabel { get; init; }

    /// <summary>The sheet's native charts on its table's cells, beside the table or each on a chart sheet of its own.</summary>
    public IReadOnlyList<XlsxChart> Charts { get; init; } = [];
}

/// <summary>
/// A native chart whose series refer to the cells of its sheet's table, so that changing a value changes the chart: the
/// categories from one column, a series from each further column.
/// </summary>
public sealed record XlsxChart
{
    /// <summary>The header of the column whose values label the categories; any type. Numbers and dates keep their format on the axis.</summary>
    public required string Category { get; init; }

    /// <summary>The headers of the columns drawn as series, in order: each a number, integer, currency or percent column.</summary>
    public IReadOnlyList<string> Series { get; init; } = [];

    /// <summary>Upright columns, bars across, lines or areas. Default <see cref="XlsxChartKind.Column"/>.</summary>
    public XlsxChartKind Kind { get; init; } = XlsxChartKind.Column;

    /// <summary>Columns, bars or areas stacked on each other rather than side by side or overlapping; lines ignore it.</summary>
    public bool Stacked { get; init; }

    /// <summary>The chart's title; <see langword="null"/>: none.</summary>
    public string? Title { get; init; }

    /// <summary>The unit of the values, shown with the value axis (<c>k</c>, <c>%</c>); the axis also shows the first series' number format.</summary>
    public string? Unit { get; init; }

    /// <summary>Where the data comes from: a second, smaller line of the title, after <see cref="GeneratedTexts.Source"/> ("Source: Annual report").</summary>
    public string? Source { get; init; }

    /// <summary>Beside the table, or on a chart sheet of its own after the table's sheet. Default <see cref="XlsxChartPlacement.Beside"/>.</summary>
    public XlsxChartPlacement Placement { get; init; } = XlsxChartPlacement.Beside;

    /// <summary>
    /// The name of the chart sheet, for <see cref="XlsxChartPlacement.Sheet"/>, by the rules of <see cref="XlsxSheet.Name"/>.
    /// Default: <see cref="GeneratedTexts.Chart"/> with the number of the chart sheet in the workbook ("Chart 1").
    /// </summary>
    public string? SheetName { get; init; }
}

/// <summary>The kinds of workbook chart.</summary>
public enum XlsxChartKind
{
    /// <summary>Upright columns, clustered or stacked.</summary>
    Column,

    /// <summary>Bars across, the categories down the left from top to bottom, clustered or stacked.</summary>
    Bar,

    /// <summary>Lines with markers.</summary>
    Line,

    /// <summary>Filled areas, overlapping or stacked.</summary>
    Area,
}

/// <summary>Where a workbook chart stands.</summary>
public enum XlsxChartPlacement
{
    /// <summary>On the table's sheet, right of the table, 8 columns by 16 rows; further charts below it.</summary>
    Beside,

    /// <summary>On a chart sheet of its own, right after the table's sheet, linked from the info sheet's contents.</summary>
    Sheet,
}

/// <summary>A column of a sheet's table: its header and the type of its values.</summary>
/// <param name="Header">The text of the header row, and the name a formula refers to it by: <c>[Header]</c>.</param>
/// <param name="Type">
/// What the values are, and so how they are shown. Default: <see cref="XlsxColumnType.Text"/>; for a column of formulas,
/// <see cref="XlsxColumnType.Number"/>, or <see cref="XlsxColumnType.Currency"/> when its formulas refer only to
/// currency columns of one currency.
/// </param>
public sealed record XlsxColumn(string Header, XlsxColumnType? Type = null)
{
    /// <summary>
    /// The column's total in the totals row under the table, written as <c>SUBTOTAL</c> so that a filter changes it to the
    /// visible rows. Only <see cref="XlsxTotal.Count"/> on a text or boolean column. Default: none.
    /// </summary>
    public XlsxTotal? Total { get; init; }

    /// <summary>
    /// Digits after the decimal point, 0 to 10, for <see cref="XlsxColumnType.Number"/>, <see cref="XlsxColumnType.Currency"/>
    /// and <see cref="XlsxColumnType.Percent"/>. Default: the culture's (the currency's for a currency of another country).
    /// </summary>
    public int? Decimals { get; init; }

    /// <summary>
    /// The ISO 4217 code of a <see cref="XlsxColumnType.Currency"/> column, such as <c>EUR</c>; its symbol is shown where
    /// the culture puts its own. Default: the culture's currency.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>The width in characters, 1 to 255. Default: measured from the header and the values, 6 to 50.</summary>
    public double? Width { get; init; }

    /// <summary>The alignment. Default: text left, booleans centred, numbers and dates right.</summary>
    public XlsxAlign? Align { get; init; }

    /// <summary>
    /// Markdown: the column's type was inferred, and the values that did not fit it stay text (<c>column-mixed</c>), so a
    /// text cell is no problem here.
    /// </summary>
    internal bool KeepsText { get; init; }
}

/// <summary>What the values of a column are.</summary>
public enum XlsxColumnType
{
    /// <summary>Text, kept as written: <c>01234</c> stays <c>01234</c>.</summary>
    Text,

    /// <summary>A number with the culture's decimals and thousands separator.</summary>
    Number,

    /// <summary>A whole number with the thousands separator.</summary>
    Integer,

    /// <summary>An amount with a currency symbol (<see cref="XlsxColumn.Currency"/>).</summary>
    Currency,

    /// <summary>A share, stored as a fraction: <c>0.25</c> is shown as 25 %.</summary>
    Percent,

    /// <summary>A date in the culture's short format.</summary>
    Date,

    /// <summary>A date and time in the culture's short formats.</summary>
    DateTime,

    /// <summary>A truth value, which Excel shows as TRUE or FALSE in its own language.</summary>
    Boolean,
}

/// <summary>What a column's total is.</summary>
public enum XlsxTotal
{
    /// <summary>The sum of the visible values: <c>SUBTOTAL(109, …)</c>.</summary>
    Sum,

    /// <summary>Their average: <c>SUBTOTAL(101, …)</c>.</summary>
    Average,

    /// <summary>How many visible cells are not empty: <c>SUBTOTAL(103, …)</c>.</summary>
    Count,

    /// <summary>The smallest visible value: <c>SUBTOTAL(105, …)</c>.</summary>
    Min,

    /// <summary>The largest visible value: <c>SUBTOTAL(104, …)</c>.</summary>
    Max,
}

/// <summary>The horizontal alignment of a column.</summary>
public enum XlsxAlign
{
    /// <summary>At the left.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>At the right.</summary>
    Right,
}

/// <summary>What an <see cref="XlsxCell"/> holds.</summary>
public enum XlsxCellKind
{
    /// <summary>Nothing.</summary>
    Empty,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A date without a time.</summary>
    Date,

    /// <summary>A date and time.</summary>
    DateTime,

    /// <summary>A truth value.</summary>
    Boolean,

    /// <summary>A formula, which Excel computes on opening.</summary>
    Formula,
}

/// <summary>
/// One value of a row: text, a number, a date, a date and time, a truth value, a formula (<see cref="Formula"/>), or nothing. Converts implicitly from
/// <see cref="string"/>, <see cref="int"/>, <see cref="long"/>, <see cref="double"/>, <see cref="decimal"/>,
/// <see cref="DateOnly"/>, <see cref="System.DateTime"/> and <see cref="bool"/>; a <see langword="null"/> or empty string is
/// <see cref="Empty"/>.
/// </summary>
public sealed record XlsxCell
{
    private XlsxCell(XlsxCellKind kind, string? text = null, double number = 0, DateTime dateTime = default, bool boolean = false)
    {
        this.Kind = kind;
        this.Text = text;
        this.Number = number;
        this.DateTime = dateTime;
        this.Boolean = boolean;
    }

    /// <summary>An empty cell.</summary>
    public static XlsxCell Empty { get; } = new(XlsxCellKind.Empty);

    /// <summary>What the cell holds.</summary>
    public XlsxCellKind Kind { get; }

    /// <summary>
    /// The text of a <see cref="XlsxCellKind.Text"/> cell, or the formula of a <see cref="XlsxCellKind.Formula"/> cell
    /// without its leading <c>=</c>; else <see langword="null"/>.
    /// </summary>
    public string? Text { get; }

    /// <summary>The value of a <see cref="XlsxCellKind.Number"/> cell; else 0.</summary>
    public double Number { get; }

    /// <summary>
    /// The value of a <see cref="XlsxCellKind.Date"/> (at midnight) or <see cref="XlsxCellKind.DateTime"/> cell, as the
    /// reader's clock shows it (its <see cref="System.DateTime.Kind"/> is ignored); else <see langword="default"/>.
    /// </summary>
    public DateTime DateTime { get; }

    /// <summary>The value of a <see cref="XlsxCellKind.Boolean"/> cell; else <see langword="false"/>.</summary>
    public bool Boolean { get; }

    /// <summary>A text cell; <see langword="null"/> or empty gives <see cref="Empty"/>.</summary>
    public static XlsxCell FromText(string? text) => string.IsNullOrEmpty(text) ? Empty : new(XlsxCellKind.Text, text);

    /// <summary>A number cell.</summary>
    public static XlsxCell FromNumber(double number) => new(XlsxCellKind.Number, number: number);

    /// <summary>A date cell.</summary>
    public static XlsxCell FromDate(DateOnly date) => new(XlsxCellKind.Date, dateTime: date.ToDateTime(TimeOnly.MinValue));

    /// <summary>A date-and-time cell.</summary>
    public static XlsxCell FromDateTime(DateTime dateTime) => new(XlsxCellKind.DateTime, dateTime: DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified));

    /// <summary>A truth-value cell.</summary>
    public static XlsxCell FromBoolean(bool value) => new(XlsxCellKind.Boolean, boolean: value);

    /// <summary>
    /// A formula in Excel's English form, with or without the leading <c>=</c>: <c>[Price]*[Quantity]</c>,
    /// <c>IF([Paid],0,[Amount])</c>, <c>SUM(B{row}:D{row})</c>. <c>[Header]</c> is the cell of the column with that header in
    /// the same row, <c>{row}</c> the row's number; A1 references are kept as written. Excel computes it on opening; the
    /// workbook stores no value for it.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="formula"/> is empty.</exception>
    public static XlsxCell Formula(string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        var text = formula.Trim();
        return new(XlsxCellKind.Formula, text.StartsWith('=') ? text[1..].TrimStart() : text);
    }

    /// <summary>A text cell, or <see cref="Empty"/>.</summary>
    public static implicit operator XlsxCell(string? text) => FromText(text);

    /// <summary>A number cell.</summary>
    public static implicit operator XlsxCell(int number) => FromNumber(number);

    /// <summary>A number cell.</summary>
    public static implicit operator XlsxCell(long number) => FromNumber(number);

    /// <summary>A number cell.</summary>
    public static implicit operator XlsxCell(double number) => FromNumber(number);

    /// <summary>A number cell.</summary>
    public static implicit operator XlsxCell(decimal number) => FromNumber((double)number);

    /// <summary>A date cell.</summary>
    public static implicit operator XlsxCell(DateOnly date) => FromDate(date);

    /// <summary>A date-and-time cell.</summary>
    public static implicit operator XlsxCell(DateTime dateTime) => FromDateTime(dateTime);

    /// <summary>A truth-value cell.</summary>
    public static implicit operator XlsxCell(bool value) => FromBoolean(value);

    /// <summary>The value, written invariantly: text as it is, a formula with its <c>=</c>, <c>1234.5</c>, <c>2026-03-15</c>, <c>2026-03-15T14:30:00</c>, <c>true</c>; empty for an empty cell.</summary>
    public override string ToString() => this.Kind switch
    {
        XlsxCellKind.Text => this.Text!,
        XlsxCellKind.Number => this.Number.ToString("R", CultureInfo.InvariantCulture),
        XlsxCellKind.Date => this.DateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        XlsxCellKind.DateTime => this.DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        XlsxCellKind.Boolean => this.Boolean ? "true" : "false",
        XlsxCellKind.Formula => "=" + this.Text,
        _ => string.Empty,
    };
}
