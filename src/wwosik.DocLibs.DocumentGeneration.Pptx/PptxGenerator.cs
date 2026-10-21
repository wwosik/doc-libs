using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Generates PowerPoint presentations (<c>.pptx</c>) from Markdown: the cover from the first <c>#</c>, a section
/// divider per later <c>#</c>, one slide per <c>##</c>, and a slide of its own for every table and image. Front
/// matter at the top of the Markdown overrides the template options.
/// </summary>
public static class PptxGenerator
{
    private static readonly XNamespace CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>Writes <paramref name="document"/> as a <c>.pptx</c> to <paramref name="output"/>.</summary>
    /// <exception cref="DocumentConversionException">
    /// An image is not among the document's resources or not a PNG, JPEG or GIF, the front matter is invalid or names
    /// an unknown brand, a named layout does not exist, or the template is damaged.
    /// </exception>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="PptxOptions.Brands"/> share a name.</exception>
    public static void Generate(MarkdownDocument document, Stream output, PptxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PptxOptions.Default;

        var frontMatter = FrontMatter.Read(document.Markdown, Format.Pptx);
        var template = PptxTemplateOptions.Apply(frontMatter, options.Template ?? PptxOptions.Default.Template, options.Brands);
        var slides = DeckParser.Parse(document with { Markdown = frontMatter.Body }, options, template.BrandValue, frontMatter.BodyLine);
        var date = template.ShowsDate
            ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;

        using var buffer = new MemoryStream();
        template.Write(buffer);

        buffer.Position = 0;
        try
        {
            using var package = PresentationDocument.Open(buffer, isEditable: true);
            if (package.DocumentType == PresentationDocumentType.Template)
            {
                package.ChangeDocumentType(PresentationDocumentType.Presentation);
            }

            var tableFont = template.BrandValue is { } brand ? ResolvedBrand.Resolve(brand).TableFontFamily : null;
            new DeckWriter(package, options, template.LayoutNames, date, tableFont).Write(slides);
            WriteCoreProperties(
                package,
                frontMatter.Title ?? Title(document, slides),
                frontMatter.Author ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author));
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or XmlException or FormatException)
        {
            throw new DocumentConversionException(Format.Pptx, "The template is damaged: " + ex.Message, ex);
        }

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private static string? Title(MarkdownDocument document, IReadOnlyList<Slide> slides)
    {
        if (document.Metadata.GetValueOrDefault(MetadataKeys.Title) is { } title)
        {
            return title;
        }

        var slide = slides.OfType<TitleSlide>().FirstOrDefault() ?? slides.FirstOrDefault();
        return slide is null ? null : string.Concat(slide.Title.Select(r => r.IsBreak ? " " : r.Text));
    }

    // Written by hand rather than through PackageProperties so that the part keeps the name docProps/core.xml.
    private static void WriteCoreProperties(PresentationDocument package, string? title, string? author)
    {
        var part = package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart();
        XDocument xml;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
        {
            xml = stream.Length > 0
                ? XDocument.Load(stream)
                : new XDocument(new XElement(
                    CoreProperties + "coreProperties",
                    new XAttribute(XNamespace.Xmlns + "cp", CoreProperties),
                    new XAttribute(XNamespace.Xmlns + "dc", DublinCore)));
        }

        Replace(xml.Root!, DublinCore + "title", title);
        Replace(xml.Root!, DublinCore + "creator", author);
        using var output = part.GetStream(FileMode.Create, FileAccess.Write);
        xml.Save(output);
    }

    private static void Replace(XElement root, XName name, string? value)
    {
        root.Elements(name).Remove();
        if (!string.IsNullOrWhiteSpace(value))
        {
            root.Add(new XElement(name, value));
        }
    }
}
