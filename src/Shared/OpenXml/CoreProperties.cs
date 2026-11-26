using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Writes the core properties (<c>docProps/core.xml</c>) of a generated Open XML package: title, creator, language and,
/// when given, the dates of creation and modification. Shared source, linked into each generator that writes Open XML,
/// so the shared package needs no Open XML SDK. Written by hand rather than through <c>PackageProperties</c> so that the
/// part keeps the name <c>docProps/core.xml</c>; a template's other properties are kept.
/// </summary>
internal static class CoreProperties
{
    private static readonly XNamespace Properties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Terms = "http://purl.org/dc/terms/";
    private static readonly XNamespace Instance = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Sets <c>dc:title</c>, <c>dc:creator</c> and <c>dc:language</c>, each left out when empty, and, when
    /// <paramref name="time"/> is given, <c>dcterms:created</c> and <c>dcterms:modified</c> to it in UTC.
    /// </summary>
    /// <param name="part">The package's part, or a new one: <c>package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart()</c>.</param>
    /// <param name="title">The title.</param>
    /// <param name="author">The creator.</param>
    /// <param name="language">The language tag.</param>
    /// <param name="time">The time of creation and modification; <see langword="null"/>: the template's dates are kept.</param>
    public static void Write(CoreFilePropertiesPart part, string? title, string? author, string language, DateTimeOffset? time = null)
    {
        XDocument xml;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
        {
            xml = stream.Length > 0
                ? XDocument.Load(stream)
                : new XDocument(new XElement(
                    Properties + "coreProperties",
                    new XAttribute(XNamespace.Xmlns + "cp", Properties),
                    new XAttribute(XNamespace.Xmlns + "dc", DublinCore)));
        }

        var root = xml.Root!;
        Replace(root, DublinCore + "title", title);
        Replace(root, DublinCore + "creator", author);
        Replace(root, DublinCore + "language", language);
        if (time is { } now)
        {
            Declare(root, "dcterms", Terms);
            Declare(root, "xsi", Instance);
            var value = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            foreach (var name in new[] { "created", "modified" })
            {
                root.Elements(Terms + name).Remove();
                root.Add(new XElement(Terms + name, new XAttribute(Instance + "type", "dcterms:W3CDTF"), value));
            }
        }

        using var output = part.GetStream(FileMode.Create, FileAccess.Write);
        xml.Save(output);
    }

    /// <summary>
    /// Sets <c>cp:contentStatus</c>, which Office and Explorer show as the document's status, to
    /// <paramref name="status"/>, such as the classification marking; <see langword="null"/> removes it.
    /// </summary>
    public static void ContentStatus(CoreFilePropertiesPart part, string? status)
    {
        XDocument xml;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
        {
            xml = XDocument.Load(stream);
        }

        Replace(xml.Root!, Properties + "contentStatus", status);
        using var output = part.GetStream(FileMode.Create, FileAccess.Write);
        xml.Save(output);
    }

    /// <summary>
    /// Sets <c>dc:subject</c> and <c>dc:description</c>, which Office shows as the subject and the comments; each left out
    /// when empty.
    /// </summary>
    public static void Describe(CoreFilePropertiesPart part, string? subject, string? description)
    {
        XDocument xml;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
        {
            xml = XDocument.Load(stream);
        }

        Replace(xml.Root!, DublinCore + "subject", subject);
        Replace(xml.Root!, DublinCore + "description", description);
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

    // The prefix the W3CDTF type names its namespace by must be declared as written.
    private static void Declare(XElement root, string prefix, XNamespace ns)
    {
        if (root.GetNamespaceOfPrefix(prefix) != ns)
        {
            root.SetAttributeValue(XNamespace.Xmlns + prefix, ns.NamespaceName);
        }
    }
}
