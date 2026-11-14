using wwosik.DocLibs.DocumentConversions;

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
    /// Asked in order for every image that is not a PNG or JPEG, such as an <c>.svg</c> image; the first conversion
    /// wins, and an image no converter takes is an error. Register <c>SvgImageConverter</c> from
    /// wwosik.DocLibs.DocumentConversions.Svg to place SVG images. Default: none.
    /// </summary>
    public IReadOnlyList<IImageConverter> ImageConverters { get; init; } = [];

    /// <summary>
    /// Called with each text element (grapheme cluster) that has a character the font of its text lacks — so
    /// <c>👍🏽</c>, <c>❤️</c> or <c>👨‍👩‍👧</c> is one call — and returns the text to put there instead, which the
    /// font must have. <see langword="null"/>: such a character throws. For text nobody can fix by hand, such as
    /// emoji written by users or an AI.
    /// </summary>
    public Func<string, string>? ReplaceUnsupported { get; init; }

    /// <summary>
    /// The document language, a BCP 47 tag such as <c>en</c>, <c>de</c> or <c>de-CH</c>, unless the front matter sets
    /// <c>lang</c>. It picks the texts the generator writes itself (see <see cref="Texts"/>) and tags the text, for
    /// spell checking and screen readers. Default <c>en</c>.
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Texts for languages besides English and German, or in place of them, by language tag (<c>pl</c>, <c>de-CH</c>):
    /// a tag is matched in full, then by its language alone. Start each from <see cref="GeneratedTexts.English"/> with
    /// <c>with { … }</c>. Default: none.
    /// </summary>
    public IReadOnlyDictionary<string, GeneratedTexts> Texts { get; init; } = new Dictionary<string, GeneratedTexts>();

    /// <summary>
    /// Typography in the document language, unless the front matter sets <c>typography</c>: straight quotes become
    /// typographic ones, <c>--</c> and a spaced hyphen dashes, <c>...</c> an ellipsis, and a no-break space keeps
    /// <c>10 km</c>, <c>z. B.</c> and <c>13. November</c> together (<see cref="DocumentGeneration.TypographyRules"/>).
    /// Code, addresses and the text of diagrams and charts are never changed. Default <see langword="false"/>.
    /// </summary>
    public bool Typography { get; init; }

    /// <summary>
    /// Typography rules for languages besides English and German, or in place of them, by language tag (<c>fr</c>,
    /// <c>de-CH</c>): a tag is matched in full, then by its language alone; any other language is set by
    /// <see cref="DocumentGeneration.TypographyRules.English"/>. Start each from <see cref="DocumentGeneration.TypographyRules.English"/>
    /// with <c>with { … }</c>. Default: none.
    /// </summary>
    public IReadOnlyDictionary<string, TypographyRules> TypographyRules { get; init; } = new Dictionary<string, TypographyRules>();

    /// <summary>
    /// The icons <c>:icon-name:</c> draws (as list bullets, alone in a paragraph, and inline in text). Default
    /// <see cref="TablerIcons.Outline"/>, the whole Tabler outline set; a set of your own replaces it, and an unknown
    /// name is an error. <see langword="null"/> switches icons off: <c>:icon-name:</c> then stays text and is not checked.
    /// </summary>
    public IIconSet? Icons { get; init; } = TablerIcons.Outline;

    /// <summary>
    /// A table of contents after the cover, unless the front matter sets <c>toc</c>: the headings down to
    /// <see cref="TableOfContentsDepth"/>, each with its page number and a link to it. Default <see langword="false"/>.
    /// </summary>
    public bool TableOfContents { get; init; }

    /// <summary>
    /// The heading levels the table of contents lists, 1 to 3 from the highest level after the cover, unless the front
    /// matter sets <c>tocDepth</c>. Default <c>2</c>. Another value throws <see cref="ArgumentOutOfRangeException"/> when
    /// a document is generated.
    /// </summary>
    public int TableOfContentsDepth { get; init; } = 2;

    /// <summary>
    /// Numbers on three heading levels from the highest one after the cover (1, 1.1, 1.1.1), unless the front matter sets
    /// <c>headingNumbers</c>; also in the bookmarks and the table of contents. Default <see langword="false"/>.
    /// </summary>
    public bool HeadingNumbers { get; init; }

    /// <summary>
    /// Justified body text, unless the front matter sets <c>justify</c>: paragraphs, list items, definition descriptions,
    /// quotes, alerts and footnotes end at the right margin, but for the last line of a paragraph, a line before a hard
    /// break, a line of one word and a line whose spaces would grow past three times their width. Headings, the cover,
    /// tables, captions, code and the table of contents stay left-aligned. Line and page breaks are those of
    /// left-aligned text; there is no hyphenation. Default <see langword="false"/>.
    /// </summary>
    public bool Justify { get; init; }

    /// <summary>
    /// How confidential the document is: <see cref="DocumentGeneration.Classification.Internal"/> and
    /// <see cref="DocumentGeneration.Classification.Confidential"/> put "Internal" or "Confidential" in the header of every
    /// page but the cover and above the cover title. The front matter's <c>classification</c> can raise it but not lower
    /// it. Default <see cref="DocumentGeneration.Classification.Public"/>, which shows nothing.
    /// </summary>
    public Classification Classification { get; init; } = Classification.Public;

    /// <summary>
    /// Marks the document a draft: "Draft" joins the marking, and every page, the cover included, gets a diagonal
    /// watermark under its content. The front matter's <c>draft: true</c> also does; it cannot undo this. Default
    /// <see langword="false"/>.
    /// </summary>
    public bool Draft { get; init; }

    /// <summary>
    /// The clock for the date of generation in the footer, and for today in timelines and Gantt charts that do not pin it
    /// with <c>today=</c>. Default <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// The limits <see cref="PdfGenerator.Check"/> holds the document to, which <see cref="PdfGenerator.AuthoringGuide"/> lists
    /// for the writer; <see cref="PdfGenerator.Generate"/> never applies them. Default <see cref="CheckOptions.Default"/>.
    /// </summary>
    public CheckOptions Checks { get; init; } = CheckOptions.Default;
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
