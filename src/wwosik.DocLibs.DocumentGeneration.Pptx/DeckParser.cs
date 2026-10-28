using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
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
internal sealed partial class DeckParser(MarkdownDocument document, PptxOptions options, Brand brand, int lineOffset)
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    private readonly List<Slide> slides = [];
    private bool hasCover;

    /// <remarks>
    /// <paramref name="brand"/> is the brand code-block renderers draw in; <paramref name="lineOffset"/> the lines of
    /// front matter cut off the Markdown, so renderers get the block's line in the original.
    /// </remarks>
    public static IReadOnlyList<Slide> Parse(MarkdownDocument document, PptxOptions options, Brand? brand = null, int lineOffset = 0)
    {
        var parser = new DeckParser(document, options, brand ?? Brand.Neutral, lineOffset);
        HeadingBlock? heading = null;
        var blocks = new List<Block>();
        foreach (var block in Markdown.Parse(document.Markdown, Pipeline))
        {
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
        return parser.slides;
    }

    // Custom containers without their inline form (`::text::`), which would take `std::vector::size` apart.
    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
            .UseAutoLinks()
            .UseAlertBlocks();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    private void Section(HeadingBlock? heading, List<Block> blocks)
    {
        if (heading is null)
        {
            return;
        }

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

        // `layout="…"` alone keeps a plain slide plain, on the layout asked for.
        if (attributes is not ({ Dark: true } or { Kicker: not null }) && (isTitle || !blocks.Any(UsesLayouts)))
        {
            var closing = attributes?.Closing ?? false;
            var section = new SectionBuilder(this.slides, title, isTitle, isTitle && this.hasCover && !closing, attributes?.Layout, closing);
            this.hasCover |= isTitle && !closing;
            foreach (var block in blocks)
            {
                this.Walk(block, section, 0, RunStyle.None);
            }

            section.Finish();
            return;
        }

        var flow = new FlowCollector(inContainer: false);
        foreach (var block in blocks)
        {
            this.Walk(block, flow, 0, RunStyle.None);
        }

        this.slides.Add(new FlowSlide(title, attributes?.Kicker, attributes?.Dark ?? false, flow.Finish()) { Layout = attributes?.Layout });
    }

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
    private static bool UsesLayouts(Block block) =>
        block is CustomContainer or AlertBlock || (block is ParagraphBlock paragraph && Tags(paragraph) is not null)
        || (block is ContainerBlock container && (container.Descendants<CustomContainer>().Any() || container.Descendants<AlertBlock>().Any()));

    private CodeBlockGraphic? Render(FencedCodeBlock fenced) =>
        options.CodeBlockRenderers.Count == 0 ? null : CodeBlockRenderers.Render(
            options.CodeBlockRenderers,
            new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), brand) { Line = fenced.Line + 1 + lineOffset },
            Format.Pptx);

    private void Walk(Block block, ITarget target, int level, RunStyle style)
    {
        switch (block)
        {
            case CustomContainer container when target is FlowCollector flow:
                this.Container(container, flow);
                break;
            case AlertBlock alert when target is FlowCollector flow:
                flow.Add(new CalloutBlock(LayoutSyntax.IsImportant(alert.Kind.ToString(), Format.Pptx), this.Flow(alert)));
                break;
            case ParagraphBlock paragraph when target is FlowCollector flow && Tags(paragraph) is { } tags:
                flow.Add(new TagsBlock(tags));
                break;
            case ListBlock list when target is FlowCollector { InContainer: true } flow && this.LabelledTags(list) is { } items:
                flow.Add(new LabelledTagsBlock(items));
                break;
            case HeadingBlock heading:
                target.Text(new TextParagraph(this.Runs(heading.Inline, imagesAsText: true, style | RunStyle.Bold), Level: level));
                break;
            case ParagraphBlock paragraph:
                this.Paragraph(paragraph.Inline, target, ParagraphKind.Plain, level, 1, style);
                break;
            case ListBlock list:
                this.List(list, target, level, style);
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

    private void List(ListBlock list, ITarget target, int level, RunStyle style)
    {
        var kind = list.IsOrdered ? ParagraphKind.Numbered : ParagraphKind.Bullet;
        var start = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
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
    }

    // Splits the paragraph at images: text before and after becomes paragraphs, each image a visual. A paragraph of
    // nothing but two or more images is one visual: a row of them, side by side.
    private void Paragraph(ContainerInline? inline, ITarget target, ParagraphKind kind, int level, int start, RunStyle style)
    {
        var collector = new RunCollector(this, imagesAsText: false);
        collector.Add(inline, style, null);
        var images = collector.Pieces.OfType<SlideImage>().ToList();
        if (images.Count >= 2 && collector.Pieces.OfType<List<TextRun>>().All(runs => Trim(runs).Count == 0))
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
            else if (piece is List<TextRun> runs && Trim(Colour(runs)) is { Count: > 0 } trimmed)
            {
                target.Text(new TextParagraph(trimmed, first ? kind : ParagraphKind.Plain, level, start));
                first = false;
            }
        }
    }

    private static List<TextRun> Trim(List<TextRun> runs)
    {
        var result = runs.SkipWhile(r => r.IsBreak || r.Text.Trim().Length == 0).ToList();
        while (result.Count > 0 && (result[^1].IsBreak || result[^1].Text.Trim().Length == 0))
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
        return Trim(Colour(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList()));
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
        switch (name)
        {
            case "cards":
                target.Add(new CardsBlock(this.Cards(container)));
                break;
            case "rows":
                target.Add(new RowsBlock(this.Rows(container)));
                break;
            case "panel":
                target.Add(new PanelBlock(arguments == "dark", this.Flow(container)));
                break;
            case "columns":
                target.Add(this.Columns(container, arguments));
                break;
            case "statement":
                if (container.FirstOrDefault() is not ParagraphBlock lead)
                {
                    throw Error("A '::: statement' starts with a paragraph, its statement.");
                }

                target.Add(new StatementBlock(this.Runs(lead.Inline, imagesAsText: true)));
                foreach (var child in container.Skip(1))
                {
                    this.Walk(child, target, 0, RunStyle.None);
                }

                break;
        }
    }

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
                cards.Add(new Card(this.Runs(heading.Inline, imagesAsText: true), figure, this.Flow(figure is null ? blocks : blocks.Skip(1))));
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
        var flow = new FlowCollector(inContainer: true);
        foreach (var block in blocks)
        {
            this.Walk(block, flow, 0, RunStyle.None);
        }

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

    private SlideImage Image(LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty;
        var resource = document.FindResource(url)
            ?? throw new DocumentConversionException(Format.Pptx, $"The image '{url}' is not among the document's resources.");
        double? scale = link.Title is { } title && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
        return this.Placeable(new SlideImage(resource, AltText(link), scale, link.Title is { } words && CaptionPattern().IsMatch(words)));
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

        return image with { Resource = converted, Natural = (width, height), Vector = IsSvg(image.Resource) ? image.Resource : null };
    }

    private static bool IsSvg(DocumentResource resource)
    {
        if (resource.ContentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) || resource.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var head = System.Text.Encoding.UTF8.GetString(resource.Content.Span[..Math.Min(resource.Content.Length, 1024)]);
        return head.Contains("<svg", StringComparison.Ordinal);
    }

    private static string AltText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    private static DocumentConversionException Error(string message) => new(Format.Pptx, message);

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

        void Visual(SlideTable? table, IReadOnlyList<SlideImage>? images, Drawing? drawing = null, ChartSpec? chart = null);
    }

    /// <summary>Collects runs, cut into pieces at images unless images count as their alt text.</summary>
    private sealed class RunCollector(DeckParser parser, bool imagesAsText)
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
                        this.Text(html.Tag, style, link);
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

    /// <summary>Collects the text and visuals of one heading's section and turns them into slides.</summary>
    private sealed class SectionBuilder(List<Slide> slides, IReadOnlyList<TextRun> title, bool isTitle, bool isSection, string? layout, bool closing) : ITarget
    {
        private List<TextParagraph> above = [];
        private List<TextParagraph> below = [];
        private SlideTable? table;
        private IReadOnlyList<SlideImage>? images;
        private Drawing? drawing;
        private ChartSpec? chart;
        private bool titlePending = isTitle;

        private bool HasVisual => this.table is not null || this.images is not null || this.drawing is not null || this.chart is not null;

        public void Text(TextParagraph paragraph) => (this.HasVisual ? this.below : this.above).Add(paragraph);

        public void Visual(SlideTable? newTable, IReadOnlyList<SlideImage>? newImages, Drawing? newDrawing = null, ChartSpec? newChart = null)
        {
            if (this.titlePending)
            {
                slides.Add(new TitleSlide(title, this.above, isSection) { Layout = layout, Closing = closing });
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
        }

        public void Finish()
        {
            if (this.titlePending)
            {
                slides.Add(new TitleSlide(title, this.above, isSection) { Layout = layout, Closing = closing });
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
            slides.Add(new VisualSlide(title, this.above, this.table, this.images, this.below) { Drawing = this.drawing, Chart = this.chart, Layout = layout });
            this.above = [];
            this.below = [];
            this.table = null;
            this.images = null;
            this.drawing = null;
            this.chart = null;
        }
    }

    /// <summary>Collects the blocks of a flow slide or of a container on one; consecutive paragraphs make one text block.</summary>
    private sealed class FlowCollector(bool inContainer) : ITarget
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

            this.text.Add(paragraph);
        }

        public void Visual(SlideTable? table, IReadOnlyList<SlideImage>? images, Drawing? drawing = null, ChartSpec? chart = null) =>
            this.Add(table is not null ? new TableBlock(table)
                : images is [var image] ? new ImageBlock(image)
                : images is not null ? new ImageRowBlock(images)
                : chart is not null ? new ChartBlock(chart)
                : new DrawingBlock(drawing!));

        public void Add(FlowBlock block)
        {
            this.text = null;
            this.blocks.Add(block);
        }

        public List<FlowBlock> Finish() => this.blocks;
    }
}
