using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// What a generated document keeps for <c>DocxConverter</c> to read it back into its Markdown (E08-S02), in an
/// ignorable namespace of its own (<see cref="Namespace"/>, listed in <c>mc:Ignorable</c>): Word and other readers skip
/// it, and Word drops it when a person saves the file, so that the converter falls back to what Word shows.
/// <list type="bullet">
/// <item><c>settings.xml</c>: <c>dl:generated</c>, the marker, with the heading shift, the marking and the style of every role.</item>
/// <item>The body: <c>dl:md</c>, the Markdown of a container or a drawn block, on the first element written for it,
/// and <c>dl:skip</c> on the others; <c>dl:code</c>, the language, on the first line of a code block; <c>dl:id</c>,
/// the Markdown id, on a heading's bookmark; <c>dl:title</c>, <c>dl:attrs</c> and <c>dl:url</c> of an image on its
/// <c>wp:docPr</c>, and <c>dl:skip</c> on the caption its title made.</item>
/// </list>
/// The converter has the namespace and the names as its own constants.
/// </summary>
internal static class DocxRoundTrip
{
    /// <summary>The namespace of what the converter reads back.</summary>
    public const string Namespace = "urn:wwosik:doclibs:wordprocessingml";

    /// <summary>The namespace's prefix.</summary>
    public const string Prefix = "dl";

    /// <summary>The marker's version; a converter reads the versions it knows.</summary>
    public const string Version = "1";

    private const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Sets the attribute <c>dl:<paramref name="name"/></c> on <paramref name="element"/>.</summary>
    public static void Set(OpenXmlElement element, string name, string value) =>
        element.SetAttribute(new OpenXmlAttribute(Prefix, name, Namespace, value));

    /// <summary>
    /// Marks the elements written for one block: the Markdown on the first, <c>dl:skip</c> on the rest. Nothing when
    /// the block wrote nothing.
    /// </summary>
    public static void Source(IReadOnlyList<OpenXmlElement> elements, string markdown)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            if (i == 0)
            {
                Set(elements[i], "md", markdown);
            }
            else
            {
                Set(elements[i], "skip", "1");
            }
        }
    }

    /// <summary>Declares the namespace on <paramref name="root"/> and lists it as ignorable.</summary>
    public static void Declare(OpenXmlPartRootElement root)
    {
        if (root.LookupNamespace(Prefix) is null)
        {
            root.AddNamespaceDeclaration(Prefix, Namespace);
        }

        if (root.LookupNamespace("mc") is null)
        {
            root.AddNamespaceDeclaration("mc", MarkupCompatibility);
        }

        var ignorable = root.MCAttributes?.Ignorable?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (!ignorable.Contains(Prefix))
        {
            root.MCAttributes = new MarkupCompatibilityAttributes { Ignorable = string.Join(" ", [.. ignorable, Prefix]) };
        }
    }

    /// <summary>
    /// Writes the marker into the settings: the version, how many levels the headings were shifted up, and the style id
    /// of every role by the role's default id.
    /// </summary>
    public static void Mark(MainDocumentPart main, DocxStyles styles, int headingShift, Marking marking, Audience audience)
    {
        var part = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
        var settings = part.Settings ??= new Settings();
        foreach (var old in settings.ChildElements.Where(e => e.NamespaceUri == Namespace).ToList())
        {
            old.Remove();
        }

        var marker = new OpenXmlUnknownElement(Prefix, "generated", Namespace);
        Set(marker, "version", Version);
        Set(marker, "headingShift", headingShift.ToString(CultureInfo.InvariantCulture));
        Set(marker, "classification", marking.Classification.ToString().ToLowerInvariant());
        Set(marker, "draft", marking.Draft ? "true" : "false");
        Set(marker, "audience", audience.ToString().ToLowerInvariant());
        foreach (var role in DocxRoles.All)
        {
            var style = new OpenXmlUnknownElement(Prefix, "style", Namespace);
            Set(style, "role", role.Id);
            Set(style, "id", styles.Id(role));
            marker.AppendChild(style);
        }

        settings.AppendChild(marker);
        Declare(settings);
        settings.Save();
    }
}
