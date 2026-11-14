using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// A part of a document that takes a style: its front-matter key (<c>styles.&lt;key&gt;</c>), the style's default
/// name, the caller's name for it in <see cref="DocxStyleNames"/>, and the built-in definition, which the built-in
/// template writes and a template that lacks the style gets.
/// </summary>
/// <param name="Key">The key after <c>styles.</c>; <see langword="null"/> for a style no caller names (the TOC levels).</param>
/// <param name="DefaultName">The style's name when the caller names none, as Word stores it (<c>heading 1</c>).</param>
/// <param name="Id">The style's id in the built-in template, and the id a missing style is added under if it is free.</param>
/// <param name="Mapped">The caller's name for the role.</param>
/// <param name="With">The names with the caller's name for the role set.</param>
/// <param name="Definition">
/// The style's content after its name in the built-in design: <c>w:pPr</c> and <c>w:rPr</c>, and for a table style
/// <c>w:tblPr</c> and the <c>w:tblStylePr</c> of the header row and the bands.
/// </param>
internal sealed record StyleRole(
    string? Key,
    string DefaultName,
    string Id,
    Func<DocxStyleNames, string?> Mapped,
    Func<DocxStyleNames, string, DocxStyleNames>? With,
    Func<StyleDesign, string> Definition)
{
    /// <summary>Whether the role takes a paragraph style (the default), a character style or a table style.</summary>
    public StyleKind Kind { get; init; } = StyleKind.Paragraph;

    /// <summary>What the role is for, in the guide's words: "body text", "inline code".</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The name the role's style is looked up by.</summary>
    public string Name(DocxStyleNames names) => this.Mapped(names) ?? this.DefaultName;
}

/// <summary>The kind of a Word style, its <c>w:type</c>.</summary>
internal enum StyleKind
{
    Paragraph,
    Character,
    Table,
}

/// <summary>What the built-in style definitions depend on: the brand's colours and fonts, and the page.</summary>
/// <param name="Brand">The brand, or the neutral one for a caller's template.</param>
/// <param name="CoverTitleSpace">The space above the cover title, in twentieths of a point, so it sits at 28 % of the page.</param>
/// <param name="TextWidth">The width between the margins, in twentieths of a point.</param>
internal sealed record StyleDesign(ResolvedBrand Brand, int CoverTitleSpace, int TextWidth)
{
    /// <summary>The family of code.</summary>
    public string CodeFont { get; init; } = DocxOptions.DefaultCodeFont;

    /// <summary>
    /// Whether Heading 1–3 are numbered through the built-in multilevel list (<see cref="DocxNumbering.HeadingNumId"/>):
    /// on the built-in template with heading numbers on, never for a style added to a caller's template.
    /// </summary>
    public bool HeadingNumbers { get; init; }
}

/// <summary>
/// Every role, in the order the built-in <c>styles.xml</c> lists them: the paragraph styles of E09-S01 (cover, table of
/// contents, headings, body text), then the content roles (lists, definitions, quotes, code, rules, captions, tables,
/// footnotes, links). Sizes and colours follow the PDF's.
/// </summary>
internal static class DocxRoles
{
    /// <summary>The prefix of the front-matter keys that name a role's style.</summary>
    public const string KeyPrefix = "styles.";

    /// <summary>The grey of quotes, as in the PDF.</summary>
    public const string Grey = "4B5563";

    /// <summary>The background of code, as in the PDF.</summary>
    public const string CodeBackground = "F3F4F6";

    // Sizes in half points, as the PDF sets them: body 10.5 pt, headings 20 / 16 / 13.5 / 12 / 11 / 10.5 pt.
    private static readonly int[] HeadingSizes = [40, 32, 27, 24, 22, 21];

    // Body text takes the document defaults (10.5 pt, 6 pt after, 1.35 lines), so that a table style's size and spacing
    // apply in table cells: Word lets a paragraph style's own settings win over the table style's.
    public static readonly StyleRole Normal = new(
        "normal", "Normal", "Normal", n => n.Normal, (n, v) => n with { Normal = v }, _ => string.Empty)
    { Description = "body text" };

    public static readonly StyleRole Title = new(
        "title", "Title", "Title", n => n.Title, (n, v) => n with { Title = v },
        d => $"<w:pPr><w:keepNext/><w:spacing w:before=\"{N(d.CoverTitleSpace)}\" w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/>"
            + "<w:ind w:right=\"1440\"/></w:pPr>"
            + $"<w:rPr>{MajorFont}<w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/><w:sz w:val=\"60\"/><w:szCs w:val=\"60\"/></w:rPr>")
    { Description = "the cover title" };

    public static readonly StyleRole Subtitle = new(
        "subtitle", "Subtitle", "Subtitle", n => n.Subtitle, (n, v) => n with { Subtitle = v },
        _ => "<w:pPr><w:keepNext/><w:spacing w:after=\"80\"/><w:ind w:right=\"1440\"/></w:pPr><w:rPr><w:sz w:val=\"26\"/><w:szCs w:val=\"26\"/></w:rPr>")
    { Description = "the paragraphs under the cover title" };

    public static readonly StyleRole CoverDetails = new(
        "coverDetails", "Cover Details", "CoverDetails", n => n.CoverDetails, (n, v) => n with { CoverDetails = v },
        d => $"<w:pPr><w:spacing w:before=\"240\" w:after=\"0\"/></w:pPr><w:rPr><w:color w:val=\"{d.Brand.Footer}\"/></w:rPr>")
    { Description = "the author and date on the cover" };

    public static readonly StyleRole TocHeading = new(
        "tocHeading", "TOC Heading", "TOCHeading", n => n.TocHeading, (n, v) => n with { TocHeading = v },
        d => "<w:pPr><w:keepNext/><w:spacing w:after=\"240\"/></w:pPr>"
            + $"<w:rPr>{MajorFont}<w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr>")
    { Description = "the heading of the table of contents" };

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

    // Lists: the marker and the indent come from the numbering (18 pt a level, as in the PDF), the style sets the gap.
    public static readonly StyleRole ListBullet = new(
        "listBullet", "List Bullet", "ListBullet", n => n.ListBullet, (n, v) => n with { ListBullet = v }, _ => ListSpacing)
    { Description = "items of bulleted lists" };

    public static readonly StyleRole ListNumber = new(
        "listNumber", "List Number", "ListNumber", n => n.ListNumber, (n, v) => n with { ListNumber = v }, _ => ListSpacing)
    { Description = "items of numbered lists" };

    public static readonly StyleRole ListTask = new(
        "listTask", "List Task", "ListTask", n => n.ListTask, (n, v) => n with { ListTask = v }, _ => ListSpacing)
    { Description = "items of task lists, which start with a check box" };

    public static readonly StyleRole DefinitionTerm = new(
        "definitionTerm", "Definition Term", "DefinitionTerm", n => n.DefinitionTerm, (n, v) => n with { DefinitionTerm = v },
        _ => "<w:pPr><w:keepNext/><w:spacing w:after=\"40\"/></w:pPr><w:rPr><w:b/><w:bCs/></w:rPr>")
    { Description = "the terms of definition lists" };

    public static readonly StyleRole Definition = new(
        "definition", "Definition", "Definition", n => n.Definition, (n, v) => n with { Definition = v },
        _ => "<w:pPr><w:ind w:left=\"360\"/></w:pPr>")
    { Description = "the descriptions of definition lists" };

    public static readonly StyleRole Quote = new(
        "quote", "Quote", "Quote", n => n.Quote, (n, v) => n with { Quote = v },
        d => $"<w:pPr><w:pBdr><w:left w:val=\"single\" w:sz=\"16\" w:space=\"10\" w:color=\"{d.Brand.TableLine}\"/></w:pBdr>"
            + $"<w:ind w:left=\"280\"/></w:pPr><w:rPr><w:color w:val=\"{Grey}\"/></w:rPr>")
    { Description = "quotes and alerts" };

    public static readonly StyleRole Code = new(
        "code", "Code", "Code", n => n.Code, (n, v) => n with { Code = v },
        d => $"<w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{CodeBackground}\"/>"
            + "<w:spacing w:before=\"120\" w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/><w:ind w:left=\"120\" w:right=\"120\"/><w:contextualSpacing/></w:pPr>"
            + $"<w:rPr>{Fonts(d.CodeFont)}<w:noProof/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr>")
    { Description = "each line of a code block" };

    public static readonly StyleRole CodeCharacter = new(
        "codeCharacter", "Code Character", "CodeCharacter", n => n.CodeCharacter, (n, v) => n with { CodeCharacter = v },
        d => $"<w:rPr>{Fonts(d.CodeFont)}<w:noProof/><w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{CodeBackground}\"/></w:rPr>")
    { Kind = StyleKind.Character, Description = "inline code (a character style)" };

    public static readonly StyleRole Rule = new(
        "rule", "Rule", "Rule", n => n.Rule, (n, v) => n with { Rule = v },
        d => $"<w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"{d.Brand.TableLine}\"/></w:pBdr>"
            + "<w:spacing w:before=\"120\" w:after=\"240\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"8\"/><w:szCs w:val=\"8\"/></w:rPr>")
    { Description = "horizontal rules, an empty paragraph with a bottom border" };

    public static readonly StyleRole Caption = new(
        "caption", "caption", "Caption", n => n.Caption, (n, v) => n with { Caption = v },
        _ => "<w:pPr><w:spacing w:before=\"60\" w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/><w:jc w:val=\"center\"/></w:pPr>"
            + "<w:rPr><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr>")
    { Description = "the captions of figures and tables" };

    public static readonly StyleRole TableTitle = new(
        "tableTitle", "Table Title", "TableTitle", n => n.TableTitle, (n, v) => n with { TableTitle = v },
        d => "<w:pPr><w:keepNext/><w:spacing w:before=\"200\" w:after=\"80\"/></w:pPr>"
            + $"<w:rPr><w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/></w:rPr>")
    { Description = "the title of a table, a paragraph of bold text right before it" };

    // Header row bold white on the header colour, banded rows, grid lines; 9.5 pt in the brand's table family, if any.
    public static readonly StyleRole Table = new(
        "table", "Doc Table", "DocTable", n => n.Table, (n, v) => n with { Table = v },
        d => "<w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>"
            + $"<w:rPr>{(d.Brand.TableFontFamily is { } family ? Fonts(family) : string.Empty)}<w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/></w:rPr>"
            + "<w:tblPr><w:tblStyleRowBandSize w:val=\"1\"/><w:tblBorders>"
            + string.Concat(new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => $"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"{d.Brand.TableLine}\"/>"))
            + "</w:tblBorders><w:tblCellMar><w:top w:w=\"80\" w:type=\"dxa\"/><w:left w:w=\"80\" w:type=\"dxa\"/><w:bottom w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>"
            + "<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/><w:bCs/><w:color w:val=\"FFFFFF\"/></w:rPr><w:tblPr/><w:trPr><w:tblHeader/></w:trPr>"
            + $"<w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{d.Brand.TableHeader}\"/></w:tcPr></w:tblStylePr>"
            + $"<w:tblStylePr w:type=\"band2Horz\"><w:tblPr/><w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{d.Brand.TableBand}\"/></w:tcPr></w:tblStylePr>")
    { Kind = StyleKind.Table, Description = "tables (a table style)" };

    public static readonly StyleRole FootnoteText = new(
        "footnoteText", "footnote text", "FootnoteText", n => n.FootnoteText, (n, v) => n with { FootnoteText = v },
        _ => "<w:pPr><w:spacing w:after=\"40\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr>")
    { Description = "the text of footnotes" };

    public static readonly StyleRole FootnoteReference = new(
        "footnoteReference", "footnote reference", "FootnoteReference", n => n.FootnoteReference, (n, v) => n with { FootnoteReference = v },
        _ => "<w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr>")
    { Kind = StyleKind.Character, Description = "footnote references (a character style)" };

    public static readonly StyleRole Hyperlink = new(
        "hyperlink", "Hyperlink", "Hyperlink", n => n.Hyperlink, (n, v) => n with { Hyperlink = v },
        d => $"<w:rPr><w:color w:val=\"{d.Brand.Link}\"/><w:u w:val=\"single\"/></w:rPr>")
    { Kind = StyleKind.Character, Description = "links (a character style)" };

    /// <summary>Every role, <see cref="Normal"/> first.</summary>
    public static readonly IReadOnlyList<StyleRole> All =
    [
        Normal, Title, Subtitle, CoverDetails, TocHeading, .. Headings, .. TocLevels,
        ListBullet, ListNumber, ListTask, DefinitionTerm, Definition, Quote, Code, CodeCharacter, Rule, Caption, TableTitle, Table,
        FootnoteText, FootnoteReference, Hyperlink,
    ];

    /// <summary>The front-matter keys of the roles a caller can name, <c>styles.title</c> and on.</summary>
    public static readonly IReadOnlyList<string> Keys = [.. All.Where(r => r.Key is not null).Select(r => KeyPrefix + r.Key)];

    private const string MajorFont = "<w:rFonts w:asciiTheme=\"majorHAnsi\" w:hAnsiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:cstheme=\"majorBidi\"/>";

    private const string ListSpacing = "<w:pPr><w:spacing w:after=\"40\"/></w:pPr>";

    /// <summary>The role of the front-matter <paramref name="key"/> (one of <see cref="Keys"/>).</summary>
    public static StyleRole OfKey(string key) => All.Single(r => r.Key is not null && KeyPrefix + r.Key == key);

    /// <summary>
    /// The role's style as a <c>w:style</c> element in the namespace prefix <c>w</c>: under <paramref name="id"/>,
    /// based on <paramref name="basedOn"/> (none for <c>Normal</c>), followed by <paramref name="next"/>. Character and
    /// table styles are based on nothing and have no next style.
    /// </summary>
    public static string Xml(StyleRole role, StyleDesign design, string id, string? basedOn, string? next, bool isDefault = false)
    {
        var name = System.Security.SecurityElement.Escape(role.DefaultName);
        var toc = TocLevels.Contains(role);
        var paragraph = role.Kind == StyleKind.Paragraph;
        var type = role.Kind switch
        {
            StyleKind.Character => "character",
            StyleKind.Table => "table",
            _ => "paragraph",
        };
        return $"<w:style w:type=\"{type}\"{(isDefault ? " w:default=\"1\"" : string.Empty)} w:styleId=\"{id}\">"
            + $"<w:name w:val=\"{name}\"/>"
            + (basedOn is null || !paragraph ? string.Empty : $"<w:basedOn w:val=\"{basedOn}\"/>")
            + (next is null || !paragraph ? string.Empty : $"<w:next w:val=\"{next}\"/>")
            + (toc ? "<w:uiPriority w:val=\"39\"/><w:unhideWhenUsed/>" : "<w:qFormat/>")
            + role.Definition(design)
            + "</w:style>";
    }

    private static StyleRole Heading(int level, Func<DocxStyleNames, string?> mapped, Func<DocxStyleNames, string, DocxStyleNames> with)
    {
        var text = N(level);
        var size = N(HeadingSizes[level - 1]);
        var before = level <= 2 ? "360" : "240";
        return new StyleRole(
            "heading" + text,
            "heading " + text,
            "Heading" + text,
            mapped,
            with,
            d => "<w:pPr><w:keepNext/><w:keepLines/>"
                + (d.HeadingNumbers && level <= 3 ? $"<w:numPr><w:ilvl w:val=\"{N(level - 1)}\"/><w:numId w:val=\"{N(DocxNumbering.HeadingNumId)}\"/></w:numPr>" : string.Empty)
                + $"<w:spacing w:before=\"{before}\" w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/>"
                + $"<w:outlineLvl w:val=\"{N(level - 1)}\"/></w:pPr>"
                + $"<w:rPr>{MajorFont}<w:b/><w:bCs/><w:color w:val=\"{d.Brand.Primary}\"/><w:sz w:val=\"{size}\"/><w:szCs w:val=\"{size}\"/></w:rPr>")
        { Description = "headings of level " + text };
    }

    // Indented by level; a dotted tab at the right margin takes the page number when Word updates the field.
    private static StyleRole TocLevel(int level)
    {
        var text = N(level);
        var indent = N((level - 1) * 220);
        return new StyleRole(
            null,
            "toc " + text,
            "TOC" + text,
            _ => null,
            null,
            d => $"<w:pPr><w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"{N(d.TextWidth)}\"/></w:tabs>"
                + $"<w:spacing w:after=\"{(level == 1 ? "100" : "60")}\"/><w:ind w:left=\"{indent}\"/></w:pPr>"
                + (level == 1 ? "<w:rPr><w:b/><w:bCs/></w:rPr>" : string.Empty));
    }

    private static string Fonts(string family)
    {
        var name = System.Security.SecurityElement.Escape(family);
        return $"<w:rFonts w:ascii=\"{name}\" w:hAnsi=\"{name}\" w:eastAsia=\"{name}\" w:cs=\"{name}\"/>";
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
