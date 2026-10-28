using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>Options for <see cref="PptxGenerator.Generate"/>.</summary>
public sealed record PptxOptions
{
    /// <summary>The default options: built-in master in the neutral brand, code blocks stay code.</summary>
    public static PptxOptions Default { get; } = new();

    /// <summary>The template and its features. Default: the built-in master in the neutral brand. Front matter in the Markdown overrides it.</summary>
    public PptxTemplateOptions Template { get; init; } = new BrandTemplateOptions();

    /// <summary>
    /// The brands front matter may select by name (<c>template: acme</c>), besides <c>neutral</c> and the brand of
    /// <see cref="Template"/>. Names must be unique. Default: none.
    /// </summary>
    public IReadOnlyList<Brand> Brands { get; init; } = [];

    /// <summary>
    /// Asked in order for every fenced code block; the first graphic wins. A drawing becomes a group of editable
    /// shapes, an image is placed like a Markdown image, and a block no renderer takes stays code. Either gets a
    /// slide of its own, like a table. This is how diagrams such as Mermaid get in. Default: none.
    /// </summary>
    public IReadOnlyList<ICodeBlockRenderer> CodeBlockRenderers { get; init; } = [];

    /// <summary>
    /// Asked in order for every image that is not a PNG, JPEG or GIF, such as an <c>.svg</c> image; the first conversion
    /// wins, and an image no converter takes is an error. Register <c>SvgImageConverter</c> from
    /// wwosik.DocLibs.DocumentConversions.Svg to place SVG images. Default: none.
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
    /// How far an image is enlarged at most to fill the room it has, as a multiple of its natural size (a pixel is
    /// 1/96 inch). Default <c>4</c>, so that small images do not turn into blurs; <c>1</c> never enlarges. An image's own
    /// <c>scale=</c> in its title wins.
    /// </summary>
    public double MaxImageUpscale { get; init; } = 4.0;

    /// <summary>
    /// Text added to the title of a slide that continues the one before because its content did not fit, such as
    /// <c>" (cont.)"</c>. Default <see langword="null"/>: the same title.
    /// </summary>
    public string? ContinuationSuffix { get; init; }

    /// <summary>
    /// Whether text slides write Markdown's bullets on every list level (<see cref="ListBullets.Always"/>), or leave
    /// level 1 to the template's style (<see cref="ListBullets.Template"/>, the default), which some templates draw
    /// without a bullet. Front matter <c>listBullets: always</c> overrides it.
    /// </summary>
    public ListBullets ListBullets { get; init; }

    /// <summary>The clock for the date of generation in the footer. Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>Who decides the bullets of lists on text slides.</summary>
public enum ListBullets
{
    /// <summary>The template's styles: Markdown's bullets only where the template has bullets.</summary>
    Template,

    /// <summary>Markdown's bullets on every level, whatever the template's styles say.</summary>
    Always,
}
