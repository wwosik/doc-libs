using System.Globalization;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>What the layout takes from the template and the document besides the blocks.</summary>
internal sealed record Design(ResolvedBrand Brand, bool ShowTagline, string? Date, string? HeaderTitle);

/// <summary>
/// Puts the block model onto pages: greedy line breaking, page breaks, lists, tables with repeated header rows,
/// images, the cover, and header and footer once the page count is known. Works only through
/// <see cref="IFontMetrics"/>, never on a PDF.
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
    private const string Grey = "4B5563";
    private const string CodeBackground = "F3F4F6";
    private const string White = "FFFFFF";

    private static readonly double[] HeadingSizes = [20, 16, 13.5, 12, 11, 10.5];

    private readonly IFontMetrics metrics;
    private readonly PageGeometry geometry;
    private readonly Design design;
    private readonly Func<string, string>? replaceUnsupported;
    private readonly List<LaidOutPage> pages = [];
    private readonly List<Bookmark> bookmarks = [];
    private readonly Dictionary<string, (int Page, double Y)> anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<IReadOnlyList<TextRun>, (double Width, List<TextLine> Lines)> paragraphs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TableBlock, (double Width, PreparedTable? Table)> tables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HeadingBlock, (double Width, List<TextRun> Runs, List<TextLine> Lines)> headings = new(ReferenceEqualityComparer.Instance);
    private LaidOutPage page = null!;
    private double y;

    // Where the headings directly above the current block start, on page chainPage; null after any other block.
    private double? chainTop;
    private int chainPage;

    public LayoutEngine(IFontMetrics metrics, PageGeometry geometry, Design design, Func<string, string>? replaceUnsupported = null)
    {
        this.metrics = metrics;
        this.geometry = geometry;
        this.design = design;
        this.replaceUnsupported = replaceUnsupported;
    }

    private string TextColor => this.design.Brand.ThemeColors[0];

    public Layout Run(DocumentModel model)
    {
        if (model.Cover is { } cover)
        {
            this.Cover(cover);
        }

        if (model.Blocks.Count > 0 || this.pages.Count == 0)
        {
            this.NewPage();
        }

        var frame = new Frame(this.geometry.Left, this.geometry.ContentWidth, this.TextColor, []);
        this.Blocks(model.Blocks, frame, 0);

        // Replaced once, as the header shows it; without a replacement it is checked only where it is drawn.
        var title = this.design.HeaderTitle is { Length: > 0 } header && this.replaceUnsupported is not null
            ? this.Supported(header, FontFace.Regular)
            : this.design.HeaderTitle;
        this.Decorate(title);
        return new Layout(this.pages, this.bookmarks, this.anchors, this.geometry, title);
    }

    private void NewPage(bool isCover = false)
    {
        this.page = new LaidOutPage(isCover);
        this.pages.Add(this.page);
        this.y = this.geometry.Top;
    }

    private bool AtTop => this.y <= this.geometry.Top + 0.01;

    // Starts a new page unless the height fits, or the page is still empty (then it overflows rather than loops).
    private void Ensure(double height)
    {
        // A hair of tolerance: content sized to the exact room left (a visual below its headings) must fit.
        if (this.y + height > this.geometry.Bottom + 1e-6 && !this.AtTop)
        {
            this.NewPage();
        }
    }

    private void Advance(double height, Frame frame)
    {
        foreach (var x in frame.QuoteRules)
        {
            this.page.Items.Add(new LineItem(x, this.y, x, this.y + height, 2, this.design.Brand.TableLine));
        }

        this.y += height;
    }

    private void Gap(double height, Frame frame)
    {
        if (!this.AtTop)
        {
            this.Advance(Math.Min(height, Math.Max(0, this.geometry.Bottom - this.y)), frame);
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
                this.Paragraph(paragraph.Runs, frame, null, ParagraphGap);
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
                this.Drawing(drawing.Drawing, frame);
                break;
            case RuleBlock:
                this.Ensure(12);
                this.page.Items.Add(new LineItem(frame.Left, this.y + 6, frame.Left + frame.Width, this.y + 6, 0.75, this.design.Brand.TableLine));
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
        if (heading.Level == 1 && !this.AtTop)
        {
            this.NewPage();
        }

        this.Gap(size * 0.8, frame);
        var (runs, lines) = this.HeadingLines(heading, frame.Width);
        var height = (lines.Count * size * LineFactor) + (size * 0.35);

        // Kept with the start of what follows: further headings and their content included.
        var above = this.ChainHeight + height;
        this.Ensure(height + this.KeepHeight(siblings, next, frame, above));
        if (this.chainTop is null || this.chainPage != this.pages.Count)
        {
            this.chainTop = this.y;
            this.chainPage = this.pages.Count;
        }

        this.Mark(runs, heading.Level - 1, heading.Anchor);
        foreach (var line in lines)
        {
            this.Line(line, frame, frame.Left, size, this.design.Brand.Primary);
        }

        this.Gap(size * 0.35, frame);
    }

    private static double HeadingSize(HeadingBlock heading) => HeadingSizes[Math.Clamp(heading.Level, 1, 6) - 1];

    // Replaced and wrapped once per heading and width, like paragraphs.
    private (List<TextRun> Runs, List<TextLine> Lines) HeadingLines(HeadingBlock heading, double width)
    {
        if (this.headings.TryGetValue(heading, out var cached) && cached.Width == width)
        {
            return (cached.Runs, cached.Lines);
        }

        var runs = this.Supported(heading.Runs, RunStyle.Bold);
        var lines = this.Wrap(runs, width, HeadingSize(heading), RunStyle.Bold);
        this.headings[heading] = (width, runs, lines);
        return (runs, lines);
    }

    // The height of the headings directly above, on this page.
    private double ChainHeight => this.chainTop is { } top && this.chainPage == this.pages.Count ? this.y - top : 0;

    // The tallest a visual may be: a page, less the headings it is kept with.
    private double VisualRoom => this.geometry.Bottom - this.geometry.Top - this.ChainHeight;

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
            case HeadingBlock { Level: 1 }:
                return 0;
            case HeadingBlock heading:
                var size = HeadingSize(heading);
                var height = (size * 0.8) + (this.HeadingLines(heading, frame.Width).Lines.Count * size * LineFactor) + (size * 0.35);
                return height + this.KeepHeight(siblings, next + 1, frame, above + height);
            case ParagraphBlock paragraph:
                return KeepLines(this.ParagraphLines(paragraph.Runs, frame.Width).Count) * BodySize * LineFactor;
            case ListBlock { Items.Count: > 0 } list:
                var inner = frame with { Left = frame.Left + ListIndent, Width = frame.Width - ListIndent };
                return list.Items[0] is [ParagraphBlock first, ..]
                    ? KeepLines(this.ParagraphLines(first.Runs, inner.Width).Count) * BodySize * LineFactor
                    : Math.Max(BodySize * LineFactor, this.KeepHeight(list.Items[0], 0, inner, above));
            case QuoteBlock quote:
                return this.KeepHeight(quote.Blocks, 0, QuoteFrame(frame), above);
            case CodeBlock code:
                return code.Lines.Count switch
                {
                    <= 1 => (2 * CodePadding) + (CodeSize * LineFactor),
                    _ => CodePadding + (2 * CodeSize * LineFactor),
                };
            case TableBlock table:
                return this.Prepare(table, frame.Width) is { } prepared ? prepared.KeepHeight : 0;
            case ImageBlock image:
                return Math.Min(this.ImageSize(image, frame).Height, this.geometry.Bottom - this.geometry.Top - above);
            case ImageRowBlock row:
                return Math.Min(this.RowSizes(row, frame).Max(s => s.Height), this.geometry.Bottom - this.geometry.Top - above);
            case DrawingBlock drawing:
                return Math.Min(DrawingScale(drawing.Drawing, frame, this.geometry) * drawing.Drawing.Height, this.geometry.Bottom - this.geometry.Top - above);
            case RuleBlock:
                return 12;
            default:
                return 0;
        }
    }

    // The lines of a paragraph that must stay together at its start: all of a short one, else two.
    private static int KeepLines(int lines) => lines <= 3 ? Math.Max(1, lines) : 2;

    private void Mark(IReadOnlyList<TextRun> title, int level, string? anchor)
    {
        var text = string.Concat(title.Select(r => r.IsBreak ? " " : r.Text)).Trim();
        if (text.Length > 0)
        {
            this.bookmarks.Add(new Bookmark(text, level, this.pages.Count - 1, this.y));
        }

        if (anchor is { Length: > 0 })
        {
            this.anchors.TryAdd(anchor, (this.pages.Count - 1, this.y));
        }
    }

    // A marker (bullet or number) goes right-aligned before the first line. A page break inside the paragraph leaves
    // at least two lines on either side: no orphan at the bottom, no widow at the top.
    private void Paragraph(IReadOnlyList<TextRun> runs, Frame frame, string? marker, double gapAfter)
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
            var room = (int)Math.Floor((this.geometry.Bottom - this.y) / lineHeight);
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
                    this.Marker(marker, frame);
                }

                this.Line(lines[i], frame, frame.Left, BodySize, frame.Color);
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

    private void Marker(string marker, Frame frame)
    {
        marker = this.Supported(marker, FontFace.Regular);
        var width = this.metrics.Width(marker, FontFace.Regular, BodySize);
        this.page.Items.Add(new TextItem(frame.Left - MarkerGap - width, this.Baseline(BodySize), marker, FontFace.Regular, BodySize, frame.Color));
    }

    private void List(ListBlock list, Frame frame, int level)
    {
        var inner = frame with { Left = frame.Left + ListIndent, Width = frame.Width - ListIndent };
        for (var i = 0; i < list.Items.Count; i++)
        {
            var marker = list.Ordered ? (list.Start + i).ToString(CultureInfo.InvariantCulture) + "." : level % 2 == 0 ? "•" : "–";
            var item = list.Items[i];
            if (item.Count == 0)
            {
                this.Paragraph([], inner, marker, ListItemGap);
                continue;
            }

            for (var b = 0; b < item.Count; b++)
            {
                if (b == 0 && item[b] is ParagraphBlock paragraph)
                {
                    this.Paragraph(paragraph.Runs, inner, marker, ListItemGap);
                }
                else
                {
                    if (b == 0)
                    {
                        this.Ensure(BodySize * LineFactor);
                        this.Marker(marker, inner);
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
        var lines = code.Lines.SelectMany(line => this.BreakByCharacters(this.Supported(line, FontFace.Mono), FontFace.Mono, CodeSize, inner)).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        this.Ensure(CodePadding + lineHeight);
        this.page.Items.Add(new RectItem(frame.Left, this.y, frame.Width, CodePadding, CodeBackground));
        this.Advance(CodePadding, frame);
        foreach (var line in lines)
        {
            this.Ensure(lineHeight);
            this.page.Items.Add(new RectItem(frame.Left, this.y, frame.Width, lineHeight, CodeBackground));
            if (line.Length > 0)
            {
                this.page.Items.Add(new TextItem(frame.Left + CodePadding, this.Baseline(CodeSize), line, FontFace.Mono, CodeSize, this.TextColor));
            }

            this.Advance(lineHeight, frame);
        }

        this.Ensure(CodePadding);
        this.page.Items.Add(new RectItem(frame.Left, this.y, frame.Width, CodePadding, CodeBackground));
        this.Advance(CodePadding, frame);
        this.Gap(ParagraphGap, frame);
    }

    private void Image(ImageBlock image, Frame frame)
    {
        var (width, height) = this.ImageSize(image, frame);
        if (height > this.VisualRoom)
        {
            // Smaller still, to share a page with the headings above it.
            width *= this.VisualRoom / height;
            height = this.VisualRoom;
        }

        this.Ensure(height);
        this.page.Items.Add(new ImageItem(image.Resource, image.Resource.Content, image.Info.Format, frame.Left + ((frame.Width - width) / 2), this.y, width, height));
        this.Advance(height, frame);
        this.Gap(ParagraphGap, frame);
    }

    // Side by side in cells of equal width, top-aligned, each centred in its cell; the row moves to the next page whole.
    private void ImageRow(ImageRowBlock row, Frame frame)
    {
        var sizes = this.RowSizes(row, frame);
        var height = sizes.Max(s => s.Height);
        var shrink = height > this.VisualRoom ? this.VisualRoom / height : 1.0;
        height *= shrink;
        this.Ensure(height);
        var cell = (frame.Width - ((row.Images.Count - 1) * RowGap)) / row.Images.Count;
        for (var i = 0; i < row.Images.Count; i++)
        {
            var (width, imageHeight) = (sizes[i].Width * shrink, sizes[i].Height * shrink);
            var image = row.Images[i];
            this.page.Items.Add(new ImageItem(image.Resource, image.Resource.Content, image.Info.Format, frame.Left + (i * (cell + RowGap)) + ((cell - width) / 2), this.y, width, imageHeight));
        }

        this.Advance(height, frame);
        this.Gap(ParagraphGap, frame);
    }

    private List<(double Width, double Height)> RowSizes(ImageRowBlock row, Frame frame)
    {
        var cell = frame with { Width = (frame.Width - ((row.Images.Count - 1) * RowGap)) / row.Images.Count };
        return row.Images.Select(image => this.ImageSize(image, cell)).ToList();
    }

    // 96 DPI: a pixel is 0.75 pt. Scaled down to the content width and the page height, never up.
    private (double Width, double Height) ImageSize(ImageBlock image, Frame frame)
    {
        var width = image.Info.Width * PointsPerPixel * image.Scale;
        var height = image.Info.Height * PointsPerPixel * image.Scale;
        var fit = Math.Min(1.0, Math.Min(frame.Width / width, (this.geometry.Bottom - this.geometry.Top) / height));
        return (width * fit, height * fit);
    }

    private static double DrawingScale(Drawing drawing, Frame frame, PageGeometry geometry) =>
        drawing.Width <= 0 || drawing.Height <= 0 ? 0 : PointsPerPixel * Math.Min(
            1.0,
            Math.Min(frame.Width / (drawing.Width * PointsPerPixel), (geometry.Bottom - geometry.Top) / (drawing.Height * PointsPerPixel)));

    // Placed like an image: centred, scaled down to the content width and the page height, never up.
    private void Drawing(Drawing drawing, Frame frame)
    {
        if (drawing.Width <= 0 || drawing.Height <= 0)
        {
            return;
        }

        var scale = DrawingScale(drawing, frame, this.geometry);
        if (drawing.Height * scale > this.VisualRoom)
        {
            scale = this.VisualRoom / drawing.Height;
        }

        var height = drawing.Height * scale;
        this.Ensure(height);
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
                    this.page.Items.Add(new ShapeItem(path, shape.Fill, shape.Stroke, shape.StrokeWidth * scale, shape.Dash?.Select(d => d * scale).ToList(), shape.RoundJoins));
                    break;
                case DrawingText text:
                    var (x, baseline) = Place(text.X, text.Baseline);
                    this.Label(text, x, baseline, scale);
                    break;
            }
        }

        this.Advance(height, frame);
        this.Gap(ParagraphGap, frame);
    }

    // A drawing's text in the diagram faces (the table faces), shrunk to its room down to 70 %, then anchored.
    private void Label(DrawingText text, double x, double baseline, double scale)
    {
        var spans = text.Spans
            .Select(span =>
            {
                var face = text.Mono ? span.Bold ? FontFace.MonoBold : FontFace.Mono : span.Bold ? FontFace.TableBold : FontFace.TableRegular;
                return (Text: this.Supported(Visible(span.Text), face), Face: face);
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
        foreach (var (spanText, face) in spans)
        {
            this.page.Items.Add(new TextItem(x, baseline, spanText, face, size, text.Color));
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
        this.Ensure(keepHeight);
        for (var r = 0; r < cells.Count; r++)
        {
            var isHeader = header is not null && r == 0;
            var fill = isHeader ? this.design.Brand.TableHeader : (r - (header is null ? 0 : 1)) % 2 == 0 ? this.design.Brand.TableBand : null;
            var row = cells[r];
            var start = 0;
            var lineCount = row.Max(c => Math.Max(1, c.Count));
            while (start < lineCount)
            {
                var room = (int)Math.Floor((this.geometry.Bottom - this.y - (2 * CellPadding) + 0.01) / lineHeight);
                var fresh = this.geometry.Bottom - this.geometry.Top - headerHeight - (2 * CellPadding);
                var fitsFresh = (lineCount - start) * lineHeight <= fresh;
                if (room < lineCount - start && (fitsFresh || room < 1) && !this.AtTop && !isHeader)
                {
                    this.NewPage();
                    if (header is not null)
                    {
                        this.Row(header, widths, table.Alignments, frame, 0, header.Max(c => Math.Max(1, c.Count)), this.design.Brand.TableHeader, true);
                    }

                    continue;
                }

                var count = Math.Max(1, Math.Min(lineCount - start, room));
                this.Row(row, widths, table.Alignments, frame, start, count, fill, isHeader);
                start += count;
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
        if (fill is not null)
        {
            this.page.Items.Add(new RectItem(frame.Left, top, total, height, fill));
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
        this.page.Items.Add(new LineItem(frame.Left, top, frame.Left + total, top, 0.5, grid));
        this.page.Items.Add(new LineItem(frame.Left, top + height, frame.Left + total, top + height, 0.5, grid));
        x = frame.Left;
        foreach (var width in widths.Prepend(0))
        {
            x += width;
            this.page.Items.Add(new LineItem(x, top, x, top + height, 0.5, grid));
        }

        this.y = top;
        this.Advance(height, frame);
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

            this.page.Items.Add(new ImageItem(artwork, artwork, art.Format, width - w, height - h, w, h));
        }

        this.y = height * 0.28;
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
            this.page.Items.Add(new ImageItem(tagline, tagline, taglineInfo.Format, width - margin - w, height - (margin * 0.6) - h, w, h));
        }

        if (brand.Logo is { } logo && ImageInfo.Read(logo) is { } logoInfo)
        {
            var w = width * 0.16;
            var h = w * logoInfo.Height / logoInfo.Width;
            this.page.Items.Add(new ImageItem(logo, logo, logoInfo.Format, width - margin - w, margin * 0.7, w, h));
        }
    }

    // Header (document title, logo) and footer (footer text, date, page number) on every page but the cover.
    private void Decorate(string? title)
    {
        var brand = this.design.Brand;
        var g = this.geometry;
        var logoInfo = brand.Logo is { } logo ? ImageInfo.Read(logo) : null;
        List<string>? footer = null;
        for (var i = 0; i < this.pages.Count; i++)
        {
            var items = this.pages[i].Items;
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

            if (title is { Length: > 0 })
            {
                var text = this.Fit(title, FontFace.Regular, FooterSize, g.ContentWidth - logoWidth - 12);
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

    // Places one line at x; links get their colour, an underline and a link area; struck text a line through it.
    private void Line(TextLine line, Frame frame, double x, double size, string color, bool advance = true, bool keepOnPage = false)
    {
        var lineHeight = size * LineFactor;
        if (advance && !keepOnPage)
        {
            this.Ensure(lineHeight);
        }

        var baseline = this.Baseline(size);
        foreach (var piece in Merge(line.Pieces))
        {
            var pieceColor = piece.Colour is { } category ? this.LabelColour(category) : piece.Link is not null ? this.design.Brand.Link : color;
            this.page.Items.Add(new TextItem(x, baseline, piece.Text, piece.Face, piece.Size, pieceColor));
            if (piece.Link is { } link && !piece.IsSpace)
            {
                this.page.Items.Add(new LineItem(x, baseline + 1.2, x + piece.Width, baseline + 1.2, 0.5, pieceColor));
                this.page.Items.Add(new LinkItem(x, this.y, piece.Width, lineHeight, link));
            }

            if (piece.Style.HasFlag(RunStyle.Strike))
            {
                var middle = baseline - (piece.Size * 0.3);
                this.page.Items.Add(new LineItem(x, middle, x + piece.Width, middle, 0.6, pieceColor));
            }

            x += piece.Width;
        }

        if (advance)
        {
            this.Advance(lineHeight, frame);
        }
    }

    // Joins neighbouring pieces in the same face, size, link and decoration into one text item.
    private static IEnumerable<Piece> Merge(IReadOnlyList<Piece> pieces)
    {
        Piece? current = null;
        foreach (var piece in pieces)
        {
            if (current is not null && current.Face == piece.Face && current.Size == piece.Size && current.Link == piece.Link
                && current.Colour == piece.Colour && current.Style.HasFlag(RunStyle.Strike) == piece.Style.HasFlag(RunStyle.Strike))
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

        void FinishLine()
        {
            lines.Add(new TextLine(current, current.Sum(p => p.Width)));
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
                FinishLine();
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
            var pieceSize = style.HasFlag(RunStyle.Code) ? size * 0.9 : size;
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
                    pieces.Add(new Piece(word, style, run.Link, face, pieceSize, this.metrics.Width(word, face, pieceSize), IsSpace: false, IsBreak: false) { Colour = run.Colour });
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
}

internal sealed record TextLine(List<Piece> Pieces, double Width);
