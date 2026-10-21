using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>Options for <see cref="PptxGenerator.Generate"/>.</summary>
public sealed record PptxOptions
{
    /// <summary>The default options: built-in neutral template, code blocks stay code.</summary>
    public static PptxOptions Default { get; } = new();

    /// <summary>The template and its features. Default: the built-in neutral template. Front matter in the Markdown overrides it.</summary>
    public PptxTemplateOptions Template { get; init; } = new NeutralTemplateOptions();

    /// <summary>
    /// Called with the language and the code of every fenced code block. A returned image is placed like a
    /// Markdown image; <see langword="null"/> keeps the block as code. This is how diagrams such as Mermaid get in.
    /// </summary>
    public Func<string, string, DocumentResource?>? RenderCodeBlock { get; init; }

    /// <summary>The font of inline code and code blocks. Default <c>Consolas</c>.</summary>
    public string CodeFont { get; init; } = "Consolas";

    /// <summary>The clock for the date of generation in the footer. Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
