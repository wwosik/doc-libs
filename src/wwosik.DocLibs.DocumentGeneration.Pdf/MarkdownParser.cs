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
/// the paragraphs up to the next heading; everything else flows as blocks.
/// </summary>
internal sealed partial class MarkdownParser(MarkdownDocument document, PdfOptions options, Brand brand, int lineOffset, GenerationNotes notes, GeneratedTexts texts)
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    // The chapters an empty `{.agenda}` lists, by its heading.
    private readonly Dictionary<Markdig.Syntax.HeadingBlock, ListBlock> agendas = new(ReferenceEqualityComparer.Instance);

    // Images in grey, by the image they were made from, so an image used twice is converted and embedded once.
    private readonly Dictionary<DocumentResource, DocumentResource> greyImages = new(ReferenceEqualityComparer.Instance);

    /// <remarks>
    /// <paramref name="brand"/> is the brand code-block renderers draw in; <paramref name="lineOffset"/> the lines of
    /// front matter cut off the Markdown, so renderers get the block's line in the original; <paramref name="notes"/>
    /// collects what the writer should know, such as HTML shown as text; <paramref name="texts"/> are the texts of the
    /// document language, for alert titles and the heading of the footnotes.
    /// </remarks>
    public static DocumentModel Parse(
        MarkdownDocument document, PdfOptions options, Brand? brand = null, int lineOffset = 0, GenerationNotes? notes = null, GeneratedTexts? texts = null)
    {
        notes ??= new GenerationNotes();
        texts ??= GeneratedTexts.English;
        var parser = new MarkdownParser(document, options, brand ?? Brand.Neutral, lineOffset, notes, texts);
        var markdown = Markdown.Parse(document.Markdown, Pipeline);

        // Page breaks are for documents: their content stays, the break itself is ignored here for now (E03-S03).
        PageBreaks.Flatten(markdown, DocumentFormat.Pdf);
        MarkdownSyntax.ReportMissingFootnotes(markdown, parser.LineOf, notes);

        // Build markers are for decks: checked like there, then left out wherever they are.
        BuildMarker.Check(markdown, DocumentFormat.Pdf, parser.LineOf);
        var footnotes = markdown.OfType<FootnoteGroup>().SelectMany(g => g.OfType<Footnote>()).OrderBy(f => f.Order).ToList();
        var blocks = markdown.Where(b => b is not FootnoteGroup && !(b is Markdig.Syntax.ParagraphBlock paragraph && BuildMarker.IsMarker(paragraph))).ToList();
        Cover? cover = null;
        var start = 0;
        if (blocks.FirstOrDefault() is Markdig.Syntax.HeadingBlock { Level: 1 } first)
        {
            var subtitle = new List<IReadOnlyList<TextRun>>();
            start = 1;
            while (start < blocks.Count && blocks[start] is Markdig.Syntax.ParagraphBlock paragraph && !HasImage(paragraph))
            {
                subtitle.Add(parser.NotDrawn(parser.Runs(paragraph.Inline), "the cover"));
                start++;
            }

            cover = new Cover(parser.NotDrawn(WithoutAttributes(parser.Runs(first.Inline)), "the cover"), subtitle, Anchor(first));
        }

        parser.CheckSections(blocks, start == 1 ? (Markdig.Syntax.HeadingBlock)blocks[0] : null);
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

        // The footnotes at the end, numbered as their references are, until footnotes at the foot of the page.
        if (footnotes.Count > 0)
        {
            result.Add(new HeadingBlock(2, [new TextRun(texts.Footnotes, RunStyle.None)], null));
            result.Add(new ListBlock(true, 1, [.. footnotes.Select(f => (IReadOnlyList<Block>)parser.Children(f))]));
        }

        return new DocumentModel(cover, result);
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
                chapters.Add(Trim([.. WithoutAttributes(this.Runs(heading.Inline)).Where(r => r.Icon is null)]));
            }
        }

        foreach (var (heading, content) in sections)
        {
            SlideKinds.CheckContainers(content, DocumentFormat.Pdf);
            if (heading is null || this.AttributesOf(heading)?.Kind is not { } kind)
            {
                continue;
            }

            var title = string.Concat(WithoutAttributes(this.Runs(heading.Inline)).Select(r => r.IsBreak ? " " : r.Text)).Trim();
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

    private HeadingAttributes? AttributesOf(Markdig.Syntax.HeadingBlock heading) =>
        this.Runs(heading.Inline) is [.., { IsBreak: false, Style: RunStyle.None, Link: null } last]
            ? HeadingAttributes.Split(last.Text, DocumentFormat.Pdf).Attributes
            : null;

    private void Add(Markdig.Syntax.Block block, List<Block> target)
    {
        switch (block)
        {
            case Markdig.Syntax.HeadingBlock heading:
                var runs = this.NotDrawn(this.Runs(heading.Inline), "a heading");
                var text = heading.Level <= 2 ? WithoutAttributes(runs) : runs;

                // A kind of slide without a title, such as `## {.quote}`, has no heading here.
                if (text.Count > 0 || heading.Level > 2 || this.AttributesOf(heading)?.Kind is null)
                {
                    target.Add(new HeadingBlock(heading.Level, text, Anchor(heading)));
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
            case CustomContainer container:
                LayoutSyntax.CheckContainer(container.Info ?? string.Empty, container.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
                if (VisualContainers.IsVisual(container.Info ?? string.Empty))
                {
                    target.Add(new DrawingBlock(VisualContainers.Draw(
                        container,
                        new VisualContext(ResolvedBrand.Resolve(brand), VisualContext.TodayOf(options.TimeProvider), texts, this.LineOf, DocumentFormat.Pdf))));
                    break;
                }

                foreach (var child in container)
                {
                    // The `---` between the columns of `::: columns` only separates them; the columns follow one another.
                    if (child is not ThematicBreakBlock || container.Info != "columns")
                    {
                        this.Add(child, target);
                    }
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
            case Markdig.Syntax.ParagraphBlock paragraph:
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
                target.Add(this.Table(table));
                break;
            case FencedCodeBlock fenced when this.Render(fenced) is { } rendered:
                target.Add(rendered switch
                {
                    DrawingGraphic drawing => new DrawingBlock(drawing.Drawing),
                    ImageGraphic image => this.ImageOf(image.Image, 1.0),
                    ChartGraphic chart => new DrawingBlock(chart.Drawing),
                    _ => throw new UnreachableException(),
                });
                break;
            case Markdig.Syntax.CodeBlock code:
                target.Add(new CodeBlock(code.Lines.ToString().Replace("\t", "    ", StringComparison.Ordinal).Split('\n')));
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

    // Slide settings at the end of a `#` or `##` heading (`{.dark kicker="…"}`) are checked like in decks, then dropped.
    private static List<TextRun> WithoutAttributes(List<TextRun> runs)
    {
        if (runs.Count == 0 || runs[^1] is not { IsBreak: false, Style: RunStyle.None, Link: null } last)
        {
            return runs;
        }

        var (text, attributes) = HeadingAttributes.Split(last.Text, DocumentFormat.Pdf);
        if (attributes is null)
        {
            return runs;
        }

        return text.Length > 0 ? [.. runs[..^1], last with { Text = text }] : runs[..^1];
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

    private List<TextRun> Runs(ContainerInline? inline)
    {
        var collector = new RunCollector(this, imagesAsText: true);
        collector.Add(inline, RunStyle.None, null);
        return Trim(this.Icons(Colour(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList()), this.LineOf(inline)));
    }

    // `:icon-name:`: an icon run of its own, unless icons are switched off.
    private List<TextRun> Icons(List<TextRun> runs, int line) => IconSyntax.Apply(
        runs,
        r => r.Text,
        r => r.Style.HasFlag(RunStyle.Code),
        (r, text) => r with { Text = text },
        (r, icon) => r with { Style = r.Style | RunStyle.Icon, Icon = icon },
        options.Icons,
        line,
        DocumentFormat.Pdf);

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
        if (attributes.Caption)
        {
            this.Ignored(line, attributes, ".caption", "PDFs show no captions under images");
        }

        if (attributes is { Focus: not null, Fit: ImageFit.Contain, Round: false })
        {
            this.Ignored(line, attributes, "focus", "the image is not cropped; add .round, or fit=cover in a row of images");
        }

        var image = this.ImageOf(resource, titleScale ?? attributes.Scale ?? 1.0) with { Attributes = attributes, Line = line };
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
                        this.Text(footnote.Footnote.Order.ToString(CultureInfo.InvariantCulture), style | RunStyle.Superscript, link);
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

            if (this.current.Count > 0 && this.current[^1] is { IsBreak: false } last && last.Style == style && last.Link == link)
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
