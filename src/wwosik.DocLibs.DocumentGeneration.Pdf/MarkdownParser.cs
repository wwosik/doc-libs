using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
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
internal sealed partial class MarkdownParser(MarkdownDocument document, PdfOptions options, Brand brand, int lineOffset)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    /// <remarks>
    /// <paramref name="brand"/> is the brand code-block renderers draw in; <paramref name="lineOffset"/> the lines of
    /// front matter cut off the Markdown, so renderers get the block's line in the original.
    /// </remarks>
    public static DocumentModel Parse(MarkdownDocument document, PdfOptions options, Brand? brand = null, int lineOffset = 0)
    {
        var parser = new MarkdownParser(document, options, brand ?? Brand.Neutral, lineOffset);
        var markdown = Markdown.Parse(document.Markdown, Pipeline);
        var blocks = markdown.ToList();
        Cover? cover = null;
        var start = 0;
        if (blocks.FirstOrDefault() is Markdig.Syntax.HeadingBlock { Level: 1 } first)
        {
            var subtitle = new List<IReadOnlyList<TextRun>>();
            start = 1;
            while (start < blocks.Count && blocks[start] is Markdig.Syntax.ParagraphBlock paragraph && !HasImage(paragraph))
            {
                subtitle.Add(parser.Runs(paragraph.Inline));
                start++;
            }

            cover = new Cover(parser.Runs(first.Inline), subtitle, Anchor(first));
        }

        var result = new List<Block>();
        foreach (var block in blocks.Skip(start))
        {
            parser.Add(block, result);
        }

        return new DocumentModel(cover, result);
    }

    private void Add(Markdig.Syntax.Block block, List<Block> target)
    {
        switch (block)
        {
            case Markdig.Syntax.HeadingBlock heading:
                target.Add(new HeadingBlock(heading.Level, this.Runs(heading.Inline), Anchor(heading)));
                break;
            case Markdig.Syntax.ParagraphBlock paragraph:
                this.Paragraph(paragraph.Inline, target);
                break;
            case Markdig.Syntax.ListBlock list:
                target.Add(new ListBlock(
                    list.IsOrdered,
                    list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 1,
                    list.OfType<ListItemBlock>().Select(item => (IReadOnlyList<Block>)this.Children(item)).ToList()));
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
                    ImageGraphic image => this.Image(image.Image, 1.0),
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

    private CodeBlockGraphic? Render(FencedCodeBlock fenced) =>
        options.CodeBlockRenderers.Count == 0 ? null : CodeBlockRenderers.Render(
            options.CodeBlockRenderers,
            new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), brand) { Line = fenced.Line + 1 + lineOffset });

    private List<Block> Children(ContainerBlock container)
    {
        var children = new List<Block>();
        foreach (var child in container)
        {
            this.Add(child, children);
        }

        return children;
    }

    // Splits the paragraph at images: text before and after becomes paragraphs, each image a block of its own.
    private void Paragraph(ContainerInline? inline, List<Block> target)
    {
        var collector = new RunCollector(this);
        collector.Add(inline, RunStyle.None, null);
        foreach (var piece in collector.Pieces)
        {
            if (piece is ImageBlock image)
            {
                target.Add(image);
            }
            else if (piece is List<TextRun> runs && Trim(runs) is { Count: > 0 } trimmed)
            {
                target.Add(new ParagraphBlock(trimmed));
            }
        }
    }

    private List<TextRun> Runs(ContainerInline? inline)
    {
        var collector = new RunCollector(this, imagesAsText: true);
        collector.Add(inline, RunStyle.None, null);
        return Trim(collector.Pieces.OfType<List<TextRun>>().SelectMany(r => r).ToList());
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

    private IReadOnlyList<TextRun> Cell(TableCell cell)
    {
        var runs = new List<TextRun>();
        foreach (var leaf in cell.Descendants<LeafBlock>().Where(l => l.Inline is not null))
        {
            if (runs.Count > 0)
            {
                runs.Add(TextRun.Break);
            }

            runs.AddRange(this.Runs(leaf.Inline));
        }

        return runs;
    }

    private ImageBlock Image(LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty;
        var resource = document.FindResource(url)
            ?? throw new DocumentConversionException(DocumentFormat.Pdf, $"The image '{url}' is not among the document's resources.");
        var scale = link.Title is { } title && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 1.0;
        return this.Image(resource, scale);
    }

    private ImageBlock Image(DocumentResource resource, double scale) =>
        ImageInfo.Read(resource.Content.Span) is { Format: ImageFormat.Png or ImageFormat.Jpeg } info
            ? new ImageBlock(resource, info, scale)
            : throw new DocumentConversionException(DocumentFormat.Pdf, $"The image '{resource.Path}' is not a PNG or JPEG.");

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
}
