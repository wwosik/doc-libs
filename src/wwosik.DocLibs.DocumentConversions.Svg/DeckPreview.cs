using System.Globalization;
using SkiaSharp;

namespace wwosik.DocLibs.DocumentConversions.Svg;

/// <summary>How <see cref="DeckPreview.Render"/> paints the slides.</summary>
public sealed record DeckPreviewOptions
{
    /// <summary>Pixels per slide, across; the height follows the slide's aspect ratio (720 on 16:9 at the default 1280).</summary>
    public int Width { get; init; } = 1280;

    /// <summary>
    /// The faces to paint with, normally <c>DeckSvgResult.Fonts</c>: the faces the slides' lines were broken with, so a
    /// slide is never painted in a system face.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fonts { get; init; } = [];

    /// <summary>What is behind a slide without a background, as RGB hex; default white, as in PowerPoint.</summary>
    public string Background { get; init; } = "FFFFFF";
}

/// <summary>The slides painted by <see cref="DeckPreview.Render"/>.</summary>
/// <param name="Slides">Each slide as a PNG, in the order given.</param>
public sealed record DeckPreviewResult(IReadOnlyList<SlidePreview> Slides);

/// <summary>One slide as a PNG.</summary>
/// <param name="Number">The slide's position in the deck, from 1, hidden slides counted.</param>
/// <param name="Title">The slide's title; empty when it has none.</param>
/// <param name="Png">The PNG.</param>
public sealed record SlidePreview(int Number, string Title, ReadOnlyMemory<byte> Png);

/// <summary>How <see cref="DeckPreview.ContactSheet"/> lays out the slides.</summary>
public sealed record ContactSheetOptions
{
    /// <summary>Thumbnails per row; default 4.</summary>
    public int Columns { get; init; } = 4;

    /// <summary>The width of each thumbnail in pixels, the height following the slide's; default 320.</summary>
    public int ThumbnailWidth { get; init; } = 320;

    /// <summary>The space around and between the thumbnails in pixels; default 16.</summary>
    public int Gap { get; init; } = 16;

    /// <summary>Whether each thumbnail has its slide number under it; default on.</summary>
    public bool Labels { get; init; } = true;

    /// <summary>
    /// The TrueType face of the labels, normally <c>DeckSvgResult.LabelFont</c>. Without one, the labels are set in
    /// SkiaSharp's default typeface, and where the platform has none, the sheet has no labels.
    /// </summary>
    public ReadOnlyMemory<byte> LabelFont { get; init; }

    /// <summary>The sheet's colour behind the thumbnails, as RGB hex; default light grey <c>F2F2F2</c>.</summary>
    public string Background { get; init; } = "F2F2F2";
}

/// <summary>
/// Previews of a deck: each slide as a PNG, and a contact sheet of all slides as one PNG, painted with SkiaSharp from
/// the slide SVGs that <c>DeckSvg.Write</c> in <c>wwosik.DocLibs.DocumentGeneration.Pptx</c> writes, so a preview shows
/// what the HTML deck shows. No browser, no Office, no network.
/// <code>
/// var svgs = DeckSvg.Write(pptx);
/// var previews = DeckPreview.Render(svgs.Slides, new() { Fonts = svgs.Fonts });
/// var sheet = DeckPreview.ContactSheet(previews, new() { LabelFont = svgs.LabelFont });
/// </code>
/// </summary>
public static class DeckPreview
{
    private static readonly SKColor Border = new(0xBF, 0xBF, 0xBF);
    private static readonly SKColor LabelColor = new(0x40, 0x40, 0x40);

    /// <summary>Paints each slide as a PNG of <see cref="DeckPreviewOptions.Width"/> pixels across.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width is not between 1 and <see cref="SvgConverter.MaxPixels"/>.</exception>
    /// <exception cref="DocumentConversionException">A slide cannot be painted; the message names it.</exception>
    public static DeckPreviewResult Render(IReadOnlyList<SlideSvg> slides, DeckPreviewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        options ??= new DeckPreviewOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 1, nameof(options.Width));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Width, SvgConverter.MaxPixels, nameof(options.Width));
        var png = new SvgPngOptions { Width = options.Width, Fonts = options.Fonts, Background = options.Background };
        var previews = new List<SlidePreview>(slides.Count);
        foreach (var slide in slides)
        {
            try
            {
                previews.Add(new SlidePreview(slide.Number, slide.Title, SvgConverter.ToPng(slide.Svg.Span, png)));
            }
            catch (DocumentConversionException ex)
            {
                throw new DocumentConversionException(DocumentFormat.Svg, $"Slide {slide.Number} cannot be painted: {ex.Reason}", ex);
            }
        }

        return new DeckPreviewResult(previews);
    }

    /// <summary>
    /// All slides as one PNG: thumbnails of <see cref="ContactSheetOptions.ThumbnailWidth"/> in rows of
    /// <see cref="ContactSheetOptions.Columns"/>, each with a hairline border and its slide number under it.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// There are no slides, an option is out of range, or the sheet would be larger than <see cref="SvgConverter.MaxPixels"/>
    /// a side; the message then names the largest thumbnail width that fits.
    /// </exception>
    public static byte[] ContactSheet(DeckPreviewResult slides, ContactSheetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(slides);
        options ??= new ContactSheetOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Columns, 1, nameof(options.Columns));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ThumbnailWidth, 1, nameof(options.ThumbnailWidth));
        ArgumentOutOfRangeException.ThrowIfNegative(options.Gap, nameof(options.Gap));
        if (slides.Slides.Count == 0)
        {
            throw new ArgumentException("A contact sheet needs at least one slide.", nameof(slides));
        }

        var background = Color(options.Background);
        var images = new List<SKImage>(slides.Slides.Count);
        try
        {
            foreach (var slide in slides.Slides)
            {
                images.Add(SKImage.FromEncodedData(slide.Png.Span)
                    ?? throw new ArgumentException($"Slide {slide.Number} is not a PNG.", nameof(slides)));
            }

            var ratios = images.Select(i => (double)i.Height / i.Width).ToList();
            using var typeface = options.Labels ? LabelTypeface(options.LabelFont) : null;
            var labels = typeface is not null;
            var columns = Math.Min(options.Columns, images.Count);
            var layout = Layout(ratios, columns, options.ThumbnailWidth, options.Gap, labels);
            if (layout.Width > SvgConverter.MaxPixels || layout.Height > SvgConverter.MaxPixels)
            {
                var fits = Enumerable.Range(1, options.ThumbnailWidth - 1).Reverse()
                    .FirstOrDefault(w => Layout(ratios, columns, w, options.Gap, labels) is var l && l.Width <= SvgConverter.MaxPixels && l.Height <= SvgConverter.MaxPixels);
                var hint = fits > 0
                    ? $"Use a ThumbnailWidth of at most {fits.ToString(CultureInfo.InvariantCulture)}, more Columns, or fewer slides."
                    : "Use more Columns, or fewer slides per sheet.";
                throw new ArgumentException(
                    $"The contact sheet would be {layout.Width} × {layout.Height} pixels, larger than {SvgConverter.MaxPixels} a side. {hint}",
                    nameof(options));
            }

            using var surface = SKSurface.Create(new SKImageInfo(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("SkiaSharp could not create the contact sheet.");
            var canvas = surface.Canvas;
            canvas.Clear(background);
            using var border = new SKPaint { Color = Border, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
            using var text = new SKPaint { Color = LabelColor, IsAntialias = true };
            using var font = typeface is null ? null : new SKFont(typeface, layout.LabelSize);
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            for (var i = 0; i < images.Count; i++)
            {
                var (x, y, width, height) = layout.Cells[i];
                var target = new SKRect(x, y, x + width, y + height);
                canvas.DrawImage(images[i], target, sampling);
                canvas.DrawRect(new SKRect(x - 0.5f, y - 0.5f, x + width + 0.5f, y + height + 0.5f), border);
                if (font is not null)
                {
                    var label = slides.Slides[i].Number.ToString(CultureInfo.InvariantCulture);
                    canvas.DrawText(label, x + (width / 2f), y + height + (layout.LabelSize * 1.25f), SKTextAlign.Center, font, text);
                }
            }

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    private sealed record SheetLayout(int Width, int Height, float LabelSize, IReadOnlyList<(int X, int Y, int Width, int Height)> Cells);

    // Rows of `columns` cells; each row as tall as its tallest thumbnail, plus the label line.
    private static SheetLayout Layout(IReadOnlyList<double> ratios, int columns, int thumbnail, int gap, bool labels)
    {
        var labelSize = (float)Math.Clamp(thumbnail / 20.0, 10, 48);
        var labelHeight = labels ? (int)Math.Ceiling(labelSize * 1.6) : 0;
        var cells = new List<(int, int, int, int)>(ratios.Count);
        var y = gap;
        for (var row = 0; row * columns < ratios.Count; row++)
        {
            var heights = ratios.Skip(row * columns).Take(columns).Select(r => Math.Max(1, (int)Math.Round(thumbnail * r))).ToList();
            for (var column = 0; column < heights.Count; column++)
            {
                cells.Add((gap + (column * (thumbnail + gap)), y, thumbnail, heights[column]));
            }

            y += heights.Max() + labelHeight + gap;
        }

        return new SheetLayout(gap + (columns * (thumbnail + gap)), y, labelSize, cells);
    }

    private static SKTypeface? LabelTypeface(ReadOnlyMemory<byte> file)
    {
        var typeface = file.IsEmpty ? SKTypeface.Default : SKTypeface.FromData(SKData.CreateCopy(file.Span)) ?? SKTypeface.Default;
        if (typeface is null || typeface.GlyphCount == 0)
        {
            typeface?.Dispose();
            return null;
        }

        return typeface;
    }

    private static SKColor Color(string hex)
    {
        var value = (hex ?? string.Empty).Trim().TrimStart('#');
        return value.Length == 6 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : throw new ArgumentException($"'{hex}' is not a colour; expected 'RRGGBB'.", nameof(hex));
    }
}
