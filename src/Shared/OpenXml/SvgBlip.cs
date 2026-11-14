using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The SVG that rides along with a picture's PNG: Office 2016 and later draw the SVG of the blip extension
/// <see cref="Uri"/> (<c>asvg:svgBlip</c>), older viewers the PNG. Shared source, linked into the PPTX and DOCX
/// generators.
/// </summary>
internal static class SvgBlip
{
    /// <summary>The URI of the blip extension that holds the SVG.</summary>
    public const string Uri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}";

    /// <summary>The namespace of <c>asvg:svgBlip</c>.</summary>
    public const string Namespace = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";

    /// <summary>Whether <paramref name="resource"/> is an SVG: by its type, its extension, or an <c>&lt;svg</c> early on.</summary>
    public static bool IsSvg(DocumentResource resource)
    {
        if (resource.ContentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) || resource.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var head = Encoding.UTF8.GetString(resource.Content.Span[..Math.Min(resource.Content.Length, 1024)]);
        return head.Contains("<svg", StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>a:extLst</c> of a blip whose SVG is the part of <paramref name="relationshipId"/>, as XML in which the
    /// prefixes <c>a</c> and <c>r</c> are declared further up.
    /// </summary>
    public static string Extension(string relationshipId) =>
        $"<a:extLst><a:ext uri=\"{Uri}\"><asvg:svgBlip xmlns:asvg=\"{Namespace}\" r:embed=\"{relationshipId}\"/></a:ext></a:extLst>";
}
