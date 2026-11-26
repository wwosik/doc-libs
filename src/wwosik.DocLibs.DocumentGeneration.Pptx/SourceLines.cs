using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// The Markdown line a generated slide starts at, kept in the slide itself so that <see cref="HtmlDeck.Write"/>, which
/// sees only the <c>.pptx</c>, can return it: one <c>p:ext</c> of its own in the slide's <c>p:extLst</c>, holding
/// <c>&lt;dl:source line="12"/&gt;</c>. Viewers skip extensions they do not know, so the slide looks the same.
/// </summary>
internal static class SourceLines
{
    /// <summary>The extension's URI, as <c>p:ext</c> names extensions.</summary>
    public const string Uri = "{0DC58704-E0A8-4DBD-9516-3D8E211A6220}";

    /// <summary>The namespace of the <c>source</c> element inside the extension.</summary>
    public const string Namespace = "https://github.com/wwosik/doc-libs/pptx/source";

    /// <summary>Adds the line to the slide, last, after its transition and timing; a line of 0 (unknown) adds nothing.</summary>
    public static void Write(P.Slide slide, int line)
    {
        if (line <= 0)
        {
            return;
        }

        var source = new OpenXmlUnknownElement("dl", "source", Namespace);
        source.SetAttribute(new OpenXmlAttribute("line", string.Empty, line.ToString(CultureInfo.InvariantCulture)));
        var extension = new P.SlideExtension { Uri = Uri };
        extension.AddNamespaceDeclaration("dl", Namespace);
        extension.Append(source);
        var list = slide.GetFirstChild<P.SlideExtensionList>() ?? slide.AppendChild(new P.SlideExtensionList());
        list.Append(extension);
    }

    /// <summary>The line kept in a slide's XML (<c>p:sld</c>); 0 when it has none or it is not a positive number.</summary>
    public static int Read(XElement slide)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var value = slide.Element(p + "extLst")?.Elements(p + "ext")
            .FirstOrDefault(e => (string?)e.Attribute("uri") == Uri)
            ?.Element(XName.Get("source", Namespace))?.Attribute("line")?.Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var line) && line > 0 ? line : 0;
    }
}
