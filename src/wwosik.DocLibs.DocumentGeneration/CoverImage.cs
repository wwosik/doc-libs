using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>How tall the cover image is: a share of the cover's height.</summary>
internal enum CoverImageSize
{
    Small,
    Medium,
    Large,
}

/// <summary>Where the cover image stands: under the subtitle, or right of the title block.</summary>
internal enum CoverImagePosition
{
    Below,
    Right,
}

/// <summary>
/// An image on the cover, such as a product mark or a partner's logo: front matter <c>coverImage</c> names a resource of
/// the document, else the brand's <see cref="Brand.CoverImage"/> is used. <c>coverImageSize</c> and
/// <c>coverImagePosition</c> say how tall it is and where it stands. Every generator reads it with <see cref="Resolve"/>
/// and places it on its own cover; one without a cover reports <see cref="NoteCodes.CoverImageIgnored"/>.
/// </summary>
/// <param name="Resource">The image as given: PNG, JPEG, GIF or a format the image converters take, such as SVG.</param>
/// <param name="Size">Its height on the cover.</param>
/// <param name="Position">Where it stands.</param>
/// <param name="Line">The line of <c>coverImage</c> in the Markdown; 0 for the brand's image.</param>
/// <param name="FromFrontMatter">Whether the front matter asked for it, rather than the brand.</param>
internal sealed record CoverImage(DocumentResource Resource, CoverImageSize Size, CoverImagePosition Position, int Line, bool FromFrontMatter)
{
    public const string Key = "coverImage";
    public const string SizeKey = "coverImageSize";
    public const string PositionKey = "coverImagePosition";

    /// <summary>The keys, in the order the guides and error messages list them.</summary>
    public static readonly IReadOnlyList<string> Keys = [Key, SizeKey, PositionKey];

    /// <summary>The share of the cover's height the image takes: 1/10, 1/6 or 1/4.</summary>
    public double HeightShare => this.Size switch
    {
        CoverImageSize.Small => 0.1,
        CoverImageSize.Large => 0.25,
        _ => 1.0 / 6,
    };

    /// <summary>
    /// The cover image of a document: the front matter's, else <paramref name="brand"/>'s; <see langword="null"/> when
    /// there is neither. The size and position keys are checked even then.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// <c>coverImage</c> names no resource of the document, or a size or position is not one the key takes.
    /// </exception>
    public static CoverImage? Resolve(FrontMatter frontMatter, MarkdownDocument document, ResolvedBrand? brand, DocumentFormat format)
    {
        var size = frontMatter.Value(SizeKey) is { } sizeValue
            ? frontMatter.OneOf(SizeKey, sizeValue, ("small", CoverImageSize.Small), ("medium", CoverImageSize.Medium), ("large", CoverImageSize.Large))
            : CoverImageSize.Medium;
        var position = frontMatter.Value(PositionKey) is { } positionValue
            ? frontMatter.OneOf(PositionKey, positionValue, ("below", CoverImagePosition.Below), ("right", CoverImagePosition.Right))
            : CoverImagePosition.Below;
        if (frontMatter.Value(Key) is { } path)
        {
            var resource = document.FindResource(path)
                ?? throw new DocumentConversionException(format, $"The front matter key '{Key}' names the image '{path}', which is not among the document's resources.");
            return new CoverImage(resource, size, position, frontMatter.LineOf(Key), FromFrontMatter: true);
        }

        if (brand?.CoverImage is { } own)
        {
            var jpeg = ImageInfo.Read(own) is { Format: ImageFormat.Jpeg };
            return new CoverImage(
                new DocumentResource(jpeg ? "brand-cover-image.jpg" : "brand-cover-image.png", jpeg ? "image/jpeg" : "image/png", own),
                size,
                position,
                0,
                FromFrontMatter: false);
        }

        return null;
    }

    /// <summary>
    /// Reports that the image is not drawn, with <paramref name="reason"/> ("the document has no cover"), when the front
    /// matter asked for it; the brand's image is left out silently.
    /// </summary>
    public void Ignored(GenerationNotes notes, string reason)
    {
        if (this.FromFrontMatter)
        {
            notes.Add(NoteCodes.CoverImageIgnored, this.Line, $"The cover image '{this.Resource.Path}' is not drawn: {reason}.");
        }
    }

    /// <summary>
    /// The image in a format the generator places itself (<paramref name="placeable"/>), with its natural size in pixels
    /// and, for a converted SVG, the SVG. Another format goes through the image converters, drawn sharp enough for
    /// <paramref name="heightInPixels"/>, at least at twice its size.
    /// </summary>
    /// <exception cref="DocumentConversionException">The image is in no placeable format and no converter takes it.</exception>
    public (DocumentResource Image, ImageInfo Info, double Width, double Height, DocumentResource? Svg) Placeable(
        IReadOnlyList<IImageConverter>? converters, double heightInPixels, DocumentFormat format, params ImageFormat[] placeable)
    {
        if (ImageInfo.Read(this.Resource.Content.Span) is { } info && placeable.Contains(info.Format))
        {
            return (this.Resource, info, info.Width, info.Height, null);
        }

        if (ImageConverters.Convert(converters, this.Resource, 1, format) is var (_, _, naturalHeight))
        {
            var scale = Math.Max(2, 2 * heightInPixels / Math.Max(1, naturalHeight));
            if (ImageConverters.Convert(converters, this.Resource, scale, format) is var (image, width, height)
                && ImageInfo.Read(image.Content.Span) is { } converted && placeable.Contains(converted.Format))
            {
                return (image, converted, width, height, SvgFormat.IsSvg(this.Resource) ? this.Resource : null);
            }
        }

        var names = string.Join(", ", placeable.Select(f => f.ToString().ToUpperInvariant()));
        throw new DocumentConversionException(
            format,
            $"The cover image '{this.Resource.Path}' is not a {names}{(converters is { Count: > 0 } ? ", and no image converter takes it" : string.Empty)}.");
    }

    /// <summary>
    /// Where the image goes: as tall as <paramref name="height"/> asks, then made smaller (never moved) until it stays
    /// within <paramref name="maxWidth"/> and above <paramref name="maxBottom"/>, and clear of every one of
    /// <paramref name="obstacles"/> (the logo, the artwork, the tagline). It hangs from (<paramref name="x"/>,
    /// <paramref name="y"/>): its top left corner, or with <paramref name="fromRight"/> its top right one. Any unit, y
    /// downwards. <see langword="null"/> when less than <paramref name="minHeight"/> is left.
    /// </summary>
    /// <remarks>The image's width over its height is <paramref name="ratio"/>.</remarks>
    public static (double X, double Y, double Width, double Height)? Place(
        double x, double y, bool fromRight, double height, double ratio, double maxWidth, double maxBottom,
        IEnumerable<(double X, double Y, double Width, double Height)> obstacles, double minHeight)
    {
        var blocked = obstacles.Where(o => o.Width > 0 && o.Height > 0).ToList();
        (double X, double Y, double Width, double Height) At(double h) => (fromRight ? x - (h * ratio) : x, y, h * ratio, h);
        bool Clear(double h)
        {
            var (left, top, width, tall) = At(h);
            return !blocked.Any(o => left < o.X + o.Width && o.X < left + width && top < o.Y + o.Height && o.Y < top + tall);
        }

        var most = Math.Min(height, Math.Min(maxBottom - y, maxWidth / ratio));
        if (most < minHeight)
        {
            return null;
        }

        if (!Clear(most))
        {
            // The rectangle grows from its corner, so once it touches an obstacle it does for every larger height.
            var (low, high) = (0.0, most);
            for (var i = 0; i < 40; i++)
            {
                var middle = (low + high) / 2;
                (low, high) = Clear(middle) ? (middle, high) : (low, middle);
            }

            most = low;
        }

        return most < minHeight ? null : At(most);
    }
}

/// <summary>Whether a resource is SVG, as the image converters and the Open XML writers tell it.</summary>
internal static class SvgFormat
{
    public static bool IsSvg(DocumentResource resource)
    {
        if (resource.ContentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) || resource.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var head = System.Text.Encoding.UTF8.GetString(resource.Content.Span[..Math.Min(resource.Content.Length, 1024)]);
        return head.Contains("<svg", StringComparison.Ordinal);
    }
}
