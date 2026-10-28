using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentConversions.Svg;

/// <summary>
/// An SVG with everything that reaches outside it taken out, before it is drawn: Svg.Skia loads an <c>&lt;image&gt;</c>'s
/// file or web address whatever its settings say. Removed: scripts, <c>foreignObject</c>, DTDs, every link
/// (<c>href</c>) that is not to the document's own elements or a <c>data:</c> URI, and the same in CSS <c>url()</c>
/// values and <c>@import</c> rules.
/// </summary>
internal static partial class SvgSanitizer
{
    private static readonly HashSet<string> Removed = ["script", "foreignObject"];

    public static string Clean(ReadOnlySpan<byte> svg)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(svg.ToArray()), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"it is not well-formed XML: {ex.Message}", ex);
        }

        if (document.Root?.Name.LocalName != "svg")
        {
            throw new FormatException("it is not an SVG document");
        }

        document.DocumentType?.Remove();
        foreach (var element in document.Descendants().Where(e => Removed.Contains(e.Name.LocalName)).ToList())
        {
            element.Remove();
        }

        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().ToList())
            {
                if (attribute.Name.LocalName == "href" && !Internal(attribute.Value))
                {
                    attribute.Remove();
                }
                else if (attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase))
                {
                    attribute.Value = Css(attribute.Value);
                }
            }

            if (element.Name.LocalName == "style")
            {
                element.Value = Css(element.Value);
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool Internal(string reference)
    {
        var value = reference.Trim();
        return value.StartsWith('#') || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    // External url() values become 'none', and @import rules go.
    private static string Css(string css) =>
        UrlPattern().Replace(ImportPattern().Replace(css, string.Empty), match => Internal(match.Groups["target"].Value) ? match.Value : "none");

    [GeneratedRegex(@"url\(\s*['""]?(?<target>[^'"")]*)['""]?\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"@import[^;]*;?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportPattern();
}
