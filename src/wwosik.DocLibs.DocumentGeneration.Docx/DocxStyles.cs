using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The paragraph style of every role in the document's template, found by name (<c>w:name</c>, case-insensitive),
/// never by id: localized templates have other ids. A role whose style the template lacks gets it added from the
/// built-in definition, based on the template's <c>Normal</c>; a name the caller mapped that the template lacks throws.
/// Paragraphs then reference the id found.
/// </summary>
internal sealed class DocxStyles
{
    private readonly Dictionary<StyleRole, string> ids;

    private DocxStyles(Dictionary<StyleRole, string> ids)
    {
        this.ids = ids;
    }

    /// <summary>The style id of <paramref name="role"/>.</summary>
    public string Id(StyleRole role) => this.ids[role];

    /// <summary>Finds or adds the style of every role in <see cref="DocxRoles.All"/>.</summary>
    /// <exception cref="DocumentConversionException">A style <paramref name="names"/> gives is not in the template.</exception>
    public static DocxStyles Resolve(MainDocumentPart main, DocxStyleNames names, StyleDesign design, string template)
    {
        var part = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        var styles = part.Styles ??= new Styles();
        var paragraphStyles = styles.Elements<Style>()
            .Where(s => s.Type?.Value == StyleValues.Paragraph && s.StyleId?.Value is not null)
            .ToList();
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in paragraphStyles)
        {
            byName.TryAdd(style.StyleName?.Val?.Value ?? style.StyleId!.Value!, style.StyleId!.Value!);
        }

        var taken = new HashSet<string>(styles.Elements<Style>().Select(s => s.StyleId?.Value).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<StyleRole, string>();
        var changed = false;
        foreach (var role in DocxRoles.All)
        {
            var name = role.Name(names);

            // Normal is also the template's default paragraph style, whatever its name.
            var id = byName.GetValueOrDefault(name)
                ?? (role == DocxRoles.Normal && role.Mapped(names) is null ? DefaultParagraphStyle(paragraphStyles) : null);
            if (id is not null)
            {
                found[role] = id;
                continue;
            }

            if (role.Mapped(names) is not null)
            {
                var available = byName.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(n => $"'{n}'");
                throw new DocumentConversionException(
                    Format.Docx,
                    $"The style '{name}' that {DocxRoles.KeyPrefix}{role.Key} names is not in {template}. Its paragraph styles: {string.Join(", ", available)}.");
            }

            // Added from the built-in definition, based on the template's Normal, under a free id.
            var newId = role.Id;
            for (var n = 2; taken.Contains(newId); n++)
            {
                newId = role.Id + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            taken.Add(newId);
            var basedOn = role == DocxRoles.Normal ? null : found[DocxRoles.Normal];
            var xml = DocxRoles.Xml(role, design, newId, basedOn, basedOn, isDefault: role == DocxRoles.Normal && DefaultParagraphStyle(paragraphStyles) is null);
            var style = new Style($"<w:style xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"{xml["<w:style".Length..]}");
            styles.Append(style);
            byName[name] = newId;
            found[role] = newId;
            changed = true;
        }

        if (changed)
        {
            styles.Save();
        }

        return new DocxStyles(found);
    }

    private static string? DefaultParagraphStyle(IEnumerable<Style> paragraphStyles) =>
        paragraphStyles.FirstOrDefault(s => s.Default?.Value == true)?.StyleId?.Value;
}
