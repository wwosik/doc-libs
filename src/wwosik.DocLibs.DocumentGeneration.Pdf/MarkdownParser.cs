using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using MarkdownDocument = wwosik.DocLibs.DocumentConversions.MarkdownDocument;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>
/// Turns Markdown into the block model. A document that starts with a <c>#</c> heading gets a cover from it and
/// the paragraphs up to the next heading; everything else flows as blocks. Headings get their numbers here
/// (<see cref="HeadingNumbering"/>), visuals their numbered captions (<see cref="Captions"/>), and the table of contents
/// its entries.
/// </summary>
internal sealed partial class MarkdownParser(
    MarkdownDocument document, PdfOptions options, Brand brand, int lineOffset, GenerationNotes notes, GeneratedTexts texts, Typography? typography,
    CodeHighlighting? highlighting)
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    // The chapters an empty `{.agenda}` lists, by its heading.
    private readonly Dictionary<Markdig.Syntax.HeadingBlock, ListBlock> agendas = new(ReferenceEqualityComparer.Instance);

    // Images in grey, by the image they were made from, so an image used twice is converted and embedded once.
    private readonly Dictionary<DocumentResource, DocumentResource> greyImages = new(ReferenceEqualityComparer.Instance);

    // The `Table:` and `Figure:` paragraphs a visual took as its caption, so they are not written again.
    private readonly HashSet<Markdig.Syntax.ParagraphBlock> captionParagraphs = new(ReferenceEqualityComparer.Instance);

    // The captions so far, by kind: figures and tables are numbered apart, through the whole document.
    private readonly Dictionary<CaptionKind, int> captions = [];

    // The headings the table of contents lists; the anchors in the document, so a generated one is new.
    private readonly List<ContentsEntry> contents = [];
    private readonly HashSet<string> anchors = new(StringComparer.Ordinal);

    // The blocks a `::: pagebreak` comes right before (PageBreaks.Flatten).
    private IReadOnlySet<Markdig.Syntax.Block> pageBreaks = new HashSet<Markdig.Syntax.Block>();

    // The highest heading level after the cover, and the numbers with `headingNumbers: true`.
    private int top = 1;
    private HeadingNumbering? numbering;

    // Whether the headings being added are appendices (from the `#` with `{.appendix}` on), and how many there are so far.
    private bool appendices;
    private int appendixCount;

    /// <remarks>
    /// <paramref name="brand"/> is the brand code-block renderers draw in; <paramref name="lineOffset"/> the lines of
    /// front matter cut off the Markdown, so renderers get the block's line in the original; <paramref name="notes"/>
    /// collects what the writer should know, such as HTML shown as text; <paramref name="texts"/> are the texts of the
    /// document language, for alert titles.
    /// </remarks>
    public static DocumentModel Parse(
        MarkdownDocument document,
        PdfOptions options,
        Brand? brand = null,
        int lineOffset = 0,
        GenerationNotes? notes = null,
        GeneratedTexts? texts = null,
        Typography? typography = null,
        CodeHighlighting? highlighting = null)
    {
        notes ??= new GenerationNotes();
        texts ??= GeneratedTexts.English;
        var parser = new MarkdownParser(document, options, brand ?? Brand.Neutral, lineOffset, notes, texts, typography, highlighting);
        var markdown = Markdown.Parse(document.Markdown, Pipeline);

        // Page breaks: their content stays where they were, and a PageBreakBlock goes before the block each comes before.
        parser.pageBreaks = PageBreaks.Flatten(markdown, DocumentFormat.Pdf);

        // Where the appendices start, checked before a version takes anything out.
        BackMatter.Check(markdown, DocumentFormat.Pdf, parser.LineOf, notes);

        // The version's content only, before anything is laid out.
        AudienceFilter.Apply(markdown, options.Audience ?? Audience.Customer, DocumentFormat.Pdf, parser.LineOf, texts, notes);
        MarkdownSyntax.ReportMissingFootnotes(markdown, parser.LineOf, notes);

        // Build markers are for decks: checked like there, then left out wherever they are.
        BuildMarker.Check(markdown, DocumentFormat.Pdf, parser.LineOf);
        SceneSyntax.Check(markdown, DocumentFormat.Pdf, parser.LineOf);
        var footnotes = markdown.OfType<FootnoteGroup>().SelectMany(g => g.OfType<Footnote>()).OrderBy(f => f.Order).ToList();
        parser.anchors.UnionWith(markdown.Descendants<Markdig.Syntax.HeadingBlock>().Select(Anchor).OfType<string>());
        var blocks = markdown.Where(b => b is not FootnoteGroup && !(b is Markdig.Syntax.ParagraphBlock paragraph && BuildMarker.IsMarker(paragraph))).ToList();
        Cover? cover = null;
        var start = 0;
        if (blocks.FirstOrDefault() is Markdig.Syntax.HeadingBlock { Level: 1 } first)
        {
            // A plain document has no cover: its first `#` is the title of the first page, and the paragraphs after it are text.
            var plain = options.Layout == PdfLayout.Plain;
            var place = plain ? "the title" : "the cover";
            var subtitle = new List<IReadOnlyList<TextRun>>();
            start = 1;
            while (!plain && start < blocks.Count && blocks[start] is Markdig.Syntax.ParagraphBlock paragraph && !HasImage(paragraph) && !parser.pageBreaks.Contains(paragraph))
            {
                subtitle.Add(parser.NotDrawn(parser.Runs(paragraph.Inline), place));
                start++;
            }

            if (parser.AttributesOf(first) is { NewPage: true })
            {
                throw new DocumentConversionException(
                    DocumentFormat.Pdf,
                    plain
                        ? "{.newpage} cannot go on the title, the first '#': the title starts the first page."
                        : "{.newpage} cannot go on the cover heading, the first '#': the cover is a page of its own.");
            }

            cover = new Cover(parser.NotDrawn(parser.TitleOf(first, 1).Text, place), subtitle, Anchor(first));
        }

        parser.CheckSections(blocks, start == 1 ? (Markdig.Syntax.HeadingBlock)blocks[0] : null);
        parser.top = HeadingNumbering.Top(blocks.Skip(start));
        parser.numbering = options.HeadingNumbers ? new HeadingNumbering(parser.top) : null;
        var result = new List<Block>();
        foreach (var block in blocks.Skip(start))
        {
            // Speaker notes are for decks: checked like there, then left out.
            if (block is CustomContainer { Info: LayoutSyntax.Notes } speakerNotes)
            {
                LayoutSyntax.CheckContainer(LayoutSyntax.Notes, speakerNotes.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
                LayoutSyntax.CheckNoNestedNotes(speakerNotes, DocumentFormat.Pdf);
                continue;
            }

            parser.Add(block, result);
        }

        // The footnotes by their number, Markdig's order through the whole document, for the foot of their page.
        var notesByNumber = new Dictionary<int, FootnoteText>();
        foreach (var footnote in footnotes.Where(f => f.Order > 0))
        {
            var paragraphs = new List<IReadOnlyList<TextRun>>();
            NoteParagraphs(parser.Children(footnote), paragraphs);
            notesByNumber.TryAdd(footnote.Order, new FootnoteText(footnote.Order, paragraphs, parser.LineOf(footnote)));
        }

        return new DocumentModel(cover, result)
        {
            Contents = parser.contents.Count > 0 ? new TableOfContents(parser.contents) : null,
            Footnotes = notesByNumber,
        };
    }

    // The slide-layout syntax of the PPTX generator is accepted and shown plainly: custom containers without their
    // inline form (`::text::`), which would take `std::vector::size` apart.
    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
            .UseAutoLinks()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAlertBlocks()
            .UseDocumentSyntax();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    // The checks of slide kinds and of `::: conclusion` and `::: comment` are the deck's, per `#` or `##` section. An empty
    // `{.agenda}` gets the chapters: the `#` headings but the cover and a `{.closing}` one.
    private void CheckSections(List<Markdig.Syntax.Block> blocks, Markdig.Syntax.HeadingBlock? cover)
    {
        var sections = new List<(Markdig.Syntax.HeadingBlock? Heading, List<Markdig.Syntax.Block> Blocks)> { (null, []) };
        foreach (var block in blocks)
        {
            if (block is Markdig.Syntax.HeadingBlock { Level: <= 2 } heading)
            {
                sections.Add((heading, []));
            }
            else
            {
                sections[^1].Blocks.Add(block);
            }
        }

        var chapters = new List<IReadOnlyList<TextRun>>();
        foreach (var (heading, _) in sections)
        {
            if (heading is { Level: 1 } && heading != cover && this.AttributesOf(heading) is not { Closing: true })
            {
                chapters.Add(Trim([.. this.TitleOf(heading, 1).Text.Where(r => r.Icon is null)]));
            }
        }

        foreach (var (heading, content) in sections)
        {
            SlideKinds.CheckContainers(content, DocumentFormat.Pdf);
            if (heading is null || this.AttributesOf(heading)?.Kind is not { } kind)
            {
                continue;
            }

            var title = string.Concat(this.TitleOf(heading, 1).Text.Select(r => r.IsBreak ? " " : r.Text)).Trim();
            if (heading.Level == 1)
            {
                throw new DocumentConversionException(DocumentFormat.Pdf, $"{{.{kind}}} goes on a '##' heading: a '#' heading is the cover or a section divider.");
            }

            SlideKinds.Check(kind, content, title, DocumentFormat.Pdf);
            if (kind == SlideKinds.Agenda && SlideKinds.Content(content).Count == 0)
            {
                if (chapters.Count == 0)
                {
                    notes.Add(
                        NoteCodes.AgendaEmpty,
                        this.LineOf(heading),
                        $"The agenda '{title}' has no list and the document no chapters ('#' headings after the cover) to list, so it shows its heading only. Add the chapters, or write the agenda as a list.");
                }
                else
                {
                    this.agendas[heading] = new ListBlock(true, 1, [.. chapters.Select(c => (IReadOnlyList<Block>)[new ParagraphBlock(c)])]);
                }
            }
        }
    }

    // A footnote's blocks as text paragraphs: lists with their markers as text, code line by line, quotes and definition
    // lists by their paragraphs, a table row by row; images and drawings are left out. A reference inside a note is its
    // number only.
    private static void NoteParagraphs(IEnumerable<Block> blocks, List<IReadOnlyList<TextRun>> into)
    {
        static IReadOnlyList<TextRun> Plain(IEnumerable<TextRun> runs) => [.. runs.Select(r => r.Footnote is null ? r : r with { Footnote = null })];

        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    into.Add(Plain(paragraph.Runs));
                    break;
                case HeadingBlock heading:
                    into.Add(Plain(heading.Runs));
                    break;
                case ListBlock list:
                    for (var i = 0; i < list.Items.Count; i++)
                    {
                        var marker = list.Tasks?[i] is { } ticked ? ticked ? "[x] " : "[ ] "
                            : list.Ordered ? string.Create(CultureInfo.InvariantCulture, $"{list.Start + i}. ") : "• ";
                        var item = new List<IReadOnlyList<TextRun>>();
                        NoteParagraphs(list.Items[i], item);
                        if (item.Count == 0)
                        {
                            item.Add([]);
                        }

                        item[0] = [new TextRun(marker, RunStyle.None), .. item[0]];
                        into.AddRange(item);
                    }

                    break;
                case CodeBlock code:
                    into.AddRange(code.Lines.Select(l => string.Concat(l.Select(s => s.Text))).Where(l => l.Trim().Length > 0).Select(l => (IReadOnlyList<TextRun>)[new TextRun(l, RunStyle.Code)]));
                    break;
                case QuoteBlock quote:
                    NoteParagraphs(quote.Blocks, into);
                    break;
                case ColumnsBlock columns:
                    foreach (var column in columns.Columns)
                    {
                        NoteParagraphs(column, into);
                    }

                    break;
                case DefinitionListBlock definitions:
                    foreach (var definition in definitions.Items)
                    {
                        into.AddRange(definition.Terms.Select(t => Plain(t.Select(r => r.IsBreak ? r : r with { Style = r.Style | RunStyle.Bold }))));
                        NoteParagraphs(definition.Description, into);
                    }

                    break;
                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        into.Add(Plain(row.SelectMany((cell, c) => c == 0 ? cell : [new TextRun(" | ", RunStyle.None), .. cell])));
                    }

                    break;
            }
        }
    }

    private HeadingAttributes? AttributesOf(Markdig.Syntax.HeadingBlock heading) => this.TitleOf(heading, 1).Attributes;

    // A heading's text and attributes. The attributes are syntax, not text: taken off before typography, which would
    // curl their quotes; then the text is typeset. `level` picks the attributes, as in Split.
    private (List<TextRun> Text, HeadingAttributes? Attributes) TitleOf(Markdig.Syntax.HeadingBlock heading, int level)
    {
        var (text, attributes) = Split(this.Runs(heading.Inline, typeset: false), level);
        return (this.Typeset(text, this.LineOf(heading.Inline)), attributes);
    }

    private void Add(Markdig.Syntax.Block block, List<Block> target)
    {
        if (this.pageBreaks.Contains(block))
        {
            target.Add(new PageBreakBlock());
        }

        switch (block)
        {
            case Markdig.Syntax.HeadingBlock heading:
                var (title, headingAttributes) = this.TitleOf(heading, heading.Level);
                var text = this.NotDrawn(title, "a heading");

                // A kind of slide without a title, such as `## {.quote}`, has no heading here.
                if (text.Count > 0 || heading.Level > 2 || headingAttributes?.Kind is null)
                {
                    target.Add(this.Heading(heading, text, headingAttributes?.Unnumbered == true, headingAttributes?.NewPage == true, headingAttributes?.Appendix == true));
                }

                if (this.agendas.TryGetValue(heading, out var agenda))
                {
                    target.Add(agenda);
                }

                break;
            case CustomContainer { Info: SlideKinds.Conclusion } conclusion:
                // Like an alert without its title.
                target.Add(new QuoteBlock(this.Children(conclusion)));
                break;
            case CustomContainer { Info: SlideKinds.Comment } comment:
                // After the block it comments, as an italic quote.
                target.Add(new QuoteBlock([.. this.Children(comment).Select(Italic)]));
                break;
            case CustomContainer { Info: LayoutSyntax.Notes }:
                throw LayoutSyntax.NestedNotes(DocumentFormat.Pdf);
            case CustomContainer { Info: SceneSyntax.Name } scene:
                // The final state of the scene as one still drawing; its steps play only in decks.
                LayoutSyntax.CheckContainer(SceneSyntax.Name, scene.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
                target.Add(new DrawingBlock(SceneDrawing.Final(SceneSyntax.Parse(scene, document.Markdown, this.LineOf, DocumentFormat.Pdf), ResolvedBrand.Resolve(brand))));
                break;
            case CustomContainer container:
                LayoutSyntax.CheckContainer(container.Info ?? string.Empty, container.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
                if (VisualContainers.IsVisual(container.Info ?? string.Empty))
                {
                    target.Add(new DrawingBlock(VisualContainers.Draw(
                        container,
                        new VisualContext(ResolvedBrand.Resolve(brand), VisualContext.TodayOf(options.TimeProvider), texts, this.LineOf, DocumentFormat.Pdf))));
                    break;
                }

                if (container.Info == "columns")
                {
                    // Side by side: the columns are split at their `---` lines, as in decks.
                    var (widths, parts) = LayoutSyntax.Columns(container, container.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
                    target.Add(new ColumnsBlock(widths, [.. parts.Select(part =>
                    {
                        var column = new List<Block>();
                        foreach (var child in part)
                        {
                            this.Add(child, column);
                        }

                        return (IReadOnlyList<Block>)column;
                    })]));
                    break;
                }

                foreach (var child in container)
                {
                    this.Add(child, target);
                }

                break;
            case AlertBlock alert:
                LayoutSyntax.IsImportant(alert.Kind.ToString(), DocumentFormat.Pdf);
                target.Add(new QuoteBlock([
                    new ParagraphBlock([new TextRun(texts.Alert(alert.Kind.ToString()), RunStyle.Bold)]),
                    .. this.Children(alert)]));
                break;
            case Markdig.Syntax.ParagraphBlock marker when BuildMarker.IsMarker(marker):
                break;
            case Markdig.Syntax.ParagraphBlock caption when this.captionParagraphs.Contains(caption):
                break;
            case Markdig.Syntax.ParagraphBlock paragraph:
                if (Captions.Kind(paragraph) is { } misplaced)
                {
                    notes.Add(NoteCodes.CaptionIgnored, this.LineOf(paragraph), Captions.Ignored(misplaced));
                }

                this.Paragraph(paragraph.Inline, target);
                break;
            case Markdig.Syntax.ListBlock list:
                var tasks = list.OfType<ListItemBlock>().Select(Task).ToList();
                var items = list.OfType<ListItemBlock>().Select(item => this.Children(item)).ToList();
                var icons = items.Select((children, i) => tasks[i] is null ? LeadingIcon(children) : null).ToList();
                target.Add(new ListBlock(
                    list.IsOrdered,
                    list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1,
                    [.. items])
                {
                    Tasks = tasks.Any(t => t is not null) ? tasks : null,
                    Icons = icons.Any(i => i is not null) ? icons : null,
                });
                break;
            case DefinitionList definitions:
                target.Add(this.Definitions(definitions));
                break;
            case Markdig.Syntax.QuoteBlock quote:
                target.Add(new QuoteBlock(this.Children(quote)));
                break;
            case Table table:
                target.Add(this.Table(table) with { Caption = this.CaptionAfter(table, CaptionKind.Table) });
                break;
            case FencedCodeBlock fenced when this.Render(fenced) is { } rendered:
                var figure = this.CaptionAfter(fenced, CaptionKind.Figure);
                target.Add(rendered switch
                {
                    DrawingGraphic drawing => new DrawingBlock(drawing.Drawing) { Caption = figure },
                    ImageGraphic image => this.ImageOf(image.Image, 1.0) with { Caption = figure },
                    ChartGraphic chart => new DrawingBlock(chart.Drawing) { Caption = figure },
                    _ => throw new UnreachableException(),
                });
                break;
            case Markdig.Syntax.CodeBlock code:
                target.Add(this.Code(code));
                break;
            case ThematicBreakBlock:
                target.Add(new RuleBlock());
                break;
            case HtmlBlock { Type: HtmlBlockType.Comment }:
            case LinkReferenceDefinitionGroup:
                break;
            case HtmlBlock html:
                this.HtmlAsText(html, html.Lines.ToString());
                target.AddRange(html.Lines.ToString().Split('\n').Where(l => l.Trim().Length > 0)
                    .Select(l => new ParagraphBlock([new TextRun(l.Trim(), RunStyle.None)])));
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                this.Paragraph(leaf.Inline, target);
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    this.Add(child, target);
                }

                break;
        }
    }

    // A heading with its number, unless it is `{.unnumbered}` or inside a slide-layout container, where it is no chapter;
    // listed in the table of contents down to its depth, with an anchor of its own if it has none. From `{.appendix}` on,
    // every `#` starts its text with "Appendix A" (in the text, the contents and the bookmarks), and the numbers below it
    // with the letter: `A.1`.
    private HeadingBlock Heading(Markdig.Syntax.HeadingBlock heading, List<TextRun> text, bool unnumbered, bool newPage, bool appendix)
    {
        var chapter = !HeadingNumbering.InLayoutContainer(heading);
        if (appendix && chapter && !this.appendices)
        {
            this.appendices = true;
            this.numbering?.StartAppendices();
        }

        var number = chapter && !unnumbered ? this.numbering?.Next(heading.Level)?.Number : null;
        if (this.appendices && chapter && !unnumbered && heading.Level == 1)
        {
            var label = $"{texts.Appendix} {BackMatter.Letter(++this.appendixCount)}";
            text = [new TextRun(text.Count > 0 ? label + " " : label, RunStyle.None), .. text];
            number = null;
        }

        var block = new HeadingBlock(heading.Level, text, Anchor(heading))
        {
            Number = number,
            NewPage = newPage,
        };
        var level = heading.Level - this.top + 1;
        if (options.TableOfContents && chapter && level <= options.TableOfContentsDepth && text.Count > 0)
        {
            block = block.Anchor is null ? block with { Anchor = this.NewAnchor() } : block;
            this.contents.Add(new ContentsEntry(level, block));
        }

        return block;
    }

    // An anchor no heading of the document has.
    private string NewAnchor()
    {
        var n = 0;
        string anchor;
        do
        {
            anchor = string.Create(CultureInfo.InvariantCulture, $"contents-{++n}");
        }
        while (!this.anchors.Add(anchor));
        return anchor;
    }

    // The `Table:` or `Figure:` paragraph right after `block`, as its caption, numbered; that paragraph is then taken.
    private Caption? CaptionAfter(Markdig.Syntax.Block block, CaptionKind kind)
    {
        var parent = block.Parent;
        var index = parent?.IndexOf(block) ?? -1;
        if (parent is null || index < 0 || index + 1 >= parent.Count
            || parent[index + 1] is not Markdig.Syntax.ParagraphBlock paragraph || Captions.Kind(paragraph) != kind)
        {
            return null;
        }

        this.captionParagraphs.Add(paragraph);
        Captions.TakePrefix(paragraph, kind);
        return this.NewCaption(kind, this.Runs(paragraph.Inline));
    }

    private Caption NewCaption(CaptionKind kind, IReadOnlyList<TextRun> text)
    {
        var number = this.captions.GetValueOrDefault(kind) + 1;
        this.captions[kind] = number;
        return new Caption(kind, number, text);
    }

    // An item whose text starts with an icon: the icon, taken off the text, goes in place of the marker.
    private static Drawing? LeadingIcon(List<Block> children)
    {
        switch (children)
        {
            case [ParagraphBlock { Runs: [{ Icon: { } icon }, ..] } paragraph, ..]:
                children[0] = new ParagraphBlock(Trim([.. paragraph.Runs.Skip(1)]));
                return icon.Drawing;
            case [IconBlock alone, ..]:
                children[0] = new ParagraphBlock([]);
                return alone.Icon;
            default:
                return null;
        }
    }

    // `- [ ]` and `- [x]`: the box at the start of the item's first paragraph.
    private static bool? Task(ListItemBlock item) =>
        item.FirstOrDefault() is Markdig.Syntax.ParagraphBlock { Inline.FirstChild: TaskList task } ? task.Checked : null;

    // An item without a term (a second `:` line) adds its description to the item before.
    private DefinitionListBlock Definitions(DefinitionList list)
    {
        var items = new List<Definition>();
        foreach (var item in list.OfType<DefinitionItem>())
        {
            var terms = item.OfType<DefinitionTerm>().Select(t => (IReadOnlyList<TextRun>)this.Runs(t.Inline)).ToList();
            var description = new List<Block>();
            foreach (var child in item.Where(c => c is not DefinitionTerm))
            {
                this.Add(child, description);
            }

            if (terms.Count == 0 && items.Count > 0)
            {
                items[^1] = items[^1] with { Description = [.. items[^1].Description, .. description] };
            }
            else
            {
                items.Add(new Definition(terms, description));
            }
        }

        return new DefinitionListBlock(items);
    }

    // A code block, tabs as four spaces: a plain span per line, or with highlighting on the highlighted spans.
    private CodeBlock Code(Markdig.Syntax.CodeBlock code)
    {
        var text = code.Lines.ToString().Replace("\t", "    ", StringComparison.Ordinal);
        var language = (code as FencedCodeBlock)?.Info;
        return code is FencedCodeBlock fenced && highlighting?.Lines(language, text, this.LineOf(fenced)) is { } lines
            ? new CodeBlock(lines, language)
            : CodeBlock.Plain(text.Split('\n'), language);
    }

    private CodeBlockGraphic? Render(FencedCodeBlock fenced) =>
        options.CodeBlockRenderers.Count == 0 ? null : CodeBlockRenderers.Render(
            options.CodeBlockRenderers,
            new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), brand)
            {
                Line = this.LineOf(fenced),
                Report = (code, message) => notes.Add(code, this.LineOf(fenced), message),
                Texts = texts,
            },
            DocumentFormat.Pdf);

    private List<Block> Children(ContainerBlock container)
    {
        var children = new List<Block>();
        foreach (var child in container)
        {
            this.Add(child, children);
        }

        return children;
    }

    // Splits the paragraph at images: text before and after becomes paragraphs, each image a block of its own. A
    // paragraph of nothing but two or more images is one row of them.
    private void Paragraph(ContainerInline? inline, List<Block> target)
    {
        var collector = new RunCollector(this);
        collector.Add(inline, RunStyle.None, null);
        if (collector.Pieces.OfType<ImageBlock>().ToList() is { Count: >= 2 } images
            && !collector.Pieces.OfType<DrawingBlock>().Any()
            && collector.Pieces.OfType<List<TextRun>>().All(runs => Trim(runs).Count == 0))
        {
            foreach (var image in images.Where(i => i.Attributes is { Fit: ImageFit.Cover, Scale: not null }))
            {
                this.Ignored(image.Line, image.Attributes, "scale", "in a row, fit=cover gives the image the row's height");
            }

            target.Add(new ImageRowBlock(images));
            return;
        }

        foreach (var piece in collector.Pieces)
        {
            if (piece is ImageBlock image)
            {
                if (image.Attributes.Fit == ImageFit.Cover)
                {
                    this.Ignored(image.Line, image.Attributes, "fit=cover", "an image of its own in a PDF has no box to fill, so it is never cropped; put it in a row of images");
                }

                target.Add(image);
            }
            else if (piece is DrawingBlock qr)
            {
                target.Add(qr);
            }
            else if (piece is List<TextRun> runs && Trim(this.Icons(Colour(runs), this.LineOf(inline))) is { Count: > 0 } trimmed)
            {
                // A paragraph of one icon is the icon on its own, as a card's figure is on a slide.
                target.Add(trimmed is [{ Icon: { } icon }] ? new IconBlock(icon.Drawing) : new ParagraphBlock(trimmed));
            }
        }
    }

    // The attributes at the end of a heading: on `#` and `##` slide settings, `{.unnumbered}` and `{.newpage}`, checked
    // like in decks; on `###` to `######` only `{.unnumbered}` and `{.newpage}`, any other braces staying text.
    private static (List<TextRun> Text, HeadingAttributes? Attributes) Split(List<TextRun> runs, int level)
    {
        if (runs.Count == 0 || runs[^1] is not { IsBreak: false, Style: RunStyle.None, Link: null } last)
        {
            return (runs, null);
        }

        var (text, attributes) = level <= 2 ? HeadingAttributes.Split(last.Text, DocumentFormat.Pdf) : HeadingAttributes.SplitDeep(last.Text, DocumentFormat.Pdf);
        if (attributes is null)
        {
            return (runs, null);
        }

        return (text.Length > 0 ? [.. runs[..^1], last with { Text = text }] : runs[..^1], attributes);
    }

    // Paragraphs in italics, those of list items too.
    private static Block Italic(Block block) => block switch
    {
        ParagraphBlock paragraph => paragraph with { Runs = [.. paragraph.Runs.Select(r => r.IsBreak ? r : r with { Style = r.Style | RunStyle.Italic })] },
        ListBlock list => list with { Items = [.. list.Items.Select(item => (IReadOnlyList<Block>)[.. item.Select(Italic)])] },
        _ => block,
    };

    // A block's line in the original Markdown, from 1.
    private int LineOf(MarkdownObject block) => block.Line + 1 + lineOffset;

    private void HtmlAsText(MarkdownObject html, string text) => notes.Add(
        NoteCodes.HtmlAsText,
        this.LineOf(html),
        $"The HTML '{GenerationNotes.Quote(text)}' is shown as text. Write Markdown instead.");

    // Without typesetting (`typeset: false`), the caller typesets the runs once it has taken the heading attributes off.
    private List<TextRun> Runs(ContainerInline? inline, bool typeset = true)
    {
        var collector = new RunCollector(this, imagesAsText: true);
        collector.Add(inline, RunStyle.None, null);
        var runs = Colour(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList());
        var line = this.LineOf(inline);
        return Trim(typeset ? this.Icons(runs, line) : this.IconsOnly(runs, line));
    }

    // `:icon-name:`: an icon run of its own, unless icons are switched off; then typography, when it is on.
    private List<TextRun> Icons(List<TextRun> runs, int line) => this.Typeset(this.IconsOnly(runs, line), line);

    private List<TextRun> IconsOnly(List<TextRun> runs, int line) =>
        IconSyntax.Apply(
            runs,
            r => r.Text,
            r => r.Style.HasFlag(RunStyle.Code),
            (r, text) => r with { Text = text },
            (r, icon) => r with { Style = r.Style | RunStyle.Icon, Icon = icon },
            options.Icons,
            line,
            DocumentFormat.Pdf);

    // Typography, when it is on: code and footnote references are left alone.
    private List<TextRun> Typeset(List<TextRun> runs, int line) => typography is null ? runs : typography.Apply(
        runs,
        r => r.Text,
        r => r.Style.HasFlag(RunStyle.Code) || r.Style.HasFlag(RunStyle.Superscript),
        (r, text) => r with { Text = text },
        line);

    // Icons where PDFs do not draw them (headings, table cells, the cover): left out, with a note.
    private List<TextRun> NotDrawn(List<TextRun> runs, string place)
    {
        if (!runs.Any(r => r.Icon is not null))
        {
            return runs;
        }

        foreach (var icon in runs.Select(r => r.Icon).OfType<InlineIcon>())
        {
            notes.Add(
                NoteCodes.IconNotDrawn,
                icon.Line,
                $"The icon ':icon-{icon.Name}:' in {place} is left out: a PDF draws icons in text, alone in a paragraph and at the start of a list item, not in headings, table cells or on the cover.");
        }

        return Trim([.. runs.Where(r => r.Icon is null)]);
    }

    // The line of a paragraph's text, from its first inline; 0 if unknown.
    private int LineOf(ContainerInline? inline) => inline?.FirstChild is { } first ? this.LineOf(first) : 0;

    // `[words]{.success}`: the words bold, in that status colour.
    private static List<TextRun> Colour(List<TextRun> runs) => ColouredWords.Apply(
        runs,
        r => r.Text,
        r => r.Style.HasFlag(RunStyle.Code),
        (r, text) => r with { Text = text },
        (r, category) => r with { Style = r.Style | RunStyle.Bold, Colour = category },
        DocumentFormat.Pdf);

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

    private TableBlock Table(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var grid = rows
            .Select(row => (IReadOnlyList<IReadOnlyList<TextRun>>)Enumerable.Range(0, columns)
                .Select(c => c < row.Count ? this.Cell((TableCell)row[c]) : [])
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
        return new TableBlock(grid, alignments, rows.Count > 0 && rows[0].IsHeader);
    }

    // A Harvey ball is drawn like an icon in text: one em wide, in the cell's text colour, at the cell's alignment.
    private IReadOnlyList<TextRun> Cell(TableCell cell)
    {
        if (HarveyBall.Quarters(cell.Descendants<LeafBlock>()) is { } quarters)
        {
            return [new TextRun(string.Empty, RunStyle.Icon) { Icon = new InlineIcon($"harvey-{quarters}", HarveyBall.Drawing(quarters), this.LineOf(cell)) }];
        }

        var runs = new List<TextRun>();
        foreach (var leaf in cell.Descendants<LeafBlock>().Where(l => l.Inline is not null))
        {
            if (runs.Count > 0)
            {
                runs.Add(TextRun.Break);
            }

            runs.AddRange(this.Runs(leaf.Inline));
        }

        return this.NotDrawn(runs, "a table cell");
    }

    // An image, or for `qr:…` the QR code as a drawing.
    private Block Image(LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty;
        if (QrDrawing.IsQr(url))
        {
            var qrAttributes = ImageAttributes.Take(link, this.LineOf(link), DocumentFormat.Pdf);
            return new DrawingBlock(QrDrawing.ForImage(url, AltText(link), link.Title, qrAttributes, ResolvedBrand.Resolve(brand), this.LineOf(link), DocumentFormat.Pdf).Drawing);
        }

        var resource = document.FindResource(url)
            ?? throw new DocumentConversionException(DocumentFormat.Pdf, $"The image '{url}' is not among the document's resources.");
        var attributes = ImageAttributes.Take(link, this.LineOf(link), DocumentFormat.Pdf);
        double? titleScale = link.Title is { } title && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
        if (titleScale is not null && attributes.Scale is not null)
        {
            throw new DocumentConversionException(
                DocumentFormat.Pdf,
                $"The image '{url}' at line {this.LineOf(link)} has scale both in its title and in its attributes '{attributes.Text}'; keep one.");
        }

        var line = this.LineOf(link);
        if (attributes is { Focus: not null, Fit: ImageFit.Contain, Round: false })
        {
            this.Ignored(line, attributes, "focus", "the image is not cropped; add .round, or fit=cover in a row of images");
        }

        var caption = Captions.OfImage(link.Title, attributes.Caption, AltText(link)) is { } words ? this.NewCaption(CaptionKind.Figure, [new TextRun(words, RunStyle.None)]) : null;
        var image = this.ImageOf(resource, titleScale ?? attributes.Scale ?? 1.0) with { Attributes = attributes, Line = line, Caption = caption };
        return attributes.Grey ? this.Grey(image) : image;
    }

    // `.grey`: the pixels in grey, once per image however often it is used.
    private ImageBlock Grey(ImageBlock image)
    {
        if (!this.greyImages.TryGetValue(image.Resource, out var grey))
        {
            grey = GreyImages.Convert(image.Resource, image.Info.Format);
            this.greyImages[image.Resource] = grey;
        }

        return image with { Resource = grey };
    }

    // An image attribute that does nothing where it is.
    private void Ignored(int line, ImageAttributes attributes, string name, string reason) =>
        notes.Add(NoteCodes.AttributeIgnored, line, $"The image attribute {name} in '{attributes.Text}' has no effect: {reason}.");

    // In a heading or a table cell an image is its alt text, so its attributes do nothing.
    private void AsText(LinkInline link)
    {
        if (ImageAttributes.Take(link, this.LineOf(link), DocumentFormat.Pdf) is { IsEmpty: false } attributes)
        {
            notes.Add(NoteCodes.AttributeIgnored, this.LineOf(link), $"The image attributes '{attributes.Text}' have no effect: the image is shown as its alt text here.");
        }
    }

    // An image in another format, such as SVG, goes through the image converters: drawn at twice the size it is placed
    // at, and placed at its natural size, as the image itself would be.
    private ImageBlock ImageOf(DocumentResource resource, double scale)
    {
        if (ImageInfo.Read(resource.Content.Span) is { Format: ImageFormat.Png or ImageFormat.Jpeg } info)
        {
            return new ImageBlock(resource, info, scale);
        }

        if (ImageConverters.Convert(options.ImageConverters, resource, 2 * scale, DocumentFormat.Pdf) is var (image, width, height)
            && ImageInfo.Read(image.Content.Span) is { Format: ImageFormat.Png or ImageFormat.Jpeg } converted)
        {
            return new ImageBlock(image, converted with { Width = Math.Max(1, (int)Math.Round(width)), Height = Math.Max(1, (int)Math.Round(height)) }, scale);
        }

        throw new DocumentConversionException(DocumentFormat.Pdf, $"The image '{resource.Path}' is not a PNG or JPEG{(options.ImageConverters.Count == 0 ? string.Empty : ", and no image converter takes it")}.");
    }

    private static bool HasImage(Markdig.Syntax.ParagraphBlock paragraph) =>
        paragraph.Inline?.Descendants<LinkInline>().Any(l => l.IsImage) == true;

    private static string? Anchor(Markdig.Syntax.HeadingBlock heading) => heading.GetAttributes().Id;

    private static string AltText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    [GeneratedRegex(@"scale\s*=\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

    /// <summary>Collects runs, cut into pieces at images unless images count as their alt text.</summary>
    private sealed class RunCollector(MarkdownParser parser, bool imagesAsText = false)
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
                        var added = emphasis.DelimiterChar == '~' ? RunStyle.Strike : emphasis.DelimiterChar == '=' || emphasis.DelimiterCount >= 2 ? RunStyle.Bold : RunStyle.Italic;
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
                        // A run of its own, never joined with the text or another reference: the layout puts its note on its page.
                        this.current.Add(new TextRun(footnote.Footnote.Order.ToString(CultureInfo.InvariantCulture), style | RunStyle.Superscript, link) { Footnote = footnote.Footnote.Order });
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

            if (this.current.Count > 0 && this.current[^1] is { IsBreak: false, Footnote: null } last && last.Style == style && last.Link == link)
            {
                this.current[^1] = last with { Text = last.Text + text };
            }
            else
            {
                this.current.Add(new TextRun(text, style, link));
            }
        }
    }
}
