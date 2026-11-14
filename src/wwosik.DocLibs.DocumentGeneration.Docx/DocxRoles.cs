using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// A part of a document that takes a paragraph style: its front-matter key (<c>styles.&lt;key&gt;</c>), the style's
/// default name, the caller's name for it in <see cref="DocxStyleNames"/>, and the built-in definition, which the
/// built-in template writes and a template that lacks the style gets.
/// </summary>
/// <param name="Key">The key after <c>styles.</c>; <see langword="null"/> for a style no caller names (the TOC levels).</param>
/// <param name="DefaultName">The style's name when the caller names none, as Word stores it (<c>heading 1</c>).</param>
/// <param name="Id">The style's id in the built-in template, and the id a missing style is added under if it is free.</param>
/// <param name="Mapped">The caller's name for the role.</param>
/// <param name="With">The names with the caller's name for the role set.</param>
/// <param name="Definition">The style's <c>w:pPr</c> and <c>w:rPr</c> in the built-in design.</param>
internal sealed record StyleRole(
    string? Key,
    string DefaultName,
    string Id,
    Func<DocxStyleNames, string?> Mapped,
    Func<DocxStyleNames, string, DocxStyleNames>? With,
    Func<StyleDesign, string> Definition)
{
    /// <summary>The name the role's style is looked up by.</summary>
    public string Name(DocxStyleNames names) => this.Mapped(names) ?? this.DefaultName;
}

/// <summary>What the built-in style definitions depend on: the brand's colours and the page.</summary>
/// <param name="Brand">The brand, or the neutral one for a caller's template.</param>
/// <param name="CoverTitleSpace">The space above the cover title, in twentieths of a point, so it sits at 28 % of the page.</param>
/// <param name="TextWidth">The width between the margins, in twentieths of a point.</param>
internal sealed record StyleDesign(ResolvedBrand Brand, int CoverTitleSpace, int TextWidth);

/// <summary>
/// Every role, in the order the built-in <c>styles.xml</c> lists them. The content roles of later stories (lists,
/// quotes, code, captions, tables, footnotes, links) join this list the same way.
/// </summary>
internal static class DocxRoles
{
    /// <summary>The prefix of the front-matter keys that name a role's style.</summary>
    public const string KeyPrefix = "styles.";

    // Sizes in half points, as the PDF sets them: body 10.5 pt, headings 20 / 16 / 13.5 / 12 / 11 / 10.5 pt.
    private static readonly int[] HeadingSizes = [40, 32, 27, 24, 22, 21];

    public static readonly StyleRole Normal = new(
        "normal", "Normal", "Normal", n => n.Normal, (n, v) => n with { Normal = v },
        _ => "<w:pPr><w:spacing w:after=\"120\" w:line=\"324\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"21\"/><w:szCs w:val=\"21\"/></w:rPr>");

    public static readonly StyleRole Title = new(
        "title", "Title", "Title", n => n.Title, (n, v) => n with { Title = v },
        d => $"<w:pPr><w:keepNext/><w:spacing w:before=\"{d.CoverTitleSpace.ToString(CultureInfo.InvariantCulture)}\" w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/>"
            + "<w:ind w:right=\"1440\"/></w:pPr>"
            + $"<w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:hAnsiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:cstheme=\"majorBidi\"/><w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/><w:sz w:val=\"60\"/><w:szCs w:val=\"60\"/></w:rPr>");

    public static readonly StyleRole Subtitle = new(
        "subtitle", "Subtitle", "Subtitle", n => n.Subtitle, (n, v) => n with { Subtitle = v },
        _ => "<w:pPr><w:keepNext/><w:spacing w:after=\"80\"/><w:ind w:right=\"1440\"/></w:pPr><w:rPr><w:sz w:val=\"26\"/><w:szCs w:val=\"26\"/></w:rPr>");

    public static readonly StyleRole CoverDetails = new(
        "coverDetails", "Cover Details", "CoverDetails", n => n.CoverDetails, (n, v) => n with { CoverDetails = v },
        d => $"<w:pPr><w:spacing w:before=\"240\" w:after=\"0\"/></w:pPr><w:rPr><w:color w:val=\"{d.Brand.Footer}\"/></w:rPr>");

    public static readonly StyleRole TocHeading = new(
        "tocHeading", "TOC Heading", "TOCHeading", n => n.TocHeading, (n, v) => n with { TocHeading = v },
        d => "<w:pPr><w:keepNext/><w:spacing w:after=\"240\"/></w:pPr>"
            + $"<w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:hAnsiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:cstheme=\"majorBidi\"/><w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr>");

    /// <summary>Heading 1–6, by level from 1.</summary>
    public static readonly IReadOnlyList<StyleRole> Headings =
    [
        Heading(1, n => n.Heading1, (n, v) => n with { Heading1 = v }),
        Heading(2, n => n.Heading2, (n, v) => n with { Heading2 = v }),
        Heading(3, n => n.Heading3, (n, v) => n with { Heading3 = v }),
        Heading(4, n => n.Heading4, (n, v) => n with { Heading4 = v }),
        Heading(5, n => n.Heading5, (n, v) => n with { Heading5 = v }),
        Heading(6, n => n.Heading6, (n, v) => n with { Heading6 = v }),
    ];

    /// <summary>The entries of the table of contents, by level from 1: Word's own <c>toc 1</c>–<c>toc 3</c>.</summary>
    public static readonly IReadOnlyList<StyleRole> TocLevels = [TocLevel(1), TocLevel(2), TocLevel(3)];

    /// <summary>Every role, <see cref="Normal"/> first.</summary>
    public static readonly IReadOnlyList<StyleRole> All = [Normal, Title, Subtitle, CoverDetails, TocHeading, .. Headings, .. TocLevels];

    /// <summary>The front-matter keys of the roles a caller can name, <c>styles.title</c> and on.</summary>
    public static readonly IReadOnlyList<string> Keys = [.. All.Where(r => r.Key is not null).Select(r => KeyPrefix + r.Key)];

    /// <summary>The role of the front-matter <paramref name="key"/> (one of <see cref="Keys"/>).</summary>
    public static StyleRole OfKey(string key) => All.Single(r => r.Key is not null && KeyPrefix + r.Key == key);

    /// <summary>
    /// The role's style as a <c>w:style</c> element in the namespace prefix <c>w</c>: under <paramref name="id"/>,
    /// based on <paramref name="basedOn"/> (none for <c>Normal</c>), followed by <paramref name="next"/>.
    /// </summary>
    public static string Xml(StyleRole role, StyleDesign design, string id, string? basedOn, string? next, bool isDefault = false)
    {
        var name = System.Security.SecurityElement.Escape(role.DefaultName);
        var toc = TocLevels.Contains(role);
        return $"<w:style w:type=\"paragraph\"{(isDefault ? " w:default=\"1\"" : string.Empty)} w:styleId=\"{id}\">"
            + $"<w:name w:val=\"{name}\"/>"
            + (basedOn is null ? string.Empty : $"<w:basedOn w:val=\"{basedOn}\"/>")
            + (next is null ? string.Empty : $"<w:next w:val=\"{next}\"/>")
            + (toc ? "<w:uiPriority w:val=\"39\"/><w:unhideWhenUsed/>" : "<w:qFormat/>")
            + role.Definition(design)
            + "</w:style>";
    }

    private static StyleRole Heading(int level, Func<DocxStyleNames, string?> mapped, Func<DocxStyleNames, string, DocxStyleNames> with)
    {
        var text = level.ToString(CultureInfo.InvariantCulture);
        var size = HeadingSizes[level - 1].ToString(CultureInfo.InvariantCulture);
        var before = level <= 2 ? "360" : "240";
        return new StyleRole(
            "heading" + text,
            "heading " + text,
            "Heading" + text,
            mapped,
            with,
            d => $"<w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"{before}\" w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/>"
                + $"<w:outlineLvl w:val=\"{(level - 1).ToString(CultureInfo.InvariantCulture)}\"/></w:pPr>"
                + "<w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:hAnsiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:cstheme=\"majorBidi\"/>"
                + $"<w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/><w:sz w:val=\"{size}\"/><w:szCs w:val=\"{size}\"/></w:rPr>");
    }

    // Indented by level; a dotted tab at the right margin takes the page number when Word updates the field.
    private static StyleRole TocLevel(int level)
    {
        var text = level.ToString(CultureInfo.InvariantCulture);
        var indent = ((level - 1) * 220).ToString(CultureInfo.InvariantCulture);
        return new StyleRole(
            null,
            "toc " + text,
            "TOC" + text,
            _ => null,
            null,
            d => $"<w:pPr><w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"{d.TextWidth.ToString(CultureInfo.InvariantCulture)}\"/></w:tabs>"
                + $"<w:spacing w:after=\"{(level == 1 ? "100" : "60")}\"/><w:ind w:left=\"{indent}\"/></w:pPr>"
                + (level == 1 ? "<w:rPr><w:b/><w:bCs/></w:rPr>" : string.Empty));
    }
}
