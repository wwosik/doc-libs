using System.Globalization;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>What the layout takes from the template and the document besides the blocks.</summary>
internal sealed record Design(ResolvedBrand Brand, bool ShowTagline, string? Date, string? HeaderTitle)
{
    /// <summary>The texts in the document language: the heading of the contents, the labels of captions.</summary>
    public GeneratedTexts Texts { get; init; } = GeneratedTexts.English;

    /// <summary>
    /// <c>justify: true</c>: paragraphs, list items, definition descriptions, quotes, alerts and footnotes are justified;
    /// the line breaks stay those of left-aligned text.
    /// </summary>
    public bool Justify { get; init; }

    /// <summary>
    /// The classification marking ("Confidential · Draft"), in the header of every page but the cover and above the cover
    /// title, in <see cref="MarkingColor"/>; <see langword="null"/>: none.
    /// </summary>
    public string? Marking { get; init; }

    /// <summary>The colour of <see cref="Marking"/>: the danger colour for a confidential document, else the footer's.</summary>
    public string? MarkingColor { get; init; }

    /// <summary>A draft's watermark ("DRAFT"), diagonally under the content of every page; <see langword="null"/>: none.</summary>
    public string? Watermark { get; init; }
}

/// <summary>
/// Puts the block model onto pages: greedy line breaking, page breaks, lists, tables with repeated header rows,
/// images, captions, the cover, the table of contents, footnotes at the foot of the page, justified text, and once the
/// page count is known its page numbers, header and footer. Works only through <see cref="IFontMetrics"/>, never on a PDF.
/// </summary>
internal sealed class LayoutEngine
{
    private const double BodySize = 10.5;
    private const double LineFactor = 1.35;
    private const double ParagraphGap = 6;
    private const double RowGap = 12;
    private const double ListItemGap = 2;
    private const double ListIndent = 18;
    private const double MarkerGap = 5;
    private const double QuoteIndent = 14;
    private const double CodeSize = 9;
    private const double CodePadding = 6;
    private const double TableSize = 9.5;
    private const double CellPadding = 4;
    private const double CoverTitleSize = 30;
    private const double CoverSubtitleSize = 13;
    private const double FooterSize = 8;
    private const double PointsPerPixel = 0.75;
    private const double MinLabelScale = 0.7;

    // The classification marking: beside the logo in the header, this far from it and from the title; on the cover at
    // this size, its baseline this far above the top of the title. A draft's watermark: this share of the page's
    // diagonal long, in this share of the text colour on white.
    private const double HeaderGap = 12;
    private const double CoverMarkingSize = 10;
    private const double CoverMarkingGap = 4;
    private const double WatermarkShare = 0.7;
    private const double WatermarkTint = 0.15;

    // Captions: 9 pt, this far from their visual. Heading numbers: this share of the heading's size after the widest
    // number of its level. The table of contents: indented by level; its page numbers in a column of the width of this
    // many of the widest digit, after a gap, which also keeps the dot leader off the title and the number.
    private const double CaptionSize = 9;
    private const double CaptionGap = 4;
    private const double NumberGap = 0.6;
    private const double ContentsIndent = 16;
    private const int ContentsDigits = 3;
    private const double ContentsGap = 6;
    private const string Grey = "4B5563";
    private const string CodeBackground = "F3F4F6";
    private const string White = "FFFFFF";

    // A footnote reference: this share of the text size, raised by this share of it.
    // Icons: a figure (a paragraph of one icon) as large as a card's figure on a slide; an icon in text one em wide,
    // with a thin space each side, centred on the height of a capital letter.
    private const double FigureSize = 32;
    private const double ThinSpace = 1.0 / 6;
    private const double CapHeight = 0.7;
    private const double SuperscriptScale = 0.65;
    private const double SuperscriptRise = 0.33;

    // Footnotes: 8.5 pt, hanging after their number; between the text and the first one a rule a third of the text
    // width long, in a band of this height; this far apart. A note taller than a third of the page may split, never
    // before its second line.
    private const double NoteSize = 8.5;
    private const double NoteIndent = 12;
    private const double NoteSeparator = 10;
    private const double NoteGap = 2;
    private const int NoteMinLines = 2;

    // Justified text: a line whose spaces would grow past this many times their width stays left-aligned.
    private const double MaxSpaceStretch = 3;

    // The markers of a task list's items, drawn as boxes rather than written: the fonts have no ☐ and ☒.
    private const string EmptyBox = "\u2610";
    private const string TickedBox = "\u2612";

    private static readonly double[] HeadingSizes = [20, 16, 13.5, 12, 11, 10.5];

    private readonly IFontMetrics metrics;
    private readonly PageGeometry geometry;
    private readonly Design design;
    private readonly Func<string, string>? replaceUnsupported;
    private readonly GenerationNotes notes;
    private readonly List<LaidOutPage> pages = [];
    private readonly List<Bookmark> bookmarks = [];
    private readonly Dictionary<string, (int Page, double Y)> anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<IReadOnlyList<TextRun>, (double Width, List<TextLine> Lines)> paragraphs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TableBlock, (double Width, PreparedTable? Table)> tables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HeadingBlock, (double Width, List<TextRun> Runs, List<TextLine> Lines)> headings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HeadingBlock, List<TextRun>> headingRuns = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Caption, (double Width, List<TextLine> Lines)> captionLines = new(ReferenceEqualityComparer.Instance);

    // Per heading level, the width of the column its numbers stand in; the entries of the contents whose page numbers
    // are written once every heading has its page.
    private readonly Dictionary<int, double> numberColumns = [];
    private readonly List<ContentsSlot> contentsSlots = [];
    // Footnotes: by number, their wrapped lines; the ones already on a page; the parts on the current page; the rests of
    // notes split at the foot of the last page, which continue on the next; the ones reported as split. While a heading
    // measures what it is kept with, the notes of those lines.
    private readonly Dictionary<int, List<TextLine>> noteLines = [];
    private readonly HashSet<int> placedNotes = [];
    private readonly List<NotePart> pageNotes = [];
    private readonly List<NotePart> carriedNotes = [];
    private readonly HashSet<int> splitNotes = [];
    private IReadOnlyDictionary<int, FootnoteText> footnotes = new Dictionary<int, FootnoteText>();
    private List<int>? keptNotes;

    // The page the last `::: pagebreak` started.
    private LaidOutPage? brokenPage;
    private LaidOutPage? page;
    private double y;

    // Where the text of the current page ends: the page's bottom less its footnotes.
    private double bottom;

    // Where the headings directly above the current block start, on page chainPage; null after any other block.
    private double? chainTop;
    private int chainPage;

    public LayoutEngine(
        IFontMetrics metrics, PageGeometry geometry, Design design, Func<string, string>? replaceUnsupported = null, GenerationNotes? notes = null)
    {
        this.notes = notes ?? new GenerationNotes();
        this.metrics = metrics;
        this.geometry = geometry;
        this.design = design;
        this.replaceUnsupported = replaceUnsupported;
    }

    private string TextColor => this.design.Brand.ThemeColors[0];

    public Layout Run(DocumentModel model)
    {
        this.footnotes = model.Footnotes;
        if (model.Cover is { } cover)
        {
            this.Cover(cover);
        }

        this.NumberColumns(model.Blocks);
        var frame = new Frame(this.geometry.Left, this.geometry.ContentWidth, this.TextColor, []);
        if (model.Contents is { } contents)
        {
            // On a page of its own after the cover; the body starts on the next page.
            this.NewPage();
            this.Contents(contents, frame);
        }

        if (model.Blocks.Count > 0 || this.pages.Count == 0)
        {
            this.NewPage();
        }

        this.Blocks(model.Blocks, frame, 0);

        // A page break with nothing after it leaves no empty page.
        if (this.page is { Items.Count: 0 } last && last == this.brokenPage && this.pageNotes.Count == 0)
        {
            this.pages.RemoveAt(this.pages.Count - 1);
            this.page = this.pages[^1];
        }

        // The notes of the last page, and the rest of a note split there on pages of its own.
        this.FinishPage();
        while (this.carriedNotes.Count > 0)
        {
            this.NewPage();
            this.FinishPage();
        }

        this.ContentsPages();

        // Replaced once, as the header shows it; without a replacement it is checked only where it is drawn.
        var title = this.design.HeaderTitle is { Length: > 0 } header && this.replaceUnsupported is not null
            ? this.Supported(header, FontFace.Regular)
            : this.design.HeaderTitle;
        this.Decorate(title);
        return new Layout(this.pages, this.bookmarks, this.anchors, this.geometry, title);
    }

    // The page before gets its footnotes; the new one starts with the rest of a note split there.
    private void NewPage(bool isCover = false)
    {
        this.FinishPage();
        this.page = new LaidOutPage(isCover);
        this.pages.Add(this.page);
        this.y = this.geometry.Top;
        this.pageNotes.AddRange(this.carriedNotes);
        this.carriedNotes.Clear();
        this.bottom = this.geometry.Bottom - this.NotesHeight(this.pageNotes);
    }

    private LaidOutPage Page => this.page!;

    private bool AtTop => this.y <= this.geometry.Top + 0.01;

    // Starts a new page unless the height fits, or the page is still empty (then it overflows rather than loops).
    private void Ensure(double height)
    {
        // A hair of tolerance: content sized to the exact room left (a visual below its headings) must fit.
        if (this.y + height > this.bottom + 1e-6 && !this.AtTop)
        {
            this.NewPage();
        }
    }

    private void Advance(double height, Frame frame)
    {
        foreach (var x in frame.QuoteRules)
        {
            this.Page.Items.Add(new LineItem(x, this.y, x, this.y + height, 2, this.design.Brand.TableLine));
        }

        this.y += height;
    }

    private void Gap(double height, Frame frame)
    {
        if (!this.AtTop)
        {
            this.Advance(Math.Min(height, Math.Max(0, this.bottom - this.y)), frame);
        }
    }

    private void Blocks(IReadOnlyList<Block> blocks, Frame frame, int listLevel)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            this.Block(blocks[i], frame, listLevel, blocks, i + 1);
        }
    }

    // `siblings` from `next` on are the blocks after this one, which a heading is kept with.
    private void Block(Block block, Frame frame, int listLevel, IReadOnlyList<Block> siblings, int next)
    {
        switch (block)
        {
            case HeadingBlock heading:
                this.Heading(heading, frame, siblings, next);
                break;
            case ParagraphBlock paragraph:
                this.Paragraph(paragraph.Runs, frame, null, ParagraphGap, justify: this.design.Justify);
                break;
            case ListBlock list:
                this.List(list, frame, listLevel);
                break;
            case CodeBlock code:
                this.Code(code, frame);
                break;
            case QuoteBlock quote:
                this.Blocks(quote.Blocks, QuoteFrame(frame), listLevel);
                break;
            case TableBlock table:
                this.Table(table, frame);
                break;
            case ImageBlock image:
                this.Image(image, frame);
                break;
            case ImageRowBlock row:
                this.ImageRow(row, frame);
                break;
            case DrawingBlock drawing:
                this.Drawing(drawing.Drawing, frame, drawing.Caption);
                break;
            case IconBlock icon:
                this.Ensure(FigureSize);
                this.Icon(icon.Icon, frame.Left, this.y, FigureSize, this.design.Brand.Primary);
                this.Advance(FigureSize, frame);
                this.Gap(ParagraphGap, frame);
                break;
            case DefinitionListBlock definitions:
                this.Definitions(definitions, frame, listLevel);
                break;
            case PageBreakBlock:
                // Never an empty page: on one that is still empty, the break has nothing to do.
                if (!this.AtTop)
                {
                    this.NewPage();
                    this.brokenPage = this.page;
                }

                break;
            case RuleBlock:
                this.Ensure(12);
                this.Page.Items.Add(new LineItem(frame.Left, this.y + 6, frame.Left + frame.Width, this.y + 6, 0.75, this.design.Brand.TableLine));
                this.Advance(12, frame);
                break;
        }

        if (block is not HeadingBlock)
        {
            this.chainTop = null;
        }
    }

    private static Frame QuoteFrame(Frame frame) =>
        new(frame.Left + QuoteIndent, frame.Width - QuoteIndent, Grey, [.. frame.QuoteRules, frame.Left + 3]);

    private void Heading(HeadingBlock heading, Frame frame, IReadOnlyList<Block> siblings, int next)
    {
        var size = HeadingSize(heading);
        if ((heading.Level == 1 || heading.NewPage) && !this.AtTop)
        {
            this.NewPage();
        }

        this.Gap(size * 0.8, frame);
        var (runs, lines) = this.HeadingLines(heading, frame.Width);
        var height = (lines.Count * size * LineFactor) + (size * 0.35);

        // Kept with the start of what follows: further headings and their content included, and the footnotes of both.
        var above = this.ChainHeight + height;
        List<int>? kept = this.footnotes.Count > 0 ? [.. lines.SelectMany(this.NewNotes)] : null;
        this.keptNotes = kept;
        var keep = this.KeepHeight(siblings, next, frame, above);
        this.keptNotes = null;
        this.Ensure(height + keep + this.NotesExtra(kept));
        if (this.chainTop is null || this.chainPage != this.pages.Count)
        {
            this.chainTop = this.y;
            this.chainPage = this.pages.Count;
        }

        this.Mark(runs, heading.Level - 1, heading.Anchor, heading.Number);
        var indent = this.NumberIndent(heading);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == 0 && heading.Number is { } number)
            {
                // In a column of its own; the lines of the title hang after it.
                this.Ensure((size * LineFactor) + this.NotesExtra(this.NewNotes(lines[0])));
                this.Page.Items.Add(new TextItem(frame.Left, this.Baseline(size), this.Supported(number, FontFace.Bold), FontFace.Bold, size, this.design.Brand.Primary));
            }

            this.Line(lines[i], frame, frame.Left + indent, size, this.design.Brand.Primary);
        }

        this.Gap(size * 0.35, frame);
    }

    private static double HeadingSize(HeadingBlock heading) => HeadingSizes[Math.Clamp(heading.Level, 1, 6) - 1];

    // The width of the number column of each level that has numbers: its widest number, then a gap.
    private void NumberColumns(IReadOnlyList<Block> blocks)
    {
        foreach (var heading in Headings(blocks).Where(h => h.Number is not null))
        {
            var size = HeadingSize(heading);
            var width = this.metrics.Width(heading.Number!, FontFace.Bold, size) + (size * NumberGap);
            this.numberColumns[heading.Level] = Math.Max(this.numberColumns.GetValueOrDefault(heading.Level), width);
        }
    }

    private static IEnumerable<HeadingBlock> Headings(IEnumerable<Block> blocks) => blocks.SelectMany(block => block switch
    {
        HeadingBlock heading => [heading],
        QuoteBlock quote => Headings(quote.Blocks),
        ListBlock list => list.Items.SelectMany(Headings),
        DefinitionListBlock definitions => definitions.Items.SelectMany(d => Headings(d.Description)),
        _ => [],
    });

    // Where a numbered heading's title starts, after its number; 0 for a heading without one.
    private double NumberIndent(HeadingBlock heading) => heading.Number is null ? 0 : this.numberColumns.GetValueOrDefault(heading.Level);

    // Replaced and wrapped once per heading and width, like paragraphs; replaced only once, whatever the width, since the
    // table of contents sets the same text.
    private (List<TextRun> Runs, List<TextLine> Lines) HeadingLines(HeadingBlock heading, double width)
    {
        if (this.headings.TryGetValue(heading, out var cached) && cached.Width == width)
        {
            return (cached.Runs, cached.Lines);
        }

        var runs = this.HeadingRuns(heading);
        var lines = this.Wrap(runs, width - this.NumberIndent(heading), HeadingSize(heading), RunStyle.Bold);
        this.headings[heading] = (width, runs, lines);
        return (runs, lines);
    }

    private List<TextRun> HeadingRuns(HeadingBlock heading)
    {
        if (!this.headingRuns.TryGetValue(heading, out var runs))
        {
            runs = this.Supported(heading.Runs, RunStyle.Bold);
            this.headingRuns[heading] = runs;
        }

        return runs;
    }

    // The contents under their heading, set like a `##` heading and bookmarked but not listed: per entry, indented by its
    // level, its number hanging before the title, wrapped short of the page-number column, and a link to its heading.
    // The page numbers and dot leaders come once the body is laid out (ContentsPages).
    private void Contents(TableOfContents contents, Frame frame)
    {
        var size = HeadingSizes[1];
        var title = this.Supported([new TextRun(this.design.Texts.Contents, RunStyle.None)], RunStyle.Bold);
        this.Mark(title, 1, null);
        foreach (var line in this.Wrap(title, frame.Width, size, RunStyle.Bold))
        {
            this.Line(line, frame, frame.Left, size, this.design.Brand.Primary);
        }

        this.Gap(size * 0.35 + ParagraphGap, frame);
        var lineHeight = BodySize * LineFactor;
        var digit = Enumerable.Range(0, 10).Max(d => Math.Max(
            this.metrics.Width(d.ToString(CultureInfo.InvariantCulture), FontFace.Regular, BodySize),
            this.metrics.Width(d.ToString(CultureInfo.InvariantCulture), FontFace.Bold, BodySize)));
        var right = frame.Left + frame.Width - (digit * ContentsDigits) - ContentsGap;
        var columns = contents.Entries.Where(e => e.Heading.Number is not null)
            .GroupBy(e => e.Level)
            .ToDictionary(g => g.Key, g => g.Max(e => this.metrics.Width(e.Heading.Number!, EntryFace(e), BodySize)) + (BodySize * NumberGap));
        for (var i = 0; i < contents.Entries.Count; i++)
        {
            var entry = contents.Entries[i];
            var style = entry.Chapter ? RunStyle.Bold : RunStyle.None;
            var left = frame.Left + ((entry.Level - 1) * ContentsIndent);
            var textLeft = left + (entry.Heading.Number is null ? 0 : columns[entry.Level]);
            var runs = this.HeadingRuns(entry.Heading).Where(r => r.Footnote is null).Select(r => r.Link is null ? r : r with { Link = null }).ToList();
            var lines = this.Wrap(runs, right - textLeft, BodySize, style);
            if (entry.Chapter && i > 0)
            {
                this.Gap(ParagraphGap, frame);
            }

            this.Ensure(Math.Max(1, lines.Count) * lineHeight);
            var top = this.y;
            if (entry.Heading.Number is { } number)
            {
                this.Page.Items.Add(new TextItem(left, this.Baseline(BodySize), this.Supported(number, EntryFace(entry)), EntryFace(entry), BodySize, this.TextColor));
            }

            var (lastBaseline, lastEnd) = (this.Baseline(BodySize), textLeft);
            foreach (var line in lines)
            {
                (lastBaseline, lastEnd) = (this.Baseline(BodySize), textLeft + line.Width);
                this.Line(line, frame, textLeft, BodySize, this.TextColor, keepOnPage: true);
            }

            this.Page.Items.Add(new LinkItem(left, top, frame.Left + frame.Width - left, this.y - top, "#" + entry.Heading.Anchor));
            this.contentsSlots.Add(new ContentsSlot(this.pages.Count - 1, lastBaseline, lastEnd, entry.Heading.Anchor!, EntryFace(entry)));
            this.Gap(ListItemGap, frame);
        }
    }

    private static FontFace EntryFace(ContentsEntry entry) => entry.Chapter ? FontFace.Bold : FontFace.Regular;

    // The page number of each entry of the contents, as the footer shows it (the cover counts), right-aligned at the
    // margin, and a dot leader to it from the end of the title, its dots on one grid on every line.
    private void ContentsPages()
    {
        var face = FontFace.Regular;
        var pitch = this.metrics.Width(". ", face, BodySize);
        var dot = this.metrics.Width(".", face, BodySize);
        foreach (var slot in this.contentsSlots)
        {
            if (!this.anchors.TryGetValue(slot.Anchor, out var target))
            {
                continue;
            }

            var items = this.pages[slot.Page].Items;
            var number = (target.Page + 1).ToString(CultureInfo.InvariantCulture);
            var x = this.geometry.Right - this.metrics.Width(number, slot.Face, BodySize);
            items.Add(new TextItem(x, slot.Baseline, number, slot.Face, BodySize, this.TextColor));
            var start = this.geometry.Left + (Math.Ceiling((slot.End + ContentsGap - this.geometry.Left) / pitch) * pitch);
            var end = x - ContentsGap;
            if (pitch > 0 && end - start >= dot)
            {
                var count = (int)Math.Floor((end - start - dot) / pitch) + 1;
                items.Add(new TextItem(start, slot.Baseline, string.Join(' ', Enumerable.Repeat(".", count)), face, BodySize, this.TextColor));
            }
        }
    }

    // A caption: its label and number in bold, then its text; replaced and wrapped once per caption and width.
    private List<TextLine> CaptionLines(Caption caption, double width)
    {
        if (this.captionLines.TryGetValue(caption, out var cached) && cached.Width == width)
        {
            return cached.Lines;
        }

        var label = string.Create(CultureInfo.InvariantCulture, $"{Captions.Label(caption.Kind, this.design.Texts)} {caption.Number}:");
        List<TextRun> runs = [new TextRun(label, RunStyle.Bold), .. caption.Text.Count > 0 ? [new TextRun(" ", RunStyle.None), .. caption.Text] : Array.Empty<TextRun>()];
        var lines = this.Wrap(this.Supported(runs, RunStyle.None), width, CaptionSize, RunStyle.None);
        this.captionLines[caption] = (width, lines);
        return lines;
    }

    // The room a caption takes with its gap to the visual; 0 without one.
    private double CaptionHeight(Caption? caption, double width) =>
        caption is null ? 0 : CaptionGap + (this.CaptionLines(caption, width).Count * CaptionSize * LineFactor);

    // Draws a caption's lines centred in [left, left + width] from `top`, without moving on.
    private void CaptionAt(Caption caption, Frame frame, double left, double width, double top)
    {
        var y = this.y;
        var lineHeight = CaptionSize * LineFactor;
        var lines = this.CaptionLines(caption, width);
        for (var i = 0; i < lines.Count; i++)
        {
            this.y = top + (i * lineHeight);
            this.PlaceNotes(this.NewNotes(lines[i]));
            this.Line(lines[i], frame, left + Math.Max(0, (width - lines[i].Width) / 2), CaptionSize, frame.Color, advance: false);
        }

        this.y = y;
    }

    // A caption under a visual that has just been placed: drawn, then moved past.
    private void CaptionHere(Caption? caption, Frame frame)
    {
        if (caption is null)
        {
            return;
        }

        this.CaptionAt(caption, frame, frame.Left, frame.Width, this.y + CaptionGap);
        this.Advance(this.CaptionHeight(caption, frame.Width), frame);
    }

    // The footnotes a caption adds to the current page.
    private double CaptionNotes(Caption? caption, double width) =>
        caption is null || this.footnotes.Count == 0 ? 0 : this.NotesExtra([.. this.CaptionLines(caption, width).SelectMany(this.NewNotes)]);

    // The height of the headings directly above, on this page.
    private double ChainHeight => this.chainTop is { } top && this.chainPage == this.pages.Count ? this.y - top : 0;

    // The tallest a visual may be: a page, less the headings it is kept with, which then also leave room for this page's
    // footnotes.
    private double VisualRoom => this.ChainHeight > 0
        ? this.bottom - this.geometry.Top - this.ChainHeight
        : this.geometry.Bottom - this.geometry.Top;

    // How much of the blocks from `next` on must share the page with a heading above them. `above` is the height of
    // that heading and the ones before it, which a visual must leave room for.
    private double KeepHeight(IReadOnlyList<Block> siblings, int next, Frame frame, double above)
    {
        if (next >= siblings.Count)
        {
            return 0;
        }

        switch (siblings[next])
        {
            case HeadingBlock { Level: 1 } or HeadingBlock { NewPage: true } or PageBreakBlock:
                return 0;
            case HeadingBlock heading:
                var size = HeadingSize(heading);
                var headingLines = this.HeadingLines(heading, frame.Width).Lines;
                this.KeepNotes(headingLines, headingLines.Count);
                var height = (size * 0.8) + (headingLines.Count * size * LineFactor) + (size * 0.35);
                return height + this.KeepHeight(siblings, next + 1, frame, above + height);
            case ParagraphBlock paragraph:
                var lines = this.ParagraphLines(paragraph.Runs, frame.Width);
                this.KeepNotes(lines, KeepLines(lines.Count));
                return KeepLines(lines.Count) * BodySize * LineFactor;
            case ListBlock { Items.Count: > 0 } list:
                var inner = frame with { Left = frame.Left + ListIndent, Width = frame.Width - ListIndent };
                if (list.Items[0] is [ParagraphBlock first, ..])
                {
                    var itemLines = this.ParagraphLines(first.Runs, inner.Width);
                    this.KeepNotes(itemLines, KeepLines(itemLines.Count));
                    return KeepLines(itemLines.Count) * BodySize * LineFactor;
                }

                return Math.Max(BodySize * LineFactor, this.KeepHeight(list.Items[0], 0, inner, above));
            case QuoteBlock quote:
                return this.KeepHeight(quote.Blocks, 0, QuoteFrame(frame), above);
            case CodeBlock code:
                return code.Lines.Count switch
                {
                    <= 1 => (2 * CodePadding) + (CodeSize * LineFactor),
                    _ => CodePadding + (2 * CodeSize * LineFactor),
                };
            case TableBlock table:
                return this.Prepare(table, frame.Width) is { } prepared ? prepared.KeepHeight + this.CaptionHeight(table.Caption, frame.Width) : 0;
            case ImageBlock image:
                return Math.Min(this.ImageSize(image, frame).Height + this.CaptionHeight(image.Caption, frame.Width), this.geometry.Bottom - this.geometry.Top - above);
            case ImageRowBlock row:
                return Math.Min(this.RowSizes(row, frame).Max(s => s.Height) + this.RowCaptionHeight(row, frame), this.geometry.Bottom - this.geometry.Top - above);
            case DrawingBlock drawing:
                return Math.Min(
                    (DrawingScale(drawing.Drawing, frame, this.geometry) * drawing.Drawing.Height) + this.CaptionHeight(drawing.Caption, frame.Width),
                    this.geometry.Bottom - this.geometry.Top - above);
            case IconBlock:
                return FigureSize;
            case DefinitionListBlock { Items.Count: > 0 }:
                return 2 * BodySize * LineFactor;
            case RuleBlock:
                return 12;
            default:
                return 0;
        }
    }

    // The lines of a paragraph that must stay together at its start: all of a short one, else two.
    private static int KeepLines(int lines) => lines <= 3 ? Math.Max(1, lines) : 2;

    // While a heading measures what it is kept with: the footnotes of the first `count` of those lines.
    private void KeepNotes(List<TextLine> lines, int count)
    {
        if (this.keptNotes is { } kept)
        {
            kept.AddRange(lines.Take(count).SelectMany(this.NewNotes));
        }
    }

    // A numbered heading's bookmark starts with its number: "1.2 Title".
    private void Mark(IReadOnlyList<TextRun> title, int level, string? anchor, string? number = null)
    {
        var text = string.Concat(title.Where(r => r.Footnote is null).Select(r => r.IsBreak ? " " : r.Text)).Trim();
        if (text.Length > 0)
        {
            this.bookmarks.Add(new Bookmark(number is null ? text : number + " " + text, level, this.pages.Count - 1, this.y));
        }

        if (anchor is { Length: > 0 })
        {
            this.anchors.TryAdd(anchor, (this.pages.Count - 1, this.y));
        }
    }

    // A marker (bullet or number) goes right-aligned before the first line. A page break inside the paragraph leaves
    // at least two lines on either side: no orphan at the bottom, no widow at the top. A line with footnotes needs the
    // room for them too. Justified, every line but the last and those before a hard break reaches the right edge.
    private void Paragraph(IReadOnlyList<TextRun> runs, Frame frame, string? marker, double gapAfter, Drawing? icon = null, bool justify = false)
    {
        var lines = this.ParagraphLines(runs, frame.Width);
        if (lines.Count == 0 && marker is not null)
        {
            lines = [new TextLine([], 0)];
        }

        var lineHeight = BodySize * LineFactor;
        var placed = 0;
        while (placed < lines.Count)
        {
            var room = this.HasNewNotes(lines, placed) ? this.LinesThatFit(lines, placed, lineHeight) : (int)Math.Floor((this.bottom - this.y) / lineHeight);
            var rest = lines.Count - placed;
            var take = rest;
            if (room < rest)
            {
                take = rest - room < 2 ? rest - 2 : room;
                if (take < 2)
                {
                    if (!this.AtTop)
                    {
                        this.NewPage();
                        continue;
                    }

                    take = Math.Min(rest, Math.Max(1, room)); // a page too small for the rule
                }
            }

            for (var i = placed; i < placed + take; i++)
            {
                if (i == 0 && marker is not null)
                {
                    this.Marker(marker, frame, icon);
                }

                this.Line(lines[i], frame, frame.Left, BodySize, frame.Color, justify: justify && i < lines.Count - 1 ? frame.Width : null);
            }

            placed += take;
            if (placed < lines.Count)
            {
                this.NewPage();
            }
        }

        this.Gap(gapAfter, frame);
    }

    // Wrapped once per paragraph and width: headings measure what follows them with the very lines it is drawn in.
    private List<TextLine> ParagraphLines(IReadOnlyList<TextRun> runs, double width)
    {
        if (this.paragraphs.TryGetValue(runs, out var cached) && cached.Width == width)
        {
            return cached.Lines;
        }

        var lines = this.Wrap(this.Supported(runs, RunStyle.None), width, BodySize, RunStyle.None);
        this.paragraphs[runs] = (width, lines);
        return lines;
    }

    // An icon in place of the marker is drawn at the text size, in the accent colour, as task boxes are drawn.
    private void Marker(string marker, Frame frame, Drawing? icon = null)
    {
        if (icon is not null)
        {
            var top = this.Baseline(BodySize) - (BodySize * (0.5 + (CapHeight / 2)));
            this.Icon(icon, frame.Left - MarkerGap - BodySize, top, BodySize, this.design.Brand.Primary);
            return;
        }

        if (marker is EmptyBox or TickedBox)
        {
            this.Box(marker == TickedBox, frame);
            return;
        }

        marker = this.Supported(marker, FontFace.Regular);
        var width = this.metrics.Width(marker, FontFace.Regular, BodySize);
        this.Page.Items.Add(new TextItem(frame.Left - MarkerGap - width, this.Baseline(BodySize), marker, FontFace.Regular, BodySize, frame.Color));
    }

    // A task box, the height of a capital letter, sitting on the first line's baseline where a bullet would be.
    private void Box(bool ticked, Frame frame)
    {
        var side = BodySize * 0.72;
        var (left, bottom) = (frame.Left - MarkerGap - side, this.Baseline(BodySize));
        var top = bottom - side;
        this.Page.Items.Add(new ShapeItem(
            [new MoveTo(left, top), new LineTo(left + side, top), new LineTo(left + side, bottom), new LineTo(left, bottom), new ClosePath()],
            null,
            frame.Color,
            0.75,
            null,
            false));
        if (ticked)
        {
            this.Page.Items.Add(new ShapeItem(
                [new MoveTo(left + (side * 0.2), top + (side * 0.52)), new LineTo(left + (side * 0.42), top + (side * 0.76)), new LineTo(left + (side * 0.82), top + (side * 0.24))],
                null,
                frame.Color,
                1.1,
                null,
                true));
        }
    }

    // Each term in bold, kept with the start of its description, which is indented under it.
    private void Definitions(DefinitionListBlock list, Frame frame, int level)
    {
        var inner = frame with { Left = frame.Left + ListIndent, Width = frame.Width - ListIndent };
        foreach (var item in list.Items)
        {
            var terms = item.Terms.Select(t => (IReadOnlyList<TextRun>)[.. t.Select(r => r.IsBreak ? r : r with { Style = r.Style | RunStyle.Bold })]).ToList();
            var termLines = terms.Sum(t => Math.Max(1, this.ParagraphLines(t, frame.Width).Count));
            this.Ensure((termLines * BodySize * LineFactor) + (item.Description.Count > 0 ? this.KeepHeight(item.Description, 0, inner, 0) : 0));
            foreach (var term in terms)
            {
                this.Paragraph(term, frame, null, ListItemGap);
            }

            this.Blocks(item.Description, inner, level);
        }
    }

    private void List(ListBlock list, Frame frame, int level)
    {
        var inner = frame with { Left = frame.Left + ListIndent, Width = frame.Width - ListIndent };
        for (var i = 0; i < list.Items.Count; i++)
        {
            var marker = list.Tasks?[i] is { } ticked ? ticked ? TickedBox : EmptyBox
                : list.Ordered ? (list.Start + i).ToString(CultureInfo.InvariantCulture) + "." : level % 2 == 0 ? "•" : "–";
            var item = list.Items[i];
            var icon = list.Icons?[i];
            if (item.Count == 0)
            {
                this.Paragraph([], inner, marker, ListItemGap, icon);
                continue;
            }

            for (var b = 0; b < item.Count; b++)
            {
                if (b == 0 && item[b] is ParagraphBlock paragraph)
                {
                    this.Paragraph(paragraph.Runs, inner, marker, ListItemGap, icon, this.design.Justify);
                }
                else
                {
                    if (b == 0)
                    {
                        this.Ensure(BodySize * LineFactor);
                        this.Marker(marker, inner, icon);
                    }

                    this.Block(item[b], inner, item[b] is ListBlock ? level + 1 : level, item, b + 1);
                }
            }
        }

        if (level == 0)
        {
            this.Gap(ParagraphGap - ListItemGap, frame);
        }
    }

    private void Code(CodeBlock code, Frame frame)
    {
        var lineHeight = CodeSize * LineFactor;
        var inner = frame.Width - (2 * CodePadding);
        var lines = code.Lines.SelectMany(line => this.BreakByCharacters(
            [.. line.Select(span => span with { Text = this.Supported(span.Text, FontFace.Mono) })], FontFace.Mono, CodeSize, inner)).ToList();
        while (lines.Count > 0 && lines[^1].Sum(span => span.Text.Length) == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var colors = this.design.Brand.Code.On(CodeBackground);

        this.Ensure(CodePadding + lineHeight);
        this.Page.Items.Add(new RectItem(frame.Left, this.y, frame.Width, CodePadding, CodeBackground));
        this.Advance(CodePadding, frame);
        foreach (var line in lines)
        {
            this.Ensure(lineHeight);
            this.Page.Items.Add(new RectItem(frame.Left, this.y, frame.Width, lineHeight, CodeBackground));
            // A span after another starts where the one before it ends: the mono face's widths add up.
            var x = frame.Left + CodePadding;
            foreach (var span in line.Where(s => s.Text.Length > 0))
            {
                this.Page.Items.Add(new TextItem(x, this.Baseline(CodeSize), span.Text, FontFace.Mono, CodeSize, colors.Of(span.Kind) ?? this.TextColor));
                x += this.metrics.Width(span.Text, FontFace.Mono, CodeSize);
            }

            this.Advance(lineHeight, frame);
        }

        this.Ensure(CodePadding);
        this.Page.Items.Add(new RectItem(frame.Left, this.y, frame.Width, CodePadding, CodeBackground));
        this.Advance(CodePadding, frame);
        this.Gap(ParagraphGap, frame);
    }

    // A caption goes under the image and stays on its page: the image leaves room for it.
    private void Image(ImageBlock image, Frame frame)
    {
        var (width, height) = this.ImageSize(image, frame);
        var caption = this.CaptionHeight(image.Caption, frame.Width);
        var room = this.VisualRoom - caption;
        if (height > room)
        {
            // Smaller still, to share a page with the headings above it.
            width *= room / height;
            height = room;
        }

        this.Ensure(height + caption + this.CaptionNotes(image.Caption, frame.Width));
        this.Page.Items.Add(Placed(image, frame.Left + ((frame.Width - width) / 2), this.y, width, height));
        this.Advance(height, frame);
        this.CaptionHere(image.Caption, frame);
        this.Gap(ParagraphGap, frame);
    }

    // Side by side in cells of equal width, top-aligned, each centred in its cell and its caption under it in the cell;
    // the row moves to the next page whole.
    private void ImageRow(ImageRowBlock row, Frame frame)
    {
        var sizes = this.RowSizes(row, frame);
        var height = sizes.Max(s => s.Height);
        var captions = this.RowCaptionHeight(row, frame);
        var room = this.VisualRoom - captions;
        var shrink = height > room ? room / height : 1.0;
        height *= shrink;
        var cell = (frame.Width - ((row.Images.Count - 1) * RowGap)) / row.Images.Count;
        this.Ensure(height + captions + (this.footnotes.Count == 0 ? 0
            : this.NotesExtra([.. row.Images.Where(i => i.Caption is not null).SelectMany(i => this.CaptionLines(i.Caption!, cell).SelectMany(this.NewNotes))])));
        for (var i = 0; i < row.Images.Count; i++)
        {
            var (width, imageHeight) = (sizes[i].Width * shrink, sizes[i].Height * shrink);
            var image = row.Images[i];
            this.Page.Items.Add(Placed(image, frame.Left + (i * (cell + RowGap)) + ((cell - width) / 2), this.y, width, imageHeight));
            if (image.Caption is { } caption)
            {
                this.CaptionAt(caption, frame, frame.Left + (i * (cell + RowGap)), cell, this.y + height + CaptionGap);
            }
        }

        this.Advance(height + captions, frame);
        this.Gap(ParagraphGap, frame);
    }

    // The tallest caption of a row's images, in their cells.
    private double RowCaptionHeight(ImageRowBlock row, Frame frame)
    {
        var cell = (frame.Width - ((row.Images.Count - 1) * RowGap)) / row.Images.Count;
        return row.Images.Max(image => this.CaptionHeight(image.Caption, cell));
    }

    // Images with `fit=cover` fill their cell at the row's height: that of its tallest other image, or 3/4 of the cell's
    // width when all of them cover; round ones are the largest circle in that box.
    private List<(double Width, double Height)> RowSizes(ImageRowBlock row, Frame frame)
    {
        var cell = frame with { Width = (frame.Width - ((row.Images.Count - 1) * RowGap)) / row.Images.Count };
        var sizes = row.Images.Select(image => this.ImageSize(image, cell)).ToList();
        bool Covers(int i) => row.Images[i].Attributes.Fit == ImageFit.Cover;
        if (!Enumerable.Range(0, sizes.Count).Any(Covers))
        {
            return sizes;
        }

        var others = Enumerable.Range(0, sizes.Count).Where(i => !Covers(i)).Select(i => sizes[i].Height).ToList();
        var height = Math.Min(others.Count > 0 ? others.Max() : cell.Width * 0.75, this.geometry.Bottom - this.geometry.Top);
        var side = Math.Min(cell.Width, height);
        return [.. sizes.Select((size, i) => !Covers(i) ? size : row.Images[i].Attributes.Round ? (side, side) : (cell.Width, height))];
    }

    // The image at its place; a cropped or round one is drawn whole, larger, and shows only through its box.
    private static ImageItem Placed(ImageBlock image, double x, double y, double width, double height)
    {
        var item = new ImageItem(image.Resource, image.Resource.Content, image.Info.Format, x, y, width, height);
        var attributes = image.Attributes;
        var crop = width > 0 && height > 0 ? attributes.Crop(image.Info.Width, image.Info.Height, width / height) : default;
        if (crop.IsNone && !attributes.Round)
        {
            return item;
        }

        var (visibleWidth, visibleHeight) = crop.Visible;
        var (wholeWidth, wholeHeight) = (width / visibleWidth, height / visibleHeight);
        return item with
        {
            X = x - (crop.Left * wholeWidth),
            Y = y - (crop.Top * wholeHeight),
            Width = wholeWidth,
            Height = wholeHeight,
            Clip = new ImageClip(x, y, width, height, attributes.Round),
        };
    }

    // 96 DPI: a pixel is 0.75 pt. Scaled down to the content width and the page height, never up. A round image is
    // the square in it.
    private (double Width, double Height) ImageSize(ImageBlock image, Frame frame)
    {
        var (pixelWidth, pixelHeight) = image.Attributes.Round
            ? (Math.Min(image.Info.Width, image.Info.Height), Math.Min(image.Info.Width, image.Info.Height))
            : (image.Info.Width, image.Info.Height);
        var width = pixelWidth * PointsPerPixel * image.Scale;
        var height = pixelHeight * PointsPerPixel * image.Scale;
        var fit = Math.Min(1.0, Math.Min(frame.Width / width, (this.geometry.Bottom - this.geometry.Top) / height));
        return (width * fit, height * fit);
    }

    private static double DrawingScale(Drawing drawing, Frame frame, PageGeometry geometry) =>
        drawing.Width <= 0 || drawing.Height <= 0 ? 0 : PointsPerPixel * Math.Min(
            1.0,
            Math.Min(frame.Width / (drawing.Width * PointsPerPixel), (geometry.Bottom - geometry.Top) / (drawing.Height * PointsPerPixel)));

    // Placed like an image: centred, scaled down to the content width and the page height, never up; a caption under it.
    private void Drawing(Drawing drawing, Frame frame, Caption? caption = null)
    {
        drawing.Validate();
        if (drawing.Width <= 0 || drawing.Height <= 0)
        {
            return;
        }

        var scale = DrawingScale(drawing, frame, this.geometry);
        var captionHeight = this.CaptionHeight(caption, frame.Width);
        var room = this.VisualRoom - captionHeight;
        if (drawing.Height * scale > room)
        {
            scale = room / drawing.Height;
        }

        var height = drawing.Height * scale;
        this.Ensure(height + captionHeight + this.CaptionNotes(caption, frame.Width));
        var left = frame.Left + ((frame.Width - (drawing.Width * scale)) / 2);
        var top = this.y;
        (double X, double Y) Place(double x, double y) => (left + (x * scale), top + (y * scale));
        foreach (var item in drawing.Items)
        {
            switch (item)
            {
                case DrawingShape shape:
                    var path = shape.Path.Segments.Select(segment => segment switch
                    {
                        MoveTo m => (PathSegment)new MoveTo(Place(m.X, m.Y).X, Place(m.X, m.Y).Y),
                        LineTo l => new LineTo(Place(l.X, l.Y).X, Place(l.X, l.Y).Y),
                        CubicTo c => new CubicTo(Place(c.X1, c.Y1).X, Place(c.X1, c.Y1).Y, Place(c.X2, c.Y2).X, Place(c.X2, c.Y2).Y, Place(c.X, c.Y).X, Place(c.X, c.Y).Y),
                        _ => segment,
                    }).ToList();
                    this.Page.Items.Add(new ShapeItem(path, shape.Fill, shape.Stroke, shape.StrokeWidth * scale, shape.Dash?.Select(d => d * scale).ToList(), shape.RoundJoins) { FillRule = shape.FillRule });
                    break;
                case DrawingText text:
                    var (x, baseline) = Place(text.X, text.Baseline);
                    this.Label(text, x, baseline, scale);
                    break;
            }
        }

        this.Advance(height, frame);
        this.CaptionHere(caption, frame);
        this.Gap(ParagraphGap, frame);
    }

    // An icon as shapes in a square of `side` points at (left, top), every stroke and fill in `colour`.
    private void Icon(Drawing icon, double left, double top, double side, string colour)
    {
        icon.Validate();
        var scale = side / Math.Max(Math.Max(icon.Width, icon.Height), 1e-9);
        (double X, double Y) Place(double x, double y) => (left + (x * scale), top + (y * scale));
        foreach (var shape in icon.Items.OfType<DrawingShape>())
        {
            var path = shape.Path.Segments.Select(segment => segment switch
            {
                MoveTo m => (PathSegment)new MoveTo(Place(m.X, m.Y).X, Place(m.X, m.Y).Y),
                LineTo l => new LineTo(Place(l.X, l.Y).X, Place(l.X, l.Y).Y),
                CubicTo c => new CubicTo(Place(c.X1, c.Y1).X, Place(c.X1, c.Y1).Y, Place(c.X2, c.Y2).X, Place(c.X2, c.Y2).Y, Place(c.X, c.Y).X, Place(c.X, c.Y).Y),
                _ => segment,
            }).ToList();
            this.Page.Items.Add(new ShapeItem(
                path,
                shape.Fill is null ? null : colour,
                shape.Stroke is null ? null : colour,
                shape.StrokeWidth * scale,
                shape.Dash?.Select(d => d * scale).ToList(),
                shape.RoundJoins) { FillRule = shape.FillRule });
        }
    }

    // A drawing's text in the diagram faces (the table faces), shrunk to its room down to 70 %, then anchored.
    private void Label(DrawingText text, double x, double baseline, double scale)
    {
        var spans = text.Spans
            .Select(span =>
            {
                var face = text.Mono ? span.Bold ? FontFace.MonoBold : FontFace.Mono : span.Bold ? FontFace.TableBold : FontFace.TableRegular;
                return (Text: this.Supported(Visible(span.Text), face), Face: face, Color: span.Color ?? text.Color);
            })
            .Where(span => span.Text.Length > 0)
            .ToList();
        double Measure(double size) => spans.Sum(span => this.metrics.Width(span.Text, span.Face, size));

        var size = text.Size * scale;
        var width = Measure(size);
        if (text.MaxWidth is { } room && width > room * scale)
        {
            size *= Math.Max(MinLabelScale, room * scale / width);
            width = Measure(size);
        }

        x -= text.Anchor switch
        {
            TextAnchor.Middle => width / 2,
            TextAnchor.End => width,
            _ => 0,
        };
        foreach (var (spanText, face, color) in spans)
        {
            this.Page.Items.Add(new TextItem(x, baseline, spanText, face, size, color));
            x += this.metrics.Width(spanText, face, size);
        }
    }

    // Replaced, measured and wrapped once per table and width, so a heading above it measures what is drawn.
    private PreparedTable? Prepare(TableBlock table, double width)
    {
        if (this.tables.TryGetValue(table, out var cached) && cached.Width == width)
        {
            return cached.Table;
        }

        PreparedTable? prepared = null;
        var columns = table.Alignments.Count;
        if (columns > 0 && table.Rows.Count > 0)
        {
            var supported = table with
            {
                Rows = [.. table.Rows.Select((row, r) => (IReadOnlyList<IReadOnlyList<TextRun>>)
                    [.. row.Select(cell => this.Supported(cell, CellStyle(table, r)))])],
            };
            var widths = this.ColumnWidths(supported, width);
            var cells = supported.Rows
                .Select((row, r) => Enumerable.Range(0, columns)
                    .Select(c => this.Wrap(row[c], widths[c] - (2 * CellPadding), TableSize, CellStyle(supported, r)))
                    .ToList())
                .ToList();
            var lineHeight = TableSize * LineFactor;
            var headerHeight = supported.HasHeader ? RowHeight(cells[0], lineHeight) : 0;

            // The header and the start of the first row stay together; a first row taller than that may split.
            var firstRow = RowHeight(cells[supported.HasHeader ? Math.Min(1, cells.Count - 1) : 0], lineHeight);
            prepared = new PreparedTable(widths, cells, headerHeight + Math.Min(firstRow, (3 * lineHeight) + (2 * CellPadding)));
        }

        this.tables[table] = (width, prepared);
        return prepared;
    }

    private void Table(TableBlock table, Frame frame)
    {
        if (this.Prepare(table, frame.Width) is not { } prepared)
        {
            return;
        }

        var (widths, cells, keepHeight) = prepared;
        var lineHeight = TableSize * LineFactor;
        var header = table.HasHeader ? cells[0] : null;
        var headerHeight = header is null ? 0 : RowHeight(header, lineHeight);

        // The caption goes above, with the header and the start of the first row; continuation pages repeat only the header.
        var caption = this.CaptionHeight(table.Caption, frame.Width);
        this.Ensure(keepHeight + caption + this.CaptionNotes(table.Caption, frame.Width) + (header is null ? 0 : this.NotesExtra(this.RowNotes(header, 0, header.Max(c => c.Count)))));
        if (table.Caption is { } tableCaption)
        {
            this.CaptionAt(tableCaption, frame, frame.Left, frame.Width, this.y);
            this.Advance(caption, frame);
        }
        for (var r = 0; r < cells.Count; r++)
        {
            var isHeader = header is not null && r == 0;
            var fill = isHeader ? this.design.Brand.TableHeader : (r - (header is null ? 0 : 1)) % 2 == 0 ? this.design.Brand.TableBand : null;
            var row = cells[r];
            var start = 0;
            var lineCount = row.Max(c => Math.Max(1, c.Count));

            // Moved once to a new page, the row goes there however little fits: its footnotes may never fit beside it.
            var moved = false;
            while (start < lineCount)
            {
                var room = this.footnotes.Count > 0 && this.RowNotes(row, start, lineCount - start).Count > 0
                    ? this.RowLinesThatFit(row, start, lineCount - start, lineHeight)
                    : (int)Math.Floor((this.bottom - this.y - (2 * CellPadding) + 0.01) / lineHeight);
                var fresh = this.geometry.Bottom - this.geometry.Top - headerHeight - (2 * CellPadding);
                var fitsFresh = (lineCount - start) * lineHeight <= fresh;
                if (room < lineCount - start && (fitsFresh || room < 1) && !this.AtTop && !isHeader && !moved)
                {
                    this.NewPage();
                    moved = true;
                    if (header is not null)
                    {
                        this.Row(header, widths, table.Alignments, frame, 0, header.Max(c => Math.Max(1, c.Count)), this.design.Brand.TableHeader, true);
                    }

                    continue;
                }

                var count = Math.Max(1, Math.Min(lineCount - start, room));
                this.Row(row, widths, table.Alignments, frame, start, count, fill, isHeader);
                start += count;
                moved = false;
            }
        }

        this.Gap(ParagraphGap, frame);
    }

    // Draws lines [start, start + count) of every cell of a row: fill, text, grid lines.
    private void Row(List<List<TextLine>> row, double[] widths, IReadOnlyList<CellAlignment> alignments, Frame frame, int start, int count, string? fill, bool isHeader)
    {
        var lineHeight = TableSize * LineFactor;
        var height = (count * lineHeight) + (2 * CellPadding);
        var top = this.y;
        var total = widths.Sum();
        this.PlaceNotes(this.RowNotes(row, start, count));
        if (fill is not null)
        {
            this.Page.Items.Add(new RectItem(frame.Left, top, total, height, fill));
        }

        var x = frame.Left;
        for (var c = 0; c < widths.Length; c++)
        {
            var lines = row[c];
            for (var i = start; i < Math.Min(lines.Count, start + count); i++)
            {
                var line = lines[i];
                var inner = widths[c] - (2 * CellPadding);
                var offset = alignments[c] switch
                {
                    CellAlignment.Right => inner - line.Width,
                    CellAlignment.Center => (inner - line.Width) / 2,
                    _ => 0,
                };
                this.y = top + CellPadding + ((i - start) * lineHeight);
                this.Line(line, frame, x + CellPadding + Math.Max(0, offset), TableSize, isHeader ? White : frame.Color, advance: false);
            }

            x += widths[c];
        }

        var grid = this.design.Brand.TableLine;
        this.Page.Items.Add(new LineItem(frame.Left, top, frame.Left + total, top, 0.5, grid));
        this.Page.Items.Add(new LineItem(frame.Left, top + height, frame.Left + total, top + height, 0.5, grid));
        x = frame.Left;
        foreach (var width in widths.Prepend(0))
        {
            x += width;
            this.Page.Items.Add(new LineItem(x, top, x, top + height, 0.5, grid));
        }

        this.y = top;
        this.Advance(height, frame);
    }

    // The footnotes not yet on a page in lines [start, start + count) of a row's cells.
    private List<int> RowNotes(List<List<TextLine>> row, int start, int count) =>
        this.footnotes.Count == 0 ? [] : [.. row.SelectMany(cell => cell.Skip(start).Take(count)).SelectMany(this.NewNotes).Distinct()];

    // How many lines of a row from `start` fit on this page, with the footnotes they bring.
    private int RowLinesThatFit(List<List<TextLine>> row, int start, int count, double lineHeight)
    {
        var fit = 0;
        while (fit < count && this.y + (2 * CellPadding) + ((fit + 1) * lineHeight) + this.NotesExtra(this.RowNotes(row, start, fit + 1)) <= this.bottom + 0.01)
        {
            fit++;
        }

        return fit;
    }

    // Cells are in the table faces; the header row is bold.
    private static RunStyle CellStyle(TableBlock table, int row) => row == 0 && table.HasHeader ? RunStyle.Table | RunStyle.Bold : RunStyle.Table;

    private static double RowHeight(List<List<TextLine>> row, double lineHeight) =>
        (row.Max(c => Math.Max(1, c.Count)) * lineHeight) + (2 * CellPadding);

    // Every column gets at least its longest word; the rest of the width is shared in proportion to the content.
    internal double[] ColumnWidths(TableBlock table, double available)
    {
        var columns = table.Alignments.Count;
        var minimum = new double[columns];
        var natural = new double[columns];
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var style = CellStyle(table, r);
            for (var c = 0; c < columns; c++)
            {
                var pieces = this.Pieces(table.Rows[r][c], TableSize, style);
                var lineWidth = 0.0;
                var wordWidth = 0.0;
                foreach (var piece in pieces)
                {
                    if (piece.IsBreak)
                    {
                        natural[c] = Math.Max(natural[c], lineWidth);
                        lineWidth = 0;
                        continue;
                    }

                    lineWidth += piece.Width;
                    if (piece.IsSpace)
                    {
                        wordWidth = 0;
                    }
                    else
                    {
                        wordWidth += piece.Width;
                        minimum[c] = Math.Max(minimum[c], wordWidth);
                    }
                }

                natural[c] = Math.Max(natural[c], lineWidth);
            }
        }

        for (var c = 0; c < columns; c++)
        {
            minimum[c] += 2 * CellPadding;
            natural[c] = Math.Max(natural[c] + (2 * CellPadding), minimum[c]);
        }

        if (minimum.Sum() >= available)
        {
            var scale = available / minimum.Sum();
            return [.. minimum.Select(m => m * scale)];
        }

        var spare = natural.Select((n, c) => n - minimum[c]).ToArray();
        if (natural.Sum() <= available || spare.Sum() <= 0)
        {
            var extra = available - natural.Sum();
            return [.. natural.Select(n => n + (extra * n / natural.Sum()))];
        }

        var share = available - minimum.Sum();
        return [.. minimum.Select((m, c) => m + (share * spare[c] / spare.Sum()))];
    }

    private void Cover(Cover cover)
    {
        this.NewPage(isCover: true);
        var brand = this.design.Brand;
        var width = this.geometry.Width;
        var height = this.geometry.Height;
        var margin = this.geometry.Left;
        if (brand.CoverArtwork is { } artwork && ImageInfo.Read(artwork) is { } art)
        {
            var ratio = (double)art.Height / art.Width;
            var w = width;
            var h = w * ratio;
            if (h > height * 0.42)
            {
                h = height * 0.42;
                w = h / ratio;
            }

            this.Page.Items.Add(new ImageItem(artwork, artwork, art.Format, width - w, height - h, w, h));
        }

        this.y = height * 0.28;
        if (this.design.Marking is { } marking)
        {
            this.Page.Items.Add(new TextItem(margin, this.y - CoverMarkingGap, this.Supported(marking, FontFace.Bold), FontFace.Bold, CoverMarkingSize, this.design.MarkingColor ?? brand.Footer));
        }

        var title = this.Supported(cover.Title, RunStyle.Bold);
        this.Mark(title, 0, cover.Anchor);
        var frame = new Frame(margin, this.geometry.ContentWidth * 0.85, this.TextColor, []);
        foreach (var line in this.Wrap(title, frame.Width, CoverTitleSize, RunStyle.Bold))
        {
            this.Line(line, frame, margin, CoverTitleSize, brand.Primary, keepOnPage: true);
        }

        this.y += 10;
        foreach (var paragraph in cover.Subtitle)
        {
            foreach (var line in this.Wrap(this.Supported(paragraph, RunStyle.None), frame.Width, CoverSubtitleSize, RunStyle.None))
            {
                this.Line(line, frame, margin, CoverSubtitleSize, this.TextColor, keepOnPage: true);
            }

            this.y += 4;
        }

        if (this.design.ShowTagline && brand.Tagline is { } tagline && ImageInfo.Read(tagline) is { } taglineInfo)
        {
            var w = width * 0.17;
            var h = w * taglineInfo.Height / taglineInfo.Width;
            this.Page.Items.Add(new ImageItem(tagline, tagline, taglineInfo.Format, width - margin - w, height - (margin * 0.6) - h, w, h));
        }

        if (brand.Logo is { } logo && ImageInfo.Read(logo) is { } logoInfo)
        {
            var w = width * 0.16;
            var h = w * logoInfo.Height / logoInfo.Width;
            this.Page.Items.Add(new ImageItem(logo, logo, logoInfo.Format, width - margin - w, margin * 0.7, w, h));
        }
    }

    // Header (document title, marking, logo) and footer (footer text, date, page number) on every page but the cover; a
    // draft's watermark under everything on every page.
    private void Decorate(string? title)
    {
        var brand = this.design.Brand;
        var g = this.geometry;
        var logoInfo = brand.Logo is { } logo ? ImageInfo.Read(logo) : null;
        var marking = this.design.Marking is { } marked ? this.Supported(marked, FontFace.Bold) : null;
        var markingWidth = marking is null ? 0 : this.metrics.Width(marking, FontFace.Bold, FooterSize);
        var watermark = this.design.Watermark is { } draft ? this.Watermark(this.Supported(draft, FontFace.Regular)) : null;
        List<string>? footer = null;
        for (var i = 0; i < this.pages.Count; i++)
        {
            var items = this.pages[i].Items;
            if (watermark is not null)
            {
                items.Insert(0, watermark);
            }

            if (this.pages[i].IsCover)
            {
                continue;
            }

            var logoWidth = 0.0;
            if (logoInfo is { } info)
            {
                var h = 18.0;
                logoWidth = h * info.Width / info.Height;
                items.Add(new ImageItem(brand.Logo!, brand.Logo, info.Format, g.Right - logoWidth, 24, logoWidth, h));
            }

            if (marking is not null)
            {
                var right = g.Right - (logoWidth > 0 ? logoWidth + HeaderGap : 0);
                items.Add(new TextItem(right - markingWidth, 38, marking, FontFace.Bold, FooterSize, this.design.MarkingColor ?? brand.Footer));
            }

            if (title is { Length: > 0 })
            {
                var room = g.ContentWidth - logoWidth - HeaderGap - (marking is null ? 0 : markingWidth + HeaderGap);
                var text = this.Fit(title, FontFace.Regular, FooterSize, room);
                items.Add(new TextItem(g.Left, 38, text, FontFace.Regular, FooterSize, brand.Footer));
            }

            var baseline = g.Height - 28;
            var x = g.Left;
            footer ??= [.. new[] { brand.FooterText, this.design.Date }.OfType<string>().Select(t => this.Supported(t, FontFace.Regular))];
            foreach (var text in footer)
            {
                items.Add(new TextItem(x, baseline, text, FontFace.Regular, FooterSize, brand.Footer));
                x += this.metrics.Width(text, FontFace.Regular, FooterSize) + 14;
            }

            var number = string.Create(CultureInfo.InvariantCulture, $"{i + 1} / {this.pages.Count}");
            items.Add(new TextItem(g.Right - this.metrics.Width(number, FontFace.Regular, FooterSize), baseline, number, FontFace.Regular, FooterSize, brand.Footer));
        }
    }

    // The watermark: the text at 45°, as long as WatermarkShare of the page's diagonal, centred on the page, in a light
    // tint of the text colour; opaque, since it lies under everything.
    private TextItem Watermark(string text)
    {
        var (width, height) = (this.geometry.Width, this.geometry.Height);
        var length = WatermarkShare * Math.Sqrt((width * width) + (height * height));
        var size = length * 100 / this.metrics.Width(text, FontFace.Regular, 100);
        var cap = size * CapHeight;
        var (cos, sin) = (Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4));
        return new TextItem(
            (width / 2) - (length / 2 * cos) + (cap / 2 * sin),
            (height / 2) + (length / 2 * sin) + (cap / 2 * cos),
            text,
            FontFace.Regular,
            size,
            ResolvedBrand.Mix(this.TextColor, WatermarkTint))
        {
            Angle = 45,
        };
    }

    private string Fit(string text, FontFace face, double size, double width)
    {
        this.CheckGlyphs(text, face);
        if (this.metrics.Width(text, face, size) <= width)
        {
            return text;
        }

        var elements = TextElements(text).ToList();
        while (elements.Count > 1 && this.metrics.Width(text + "…", face, size) > width)
        {
            elements.RemoveAt(elements.Count - 1);
            text = string.Concat(elements);
        }

        return text.TrimEnd() + "…";
    }

    private double Baseline(double size) => this.y + (size * 1.05);

    private string LabelColour(LabelCategory category) => category switch
    {
        LabelCategory.Success => this.design.Brand.Labels.Success,
        LabelCategory.Info => this.design.Brand.Labels.Info,
        LabelCategory.Warning => this.design.Brand.Labels.Warning,
        LabelCategory.Danger => this.design.Brand.Labels.Danger,
        _ => this.design.Brand.Labels.Neutral,
    };

    // Places one line at x; links get their colour, an underline and a link area; struck text a line through it. A line
    // that advances brings its footnotes onto the page: it moves to the next page unless they fit with it. With
    // `justify`, the line's spaces grow evenly so that it ends at x + justify, unless it has no space, already fills the
    // width, or its spaces would grow past MaxSpaceStretch times their width; each word is then placed on its own.
    private void Line(TextLine line, Frame frame, double x, double size, string color, bool advance = true, bool keepOnPage = false, double? justify = null)
    {
        var lineHeight = size * LineFactor;
        var notes = advance ? this.NewNotes(line) : [];
        if (advance && !keepOnPage)
        {
            this.Ensure(lineHeight + this.NotesExtra(notes));
        }

        this.PlaceNotes(notes);
        var baseline = this.Baseline(size);
        var stretch = justify is { } target && !line.EndsWithBreak ? Stretch(line, target) : null;
        var pieces = stretch is { } gap ? Words(line.Pieces, gap) : [.. Merge(line.Pieces)];
        for (var k = 0; k < pieces.Count; k++)
        {
            var piece = pieces[k];
            var pieceColor = piece.Colour is { } category ? this.LabelColour(category) : piece.Link is not null ? this.design.Brand.Link : color;
            var raised = piece.Style.HasFlag(RunStyle.Superscript) ? baseline - (size * SuperscriptRise) : baseline;
            if (piece.Icon is { } icon)
            {
                this.Icon(icon, x + (piece.Size * ThinSpace), baseline - (piece.Size * (0.5 + (CapHeight / 2))), piece.Size, pieceColor);
            }
            else
            {
                this.Page.Items.Add(new TextItem(x, raised, piece.Text, piece.Face, piece.Size, pieceColor));
            }

            // Justified, a space is a piece of its own: underlined between linked words, struck between struck ones.
            if (piece.Link is { } link && (!piece.IsSpace || (stretch is not null && Between(pieces, k, p => p.Link == link))))
            {
                this.Page.Items.Add(new LineItem(x, baseline + 1.2, x + piece.Width, baseline + 1.2, 0.5, pieceColor));
                this.Page.Items.Add(new LinkItem(x, this.y, piece.Width, lineHeight, link));
            }
            else if (piece.Footnote is { } number && this.footnotes.ContainsKey(number))
            {
                // A reference links to its note, without the look of a link.
                this.Page.Items.Add(new LinkItem(x, this.y, piece.Width, lineHeight, "#" + NoteAnchor(number)));
            }

            if (piece.Style.HasFlag(RunStyle.Strike) && (!piece.IsSpace || stretch is null || Between(pieces, k, p => p.Style.HasFlag(RunStyle.Strike))))
            {
                var middle = baseline - (piece.Size * 0.3);
                this.Page.Items.Add(new LineItem(x, middle, x + piece.Width, middle, 0.6, pieceColor));
            }

            x += piece.Width;
        }

        if (advance)
        {
            this.Advance(lineHeight, frame);
        }
    }

    // How much each space of a line grows to end it at `width`; null: the line stays left-aligned.
    private static double? Stretch(TextLine line, double width)
    {
        var spaces = line.Pieces.Where(p => p.IsSpace).ToList();
        var extra = width - line.Width;
        var normal = spaces.Sum(p => p.Width);
        if (spaces.Count == 0 || extra <= 1e-9 || normal <= 0 || normal + extra > MaxSpaceStretch * normal)
        {
            return null;
        }

        return extra / spaces.Count;
    }

    // A justified line: each word merged on its own, each space a piece of its own, `gap` wider.
    private static List<Piece> Words(IReadOnlyList<Piece> pieces, double gap)
    {
        var result = new List<Piece>();
        var word = new List<Piece>();
        foreach (var piece in pieces)
        {
            if (piece.IsSpace)
            {
                result.AddRange(Merge(word));
                word.Clear();
                result.Add(piece with { Width = piece.Width + gap });
            }
            else
            {
                word.Add(piece);
            }
        }

        result.AddRange(Merge(word));
        return result;
    }

    // Whether the pieces on both sides of pieces[k] are what `same` asks.
    private static bool Between(List<Piece> pieces, int k, Func<Piece, bool> same) =>
        k > 0 && k < pieces.Count - 1 && same(pieces[k - 1]) && same(pieces[k + 1]);

    // Joins neighbouring pieces in the same face, size, link and decoration into one text item; a footnote reference
    // stays a piece of its own.
    private static IEnumerable<Piece> Merge(IReadOnlyList<Piece> pieces)
    {
        Piece? current = null;
        foreach (var piece in pieces)
        {
            if (current is not null && current.Icon is null && piece.Icon is null && current.Face == piece.Face && current.Size == piece.Size && current.Link == piece.Link
                && current.Colour == piece.Colour && current.Style.HasFlag(RunStyle.Strike) == piece.Style.HasFlag(RunStyle.Strike)
                && current.Style.HasFlag(RunStyle.Superscript) == piece.Style.HasFlag(RunStyle.Superscript) && current.Footnote == piece.Footnote)
            {
                current = current with { Text = current.Text + piece.Text, Width = current.Width + piece.Width, IsSpace = current.IsSpace && piece.IsSpace };
            }
            else
            {
                if (current is not null)
                {
                    yield return current;
                }

                current = piece;
            }
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    /// <summary>Greedy line breaking at spaces; a word wider than the line is broken by characters.</summary>
    internal List<TextLine> Wrap(IReadOnlyList<TextRun> runs, double width, double size, RunStyle baseStyle)
    {
        var lines = new List<TextLine>();
        var current = new List<Piece>();
        var currentWidth = 0.0;
        Piece? pendingSpace = null;
        var word = new List<Piece>();

        void FinishLine(bool hardBreak = false)
        {
            lines.Add(new TextLine(current, current.Sum(p => p.Width)) { EndsWithBreak = hardBreak });
            current = [];
            currentWidth = 0;
            pendingSpace = null;
        }

        void FlushWord()
        {
            if (word.Count == 0)
            {
                return;
            }

            var wordWidth = word.Sum(p => p.Width);
            var spaceWidth = current.Count > 0 && pendingSpace is not null ? pendingSpace.Width : 0;
            if (current.Count > 0 && currentWidth + spaceWidth + wordWidth > width)
            {
                FinishLine();
                spaceWidth = 0;
            }

            if (current.Count > 0 && pendingSpace is not null)
            {
                current.Add(pendingSpace);
                currentWidth += spaceWidth;
            }

            pendingSpace = null;
            if (wordWidth <= width - currentWidth)
            {
                current.AddRange(word);
                currentWidth += wordWidth;
            }
            else
            {
                // Too wide even for an empty line: fill lines character by character, never inside a text element.
                foreach (var piece in word)
                {
                    foreach (var character in TextElements(piece.Text))
                    {
                        var part = piece with { Text = character, Width = this.metrics.Width(character, piece.Face, piece.Size) };
                        if (current.Count > 0 && currentWidth + part.Width > width)
                        {
                            FinishLine();
                        }

                        current.Add(part);
                        currentWidth += part.Width;
                    }
                }
            }

            word.Clear();
        }

        foreach (var piece in this.Pieces(runs, size, baseStyle))
        {
            if (piece.IsBreak)
            {
                FlushWord();
                FinishLine(hardBreak: true);
            }
            else if (piece.IsSpace && word.Count > 0 && word[^1].Icon is not null)
            {
                // An icon is never broken from the word after it.
                word.Add(piece with { IsSpace = false });
            }
            else if (piece.IsSpace)
            {
                FlushWord();
                pendingSpace ??= piece;
            }
            else
            {
                word.Add(piece);
            }
        }

        FlushWord();
        if (current.Count > 0)
        {
            FinishLine();
        }

        return lines;
    }

    // The runs cut at spaces and breaks into measured pieces: words, single spaces and breaks.
    private List<Piece> Pieces(IReadOnlyList<TextRun> runs, double size, RunStyle baseStyle)
    {
        var pieces = new List<Piece>();
        foreach (var run in runs)
        {
            if (run.IsBreak)
            {
                pieces.Add(new Piece(string.Empty, RunStyle.None, null, FontFace.Regular, size, 0, IsSpace: false, IsBreak: true));
                continue;
            }

            var style = run.Style | baseStyle;
            var face = Face(style);
            var pieceSize = (style.HasFlag(RunStyle.Code) ? size * 0.9 : size) * (style.HasFlag(RunStyle.Superscript) ? SuperscriptScale : 1);
            if (run.Icon is { } icon)
            {
                pieces.Add(new Piece(string.Empty, style, run.Link, face, pieceSize, pieceSize * (1 + (2 * ThinSpace)), IsSpace: false, IsBreak: false) { Colour = run.Colour, Icon = icon.Drawing });
                continue;
            }

            var text = Visible(run.Text);
            var start = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] != ' ')
                {
                    continue;
                }

                if (i > start)
                {
                    var word = text[start..i];
                    this.CheckGlyphs(word, face);
                    pieces.Add(new Piece(word, style, run.Link, face, pieceSize, this.metrics.Width(word, face, pieceSize), IsSpace: false, IsBreak: false) { Colour = run.Colour, Footnote = run.Footnote });
                }

                if (i < text.Length)
                {
                    pieces.Add(new Piece(" ", style, run.Link, face, pieceSize, this.metrics.Width(" ", face, pieceSize), IsSpace: true, IsBreak: false) { Colour = run.Colour });
                }

                start = i + 1;
            }
        }

        return pieces;
    }

    // The footnotes a line refers to that are on no page yet, in order.
    private List<int> NewNotes(TextLine line)
    {
        if (this.footnotes.Count == 0)
        {
            return [];
        }

        var numbers = new List<int>();
        foreach (var piece in line.Pieces)
        {
            if (piece.Footnote is { } number && this.footnotes.ContainsKey(number) && !this.placedNotes.Contains(number) && !numbers.Contains(number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private bool HasNewNotes(List<TextLine> lines, int from) =>
        this.footnotes.Count > 0 && lines.Skip(from).Any(line => this.NewNotes(line).Count > 0);

    // How many lines from `from` fit on this page, each with the footnotes it brings.
    private int LinesThatFit(List<TextLine> lines, int from, double lineHeight)
    {
        var numbers = new List<int>();
        var fit = 0;
        while (from + fit < lines.Count)
        {
            numbers.AddRange(this.NewNotes(lines[from + fit]).Where(n => !numbers.Contains(n)));
            if (this.y + ((fit + 1) * lineHeight) + this.NotesExtra(numbers) > this.bottom + 1e-6)
            {
                break;
            }

            fit++;
        }

        return fit;
    }

    // How much higher the footnotes of this page get with these notes, the rule above the first one included.
    private double NotesExtra(List<int>? numbers)
    {
        if (numbers is null || numbers.Count == 0)
        {
            return 0;
        }

        var added = numbers.Where(n => !this.placedNotes.Contains(n)).Distinct().Select(n => this.Reserve(n, 0)).ToList();
        return added.Count == 0 ? 0 : this.NotesHeight([.. this.pageNotes, .. added]) - this.NotesHeight(this.pageNotes);
    }

    // Puts the notes on the current page, and marks their references here (at the top of the line about to be drawn).
    private void PlaceNotes(List<int> numbers)
    {
        foreach (var number in numbers)
        {
            if (this.placedNotes.Add(number))
            {
                this.pageNotes.Add(this.Reserve(number, 0));
                this.anchors.TryAdd(ReferenceAnchor(number), (this.pages.Count - 1, this.y));
            }
        }

        if (numbers.Count > 0)
        {
            this.bottom = this.geometry.Bottom - this.NotesHeight(this.pageNotes);
        }
    }

    // A note's lines from `from` on, as the room it asks on a page: all of them, or for a note taller than a third of the
    // page its first lines only, which grow into the room the page leaves (FinishPage).
    private NotePart Reserve(int number, int from)
    {
        var rest = this.NoteLines(number).Count - from;
        var tall = rest * NoteSize * LineFactor > (this.geometry.Bottom - this.geometry.Top) / 3;
        return new NotePart(number, from, tall ? Math.Min(NoteMinLines, rest) : rest);
    }

    private double NotesHeight(IReadOnlyList<NotePart> parts) =>
        parts.Count == 0 ? 0 : NoteSeparator + (parts.Sum(p => p.Count) * NoteSize * LineFactor) + ((parts.Count - 1) * NoteGap);

    // Wrapped once, across the text width after the number; the last line of each of its paragraphs marked as one,
    // so it stays left-aligned when justified. A note without text gets one empty line.
    private List<TextLine> NoteLines(int number)
    {
        if (!this.noteLines.TryGetValue(number, out var lines))
        {
            lines = [];
            foreach (var paragraph in this.footnotes[number].Paragraphs)
            {
                var wrapped = this.Wrap(this.Supported(paragraph, RunStyle.None), this.geometry.ContentWidth - NoteIndent, NoteSize, RunStyle.None);
                if (wrapped.Count > 0)
                {
                    wrapped[^1] = wrapped[^1] with { EndsWithBreak = true };
                }

                lines.AddRange(wrapped);
            }

            if (lines.Count == 0)
            {
                lines.Add(new TextLine([], 0) { EndsWithBreak = true });
            }

            this.noteLines[number] = lines;
        }

        return lines;
    }

    // Draws the footnotes of the finished page at its foot, under a short rule: each with its number raised before it,
    // linking back to its reference; the rest of a split note goes on to the next page. A note that may split first takes
    // the room the page left above its notes.
    private void FinishPage()
    {
        if (this.page is null || this.pageNotes.Count == 0)
        {
            return;
        }

        var lineHeight = NoteSize * LineFactor;
        var free = this.bottom - this.y;
        for (var i = 0; i < this.pageNotes.Count; i++)
        {
            var part = this.pageNotes[i];
            var total = this.NoteLines(part.Number).Count;
            var grow = Math.Max(0, Math.Min(total - part.From - part.Count, (int)Math.Floor((free + 1e-6) / lineHeight)));
            this.pageNotes[i] = part with { Count = part.Count + grow };
            free -= grow * lineHeight;
        }

        var (y, page) = (this.y, this.pages.Count - 1);
        var left = this.geometry.Left;
        var top = this.geometry.Bottom - this.NotesHeight(this.pageNotes);
        this.Page.Items.Add(new LineItem(left, top + (NoteSeparator / 2), left + (this.geometry.ContentWidth / 3), top + (NoteSeparator / 2), 0.5, this.TextColor));
        var frame = new Frame(left + NoteIndent, this.geometry.ContentWidth - NoteIndent, this.TextColor, []);
        this.y = top + NoteSeparator;
        for (var p = 0; p < this.pageNotes.Count; p++)
        {
            var part = this.pageNotes[p];
            if (p > 0)
            {
                this.y += NoteGap;
            }

            var lines = this.NoteLines(part.Number);
            if (part.From == 0)
            {
                var number = part.Number.ToString(CultureInfo.InvariantCulture);
                var size = NoteSize * SuperscriptScale;
                var width = this.metrics.Width(number, FontFace.Regular, size);
                var x = frame.Left - 3 - width;
                this.Page.Items.Add(new TextItem(x, this.Baseline(NoteSize) - (NoteSize * SuperscriptRise), number, FontFace.Regular, size, this.TextColor));
                this.Page.Items.Add(new LinkItem(x, this.y, width, lineHeight, "#" + ReferenceAnchor(part.Number)));
                this.anchors.TryAdd(NoteAnchor(part.Number), (page, this.y));
            }

            for (var i = part.From; i < part.From + part.Count; i++)
            {
                this.Line(lines[i], frame, frame.Left, NoteSize, this.TextColor, advance: false, justify: this.design.Justify && !lines[i].EndsWithBreak ? frame.Width : null);
                this.y += lineHeight;
            }

            if (part.From + part.Count < lines.Count)
            {
                this.carriedNotes.Add(this.Reserve(part.Number, part.From + part.Count));
                if (this.splitNotes.Add(part.Number))
                {
                    this.notes.Add(
                        NoteCodes.FootnoteSplit,
                        this.footnotes[part.Number].Line,
                        $"The footnote {part.Number} is too long for the foot of its page and continues at the foot of the next. Shorten it, or move the detail into the text.");
                }
            }
        }

        this.y = y;
        this.pageNotes.Clear();
    }

    // The anchors a reference and its note link to; ':' is in no heading's anchor.
    private static string NoteAnchor(int number) => string.Create(CultureInfo.InvariantCulture, $"footnote:{number}");

    private static string ReferenceAnchor(int number) => string.Create(CultureInfo.InvariantCulture, $"footnote-reference:{number}");

    // A line of code spans broken like a string: by characters, each piece keeping its span's kind; a break inside a
    // span splits it. One span per line in, one span per line out.
    private List<IReadOnlyList<CodeSpan>> BreakByCharacters(IReadOnlyList<CodeSpan> line, FontFace face, double size, double width)
    {
        var result = new List<IReadOnlyList<CodeSpan>>();
        var current = new List<CodeSpan>();
        var used = 0.0;
        var empty = true;
        foreach (var span in line)
        {
            this.CheckGlyphs(span.Text, face);
            var text = new StringBuilder();
            foreach (var element in TextElements(span.Text))
            {
                var w = this.metrics.Width(element, face, size);
                if (!empty && used + w > width)
                {
                    if (text.Length > 0)
                    {
                        current.Add(span with { Text = text.ToString() });
                        text.Clear();
                    }

                    result.Add(current);
                    current = [];
                    used = 0;
                    empty = true;
                }

                text.Append(element);
                used += w;
                empty = false;
            }

            if (text.Length > 0 || line.Count == 1)
            {
                current.Add(span with { Text = text.ToString() });
            }
        }

        result.Add(current);
        return result;
    }

    private List<string> BreakByCharacters(string line, FontFace face, double size, double width)
    {
        this.CheckGlyphs(line, face);
        var result = new List<string>();
        var current = new StringBuilder();
        var used = 0.0;
        foreach (var element in TextElements(line))
        {
            var w = this.metrics.Width(element, face, size);
            if (current.Length > 0 && used + w > width)
            {
                result.Add(current.ToString());
                current.Clear();
                used = 0;
            }

            current.Append(element);
            used += w;
        }

        result.Add(current.ToString());
        return result;
    }

    // The runs with what their face lacks replaced; control characters are dropped first, as the layout drops them.
    private List<TextRun> Supported(IReadOnlyList<TextRun> runs, RunStyle baseStyle) =>
        [.. runs.Select(run => run.IsBreak ? run : run with { Text = this.Supported(Visible(run.Text), Face(run.Style | baseStyle)) })];

    // The text with each text element the face lacks a glyph for replaced through the caller's hook. Throws without
    // a hook, or when a replacement lacks a glyph too.
    private string Supported(string text, FontFace face)
    {
        if (this.replaceUnsupported is not { } replace)
        {
            this.CheckGlyphs(text, face);
            return text;
        }

        StringBuilder? result = null;
        var index = 0;
        foreach (var element in TextElements(text))
        {
            if (this.Lacks(element, face))
            {
                var replacement = replace(element);
                this.CheckGlyphs(replacement, face, element);
                this.notes.Add(
                    NoteCodes.CharacterReplaced,
                    0,
                    $"The character '{element}' ({CodePoints(element)}) is not in the fonts and was replaced by '{replacement}'.");
                result ??= new StringBuilder(text, 0, index, text.Length);
                result.Append(replacement);
            }
            else
            {
                result?.Append(element);
            }

            index += element.Length;
        }

        return result?.ToString() ?? text;
    }

    private static string CodePoints(string element) =>
        string.Join(" ", element.EnumerateRunes().Select(r => $"U+{r.Value:X4}"));

    private void CheckGlyphs(string text, FontFace face, string? replacing = null)
    {
        foreach (var element in TextElements(text))
        {
            if (this.Lacks(element, face))
            {
                var codes = string.Join(' ', element.EnumerateRunes().Select(r => string.Create(CultureInfo.InvariantCulture, $"U+{r.Value:X4}")));
                throw new DocumentConversionException(
                    DocumentFormat.Pdf,
                    replacing is null
                        ? $"The character '{element}' ({codes}) is not in the font ({face})."
                        : $"The replacement '{text}' for '{replacing}' has the character '{element}' ({codes}), which is not in the font ({face}) either.");
            }
        }
    }

    private bool Lacks(string element, FontFace face) => element.EnumerateRunes().Any(r => !this.metrics.HasGlyph(r, face));

    // Grapheme clusters: a letter with its combining marks, an emoji with its modifiers, selectors and joiners.
    private static IEnumerable<string> TextElements(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            yield return elements.GetTextElement();
        }
    }

    private static string Visible(string text) => new([.. text.Where(c => !char.IsControl(c))]);

    private static FontFace Face(RunStyle style) => style.HasFlag(RunStyle.Code)
        ? style.HasFlag(RunStyle.Bold) ? FontFace.MonoBold : FontFace.Mono
        : (style.HasFlag(RunStyle.Table), style.HasFlag(RunStyle.Bold), style.HasFlag(RunStyle.Italic)) switch
        {
            (false, true, true) => FontFace.BoldItalic,
            (false, true, false) => FontFace.Bold,
            (false, false, true) => FontFace.Italic,
            (false, false, false) => FontFace.Regular,
            (true, true, true) => FontFace.TableBoldItalic,
            (true, true, false) => FontFace.TableBold,
            (true, false, true) => FontFace.TableItalic,
            (true, false, false) => FontFace.TableRegular,
        };

    /// <summary>
    /// An entry of the contents waiting for its page number: its page, the baseline and end of its title's last line, the
    /// anchor of its heading, and the face of its number.
    /// </summary>
    private sealed record ContentsSlot(int Page, double Baseline, double End, string Anchor, FontFace Face);

    /// <summary>Lines [From, From + Count) of a footnote, at the foot of one page.</summary>
    private sealed record NotePart(int Number, int From, int Count);

    /// <summary>A table with its text replaced, its column widths, its wrapped cells, and the height its start needs.</summary>
    private sealed record PreparedTable(double[] Widths, List<List<List<TextLine>>> Cells, double KeepHeight);

    /// <summary>The column a block flows in, its text colour, and the x of the rule of each enclosing quote.</summary>
    private readonly record struct Frame(double Left, double Width, string Color, IReadOnlyList<double> QuoteRules);
}

/// <summary>A measured piece of a line: a word, a space or a break.</summary>
internal sealed record Piece(string Text, RunStyle Style, string? Link, FontFace Face, double Size, double Width, bool IsSpace, bool IsBreak)
{
    /// <summary>The status colour of <c>[words]{.success}</c>.</summary>
    public LabelCategory? Colour { get; init; }

    /// <summary>An icon, one em and a thin space each side wide, in place of text.</summary>
    public Drawing? Icon { get; init; }

    /// <summary>A footnote reference: the number of its note.</summary>
    public int? Footnote { get; init; }
}

/// <summary>A wrapped line; <see cref="EndsWithBreak"/>: a hard break, or the end of a footnote's paragraph, follows it.</summary>
internal sealed record TextLine(List<Piece> Pieces, double Width)
{
    public bool EndsWithBreak { get; init; }
}
