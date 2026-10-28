using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>Asks the image converters in order, for an image that is not a format the generator places itself.</summary>
internal static class ImageConverters
{
    /// <summary>
    /// The first conversion, as a resource at the same path; <see langword="null"/> when no converter takes the image.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// A converter failed; a converter's other exceptions become the inner exception, so every document problem has the
    /// same type.
    /// </exception>
    public static (DocumentResource Image, double Width, double Height)? Convert(
        IReadOnlyList<IImageConverter>? converters, DocumentResource image, double scale, DocumentFormat format)
    {
        foreach (var converter in converters ?? [])
        {
            ConvertedImage? converted;
            try
            {
                converted = converter.Convert(image, scale);
            }
            catch (Exception ex) when (ex is not DocumentConversionException)
            {
                throw new DocumentConversionException(format, $"The image '{image.Path}' cannot be converted: {ex.Message}", ex);
            }

            if (converted is not null)
            {
                return (new DocumentResource(image.Path, converted.ContentType, converted.Content), converted.Width, converted.Height);
            }
        }

        return null;
    }
}
