using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

[Flags]
internal enum RunStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strike = 8,

    /// <summary>Set by the layout, never by the parser: text in a table cell, drawn in the table faces.</summary>
    Table = 16,

    /// <summary>A footnote reference: smaller, and raised above the baseline.</summary>
    Superscript = 32,
}

/// <summary>Text in one style; the text <c>"\n"</c> alone is a line break. <see cref="Link"/> is a URL or <c>#anchor</c>.</summary>
internal sealed record TextRun(string Text, RunStyle Style, string? Link = null)
{
    /// <summary>From <c>[words]{.success}</c>: the words in that status colour.</summary>
    public LabelCategory? Colour { get; init; }

    public static TextRun Break { get; } = new("\n", RunStyle.None);

    public bool IsBreak => this.Text == "\n";
}

internal enum CellAlignment
{
    Default,
    Left,
    Center,
    Right,
}

internal abstract record Block;

/// <summary>A heading; <see cref="Anchor"/> is the target of <c>[x](#anchor)</c> links.</summary>
internal sealed record HeadingBlock(int Level, IReadOnlyList<TextRun> Runs, string? Anchor) : Block;

internal sealed record ParagraphBlock(IReadOnlyList<TextRun> Runs) : Block;

/// <summary>A list; each item is a sequence of blocks, the first usually a paragraph next to the marker.</summary>
internal sealed record ListBlock(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<Block>> Items) : Block
{
    /// <summary>
    /// Per item of a task list, whether its box is ticked; <see langword="null"/> for an item without a box. The whole
    /// list is <see langword="null"/> when no item has one.
    /// </summary>
    public IReadOnlyList<bool?>? Tasks { get; init; }
}

/// <summary>A definition list: per item its terms, in bold, and its description, indented under them.</summary>
internal sealed record DefinitionListBlock(IReadOnlyList<Definition> Items) : Block;

internal sealed record Definition(IReadOnlyList<IReadOnlyList<TextRun>> Terms, IReadOnlyList<Block> Description);

internal sealed record CodeBlock(IReadOnlyList<string> Lines) : Block;

internal sealed record QuoteBlock(IReadOnlyList<Block> Blocks) : Block;

internal sealed record TableBlock(IReadOnlyList<IReadOnlyList<IReadOnlyList<TextRun>>> Rows, IReadOnlyList<CellAlignment> Alignments, bool HasHeader) : Block;

/// <summary>A PNG or JPEG; <see cref="Scale"/> multiplies its natural size.</summary>
internal sealed record ImageBlock(DocumentResource Resource, ImageInfo Info, double Scale) : Block
{
    /// <summary>The attributes after the image (<c>{.round fit=cover}</c>): how it is cropped. <c>.grey</c> is in the pixels already.</summary>
    public ImageAttributes Attributes { get; init; } = ImageAttributes.None;

    /// <summary>The image's line in the Markdown, from 1, for notes; 0 if unknown.</summary>
    public int Line { get; init; }
}

/// <summary>A paragraph of nothing but images: side by side across the text width, in cells of equal width.</summary>
internal sealed record ImageRowBlock(IReadOnlyList<ImageBlock> Images) : Block;

internal sealed record RuleBlock : Block;

/// <summary>A vector drawing from a code-block renderer.</summary>
internal sealed record DrawingBlock(Drawing Drawing) : Block;

/// <summary>The cover: the first <c>#</c> heading and the paragraphs up to the next heading.</summary>
internal sealed record Cover(IReadOnlyList<TextRun> Title, IReadOnlyList<IReadOnlyList<TextRun>> Subtitle, string? Anchor);

internal sealed record DocumentModel(Cover? Cover, IReadOnlyList<Block> Blocks);
