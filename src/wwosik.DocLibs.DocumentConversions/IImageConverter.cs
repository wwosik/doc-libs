namespace wwosik.DocLibs.DocumentConversions;

/// <summary>
/// Turns an image in a format the document generators cannot place, such as SVG, into a PNG or JPEG they can. The
/// generators ask their converters in order for each image that is not a PNG, JPEG or (decks) GIF; the first result
/// wins, and an image nobody converts is an error, as without converters.
/// </summary>
public interface IImageConverter
{
    /// <summary>
    /// The image as a PNG or JPEG drawn at <paramref name="scale"/> pixels per pixel of its natural size, so it stays
    /// sharp where it is enlarged; <see langword="null"/> when the image is not in a format this converter takes.
    /// </summary>
    /// <exception cref="DocumentConversionException">The image is in this converter's format but cannot be converted.</exception>
    ConvertedImage? Convert(DocumentResource image, double scale);
}

/// <summary>An image converted by an <see cref="IImageConverter"/>.</summary>
/// <param name="Content">The PNG or JPEG.</param>
/// <param name="ContentType"><c>image/png</c> or <c>image/jpeg</c>.</param>
/// <param name="Width">The natural width in pixels: what the image is placed at, before any scaling.</param>
/// <param name="Height">The natural height in pixels.</param>
public sealed record ConvertedImage(ReadOnlyMemory<byte> Content, string ContentType, double Width, double Height);
