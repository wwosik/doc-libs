namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A client's or project's design for the generators: colours, fonts, footer text, images, and optionally its own
/// PowerPoint template. Build one in code or load it with <see cref="BrandPack"/>. Only <see cref="Name"/> and
/// <see cref="BrandColors.Primary"/> are required; everything left out falls back to a documented default or is
/// simply not drawn. A brand is checked when a generator uses it: anything invalid throws
/// <see cref="ArgumentException"/> naming the property.
/// </summary>
public sealed record Brand
{
    /// <summary>The name front matter selects the brand by (<c>template: acme</c>): letters, digits and <c>-</c>, case-insensitive.</summary>
    public required string Name { get; init; }

    /// <summary>The colours. Default: <see cref="BrandColors.Neutral"/>.</summary>
    public BrandColors Colors { get; init; } = BrandColors.Neutral;

    /// <summary>
    /// The colours of decks, when they differ from those of PDFs — for example a lighter primary colour for a template
    /// with dark slides. Every optional property left <see langword="null"/> takes the value from <see cref="Colors"/>.
    /// <see langword="null"/> (the default): decks use <see cref="Colors"/>. PDFs never use these.
    /// </summary>
    public BrandColors? SlideColors { get; init; }

    /// <summary>The fonts. <see langword="null"/>: Liberation Sans and Mono in PDFs, Arial in decks.</summary>
    public BrandFonts? Fonts { get; init; }

    /// <summary>Text at the left of the footer of every page and slide but the cover.</summary>
    public string? FooterText { get; init; }

    /// <summary>Top right on the cover, in the header of PDF pages and bottom right on slides.</summary>
    public BrandImage? Logo { get; init; }

    /// <summary>The logo for dark backgrounds: bottom right on PPTX section dividers, which have the brand gradient.</summary>
    public BrandImage? LogoOnDark { get; init; }

    /// <summary>Bottom right of the cover, as wide as the page and at most 42 % of its height.</summary>
    public BrandImage? CoverArtwork { get; init; }

    /// <summary>A slogan as an image, bottom right on the cover.</summary>
    public BrandImage? Tagline { get; init; }

    /// <summary>A <c>.pptx</c> or <c>.potx</c> to build decks on instead of the built-in master. PDFs ignore it.</summary>
    public PptxBrandTemplate? Pptx { get; init; }

    /// <summary>White, dark titles, blue accents; no fonts of its own, no images, no footer text. Front-matter name <c>neutral</c>.</summary>
    public static Brand Neutral { get; } = new() { Name = "neutral" };
}

/// <summary>
/// Brand colours as RGB hex, <c>#RRGGBB</c> or <c>RRGGBB</c>. Only <see cref="Primary"/> is required; each other
/// colour has a default derived from it.
/// </summary>
public sealed record BrandColors
{
    /// <summary>Titles, headings, the cover title.</summary>
    public required string Primary { get; init; }

    /// <summary>Links. Default: <see cref="Primary"/>.</summary>
    public string? Link { get; init; }

    /// <summary>Header and footer text. Default: grey <c>6B7280</c>.</summary>
    public string? Footer { get; init; }

    /// <summary>The fill of table header rows, which have white bold text. Default: <see cref="Primary"/>.</summary>
    public string? TableHeader { get; init; }

    /// <summary>The fill of every other table row. Default: <see cref="Primary"/> at 10 % on white.</summary>
    public string? TableBand { get; init; }

    /// <summary>Table grid lines. Default: grey <c>D1D5DB</c>.</summary>
    public string? TableLine { get; init; }

    /// <summary>
    /// The background of PPTX section dividers, left to right, at least two colours. Default: <see cref="Primary"/>
    /// to <see cref="Link"/>, or to <see cref="Primary"/> at 60 % on white when both are the same.
    /// </summary>
    public IReadOnlyList<string>? Gradient { get; init; }

    /// <summary>
    /// The PowerPoint theme colours, exactly 12: dark 1, light 1, dark 2, light 2, accent 1–6, hyperlink, followed
    /// hyperlink. Default: black, white, <see cref="Primary"/>, <see cref="TableBand"/>, <see cref="Primary"/>, five
    /// neutral accents, <see cref="Link"/>, violet.
    /// </summary>
    public IReadOnlyList<string>? Theme { get; init; }

    /// <summary>Diagrams drawn by a code-block renderer. Default: every colour derived from the others.</summary>
    public BrandDiagramColors? Diagram { get; init; }

    /// <summary>
    /// Highlights on slides: <c>==marked==</c> text and table rows, the bar of important callouts. Default: theme
    /// accent 2.
    /// </summary>
    public string? Highlight { get; init; }

    /// <summary>Status labels in slide tables, one colour per category. Default: green, blue, amber, red, grey.</summary>
    public BrandLabelColors? Labels { get; init; }

    /// <summary>The colours of <see cref="Brand.Neutral"/>.</summary>
    public static BrandColors Neutral { get; } = new()
    {
        Primary = "1F2937",
        Link = "1D4ED8",
        Footer = "6B7280",
        TableHeader = "1D4ED8",
        TableBand = "F3F4F6",
        TableLine = "D1D5DB",
        Gradient = ["1F2937", "1D4ED8"],
        Theme = ["000000", "FFFFFF", "1F2937", "F3F4F6", "1D4ED8", "0F766E", "B45309", "7C3AED", "BE123C", "4B5563", "1D4ED8", "6D28D9"],
    };

    /// <inheritdoc/>
    public bool Equals(BrandColors? other) =>
        other is not null
        && this.Primary == other.Primary && this.Link == other.Link && this.Footer == other.Footer
        && this.TableHeader == other.TableHeader && this.TableBand == other.TableBand && this.TableLine == other.TableLine
        && SameList(this.Gradient, other.Gradient) && SameList(this.Theme, other.Theme) && Equals(this.Diagram, other.Diagram)
        && this.Highlight == other.Highlight && Equals(this.Labels, other.Labels);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Primary, this.Link, this.TableHeader, this.Gradient?.Count, this.Theme?.Count);

    internal static bool SameList(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is null ? b is null : b is not null && a.SequenceEqual(b);
}

/// <summary>
/// The colours of diagrams, such as Mermaid diagrams, as RGB hex. Each one is optional and derived from the brand's
/// other colours. The background is always white.
/// </summary>
public sealed record BrandDiagramColors
{
    /// <summary>Labels. Default: the body text colour (theme colour dark 1).</summary>
    public string? Text { get; init; }

    /// <summary>Edges and arrows. Default: <see cref="BrandColors.Footer"/>.</summary>
    public string? Line { get; init; }

    /// <summary>Highlights, such as decision nodes and title markers. Default: <see cref="BrandColors.Primary"/>.</summary>
    public string? Accent { get; init; }

    /// <summary>The fill of nodes. Default: <see cref="BrandColors.TableBand"/>.</summary>
    public string? Surface { get; init; }

    /// <summary>The outline of nodes. Default: <see cref="BrandColors.Primary"/>.</summary>
    public string? Border { get; init; }

    /// <summary>
    /// Series colours, such as pie slices, gantt sections and subgraphs, in order, at least one. Default: the theme's
    /// accents 1–6.
    /// </summary>
    public IReadOnlyList<string>? Palette { get; init; }

    /// <inheritdoc/>
    public bool Equals(BrandDiagramColors? other) =>
        other is not null && this.Text == other.Text && this.Line == other.Line && this.Accent == other.Accent
        && this.Surface == other.Surface && this.Border == other.Border && BrandColors.SameList(this.Palette, other.Palette);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Text, this.Line, this.Accent, this.Surface, this.Border, this.Palette?.Count);
}

/// <summary>
/// The colours of status labels on slides, as RGB hex, one per category; a deck assigns its labels to categories in
/// front matter (<c>labels.success: Done</c>). Labels are drawn bold on a light tint of their colour. Each colour is
/// optional.
/// </summary>
public sealed record BrandLabelColors
{
    /// <summary>Done, delivered, passed. Default: green <c>16A34A</c>.</summary>
    public string? Success { get; init; }

    /// <summary>In progress, in review. Default: <see cref="BrandColors.Link"/>.</summary>
    public string? Info { get; init; }

    /// <summary>Next, at risk, waiting. Default: amber <c>D97706</c>.</summary>
    public string? Warning { get; init; }

    /// <summary>Failed, blocked. Default: red <c>DC2626</c>.</summary>
    public string? Danger { get; init; }

    /// <summary>Later, not started, and every label no category names. Default: grey <c>9CA3AF</c>.</summary>
    public string? Neutral { get; init; }
}

/// <summary>A PNG or JPEG image. Compared by content.</summary>
/// <param name="Content">The file's bytes.</param>
public sealed record BrandImage(ReadOnlyMemory<byte> Content)
{
    /// <inheritdoc/>
    public bool Equals(BrandImage? other) => other is not null && this.Content.Span.SequenceEqual(other.Content.Span);

    /// <inheritdoc/>
    public override int GetHashCode() => this.Content.Length;
}

/// <summary>
/// The brand's fonts. The four text faces are required; nothing is synthesized. Mono is optional: without it, code
/// stays in Liberation Mono in PDFs and in the caller's code font in decks. PDFs embed the subset they use; decks name
/// the families and, unless switched off, embed the whole faces. Make sure the fonts' licences allow embedding them
/// in documents.
/// </summary>
public sealed record BrandFonts
{
    /// <summary>Body text.</summary>
    public required BrandFont Regular { get; init; }

    /// <summary>Headings, bold text, table header rows.</summary>
    public required BrandFont Bold { get; init; }

    /// <summary>Italic text and quotes.</summary>
    public required BrandFont Italic { get; init; }

    /// <summary>Bold italic text.</summary>
    public required BrandFont BoldItalic { get; init; }

    /// <summary>Code. <see langword="null"/>: Liberation Mono in PDFs, the caller's code font in decks.</summary>
    public BrandFont? Mono { get; init; }

    /// <summary>Bold code. <see langword="null"/>: <see cref="Mono"/>, or Liberation Mono Bold without it.</summary>
    public BrandFont? MonoBold { get; init; }

    /// <summary>
    /// The family name decks put in their theme. <see langword="null"/>: read from <see cref="Regular"/> (its family
    /// name, name ID 1: the name Windows and PowerPoint group the four faces under, and the one an embedded font is
    /// found by; else its typographic family name).
    /// </summary>
    public string? Family { get; init; }

    /// <summary>Table cells, typically a condensed cut of the family. <see langword="null"/>: tables use the text faces.</summary>
    public BrandTableFonts? Table { get; init; }
}

/// <summary>
/// A separate family for table cells. Regular and bold are required; a missing italic face falls back to the upright
/// face of this family, never to the text italics. Inline code in cells stays mono.
/// </summary>
public sealed record BrandTableFonts
{
    /// <summary>Cell text.</summary>
    public required BrandFont Regular { get; init; }

    /// <summary>Header rows and bold text in cells.</summary>
    public required BrandFont Bold { get; init; }

    /// <summary>Italic text in cells. <see langword="null"/>: <see cref="Regular"/>.</summary>
    public BrandFont? Italic { get; init; }

    /// <summary>Bold italic text in cells. <see langword="null"/>: <see cref="Bold"/>.</summary>
    public BrandFont? BoldItalic { get; init; }

    /// <summary>
    /// The family name decks put in table cells. <see langword="null"/>: read from <see cref="Regular"/> like
    /// <see cref="BrandFonts.Family"/>.
    /// </summary>
    public string? Family { get; init; }
}

/// <summary>A TrueType font: a <c>.ttf</c>, or an <c>.otf</c> with TrueType outlines. Fonts with CFF outlines are not supported. Compared by content.</summary>
/// <param name="Content">The file's bytes.</param>
public sealed record BrandFont(ReadOnlyMemory<byte> Content)
{
    /// <inheritdoc/>
    public bool Equals(BrandFont? other) => other is not null && this.Content.Span.SequenceEqual(other.Content.Span);

    /// <inheritdoc/>
    public override int GetHashCode() => this.Content.Length;
}

/// <summary>
/// A brand's own PowerPoint template, used like a caller's <c>CustomTemplateOptions</c>: its masters, layouts and
/// theme are kept, its slides dropped. A layout left <see langword="null"/> is found by type. Compared by content.
/// </summary>
/// <param name="Content">The bytes of a <c>.pptx</c> or <c>.potx</c>.</param>
public sealed record PptxBrandTemplate(ReadOnlyMemory<byte> Content)
{
    /// <summary>The layout for the cover, by name as PowerPoint shows it.</summary>
    public string? TitleLayout { get; init; }

    /// <summary>The layout for text slides.</summary>
    public string? ContentLayout { get; init; }

    /// <summary>The layout for table and image slides.</summary>
    public string? TitleOnlyLayout { get; init; }

    /// <summary>The layout for section dividers.</summary>
    public string? SectionHeaderLayout { get; init; }

    /// <summary>
    /// The layout of the closing slide, which a last <c>#</c> heading with <c>{.closing}</c> asks for. <see langword="null"/>:
    /// a layout named like a closing slide (<c>Closing</c>, <c>End</c>, <c>Thank you</c>), else the title layout.
    /// </summary>
    public string? ClosingLayout { get; init; }

    /// <summary>
    /// Whether the template's slides are light or dark, which sets the colours of everything the generator draws on
    /// them. Default <see cref="SlideTone.Auto"/>: read from the template.
    /// </summary>
    public SlideTone Tone { get; init; }

    /// <inheritdoc/>
    public bool Equals(PptxBrandTemplate? other) =>
        other is not null && this.Content.Span.SequenceEqual(other.Content.Span)
        && this.TitleLayout == other.TitleLayout && this.ContentLayout == other.ContentLayout
        && this.TitleOnlyLayout == other.TitleOnlyLayout && this.SectionHeaderLayout == other.SectionHeaderLayout
        && this.ClosingLayout == other.ClosingLayout && this.Tone == other.Tone;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Content.Length, this.TitleLayout, this.ContentLayout);
}

/// <summary>Whether the slides of a brand's PowerPoint template are light or dark.</summary>
public enum SlideTone
{
    /// <summary>
    /// Read from the template: dark when its master's title text is light, or else its master background is dark.
    /// Photographic backgrounds are not looked at; set <see cref="Dark"/> when the text colour does not tell.
    /// </summary>
    Auto,

    /// <summary>Light slides: dark text, boxes in light tints of the brand's colours.</summary>
    Light,

    /// <summary>Dark slides: white text, boxes as translucent veils over the background.</summary>
    Dark,
}
