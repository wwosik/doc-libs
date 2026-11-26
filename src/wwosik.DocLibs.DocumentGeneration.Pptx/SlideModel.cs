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

    /// <summary>From <c>:icon-name:</c>: an icon in place of text (<see cref="Text"/> is empty). Slides draw it only as a
    /// card's figure or in an icon list; anywhere else it is left out, with a note.</summary>
    public InlineIcon? Icon { get; init; }

    /// <summary>A Harvey ball's glyph (○ ◔ ◑ ◕ ●): written in <see cref="HarveyBall.SymbolFont"/>, never embedded.</summary>
    public bool Symbol { get; init; }

    /// <summary>In a highlighted code block: what the run is, which gives it its colour. <see langword="null"/> elsewhere.</summary>
    public CodeTokenKind? Token { get; init; }

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
internal sealed record TextParagraph(IReadOnlyList<TextRun> Runs, ParagraphKind Kind = ParagraphKind.Plain, int Level = 0, int NumberStart = 1)
{
    /// <summary>The build marker of the block the paragraph comes from, if any, with the number of its list item.</summary>
    public BuildMark? Build { get; init; }

    /// <summary>
    /// Whether it comes from a <c>###</c> (or deeper) heading, set bold by the parser: with a template's text levels it
    /// goes to the heading level instead, in that level's style.
    /// </summary>
    public bool Heading { get; init; }
}

/// <summary>
/// A build marker on content: its builds, played as consecutive clicks; <see cref="Block"/>, the number of the marked
/// block in the document, which orders the clicks of a slide; and <see cref="Item"/>, the number of the top-level list
/// item (or card, row, column, image) the content belongs to, for builds that go item by item.
/// </summary>
internal sealed record BuildMark(IReadOnlyList<Build> Builds, int Block, int Item = 0)
{
    /// <summary>
    /// Whether every mark with this <see cref="Block"/> plays on one click, each with its own single build: a scene's
    /// step, whose shapes come in and go together.
    /// </summary>
    public bool Joined { get; init; }
}

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

    /// <summary>The attributes after the image (<c>{fit=cover .round .grey}</c>): how it is cropped and coloured.</summary>
    public ImageAttributes Attributes { get; init; } = ImageAttributes.None;
}

internal abstract record Slide(IReadOnlyList<TextRun> Title)
{
    /// <summary>The template's layout the heading asked for (<c>layout="…"</c>); <see langword="null"/>: by the kind of slide.</summary>
    public string? Layout { get; init; }

    /// <summary>The line of the heading the slide comes from, from 1, front matter included; 0 if unknown.</summary>
    public int Line { get; init; }

    /// <summary>The footnotes the slide references, by number: in small text at the foot of its content area.</summary>
    public IReadOnlyList<SlideFootnote> Footnotes { get; init; } = [];

    /// <summary>The speaker notes, from the section's <c>::: notes</c>: on every slide the section produces.</summary>
    public IReadOnlyList<TextParagraph> SpeakerNotes { get; init; } = [];

    /// <summary>How the slide is brought on when presented: its heading's <c>transition="…"</c>, else the deck's.</summary>
    public SlideTransition Transition { get; init; }

    /// <summary>
    /// In the deck's backup (<c># … {.backup}</c> and every slide after it, E06-S07): behind the closing slide, out of the
    /// agenda and the chapter numbers, the footer adding "Backup".
    /// </summary>
    public bool Backup { get; init; }

    /// <summary>Every run of the slide: its title and all its text, tables, cards and rows included.</summary>
    public IEnumerable<TextRun> AllRuns() => this.Title.Concat(this switch
    {
        TitleSlide title => title.Subtitle.SelectMany(p => p.Runs),
        ContentSlide content => content.Body.SelectMany(p => p.Runs),
        VisualSlide visual => visual.Above.Concat(visual.Below).SelectMany(p => p.Runs)
            .Concat(visual.Table?.Rows.SelectMany(r => r).SelectMany(c => c) ?? []),
        FlowSlide flow => flow.Blocks.SelectMany(Runs),
        PlaceholderSlide placeholders => placeholders.Items.SelectMany(i => (i.Text ?? []).SelectMany(p => p.Runs)
            .Concat(i.Visual?.Table?.Rows.SelectMany(r => r).SelectMany(c => c) ?? [])),
        KindSlide kind => kind.Paragraphs().SelectMany(p => p.Runs),
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
        ConclusionBlock conclusion => conclusion.Paragraphs.SelectMany(p => p.Runs),
        CommentBlock comment => Runs(comment.Target).Concat(comment.Paragraphs.SelectMany(p => p.Runs)),
        BylineBlock byline => byline.Text,
        LabelledTagsBlock tags => tags.Items.SelectMany(i => i.Text),
        IconListBlock icons => icons.Items.SelectMany(i => i.Text),
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

    /// <summary>The chapter number of a section divider when chapters are numbered (from 1); <see langword="null"/>: none.</summary>
    public int? Number { get; init; }
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

    /// <summary>The build marker of the table, image, drawing or chart, if any.</summary>
    public BuildMark? VisualBuild { get; init; }

    /// <summary>How <see cref="Drawing"/> is sized: a QR code is held to a third of the width.</summary>
    public DrawingPlacement Placement { get; init; } = DrawingPlacement.Default;
}

/// <summary>
/// A <c>##</c> section with <c>layout="…"</c> that fills the layout's placeholders: its text cut at top-level <c>---</c>
/// into parts, one per text placeholder, and its images, charts, tables and drawings in the picture, chart and
/// <c>obj</c> placeholders, all in the order they appear in the Markdown.
/// </summary>
internal sealed record PlaceholderSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<PlaceholderItem> Items) : Slide(Title);

/// <summary>One part of a <see cref="PlaceholderSlide"/>: a text part, or one visual.</summary>
internal sealed record PlaceholderItem(IReadOnlyList<TextParagraph>? Text, PlaceholderVisual? Visual)
{
    /// <summary>Whether a placeholder of that kind takes it: an <c>obj</c> body takes anything.</summary>
    public bool Accepts(SlotKind slot) => slot == SlotKind.Object || (this.Text is not null ? slot == SlotKind.Text
        : this.Visual?.Image is not null ? slot == SlotKind.Picture
        : this.Visual?.Chart is not null && slot == SlotKind.Chart);

    /// <summary>What it is, for messages.</summary>
    public string Kind => this.Text is not null ? "text" : this.Visual?.Image is not null ? "an image" : this.Visual?.Chart is not null ? "a chart"
        : this.Visual?.Table is not null ? "a table" : "a diagram";
}

/// <summary>An image, chart, table or drawing of a <see cref="PlaceholderSlide"/>, with its build marker.</summary>
internal sealed record PlaceholderVisual(SlideTable? Table, SlideImage? Image, Drawing? Drawing, ChartSpec? Chart)
{
    public BuildMark? Build { get; init; }

    /// <inheritdoc cref="VisualSlide.Placement"/>
    public DrawingPlacement Placement { get; init; } = DrawingPlacement.Default;
}

/// <summary>
/// A placeholder of a layout as a <see cref="PlaceholderSlide"/> fills it: text only (<c>body</c>, untyped or the
/// subtitle), an <c>obj</c> body (text or any visual), a picture or a chart placeholder.
/// </summary>
internal enum SlotKind
{
    Text,
    Object,
    Picture,
    Chart,
}

/// <summary>How the items of a <see cref="PlaceholderSlide"/> are spread over the placeholders of its layout.</summary>
internal static class PlaceholderFill
{
    /// <summary>
    /// The items slide by slide, each with the slot it goes into: in order, each into the first free slot that takes it
    /// (an image first into a picture, a chart into a chart placeholder, then an <c>obj</c> body; text into the text
    /// slots in their order); an item that finds none starts a further slide. <see langword="null"/> when an item finds
    /// no slot even on an empty slide.
    /// </summary>
    public static List<List<(PlaceholderItem Item, int Slot)>>? Distribute(IReadOnlyList<PlaceholderItem> items, IReadOnlyList<SlotKind> slots)
    {
        var slides = new List<List<(PlaceholderItem Item, int Slot)>> { new() };
        var used = new HashSet<int>();
        foreach (var item in items)
        {
            if (Free(item, slots, used) is not { } slot)
            {
                used.Clear();
                if (Free(item, slots, used) is not { } fresh)
                {
                    return null;
                }

                slides.Add([]);
                slot = fresh;
            }

            used.Add(slot);
            slides[^1].Add((item, slot));
        }

        return slides;
    }

    /// <summary>The first item that no slot takes, even on an empty slide; <see langword="null"/>: every one has a slot.</summary>
    public static PlaceholderItem? Unplaceable(IReadOnlyList<PlaceholderItem> items, IReadOnlyList<SlotKind> slots) =>
        items.FirstOrDefault(i => Free(i, slots, new HashSet<int>()) is null);

    // Its own kind first, then an `obj` body; text goes to the text slots in their order, `obj` bodies among them.
    private static int? Free(PlaceholderItem item, IReadOnlyList<SlotKind> slots, IReadOnlySet<int> used)
    {
        int? First(Func<SlotKind, bool> match) =>
            Enumerable.Range(0, slots.Count).Cast<int?>().FirstOrDefault(i => !used.Contains(i!.Value) && match(slots[i.Value]));
        return item.Text is not null ? First(k => k is SlotKind.Text or SlotKind.Object)
            : First(k => k != SlotKind.Object && item.Accepts(k)) ?? First(k => k == SlotKind.Object);
    }
}

/// <summary>
/// A <c>##</c> section of a kind (<c>{.agenda}</c>, <c>{.quote}</c>, <c>{.statement}</c>, <c>{.contact}</c>,
/// <c>{.chart}</c>): on the template's layout for that kind, its content in the layout's placeholders; on a template
/// without that layout, a <see cref="FlowSlide"/> with the same content.
/// </summary>
internal abstract record KindSlide(IReadOnlyList<TextRun> Title) : Slide(Title)
{
    /// <summary>From <c>kicker="…"</c>: a small label above the title.</summary>
    public string? Kicker { get; init; }

    /// <summary>Every paragraph of the slide, for its runs.</summary>
    public abstract IEnumerable<TextParagraph> Paragraphs();
}

/// <summary><c>{.agenda}</c>: its list, or the deck's chapters when it had none (<see cref="FromChapters"/>).</summary>
internal sealed record AgendaSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Items) : KindSlide(Title)
{
    /// <summary>Whether it is to list the chapters, filled in once the whole document is parsed.</summary>
    public bool FromChapters { get; init; }

    public override IEnumerable<TextParagraph> Paragraphs() => this.Items;
}

/// <summary><c>{.quote}</c>: the quote, and its byline without the dash, if any.</summary>
internal sealed record QuoteSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Quote, IReadOnlyList<TextRun>? Byline) : KindSlide(Title)
{
    public override IEnumerable<TextParagraph> Paragraphs() => this.Byline is { } byline ? [.. this.Quote, new TextParagraph(byline)] : this.Quote;
}

/// <summary><c>{.statement}</c>: the heading is the statement; <see cref="Text"/> goes below it.</summary>
internal sealed record StatementSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Text) : KindSlide(Title)
{
    public override IEnumerable<TextParagraph> Paragraphs() => this.Text;
}

/// <summary><c>{.contact}</c>: up to five people a slide; more continue on further slides.</summary>
internal sealed record ContactSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<Person> People) : KindSlide(Title)
{
    public override IEnumerable<TextParagraph> Paragraphs() => this.People.SelectMany(p => p.Lines.Prepend(new TextParagraph(p.Name)));
}

/// <summary>A person on a contact slide: the name, the other lines of the item, and the image in it.</summary>
internal sealed record Person(IReadOnlyList<TextRun> Name, IReadOnlyList<TextParagraph> Lines, SlideImage? Photo)
{
    /// <summary>The build marker of the list item, if the list builds item by item.</summary>
    public BuildMark? Build { get; init; }
}

/// <summary><c>{.chart}</c>: its charts (two a slide; more continue) and its text.</summary>
internal sealed record ChartSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<ChartBlock> Charts, IReadOnlyList<TextParagraph> Text) : KindSlide(Title)
{
    public override IEnumerable<TextParagraph> Paragraphs() => this.Text;
}

/// <summary>
/// How far a drawing is enlarged at most to fill its box, and the share of the box's width it may take: diagrams
/// <see cref="Default"/>, a QR code at most a third of the width, or with <c>scale</c> its own size at most.
/// </summary>
internal sealed record DrawingPlacement(double MaxUpscale, double MaxWidthShare)
{
    public static DrawingPlacement Default { get; } = new(1.5, 1);
}

/// <summary>
/// A <c>##</c> section that uses layouts — heading attributes, <c>:::</c> containers, alerts or tag lists: its blocks
/// stacked top to bottom, with a <see cref="Kicker"/> above the title and, if <see cref="Dark"/>, a dark background.
/// </summary>
internal sealed record FlowSlide(IReadOnlyList<TextRun> Title, string? Kicker, bool Dark, IReadOnlyList<FlowBlock> Blocks) : Slide(Title)
{
    /// <summary>Whether the title is left off the slide: a statement drawn as a flow slide, whose first block is its heading.</summary>
    public bool NoTitle { get; init; }
}

/// <summary>
/// A <c>::: scene</c>: its title on the title-only layout (or <c>layout="…"</c>) and its shapes at their places on a
/// 160 × 90 grid over the whole slide. Never shrunk or split.
/// </summary>
internal sealed record SceneSlide(IReadOnlyList<TextRun> Title, Scene Scene) : Slide(Title);

/// <summary>One block of a <see cref="FlowSlide"/>, or of a card, panel or callout on one.</summary>
internal abstract record FlowBlock
{
    /// <summary>
    /// The build marker of the block, if any. Text carries its markers on its paragraphs instead; a block marked
    /// item by item builds by card, row, column, labelled tag or image, any other as a whole.
    /// </summary>
    public BuildMark? Build { get; init; }
}

internal sealed record TextBlock(IReadOnlyList<TextParagraph> Paragraphs) : FlowBlock;

/// <summary>
/// A code block that no renderer took: its lines on a box of their own, each line its runs of code (none for an empty
/// line); highlighted runs carry their <see cref="TextRun.Token"/>.
/// </summary>
internal sealed record CodeLinesBlock(IReadOnlyList<IReadOnlyList<TextRun>> Lines) : FlowBlock;

internal sealed record TableBlock(SlideTable Table) : FlowBlock;

internal sealed record ImageBlock(SlideImage Image) : FlowBlock;

internal sealed record DrawingBlock(Drawing Drawing) : FlowBlock
{
    /// <inheritdoc cref="VisualSlide.Placement"/>
    public DrawingPlacement Placement { get; init; } = DrawingPlacement.Default;
}

/// <summary>A chart, written as a native PowerPoint chart.</summary>
internal sealed record ChartBlock(ChartSpec Chart) : FlowBlock;

/// <summary>A paragraph of nothing but images: side by side, sharing the width equally.</summary>
internal sealed record ImageRowBlock(IReadOnlyList<SlideImage> Images) : FlowBlock;

/// <summary><c>::: columns</c>: its parts, split at <c>---</c>, side by side with widths in the ratio of <see cref="Widths"/>.</summary>
internal sealed record ColumnsBlock(IReadOnlyList<int> Widths, IReadOnlyList<IReadOnlyList<FlowBlock>> Columns) : FlowBlock;

/// <summary><c>::: cards</c>: boxes side by side.</summary>
internal sealed record CardsBlock(IReadOnlyList<Card> Cards) : FlowBlock;

/// <summary>A card: its <c>###</c> or bold title, an optional big figure, and its blocks.</summary>
internal sealed record Card(IReadOnlyList<TextRun> Title, IReadOnlyList<TextRun>? Figure, IReadOnlyList<FlowBlock> Blocks)
{
    /// <summary>A photo above the title: a person of a contact slide drawn as a flow slide.</summary>
    public SlideImage? Photo { get; init; }

    /// <summary>A first paragraph that is one <c>:icon-name:</c>: the icon as the figure, at the figure size in the accent colour.</summary>
    public Drawing? FigureIcon { get; init; }
}

/// <summary><c>::: rows</c>: the term on the left, its description beside it, a rule above each row.</summary>
internal sealed record RowsBlock(IReadOnlyList<Row> Rows) : FlowBlock;

internal sealed record Row(IReadOnlyList<TextRun> Term, IReadOnlyList<TextParagraph> Description);

/// <summary><c>::: panel</c> or <c>::: panel dark</c>: a box behind its blocks.</summary>
internal sealed record PanelBlock(bool Dark, IReadOnlyList<FlowBlock> Blocks) : FlowBlock;

/// <summary><c>> [!NOTE]</c> and the other alerts: a band with a bar on the left; <see cref="Important"/> takes the highlight colour.</summary>
internal sealed record CalloutBlock(bool Important, IReadOnlyList<FlowBlock> Blocks) : FlowBlock;

/// <summary>The first paragraph of <c>::: statement</c>, set large; the rest of the container follows as normal blocks.</summary>
internal sealed record StatementBlock(IReadOnlyList<TextRun> Text) : FlowBlock;

/// <summary><c>::: conclusion</c>, the last block of a flow slide: a band across the foot of its content area.</summary>
internal sealed record ConclusionBlock(IReadOnlyList<TextParagraph> Paragraphs) : FlowBlock;

/// <summary>
/// <c>::: comment</c> with the block before it, <see cref="Target"/>: a callout over the target's top right, taking no
/// height of its own; both move together when a slide is split.
/// </summary>
internal sealed record CommentBlock(FlowBlock Target, IReadOnlyList<TextParagraph> Paragraphs) : FlowBlock;

/// <summary>The byline of a quote drawn as a flow slide: right-aligned, under the quote.</summary>
internal sealed record BylineBlock(IReadOnlyList<TextRun> Text) : FlowBlock;

/// <summary>A paragraph of nothing but code spans: chips that flow and wrap.</summary>
internal sealed record TagsBlock(IReadOnlyList<string> Tags) : FlowBlock;

/// <summary>A list in a container whose items start with a code span: the span as a label, the rest beside it.</summary>
internal sealed record LabelledTagsBlock(IReadOnlyList<LabelledTag> Items) : FlowBlock;

internal sealed record LabelledTag(string Label, IReadOnlyList<TextRun> Text);

/// <summary>A list whose every item starts with <c>:icon-name:</c>: per item a row, the icon on the left, its text beside it.</summary>
internal sealed record IconListBlock(IReadOnlyList<IconItem> Items) : FlowBlock;

internal sealed record IconItem(Drawing Icon, IReadOnlyList<TextRun> Text);
