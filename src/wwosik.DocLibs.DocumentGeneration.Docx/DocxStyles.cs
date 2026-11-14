using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The style of every role in the document's template, found by name (<c>w:name</c>, case-insensitive) among the
/// styles of the role's kind (paragraph, character or table), never by id: localized templates have other ids. A role
/// whose style the template lacks gets it added from the built-in definition, a paragraph style based on the
/// template's <c>Normal</c>; a name the caller mapped that the template lacks throws. Paragraphs, runs and tables then
/// reference the id found.
/// </summary>
internal sealed class DocxStyles
{
    private readonly Dictionary<StyleRole, string> ids;
    private readonly Dictionary<string, Style> styles;

    private DocxStyles(Dictionary<StyleRole, string> ids, Dictionary<string, Style> styles)
    {
        this.ids = ids;
        this.styles = styles;
    }

    /// <summary>The style id of <paramref name="role"/>.</summary>
    public string Id(StyleRole role) => this.ids[role];

    /// <summary>The style of <paramref name="role"/> as the template defines it, or as it was added.</summary>
    public Style Style(StyleRole role) => this.styles[this.ids[role]];

    /// <summary>The style with the id <paramref name="id"/>, if the template has one.</summary>
    public Style? ById(string id) => this.styles.GetValueOrDefault(id);

    /// <summary>Finds or adds the style of every role in <see cref="DocxRoles.All"/>.</summary>
    /// <exception cref="DocumentConversionException">A style <paramref name="names"/> gives is not in the template.</exception>
    public static DocxStyles Resolve(MainDocumentPart main, DocxStyleNames names, StyleDesign design, string template)
    {
        var part = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        var styles = part.Styles ??= new Styles();
        var all = styles.Elements<Style>().Where(s => s.StyleId?.Value is not null).ToList();
        var paragraphStyles = all.Where(s => s.Type?.Value == StyleValues.Paragraph).ToList();
        var byName = new Dictionary<(StyleKind Kind, string Name), string>(new NameComparer());
        var byId = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in all)
        {
            byId.TryAdd(style.StyleId!.Value!, style);
            if (KindOf(style) is { } kind)
            {
                byName.TryAdd((kind, style.StyleName?.Val?.Value ?? style.StyleId!.Value!), style.StyleId!.Value!);
            }
        }

        var taken = new HashSet<string>(byId.Keys, StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<StyleRole, string>();
        var changed = false;
        foreach (var role in DocxRoles.All)
        {
            var name = role.Name(names);

            // Normal is also the template's default paragraph style, whatever its name.
            var id = byName.GetValueOrDefault((role.Kind, name))
                ?? (role == DocxRoles.Normal && role.Mapped(names) is null ? DefaultParagraphStyle(paragraphStyles) : null);
            if (id is not null)
            {
                found[role] = id;
                continue;
            }

            if (role.Mapped(names) is not null)
            {
                var kind = role.Kind switch
                {
                    StyleKind.Character => "character",
                    StyleKind.Table => "table",
                    _ => "paragraph",
                };
                var available = byName.Keys.Where(k => k.Kind == role.Kind).Select(k => k.Name).Order(StringComparer.OrdinalIgnoreCase).Select(n => $"'{n}'");
                throw new DocumentConversionException(
                    Format.Docx,
                    $"The style '{name}' that {DocxRoles.KeyPrefix}{role.Key} names is not in {template}. Its {kind} styles: {string.Join(", ", available)}.");
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
            byName[(role.Kind, name)] = newId;
            byId[newId] = style;
            found[role] = newId;
            changed = true;
        }

        if (changed)
        {
            styles.Save();
        }

        return new DocxStyles(found, byId);
    }

    private static StyleKind? KindOf(Style style) =>
        style.Type?.Value == StyleValues.Paragraph ? StyleKind.Paragraph
        : style.Type?.Value == StyleValues.Character ? StyleKind.Character
        : style.Type?.Value == StyleValues.Table ? StyleKind.Table
        : null;

    private static string? DefaultParagraphStyle(IEnumerable<Style> paragraphStyles) =>
        paragraphStyles.FirstOrDefault(s => s.Default?.Value == true)?.StyleId?.Value;

    private sealed class NameComparer : IEqualityComparer<(StyleKind Kind, string Name)>
    {
        public bool Equals((StyleKind Kind, string Name) x, (StyleKind Kind, string Name) y) =>
            x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode((StyleKind Kind, string Name) obj) => HashCode.Combine(obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
