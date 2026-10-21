namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>Options for <see cref="PdfGenerator.Generate"/>.</summary>
public sealed record PdfOptions
{
    /// <summary>The default options: neutral design, A4 portrait, code blocks stay code.</summary>
    public static PdfOptions Default { get; } = new();

    /// <summary>The design and its features. Default: the neutral brand. Front matter in the Markdown overrides it.</summary>
    public PdfTemplateOptions Template { get; init; } = new BrandPdfTemplateOptions();

    /// <summary>
    /// The brands front matter may select by name (<c>template: acme</c>), besides <c>neutral</c> and the brand of
    /// <see cref="Template"/>. Names must be unique. Default: none.
    /// </summary>
    public IReadOnlyList<Brand> Brands { get; init; } = [];

    /// <summary>The paper size. Default <see cref="PdfPageSize.A4"/>.</summary>
    public PdfPageSize PageSize { get; init; } = PdfPageSize.A4;

    /// <summary>Default <see cref="PdfOrientation.Portrait"/>.</summary>
    public PdfOrientation Orientation { get; init; } = PdfOrientation.Portrait;

    /// <summary>
    /// Asked in order for every fenced code block; the first graphic wins. A drawing is drawn as vector graphics in the
    /// brand's diagram font, an image (PNG or JPEG) is placed like a Markdown image, and a block no renderer takes
    /// stays code. This is how diagrams such as Mermaid get in. Default: none.
    /// </summary>
    public IReadOnlyList<ICodeBlockRenderer> CodeBlockRenderers { get; init; } = [];

    /// <summary>
    /// Called with each text element (grapheme cluster) that has a character the font of its text lacks — so
    /// <c>👍🏽</c>, <c>❤️</c> or <c>👨‍👩‍👧</c> is one call — and returns the text to put there instead, which the
    /// font must have. <see langword="null"/>: such a character throws. For text nobody can fix by hand, such as
    /// emoji written by users or an AI.
    /// </summary>
    public Func<string, string>? ReplaceUnsupported { get; init; }

    /// <summary>The clock for the date of generation in the footer. Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>Paper sizes.</summary>
public enum PdfPageSize
{
    /// <summary>210 × 297 mm.</summary>
    A4,

    /// <summary>297 × 420 mm.</summary>
    A3,

    /// <summary>8.5 × 11 in.</summary>
    Letter,
}

/// <summary>Page orientation.</summary>
public enum PdfOrientation
{
    /// <summary>Taller than wide.</summary>
    Portrait,

    /// <summary>Wider than tall.</summary>
    Landscape,
}
