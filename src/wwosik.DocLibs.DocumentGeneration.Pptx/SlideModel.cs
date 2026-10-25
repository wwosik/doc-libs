using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

[Flags]
internal enum RunStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strike = 8,

    /// <summary><c>==text==</c>: bold, on the light highlight colour.</summary>
    Mark = 16,

    /// <summary>A table cell that is one code span, on a flow slide: a status label, its cell tinted by its category.</summary>
    Label = 32,
}

/// <summary>Text in one style; the text <c>"\n"</c> alone is a line break.</summary>
internal sealed record TextRun(string Text, RunStyle Style, string? Link = null)
{
    public static TextRun Break { get; } = new("\n", RunStyle.None);

    public bool IsBreak => this.Text == "\n";
}

internal enum ParagraphKind
{
    Plain,
    Bullet,
    Numbered,
}

internal enum CellAlignment
{
    Default,
    Left,
    Center,
    Right,
}

/// <summary>A paragraph; <see cref="NumberStart"/> is the start value of the numbered list it belongs to.</summary>
internal sealed record TextParagraph(IReadOnlyList<TextRun> Runs, ParagraphKind Kind = ParagraphKind.Plain, int Level = 0, int NumberStart = 1);

/// <summary>A table; <see cref="Highlighted"/> holds, per row, whether its first cell is all <c>==marked==</c>.</summary>
internal sealed record SlideTable(
    IReadOnlyList<IReadOnlyList<IReadOnlyList<TextRun>>> Rows,
    IReadOnlyList<CellAlignment> Alignments,
    bool HasHeader,
    IReadOnlyList<bool> Highlighted);

internal sealed record SlideImage(DocumentResource Resource, string AltText, double Scale);

internal abstract record Slide(IReadOnlyList<TextRun> Title);

/// <summary>
/// From a <c>#</c> heading: title plus the text that follows it as subtitle. The first one is the cover, every
/// later one a section divider.
/// </summary>
internal sealed record TitleSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Subtitle, bool IsSection) : Slide(Title);

/// <summary>Text only, in the body placeholder of a title-and-content layout.</summary>
internal sealed record ContentSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Body) : Slide(Title);

/// <summary>One table, image or drawing, with text above it and a caption below it.</summary>
internal sealed record VisualSlide(
    IReadOnlyList<TextRun> Title,
    IReadOnlyList<TextParagraph> Above,
    SlideTable? Table,
    SlideImage? Image,
    IReadOnlyList<TextParagraph> Below) : Slide(Title)
{
    /// <summary>A vector drawing from a code-block renderer, drawn as a group of shapes.</summary>
    public Drawing? Drawing { get; init; }
}

/// <summary>
/// A <c>##</c> section that uses layouts — heading attributes, <c>:::</c> containers, alerts or tag lists: its blocks
/// stacked top to bottom, with a <see cref="Kicker"/> above the title and, if <see cref="Dark"/>, a dark background.
/// </summary>
internal sealed record FlowSlide(IReadOnlyList<TextRun> Title, string? Kicker, bool Dark, IReadOnlyList<FlowBlock> Blocks) : Slide(Title);

/// <summary>One block of a <see cref="FlowSlide"/>, or of a card, panel or callout on one.</summary>
internal abstract record FlowBlock;

internal sealed record TextBlock(IReadOnlyList<TextParagraph> Paragraphs) : FlowBlock;

/// <summary>A code block that no renderer took: its lines on a box of their own.</summary>
internal sealed record CodeLinesBlock(IReadOnlyList<string> Lines) : FlowBlock;

internal sealed record TableBlock(SlideTable Table) : FlowBlock;

internal sealed record ImageBlock(SlideImage Image) : FlowBlock;

internal sealed record DrawingBlock(Drawing Drawing) : FlowBlock;

/// <summary><c>::: cards</c>: boxes side by side.</summary>
internal sealed record CardsBlock(IReadOnlyList<Card> Cards) : FlowBlock;

/// <summary>A card: its <c>###</c> or bold title, an optional big figure, and its blocks.</summary>
internal sealed record Card(IReadOnlyList<TextRun> Title, IReadOnlyList<TextRun>? Figure, IReadOnlyList<FlowBlock> Blocks);

/// <summary><c>::: rows</c>: the term on the left, its description beside it, a rule above each row.</summary>
internal sealed record RowsBlock(IReadOnlyList<Row> Rows) : FlowBlock;

internal sealed record Row(IReadOnlyList<TextRun> Term, IReadOnlyList<TextParagraph> Description);

/// <summary><c>::: panel</c> or <c>::: panel dark</c>: a box behind its blocks.</summary>
internal sealed record PanelBlock(bool Dark, IReadOnlyList<FlowBlock> Blocks) : FlowBlock;

/// <summary><c>> [!NOTE]</c> and the other alerts: a band with a bar on the left; <see cref="Important"/> takes the highlight colour.</summary>
internal sealed record CalloutBlock(bool Important, IReadOnlyList<FlowBlock> Blocks) : FlowBlock;

/// <summary>The first paragraph of <c>::: statement</c>, set large; the rest of the container follows as normal blocks.</summary>
internal sealed record StatementBlock(IReadOnlyList<TextRun> Text) : FlowBlock;

/// <summary>A paragraph of nothing but code spans: chips that flow and wrap.</summary>
internal sealed record TagsBlock(IReadOnlyList<string> Tags) : FlowBlock;

/// <summary>A list in a container whose items start with a code span: the span as a label, the rest beside it.</summary>
internal sealed record LabelledTagsBlock(IReadOnlyList<LabelledTag> Items) : FlowBlock;

internal sealed record LabelledTag(string Label, IReadOnlyList<TextRun> Text);
