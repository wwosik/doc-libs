using wwosik.DocLibs.DocumentConversions.OpenXml;
using wwosik.DocLibs.DocumentConversions.Pdf;

namespace wwosik.DocLibs.DocumentConversions;

/// <summary>Detects a document's format and hands it to the first converter that handles it.</summary>
/// <param name="converters">The converters, in order of precedence.</param>
public sealed class DocumentConverterRegistry(IEnumerable<IDocumentConverter> converters)
{
    private readonly IReadOnlyList<IDocumentConverter> converters = [.. converters];

    /// <summary>A registry with the converters of this package (PDF, DOCX, PPTX), followed by <paramref name="additional"/>.</summary>
    public static DocumentConverterRegistry CreateDefault(params IEnumerable<IDocumentConverter> additional) =>
        new([new PdfConverter(), new DocxConverter(), new PptxConverter(), .. additional]);

    /// <summary>Whether any registered converter handles <paramref name="format"/>.</summary>
    public bool CanConvert(DocumentFormat format) => this.converters.Any(c => c.CanConvert(format));

    /// <summary>Detects the format of <paramref name="input"/> and converts it.</summary>
    /// <param name="input">The document; readable and seekable.</param>
    /// <param name="fileName">The original file name, if known; see <see cref="DocumentFormatDetector.Detect"/>.</param>
    /// <param name="options">Conversion options; <see cref="ConversionOptions.Default"/> if omitted.</param>
    /// <exception cref="DocumentConversionException">No converter handles the format, or the conversion failed.</exception>
    public MarkdownDocument Convert(Stream input, string? fileName = null, ConversionOptions? options = null)
    {
        var format = DocumentFormatDetector.Detect(input, fileName ?? options?.FileName);
        var converter = this.converters.FirstOrDefault(c => c.CanConvert(format))
            ?? throw new DocumentConversionException(format, "No converter is registered for this format.");
        options ??= ConversionOptions.Default;
        return converter.Convert(input, fileName is null ? options : options with { FileName = fileName });
    }
}
