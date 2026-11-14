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

    /// <summary>
    /// Whether the styles are for a caller's template, which has no brand: the deck-syntax styles then take the template's
    /// theme colours (accent 1 for the primary colour, accent 2 for the highlight, background 2 for boxes), as decks do on
    /// a caller's template, with the neutral brand's colours as the fallback Word ignores.
    /// </summary>
    public bool Theme { get; init; }

    /// <summary>Card titles, statements, the bar of notes and tips, dark panels.</summary>
    public WordColor Primary => new(this.Brand.Primary, this.Theme ? "accent1" : null);

    /// <summary>The bar of important alerts, warnings and cautions.</summary>
    public WordColor Highlight => new(this.Brand.Highlight, this.Theme ? "accent2" : null);

    /// <summary>Marks, highlighted table rows and the shading of important alerts: the highlight at 15 % on white.</summary>
    public WordColor HighlightTint => new(ResolvedBrand.Mix(this.Brand.Highlight, 0.15), this.Theme ? "accent2" : null, this.Theme ? "26" : null);

    /// <summary>Panels and the shading of notes and tips.</summary>
    public WordColor Band => new(this.Brand.TableBand, this.Theme ? "background2" : null);

    /// <summary>
    /// The colour of highlighted code of <paramref name="kind"/>, <see langword="null"/> for plain text: the brand's code
    /// colours on the code background; on a caller's template its theme colours (keywords accent 1, types accent 2,
    /// strings accent 6, numbers accent 4, comments the text at 60 %, markup the hyperlink colour).
    /// </summary>
    /// <param name="kind">The token's kind.</param>
    /// <param name="colors">The brand's code colours on the code background, <see cref="CodeColors"/>.</param>
    public WordColor? Code(CodeTokenKind kind, ResolvedCodeColors colors) =>
        colors.Of(kind) is not { } hex ? null
        : !this.Theme ? new WordColor(hex)
        : kind switch
        {
            CodeTokenKind.Keyword => new WordColor(hex, "accent1"),
            CodeTokenKind.Type => new WordColor(hex, "accent2"),
            CodeTokenKind.String => new WordColor(hex, "accent6"),
            CodeTokenKind.Number => new WordColor(hex, "accent4"),
            CodeTokenKind.Comment => new WordColor(hex, "text1", "99"),
            _ => new WordColor(hex, "hyperlink"),
        };

    /// <summary>The brand's code colours, each derived one readable on the code background.</summary>
    public ResolvedCodeColors CodeColors => this.Brand.Code.On(DocxRoles.CodeBackground);

    /// <summary>The colour of a status label category, from the brand (the neutral brand's on a caller's template).</summary>
    public WordColor Label(LabelCategory category) => new(category switch
    {
        LabelCategory.Success => this.Brand.Labels.Success,
        LabelCategory.Info => this.Brand.Labels.Info,
        LabelCategory.Warning => this.Brand.Labels.Warning,
        LabelCategory.Danger => this.Brand.Labels.Danger,
        _ => this.Brand.Labels.Neutral,
    });
}

/// <summary>
/// A colour of a built-in style: RGB hex, and on a caller's template the theme colour it stands for
/// (<c>w:themeColor</c>), lightened by <see cref="Tint"/> (hex, <c>26</c> for 15 %), which Word uses instead.
/// </summary>
internal readonly record struct WordColor(string Hex, string? Theme = null, string? Tint = null)
{
    /// <summary>As the colour of text, <c>w:color</c>.</summary>
    public string Color => $"<w:color w:val=\"{this.Hex}\"{this.ThemeAttributes("themeColor", "themeTint")}/>";

    /// <summary>As shading, <c>w:shd</c>.</summary>
    public string Fill => $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{this.Hex}\"{this.ThemeAttributes("themeFill", "themeFillTint")}/>";

    /// <summary>As a single line border on <paramref name="side"/>, <paramref name="size"/> in eighths of a point.</summary>
    public string Border(string side, int size, int space) =>
        string.Create(CultureInfo.InvariantCulture, $"<w:{side} w:val=\"single\" w:sz=\"{size}\" w:space=\"{space}\" w:color=\"{this.Hex}\"{this.ThemeAttributes("themeColor", "themeTint")}/>");

    private string ThemeAttributes(string color, string tint) =>
        this.Theme is null ? string.Empty : $" w:{color}=\"{this.Theme}\"" + (this.Tint is null ? string.Empty : $" w:{tint}=\"{this.Tint}\"");
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

    // The deck syntax in Word (E09-S02): alerts, marks, labels and coloured words, containers, image rows, the marking.

    /// <summary>The alerts by kind (<c>NOTE</c> … <c>CAUTION</c>): a bar on the left, a light shading.</summary>
    public static readonly IReadOnlyDictionary<string, StyleRole> Alerts = new Dictionary<string, StyleRole>(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = Alert("note", "Note", n => n.Note, (n, v) => n with { Note = v }, important: false),
        ["TIP"] = Alert("tip", "Tip", n => n.Tip, (n, v) => n with { Tip = v }, important: false),
        ["IMPORTANT"] = Alert("important", "Important", n => n.Important, (n, v) => n with { Important = v }, important: true),
        ["WARNING"] = Alert("warning", "Warning", n => n.Warning, (n, v) => n with { Warning = v }, important: true),
        ["CAUTION"] = Alert("caution", "Caution", n => n.Caution, (n, v) => n with { Caution = v }, important: true),
    };

    /// <summary>Word's own <c>Strong</c>, for the title of an alert; found by name, added when missing, never mapped.</summary>
    public static readonly StyleRole Strong = new(
        null, "Strong", "Strong", _ => null, null, _ => "<w:rPr><w:b/><w:bCs/></w:rPr>")
    { Kind = StyleKind.Character, Description = "the title of an alert (a character style)" };

    // Bold on the highlight's tint, as decks mark text; not Word's yellow highlighter.
    public static readonly StyleRole Mark = new(
        "mark", "Mark", "Mark", n => n.Mark, (n, v) => n with { Mark = v },
        d => $"<w:rPr><w:b/><w:bCs/>{d.HighlightTint.Fill}</w:rPr>")
    { Kind = StyleKind.Character, Description = "`==marked==` text (a character style)" };

    /// <summary>Status labels by category: bold in the category's colour, on a light tint of it, like a chip.</summary>
    public static readonly IReadOnlyDictionary<LabelCategory, StyleRole> Labels = new Dictionary<LabelCategory, StyleRole>
    {
        [LabelCategory.Success] = Label(LabelCategory.Success, n => n.LabelSuccess, (n, v) => n with { LabelSuccess = v }),
        [LabelCategory.Info] = Label(LabelCategory.Info, n => n.LabelInfo, (n, v) => n with { LabelInfo = v }),
        [LabelCategory.Warning] = Label(LabelCategory.Warning, n => n.LabelWarning, (n, v) => n with { LabelWarning = v }),
        [LabelCategory.Danger] = Label(LabelCategory.Danger, n => n.LabelDanger, (n, v) => n with { LabelDanger = v }),
        [LabelCategory.Neutral] = Label(LabelCategory.Neutral, n => n.LabelNeutral, (n, v) => n with { LabelNeutral = v }),
    };

    /// <summary>Coloured words (<c>[words]{.success}</c>) by category: bold in the category's colour.</summary>
    public static readonly IReadOnlyDictionary<LabelCategory, StyleRole> Colours = new Dictionary<LabelCategory, StyleRole>
    {
        [LabelCategory.Success] = Colour(LabelCategory.Success, n => n.ColourSuccess, (n, v) => n with { ColourSuccess = v }),
        [LabelCategory.Info] = Colour(LabelCategory.Info, n => n.ColourInfo, (n, v) => n with { ColourInfo = v }),
        [LabelCategory.Warning] = Colour(LabelCategory.Warning, n => n.ColourWarning, (n, v) => n with { ColourWarning = v }),
        [LabelCategory.Danger] = Colour(LabelCategory.Danger, n => n.ColourDanger, (n, v) => n with { ColourDanger = v }),
        [LabelCategory.Neutral] = Colour(LabelCategory.Neutral, n => n.ColourNeutral, (n, v) => n with { ColourNeutral = v }),
    };

    // No outline level, so it is no chapter and not in the table of contents; never numbered.
    public static readonly StyleRole ContainerHeading = new(
        "containerHeading", "Container Heading", "ContainerHeading", n => n.ContainerHeading, (n, v) => n with { ContainerHeading = v },
        d => "<w:pPr><w:keepNext/><w:spacing w:before=\"200\" w:after=\"60\"/></w:pPr>"
            + $"<w:rPr><w:b/><w:bCs/>{d.Primary.Color}<w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/></w:rPr>")
    { Description = "headings inside containers, such as a card's `###`: unnumbered and not in the table of contents" };

    public static readonly StyleRole Statement = new(
        "statement", "Statement", "Statement", n => n.Statement, (n, v) => n with { Statement = v },
        d => "<w:pPr><w:spacing w:before=\"120\" w:after=\"120\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr>"
            + $"<w:rPr>{MajorFont}<w:b/><w:bCs/>{d.Primary.Color}<w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/></w:rPr>")
    { Description = "the statement of `::: statement` and a card's figure" };

    // Shaded with a border all round: Word joins consecutive paragraphs of the same border into one box.
    public static readonly StyleRole Panel = new(
        "panel", "Panel", "Panel", n => n.Panel, (n, v) => n with { Panel = v },
        d => PanelDefinition(new WordColor(d.Brand.TableLine), d.Band, dark: false))
    { Description = "the paragraphs of `::: panel`" };

    public static readonly StyleRole PanelDark = new(
        "panelDark", "Panel Dark", "PanelDark", n => n.PanelDark, (n, v) => n with { PanelDark = v },
        d => PanelDefinition(d.Primary, d.Primary, dark: true))
    { Description = "the paragraphs of `::: panel dark`" };

    public static readonly StyleRole Tag = new(
        "tag", "Tag", "Tag", n => n.Tag, (n, v) => n with { Tag = v },
        d => $"<w:rPr>{Fonts(d.CodeFont)}<w:noProof/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{TagBackground}\"/></w:rPr>")
    { Kind = StyleKind.Character, Description = "tags, code spans shown as chips (a character style)" };

    // No borders, no shading; the cells' paragraphs without spacing.
    public static readonly StyleRole ImageRow = new(
        "imageRow", "Image Row", "ImageRow", n => n.ImageRow, (n, v) => n with { ImageRow = v },
        _ => "<w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>"
            + "<w:tblPr><w:tblBorders>"
            + string.Concat(new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => $"<w:{side} w:val=\"nil\"/>"))
            + "</w:tblBorders><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"60\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"60\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>")
    { Kind = StyleKind.Table, Description = "a row of images side by side (a table style)" };

    // Right-aligned bold capitals at the footer's size; a confidential marking is in the danger colour (Colour Danger).
    public static readonly StyleRole Classification = new(
        "classification", "Classification", "Classification", n => n.Classification, (n, v) => n with { Classification = v },
        d => "<w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/><w:jc w:val=\"right\"/></w:pPr>"
            + $"<w:rPr><w:b/><w:bCs/><w:caps/><w:color w:val=\"{d.Brand.Footer}\"/><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr>")
    { Description = "the classification marking in the header" };

    // Above the cover title, which then starts right under it: the space above the title moves above the marking.
    public static readonly StyleRole ClassificationCover = new(
        "classificationCover", "Classification Cover", "ClassificationCover", n => n.ClassificationCover, (n, v) => n with { ClassificationCover = v },
        d => $"<w:pPr><w:keepNext/><w:spacing w:before=\"{N(Math.Max(0, d.CoverTitleSpace - CoverMarkingHeight))}\" w:after=\"120\"/><w:ind w:right=\"1440\"/></w:pPr>"
            + $"<w:rPr><w:b/><w:bCs/><w:caps/><w:color w:val=\"{d.Brand.Footer}\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr>")
    { Description = "the classification marking above the cover title" };

    /// <summary>The height of the cover marking with its spacing, in twentieths of a point, taken off the space above it.</summary>
    public const int CoverMarkingHeight = 400;

    /// <summary>The background of tags: light grey.</summary>
    public const string TagBackground = "E5E7EB";

    /// <summary>Every role, <see cref="Normal"/> first.</summary>
    public static readonly IReadOnlyList<StyleRole> All =
    [
        Normal, Title, Subtitle, CoverDetails, TocHeading, .. Headings, .. TocLevels,
        ListBullet, ListNumber, ListTask, DefinitionTerm, Definition, Quote, Code, CodeCharacter, Rule, Caption, TableTitle, Table,
        FootnoteText, FootnoteReference, Hyperlink,
        .. Alerts.Values, Strong, Mark, .. Labels.Values, .. Colours.Values, ContainerHeading, Statement, Panel, PanelDark, Tag, ImageRow,
        Classification, ClassificationCover,
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

    // Notes and tips: the bar in the primary colour on the band; the others in the highlight on its tint.
    private static StyleRole Alert(string key, string name, Func<DocxStyleNames, string?> mapped, Func<DocxStyleNames, string, DocxStyleNames> with, bool important) => new(
        key, name, name, mapped, with,
        d => $"<w:pPr><w:pBdr>{(important ? d.Highlight : d.Primary).Border("left", 24, 8)}</w:pBdr>{(important ? d.HighlightTint : d.Band).Fill}"
            + "<w:spacing w:before=\"0\" w:after=\"80\"/><w:ind w:left=\"240\" w:right=\"120\"/></w:pPr>")
    { Description = $"alerts of the kind `[!{name.ToUpperInvariant()}]`" };

    private static StyleRole Label(LabelCategory category, Func<DocxStyleNames, string?> mapped, Func<DocxStyleNames, string, DocxStyleNames> with)
    {
        var name = category.ToString();
        return new StyleRole(
            "label" + name, "Label " + name, "Label" + name, mapped, with,
            d => $"<w:rPr><w:b/><w:bCs/>{d.Label(category).Color}{new WordColor(ResolvedBrand.Mix(d.Label(category).Hex, 0.22)).Fill}</w:rPr>")
        { Kind = StyleKind.Character, Description = $"status labels of the category {name.ToLowerInvariant()} (a character style)" };
    }

    private static StyleRole Colour(LabelCategory category, Func<DocxStyleNames, string?> mapped, Func<DocxStyleNames, string, DocxStyleNames> with)
    {
        var name = category.ToString();
        return new StyleRole(
            "colour" + name, "Colour " + name, "Colour" + name, mapped, with,
            d => $"<w:rPr><w:b/><w:bCs/>{d.Label(category).Color}</w:rPr>")
        { Kind = StyleKind.Character, Description = $"`[words]{{.{name.ToLowerInvariant()}}}` (a character style)" };
    }

    private static string PanelDefinition(WordColor line, WordColor fill, bool dark) =>
        "<w:pPr><w:pBdr>" + string.Concat(new[] { "top", "left", "bottom", "right" }.Select(side => line.Border(side, 4, 6))) + "</w:pBdr>"
        + fill.Fill + "<w:spacing w:before=\"0\" w:after=\"80\"/><w:ind w:left=\"160\" w:right=\"160\"/></w:pPr>"
        + (dark ? "<w:rPr><w:color w:val=\"FFFFFF\"/></w:rPr>" : string.Empty);

    private static string Fonts(string family)
    {
        var name = System.Security.SecurityElement.Escape(family);
        return $"<w:rFonts w:ascii=\"{name}\" w:hAnsi=\"{name}\" w:eastAsia=\"{name}\" w:cs=\"{name}\"/>";
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
