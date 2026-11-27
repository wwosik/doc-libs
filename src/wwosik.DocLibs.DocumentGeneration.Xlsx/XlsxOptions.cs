using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>Options for <see cref="XlsxGenerator"/>.</summary>
public sealed record XlsxOptions
{
    /// <summary>The default options: the neutral brand, an info sheet when the workbook has a title or a description, A4.</summary>
    public static XlsxOptions Default { get; } = new();

    /// <summary>The brand, unless the workbook names another (<see cref="XlsxWorkbook.Brand"/>). Default <see cref="DocumentGeneration.Brand.Neutral"/>.</summary>
    public Brand? Brand { get; init; }

    /// <summary>
    /// The brands a workbook may name, besides <c>neutral</c> and <see cref="Brand"/>. Names must be unique. Default: none.
    /// </summary>
    public IReadOnlyList<Brand> Brands { get; init; } = [];

    /// <summary>
    /// Whether the info sheet comes first. <see langword="null"/> (the default): when the workbook has a title or a
    /// description.
    /// </summary>
    public bool? Info { get; init; }

    /// <summary>The date on the info sheet and above every table. Default <see langword="true"/>.</summary>
    public bool ShowDate { get; init; } = true;

    /// <summary>
    /// The culture of the number and date formats. Default: the workbook's language (a neutral language such as <c>de</c>
    /// as its main country, <c>de-DE</c>).
    /// </summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Texts for languages besides English and German, or in place of them, by language tag: a tag is matched in full,
    /// then by its language alone. Start each from <see cref="GeneratedTexts.English"/> with <c>with { … }</c>. Default: none.
    /// </summary>
    public IReadOnlyDictionary<string, GeneratedTexts> Texts { get; init; } = new Dictionary<string, GeneratedTexts>();

    /// <summary>
    /// The host's classification marking. The workbook's own <see cref="XlsxWorkbook.Marking"/> can raise it but not
    /// lower it. Default: none.
    /// </summary>
    public XlsxMarking? Marking { get; init; }

    /// <summary>
    /// Markdown only: the version to generate (<see cref="DocumentGeneration.Audience"/>), which wins over the front
    /// matter's <c>audience</c>. Default <see langword="null"/>: the front matter's, else <see cref="Audience.Customer"/>.
    /// </summary>
    public Audience? Audience { get; init; }

    /// <summary>The paper size when printed. Default <see cref="XlsxPageSize.A4"/>.</summary>
    public XlsxPageSize PageSize { get; init; } = XlsxPageSize.A4;

    /// <summary>
    /// The clock for today's date (without <see cref="XlsxWorkbook.Date"/>) and for the created and modified dates in the
    /// core properties. Default <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>Paper sizes of a printed workbook.</summary>
public enum XlsxPageSize
{
    /// <summary>210 × 297 mm.</summary>
    A4,

    /// <summary>297 × 420 mm.</summary>
    A3,

    /// <summary>8.5 × 11 in.</summary>
    Letter,
}
