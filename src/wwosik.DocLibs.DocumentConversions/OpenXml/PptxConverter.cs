using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentConversions.OpenXml;

/// <summary>
/// Converts PowerPoint presentations (<c>.pptx</c>) into Markdown: one section per slide with the
/// slide title as its heading, then text, tables and pictures in shape order.
/// </summary>
public sealed class PptxConverter : IDocumentConverter
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private const string PresentationPart = "ppt/presentation.xml";

    /// <inheritdoc/>
    public bool CanConvert(DocumentFormat format) => format == DocumentFormat.Pptx;

    /// <inheritdoc/>
    public MarkdownDocument Convert(Stream input, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        return OpenXmlPackage.Guard(DocumentFormat.Pptx, () =>
        {
            using var package = new OpenXmlPackage(input);
            var presentation = package.ReadXml(PresentationPart)
                ?? throw new DocumentConversionException(DocumentFormat.Pptx, "ppt/presentation.xml is missing.");
            var rels = package.Relationships(PresentationPart);
            var images = options.IncludeImages ? new ResourceCollector(options.ResourcePathPrefix) : null;

            var builder = new StringBuilder();
            var number = 0;
            var anyContent = false;
            foreach (var slideId in presentation.Descendants(P + "sldId"))
            {
                var id = (string?)slideId.Attribute(R + "id");
                if (id is null || !rels.TryGetValue(id, out var part))
                {
                    continue;
                }

                number++;
                anyContent |= RenderSlide(package, part, number, images, builder);
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MetadataKeys.SlideCount] = number.ToString(CultureInfo.InvariantCulture),
            };
            package.AddCoreProperties(metadata);
            return new MarkdownDocument(
                anyContent ? MarkdownText.Normalize(builder.ToString()) : string.Empty,
                images?.Resources ?? [],
                metadata);
        });
    }

    // Returns whether the slide had any content beyond its heading.
    private static bool RenderSlide(OpenXmlPackage package, string part, int number, ResourceCollector? images, StringBuilder builder)
    {
        var tree = package.ReadXml(part)?.Root?.Element(P + "cSld")?.Element(P + "spTree");
        var titleShape = tree?.Descendants(P + "sp").FirstOrDefault(IsTitle);
        var title = titleShape is null ? string.Empty : string.Join(" ", Paragraphs(titleShape.Element(P + "txBody")!));

        builder.Append("## ").Append(title.Length > 0 ? title : "Slide " + number.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
        if (tree is null)
        {
            return title.Length > 0;
        }

        var rels = package.Relationships(part);
        var content = title.Length > 0;
        foreach (var element in tree.Descendants())
        {
            if (element.Ancestors(MC + "Fallback").Any() || element.Ancestors(A + "tbl").Any())
            {
                continue;
            }

            if (element.Name == P + "txBody" && element.Parent != titleShape && !IsFooter(element.Parent!))
            {
                var lines = Paragraphs(element).ToList();
                if (lines.Count > 0)
                {
                    builder.Append(string.Join("\n\n", lines)).Append("\n\n");
                    content = true;
                }
            }
            else if (element.Name == A + "tbl")
            {
                var grid = element.Elements(A + "tr")
                    .Select(tr => (IReadOnlyList<string>)[.. tr.Elements(A + "tc").Select(tc => string.Join("<br>", Paragraphs(tc)))])
                    .ToList();
                builder.Append(MarkdownText.Table(grid));
                content = true;
            }
            else if (element.Name == P + "pic" && images is not null && Picture(element, package, rels, images) is { } picture)
            {
                builder.Append(picture).Append("\n\n");
                content = true;
            }
        }

        return content;
    }

    private static bool IsTitle(XElement shape) => PlaceholderType(shape) is "title" or "ctrTitle";

    // Slide number, date and footer placeholders repeat on every slide and are not content.
    private static bool IsFooter(XElement shape) => PlaceholderType(shape) is "sldNum" or "dt" or "ftr";

    private static string? PlaceholderType(XElement shape) =>
        (string?)shape.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph")?.Attribute("type");

    private static string? Picture(XElement picture, OpenXmlPackage package, Dictionary<string, string> rels, ResourceCollector images)
    {
        var id = (string?)picture.Element(P + "blipFill")?.Element(A + "blip")?.Attribute(R + "embed");
        if (id is null || !rels.TryGetValue(id, out var part) || package.ReadBytes(part) is not { } bytes)
        {
            return null;
        }

        var properties = picture.Element(P + "nvPicPr")?.Element(P + "cNvPr");
        var alt = (string?)properties?.Attribute("descr") ?? string.Empty;
        var path = images.Add(part[(part.LastIndexOf('/') + 1)..], bytes);
        return $"![{MarkdownText.Escape(alt).Replace('\n', ' ')}]({MarkdownText.Destination(path)})";
    }

    private static IEnumerable<string> Paragraphs(XElement container) =>
        container.Descendants(A + "p")
            .Select(p => string.Concat(p.Elements().Select(e =>
                e.Name == A + "br" ? " " : string.Concat(e.Descendants(A + "t").Select(t => t.Value)))).Trim())
            .Where(t => t.Length > 0)
            .Select(MarkdownText.Escape);
}
