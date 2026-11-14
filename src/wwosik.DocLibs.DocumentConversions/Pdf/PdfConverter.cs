using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace wwosik.DocLibs.DocumentConversions.Pdf;

/// <summary>
/// Converts PDF documents into Markdown, one section per page. Text comes from the text layer only, so scanned
/// pages come out empty. Words are grouped into lines and lines into paragraphs, lists, code blocks and headings;
/// ruled tables become pipe tables, footnotes found at the foot of a page become Markdown footnotes, and images
/// stand where they are on the page. Running headers and footers, images that appear on every page, and rotated
/// text such as watermarks are left out.
/// </summary>
public sealed partial class PdfConverter : IDocumentConverter
{
    /// <summary>
    /// Called before a page is analysed, with <see langword="true"/> for the structured pass and
    /// <see langword="false"/> for the plain one. Tests throw from it to check the fallbacks.
    /// </summary>
    internal Action<Page, bool>? AnalysisStarting { get; init; }

    /// <inheritdoc/>
    public bool CanConvert(DocumentFormat format) => format == DocumentFormat.Pdf;

    /// <inheritdoc/>
    public MarkdownDocument Convert(Stream input, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            return this.ConvertDocument(input, options);
        }
        catch (Exception ex) when (ex is not (DocumentConversionException or OutOfMemoryException))
        {
            throw new DocumentConversionException(DocumentFormat.Pdf, "The PDF could not be read: " + ex.Message, ex);
        }
    }

    private MarkdownDocument ConvertDocument(Stream input, ConversionOptions options)
    {
        using var buffer = input.CanSeek ? null : new MemoryStream();
        if (buffer is not null)
        {
            input.CopyTo(buffer);
            buffer.Position = 0;
        }

        using var document = PdfDocument.Open(buffer ?? input);
        var pages = new List<PageContent>();
        foreach (var page in document.GetPages())
        {
            var text = this.ReadPage(page);
            var (images, skipped) = options.IncludeImages ? PageImages(page) : ([], 0);
            pages.Add(new PageContent(page.Number, text, images, skipped));
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MetadataKeys.PageCount] = document.NumberOfPages.ToString(CultureInfo.InvariantCulture),
        };
        if (document.Information.Title?.Trim() is { Length: > 0 } title)
        {
            metadata[MetadataKeys.Title] = title;
        }

        if (document.Information.Author?.Trim() is { Length: > 0 } author)
        {
            metadata[MetadataKeys.Author] = author;
        }

        var skippedImages = pages.Sum(p => p.SkippedImages);
        if (skippedImages > 0)
        {
            metadata[MetadataKeys.SkippedImages] = skippedImages.ToString(CultureInfo.InvariantCulture);
        }

        var resources = new ResourceCollector(options.ResourcePathPrefix);
        var markdown = Render(pages, resources);
        return new MarkdownDocument(markdown, resources.Resources, metadata);
    }

    // The structured analysis; if it fails, the plain paragraphs; if those fail too, the text in content order.
    private PageText ReadPage(Page page)
    {
        try
        {
            this.AnalysisStarting?.Invoke(page, true);
            return Analyse(page, structured: true);
        }
        catch (Exception)
        {
            // Falls through to the plain analysis.
        }

        try
        {
            this.AnalysisStarting?.Invoke(page, false);
            return Analyse(page, structured: false);
        }
        catch (Exception)
        {
            return new PageText(FallbackParagraphs(page), []);
        }
    }

    private enum BlockKind
    {
        Paragraph,
        Item,
        Table,
        Code,
        Image,
    }

    /// <summary>A piece of a page's Markdown; <see cref="Top"/> is where it starts on the page (PDF y, upwards).</summary>
    private sealed record PageBlock(double Top, BlockKind Kind, string Markdown)
    {
        public bool IsText => this.Kind is BlockKind.Paragraph or BlockKind.Item;

        /// <summary>An item that starts a new list right after another one (a different marker at the same level).</summary>
        public bool Separate { get; init; }
    }

    /// <summary>A page's blocks in reading order, and the footnote definitions that go at the end of its section.</summary>
    private sealed record PageText(List<PageBlock> Blocks, List<string> Footnotes);

    private sealed record PageContent(int Number, PageText Text, List<PageImage> Images, int SkippedImages);

    private sealed record PageImage(string Hash, byte[] Content, string Extension, double Top);

    private static string Render(List<PageContent> pages, ResourceCollector resources)
    {
        // An image on every page is a logo or letterhead, not content.
        var repeatedImages = pages.Count < 2
            ? []
            : pages.SelectMany(p => p.Images.Select(i => i.Hash).Distinct())
                .GroupBy(h => h)
                .Where(g => g.Count() == pages.Count)
                .Select(g => g.Key)
                .ToHashSet();
        var images = pages.ToDictionary(p => p.Number, p => p.Images.Where(i => !repeatedImages.Contains(i.Hash)).ToList());
        if (pages.All(p => p.Text.Blocks.Count == 0 && p.Text.Footnotes.Count == 0 && images[p.Number].Count == 0))
        {
            return string.Empty;
        }

        // Running header or footer: one of the first or last two text blocks of a page whose text,
        // digits ignored, recurs in that position on at least three pages.
        var boilerplate = new HashSet<string>();
        if (pages.Count >= 3)
        {
            boilerplate = pages
                .SelectMany(p => Edges(p.Text.Blocks).Where(t => t.Length < 200).Select(Key).Distinct())
                .GroupBy(k => k)
                .Where(g => g.Count() >= 3)
                .Select(g => g.Key)
                .ToHashSet();
        }

        var builder = new StringBuilder();
        foreach (var page in pages)
        {
            builder.Append("## Page ").Append(page.Number.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
            var edge = Edges(page.Text.Blocks).ToHashSet();
            var flow = page.Text.Blocks.Where(b => !(b.IsText && edge.Contains(b.Markdown) && boilerplate.Contains(Key(b.Markdown)))).ToList();
            var index = 0;
            var placed = images[page.Number].OrderByDescending(i => i.Top).Select(image =>
            {
                var name = $"page{page.Number}-image{++index}{image.Extension}";
                return new PageBlock(image.Top, BlockKind.Image, "![](" + MarkdownText.Destination(resources.Add(name, image.Content)) + ")");
            }).ToList();

            PageBlock? previous = null;
            foreach (var block in Merge(flow, placed))
            {
                // Consecutive list items stay one tight list.
                if (previous is not null)
                {
                    builder.Append(previous.Kind == BlockKind.Item && block.Kind == BlockKind.Item && !block.Separate ? "\n" : "\n\n");
                }

                builder.Append(block.Markdown);
                previous = block;
            }

            builder.Append("\n\n");
            foreach (var footnote in page.Text.Footnotes)
            {
                builder.Append(footnote).Append("\n\n");
            }
        }

        return MarkdownText.Normalize(builder.ToString());
    }

    private static IEnumerable<string> Edges(List<PageBlock> blocks)
    {
        var text = blocks.Where(b => b.IsText).Select(b => b.Markdown).ToList();
        return text.Take(2).Concat(text.TakeLast(2));
    }

    // Blocks placed by position (tables, images) go before the first flow block that starts below them.
    private static List<PageBlock> Merge(List<PageBlock> flow, IEnumerable<PageBlock> placed)
    {
        var pending = new Queue<PageBlock>(placed.OrderByDescending(b => b.Top));
        var result = new List<PageBlock>(flow.Count + pending.Count);
        foreach (var block in flow)
        {
            while (pending.Count > 0 && pending.Peek().Top > block.Top)
            {
                result.Add(pending.Dequeue());
            }

            result.Add(block);
        }

        result.AddRange(pending);
        return result;
    }

    // Top to bottom; JPEG data is kept as it is, everything else is re-encoded as PNG.
    private static (List<PageImage> Images, int Skipped) PageImages(Page page)
    {
        var images = new List<PageImage>();
        var skipped = 0;
        foreach (var image in page.GetImages().OrderByDescending(i => i.BoundingBox.Top))
        {
            byte[] content;
            string extension;
            if (image.RawMemory.Span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            {
                content = image.RawMemory.ToArray();
                extension = ".jpg";
            }
            else if (TryGetPng(image, out var png))
            {
                content = png;
                extension = ".png";
            }
            else
            {
                skipped++;
                continue;
            }

            images.Add(new PageImage(System.Convert.ToHexString(SHA256.HashData(content)), content, extension, image.BoundingBox.Top));
        }

        return (images, skipped);
    }

    private static bool TryGetPng(IPdfImage image, out byte[] png)
    {
        try
        {
            if (image.TryGetPng(out var bytes) && bytes is { Length: > 0 })
            {
                png = bytes;
                return true;
            }
        }
        catch (Exception)
        {
            // Unsupported filters or colour spaces count as undecodable.
        }

        png = [];
        return false;
    }

    private static string Key(string paragraph) => KeyNoise().Replace(paragraph.ToLowerInvariant(), string.Empty);

    // The page's text in content order, one paragraph per line, placed before the page's images.
    private static List<PageBlock> FallbackParagraphs(Page page)
    {
        var rotated = page.Letters.Any(l => l.TextOrientation != TextOrientation.Horizontal && !string.IsNullOrWhiteSpace(l.Value));
        return [.. (rotated ? HorizontalText(page) : ContentOrderTextExtractor.GetText(page))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select((l, i) => new PageBlock((page.Height * 2) - i, BlockKind.Paragraph, MarkdownText.EscapeLineStart(MarkdownText.Escape(l))))];
    }

    // The horizontal letters in content order: a new line where the baseline moves or the text goes back, a space at a gap.
    private static string HorizontalText(Page page)
    {
        var builder = new StringBuilder();
        Letter? previous = null;
        foreach (var letter in page.Letters.Where(l => l.TextOrientation == TextOrientation.Horizontal))
        {
            if (previous is not null)
            {
                if (Math.Abs(letter.StartBaseLine.Y - previous.StartBaseLine.Y) > previous.PointSize * 0.5 || letter.StartBaseLine.X < previous.StartBaseLine.X - 1)
                {
                    builder.Append('\n');
                }
                else if (letter.StartBaseLine.X - previous.EndBaseLine.X > previous.PointSize * 0.15)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(letter.Value);
            previous = letter;
        }

        return builder.ToString();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(x => x > 0).OrderBy(x => x).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    [GeneratedRegex(@"\d+|\s+|[#*\-\\]")]
    private static partial Regex KeyNoise();
}
