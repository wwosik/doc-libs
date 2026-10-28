using System.Text;

namespace wwosik.DocLibs.DocumentConversions.Svg;

/// <summary>
/// Lets the document generators place <c>.svg</c> images: register it in their options
/// (<c>ImageConverters = [new SvgImageConverter()]</c>), and each SVG becomes a PNG at the size it is placed, twice as
/// sharp. Takes resources of type <c>image/svg+xml</c>, a path ending in <c>.svg</c>, or content starting like SVG.
/// </summary>
public sealed class SvgImageConverter : IImageConverter
{
    /// <summary>Fonts used for the SVG's <c>font-family</c> names before the system's, such as the brand's.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fonts { get; init; } = [];

    /// <inheritdoc/>
    public ConvertedImage? Convert(DocumentResource image, double scale)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!IsSvg(image))
        {
            return null;
        }

        try
        {
            var (width, height) = SvgConverter.Size(image.Content.Span);
            var png = SvgConverter.ToPng(image.Content.Span, new SvgPngOptions { Scale = Math.Max(scale, 0.01), Fonts = this.Fonts });
            return new ConvertedImage(png, "image/png", width, height);
        }
        catch (DocumentConversionException ex)
        {
            throw new DocumentConversionException(DocumentFormat.Svg, $"The image '{image.Path}' cannot be converted: {ex.Reason}", ex);
        }
    }

    private static bool IsSvg(DocumentResource image)
    {
        if (image.ContentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
            || image.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Content that starts like XML and names an svg element early on.
        var head = Encoding.UTF8.GetString(image.Content.Span[..Math.Min(image.Content.Length, 1024)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return (head.StartsWith("<svg", StringComparison.Ordinal) || head.StartsWith("<?xml", StringComparison.Ordinal))
            && head.Contains("<svg", StringComparison.Ordinal);
    }
}
