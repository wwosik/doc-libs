using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using MarkdownDocument = wwosik.DocLibs.DocumentConversions.MarkdownDocument;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Turns Markdown into slides: the cover from the first <c>#</c>, a section divider per later <c>#</c>, one slide per
/// <c>##</c>, and a slide per table or image. A <c>##</c> section that uses layouts becomes one <see cref="FlowSlide"/>.
/// </summary>
internal sealed partial class DeckParser(
    MarkdownDocument document,
    PptxOptions options,
    Brand brand,
    int lineOffset,
    GenerationNotes notes,
    GeneratedTexts texts,
    bool numberChapters,
    Func<string, IReadOnlyList<SlotKind>?>? layoutSlots)
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    private readonly List<Slide> slides = [];

    // The footnotes referenced in the current section, numbered from 1 in the order of their first reference.
    private readonly Dictionary<Footnote, int> footnotes = [];
    private bool hasCover;

    // The number of the latest section divider, when chapters are numbered.
    private int chapters;

    // Whether the section being walked fills a layout with picture placeholders, where every image is cropped.
    private bool pictureSlots;

    // The build marker of the block being walked, and that block, while it is walked; see WalkMarked.
    private BuildMark? build;
    private Block? marked;

    // Whether the blocks being walked are on a dark background: a `.dark` slide or a `::: panel dark`, for drawn containers.
    private bool onDark;

    /// <remarks>
    /// <paramref name="brand"/> is the brand code-block renderers draw in; <paramref name="lineOffset"/> the lines of
    /// front matter cut off the Markdown, so renderers get the block's line in the original; <paramref name="notes"/>
    /// collects what the writer should know, such as HTML shown as text; <paramref name="texts"/> are the texts of the
    /// document language, for code-block renderers; with <paramref name="numberChapters"/> every section divider gets
    /// its number; <paramref name="layoutSlots"/> gives the placeholders a layout named by <c>layout="…"</c> offers
    /// (<see langword="null"/>: unknown), which decides whether a section fills them.
    /// </remarks>
    public static IReadOnlyList<Slide> Parse(
        MarkdownDocument document,
        PptxOptions options,
        Brand? brand = null,
        int lineOffset = 0,
        GenerationNotes? notes = null,
        GeneratedTexts? texts = null,
        bool numberChapters = false,
        Func<string, IReadOnlyList<SlotKind>?>? layoutSlots = null)
    {
        var parser = new DeckParser(
            document, options, brand ?? Brand.Neutral, lineOffset, notes ?? new GenerationNotes(), texts ?? GeneratedTexts.English, numberChapters, layoutSlots);
        HeadingBlock? heading = null;
        var blocks = new List<Block>();
        var markdown = Markdown.Parse(document.Markdown, Pipeline);

        // Slides have no pages to break: a page break's content stays where it is, the break itself is ignored.
        PageBreaks.Flatten(markdown, Format.Pptx);
        MarkdownSyntax.ReportMissingFootnotes(markdown, parser.LineOf, parser.Notes);
        BuildMarker.Check(markdown, Format.Pptx, parser.LineOf);
        foreach (var block in markdown)
        {
            if (block is FootnoteGroup)
            {
                continue;
            }

            if (block is HeadingBlock { Level: <= 2 } next)
            {
                parser.Section(heading, blocks);
                heading = next;
                blocks = [];
            }
            else
            {
                blocks.Add(block);
            }
        }

        parser.Section(heading, blocks);
        parser.FillAgendas();
        return parser.slides;
    }

    // An empty `{.agenda}` lists the deck's chapters, the section dividers before and after it, with their numbers when
    // chapters are numbered; without chapters it keeps its title only, with a note.
    private void FillAgendas()
    {
        var chapters = this.slides.OfType<TitleSlide>().Where(t => t.IsSection).ToList();
        for (var i = 0; i < this.slides.Count; i++)
        {
            if (this.slides[i] is not AgendaSlide { FromChapters: true } agenda)
            {
                continue;
            }

            if (chapters.Count == 0)
            {
                notes.Add(
                    NoteCodes.AgendaEmpty,
                    agenda.Line,
                    $"The agenda '{PlainText(agenda.Title)}' has no list and the deck no chapters ('#' headings after the cover) to list, so it shows its title only. Add the chapters, or write the agenda as a list.");
                continue;
            }

            this.slides[i] = agenda with
            {
                Items = [.. chapters.Select(c => c.Number is { } number
                    ? new TextParagraph([new TextRun(number.ToString("00", CultureInfo.InvariantCulture), RunStyle.Bold), new TextRun("  ", RunStyle.None), .. c.Title.Where(r => !r.IsBreak)])
                    : new TextParagraph([.. c.Title.Where(r => !r.IsBreak)], ParagraphKind.Numbered))],
            };
        }
    }

    // Custom containers without their inline form (`::text::`), which would take `std::vector::size` apart.
    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
            .UseAutoLinks()
            .UseAlertBlocks()
            .UseDocumentSyntax();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    // `::: notes` at the top level of a section is taken out before the kind of slide is decided, so speaker notes
    // never turn a text slide into a flow slide; they go on every slide the section produces.
    private void Section(HeadingBlock? heading, List<Block> blocks)
    {
        var speakerNotes = blocks.OfType<CustomContainer>().Where(c => c.Info == LayoutSyntax.Notes).ToList();
        if (heading is null)
        {
            foreach (var dropped in speakerNotes)
            {
                notes.Add(
                    NoteCodes.NotesDropped,
                    this.LineOf(dropped),
                    "The speaker notes before the first heading belong to no slide and were left out. Move them under the heading of their slide.");
            }

            return;
        }

        foreach (var block in blocks.Where(b => !speakerNotes.Contains(b)))
        {
            LayoutSyntax.CheckNoNestedNotes(block, Format.Pptx);
        }

        SlideKinds.CheckContainers(blocks, Format.Pptx);

        // Every slide of the section carries its heading's line, for the notes about it, and the footnotes it references.
        var first = this.slides.Count;
        this.footnotes.Clear();
        var transition = this.Slides(heading, speakerNotes.Count == 0 ? blocks : [.. blocks.Where(b => !speakerNotes.Contains(b))]);
        var footnotes = this.footnotes.OrderBy(f => f.Value).Select(f => new SlideFootnote(f.Value, this.FootnoteText(f.Key))).ToList();
        var paragraphs = new List<TextParagraph>();
        foreach (var container in speakerNotes)
        {
            LayoutSyntax.CheckContainer(LayoutSyntax.Notes, container.Arguments?.Trim() ?? string.Empty, Format.Pptx);
            this.SpeakerNotes(container, paragraphs, 0);
        }

        for (var i = first; i < this.slides.Count; i++)
        {
            var references = this.slides[i].References();
            this.slides[i] = this.slides[i] with
            {
                Line = this.LineOf(heading),
                Footnotes = [.. footnotes.Where(f => references.Contains(f.Number))],
                SpeakerNotes = paragraphs,
                Transition = transition ?? options.Transition,
            };
        }

        // Icons only a card's figure or an icon list draws; anywhere else on a slide they are left out.
        for (var i = first; i < this.slides.Count; i++)
        {
            var slide = this.slides[i];
            foreach (var icon in slide.AllRuns().Concat(slide.SpeakerNotes.SelectMany(p => p.Runs)).Concat(slide.Footnotes.SelectMany(f => f.Text))
                .Select(r => r.Icon).OfType<InlineIcon>())
            {
                notes.Add(
                    NoteCodes.IconNotDrawn,
                    icon.Line,
                    $"The icon ':icon-{icon.Name}:' is left out: a slide cannot draw an icon inside text, a heading or a table cell. Put it alone in a card's first paragraph, or at the start of every item of a list.");
            }
        }
    }

    // Paragraphs and lists keep their form; every other block is written as plain text, and reported.
    private void SpeakerNotes(IEnumerable<Block> blocks, List<TextParagraph> into, int level)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case CustomContainer { Info: LayoutSyntax.Notes }:
                    throw LayoutSyntax.NestedNotes(Format.Pptx);
                case ParagraphBlock paragraph:
                    this.NotesParagraph(paragraph, into, ParagraphKind.Plain, level, 1);
                    break;
                case ListBlock list:
                    this.NotesList(list, into, level);
                    break;
                case HtmlBlock { Type: HtmlBlockType.Comment }:
                case ThematicBreakBlock:
                case LinkReferenceDefinitionGroup:
                    break;
                default:
                    notes.Add(
                        NoteCodes.NotesSimplified,
                        this.LineOf(block),
                        $"The speaker notes hold {Kind(block)}, which is written as plain text. Keep speaker notes to paragraphs and lists.");
                    this.NotesText(block, into, level);
                    break;
            }
        }
    }

    private static string Kind(Block block) => block switch
    {
        Markdig.Extensions.Tables.Table => "a table",
        FencedCodeBlock or CodeBlock => "a code block",
        AlertBlock => "an alert",
        QuoteBlock => "a quote",
        HeadingBlock => "a heading",
        HtmlBlock => "HTML",
        CustomContainer container => $"a '::: {container.Info}'",
        _ => "a block other than a paragraph or list",
    };

    // A paragraph with its styles and links; an image in it becomes its alt text.
    private void NotesParagraph(ParagraphBlock paragraph, List<TextParagraph> into, ParagraphKind kind, int level, int start)
    {
        if (paragraph.Inline?.Descendants<LinkInline>().Any(l => l.IsImage) ?? false)
        {
            notes.Add(
                NoteCodes.NotesSimplified,
                this.LineOf(paragraph),
                "The speaker notes hold an image, which is written as its alt text. Keep speaker notes to paragraphs and lists.");
        }

        var runs = this.Runs(paragraph.Inline, imagesAsText: true);
        if (runs.Count > 0 || kind != ParagraphKind.Plain)
        {
            into.Add(new TextParagraph(runs, kind, level, start));
        }
    }

    private void NotesList(ListBlock list, List<TextParagraph> into, int level)
    {
        var start = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var kind = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList task } ? task.Checked ? ParagraphKind.TaskDone : ParagraphKind.Task
                : list.IsOrdered ? ParagraphKind.Numbered : ParagraphKind.Bullet;
            var marked = false;
            foreach (var child in item)
            {
                if (!marked && child is ParagraphBlock paragraph)
                {
                    this.NotesParagraph(paragraph, into, kind, level, start);
                    marked = true;
                }
                else if (child is ListBlock nested)
                {
                    this.NotesList(nested, into, level + 1);
                }
                else
                {
                    this.SpeakerNotes([child], into, level);
                }
            }

            if (!marked)
            {
                into.Add(new TextParagraph([], kind, level, start));
            }
        }
    }

    // A block's text as plain paragraphs: a table row by row, its cells separated by " | "; code and HTML line by line.
    private void NotesText(Block block, List<TextParagraph> into, int level)
    {
        switch (block)
        {
            case Table table:
                foreach (var row in table.OfType<TableRow>())
                {
                    var runs = new List<TextRun>();
                    foreach (var cell in row.OfType<TableCell>())
                    {
                        if (runs.Count > 0)
                        {
                            runs.Add(new TextRun(" | ", RunStyle.None));
                        }

                        runs.AddRange(this.Cell(cell, labels: false).Select(r => r.IsBreak ? new TextRun(" ", RunStyle.None) : r));
                    }

                    into.Add(new TextParagraph(runs, Level: level));
                }

                break;
            case CodeBlock or HtmlBlock:
                foreach (var line in ((LeafBlock)block).Lines.ToString().Split('\n').Where(l => l.Trim().Length > 0))
                {
                    into.Add(new TextParagraph([new TextRun(line.TrimEnd(), RunStyle.None)], Level: level));
                }

                break;
            case LeafBlock { Inline: { } inline }:
                if (this.Runs(inline, imagesAsText: true) is { Count: > 0 } text)
                {
                    into.Add(new TextParagraph(text, Level: level));
                }

                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    switch (child)
                    {
                        case CustomContainer { Info: LayoutSyntax.Notes }:
                            throw LayoutSyntax.NestedNotes(Format.Pptx);
                        case ParagraphBlock paragraph:
                            this.NotesParagraph(paragraph, into, ParagraphKind.Plain, level, 1);
                            break;
                        case ListBlock list:
                            this.NotesList(list, into, level);
                            break;
                        default:
                            this.NotesText(child, into, level);
                            break;
                    }
                }

                break;
        }
    }

    private GenerationNotes Notes => notes;

    private int FootnoteNumber(Footnote footnote)
    {
        if (!this.footnotes.TryGetValue(footnote, out var number))
        {
            number = this.footnotes.Count + 1;
            this.footnotes[footnote] = number;
        }

        return number;
    }

    // A footnote's paragraphs, one after the other; its other blocks as their text.
    private List<TextRun> FootnoteText(Footnote footnote)
    {
        var runs = new List<TextRun>();
        foreach (var leaf in footnote.Descendants<LeafBlock>().Where(l => l.Inline is not null))
        {
            if (runs.Count > 0)
            {
                runs.Add(new TextRun(" ", RunStyle.None));
            }

            runs.AddRange(this.Runs(leaf.Inline, imagesAsText: true));
        }

        return runs;
    }

    // A block's line in the original Markdown, from 1.
    private int LineOf(MarkdownObject block) => block.Line + 1 + lineOffset;

    private void HtmlAsText(MarkdownObject html, string text) => notes.Add(
        NoteCodes.HtmlAsText,
        this.LineOf(html),
        $"The HTML '{GenerationNotes.Quote(text)}' is shown as text. Write Markdown instead.");

    // Returns the transition the heading asks for, if any.
    private SlideTransition? Slides(HeadingBlock heading, List<Block> blocks)
    {
        var title = this.Runs(heading.Inline, imagesAsText: true);
        var isTitle = heading.Level == 1;
        var attributes = Attributes(title);
        if (isTitle && attributes is { Dark: true } or { Kicker: not null })
        {
            throw Error("A '#' heading takes only the attributes layout=\"…\" and .closing; .dark and kicker=\"…\" go on '##' headings.");
        }

        if (!isTitle && attributes is { Closing: true })
        {
            throw Error("{.closing} goes on a '#' heading: the closing slide.");
        }

        if (attributes?.Kind is { } kind)
        {
            if (isTitle)
            {
                throw Error($"{{.{kind}}} goes on a '##' heading: a '#' heading is the cover or a section divider.");
            }

            SlideKinds.Check(kind, blocks, PlainText(title), Format.Pptx);
            this.slides.Add(this.KindSlide(kind, title, blocks, attributes));
            return Transition(attributes);
        }

        // `layout="…"` with `---` fills the layout's placeholders, which have no room for flow blocks.
        var split = !isTitle && attributes?.Layout is not null && blocks.Any(b => b is ThematicBreakBlock);
        if (split && (attributes is { Dark: true } or { Kicker: not null } || blocks.Any(this.UsesLayouts)))
        {
            throw Error(
                $"layout=\"{attributes!.Layout}\" with '---' takes text, images, charts, tables and diagrams (the slide '{PlainText(title)}' at line {this.LineOf(heading)}); "
                + "use a flow slide, without '---', for containers, alerts, .dark and kicker=\"…\".");
        }

        // `layout="…"` alone keeps a plain slide plain, on the layout asked for, unless it fills the layout's placeholders.
        if (attributes is not ({ Dark: true } or { Kicker: not null }) && (isTitle || !blocks.Any(this.UsesLayouts)))
        {
            var closing = attributes?.Closing ?? false;
            var isSection = isTitle && this.hasCover && !closing;
            var section = new SectionBuilder(this, this.slides, title, isTitle, isSection, attributes?.Layout, closing, isSection && numberChapters ? ++this.chapters : null);
            this.hasCover |= isTitle && !closing;
            if (!isTitle && attributes?.Layout is { } layout
                && (split || (layoutSlots?.Invoke(layout) is { } slots && slots.Any(k => k is SlotKind.Picture or SlotKind.Chart))))
            {
                this.Placeholders(title, blocks, layout, split, section);
            }
            else
            {
                this.WalkMarked(blocks, section, section: true);
            }

            section.Finish();
            return Transition(attributes);
        }

        var flow = new FlowCollector(this, inContainer: false);
        this.onDark = attributes?.Dark ?? false;
        this.WalkMarked(blocks, flow, section: true);
        this.onDark = false;
        this.slides.Add(new FlowSlide(title, attributes?.Kicker, attributes?.Dark ?? false, flow.Finish()) { Layout = attributes?.Layout });
        return Transition(attributes);
    }

    // The section's text parts (cut at top-level `---`) and visuals into the placeholders of `layout`. Without `---`
    // only when each of them has a placeholder there and an image or chart is among them; otherwise it becomes the
    // slides it always did, through `section`.
    private void Placeholders(List<TextRun> title, List<Block> blocks, string layout, bool split, SectionBuilder section)
    {
        var slots = layoutSlots?.Invoke(layout);
        var collector = new PlaceholderCollector(this);
        this.pictureSlots = slots?.Contains(SlotKind.Picture) ?? false;
        var part = new List<Block>();
        foreach (var block in blocks.Append(new ThematicBreakBlock(null!)))
        {
            if (block is ThematicBreakBlock)
            {
                this.WalkMarked(part, collector, section: true);
                collector.NextPart();
                part = [];
            }
            else
            {
                part.Add(block);
            }
        }

        this.pictureSlots = false;
        var items = collector.Items();
        var unplaceable = slots is null ? null : PlaceholderFill.Unplaceable(items, slots);
        if (split && unplaceable is not null)
        {
            throw Error(
                $"The layout '{layout}' has no placeholder for {unplaceable.Kind} (the slide '{PlainText(title)}'). "
                + "Choose a layout that has one, or move it to a slide of its own.");
        }

        if (split || (slots is not null && unplaceable is null && items.Any(i => i.Visual is { Image: not null } or { Chart: not null })))
        {
            section.Placeholders(new PlaceholderSlide(title, items) { Layout = layout });
            return;
        }

        collector.Replay(section);
    }

    // The slide of a kind, from blocks checked by SlideKinds.Check. A chart slide whose charts no renderer drew is the
    // flow slide its blocks make.
    private Slide KindSlide(string kind, List<TextRun> title, List<Block> blocks, HeadingAttributes attributes)
    {
        var content = SlideKinds.Content(blocks);
        KindSlide slide;
        switch (kind)
        {
            case SlideKinds.Agenda:
                slide = content.Count == 0
                    ? new AgendaSlide(title, []) { FromChapters = true }
                    : new AgendaSlide(title, [.. this.Collect(blocks).Paragraphs.Select(p => p is { Level: 0, Kind: ParagraphKind.Bullet } ? p with { Kind = ParagraphKind.Numbered, NumberStart = 1 } : p)]);
                break;
            case SlideKinds.Quote:
                slide = this.Quote(title, content);
                break;
            case SlideKinds.Statement:
                slide = new StatementSlide(title, this.Collect(blocks).Paragraphs);
                break;
            case SlideKinds.Contact:
                slide = new ContactSlide(title, this.People(blocks));
                break;
            default:
                var collected = this.Collect(blocks);
                if (collected.Charts.Count == 0 || collected.Other)
                {
                    var flow = new FlowCollector(this, inContainer: false);
                    this.WalkMarked(blocks, flow, section: true);
                    return new FlowSlide(title, attributes.Kicker, false, flow.Finish()) { Layout = attributes.Layout };
                }

                slide = new ChartSlide(title, collected.Charts, collected.Paragraphs);
                break;
        }

        return slide with { Layout = attributes.Layout, Kicker = attributes.Kicker };
    }

    private KindCollector Collect(IEnumerable<Block> blocks, RunStyle style = RunStyle.None)
    {
        var collector = new KindCollector(this);
        if (style == RunStyle.None)
        {
            this.WalkMarked(blocks, collector, section: true);
        }
        else
        {
            foreach (var block in blocks)
            {
                this.Walk(block, collector, 0, style);
            }
        }

        return collector;
    }

    // The quote in italics, and its byline: the paragraph after it, or the quote's last line, that starts with a dash.
    private QuoteSlide Quote(List<TextRun> title, List<Block> content)
    {
        var quote = (QuoteBlock)content[0];
        var children = quote.ToList();
        List<TextRun>? byline = null;
        TextParagraph? tail = null;
        if (content is [_, ParagraphBlock after])
        {
            byline = this.Runs(after.Inline, imagesAsText: true);
        }
        else if (children.LastOrDefault() is ParagraphBlock last)
        {
            var runs = this.LineRuns(last.Inline, RunStyle.Italic);
            var cut = runs.FindLastIndex(r => r.IsBreak || r.Text == SoftBreak);
            var line = Trim(runs[(cut + 1)..]);
            if (line is [{ IsBreak: false } first, ..] && SlideKinds.StartsWithDash(first.Text))
            {
                byline = [.. line.Select(r => r with { Style = r.Style & ~RunStyle.Italic })];
                children.RemoveAt(children.Count - 1);
                var body = Trim([.. runs[..Math.Max(cut, 0)].Select(r => r.Text == SoftBreak ? new TextRun(" ", r.Style) : r)]);
                tail = body.Count > 0 ? new TextParagraph(body) : null;
            }
        }

        if (byline is [var dash, ..])
        {
            byline[0] = dash with { Text = SlideKinds.WithoutDash(dash.Text) };
            byline = Trim(byline);
        }

        var paragraphs = this.Collect(children, RunStyle.Italic).Paragraphs;
        return new QuoteSlide(title, tail is null ? paragraphs : [.. paragraphs, tail], byline is { Count: > 0 } ? byline : null);
    }

    // Per item of the list: its first image as the photo; its lines (cut at line breaks) with the bold start of the first
    // as the name, else the first line; the rest of the item as further lines.
    private List<Person> People(List<Block> blocks)
    {
        var (list, mark) = this.Marked(blocks, section: true).Where(m => m.Block is ListBlock).Select(m => ((ListBlock)m.Block, m.Mark)).First();
        var people = new List<Person>();
        foreach (var (index, item) in list.OfType<ListItemBlock>().Index())
        {
            SlideImage? photo = null;
            var lines = new List<List<TextRun>>();
            var more = new List<TextParagraph>();
            foreach (var child in item)
            {
                if (child is not ParagraphBlock paragraph)
                {
                    more.AddRange(this.Collect([child]).Paragraphs.Select(p => p with { Kind = ParagraphKind.Plain, Level = 0 }));
                    continue;
                }

                // A photo is cropped into the contact layout's picture placeholder, so `focus` has its effect there.
                var collector = new RunCollector(this, imagesAsText: false, softBreaks: true);
                this.pictureSlots = true;
                collector.Add(paragraph.Inline, RunStyle.None, null);
                this.pictureSlots = false;
                photo ??= collector.Pieces.OfType<SlideImage>().FirstOrDefault();
                var runs = this.Icons(Colour(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList()), this.LineOf(paragraph.Inline));
                var line = new List<TextRun>();
                foreach (var run in runs.Append(TextRun.Break))
                {
                    if (run.IsBreak || run.Text == SoftBreak)
                    {
                        if (Trim(line) is { Count: > 0 } trimmed)
                        {
                            lines.Add(trimmed);
                        }

                        line = [];
                    }
                    else
                    {
                        line.Add(run);
                    }
                }
            }

            var name = new List<TextRun>();
            if (lines.Count > 0)
            {
                var bold = lines[0].TakeWhile(r => r.Style.HasFlag(RunStyle.Bold)).ToList();
                if (bold.Count > 0)
                {
                    name = Trim([.. bold.Select(r => r with { Style = r.Style & ~RunStyle.Bold })]);
                    var rest = lines[0].Skip(bold.Count).ToList();
                    if (rest.Count > 0 && rest[0].Style == RunStyle.None)
                    {
                        rest[0] = rest[0] with { Text = SeparatorPattern().Replace(rest[0].Text, string.Empty) };
                    }

                    lines[0] = Trim(rest);
                }
                else
                {
                    name = lines[0];
                    lines[0] = [];
                }
            }

            people.Add(new Person(name, [.. lines.Where(l => l.Count > 0).Select(l => new TextParagraph(l)), .. more], photo)
            {
                Build = mark is null ? null : mark with { Item = index },
            });
        }

        return people;
    }

    private List<TextRun> LineRuns(ContainerInline? inline, RunStyle style)
    {
        var collector = new RunCollector(this, imagesAsText: true, softBreaks: true);
        collector.Add(inline, style, null);
        return this.Icons(Colour(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList()), this.LineOf(inline));
    }

    private static string PlainText(IReadOnlyList<TextRun> runs) => string.Concat(runs.Select(r => r.IsBreak ? " " : r.Text)).Trim();

    private static SlideTransition? Transition(HeadingAttributes? attributes) =>
        attributes?.Transition is { } name ? Transitions.Parse(name) : null;

    // Takes the attributes off the end of the title, which is then shorter by them.
    private static HeadingAttributes? Attributes(List<TextRun> title)
    {
        if (title.Count == 0 || title[^1] is not { IsBreak: false, Style: RunStyle.None, Link: null } last)
        {
            return null;
        }

        var (text, attributes) = HeadingAttributes.Split(last.Text, Format.Pptx);
        if (attributes is not null)
        {
            title.RemoveAt(title.Count - 1);
            if (text.Length > 0)
            {
                title.Add(last with { Text = text });
            }
        }

        return attributes;
    }

    // Anything only a flow slide can show: containers and alerts anywhere, a tag list as a block of its own.
    // A definition list is shown as rows, and a list of icon items as an icon list, which only a flow slide has.
    private bool UsesLayouts(Block block) =>
        block is CustomContainer or AlertBlock or DefinitionList || (block is ParagraphBlock paragraph && Tags(paragraph) is not null)
        || (block is ListBlock list && this.IsIconList(list))
        || (block is ContainerBlock container && (container.Descendants<CustomContainer>().Any() || container.Descendants<AlertBlock>().Any()
            || container.Descendants<DefinitionList>().Any()));

    private CodeBlockGraphic? Render(FencedCodeBlock fenced) =>
        options.CodeBlockRenderers.Count == 0 ? null : CodeBlockRenderers.Render(
            options.CodeBlockRenderers,
            new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), brand)
            {
                Line = this.LineOf(fenced),
                Report = (code, message) => notes.Add(code, this.LineOf(fenced), message),
                Texts = texts,
            },
            Format.Pptx);

    private void Walk(Block block, ITarget target, int level, RunStyle style)
    {
        switch (block)
        {
            case CustomContainer container when target is FlowCollector flow:
                this.Container(container, flow);
                break;
            case DefinitionList definitions when target is FlowCollector flow:
                flow.Add(new RowsBlock(this.Definitions(definitions)));
                break;
            case AlertBlock alert when target is FlowCollector flow:
                flow.Add(new CalloutBlock(LayoutSyntax.IsImportant(alert.Kind.ToString(), Format.Pptx), this.Flow(alert)));
                break;
            case ParagraphBlock paragraph when target is FlowCollector flow && Tags(paragraph) is { } tags:
                flow.Add(new TagsBlock(tags));
                break;
            case ListBlock list when target is FlowCollector flow && this.IconList(list) is { } icons:
                flow.Add(new IconListBlock(icons));
                break;
            case ListBlock list when target is FlowCollector { InContainer: true } flow && this.LabelledTags(list) is { } items:
                flow.Add(new LabelledTagsBlock(items));
                break;
            case HeadingBlock heading:
                target.Text(new TextParagraph(this.Runs(heading.Inline, imagesAsText: true, style | RunStyle.Bold), Level: level) { Heading = true });
                break;
            case ParagraphBlock paragraph:
                this.Paragraph(paragraph.Inline, target, ParagraphKind.Plain, level, 1, style);
                break;
            case ListBlock list:
                this.List(list, target, level, style, items: this.build is not null && ReferenceEquals(list, this.marked));
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    this.Walk(child, target, level, style | RunStyle.Italic);
                }

                break;
            case Table table:
                target.Visual(this.Table(table, labels: target is FlowCollector), null);
                break;
            case FencedCodeBlock fenced when this.Render(fenced) is { } rendered:
                if (rendered is DrawingGraphic drawing)
                {
                    target.Visual(null, null, drawing.Drawing);
                }
                else if (rendered is ImageGraphic image)
                {
                    target.Visual(null, [this.Placeable(new SlideImage(image.Image, fenced.Info ?? string.Empty, 1.0))]);
                }
                else if (rendered is ChartGraphic chart)
                {
                    target.Visual(null, null, chart: chart.Chart);
                }

                break;
            case CodeBlock code when target is FlowCollector flow:
                flow.Add(new CodeLinesBlock(code.Lines.ToString().Split('\n')));
                break;
            case CodeBlock code:
                foreach (var line in code.Lines.ToString().Split('\n'))
                {
                    target.Text(new TextParagraph(line.Length > 0 ? [new TextRun(line, style | RunStyle.Code)] : [], Level: level));
                }

                break;
            case HtmlBlock { Type: HtmlBlockType.Comment }:
            case ThematicBreakBlock:
            case LinkReferenceDefinitionGroup:
                break;
            case HtmlBlock html:
                this.HtmlAsText(html, html.Lines.ToString());
                foreach (var line in html.Lines.ToString().Split('\n').Where(l => l.Trim().Length > 0))
                {
                    target.Text(new TextParagraph([new TextRun(line.Trim(), style)], Level: level));
                }

                break;
            case LeafBlock leaf when leaf.Inline is not null:
                this.Paragraph(leaf.Inline, target, ParagraphKind.Plain, level, 1, style);
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    this.Walk(child, target, level, style);
                }

                break;
        }
    }

    // A task list item (`- [ ]`, `- [x]`) has a box for its bullet, numbered or not. A list with a marker of its own
    // builds by its top-level `items`: each item's paragraphs, its sub-items included, carry the item's number.
    private void List(ListBlock list, ITarget target, int level, RunStyle style, bool items = false)
    {
        var start = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        var marker = this.build;
        foreach (var (index, item) in list.OfType<ListItemBlock>().Index())
        {
            if (items)
            {
                this.build = marker! with { Item = index };
            }

            var kind = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList task } ? task.Checked ? ParagraphKind.TaskDone : ParagraphKind.Task
                : list.IsOrdered ? ParagraphKind.Numbered : ParagraphKind.Bullet;
            var marked = false;
            foreach (var child in item)
            {
                if (!marked && child is ParagraphBlock paragraph)
                {
                    this.Paragraph(paragraph.Inline, target, kind, level, start, style);
                    marked = true;
                }
                else if (child is ListBlock nested)
                {
                    this.List(nested, target, level + 1, style);
                }
                else
                {
                    this.Walk(child, target, level, style);
                }
            }

            if (!marked)
            {
                target.Text(new TextParagraph([], kind, level, start));
            }
        }

        this.build = marker;
    }

    // The blocks with the build marker before each, if any; the marker paragraphs themselves are left out. With
    // `build: lists`, every list of a section without a marker gets `{.build}`. A mark is numbered by where its block
    // starts in the Markdown, which orders the clicks of a slide.
    private IEnumerable<(Block Block, BuildMark? Mark)> Marked(IEnumerable<Block> blocks, bool section)
    {
        IReadOnlyList<Build>? pending = null;
        foreach (var block in blocks)
        {
            if (block is ParagraphBlock paragraph && BuildMarker.TryParse(paragraph, Format.Pptx) is { } builds)
            {
                pending = builds;
                continue;
            }

            if (pending is null && section && options.Build == DeckBuild.Lists && block is ListBlock)
            {
                pending = BuildMarker.Lists;
            }

            yield return (block, pending is null ? null : new BuildMark(pending, block.Span.Start));
            pending = null;
        }
    }

    // Walks blocks of a section or a container, each with its own marker: the blocks inside a marked container build
    // with it, as part of it, unless they have a marker of their own.
    private void WalkMarked(IEnumerable<Block> blocks, ITarget target, bool section = false)
    {
        var (outer, outerBlock) = (this.build, this.marked);
        var all = this.Marked(blocks, section).ToList();
        foreach (var (index, (block, mark)) in all.Index())
        {
            // A block a comment follows becomes a block of its own, so that the comment belongs to it alone.
            if (target is FlowCollector flow && index + 1 < all.Count && all[index + 1].Block is CustomContainer { Info: SlideKinds.Comment })
            {
                flow.Break();
            }

            (this.build, this.marked) = (mark, mark is null ? null : block);
            this.Walk(block, target, 0, RunStyle.None);
        }

        (this.build, this.marked) = (outer, outerBlock);
    }

    // Splits the paragraph at images: text before and after becomes paragraphs, each image a visual. A paragraph of
    // nothing but two or more images is one visual: a row of them, side by side.
    private void Paragraph(ContainerInline? inline, ITarget target, ParagraphKind kind, int level, int start, RunStyle style)
    {
        var collector = new RunCollector(this, imagesAsText: false);
        collector.Add(inline, style, null);
        var images = collector.Pieces.OfType<SlideImage>().ToList();
        if (images.Count >= 2 && !collector.Pieces.OfType<QrCode>().Any() && collector.Pieces.OfType<List<TextRun>>().All(runs => Trim(runs).Count == 0))
        {
            target.Visual(null, images);
            return;
        }

        var first = true;
        foreach (var piece in collector.Pieces)
        {
            if (piece is SlideImage image)
            {
                target.Visual(null, [image]);
            }
            else if (piece is QrCode qr)
            {
                target.Visual(null, null, qr.Drawing, placement: qr.Placement);
            }
            else if (piece is List<TextRun> runs && Trim(this.Icons(Colour(runs), this.LineOf(inline))) is { Count: > 0 } trimmed)
            {
                target.Text(new TextParagraph(trimmed, first ? kind : ParagraphKind.Plain, level, start));
                first = false;
            }
        }
    }

    // An icon run has no text, but is not blank.
    private static List<TextRun> Trim(List<TextRun> runs)
    {
        static bool Blank(TextRun r) => r.IsBreak || (r.Icon is null && r.Text.Trim().Length == 0);
        var result = runs.SkipWhile(Blank).ToList();
        while (result.Count > 0 && Blank(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        if (result.Count > 0)
        {
            result[0] = result[0] with { Text = result[0].Text.TrimStart() };
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        }

        return result;
    }

    private List<TextRun> Runs(ContainerInline? inline, bool imagesAsText, RunStyle style = RunStyle.None)
    {
        var collector = new RunCollector(this, imagesAsText);
        collector.Add(inline, style, null);
        return Trim(this.Icons(Colour(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList()), this.LineOf(inline)));
    }

    // `:icon-name:`: an icon run of its own, unless icons are switched off.
    private List<TextRun> Icons(List<TextRun> runs, int line) => IconSyntax.Apply(
        runs,
        r => r.Text,
        r => r.Style.HasFlag(RunStyle.Code) || r.Style.HasFlag(RunStyle.Label),
        (r, text) => r with { Text = text },
        (r, icon) => r with { Icon = icon },
        options.Icons,
        line,
        Format.Pptx);

    // The line of a paragraph's text, from its first inline; 0 if unknown.
    private int LineOf(ContainerInline? inline) => inline?.FirstChild is { } first ? this.LineOf(first) : 0;

    // Every item is one paragraph that starts with an icon: the section is then a flow slide, and the list an icon list.
    private bool IsIconList(ListBlock list) =>
        options.Icons is not null && list.Count > 0
        && list.OfType<ListItemBlock>().All(item => item is [ParagraphBlock paragraph] && IconSyntax.StartsWithIcon(paragraph));

    private List<IconItem>? IconList(ListBlock list)
    {
        if (!this.IsIconList(list))
        {
            return null;
        }

        var items = new List<IconItem>();
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var runs = this.Runs(((ParagraphBlock)item[0]).Inline, imagesAsText: true);
            if (runs is not [{ Icon: { } icon }, ..])
            {
                return null;
            }

            items.Add(new IconItem(icon.Drawing, Trim(runs[1..])));
        }

        return items;
    }

    // `[words]{.success}`: the words bold, in that status colour.
    private static List<TextRun> Colour(List<TextRun> runs) => ColouredWords.Apply(
        runs,
        r => r.Text,
        r => r.Style.HasFlag(RunStyle.Code),
        (r, text) => r with { Text = text },
        (r, category) => r with { Style = r.Style | RunStyle.Bold, Colour = category },
        Format.Pptx);

    private void Container(CustomContainer container, FlowCollector target)
    {
        var name = container.Info ?? string.Empty;
        var arguments = container.Arguments?.Trim() ?? string.Empty;
        LayoutSyntax.CheckContainer(name, arguments, Format.Pptx);
        if (VisualContainers.IsVisual(name))
        {
            target.Add(new DrawingBlock(VisualContainers.Draw(container, this.Visuals())) { Placement = DrawingPlacement.Default });
            return;
        }

        switch (name)
        {
            case "cards":
                target.Add(new CardsBlock(this.Cards(container)));
                break;
            case "rows":
                target.Add(new RowsBlock(this.Rows(container)));
                break;
            case "panel":
                var outerDark = this.onDark;
                this.onDark |= arguments == "dark";
                target.Add(new PanelBlock(arguments == "dark", this.Flow(container)));
                this.onDark = outerDark;
                break;
            case "columns":
                target.Add(this.Columns(container, arguments));
                break;
            case LayoutSyntax.Notes:
                throw LayoutSyntax.NestedNotes(Format.Pptx);
            case SlideKinds.Conclusion:
                target.Add(new ConclusionBlock(this.Collect(container).Paragraphs));
                break;
            case SlideKinds.Comment:
                target.Comment(this.Collect(container).Paragraphs);
                break;
            case "statement":
                // Its blocks follow on the slide itself: with the statement's marker, unless they have their own.
                var children = this.Marked(container, section: false).ToList();
                if (children.FirstOrDefault() is not (ParagraphBlock lead, var leadMark))
                {
                    throw Error("A '::: statement' starts with a paragraph, its statement.");
                }

                var (outer, outerBlock) = (this.build, this.marked);
                this.build = leadMark ?? outer;
                target.Add(new StatementBlock(this.Runs(lead.Inline, imagesAsText: true)));
                foreach (var (child, mark) in children.Skip(1))
                {
                    (this.build, this.marked) = (mark ?? outer, mark is null ? null : child);
                    this.Walk(child, target, 0, RunStyle.None);
                }

                (this.build, this.marked) = (outer, outerBlock);
                break;
        }
    }

    // What drawn containers are drawn with: on a dark background the brand for dark slides, unless it already is one.
    private VisualContext Visuals() => new(
        ResolvedBrand.Resolve(this.onDark && !ResolvedBrand.IsDark(brand) ? ResolvedBrand.DarkSlideBrand(brand) : brand),
        VisualContext.TodayOf(options.TimeProvider),
        texts,
        this.LineOf,
        Format.Pptx);

    // Its parts, split at thematic breaks (`---` after a blank line), side by side.
    private ColumnsBlock Columns(CustomContainer container, string arguments)
    {
        var parts = new List<List<Block>> { new() };
        foreach (var child in container)
        {
            if (child is ThematicBreakBlock)
            {
                parts.Add([]);
            }
            else
            {
                parts[^1].Add(child);
            }
        }

        if (parts.Count is not (2 or 3) || parts.Any(p => p.Count == 0))
        {
            throw Error("A '::: columns' holds two or three columns, separated by a line '---' with a blank line before it.");
        }

        var widths = arguments.Length > 0 ? LayoutSyntax.ColumnWidths(arguments)! : Enumerable.Repeat(1, parts.Count).ToList();
        if (widths.Count != parts.Count)
        {
            throw Error($"'::: columns {arguments}' gives {widths.Count} widths for {parts.Count} columns.");
        }

        return new ColumnsBlock(widths, parts.Select(p => (IReadOnlyList<FlowBlock>)this.Flow(p)).ToList());
    }

    // A list of `**Title** – text` items, or a `###` heading before each card's blocks.
    private List<Card> Cards(CustomContainer container)
    {
        if (container.Count == 1 && container[0] is ListBlock list)
        {
            return list.OfType<ListItemBlock>().Select(item =>
            {
                var (title, rest, others) = this.Item(item, "cards");
                var body = this.Flow(others);
                if (rest.Count > 0)
                {
                    body.Insert(0, new TextBlock([new TextParagraph(rest)]));
                }

                return new Card(title, null, body);
            }).ToList();
        }

        var cards = new List<Card>();
        HeadingBlock? heading = null;
        var blocks = new List<Block>();
        void Add()
        {
            if (heading is not null)
            {
                var figure = blocks.FirstOrDefault() is ParagraphBlock first && Figure(first) is { } strong ? this.Runs(strong, imagesAsText: true) : null;
                var icon = figure is null && options.Icons is not null && blocks.FirstOrDefault() is ParagraphBlock lead && IconSyntax.StartsWithIcon(lead)
                    && this.Runs(lead.Inline, imagesAsText: true) is [{ Icon: { } only }]
                    ? only.Drawing
                    : null;
                cards.Add(new Card(this.Runs(heading.Inline, imagesAsText: true), figure, this.Flow(figure is null && icon is null ? blocks : blocks.Skip(1))) { FigureIcon = icon });
            }
        }

        foreach (var child in container)
        {
            if (child is HeadingBlock { Level: 3 } next)
            {
                Add();
                heading = next;
                blocks = [];
            }
            else if (heading is null)
            {
                throw Error("In '::: cards', each card starts with a ### heading, or the container holds one list of '- **Title** – text' items.");
            }
            else
            {
                blocks.Add(child);
            }
        }

        Add();
        return cards.Count > 0 ? cards : throw Error("A '::: cards' holds no cards.");
    }

    // Each item a row: its terms on the left, its description beside it; an item without a term (a second `:`)
    // continues the description of the one before.
    private List<Row> Definitions(DefinitionList list)
    {
        var rows = new List<Row>();
        foreach (var item in list.OfType<DefinitionItem>())
        {
            var term = new List<TextRun>();
            foreach (var definition in item.OfType<DefinitionTerm>())
            {
                if (term.Count > 0)
                {
                    term.Add(TextRun.Break);
                }

                term.AddRange(this.Runs(definition.Inline, imagesAsText: true));
            }

            var description = new List<TextParagraph>();
            foreach (var block in this.Flow(item.Where(b => b is not DefinitionTerm)))
            {
                description.AddRange(block is TextBlock text ? text.Paragraphs : throw Error("The descriptions of a definition list on a slide hold text only."));
            }

            if (term.Count == 0 && rows.Count > 0)
            {
                rows[^1] = rows[^1] with { Description = [.. rows[^1].Description, .. description] };
            }
            else
            {
                rows.Add(new Row(term, description));
            }
        }

        return rows;
    }

    private List<Row> Rows(CustomContainer container)
    {
        if (container.Count != 1 || container[0] is not ListBlock list)
        {
            throw Error("A '::: rows' holds one list of '- **Term** – description' items.");
        }

        return list.OfType<ListItemBlock>().Select(item =>
        {
            var (term, rest, others) = this.Item(item, "rows");
            var description = new List<TextParagraph>();
            if (rest.Count > 0)
            {
                description.Add(new TextParagraph(rest));
            }

            foreach (var block in this.Flow(others))
            {
                description.AddRange(block is TextBlock text ? text.Paragraphs : throw Error("The items of '::: rows' hold text only."));
            }

            return new Row(term, description);
        }).ToList();
    }

    // The bold start of an item's first paragraph, the rest of that paragraph without its separator, and the item's other blocks.
    private (List<TextRun> Title, List<TextRun> Body, List<Block> Others) Item(ListItemBlock item, string container)
    {
        var runs = item.FirstOrDefault() is ParagraphBlock paragraph ? this.Runs(paragraph.Inline, imagesAsText: true) : [];
        var bold = runs.TakeWhile(r => !r.IsBreak && r.Style.HasFlag(RunStyle.Bold)).ToList();
        if (bold.Count == 0)
        {
            throw Error($"Each item of '::: {container}' starts with **bold** text, its title.");
        }

        var title = bold.Select(r => r with { Style = r.Style & ~RunStyle.Bold }).ToList();
        title[^1] = title[^1] with { Text = title[^1].Text.TrimEnd().TrimEnd(':').TrimEnd() };
        var rest = runs.Skip(bold.Count).ToList();
        if (rest.Count > 0 && !rest[0].IsBreak && rest[0].Style == RunStyle.None)
        {
            rest[0] = rest[0] with { Text = SeparatorPattern().Replace(rest[0].Text, string.Empty) };
        }

        return (title, Trim(rest), item.Skip(1).ToList());
    }

    private List<FlowBlock> Flow(IEnumerable<Block> blocks)
    {
        var flow = new FlowCollector(this, inContainer: true);
        this.WalkMarked(blocks, flow);
        return flow.Finish();
    }

    // Two or more code spans and nothing but spaces between them.
    private static List<string>? Tags(ParagraphBlock paragraph)
    {
        var tags = new List<string>();
        foreach (var inline in paragraph.Inline ?? [])
        {
            switch (inline)
            {
                case CodeInline code:
                    tags.Add(code.Content);
                    break;
                case LiteralInline literal when literal.Content.ToString().Trim().Length == 0:
                case LineBreakInline:
                    break;
                default:
                    return null;
            }
        }

        return tags.Count >= 2 ? tags : null;
    }

    // Every item is one paragraph that starts with a code span and goes on with text.
    private List<LabelledTag>? LabelledTags(ListBlock list)
    {
        var items = new List<LabelledTag>();
        foreach (var item in list.OfType<ListItemBlock>())
        {
            if (item.Count != 1 || item[0] is not ParagraphBlock { Inline.FirstChild: CodeInline code } paragraph)
            {
                return null;
            }

            var text = Trim(this.Runs(paragraph.Inline, imagesAsText: true).Skip(1).ToList());
            if (text.Count == 0)
            {
                return null;
            }

            items.Add(new LabelledTag(code.Content, text));
        }

        return items.Count > 0 ? items : null;
    }

    // A paragraph that is one **strong** run, spaces around it aside.
    private static EmphasisInline? Figure(ParagraphBlock paragraph)
    {
        var inlines = (paragraph.Inline ?? []).Where(i => i is not LiteralInline literal || literal.Content.ToString().Trim().Length > 0).ToList();
        return inlines is [EmphasisInline { DelimiterCount: >= 2, DelimiterChar: '*' or '_' } strong] ? strong : null;
    }

    private SlideTable Table(Table table, bool labels)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var grid = rows
            .Select(row => (IReadOnlyList<IReadOnlyList<TextRun>>)Enumerable.Range(0, columns)
                .Select(c => c < row.Count ? this.Cell((TableCell)row[c], labels && !row.IsHeader) : [])
                .ToList())
            .ToList();
        var alignments = Enumerable.Range(0, columns)
            .Select(c => c < table.ColumnDefinitions.Count
                ? table.ColumnDefinitions[c].Alignment switch
                {
                    TableColumnAlign.Left => CellAlignment.Left,
                    TableColumnAlign.Center => CellAlignment.Center,
                    TableColumnAlign.Right => CellAlignment.Right,
                    _ => CellAlignment.Default,
                }
                : CellAlignment.Default)
            .ToList();
        var highlighted = grid
            .Select((row, i) => !rows[i].IsHeader && row.Count > 0 && row[0].Any(r => !r.IsBreak) && row[0].All(r => r.IsBreak || r.Style.HasFlag(RunStyle.Mark)))
            .ToList();
        return new SlideTable(grid, alignments, rows.Count > 0 && rows[0].IsHeader, highlighted);
    }

    private IReadOnlyList<TextRun> Cell(TableCell cell, bool labels)
    {
        var leaves = cell.Descendants<LeafBlock>().Where(l => l.Inline is not null).ToList();
        if (HarveyBall.Quarters(leaves) is { } quarters)
        {
            return [new TextRun(HarveyBall.Glyph(quarters), RunStyle.None) { Symbol = true }];
        }

        if (labels && leaves is [{ Inline: { } only }]
            && only.Where(i => i is not LiteralInline literal || literal.Content.ToString().Trim().Length > 0).ToList() is [CodeInline code])
        {
            return [new TextRun(code.Content, RunStyle.Label)];
        }

        var runs = new List<TextRun>();
        foreach (var leaf in leaves)
        {
            if (runs.Count > 0)
            {
                runs.Add(TextRun.Break);
            }

            runs.AddRange(this.Runs(leaf.Inline, imagesAsText: true));
        }

        return runs;
    }

    // A SlideImage, or for `qr:…` a QrCode.
    private object Image(LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty;
        var attributes = ImageAttributes.Take(link, this.LineOf(link), Format.Pptx);
        if (QrDrawing.IsQr(url))
        {
            var (drawing, scaled) = QrDrawing.ForImage(url, AltText(link), link.Title, attributes, ResolvedBrand.Resolve(brand), this.LineOf(link), Format.Pptx);
            return new QrCode(drawing, scaled ? new DrawingPlacement(1, 1) : DrawingPlacement.Default with { MaxWidthShare = 1.0 / 3 });
        }

        var resource = document.FindResource(url)
            ?? throw new DocumentConversionException(Format.Pptx, $"The image '{url}' is not among the document's resources.");
        double? scale = link.Title is { } title && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
        if (scale is not null && attributes.Scale is not null)
        {
            throw Error($"The image '{url}' at line {this.LineOf(link)} has scale both in its title and in its attributes '{attributes.Text}'; keep one.");
        }

        this.Ignored(link, attributes, attributes.Focus is not null && attributes.Fit == ImageFit.Contain && !attributes.Round && !this.pictureSlots, "focus", "it is not cropped; add fit=cover or .round");
        this.Ignored(link, attributes, attributes.Scale is not null && attributes.Fit == ImageFit.Cover, "scale", "with fit=cover it fills its box");
        var caption = (link.Title is { } words && CaptionPattern().IsMatch(words)) || attributes.Caption;
        return this.Placeable(new SlideImage(resource, AltText(link), scale ?? attributes.Scale, caption) { Attributes = attributes });
    }

    // An image attribute that does nothing where it is.
    private void Ignored(LinkInline link, ImageAttributes attributes, bool ignored, string name, string reason)
    {
        if (ignored)
        {
            notes.Add(NoteCodes.AttributeIgnored, this.LineOf(link), $"The image attribute {name} in '{attributes.Text}' has no effect: {reason}.");
        }
    }

    // In a heading or a table cell an image is its alt text, so its attributes do nothing.
    private void AsText(LinkInline link)
    {
        if (ImageAttributes.Take(link, this.LineOf(link), Format.Pptx) is { IsEmpty: false } attributes)
        {
            notes.Add(NoteCodes.AttributeIgnored, this.LineOf(link), $"The image attributes '{attributes.Text}' have no effect: the image is shown as its alt text here.");
        }
    }

    // An image in a format slides cannot hold, such as SVG, through the image converters: a PNG at twice its natural
    // size, placed at its natural size. An SVG also rides along, for PowerPoint to draw as a vector.
    private SlideImage Placeable(SlideImage image)
    {
        if (ImageInfo.Read(image.Resource.Content.Span) is not null
            || ImageConverters.Convert(options.ImageConverters, image.Resource, 2, Format.Pptx) is not var (converted, width, height))
        {
            return image;
        }

        return image with { Resource = converted, Natural = (width, height), Vector = SvgBlip.IsSvg(image.Resource) ? image.Resource : null };
    }

    private static string AltText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    private static DocumentConversionException Error(string message) => new(Format.Pptx, message);

    // A soft line break, kept apart from spaces only where lines matter: a quote's byline, a person's lines.
    private const string SoftBreak = "\u2028";

    [GeneratedRegex(@"scale\s*=\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"(?<![\w-])caption(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CaptionPattern();

    [GeneratedRegex(@"^\s*(?:[–—:]|-(?=\s))?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    /// <summary>Where the blocks of a section go: text, and tables, images or drawings.</summary>
    private interface ITarget
    {
        void Text(TextParagraph paragraph);

        void Visual(SlideTable? table, IReadOnlyList<SlideImage>? images, Drawing? drawing = null, ChartSpec? chart = null, DrawingPlacement? placement = null);
    }

    /// <summary>A <c>qr:…</c> image: its drawing, and how it is sized on the slide.</summary>
    private sealed record QrCode(Drawing Drawing, DrawingPlacement Placement);

    /// <summary>Collects runs, cut into pieces at images unless images count as their alt text.</summary>
    private sealed class RunCollector(DeckParser parser, bool imagesAsText, bool softBreaks = false)
    {
        private List<TextRun> current = [];

        public List<object> Pieces { get; } = [];

        public void Add(ContainerInline? container, RunStyle style, string? link)
        {
            if (this.Pieces.Count == 0)
            {
                this.Pieces.Add(this.current);
            }

            if (container is null)
            {
                return;
            }

            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        this.Text(literal.Content.ToString(), style, link);
                        break;
                    case CodeInline code:
                        this.Text(code.Content, style | RunStyle.Code, link);
                        break;
                    case EmphasisInline emphasis:
                        var added = emphasis.DelimiterChar switch
                        {
                            '~' => RunStyle.Strike,
                            '=' => RunStyle.Mark,
                            _ => emphasis.DelimiterCount >= 2 ? RunStyle.Bold : RunStyle.Italic,
                        };
                        this.Add(emphasis, style | added, link);
                        break;
                    case LinkInline { IsImage: true } image when imagesAsText:
                        parser.AsText(image);
                        this.Text(AltText(image), style, link);
                        break;
                    case LinkInline { IsImage: true } image:
                        this.Pieces.Add(parser.Image(image));
                        this.current = [];
                        this.Pieces.Add(this.current);
                        break;
                    case LinkInline hyperlink:
                        this.Add(hyperlink, style, hyperlink.GetDynamicUrl?.Invoke() ?? hyperlink.Url);
                        break;
                    case AutolinkInline autolink:
                        this.Text(autolink.Url, style, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url);
                        break;
                    case LineBreakInline lineBreak when lineBreak.IsHard:
                        this.current.Add(TextRun.Break);
                        break;
                    case LineBreakInline when softBreaks:
                        this.current.Add(new TextRun(SoftBreak, RunStyle.None));
                        break;
                    case LineBreakInline:
                        this.Text(" ", style, link);
                        break;
                    case HtmlEntityInline entity:
                        this.Text(entity.Transcoded.ToString(), style, link);
                        break;
                    case HtmlInline html:
                        parser.HtmlAsText(html, html.Tag);
                        this.Text(html.Tag, style, link);
                        break;
                    case FootnoteLink { IsBackLink: false } footnote:
                        this.Text(parser.FootnoteNumber(footnote.Footnote).ToString(CultureInfo.InvariantCulture), style | RunStyle.Superscript, link);
                        break;
                    case FootnoteLink:
                    case TaskList:
                        break;
                    case ContainerInline nested:
                        this.Add(nested, style, link);
                        break;
                }
            }
        }

        private void Text(string text, RunStyle style, string? link)
        {
            if (text.Length == 0)
            {
                return;
            }

            // Footnote references stay apart, so `[^1][^2]` is two of them.
            if (this.current.Count > 0 && this.current[^1] is { IsBreak: false } last && last.Style == style && last.Link == link
                && !style.HasFlag(RunStyle.Superscript) && last.Text != SoftBreak)
            {
                this.current[^1] = last with { Text = last.Text + text };
            }
            else
            {
                this.current.Add(new TextRun(text, style, link));
            }
        }
    }

    /// <summary>
    /// Collects the text and visuals of one heading's section and turns them into slides; each paragraph and visual
    /// takes the build marker of the block it comes from.
    /// </summary>
    private sealed class SectionBuilder(
        DeckParser parser, List<Slide> slides, IReadOnlyList<TextRun> title, bool isTitle, bool isSection, string? layout, bool closing, int? number) : ITarget
    {
        private bool done;
        private List<TextParagraph> above = [];
        private List<TextParagraph> below = [];
        private SlideTable? table;
        private IReadOnlyList<SlideImage>? images;
        private Drawing? drawing;
        private ChartSpec? chart;
        private BuildMark? visualBuild;
        private DrawingPlacement placement = DrawingPlacement.Default;
        private bool titlePending = isTitle;

        private bool HasVisual => this.table is not null || this.images is not null || this.drawing is not null || this.chart is not null;

        public void Text(TextParagraph paragraph) =>
            (this.HasVisual ? this.below : this.above).Add(parser.build is { } mark ? paragraph with { Build = mark } : paragraph);

        public void Visual(SlideTable? newTable, IReadOnlyList<SlideImage>? newImages, Drawing? newDrawing = null, ChartSpec? newChart = null, DrawingPlacement? newPlacement = null)
        {
            if (this.titlePending)
            {
                slides.Add(new TitleSlide(title, this.above, isSection) { Layout = layout, Closing = closing, Number = number });
                this.above = [];
                this.titlePending = false;
            }
            else if (this.HasVisual)
            {
                this.FlushVisual();
            }

            this.table = newTable;
            this.images = newImages;
            this.drawing = newDrawing;
            this.chart = newChart;
            this.visualBuild = parser.build;
            this.placement = newPlacement ?? DrawingPlacement.Default;
        }

        /// <summary>The section as one slide filling its layout's placeholders, instead of what it would become.</summary>
        public void Placeholders(PlaceholderSlide slide)
        {
            slides.Add(slide);
            this.done = true;
        }

        public void Finish()
        {
            if (this.done)
            {
                return;
            }

            if (this.titlePending)
            {
                slides.Add(new TitleSlide(title, this.above, isSection) { Layout = layout, Closing = closing, Number = number });
            }
            else if (this.HasVisual)
            {
                this.FlushVisual();
            }
            else
            {
                slides.Add(new ContentSlide(title, this.above) { Layout = layout });
            }
        }

        private void FlushVisual()
        {
            slides.Add(new VisualSlide(title, this.above, this.table, this.images, this.below)
            {
                Drawing = this.drawing,
                Chart = this.chart,
                Placement = this.placement,
                Layout = layout,
                VisualBuild = this.visualBuild,
            });
            this.above = [];
            this.below = [];
            this.table = null;
            this.images = null;
            this.drawing = null;
            this.chart = null;
            this.visualBuild = null;
        }
    }

    /// <summary>
    /// Collects the text parts and visuals of a section that fills a layout's placeholders, in their order, each with its
    /// build marker; or replays them into a <see cref="SectionBuilder"/> as the slides the section would otherwise be.
    /// </summary>
    private sealed class PlaceholderCollector(DeckParser parser) : ITarget
    {
        private readonly List<Call> calls = [];
        private int part;

        public void NextPart() => this.part++;

        public void Text(TextParagraph paragraph) => this.calls.Add(new Call(this.part, parser.build, paragraph, null, null, null, null, null));

        public void Visual(SlideTable? table, IReadOnlyList<SlideImage>? images, Drawing? drawing = null, ChartSpec? chart = null, DrawingPlacement? placement = null) =>
            this.calls.Add(new Call(this.part, parser.build, null, table, images, drawing, chart, placement));

        // Text parts in the order of their first paragraph; the images of a row each on its own.
        public List<PlaceholderItem> Items()
        {
            var items = new List<PlaceholderItem>();
            var texts = new Dictionary<int, List<TextParagraph>>();
            foreach (var call in this.calls)
            {
                if (call.Text is { } text)
                {
                    if (!texts.TryGetValue(call.Part, out var paragraphs))
                    {
                        texts[call.Part] = paragraphs = [];
                        items.Add(new PlaceholderItem(paragraphs, null));
                    }

                    paragraphs.Add(call.Mark is { } mark ? text with { Build = mark } : text);
                }
                else if (call.Images is { } images)
                {
                    items.AddRange(images.Select((image, i) => new PlaceholderItem(
                        null,
                        new PlaceholderVisual(null, image, null, null) { Build = images.Count > 1 && call.Mark is { } mark ? mark with { Item = i } : call.Mark })));
                }
                else
                {
                    items.Add(new PlaceholderItem(null, new PlaceholderVisual(call.Table, null, call.Drawing, call.Chart)
                    {
                        Build = call.Mark,
                        Placement = call.Placement ?? DrawingPlacement.Default,
                    }));
                }
            }

            return items;
        }

        public void Replay(SectionBuilder section)
        {
            var outer = parser.build;
            foreach (var call in this.calls)
            {
                parser.build = call.Mark;
                if (call.Text is { } text)
                {
                    section.Text(text);
                }
                else
                {
                    section.Visual(call.Table, call.Images, call.Drawing, call.Chart, call.Placement);
                }
            }

            parser.build = outer;
        }

        private sealed record Call(
            int Part, BuildMark? Mark, TextParagraph? Text, SlideTable? Table, IReadOnlyList<SlideImage>? Images, Drawing? Drawing, ChartSpec? Chart, DrawingPlacement? Placement);
    }

    /// <summary>
    /// Collects the blocks of a flow slide or of a container on one; consecutive paragraphs make one text block. Each
    /// paragraph and block takes the build marker of the Markdown block it comes from.
    /// </summary>
    private sealed class FlowCollector(DeckParser parser, bool inContainer) : ITarget
    {
        private readonly List<FlowBlock> blocks = [];
        private List<TextParagraph>? text;

        /// <summary>Whether this is the inside of a container, where lists of code-span items become labelled tags.</summary>
        public bool InContainer => inContainer;

        public void Text(TextParagraph paragraph)
        {
            if (this.text is null)
            {
                this.text = [];
                this.blocks.Add(new TextBlock(this.text));
            }

            this.text.Add(parser.build is { } mark ? paragraph with { Build = mark } : paragraph);
        }

        public void Visual(SlideTable? table, IReadOnlyList<SlideImage>? images, Drawing? drawing = null, ChartSpec? chart = null, DrawingPlacement? placement = null) =>
            this.Add(table is not null ? new TableBlock(table)
                : images is [var image] ? new ImageBlock(image)
                : images is not null ? new ImageRowBlock(images)
                : chart is not null ? new ChartBlock(chart)
                : new DrawingBlock(drawing!) { Placement = placement ?? DrawingPlacement.Default });

        public void Add(FlowBlock block)
        {
            this.text = null;
            this.blocks.Add(parser.build is { } mark && block.Build is null ? block with { Build = mark } : block);
        }

        /// <summary>The next paragraph starts a text block of its own.</summary>
        public void Break() => this.text = null;

        /// <summary>A <c>::: comment</c>: it takes the block before it as its target.</summary>
        public void Comment(IReadOnlyList<TextParagraph> paragraphs)
        {
            if (this.blocks.Count == 0)
            {
                throw Error("A '::: comment' goes right after the block it comments; it cannot be the first block of a slide.");
            }

            this.blocks[^1] = new CommentBlock(this.blocks[^1], paragraphs) { Build = parser.build };
            this.text = null;
        }

        public List<FlowBlock> Finish() => this.blocks;
    }

    /// <summary>
    /// Collects the text of a slide kind, each paragraph with its build marker, and its charts; <see cref="Other"/>
    /// says whether anything else came.
    /// </summary>
    private sealed class KindCollector(DeckParser parser) : ITarget
    {
        public List<TextParagraph> Paragraphs { get; } = [];

        public List<ChartBlock> Charts { get; } = [];

        public bool Other { get; private set; }

        public void Text(TextParagraph paragraph) => this.Paragraphs.Add(parser.build is { } mark ? paragraph with { Build = mark } : paragraph);

        public void Visual(SlideTable? table, IReadOnlyList<SlideImage>? images, Drawing? drawing = null, ChartSpec? chart = null, DrawingPlacement? placement = null)
        {
            if (chart is not null)
            {
                this.Charts.Add(new ChartBlock(chart) { Build = parser.build });
            }
            else
            {
                this.Other = true;
            }
        }
    }
}
