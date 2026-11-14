using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>Options for <see cref="DocxGenerator.Generate"/>.</summary>
public sealed record DocxOptions
{
    /// <summary>The default options: the built-in template in the neutral brand, A4 portrait, no table of contents.</summary>
    public static DocxOptions Default { get; } = new();

    /// <summary>The template and its features. Default: the built-in template in the neutral brand. Front matter in the Markdown overrides it.</summary>
    public DocxTemplateOptions Template { get; init; } = new BrandDocxTemplateOptions();

    /// <summary>
    /// The brands front matter may select by name (<c>template: acme</c>), besides <c>neutral</c> and the brand of
    /// <see cref="Template"/>. Names must be unique. Default: none.
    /// </summary>
    public IReadOnlyList<Brand> Brands { get; init; } = [];

    /// <summary>
    /// Asked in order for every fenced code block. Reserved for drawings in Word documents: this version writes every
    /// code block as code, with a <c>code-not-drawn</c> note for each block a renderer would draw. Default: none.
    /// </summary>
    public IReadOnlyList<ICodeBlockRenderer> CodeBlockRenderers { get; init; } = [];

    /// <summary>
    /// Asked in order for every image that is not a PNG, JPEG or GIF, such as an <c>.svg</c> image; the first conversion
    /// wins, drawn at twice the size it is placed at. An image no converter takes throws. Default: none.
    /// </summary>
    public IReadOnlyList<IImageConverter> ImageConverters { get; init; } = [];

    /// <summary>
    /// The font of inline code and code blocks. <see langword="null"/> (the default): the brand's mono family, else
    /// <c>Consolas</c>.
    /// </summary>
    public string? CodeFont { get; init; }

    /// <summary>The code font without a brand mono family or <see cref="CodeFont"/>.</summary>
    internal const string DefaultCodeFont = "Consolas";

    /// <summary>
    /// The document language, a BCP 47 tag such as <c>en</c>, <c>de</c> or <c>de-CH</c>, unless the front matter sets
    /// <c>lang</c>. It picks the texts the generator writes itself (see <see cref="Texts"/>) and is the language in the
    /// document's properties. Default <c>en</c>.
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Texts for languages besides English and German, or in place of them, by language tag (<c>pl</c>, <c>de-CH</c>):
    /// a tag is matched in full, then by its language alone. Start each from <see cref="GeneratedTexts.English"/> with
    /// <c>with { … }</c>. Default: none.
    /// </summary>
    public IReadOnlyDictionary<string, GeneratedTexts> Texts { get; init; } = new Dictionary<string, GeneratedTexts>();

    /// <summary>
    /// The clock for the date of generation on the cover and in the footer, and for the document's created and modified
    /// dates. Default <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// A table of contents after the cover, unless the front matter sets <c>toc</c>: the headings down to
    /// <see cref="TableOfContentsDepth"/>, which Word gives their page numbers when it opens the document. Default
    /// <see langword="false"/>.
    /// </summary>
    public bool TableOfContents { get; init; }

    /// <summary>
    /// The heading levels the table of contents lists, 1 to 3, unless the front matter sets <c>tocDepth</c>. Default
    /// <c>2</c>. Another value throws <see cref="ArgumentOutOfRangeException"/> when a document is generated.
    /// </summary>
    public int TableOfContentsDepth { get; init; } = 2;

    /// <summary>
    /// Numbers on Heading 1–3 of the built-in template (1, 1.1, 1.1.1), unless the front matter sets
    /// <c>headingNumbers</c>. A caller's template, or a brand's own, numbers headings as its heading styles say, whatever
    /// this is. Default <see langword="true"/>.
    /// </summary>
    public bool HeadingNumbers { get; init; } = true;

    /// <summary>
    /// How confidential the document is: <see cref="DocumentGeneration.Classification.Internal"/> and
    /// <see cref="DocumentGeneration.Classification.Confidential"/> put "Internal" or "Confidential" in the header of every
    /// content page, above the cover title and in the document's properties (<c>cp:contentStatus</c>). The front matter's
    /// <c>classification</c> can raise it but not lower it. Default <see cref="DocumentGeneration.Classification.Public"/>,
    /// which shows nothing.
    /// </summary>
    public Classification Classification { get; init; } = Classification.Public;

    /// <summary>
    /// Marks the document a draft: "Draft" joins the marking, and every page gets Word's own draft watermark. The front
    /// matter's <c>draft: true</c> also does; it cannot undo this. Default <see langword="false"/>.
    /// </summary>
    public bool Draft { get; init; }
}

/// <summary>Paper sizes of the built-in template.</summary>
public enum DocxPageSize
{
    /// <summary>210 × 297 mm.</summary>
    A4,

    /// <summary>8.5 × 11 in.</summary>
    Letter,

    /// <summary>297 × 420 mm.</summary>
    A3,
}

/// <summary>Page orientation of the built-in template.</summary>
public enum DocxOrientation
{
    /// <summary>Taller than wide.</summary>
    Portrait,

    /// <summary>Wider than tall.</summary>
    Landscape,
}
