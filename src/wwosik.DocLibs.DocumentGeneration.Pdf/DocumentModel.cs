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
}

/// <summary>Text in one style; the text <c>"\n"</c> alone is a line break. <see cref="Link"/> is a URL or <c>#anchor</c>.</summary>
internal sealed record TextRun(string Text, RunStyle Style, string? Link = null)
{
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
internal sealed record ListBlock(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<Block>> Items) : Block;

internal sealed record CodeBlock(IReadOnlyList<string> Lines) : Block;

internal sealed record QuoteBlock(IReadOnlyList<Block> Blocks) : Block;

internal sealed record TableBlock(IReadOnlyList<IReadOnlyList<IReadOnlyList<TextRun>>> Rows, IReadOnlyList<CellAlignment> Alignments, bool HasHeader) : Block;

/// <summary>A PNG or JPEG; <see cref="Scale"/> multiplies its natural size.</summary>
internal sealed record ImageBlock(DocumentResource Resource, ImageInfo Info, double Scale) : Block;

internal sealed record RuleBlock : Block;

/// <summary>The cover: the first <c>#</c> heading and the paragraphs up to the next heading.</summary>
internal sealed record Cover(IReadOnlyList<TextRun> Title, IReadOnlyList<IReadOnlyList<TextRun>> Subtitle, string? Anchor);

internal sealed record DocumentModel(Cover? Cover, IReadOnlyList<Block> Blocks);
