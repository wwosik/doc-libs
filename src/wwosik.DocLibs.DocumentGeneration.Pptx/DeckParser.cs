using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
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
/// <c>##</c>, and a slide per table or image.
/// </summary>
internal sealed partial class DeckParser(MarkdownDocument document, PptxOptions options, Brand brand, int lineOffset)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .Build();

    private readonly List<Slide> slides = [];
    private SectionBuilder? section;
    private bool hasCover;

    /// <remarks>
    /// <paramref name="brand"/> is the brand code-block renderers draw in; <paramref name="lineOffset"/> the lines of
    /// front matter cut off the Markdown, so renderers get the block's line in the original.
    /// </remarks>
    public static IReadOnlyList<Slide> Parse(MarkdownDocument document, PptxOptions options, Brand? brand = null, int lineOffset = 0)
    {
        var parser = new DeckParser(document, options, brand ?? Brand.Neutral, lineOffset);
        foreach (var block in Markdown.Parse(document.Markdown, Pipeline))
        {
            parser.Block(block);
        }

        parser.section?.Finish();
        return parser.slides;
    }

    private void Block(Block block)
    {
        if (block is HeadingBlock { Level: <= 2 } heading)
        {
            this.section?.Finish();
            var isTitle = heading.Level == 1;
            this.section = new SectionBuilder(this.slides, this.Runs(heading.Inline, imagesAsText: true), isTitle, isTitle && this.hasCover);
            this.hasCover |= isTitle;
        }
        else if (this.section is { } current)
        {
            this.Walk(block, current, 0, RunStyle.None);
        }
    }

    private CodeBlockGraphic? Render(FencedCodeBlock fenced) =>
        options.CodeBlockRenderers.Count == 0 ? null : CodeBlockRenderers.Render(
            options.CodeBlockRenderers,
            new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), brand) { Line = fenced.Line + 1 + lineOffset });

    private void Walk(Block block, SectionBuilder target, int level, RunStyle style)
    {
        switch (block)
        {
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
                target.Visual(this.Table(table), null);
                break;
            case FencedCodeBlock fenced when this.Render(fenced) is { } rendered:
                if (rendered is DrawingGraphic drawing)
                {
                    target.Visual(null, null, drawing.Drawing);
                }
                else if (rendered is ImageGraphic image)
                {
                    target.Visual(null, new SlideImage(image.Image, fenced.Info ?? string.Empty, 1.0));
                }

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

    private void List(ListBlock list, SectionBuilder target, int level, RunStyle style)
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

    // Splits the paragraph at images: text before and after becomes paragraphs, each image a visual.
    private void Paragraph(ContainerInline? inline, SectionBuilder target, ParagraphKind kind, int level, int start, RunStyle style)
    {
        var collector = new RunCollector(this, imagesAsText: false);
        collector.Add(inline, style, null);
        var first = true;
        foreach (var piece in collector.Pieces)
        {
            if (piece is SlideImage image)
            {
                target.Visual(null, image);
            }
            else if (piece is List<TextRun> runs && Trim(runs) is { Count: > 0 } trimmed)
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
        return Trim(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList());
    }

    private SlideTable Table(Table table)
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
        return new SlideTable(grid, alignments, rows.Count > 0 && rows[0].IsHeader);
    }

    private IReadOnlyList<TextRun> Cell(TableCell cell)
    {
        var runs = new List<TextRun>();
        foreach (var leaf in cell.Descendants<LeafBlock>().Where(l => l.Inline is not null))
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
        var scale = link.Title is { } title && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 1.0;
        return new SlideImage(resource, AltText(link), scale);
    }

    private static string AltText(ContainerInline container) =>
        string.Concat(container.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    [GeneratedRegex(@"scale\s*=\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

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
                        var added = emphasis.DelimiterChar == '~' ? RunStyle.Strike : emphasis.DelimiterCount >= 2 ? RunStyle.Bold : RunStyle.Italic;
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
    private sealed class SectionBuilder(List<Slide> slides, IReadOnlyList<TextRun> title, bool isTitle, bool isSection)
    {
        private List<TextParagraph> above = [];
        private List<TextParagraph> below = [];
        private SlideTable? table;
        private SlideImage? image;
        private Drawing? drawing;
        private bool titlePending = isTitle;

        private bool HasVisual => this.table is not null || this.image is not null || this.drawing is not null;

        public void Text(TextParagraph paragraph) => (this.HasVisual ? this.below : this.above).Add(paragraph);

        public void Visual(SlideTable? newTable, SlideImage? newImage, Drawing? newDrawing = null)
        {
            if (this.titlePending)
            {
                slides.Add(new TitleSlide(title, this.above, isSection));
                this.above = [];
                this.titlePending = false;
            }
            else if (this.HasVisual)
            {
                this.FlushVisual();
            }

            this.table = newTable;
            this.image = newImage;
            this.drawing = newDrawing;
        }

        public void Finish()
        {
            if (this.titlePending)
            {
                slides.Add(new TitleSlide(title, this.above, isSection));
            }
            else if (this.HasVisual)
            {
                this.FlushVisual();
            }
            else
            {
                slides.Add(new ContentSlide(title, this.above));
            }
        }

        private void FlushVisual()
        {
            slides.Add(new VisualSlide(title, this.above, this.table, this.image, this.below) { Drawing = this.drawing });
            this.above = [];
            this.below = [];
            this.table = null;
            this.image = null;
            this.drawing = null;
        }
    }
}
