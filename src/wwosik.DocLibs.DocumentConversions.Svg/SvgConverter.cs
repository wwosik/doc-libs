using System.Globalization;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace wwosik.DocLibs.DocumentConversions.Svg;

/// <summary>
/// Converts SVG to PNG with SkiaSharp and Svg.Skia: any SVG, its text in the given fonts or the system's. Scripts never
/// run, and nothing outside the SVG is loaded — no files, no web addresses, only <c>data:</c> URIs and the document's
/// own elements — so an SVG from an untrusted source cannot reach the disk or the network.
/// </summary>
public static class SvgConverter
{
    /// <summary>The SVG's natural size in pixels: its <c>width</c> and <c>height</c>, else its <c>viewBox</c>.</summary>
    /// <exception cref="DocumentConversionException">The SVG cannot be read, or has no size.</exception>
    public static (double Width, double Height) Size(ReadOnlySpan<byte> svg)
    {
        using var document = Load(svg, null);
        return Natural(document);
    }

    /// <summary>The SVG as a PNG, at <see cref="SvgPngOptions.Scale"/> times its natural size or at <see cref="SvgPngOptions.Width"/>.</summary>
    /// <exception cref="DocumentConversionException">The SVG cannot be read or drawn, or has no size.</exception>
    public static byte[] ToPng(ReadOnlySpan<byte> svg, SvgPngOptions? options = null)
    {
        options ??= new SvgPngOptions();
        using var document = Load(svg, options.Fonts);
        var (width, height) = Natural(document);
        var scale = options.Width is { } wanted ? wanted / width : options.Scale;
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            throw Error("the scale must be a positive number");
        }

        if (width * scale > MaxPixels || height * scale > MaxPixels)
        {
            throw Error($"{Math.Round(width * scale)} × {Math.Round(height * scale)} pixels is larger than {MaxPixels} pixels a side");
        }

        var background = options.Background is { } hex ? Color(hex) : SKColors.Transparent;
        using var output = new MemoryStream();
        if (!document.Save(output, background, SKEncodedImageFormat.Png, 100, (float)scale, (float)scale))
        {
            throw Error("it could not be drawn");
        }

        return output.ToArray();
    }

    /// <summary>The SVG markup as a PNG; see <see cref="ToPng(ReadOnlySpan{byte}, SvgPngOptions?)"/>.</summary>
    /// <exception cref="DocumentConversionException">The SVG cannot be read or drawn, or has no size.</exception>
    public static byte[] ToPng(string svg, SvgPngOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(svg);
        return ToPng(System.Text.Encoding.UTF8.GetBytes(svg), options);
    }

    /// <summary>The largest side, in pixels, a PNG may have: beyond it the image would take gigabytes of memory.</summary>
    public const int MaxPixels = 16384;

    private static SKSvg Load(ReadOnlySpan<byte> svg, IReadOnlyList<ReadOnlyMemory<byte>>? fonts)
    {
        string clean;
        try
        {
            clean = SvgSanitizer.Clean(svg);
        }
        catch (FormatException ex)
        {
            throw Error(ex.Message, ex);
        }

        var document = new SKSvg();
        try
        {
            document.Settings.EnableJavaScript = false;
            document.Settings.EnableExternalJavaScript = false;
            if (fonts is { Count: > 0 })
            {
                var providers = document.Settings.TypefaceProviders ?? [];
                providers.Insert(0, new FontProvider(fonts));
                document.Settings.TypefaceProviders = providers;
            }

            var parameters = new SvgParameters(null, null, null, new SvgDocumentLoadOptions { ExternalResources = SvgExternalResourcePolicy.SameDocumentAndDataOnly });
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(clean));
            if (document.Load(stream, parameters) is null)
            {
                throw Error("it is not an SVG document");
            }

            return document;
        }
        catch (DocumentConversionException)
        {
            document.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            document.Dispose();
            throw Error(ex.Message, ex);
        }
    }

    // The fonts passed in, found by family name whatever their width — a narrow font is still the one asked for — and
    // in the weight and slant nearest to the text's.
    private sealed class FontProvider(IReadOnlyList<ReadOnlyMemory<byte>> fonts) : ITypefaceProvider
    {
        private readonly List<SKTypeface> typefaces = [.. fonts.Select(font => SKTypeface.FromData(SKData.CreateCopy(font.Span))).OfType<SKTypeface>()];

        public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight fontWeight, SKFontStyleWidth fontWidth, SKFontStyleSlant fontStyle)
        {
            foreach (var family in fontFamily.Split(',').Select(f => f.Trim().Trim('\'', '"')))
            {
                var match = this.typefaces
                    .Where(t => string.Equals(t.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => Math.Abs(t.FontWeight - (int)fontWeight) + (t.FontSlant == fontStyle ? 0 : 1000))
                    .FirstOrDefault();
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }
    }

    private static (double Width, double Height) Natural(SKSvg document)
    {
        var bounds = document.Picture!.CullRect;
        return bounds.Width > 0 && bounds.Height > 0
            ? (bounds.Width, bounds.Height)
            : throw Error("it has no size: give it width and height, or a viewBox");
    }

    private static SKColor Color(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        return value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : throw new ArgumentException($"'{hex}' is not a colour; expected 'RRGGBB'.", nameof(hex));
    }

    private static DocumentConversionException Error(string reason, Exception? inner = null) =>
        new(DocumentFormat.Svg, $"The SVG cannot be converted: {reason}.", inner);
}

/// <summary>How <see cref="SvgConverter"/> draws a PNG.</summary>
public sealed record SvgPngOptions
{
    /// <summary>Pixels per pixel of the SVG's natural size. Default 2, sharp on high-density screens and when enlarged.</summary>
    public double Scale { get; init; } = 2;

    /// <summary>The PNG's width in pixels, the height following; overrides <see cref="Scale"/>. Default: none.</summary>
    public double? Width { get; init; }

    /// <summary>A colour filling the PNG first, as RGB hex (<c>FFFFFF</c>); <see langword="null"/> (the default): transparent.</summary>
    public string? Background { get; init; }

    /// <summary>
    /// TrueType or OpenType fonts used for the SVG's <c>font-family</c> names before the system's, such as a brand's
    /// fonts. A family that neither has is drawn in the system's default font.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fonts { get; init; } = [];
}
