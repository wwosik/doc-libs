using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QRCoder;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>![Survey](qr:https://example.com/survey)</c>: a QR code for the text after <c>qr:</c>, taken literally, drawn as a
/// <see cref="Drawing"/>. QRCoder (MIT) encodes it, error correction M, byte mode (UTF-8), in the smallest version that
/// holds it; only its module matrix is used.
/// </summary>
internal static partial class QrDrawing
{
    /// <summary>What an image URL starts with to be a QR code.</summary>
    public const string Prefix = "qr:";

    /// <summary>The most bytes (UTF-8) a code may hold: anything denser cannot be scanned from a slide.</summary>
    public const int MaxBytes = 1000;

    /// <summary>The pixels per module at scale 1.</summary>
    public const double ModuleSize = 4;

    private const string White = "FFFFFF";
    private const string Black = "000000";
    private const double MinContrast = 4.5;
    private const double CaptionSize = 13;
    private const double CaptionGap = 6;

    /// <summary>Whether the image URL is a QR code.</summary>
    public static bool IsQr(string url) => url.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The code for an image <c>![alt](qr:…  "title"){attributes}</c>: <c>scale=</c> (in the title or the attributes)
    /// multiplies the module size, and <c>caption</c> in the title or <c>.caption</c> puts the alt text under the code.
    /// Returns whether a scale was given, which decks take as the size to keep.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// Another attribute than <c>scale</c> and <c>.caption</c>, scale given twice, or content that cannot be encoded.
    /// </exception>
    public static (Drawing Drawing, bool Scaled) ForImage(
        string url, string altText, string? title, ImageAttributes attributes, ResolvedBrand brand, int line, DocumentFormat format)
    {
        var at = line > 0 ? string.Create(CultureInfo.InvariantCulture, $" at line {line}") : string.Empty;
        if (attributes is { Fit: ImageFit.Cover } or { Focus: not null } or { Round: true } or { Grey: true })
        {
            throw new DocumentConversionException(format, $"The QR code{at} takes only the attributes scale=<number> and .caption, not '{attributes.Text}'.");
        }

        double? titleScale = title is not null && ScalePattern().Match(title) is { Success: true } match
            && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
        if (titleScale is not null && attributes.Scale is not null)
        {
            throw new DocumentConversionException(format, $"The QR code{at} has scale both in its title and in its attributes '{attributes.Text}'; keep one.");
        }

        var scale = titleScale ?? attributes.Scale;
        var caption = (title is not null && CaptionPattern().IsMatch(title)) || attributes.Caption ? altText : null;
        return (Draw(url[Prefix.Length..], brand, caption, scale ?? 1, line, format), scale is not null);
    }

    /// <summary>
    /// The code for <paramref name="text"/>: a white square with a quiet zone of four modules, and one path of every
    /// dark module (a rectangle per horizontal run), in the brand's primary colour, or its diagram text colour when the
    /// primary has less than 4.5:1 contrast to white, or black. <paramref name="caption"/>, if given, goes under it.
    /// </summary>
    /// <exception cref="DocumentConversionException">The text is empty or longer than <see cref="MaxBytes"/>.</exception>
    public static Drawing Draw(string text, ResolvedBrand brand, string? caption, double scale, int line, DocumentFormat format)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var at = line > 0 ? string.Create(CultureInfo.InvariantCulture, $" at line {line}") : string.Empty;
        if (bytes.Length == 0)
        {
            throw new DocumentConversionException(format, $"The QR code{at} has no content: write it after 'qr:', such as qr:https://example.com.");
        }

        if (bytes.Length > MaxBytes)
        {
            throw new DocumentConversionException(
                format,
                string.Create(CultureInfo.InvariantCulture, $"The QR code{at} holds {bytes.Length} bytes; at most {MaxBytes} can be scanned from a slide or page. Use a shorter address."));
        }

        var modules = Matrix(bytes);
        var size = modules.Count;
        var unit = ModuleSize * scale;
        var path = new List<PathSegment>();
        for (var row = 0; row < size; row++)
        {
            var column = 0;
            while (column < size)
            {
                if (!modules[row][column])
                {
                    column++;
                    continue;
                }

                var start = column;
                while (column < size && modules[row][column])
                {
                    column++;
                }

                path.AddRange(DrawingPath.Rectangle(start * unit, row * unit, (column - start) * unit, unit).Segments);
            }
        }

        var side = size * unit;
        var items = new List<DrawingItem>
        {
            new DrawingShape(DrawingPath.Rectangle(0, 0, side, side), White, null, 0) { Kind = DrawingShapeKind.Rectangle },
            new DrawingShape(new DrawingPath(path), Colour(brand), null, 0),
        };
        var height = side;
        if (caption is { Length: > 0 })
        {
            var captionSize = CaptionSize * Math.Max(scale, 0.5);
            height += CaptionGap + (captionSize * 1.3);
            items.Add(new DrawingText(side / 2, side + CaptionGap + captionSize, caption, captionSize, brand.Diagram.Text)
            {
                Anchor = TextAnchor.Middle,
                MaxWidth = side,
            });
        }

        return new Drawing(side, height, items);
    }

    /// <summary>The modules, quiet zone included, row by row: true for dark.</summary>
    internal static List<bool[]> Matrix(byte[] bytes)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(bytes, QRCodeGenerator.ECCLevel.M);
        return [.. data.ModuleMatrix.Select(row => Enumerable.Range(0, row.Length).Select(i => row[i]).ToArray())];
    }

    /// <summary>The colour of the modules: dark on the white square, whatever the slide's background.</summary>
    internal static string Colour(ResolvedBrand brand) =>
        Contrast(brand.Primary, White) >= MinContrast ? brand.Primary
        : Contrast(brand.Diagram.Text, White) >= MinContrast ? brand.Diagram.Text
        : Black;

    /// <summary>The WCAG contrast ratio of two RRGGBB colours, from 1 to 21.</summary>
    internal static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    [GeneratedRegex(@"scale\s*=\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"(?<![\w-])caption(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CaptionPattern();
}
