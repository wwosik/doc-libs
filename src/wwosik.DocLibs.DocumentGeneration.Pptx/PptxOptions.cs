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
    /// The font of inline code and code blocks. <see langword="null"/> (the default): the brand's mono family, else
    /// <c>Consolas</c>.
    /// </summary>
    public string? CodeFont { get; init; }

    /// <summary>The code font without a brand mono family or <see cref="CodeFont"/>.</summary>
    internal const string DefaultCodeFont = "Consolas";

    /// <summary>The clock for the date of generation in the footer. Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
