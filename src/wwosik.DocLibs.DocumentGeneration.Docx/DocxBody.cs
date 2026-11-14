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
    private readonly Dictionary<(OpenXmlPart, DocumentResource), (string Id, ImageInfo Info)> images = [];
    private readonly Dictionary<CaptionKind, int> captions = [];
    private readonly int[] headingCounters = new int[3];
    private readonly List<TocEntry> headings = [];
    private DocxNumbering numbering = null!;
    private DocxFootnotes footnotes = null!;
    private DocxRuns runs = null!;
    private int shift;
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

        // The highest level used after the cover becomes Heading 1.
        var levels = blocks.SelectMany(b => b is HeadingBlock h ? [h] : b is ContainerBlock c ? c.Descendants<HeadingBlock>() : []).Select(h => h.Level).ToList();
        this.shift = levels.Count == 0 ? 0 : levels.Min() - 1;

        var elements = new List<OpenXmlElement>();
        this.Blocks(blocks, new Frame(DocxRoles.Normal, this.MainPart), elements);
        this.numbering.Save();
        this.footnotes.Save();
        return (elements, this.headings);
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

            if (block is ParagraphBlock misplaced && Captions.Kind(misplaced) is { } kind)
            {
                this.Context.Notes.Add(
                    NoteCodes.CaptionIgnored,
                    this.LineOf(misplaced),
                    $"The paragraph '{GenerationNotes.Quote(Captions.Prefix(kind))}…' follows no {(kind == CaptionKind.Table ? "table" : "drawn diagram or chart")}, so it is shown as text. Put it right after its {(kind == CaptionKind.Table ? "table" : "diagram")}.");
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
            case HeadingBlock heading:
                this.Heading(heading, target);
                break;
            case CustomContainer { Info: LayoutSyntax.Notes }:
                throw LayoutSyntax.NestedNotes(Format.Docx);
            case CustomContainer container:
                // Slide layouts are shown plainly, their content in sequence; the `---` between columns only separates them.
                LayoutSyntax.CheckContainer(container.Info ?? string.Empty, container.Arguments?.Trim() ?? string.Empty, Format.Docx);
                this.Blocks([.. container.Where(c => c is not ThematicBreakBlock || container.Info != "columns")], frame, target);
                break;
            case AlertBlock alert:
                LayoutSyntax.IsImportant(alert.Kind.ToString(), Format.Docx);
                var quote = frame with { Style = DocxRoles.Quote, Indent = null };
                target.Add(this.Paragraph(quote, [new DocxRun(this.Context.Texts.Alert(alert.Kind.ToString()), RunFlags.Bold, null)]));
                this.Blocks([.. alert], quote, target);
                break;
            case ParagraphBlock marker when BuildMarker.IsMarker(marker):
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
                this.Code(code, target);
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

        // A later `#` starts a page, as in the PDF; the first content already does.
        if (heading.Level == 1 && target.Count > 0)
        {
            properties.Append(new PageBreakBefore());
        }

        if (unnumbered)
        {
            properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 0 }));
        }

        var paragraph = new Paragraph(properties);
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

        target.Add(paragraph);
        this.headings.Add(new TocEntry(level, DocxRuns.Text(runs).Trim()) { Number = unnumbered ? null : this.Number(level), Bookmark = bookmark });
    }

    // The number Word shows for a heading of Heading 1–3 through the multilevel list, as it counts: a level skipped shows 0.
    private string? Number(int level)
    {
        if (!this.Context.HeadingNumbers || level > 3)
        {
            return null;
        }

        this.headingCounters[level - 1]++;
        for (var deeper = level; deeper < 3; deeper++)
        {
            this.headingCounters[deeper] = 0;
        }

        return string.Join(".", this.headingCounters.Take(level).Select(n => n.ToString(CultureInfo.InvariantCulture)));
    }

    // Text before and after images becomes paragraphs of their own style; each image is a centred paragraph.
    private void Paragraph(ContainerInline? inline, Frame frame, List<OpenXmlElement> target)
    {
        foreach (var piece in this.runs.Pieces(inline))
        {
            if (piece is LinkInline image)
            {
                this.Image(image, frame, target);
            }
            else if (piece is List<DocxRun> runs && DocxRuns.Trim(runs) is { Count: > 0 } trimmed)
            {
                target.Add(this.Paragraph(frame, trimmed));
            }
        }
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
            var children = item.ToList();
            var task = Task(item);
            var itemStyle = task is null ? style : DocxRoles.ListTask;
            var continuation = frame with { Style = itemStyle, Indent = indent, SuppressNumbering = this.numbering.StyleNumbers(itemStyle) };

            // The item's first paragraph carries the marker (or the check box); an item that starts otherwise gets an empty one.
            var first = children.FirstOrDefault() as ParagraphBlock;
            var pieces = first is null ? [new List<DocxRun>()] : this.runs.Pieces(first.Inline);
            var lead = pieces.FirstOrDefault() as List<DocxRun> ?? [];
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

    // One paragraph per line in the code style, tabs as four spaces; a block a renderer would draw stays code for now.
    private void Code(CodeBlock code, List<OpenXmlElement> target)
    {
        if (code is FencedCodeBlock fenced && this.Context.Options.CodeBlockRenderers.Count > 0
            && CodeBlockRenderers.Render(
                this.Context.Options.CodeBlockRenderers,
                new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), this.Context.Brand)
                {
                    Line = this.LineOf(fenced),
                    Report = (_, _) => { },
                    Texts = this.Context.Texts,
                },
                Format.Docx) is not null)
        {
            this.Context.Notes.Add(
                NoteCodes.CodeNotDrawn,
                this.LineOf(fenced),
                $"The '{fenced.Info}' block is shown as code: Word documents do not draw diagrams and charts yet.");
        }

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
            .Select(c => c < row.Count ? this.Cell((MarkdigTableCell)row[c]) : [])
            .ToList()).ToList();
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
                row.Append(new TableCell(
                    new TableCellProperties(new TableCellWidth { Width = widths[c].ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa }),
                    paragraph));
            }

            result.Append(row);
        }

        target.Add(result);
    }

    // A cell's paragraphs as one run list, separated by breaks; images as their alt text.
    private List<DocxRun> Cell(MarkdigTableCell cell)
    {
        var runs = new List<DocxRun>();
        foreach (var leaf in cell.Descendants<LeafBlock>().Where(l => l.Inline is not null))
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
        runs.Select(r => (r.IsBreak ? TextMetrics.LineBreak : r.Text, bold || r.Flags.HasFlag(RunFlags.Bold)));

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
        var line = this.LineOf(link);
        var alt = AltText(link);
        var attributes = ImageAttributes.Take(link, line, Format.Docx);
        if (QrDrawing.IsQr(url))
        {
            // Drawn with the other drawings in a later version; until then its alt text.
            target.Add(this.Paragraph(frame, [new DocxRun(alt, RunFlags.None, null)]));
            return;
        }

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
        var (id, info) = this.ImagePart(resource, scale, frame.Part);

        // The natural size at 96 DPI times the scale, fitted to the text width and 80 % of the text height, never enlarged.
        var page = this.Context.Page;
        var width = info.Width * EmuPerPixel * scale;
        var height = info.Height * EmuPerPixel * scale;
        var fit = Math.Min(1.0, Math.Min(page.TextWidth * DocxPage.EmuPerTwip / width, (page.Height - page.Top - page.Bottom) * DocxPage.EmuPerTwip * 0.8 / height));
        var cx = Math.Max(1, (long)(width * fit));
        var cy = Math.Max(1, (long)(height * fit));
        var caption = Captions.OfImage(link.Title, attributes.Caption, alt);

        var paragraph = this.Paragraph(frame.Style);
        var properties = paragraph.ParagraphProperties!;
        if (caption is not null)
        {
            properties.Append(new KeepNext());
        }

        if (frame.Indent is { } indent)
        {
            if (frame.SuppressNumbering)
            {
                properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 0 }));
            }

            properties.Append(new Indentation { Left = indent.ToString(CultureInfo.InvariantCulture) });
        }

        properties.Append(new Justification { Val = JustificationValues.Center });
        paragraph.Append(new Run(this.Picture(id, cx, cy, alt)));
        target.Add(paragraph);
        if (caption is not null)
        {
            target.Add(this.Caption(CaptionKind.Figure, [new DocxRun(caption, RunFlags.None, null)], frame.Part));
        }
    }

    // PNG, JPEG and GIF as they are; other formats through the image converters at twice the size, as in the PDF. Each
    // image once per part, however often it is used.
    private (string Id, ImageInfo Info) ImagePart(DocumentResource resource, double scale, OpenXmlPart part)
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

        var type = found.Format switch
        {
            ImageFormat.Jpeg => ImagePartType.Jpeg,
            ImageFormat.Gif => ImagePartType.Gif,
            _ => ImagePartType.Png,
        };
        var imagePart = part switch
        {
            MainDocumentPart main => main.AddImagePart(type),
            FootnotesPart footnotesPart => footnotesPart.AddImagePart(type),
            _ => throw new InvalidOperationException("Images go in the body and in footnotes."),
        };
        using (var stream = new MemoryStream(image.Content.ToArray()))
        {
            imagePart.FeedData(stream);
        }

        var result = (part.GetIdOfPart(imagePart), found);
        this.images[(part, resource)] = result;
        return result;
    }

    // An inline picture, its alt text in wp:docPr/@descr.
    private WordDrawing Picture(string relationship, long cx, long cy, string alt)
    {
        var id = ++this.drawingId;
        var name = "Picture " + id.ToString(CultureInfo.InvariantCulture);
        var description = SecurityElement.Escape(alt);
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
            + $"<pic:blipFill><a:blip r:embed=\"{relationship}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
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
    private int LineOf(MarkdownObject block) => block.Line + 1 + this.Context.LineOffset;

    [GeneratedRegex(@"[^A-Za-z0-9_]", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeCharacters();

    [GeneratedRegex(@"scale\s*=\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"<(?:wp:docPr|pic:cNvPr)\b[^>]*\sid=""([0-9]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex DocPrIdPattern();

    /// <summary>Where blocks go: the style of plain paragraphs, the indent of a list item they continue, and the part.</summary>
    private sealed record Frame(StyleRole Style, OpenXmlPart Part)
    {
        /// <summary>The left indent of the list item the paragraphs continue; <see langword="null"/>: none.</summary>
        public int? Indent { get; init; }

        /// <summary>Whether the style numbers itself, so continuing paragraphs switch it off.</summary>
        public bool SuppressNumbering { get; init; }
    }

    // The body writer keeps per-document state, so the generator gets a new one each time.
    private sealed class Factory : IBodyWriter
    {
        public (IReadOnlyList<OpenXmlElement> Elements, IReadOnlyList<TocEntry> Headings) Write(IReadOnlyList<Block> blocks, DocxContext context) =>
            new DocxBody(context).WriteAll(blocks);
    }
}
