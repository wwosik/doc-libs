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

    /// <summary>A footnote reference: raised and smaller.</summary>
    Superscript = 64,
}

/// <summary>Text in one style; the text <c>"\n"</c> alone is a line break.</summary>
internal sealed record TextRun(string Text, RunStyle Style, string? Link = null)
{
    /// <summary>From <c>[words]{.success}</c>: the words in that status colour.</summary>
    public LabelCategory? Colour { get; init; }

    public static TextRun Break { get; } = new("\n", RunStyle.None);

    public bool IsBreak => this.Text == "\n";
}

internal enum ParagraphKind
{
    Plain,
    Bullet,
    Numbered,

    /// <summary>An item of a task list, <c>- [ ]</c>: an empty box for its bullet.</summary>
    Task,

    /// <summary>A ticked item of a task list, <c>- [x]</c>.</summary>
    TaskDone,
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

/// <summary>
/// An image. <see cref="Scale"/>, from <c>scale=</c> in its title, caps how far it is enlarged (<c>1</c>: never beyond its
/// natural size); <see langword="null"/>: up to <see cref="PptxOptions.MaxImageUpscale"/>. <see cref="Caption"/>, from
/// <c>caption</c> in its title: the alt text goes under it.
/// </summary>
internal sealed record SlideImage(DocumentResource Resource, string AltText, double? Scale, bool Caption = false)
{
    /// <summary>The size it is placed at, in pixels, for an image converted from another format; else its pixel size.</summary>
    public (double Width, double Height)? Natural { get; init; }

    /// <summary>The SVG an image was converted from: PowerPoint draws it, and the PNG is for other viewers.</summary>
    public DocumentResource? Vector { get; init; }
}

internal abstract record Slide(IReadOnlyList<TextRun> Title)
{
    /// <summary>The template's layout the heading asked for (<c>layout="…"</c>); <see langword="null"/>: by the kind of slide.</summary>
    public string? Layout { get; init; }

    /// <summary>The line of the heading the slide comes from, from 1, front matter included; 0 if unknown.</summary>
    public int Line { get; init; }

    /// <summary>The footnotes the slide references, by number: in small text at the foot of its content area.</summary>
    public IReadOnlyList<SlideFootnote> Footnotes { get; init; } = [];

    /// <summary>Every run of the slide: its title and all its text, tables, cards and rows included.</summary>
    public IEnumerable<TextRun> AllRuns() => this.Title.Concat(this switch
    {
        TitleSlide title => title.Subtitle.SelectMany(p => p.Runs),
        ContentSlide content => content.Body.SelectMany(p => p.Runs),
        VisualSlide visual => visual.Above.Concat(visual.Below).SelectMany(p => p.Runs)
            .Concat(visual.Table?.Rows.SelectMany(r => r).SelectMany(c => c) ?? []),
        FlowSlide flow => flow.Blocks.SelectMany(Runs),
        _ => [],
    });

    /// <summary>The numbers of the footnotes the slide references.</summary>
    public IReadOnlySet<int> References() =>
        this.AllRuns()
            .Select(r => r.Style.HasFlag(RunStyle.Superscript) && int.TryParse(r.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0)
            .ToHashSet();

    private static IEnumerable<TextRun> Runs(FlowBlock block) => block switch
    {
        TextBlock text => text.Paragraphs.SelectMany(p => p.Runs),
        TableBlock table => table.Table.Rows.SelectMany(r => r).SelectMany(c => c),
        ColumnsBlock columns => columns.Columns.SelectMany(c => c).SelectMany(Runs),
        CardsBlock cards => cards.Cards.SelectMany(c => c.Title.Concat(c.Figure ?? []).Concat(c.Blocks.SelectMany(Runs))),
        RowsBlock rows => rows.Rows.SelectMany(r => r.Term.Concat(r.Description.SelectMany(p => p.Runs))),
        PanelBlock panel => panel.Blocks.SelectMany(Runs),
        CalloutBlock callout => callout.Blocks.SelectMany(Runs),
        StatementBlock statement => statement.Text,
        LabelledTagsBlock tags => tags.Items.SelectMany(i => i.Text),
        _ => [],
    };
}

/// <summary>A footnote on a slide: its number there, and its text.</summary>
internal sealed record SlideFootnote(int Number, IReadOnlyList<TextRun> Text);

/// <summary>
/// From a <c>#</c> heading: title plus the text that follows it as subtitle. The first one is the cover, every
/// later one a section divider.
/// </summary>
internal sealed record TitleSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Subtitle, bool IsSection) : Slide(Title)
{
    /// <summary>From <c># … {.closing}</c>: the closing slide, on the template's closing layout, without a footer.</summary>
    public bool Closing { get; init; }
}

/// <summary>Text only, in the body placeholder of a title-and-content layout.</summary>
internal sealed record ContentSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Body) : Slide(Title);

/// <summary>One table, drawing, image or row of images side by side, with text above it and a caption below it.</summary>
internal sealed record VisualSlide(
    IReadOnlyList<TextRun> Title,
    IReadOnlyList<TextParagraph> Above,
    SlideTable? Table,
    IReadOnlyList<SlideImage>? Images,
    IReadOnlyList<TextParagraph> Below) : Slide(Title)
{
    /// <summary>A vector drawing from a code-block renderer, drawn as a group of shapes.</summary>
    public Drawing? Drawing { get; init; }

    /// <summary>A chart from a code-block renderer, written as a native chart.</summary>
    public ChartSpec? Chart { get; init; }
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

/// <summary>A chart, written as a native PowerPoint chart.</summary>
internal sealed record ChartBlock(ChartSpec Chart) : FlowBlock;

/// <summary>A paragraph of nothing but images: side by side, sharing the width equally.</summary>
internal sealed record ImageRowBlock(IReadOnlyList<SlideImage> Images) : FlowBlock;

/// <summary><c>::: columns</c>: its parts, split at <c>---</c>, side by side with widths in the ratio of <see cref="Widths"/>.</summary>
internal sealed record ColumnsBlock(IReadOnlyList<int> Widths, IReadOnlyList<IReadOnlyList<FlowBlock>> Columns) : FlowBlock;

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
