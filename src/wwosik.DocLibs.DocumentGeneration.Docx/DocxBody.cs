using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Footnote = Markdig.Extensions.Footnotes.Footnote;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using W14 = DocumentFormat.OpenXml.Office2010.Word;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;
using WordDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The content of a Word document: every block of the Markdown body as Word paragraphs and tables in the template's
/// role styles. Headings are shifted so that the highest level after the cover is Heading 1, carry a bookmark, and a
/// later <c>#</c> starts a page; lists use the numbering (<see cref="DocxNumbering"/>), footnotes are real footnotes
/// (<see cref="DocxFootnotes"/>), captions are numbered by <c>SEQ</c> fields. Slide syntax is checked as in the PDF and
/// shown plainly.
/// </summary>
internal sealed partial class DocxBody
{
    // Pixels at 96 DPI, in EMU.
    private const long EmuPerPixel = 9525;

    // The table font, 9.5 pt, in hundredths of a point for TextMetrics; the cells' left and right margin, 4 pt.
    private const int TableSize = 950;
    private const int CellMargin = 80;

    private readonly Dictionary<HeadingBlock, string> bookmarks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<(OpenXmlPart, DocumentResource), (string Id, ImageInfo Info, string? Svg)> images = [];
    private readonly Dictionary<CaptionKind, int> captions = [];
    private readonly List<TocEntry> headings = [];
    private readonly List<int> breaks = [];
    private DocxNumbering numbering = null!;
    private DocxFootnotes footnotes = null!;
    private DocxRuns runs = null!;
    private int shift;
    private HeadingNumbering numbers = null!;
    private int drawingId;
    private int bookmarkId;
    private int controlId;

    private DocxBody(DocxContext context)
    {
        this.Context = context;
    }

    /// <summary>A new writer for each document: it keeps the document's numbers.</summary>
    public static IBodyWriter Instance => new Factory();

    public DocxContext Context { get; }

    private OpenXmlPart MainPart => this.Context.Main;

    /// <summary>The text of an image's alt text.</summary>
    public static string AltText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    /// <summary>The id of the Word footnote for <paramref name="footnote"/>, written on its reference: one per reference.</summary>
    public long FootnoteId(Footnote footnote)
    {
        var part = this.footnotes.Part;
        var frame = new Frame(DocxRoles.FootnoteText, part);
        var paragraphs = new List<OpenXmlElement>();
        this.Blocks([.. footnote], frame, paragraphs);
        var list = paragraphs.OfType<Paragraph>().ToList();
        if (list.Count == 0)
        {
            list.Add(this.Paragraph(DocxRoles.FootnoteText));
        }

        // The footnote's own number first, then a space.
        var first = list[0];
        var reference = new Run(new RunProperties(new RunStyle { Val = this.Context.Styles.Id(DocxRoles.FootnoteReference) }), new FootnoteReferenceMark());
        var space = DocxWriter.Run(" ");
        if (first.ParagraphProperties is { } properties)
        {
            first.InsertAfter(space, properties);
            first.InsertAfter(reference, properties);
        }
        else
        {
            first.PrependChild(space);
            first.PrependChild(reference);
        }

        return this.footnotes.Add(list);
    }

    /// <summary>
    /// Where a link goes: an absolute address stays one, <c>#id</c> becomes <c>#bookmark</c> of that heading;
    /// <see langword="null"/> for anything else, which stays text.
    /// </summary>
    public string? LinkTarget(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith('#'))
        {
            return this.anchors.TryGetValue(url[1..], out var bookmark) ? "#" + bookmark : null;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" or "ftp" ? url : null;
    }

    /// <summary>In a heading or a table cell an image is its alt text, so its attributes do nothing.</summary>
    public void ImageAsText(LinkInline link)
    {
        if (ImageAttributes.Take(link, this.LineOf(link), Format.Docx) is { IsEmpty: false } attributes)
        {
            this.Context.Notes.Add(NoteCodes.AttributeIgnored, this.LineOf(link), $"The image attributes '{attributes.Text}' have no effect: the image is shown as its alt text here.");
        }
    }

    public void HtmlAsText(MarkdownObject html, string text) => this.Context.Notes.Add(
        NoteCodes.HtmlAsText,
        this.LineOf(html),
        $"The HTML '{GenerationNotes.Quote(text)}' is shown as text. Write Markdown instead.");

    private (IReadOnlyList<OpenXmlElement> Elements, IReadOnlyList<TocEntry> Headings) WriteAll(IReadOnlyList<Block> blocks)
    {
        this.numbering = new DocxNumbering(this.Context.Main, this.Context.Styles);
        this.footnotes = new DocxFootnotes(this.Context.Main);
        this.runs = new DocxRuns(this);
        this.drawingId = MaxDrawingId(this.Context.Main);
        this.CheckSections(blocks);
        this.Bookmarks(blocks);

        // The highest level used after the cover becomes Heading 1; headings inside slide-layout containers are no chapters.
        this.shift = HeadingNumbering.Top(blocks) - 1;
        this.numbers = new HeadingNumbering(1);

        var elements = new List<OpenXmlElement>();
        this.Blocks(blocks, new Frame(DocxRoles.Normal, this.MainPart), elements);
        this.PageBreaksBefore(elements);
        this.numbering.Save();
        this.footnotes.Save();
        return (elements, this.headings);
    }

    // `::: pagebreak`: where the block it comes before begins in the body, that element starts a page. Not the first
    // element: the cover and the table of contents already end their pages, so that would give an empty page.
    private void MarkBreak(Block block, Frame frame, List<OpenXmlElement> target)
    {
        if (frame.Part == this.MainPart && this.Context.PageBreaks.Contains(block))
        {
            this.breaks.Add(target.Count);
        }
    }

    private void PageBreaksBefore(List<OpenXmlElement> elements)
    {
        foreach (var index in this.breaks.Where(i => i > 0 && i < elements.Count).Distinct())
        {
            var paragraph = elements[index] as Paragraph ?? elements[index].Descendants<Paragraph>().FirstOrDefault();
            if (paragraph is not null)
            {
                var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();
                properties.PageBreakBefore ??= new PageBreakBefore();
            }
        }
    }

    // The checks of slide kinds and of `::: conclusion` and `::: comment` are the deck's, per `#` or `##` section.
    private void CheckSections(IReadOnlyList<Block> blocks)
    {
        var sections = new List<(HeadingBlock? Heading, List<Block> Blocks)> { (null, []) };
        foreach (var block in blocks)
        {
            if (block is HeadingBlock { Level: <= 2 } heading)
            {
                sections.Add((heading, []));
            }
            else
            {
                sections[^1].Blocks.Add(block);
            }
        }

        foreach (var (heading, content) in sections)
        {
            SlideKinds.CheckContainers(content, Format.Docx);
            if (heading is null || this.HeadingParts(heading).Attributes?.Kind is not { } kind)
            {
                continue;
            }

            if (heading.Level == 1)
            {
                throw new DocumentConversionException(Format.Docx, $"{{.{kind}}} goes on a '##' heading: a '#' heading is the cover or a section divider.");
            }

            SlideKinds.Check(kind, content, DocxRuns.Text(this.HeadingParts(heading).Runs).Trim(), Format.Docx);
        }
    }

    // Every heading's bookmark, made from its Markdig id: letters, digits and `_`, at most 40 characters, unique.
    private void Bookmarks(IReadOnlyList<Block> blocks)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var heading in blocks.SelectMany(b => b is HeadingBlock h ? [h] : b is ContainerBlock c ? c.Descendants<HeadingBlock>() : []))
        {
            if (heading.GetAttributes().Id is not { Length: > 0 } id)
            {
                continue;
            }

            var name = UnsafeCharacters().Replace(id, "_");
            if (!char.IsAsciiLetter(name[0]))
            {
                name = "h_" + name;
            }

            name = name.Length > 40 ? name[..40] : name;
            var unique = name;
            for (var n = 2; !used.Add(unique); n++)
            {
                var suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
                unique = (name.Length + suffix.Length > 40 ? name[..(40 - suffix.Length)] : name) + suffix;
            }

            this.bookmarks[heading] = unique;
            this.anchors.TryAdd(id, unique);
        }
    }

    // A sequence of sibling blocks: a bold paragraph right before a table is its title, a `Table:` paragraph right after
    // it its caption; any other `Table:` or `Figure:` paragraph stays text.
    private void Blocks(IReadOnlyList<Block> blocks, Frame frame, List<OpenXmlElement> target)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            this.MarkBreak(block, frame, target);
            if (block is ParagraphBlock title && IsTableTitle(title) && i + 1 < blocks.Count && blocks[i + 1] is MarkdigTable)
            {
                var table = (MarkdigTable)blocks[i + 1];
                var caption = CaptionAfter(blocks, i + 1);
                this.Table(table, title, caption, frame, target);
                i += caption is null ? 1 : 2;
                continue;
            }

            if (block is MarkdigTable alone)
            {
                var caption = CaptionAfter(blocks, i);
                this.Table(alone, null, caption, frame, target);
                i += caption is null ? 0 : 1;
                continue;
            }

            // A `Figure:` paragraph right after a fenced block that was drawn is its caption, below it.
            if (block is FencedCodeBlock fenced)
            {
                var figure = i + 1 < blocks.Count && blocks[i + 1] is ParagraphBlock next && Captions.Kind(next) == CaptionKind.Figure ? next : null;
                if (this.Code(fenced, frame, target, figure is not null) && figure is not null)
                {
                    Captions.TakePrefix(figure, CaptionKind.Figure);
                    target.Add(this.Caption(CaptionKind.Figure, this.runs.Runs(figure.Inline), frame.Part));
                    i++;
                }

                continue;
            }

            if (block is ParagraphBlock misplaced && Captions.Kind(misplaced) is { } kind)
            {
                this.Context.Notes.Add(
                    NoteCodes.CaptionIgnored,
                    this.LineOf(misplaced),
                    Captions.Ignored(kind));
            }

            this.Add(block, frame, target);
        }
    }

    private static ParagraphBlock? CaptionAfter(IReadOnlyList<Block> blocks, int table) =>
        table + 1 < blocks.Count && blocks[table + 1] is ParagraphBlock paragraph && Captions.Kind(paragraph) == CaptionKind.Table ? paragraph : null;

    // A paragraph of nothing but bold text.
    private static bool IsTableTitle(ParagraphBlock paragraph)
    {
        var inlines = paragraph.Inline?.Where(i => !(i is LiteralInline literal && literal.Content.ToString().Trim().Length == 0)).ToList();
        return inlines is [EmphasisInline { DelimiterChar: '*' or '_', DelimiterCount: 2 }];
    }

    private void Add(Block block, Frame frame, List<OpenXmlElement> target)
    {
        switch (block)
        {
            case HeadingBlock heading when frame.Container is not null:
                this.ContainerHeading(heading, frame, target);
                break;
            case HeadingBlock heading:
                this.Heading(heading, target);
                break;
            case CustomContainer { Info: LayoutSyntax.Notes }:
                throw LayoutSyntax.NestedNotes(Format.Docx);
            case CustomContainer container:
                LayoutSyntax.CheckContainer(container.Info ?? string.Empty, container.Arguments?.Trim() ?? string.Empty, Format.Docx);
                this.Container(container, frame, target);
                break;
            case AlertBlock alert:
                this.Alert(alert, frame, target);
                break;
            case ParagraphBlock marker when BuildMarker.IsMarker(marker):
                break;
            case ParagraphBlock paragraph when Tags(paragraph) is { } tags:
                // Code spans and nothing else: chips, separated by spaces.
                target.Add(this.Paragraph(frame, [.. tags.SelectMany((t, i) => i == 0
                    ? new[] { new DocxRun(t, RunFlags.Tag, null) }
                    : [new DocxRun(" ", RunFlags.None, null), new DocxRun(t, RunFlags.Tag, null)])]));
                break;
            case ParagraphBlock paragraph:
                this.Paragraph(paragraph.Inline, frame, target);
                break;
            case ListBlock list:
                this.List(list, 0, frame, target);
                break;
            case DefinitionList definitions:
                this.Definitions(definitions, frame, target);
                break;
            case QuoteBlock quoted:
                // Nested quotes are flattened into the one quote style.
                this.Blocks([.. quoted], frame with { Style = DocxRoles.Quote, Indent = null }, target);
                break;
            case MarkdigTable table:
                this.Table(table, null, null, frame, target);
                break;
            case CodeBlock code:
                this.Code(code, frame, target, captioned: false);
                break;
            case ThematicBreakBlock:
                target.Add(this.Paragraph(DocxRoles.Rule));
                break;
            case HtmlBlock { Type: HtmlBlockType.Comment }:
            case LinkReferenceDefinitionGroup:
            case FootnoteGroup:
                break;
            case HtmlBlock html:
                this.HtmlAsText(html, html.Lines.ToString());
                foreach (var line in html.Lines.ToString().Split('\n').Where(l => l.Trim().Length > 0))
                {
                    target.Add(this.Paragraph(frame, [new DocxRun(line.Trim(), RunFlags.None, null)]));
                }

                break;
            case LeafBlock { Inline: { } inline }:
                this.Paragraph(inline, frame, target);
                break;
            case ContainerBlock container:
                this.Blocks([.. container], frame, target);
                break;
        }
    }

    // The heading's runs and its attributes: slide settings on `#` and `##` (checked as in decks), `.unnumbered` on any
    // level; on `###` to `######` other braces stay text.
    private (List<DocxRun> Runs, HeadingAttributes? Attributes) HeadingParts(HeadingBlock heading)
    {
        var runs = this.runs.Runs(heading.Inline);
        if (runs.Count == 0 || runs[^1] is not { IsBreak: false, Footnote: null, Flags: RunFlags.None, Link: null } last)
        {
            return (runs, null);
        }

        var (text, attributes) = heading.Level <= 2 ? HeadingAttributes.Split(last.Text, Format.Docx) : HeadingAttributes.SplitDeep(last.Text, Format.Docx);
        if (attributes is null)
        {
            return (runs, null);
        }

        return (DocxRuns.Trim(text.Length > 0 ? [.. runs[..^1], last with { Text = text }] : runs[..^1]), attributes);
    }

    private void Heading(HeadingBlock heading, List<OpenXmlElement> target)
    {
        var (runs, attributes) = this.HeadingParts(heading);

        // A kind of slide without a title, such as `## {.quote}`, has no heading here.
        if (runs.Count == 0 && attributes?.Kind is not null)
        {
            return;
        }

        var level = Math.Clamp(heading.Level - this.shift, 1, 6);
        var unnumbered = attributes?.Unnumbered == true;
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = this.Context.Styles.Id(DocxRoles.Headings[level - 1]) });

        // A later `#` starts a page, as in the PDF, and so does `{.newpage}`; the first content already does.
        if ((heading.Level == 1 || attributes?.NewPage == true) && target.Count > 0)
        {
            properties.Append(new PageBreakBefore());
        }

        var number = unnumbered || !this.Context.HeadingNumbers ? null : this.numbers.Next(level);
        if (unnumbered)
        {
            properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 0 }));
        }
        else if (number is { } counted && counted.Depth != level)
        {
            // A level skipped above it: Word counts the number's own level, so that it never shows a 0 (`0.1`, `1.0.1`).
            properties.Append(new NumberingProperties(new NumberingLevelReference { Val = counted.Depth - 1 }, new NumberingId { Val = DocxNumbering.HeadingNumId }));
        }

        var paragraph = new Paragraph(properties);
        var bookmark = this.Bookmarked(paragraph, heading, runs);
        target.Add(paragraph);
        this.headings.Add(new TocEntry(level, DocxRuns.Text(runs).Trim()) { Number = number?.Number, Bookmark = bookmark });
    }

    // The heading's runs inside its bookmark, if it has one; returns the bookmark.
    private string? Bookmarked(Paragraph paragraph, HeadingBlock heading, IReadOnlyList<DocxRun> runs)
    {
        var bookmark = this.bookmarks.GetValueOrDefault(heading);
        var id = (this.bookmarkId++).ToString(CultureInfo.InvariantCulture);
        if (bookmark is not null)
        {
            paragraph.Append(new BookmarkStart { Id = id, Name = bookmark });
        }

        paragraph.Append(this.runs.Elements(runs, this.MainPart));
        if (bookmark is not null)
        {
            paragraph.Append(new BookmarkEnd { Id = id });
        }

        return bookmark;
    }

    // A heading inside a slide-layout container, such as a card's `###`: no chapter, so not numbered and not in the table
    // of contents. In a panel it is a bold paragraph of the panel, so that the box stays one.
    private void ContainerHeading(HeadingBlock heading, Frame frame, List<OpenXmlElement> target)
    {
        var (runs, attributes) = this.HeadingParts(heading);
        if (runs.Count == 0)
        {
            return;
        }

        if (frame.Style == DocxRoles.Panel || frame.Style == DocxRoles.PanelDark)
        {
            target.Add(this.Paragraph(frame, [.. runs.Select(r => r with { Flags = r.Flags | RunFlags.Bold })]));
            return;
        }

        var paragraph = this.Paragraph(DocxRoles.ContainerHeading);
        if (attributes?.NewPage == true && target.Count > 0)
        {
            paragraph.ParagraphProperties!.PageBreakBefore = new PageBreakBefore();
        }

        this.Bookmarked(paragraph, heading, runs);
        target.Add(paragraph);
    }

    // The layouts of slides in Word, written in sequence (E09-S02): cards as container headings with their figures, or
    // as terms and descriptions; rows as terms and descriptions; panels as shaded boxes; a statement's first paragraph
    // large; columns one after the other. Other containers (`conclusion`, `comment`, drawn ones) show their content.
    private void Container(CustomContainer container, Frame frame, List<OpenXmlElement> target)
    {
        var name = container.Info ?? string.Empty;
        var inner = frame with { Container = name };
        switch (name)
        {
            case "cards":
                if (!this.TermList(container, inner, target))
                {
                    this.Cards(container, inner, target);
                }

                break;
            case "rows":
                if (!this.TermList(container, inner, target))
                {
                    this.Blocks([.. container], inner, target);
                }

                break;
            case "panel":
                var panel = container.Arguments?.Trim() == "dark" ? DocxRoles.PanelDark : DocxRoles.Panel;
                this.Blocks([.. container], inner with { Style = panel, Indent = null, SuppressNumbering = false }, target);
                break;
            case "statement" when container.FirstOrDefault() is ParagraphBlock lead:
                this.MarkBreak(lead, inner, target);
                this.Paragraph(lead.Inline, inner with { Style = DocxRoles.Statement, Indent = null, SuppressNumbering = false }, target);
                this.Blocks([.. container.Skip(1)], inner, target);
                break;
            case "columns":
                // The `---` between the columns only separates them.
                this.Blocks([.. container.Where(c => c is not ThematicBreakBlock)], inner, target);
                break;
            case var visual when VisualContainers.IsVisual(visual):
                if (!this.VisualContainer(container, frame, target))
                {
                    this.Blocks([.. container], inner, target);
                }

                break;
            default:
                this.Blocks([.. container], inner, target);
                break;
        }
    }

    // `### Title` before each card's blocks: a container heading, and a paragraph of only bold text right after it, the
    // card's figure, in the statement style.
    private void Cards(CustomContainer container, Frame frame, List<OpenXmlElement> target)
    {
        var pending = new List<Block>();
        var children = container.ToList();
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not HeadingBlock heading)
            {
                pending.Add(children[i]);
                continue;
            }

            this.Blocks(pending, frame, target);
            pending.Clear();
            this.MarkBreak(heading, frame, target);
            this.ContainerHeading(heading, frame, target);
            if (i + 1 < children.Count && children[i + 1] is ParagraphBlock figure && IsTableTitle(figure))
            {
                this.MarkBreak(figure, frame, target);
                var runs = this.runs.Runs(figure.Inline).Select(r => r with { Flags = r.Flags & ~RunFlags.Bold }).ToList();
                target.Add(this.Paragraph(frame with { Style = DocxRoles.Statement, Indent = null, SuppressNumbering = false }, runs));
                i++;
            }
        }

        this.Blocks(pending, frame, target);
    }

    // A container that holds one list of `- **Title** – text` items, as the short form of cards and as rows: each item a
    // term, the rest of the item its description. False, writing nothing, when the container is not that list.
    private bool TermList(CustomContainer container, Frame frame, List<OpenXmlElement> target)
    {
        if (container.Count != 1 || container[0] is not ListBlock list)
        {
            return false;
        }

        var items = list.OfType<ListItemBlock>().Select(item => (Item: item, Parts: this.TermOf(item))).ToList();
        if (items.Count == 0 || items.Any(i => i.Parts is null))
        {
            return false;
        }

        this.MarkBreak(list, frame, target);
        var term = frame with { Style = DocxRoles.DefinitionTerm, Indent = null, SuppressNumbering = false };
        var description = frame with { Style = DocxRoles.Definition, Indent = null, SuppressNumbering = false };
        foreach (var (item, parts) in items)
        {
            this.MarkBreak(item, frame, target);
            target.Add(this.Paragraph(term, parts!.Value.Term));
            if (parts.Value.Text.Count > 0)
            {
                target.Add(this.Paragraph(description, parts.Value.Text));
            }

            this.Blocks([.. item.Skip(1)], description, target);
        }

        return true;
    }

    // The bold start of an item's first paragraph, without the bold (the term style is bold) and a trailing colon, and the
    // rest of that paragraph without its separator (`–`, `—`, `-` or `:`); null when the item does not start bold.
    private (List<DocxRun> Term, List<DocxRun> Text)? TermOf(ListItemBlock item)
    {
        if (item.FirstOrDefault() is not ParagraphBlock paragraph)
        {
            return null;
        }

        var runs = this.runs.Runs(paragraph.Inline);
        var bold = runs.TakeWhile(r => r is { IsBreak: false, Footnote: null } && r.Flags.HasFlag(RunFlags.Bold)).ToList();
        if (bold.Count == 0)
        {
            return null;
        }

        var term = bold.Select(r => r with { Flags = r.Flags & ~RunFlags.Bold }).ToList();
        term[^1] = term[^1] with { Text = term[^1].Text.TrimEnd().TrimEnd(':').TrimEnd() };
        var rest = runs.Skip(bold.Count).ToList();
        if (rest.Count > 0 && rest[0] is { IsBreak: false, Footnote: null, Flags: RunFlags.None })
        {
            rest[0] = rest[0] with { Text = SeparatorPattern().Replace(rest[0].Text, string.Empty) };
        }

        return (DocxRuns.Trim(term), DocxRuns.Trim(rest));
    }

    // An alert: every paragraph in the kind's style, so that the bar on the left runs through; the first paragraph starts
    // with the title in the document language, in Word's Strong. Lists inside keep their style and get the bar directly.
    private void Alert(AlertBlock alert, Frame frame, List<OpenXmlElement> target)
    {
        var kind = alert.Kind.ToString();
        LayoutSyntax.IsImportant(kind, Format.Docx);
        var style = DocxRoles.Alerts[kind];
        var inner = frame with { Style = style, Indent = null, SuppressNumbering = false, Box = style };
        List<DocxRun> title = [new DocxRun(this.Context.Texts.Alert(kind), RunFlags.Strong, null)];
        var children = alert.ToList();
        if (children.FirstOrDefault() is ParagraphBlock first && Tags(first) is null && !BuildMarker.IsMarker(first))
        {
            this.MarkBreak(first, inner, target);
            this.Paragraph(first.Inline, inner, target, [.. title, new DocxRun(" ", RunFlags.None, null)]);
            children.RemoveAt(0);
        }
        else
        {
            target.Add(this.Paragraph(inner, title));
        }

        this.Blocks(children, inner, target);
    }

    // Two or more code spans and nothing but spaces between them: a paragraph of tags, as on slides.
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

    // Paragraphs of other styles inside an alert (list items) take its bar and shading directly.
    private void Boxed(Paragraph paragraph, Frame frame)
    {
        if (frame.Box is not { } box || frame.Style == box || this.Context.Styles.Style(box).StyleParagraphProperties is not { } style)
        {
            return;
        }

        var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();
        if (style.ParagraphBorders is { } borders)
        {
            properties.ParagraphBorders = (ParagraphBorders)borders.CloneNode(true);
        }

        if (style.Shading is { } shading)
        {
            properties.Shading = (Shading)shading.CloneNode(true);
        }
    }

    // Text before and after images becomes paragraphs of their own style; each image is a centred paragraph. A paragraph
    // of nothing but two or more images is a row of them. `prefix` goes before the first text (an alert's title).
    private void Paragraph(ContainerInline? inline, Frame frame, List<OpenXmlElement> target, IReadOnlyList<DocxRun>? prefix = null)
    {
        var pieces = this.runs.Pieces(inline);
        if (prefix is null && ImageRowOf(pieces) is { } row)
        {
            this.ImageRow(row, frame, target);
            return;
        }

        foreach (var piece in pieces)
        {
            if (piece is LinkInline image)
            {
                if (prefix is not null)
                {
                    target.Add(this.Paragraph(frame, DocxRuns.Trim([.. prefix])));
                    prefix = null;
                }

                this.Image(image, frame, target);
            }
            else if (piece is List<DocxRun> runs && DocxRuns.Trim(prefix is null ? runs : [.. prefix, .. runs]) is { Count: > 0 } trimmed)
            {
                prefix = null;
                target.Add(this.Paragraph(frame, trimmed));
            }
        }

        if (prefix is not null)
        {
            target.Add(this.Paragraph(frame, DocxRuns.Trim([.. prefix])));
        }
    }

    // Two or more images and only spaces between them; `qr:` images are not pictures yet, so they break a row.
    private static List<LinkInline>? ImageRowOf(List<object> pieces)
    {
        var images = pieces.OfType<LinkInline>().ToList();
        return images.Count >= 2
            && images.All(i => !QrDrawing.IsQr(i.GetDynamicUrl?.Invoke() ?? i.Url ?? string.Empty))
            && pieces.OfType<List<DocxRun>>().All(r => DocxRuns.Trim(r).Count == 0)
            ? images
            : null;
    }

    private Paragraph Paragraph(Frame frame, IReadOnlyList<DocxRun> runs)
    {
        var paragraph = this.Paragraph(frame.Style);
        if (frame.Indent is { } indent)
        {
            // A paragraph that continues a list item: the item's indent, no marker.
            var properties = paragraph.ParagraphProperties!;
            if (frame.SuppressNumbering)
            {
                properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 0 }));
            }

            properties.Append(new Indentation { Left = indent.ToString(CultureInfo.InvariantCulture) });
        }

        this.Boxed(paragraph, frame);
        paragraph.Append(this.runs.Elements(runs, frame.Part));
        return paragraph;
    }

    private Paragraph Paragraph(StyleRole style) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = this.Context.Styles.Id(style) }));

    private void List(ListBlock list, int depth, Frame frame, List<OpenXmlElement> target)
    {
        var level = Math.Min(depth, 2);
        if (depth == 3)
        {
            this.Context.Notes.Add(
                NoteCodes.ListTooDeep,
                this.LineOf(list),
                "The list is nested deeper than three levels; its items are set at the third level. Flatten it to three levels.");
        }

        var style = list.IsOrdered ? DocxRoles.ListNumber : DocxRoles.ListBullet;
        var start = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        var numId = list.IsOrdered ? this.numbering.Ordered(level, start) : this.numbering.Bullet();
        var indent = this.numbering.Indent(numId, level);
        foreach (var item in list.OfType<ListItemBlock>())
        {
            this.MarkBreak(item, frame, target);
            var children = item.ToList();
            var task = Task(item);
            var itemStyle = task is null ? style : DocxRoles.ListTask;
            var continuation = frame with { Style = itemStyle, Indent = indent, SuppressNumbering = this.numbering.StyleNumbers(itemStyle) };

            // The item's first paragraph carries the marker (or the check box); an item that starts otherwise gets an empty one.
            var first = children.FirstOrDefault() as ParagraphBlock;
            var pieces = first is null ? [new List<DocxRun>()] : this.runs.Pieces(first.Inline);
            var lead = pieces.FirstOrDefault() as List<DocxRun> ?? [];

            // In a container, an item that starts with a code span starts with it as a tag, as on slides.
            if (frame.Container is not null && first?.Inline?.FirstChild is CodeInline && lead is [{ Flags: var flags } span, ..] && flags.HasFlag(RunFlags.Code))
            {
                lead[0] = span with { Flags = (flags & ~RunFlags.Code) | RunFlags.Tag };
            }

            var marker = this.Paragraph(itemStyle);
            var properties = marker.ParagraphProperties!;
            if (task is { } done)
            {
                properties.Append(new Indentation { Left = indent.ToString(CultureInfo.InvariantCulture), Hanging = DocxNumbering.LevelIndent.ToString(CultureInfo.InvariantCulture) });
                marker.Append(this.CheckBox(done), new Run(new TabChar()));
            }
            else
            {
                properties.Append(new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = numId }));
            }

            this.Boxed(marker, continuation);

            marker.Append(this.runs.Elements(DocxRuns.Trim(lead), frame.Part));
            target.Add(marker);
            foreach (var piece in pieces.Skip(1))
            {
                if (piece is LinkInline image)
                {
                    this.Image(image, continuation, target);
                }
                else if (piece is List<DocxRun> more && DocxRuns.Trim(more) is { Count: > 0 } trimmed)
                {
                    target.Add(this.Paragraph(continuation, trimmed));
                }
            }

            foreach (var child in children.Skip(first is null ? 0 : 1))
            {
                if (child is ListBlock nested)
                {
                    this.List(nested, depth + 1, frame, target);
                }
                else
                {
                    this.Blocks([child], continuation, target);
                }
            }
        }
    }

    // `- [ ]` and `- [x]`: the box at the start of the item's first paragraph.
    private static bool? Task(ListItemBlock item) =>
        item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList task } ? task.Checked : null;

    // A check box content control Word ticks on a click: ☐ and ☒ in a symbol font.
    private SdtRun CheckBox(bool done)
    {
        static RunFonts Symbol() => new() { Ascii = HarveyBall.SymbolFont, HighAnsi = HarveyBall.SymbolFont, EastAsia = HarveyBall.SymbolFont, ComplexScript = HarveyBall.SymbolFont };
        return new SdtRun(
            new SdtProperties(
                new SdtId { Val = ++this.controlId },
                new W14.SdtContentCheckBox(
                    new W14.Checked { Val = done ? W14.OnOffValues.One : W14.OnOffValues.Zero },
                    new W14.CheckedState { Font = HarveyBall.SymbolFont, Val = "2612" },
                    new W14.UncheckedState { Font = HarveyBall.SymbolFont, Val = "2610" })),
            new SdtContentRun(new Run(new RunProperties(Symbol()), new Text(done ? "☒" : "☐"))));
    }

    // Each term a bold paragraph kept with its description; descriptions indented.
    private void Definitions(DefinitionList list, Frame frame, List<OpenXmlElement> target)
    {
        foreach (var item in list.OfType<DefinitionItem>())
        {
            this.MarkBreak(item, frame, target);
            foreach (var child in item)
            {
                if (child is DefinitionTerm term)
                {
                    target.Add(this.Paragraph(frame with { Style = DocxRoles.DefinitionTerm, Indent = null }, this.runs.Runs(term.Inline)));
                }
                else
                {
                    this.Blocks([child], frame with { Style = DocxRoles.Definition, Indent = null }, target);
                }
            }
        }
    }

    // A fenced block a renderer draws becomes a chart or a picture (DocxBody.Graphics.cs); every other block, and a
    // drawing without an image converter, is one paragraph per line in the code style, tabs as four spaces. Returns
    // whether the block was drawn.
    private bool Code(CodeBlock code, Frame frame, List<OpenXmlElement> target, bool captioned)
    {
        if (code is FencedCodeBlock fenced && this.Graphic(fenced, frame, target, captioned))
        {
            return true;
        }

        this.CodeLines(code, target);
        return false;
    }

    private void CodeLines(CodeBlock code, List<OpenXmlElement> target)
    {
        foreach (var line in code.Lines.ToString().Replace("\t", "    ", StringComparison.Ordinal).Split('\n'))
        {
            var paragraph = this.Paragraph(DocxRoles.Code);
            if (line.TrimEnd('\r').Length > 0)
            {
                paragraph.Append(DocxWriter.Run(line.TrimEnd('\r')));
            }

            target.Add(paragraph);
        }
    }

    private void Table(MarkdigTable table, ParagraphBlock? title, ParagraphBlock? caption, Frame frame, List<OpenXmlElement> target)
    {
        if (title is not null)
        {
            // The title style is bold already.
            var titleRuns = this.runs.Runs(title.Inline).Select(r => r with { Flags = r.Flags & ~RunFlags.Bold }).ToList();
            target.Add(this.Paragraph(frame with { Style = DocxRoles.TableTitle, Indent = null }, titleRuns));
        }

        if (caption is not null)
        {
            Captions.TakePrefix(caption, CaptionKind.Table);
            var paragraph = this.Caption(CaptionKind.Table, this.runs.Runs(caption.Inline), frame.Part);
            paragraph.ParagraphProperties!.Append(new KeepNext());
            target.Add(paragraph);
        }

        var rows = table.OfType<MarkdigTableRow>().ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (columns == 0)
        {
            return;
        }

        var header = rows[0].IsHeader;
        var cells = rows.Select(row => Enumerable.Range(0, columns)
            .Select(c => c < row.Count ? this.Cell((MarkdigTableCell)row[c], labels: !row.IsHeader) : [])
            .ToList()).ToList();

        // A row whose first cell is all `==…==` is highlighted: its cells shaded in the mark's tint, the one direct
        // formatting of a table, since a cell's shading cannot come from a character or paragraph style.
        var highlighted = cells.Select((row, r) => !rows[r].IsHeader && row.Count > 0 && row[0].Any(x => !x.IsBreak)
            && row[0].All(x => x.IsBreak || x.Flags.HasFlag(RunFlags.Mark))).ToList();
        var widths = ColumnWidths(cells, header, this.Context.Page.TextWidth);
        var alignments = Enumerable.Range(0, columns)
            .Select(c => c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null)
            .Select(a => a switch
            {
                Markdig.Extensions.Tables.TableColumnAlign.Center => JustificationValues.Center,
                Markdig.Extensions.Tables.TableColumnAlign.Right => JustificationValues.Right,
                Markdig.Extensions.Tables.TableColumnAlign.Left => JustificationValues.Left,
                _ => (JustificationValues?)null,
            })
            .ToList();

        var result = new Table(
            new TableProperties(
                new TableStyle { Val = this.Context.Styles.Id(DocxRoles.Table) },
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableLook { Val = header ? "04A0" : "0480", FirstRow = header, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true }),
            new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));

        var pageHeight = this.Context.Page.Height - this.Context.Page.Top - this.Context.Page.Bottom;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new TableRow();
            var rowProperties = new TableRowProperties();

            // A row stays on one page, unless it is taller than one.
            var lines = cells[r].Select((runs, c) => TextMetrics.Lines(Measured(runs, header && r == 0), TableSize, (widths[c] - (2 * CellMargin)) * DocxPage.EmuPerTwip)).Max();
            if ((lines * TableSize / 100.0 * 1.2 * 20) + (2 * CellMargin) < pageHeight)
            {
                rowProperties.Append(new CantSplit());
            }

            if (header && r == 0)
            {
                rowProperties.Append(new TableHeader());
            }

            if (rowProperties.HasChildren)
            {
                row.Append(rowProperties);
            }

            for (var c = 0; c < columns; c++)
            {
                var properties = new ParagraphProperties(new ParagraphStyleId { Val = this.Context.Styles.Id(DocxRoles.Normal) });
                if (alignments[c] is { } alignment)
                {
                    properties.Append(new Justification { Val = alignment });
                }

                var paragraph = new Paragraph(properties);
                paragraph.Append(this.runs.Elements(cells[r][c], frame.Part));
                var cellProperties = new TableCellProperties(new TableCellWidth { Width = widths[c].ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });
                if (highlighted[r])
                {
                    cellProperties.Shading = this.HighlightShading();
                }

                row.Append(new TableCell(cellProperties, paragraph));
            }

            result.Append(row);
        }

        target.Add(result);
    }

    private Shading HighlightShading()
    {
        var tint = (this.Context.Design ?? new StyleDesign(ResolvedBrand.Neutral, 0, 0)).HighlightTint;
        var shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = tint.Hex };
        if (tint.Theme is { } theme)
        {
            shading.ThemeFill = new ThemeColorValues(theme);
            shading.ThemeFillTint = tint.Tint;
        }

        return shading;
    }

    // A cell's paragraphs as one run list, separated by breaks; images as their alt text. A body cell that is one code
    // span which front matter `labels.*` names is a status label; any other code stays code.
    private List<DocxRun> Cell(MarkdigTableCell cell, bool labels)
    {
        var leaves = cell.Descendants<LeafBlock>().Where(l => l.Inline is not null).ToList();
        if (labels && leaves is [{ Inline: { } only }]
            && only.Where(i => i is not LiteralInline literal || literal.Content.ToString().Trim().Length > 0).ToList() is [CodeInline code]
            && this.Context.Labels.TryGetValue(code.Content, out var category))
        {
            return [new DocxRun(code.Content, RunFlags.None, null) { Category = category, IsLabel = true }];
        }

        var runs = new List<DocxRun>();
        foreach (var leaf in leaves)
        {
            if (runs.Count > 0)
            {
                runs.Add(DocxRun.Break);
            }

            runs.AddRange(this.runs.Runs(leaf.Inline));
        }

        return runs;
    }

    private static IEnumerable<(string Text, bool Bold)> Measured(IEnumerable<DocxRun> runs, bool bold) =>
        runs.Select(r => (r.IsBreak ? TextMetrics.LineBreak : r.Text, bold || r.LooksBold));

    /// <summary>
    /// The PDF's rule, in twentieths of a point: every column gets at least its longest word, and the rest of
    /// <paramref name="available"/> is shared in proportion to the content. The widths add up to it exactly.
    /// </summary>
    internal static int[] ColumnWidths(IReadOnlyList<IReadOnlyList<IReadOnlyList<DocxRun>>> rows, bool header, int available)
    {
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var minimum = new double[columns];
        var natural = new double[columns];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                var pieces = Measured(rows[r][c], header && r == 0).ToList();
                minimum[c] = Math.Max(minimum[c], TextMetrics.LongestWord(pieces, TableSize) / DocxPage.EmuPerTwip);
                var line = 0.0;
                foreach (var (text, bold) in pieces)
                {
                    if (text == TextMetrics.LineBreak)
                    {
                        natural[c] = Math.Max(natural[c], line);
                        line = 0;
                    }
                    else
                    {
                        line += TextMetrics.Width(text, TableSize, bold) / DocxPage.EmuPerTwip;
                    }
                }

                natural[c] = Math.Max(natural[c], line);
            }
        }

        for (var c = 0; c < columns; c++)
        {
            minimum[c] += 2 * CellMargin;
            natural[c] = Math.Max(natural[c] + (2 * CellMargin), minimum[c]);
        }

        double[] widths;
        var spare = natural.Select((w, c) => w - minimum[c]).ToArray();
        if (minimum.Sum() >= available)
        {
            widths = [.. minimum.Select(m => m * available / minimum.Sum())];
        }
        else if (natural.Sum() <= available || spare.Sum() <= 0)
        {
            var extra = available - natural.Sum();
            widths = [.. natural.Select(w => w + (extra * w / natural.Sum()))];
        }
        else
        {
            var share = available - minimum.Sum();
            widths = [.. minimum.Select((m, c) => m + (share * spare[c] / spare.Sum()))];
        }

        var result = widths.Select(w => (int)Math.Floor(w)).ToArray();
        if (result.Length > 0)
        {
            result[^1] += available - result.Sum();
        }

        return result;
    }

    // "Table 1: text": the label and its SEQ field in bold, the field's result filled in, so it is right without an update.
    private Paragraph Caption(CaptionKind kind, IReadOnlyList<DocxRun> text, OpenXmlPart part)
    {
        var number = this.captions.GetValueOrDefault(kind) + 1;
        this.captions[kind] = number;
        var name = kind == CaptionKind.Table ? "Table" : "Figure";
        static Run Bold(OpenXmlElement content) => new(new RunProperties(new Bold()), content);
        var paragraph = this.Paragraph(DocxRoles.Caption);
        paragraph.Append(
            Bold(new Text(Captions.Label(kind, this.Context.Texts) + " ") { Space = SpaceProcessingModeValues.Preserve }),
            Bold(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            Bold(new FieldCode($" SEQ {name} \\* ARABIC ") { Space = SpaceProcessingModeValues.Preserve }),
            Bold(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            Bold(new Text(number.ToString(CultureInfo.InvariantCulture))),
            Bold(new FieldChar { FieldCharType = FieldCharValues.End }),
            Bold(new Text(":")),
            DocxWriter.Run(" "));
        paragraph.Append(this.runs.Elements(text, part));
        return paragraph;
    }

    private void Image(LinkInline link, Frame frame, List<OpenXmlElement> target)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty;
        if (QrDrawing.IsQr(url))
        {
            this.Qr(link, url, frame, target);
            return;
        }

        var (picture, caption) = this.Picture(link, this.Context.Page.TextWidth * DocxPage.EmuPerTwip, frame.Part);
        target.Add(this.PictureParagraph(frame, picture, keepNext: caption is not null));
        if (caption is not null)
        {
            target.Add(this.Caption(CaptionKind.Figure, [new DocxRun(caption, RunFlags.None, null)], frame.Part));
        }
    }

    // A paragraph of only images: a borderless table of one row that does not split, a cell of equal width per image,
    // each image fitted to its cell and its caption under it in the same cell.
    private void ImageRow(List<LinkInline> images, Frame frame, List<OpenXmlElement> target)
    {
        const int Margin = 60;
        var available = this.Context.Page.TextWidth;
        var widths = Enumerable.Repeat(available / images.Count, images.Count).ToArray();
        widths[^1] += available - widths.Sum();
        var table = new Table(
            new TableProperties(
                new TableStyle { Val = this.Context.Styles.Id(DocxRoles.ImageRow) },
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableLook { Val = "0000", FirstRow = false, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = true, NoVerticalBand = true }),
            new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));
        var row = new TableRow(new TableRowProperties(new CantSplit()));
        for (var i = 0; i < images.Count; i++)
        {
            var (picture, caption) = this.Picture(images[i], (widths[i] - (2 * Margin)) * DocxPage.EmuPerTwip, frame.Part);
            var paragraph = this.Paragraph(DocxRoles.Normal);
            paragraph.ParagraphProperties!.Append(new Justification { Val = JustificationValues.Center });
            paragraph.Append(picture);
            var cell = new TableCell(
                new TableCellProperties(new TableCellWidth { Width = widths[i].ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa }),
                paragraph);
            if (caption is not null)
            {
                cell.Append(this.Caption(CaptionKind.Figure, [new DocxRun(caption, RunFlags.None, null)], frame.Part));
            }

            row.Append(cell);
        }

        table.Append(row);
        target.Add(table);
    }

    // The picture of an image, fitted to `maxWidth` (EMU) and 80 % of the text height, and its caption, if it has one.
    private (Run Picture, string? Caption) Picture(LinkInline link, double maxWidth, OpenXmlPart part)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty;
        var line = this.LineOf(link);
        var alt = AltText(link);
        var attributes = ImageAttributes.Take(link, line, Format.Docx);
        var resource = this.Context.Document?.FindResource(url)
            ?? throw new DocumentConversionException(Format.Docx, $"The image '{url}' is not among the document's resources.");
        double? titleScale = link.Title is { } title && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
        if (titleScale is not null && attributes.Scale is not null)
        {
            throw new DocumentConversionException(
                Format.Docx,
                $"The image '{url}' at line {line} has scale both in its title and in its attributes '{attributes.Text}'; keep one.");
        }

        foreach (var (used, name) in new[] { (attributes.Fit == ImageFit.Cover, "fit=cover"), (attributes.Focus is not null, "focus"), (attributes.Round, ".round"), (attributes.Grey, ".grey") })
        {
            if (used)
            {
                this.Context.Notes.Add(NoteCodes.AttributeIgnored, line, $"The image attribute {name} in '{attributes.Text}' has no effect: a Word document shows the whole image as it is.");
            }
        }

        var scale = titleScale ?? attributes.Scale ?? 1.0;
        return (this.ResourcePicture(resource, scale, alt, maxWidth, part), Captions.OfImage(link.Title, attributes.Caption, alt));
    }

    // An image resource as an inline picture at its natural size times `scale`, fitted; an SVG keeps its SVG beside.
    private Run ResourcePicture(DocumentResource resource, double scale, string alt, double maxWidth, OpenXmlPart part)
    {
        var (id, info, svg) = this.ImagePart(resource, scale, part);
        var (cx, cy) = this.Fit(info.Width * scale, info.Height * scale, maxWidth);
        return new Run(this.Picture(id, cx, cy, alt, svg));
    }

    // A natural size in pixels at 96 DPI, in EMU, fitted to `maxWidth` (EMU) and 80 % of the text height, never enlarged.
    private (long Cx, long Cy) Fit(double widthInPixels, double heightInPixels, double maxWidth)
    {
        var width = widthInPixels * EmuPerPixel;
        var height = heightInPixels * EmuPerPixel;
        var fit = Math.Min(1.0, Math.Min(maxWidth / width, this.TextHeight * 0.8 / height));
        return (Math.Max(1, (long)(width * fit)), Math.Max(1, (long)(height * fit)));
    }

    // The height of the text area, in EMU.
    private double TextHeight => (this.Context.Page.Height - this.Context.Page.Top - this.Context.Page.Bottom) * DocxPage.EmuPerTwip;

    // PNG, JPEG and GIF as they are; other formats through the image converters at twice the size, as in the PDF. Each
    // image once per part, however often it is used.
    private (string Id, ImageInfo Info, string? Svg) ImagePart(DocumentResource resource, double scale, OpenXmlPart part)
    {
        if (this.images.TryGetValue((part, resource), out var known))
        {
            return known;
        }

        var image = resource;
        var info = ImageInfo.Read(resource.Content.Span);
        if (info is null
            && ImageConverters.Convert(this.Context.Options.ImageConverters, resource, 2 * scale, Format.Docx) is var (converted, width, height)
            && ImageInfo.Read(converted.Content.Span) is { Format: ImageFormat.Png or ImageFormat.Jpeg } convertedInfo)
        {
            image = converted;
            info = convertedInfo with { Width = Math.Max(1, (int)Math.Round(width)), Height = Math.Max(1, (int)Math.Round(height)) };
        }

        if (info is not { } found)
        {
            throw new DocumentConversionException(
                Format.Docx,
                $"The image '{resource.Path}' is not a PNG, JPEG or GIF{(this.Context.Options.ImageConverters.Count == 0 ? string.Empty : ", and no image converter takes it")}.");
        }

        // A converted SVG goes in as it came, beside its PNG.
        var svg = ReferenceEquals(image, resource) || !SvgBlip.IsSvg(resource) ? null : AddImage(part, ImagePartType.Svg, resource.Content);
        var result = (AddImage(part, PartType(found.Format), image.Content), found, svg);
        this.images[(part, resource)] = result;
        return result;
    }

    private static PartTypeInfo PartType(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ImagePartType.Jpeg,
        ImageFormat.Gif => ImagePartType.Gif,
        _ => ImagePartType.Png,
    };

    // An image part of `part` (the body or the footnotes) with `content`; returns its relationship id.
    private static string AddImage(OpenXmlPart part, PartTypeInfo type, ReadOnlyMemory<byte> content)
    {
        var imagePart = part switch
        {
            MainDocumentPart main => main.AddImagePart(type),
            FootnotesPart footnotesPart => footnotesPart.AddImagePart(type),
            _ => throw new InvalidOperationException("Images go in the body and in footnotes."),
        };
        using (var stream = new MemoryStream(content.ToArray()))
        {
            imagePart.FeedData(stream);
        }

        return part.GetIdOfPart(imagePart);
    }

    // An inline picture, its alt text in wp:docPr/@descr; with `svg`, the SVG Word 2016 and later draw instead of the PNG.
    private WordDrawing Picture(string relationship, long cx, long cy, string alt, string? svg = null)
    {
        var id = ++this.drawingId;
        var name = "Picture " + id.ToString(CultureInfo.InvariantCulture);
        var description = SecurityElement.Escape(XmlSafe(alt));
        var size = $"cx=\"{cx.ToString(CultureInfo.InvariantCulture)}\" cy=\"{cy.ToString(CultureInfo.InvariantCulture)}\"";
        return new WordDrawing(
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
            + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" "
            + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent {size}/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>"
            + $"<wp:docPr id=\"{id.ToString(CultureInfo.InvariantCulture)}\" name=\"{name}\" descr=\"{description}\"/>"
            + "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>"
            + "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>"
            + $"<pic:nvPicPr><pic:cNvPr id=\"{id.ToString(CultureInfo.InvariantCulture)}\" name=\"{name}\" descr=\"{description}\"/><pic:cNvPicPr/></pic:nvPicPr>"
            + $"<pic:blipFill><a:blip r:embed=\"{relationship}\">{(svg is null ? string.Empty : SvgBlip.Extension(svg))}</a:blip><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext {size}/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            + "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>");
    }

    // Drawing ids are unique across the document: the body's follow the template's headers and footers.
    private static int MaxDrawingId(MainDocumentPart main)
    {
        IEnumerable<OpenXmlPart> parts = [main, .. main.HeaderParts, .. main.FooterParts];
        var ids = new List<int> { 0 };
        foreach (var part in parts)
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            foreach (Match match in DocPrIdPattern().Matches(reader.ReadToEnd()))
            {
                ids.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        return ids.Max();
    }

    // A block's line in the original Markdown, from 1.
    internal int LineOf(MarkdownObject block) => block.Line + 1 + this.Context.LineOffset;

    [GeneratedRegex(@"[^A-Za-z0-9_]", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeCharacters();

    [GeneratedRegex(@"scale\s*=\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"<(?:wp:docPr|pic:cNvPr)\b[^>]*\sid=""([0-9]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex DocPrIdPattern();

    // What separates a term from its text in `- **Term** – text`, as on slides.
    [GeneratedRegex(@"^\s*(?:[–—:]|-(?=\s))?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    /// <summary>Where blocks go: the style of plain paragraphs, the indent of a list item they continue, and the part.</summary>
    private sealed record Frame(StyleRole Style, OpenXmlPart Part)
    {
        /// <summary>The left indent of the list item the paragraphs continue; <see langword="null"/>: none.</summary>
        public int? Indent { get; init; }

        /// <summary>Whether the style numbers itself, so continuing paragraphs switch it off.</summary>
        public bool SuppressNumbering { get; init; }

        /// <summary>The slide-layout container (<c>cards</c>, <c>panel</c> …) the blocks are in; <see langword="null"/>: none.</summary>
        public string? Container { get; init; }

        /// <summary>
        /// The alert whose bar and shading paragraphs of other styles inside it (list items) take as direct formatting, so
        /// that the bar runs on; <see langword="null"/>: none.
        /// </summary>
        public StyleRole? Box { get; init; }
    }

    // The body writer keeps per-document state, so the generator gets a new one each time.
    private sealed class Factory : IBodyWriter
    {
        public (IReadOnlyList<OpenXmlElement> Elements, IReadOnlyList<TocEntry> Headings) Write(IReadOnlyList<Block> blocks, DocxContext context) =>
            new DocxBody(context).WriteAll(blocks);
    }
}
