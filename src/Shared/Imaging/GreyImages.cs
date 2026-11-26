using System.Runtime.CompilerServices;
using SkiaSharp;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>.grey</c> on an image: its pixels turned grey with SkiaSharp, by their luminance (Rec. 709), alpha kept; a JPEG
/// stays a JPEG at quality 90, anything else (PNG, GIF) becomes a PNG. SkiaSharp is only loaded here, so documents
/// without grey images never need its native library. Linked source, compiled into the PDF and the DOCX generator.
/// </summary>
internal static class GreyImages
{
    private const int JpegQuality = 90;

    // Rec. 709 luminance in each of red, green and blue; alpha unchanged.
    private static readonly float[] Luminance =
    [
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0,
    ];

    /// <exception cref="DocumentConversionException">
    /// The image cannot be read, or SkiaSharp's native library is missing (on Linux: the package
    /// <c>SkiaSharp.NativeAssets.Linux</c>).
    /// </exception>
    public static DocumentResource Convert(DocumentResource image, ImageFormat format, Format target)
    {
        try
        {
            return Skia(image, format, target);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new DocumentConversionException(
                target,
                $"The image '{image.Path}' cannot be made grey: SkiaSharp's native library is missing. Add the package SkiaSharp.NativeAssets.Linux (or SkiaSharp.NativeAssets.Linux.NoDependencies) to the application on Linux. {ex.Message}",
                ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DocumentResource Skia(DocumentResource image, ImageFormat format, Format target)
    {
        using var source = SKBitmap.Decode(image.Content.Span)
            ?? throw new DocumentConversionException(target, $"The image '{image.Path}' could not be read to make it grey.");
        using var grey = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, source.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul));
        using (var canvas = new SKCanvas(grey))
        using (var filter = SKColorFilter.CreateColorMatrix(Luminance))
        using (var paint = new SKPaint { ColorFilter = filter })
        using (var pixels = SKImage.FromBitmap(source))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(pixels, 0, 0, SKSamplingOptions.Default, paint);
        }

        var png = format != ImageFormat.Jpeg;
        using var data = grey.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, png ? 100 : JpegQuality)
            ?? throw new DocumentConversionException(target, $"The image '{image.Path}' could not be written in grey.");
        return new DocumentResource(image.Path, png ? "image/png" : "image/jpeg", data.ToArray());
    }
}
