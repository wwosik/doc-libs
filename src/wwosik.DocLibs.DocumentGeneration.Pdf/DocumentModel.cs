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

    /// <summary>From <c>:icon-name:</c>: an icon of one em, drawn on the line in the text colour (<see cref="TextRun.Icon"/>).</summary>
    Icon = 64,
}

/// <summary>Text in one style; the text <c>"\n"</c> alone is a line break. <see cref="Link"/> is a URL or <c>#anchor</c>.</summary>
internal sealed record TextRun(string Text, RunStyle Style, string? Link = null)
{
    /// <summary>From <c>[words]{.success}</c>: the words in that status colour.</summary>
    public LabelCategory? Colour { get; init; }

    /// <summary>The icon of a <see cref="RunStyle.Icon"/> run, whose <see cref="Text"/> is empty.</summary>
    public InlineIcon? Icon { get; init; }

    /// <summary>
    /// For a footnote reference (a <see cref="RunStyle.Superscript"/> run of its number): the number of its note in
    /// <see cref="DocumentModel.Footnotes"/>; <see langword="null"/> for any other run, and for a reference inside a note.
    /// </summary>
    public int? Footnote { get; init; }

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
internal sealed record HeadingBlock(int Level, IReadOnlyList<TextRun> Runs, string? Anchor) : Block
{
    /// <summary><c>{.newpage}</c>: the heading starts a new page, unless the page is still empty.</summary>
    public bool NewPage { get; init; }

    /// <summary>Its number with <c>headingNumbers: true</c> (<c>"1.2"</c>, <see cref="HeadingNumbering"/>); <see langword="null"/>: none.</summary>
    public string? Number { get; init; }
}

/// <summary>
/// The caption of a visual: <c>&lt;label&gt; &lt;n&gt;: &lt;text&gt;</c>, numbered by kind through the document
/// (<see cref="Captions"/>).
/// </summary>
internal sealed record Caption(CaptionKind Kind, int Number, IReadOnlyList<TextRun> Text);

internal sealed record ParagraphBlock(IReadOnlyList<TextRun> Runs) : Block;

/// <summary>A list; each item is a sequence of blocks, the first usually a paragraph next to the marker.</summary>
internal sealed record ListBlock(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<Block>> Items) : Block
{
    /// <summary>
    /// Per item of a task list, whether its box is ticked; <see langword="null"/> for an item without a box. The whole
    /// list is <see langword="null"/> when no item has one.
    /// </summary>
    public IReadOnlyList<bool?>? Tasks { get; init; }

    /// <summary>
    /// Per item, the icon its text started with, drawn in place of its marker; <see langword="null"/> for an item
    /// without one. The whole list is <see langword="null"/> when no item has one.
    /// </summary>
    public IReadOnlyList<Drawing?>? Icons { get; init; }
}

/// <summary>A definition list: per item its terms, in bold, and its description, indented under them.</summary>
internal sealed record DefinitionListBlock(IReadOnlyList<Definition> Items) : Block;

internal sealed record Definition(IReadOnlyList<IReadOnlyList<TextRun>> Terms, IReadOnlyList<Block> Description);

/// <summary>
/// A code block that no renderer took: per line its spans, one plain span unless the block is highlighted. The language
/// is the fence's, if any.
/// </summary>
internal sealed record CodeBlock(IReadOnlyList<IReadOnlyList<CodeSpan>> Lines, string? Language = null) : Block
{
    /// <summary>A block of plain lines, one span each.</summary>
    public static CodeBlock Plain(IEnumerable<string> lines, string? language = null) =>
        new([.. lines.Select(line => (IReadOnlyList<CodeSpan>)[new CodeSpan(line, CodeTokenKind.Plain)])], language);
}

internal sealed record QuoteBlock(IReadOnlyList<Block> Blocks) : Block;

internal sealed record TableBlock(IReadOnlyList<IReadOnlyList<IReadOnlyList<TextRun>>> Rows, IReadOnlyList<CellAlignment> Alignments, bool HasHeader) : Block
{
    /// <summary>From a <c>Table: …</c> paragraph after it: set above the table, kept with its start.</summary>
    public Caption? Caption { get; init; }
}

/// <summary>A PNG or JPEG; <see cref="Scale"/> multiplies its natural size.</summary>
internal sealed record ImageBlock(DocumentResource Resource, ImageInfo Info, double Scale) : Block
{
    /// <summary>The attributes after the image (<c>{.round fit=cover}</c>): how it is cropped. <c>.grey</c> is in the pixels already.</summary>
    public ImageAttributes Attributes { get; init; } = ImageAttributes.None;

    /// <summary>The image's line in the Markdown, from 1, for notes; 0 if unknown.</summary>
    public int Line { get; init; }

    /// <summary>From its title, or a <c>Figure: …</c> paragraph after a diagram drawn as an image: set under it.</summary>
    public Caption? Caption { get; init; }
}

/// <summary>A paragraph of nothing but images: side by side across the text width, in cells of equal width.</summary>
internal sealed record ImageRowBlock(IReadOnlyList<ImageBlock> Images) : Block;

internal sealed record RuleBlock : Block;

/// <summary>
/// From <c>::: columns</c>: two or three columns side by side, their widths in proportion to <see cref="Widths"/>, each a
/// sequence of blocks.
/// </summary>
internal sealed record ColumnsBlock(IReadOnlyList<int> Widths, IReadOnlyList<IReadOnlyList<Block>> Columns) : Block;

/// <summary>From <c>::: pagebreak</c>: what follows starts a new page, unless the page is still empty.</summary>
internal sealed record PageBreakBlock : Block;

/// <summary>A vector drawing from a code-block renderer, or a QR code.</summary>
internal sealed record DrawingBlock(Drawing Drawing) : Block
{
    /// <summary>From a <c>Figure: …</c> paragraph after the diagram: set under it.</summary>
    public Caption? Caption { get; init; }
}

/// <summary>A paragraph that is one <c>:icon-name:</c>: the icon on its own, left-aligned, at the figure size, in the accent colour.</summary>
internal sealed record IconBlock(Drawing Icon) : Block;

/// <summary>
/// The cover: the first <c>#</c> heading and the paragraphs up to the next heading. In a plain document
/// (<see cref="PdfLayout.Plain"/>) the title at the top of the first page, without a subtitle.
/// </summary>
internal sealed record Cover(IReadOnlyList<TextRun> Title, IReadOnlyList<IReadOnlyList<TextRun>> Subtitle, string? Anchor);

internal sealed record DocumentModel(Cover? Cover, IReadOnlyList<Block> Blocks)
{
    /// <summary>The table of contents, after the cover; <see langword="null"/>: none.</summary>
    public TableOfContents? Contents { get; init; }

    /// <summary>The footnotes by their number, which is Markdig's order through the whole document; printed at the foot of the page of their first reference.</summary>
    public IReadOnlyDictionary<int, FootnoteText> Footnotes { get; init; } = new Dictionary<int, FootnoteText>();
}

/// <summary>
/// A footnote's text: its paragraphs as runs, lists and code included as text paragraphs. <see cref="Line"/> is the line
/// of its definition in the Markdown, for notes.
/// </summary>
internal sealed record FootnoteText(int Number, IReadOnlyList<IReadOnlyList<TextRun>> Paragraphs, int Line);

/// <summary>
/// A table of contents: an entry per heading it lists, in document order, each linking to its heading's anchor.
/// <see cref="ContentsEntry.Level"/> counts from 1 for the highest level after the cover.
/// </summary>
internal sealed record TableOfContents(IReadOnlyList<ContentsEntry> Entries);

/// <summary>A heading the contents list; <see cref="Heading"/> is the very block, so its text is replaced once.</summary>
internal sealed record ContentsEntry(int Level, HeadingBlock Heading)
{
    /// <summary>Set in bold: a <c>#</c> chapter.</summary>
    public bool Chapter => this.Heading.Level == 1;
}
