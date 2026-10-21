using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace wwosik.DocLibs.DocumentConversions.Pdf;

/// <summary>
/// Converts PDF documents into Markdown, one section per page. Text comes from the text layer only,
/// so scanned pages come out empty. Words are grouped into lines and lines into paragraphs; running
/// headers and footers are removed, and so are images that appear on every page.
/// </summary>
public sealed partial class PdfConverter : IDocumentConverter
{
    /// <inheritdoc/>
    public bool CanConvert(DocumentFormat format) => format == DocumentFormat.Pdf;

    /// <inheritdoc/>
    public MarkdownDocument Convert(Stream input, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            return ConvertDocument(input, options);
        }
        catch (Exception ex) when (ex is not (DocumentConversionException or OutOfMemoryException))
        {
            throw new DocumentConversionException(DocumentFormat.Pdf, "The PDF could not be read: " + ex.Message, ex);
        }
    }

    private static MarkdownDocument ConvertDocument(Stream input, ConversionOptions options)
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
            List<string> paragraphs;
            try
            {
                paragraphs = PageParagraphs(page);
            }
            catch (Exception)
            {
                paragraphs = FallbackParagraphs(page);
            }

            var (images, skipped) = options.IncludeImages ? PageImages(page) : ([], 0);
            pages.Add(new PageContent(page.Number, paragraphs, images, skipped));
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

    private sealed record PageContent(int Number, List<string> Paragraphs, List<PageImage> Images, int SkippedImages);

    private sealed record PageImage(string Hash, byte[] Content, string Extension);

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
        if (pages.All(p => p.Paragraphs.Count == 0 && images[p.Number].Count == 0))
        {
            return string.Empty;
        }

        // Running header or footer: one of the first or last two paragraphs of a page whose text,
        // digits ignored, recurs in that position on at least three pages.
        var boilerplate = new HashSet<string>();
        if (pages.Count >= 3)
        {
            boilerplate = pages
                .SelectMany(p => p.Paragraphs.Take(2).Concat(p.Paragraphs.TakeLast(2))
                    .Where(t => t.Length < 200).Select(Key).Distinct())
                .GroupBy(k => k)
                .Where(g => g.Count() >= 3)
                .Select(g => g.Key)
                .ToHashSet();
        }

        var builder = new StringBuilder();
        foreach (var page in pages)
        {
            builder.Append("## Page ").Append(page.Number.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
            string? previous = null;
            var edge = page.Paragraphs.Take(2).Concat(page.Paragraphs.TakeLast(2)).ToHashSet();
            foreach (var paragraph in page.Paragraphs.Where(p => !(edge.Contains(p) && boilerplate.Contains(Key(p)))))
            {
                // Consecutive list items stay one tight list.
                var tight = previous is not null && previous.StartsWith("- ", StringComparison.Ordinal) && paragraph.StartsWith("- ", StringComparison.Ordinal);
                if (previous is not null)
                {
                    builder.Append(tight ? "\n" : "\n\n");
                }

                builder.Append(paragraph);
                previous = paragraph;
            }

            builder.Append("\n\n");
            var index = 0;
            foreach (var image in images[page.Number])
            {
                var name = $"page{page.Number}-image{++index}{image.Extension}";
                builder.Append("![](").Append(MarkdownText.Destination(resources.Add(name, image.Content))).Append(")\n\n");
            }
        }

        return MarkdownText.Normalize(builder.ToString());
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

            images.Add(new PageImage(System.Convert.ToHexString(SHA256.HashData(content)), content, extension));
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

    private static List<string> PageParagraphs(Page page)
    {
        var lines = BuildLines(page);
        if (lines.Count == 0)
        {
            return [];
        }

        var body = page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value) && l.PointSize > 0)
            .GroupBy(l => Math.Round(l.PointSize * 2) / 2)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (body <= 0)
        {
            body = Median(lines.Select(l => l.Size));
        }

        var marginTop = page.Height * 0.92;
        var marginBottom = page.Height * 0.08;
        var rights = lines.Select(l => l.Right).OrderBy(x => x).ToList();
        var lefts = lines.Select(l => l.Left).OrderBy(x => x).ToList();
        var textRight = rights[(int)(rights.Count * 0.9)];
        var textLeft = lefts[(int)(lefts.Count * 0.1)];
        var width = Math.Max(textRight - textLeft, 1);
        var gaps = lines.Zip(lines.Skip(1), (a, b) => a.Bottom - b.Top).Where(g => g > 0).ToList();
        var typicalGap = gaps.Count == 0 ? body * 0.3 : Median(gaps);

        var output = new List<string>();
        var current = new StringBuilder();
        var currentLines = new List<Line>();
        var isItem = false;

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length > 0)
            {
                var size = currentLines.Average(l => l.Size);
                var bold = currentLines.All(l => l.Bold);
                var heading = !isItem && text.Length < 100
                    && !TableOfContentsEntry().IsMatch(text)
                    && (StartsNumbered(text) || !(text.EndsWith('.') || text.EndsWith(':')))
                    && (size >= body * 1.15 || (bold && StartsNumbered(text)));
                var escaped = MarkdownText.Escape(text);
                output.Add(heading ? "### " + escaped : isItem ? "- " + escaped : MarkdownText.EscapeLineStart(escaped));
            }

            current.Clear();
            currentLines.Clear();
            isItem = false;
        }

        Line? previous = null;
        foreach (var line in lines)
        {
            var text = line.Text;
            var item = StripBullet(text);
            if (previous is not null)
            {
                var gap = previous.Bottom - line.Top;
                var breakHere = item is not null
                    || StartsNumbered(text)
                    || InMargin(previous, marginTop, marginBottom) || InMargin(line, marginTop, marginBottom)
                    || gap > typicalGap + (body * 0.35)
                    || previous.Right < textRight - (width * 0.15)                    // the previous line ended early
                    || line.Left < previous.Left - (body * 1.5)                       // an outdent ends a hanging item
                    || Math.Abs(line.Size - previous.Size) > Math.Max(previous.Size, 1) * 0.15;
                if (breakHere)
                {
                    Flush();
                }
            }

            if (item is not null)
            {
                isItem = true;
                text = item;
            }

            Join(current, text);
            currentLines.Add(line);
            previous = line;
        }

        Flush();
        return output;
    }

    private static bool InMargin(Line line, double top, double bottom) => line.Top > top || line.Bottom < bottom;

    private sealed record Line(string Text, double Left, double Right, double Top, double Bottom, double Size, bool Bold);

    // Groups words into visual lines by baseline; PDF y grows upwards.
    private static List<Line> BuildLines(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.TextOrientation == TextOrientation.Horizontal)
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var rows = new List<List<Word>>();
        foreach (var word in words)
        {
            var row = rows.Count == 0 ? null : rows[^1];
            var tolerance = Math.Max(word.BoundingBox.Height, 2) * 0.5;
            if (row is not null && Math.Abs(row.Average(x => x.BoundingBox.Bottom) - word.BoundingBox.Bottom) <= tolerance)
            {
                row.Add(word);
            }
            else
            {
                rows.Add([word]);
            }
        }

        return [.. rows.Select(r =>
        {
            var ordered = r.OrderBy(w => w.BoundingBox.Left).ToList();
            var letters = ordered.SelectMany(w => w.Letters).ToList();
            var bold = letters.Count > 0
                && letters.Count(l => (l.FontName ?? string.Empty).Contains("Bold", StringComparison.OrdinalIgnoreCase)) >= letters.Count * 0.8;
            return new Line(
                string.Join(' ', ordered.Select(w => w.Text)).Trim(),
                ordered.Min(w => w.BoundingBox.Left),
                ordered.Max(w => w.BoundingBox.Right),
                ordered.Max(w => w.BoundingBox.Top),
                ordered.Min(w => w.BoundingBox.Bottom),
                Median(letters.Select(l => l.PointSize)),
                bold);
        })];
    }

    // Joins a line onto the paragraph and repairs words hyphenated across lines.
    private static void Join(StringBuilder current, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (current.Length == 0)
        {
            current.Append(text);
            return;
        }

        var n = current.Length;
        if (current[n - 1] == '-' && n >= 2 && char.IsLetter(current[n - 2]))
        {
            if (char.IsLower(current[n - 2]) && char.IsLower(text[0]))
            {
                current.Length--; // "Netz-|nutzung" joins, "GELI-|Anmeldung" keeps its hyphen.
            }

            current.Append(text);
            return;
        }

        current.Append(' ').Append(text);
    }

    // The text after a leading bullet glyph, or null if the line is no bullet item.
    private static string? StripBullet(string text)
    {
        if (text.Length < 2)
        {
            return null;
        }

        var c = text[0];
        var strong = c is '•' or '▪' or '■' or '◦' or '·' or '●' or '‣' or '⁃' or '➢' or '→' or (>= '\uF000' and <= '\uF0FF');
        var weak = c is '-' or '–' or '*' or 'o';
        if (strong)
        {
            return text[1..].TrimStart();
        }

        return weak && char.IsWhiteSpace(text[1]) ? text[2..].TrimStart() : null;
    }

    // "1.", "2.3", "4.1.2 ": numbered headings and items start a new paragraph.
    private static bool StartsNumbered(string text) => NumberedStart().IsMatch(text);

    private static List<string> FallbackParagraphs(Page page) =>
        [.. ContentOrderTextExtractor.GetText(page)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => MarkdownText.EscapeLineStart(MarkdownText.Escape(l)))];

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(x => x > 0).OrderBy(x => x).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    [GeneratedRegex(@"\d+|\s+|[#*\-\\]")]
    private static partial Regex KeyNoise();

    [GeneratedRegex(@"\.{4,}\s*\d+$")]
    private static partial Regex TableOfContentsEntry();

    [GeneratedRegex(@"^\d{1,2}(\.\d{1,2})*\.?\s+\S")]
    private static partial Regex NumberedStart();
}
