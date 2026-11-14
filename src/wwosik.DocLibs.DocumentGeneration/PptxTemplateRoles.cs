using System.Text;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// What a deck template's layouts and placeholders are for, beyond what their OOXML types say: the layouts of further
/// kinds of slide, the role of placeholders inside a layout, the outline levels Markdown's text goes to, a footer
/// pattern, and where the content grid is. Everything is optional: a template that declares nothing is used as
/// before. Names are those PowerPoint shows. Compared by content.
/// </summary>
public sealed partial record PptxTemplateRoles
{
    /// <summary>The layouts for further kinds of slide, by name. Default: none named.</summary>
    public PptxLayoutRoles Layouts { get; init; } = new();

    /// <summary>
    /// The roles of placeholders, each by its layout's name and the placeholder's shape name as the Selection Pane shows
    /// it (case-insensitive). Several <see cref="PlaceholderRole.Body"/> entries of one layout give the order of its
    /// bodies. A placeholder not listed gets the role its type says. Default: none.
    /// </summary>
    public IReadOnlyList<PptxPlaceholderRole> Placeholders { get; init; } = [];

    /// <summary>
    /// The outline levels <c>###</c> headings, paragraphs and list items go to in placeholders, for templates whose
    /// levels have meanings of their own. <see langword="null"/> (the default): Markdown's levels, as before.
    /// </summary>
    public PptxTextLevels? TextLevels { get; init; }

    /// <summary>
    /// The text of the template's footer placeholder, with the fields <c>{footer}</c> (the brand's footer text),
    /// <c>{title}</c> (the deck's title), <c>{chapter}</c> (the current <c>#</c> section) and <c>{author}</c>: for
    /// example <c>{footer} | {chapter}</c>. Separators left at either end by empty fields are dropped.
    /// <see langword="null"/> (the default): the brand's footer text, as before.
    /// </summary>
    public string? FooterPattern { get; init; }

    /// <summary>Where the content of slides is placed. Default <see cref="PptxContentArea.Placeholders"/>.</summary>
    public PptxContentArea ContentArea { get; init; }

    /// <summary>The fields a <see cref="FooterPattern"/> can hold.</summary>
    internal static IReadOnlyList<string> FooterFields { get; } = ["footer", "title", "chapter", "author"];

    /// <inheritdoc/>
    public bool Equals(PptxTemplateRoles? other) =>
        other is not null && this.Layouts == other.Layouts
        && (this.Placeholders is null ? other.Placeholders is null : other.Placeholders is not null && this.Placeholders.SequenceEqual(other.Placeholders))
        && this.TextLevels == other.TextLevels && this.FooterPattern == other.FooterPattern && this.ContentArea == other.ContentArea;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Layouts, this.Placeholders?.Count, this.TextLevels, this.FooterPattern, this.ContentArea);

    /// <summary>What is wrong with the roles, as the property and a reason; <see langword="null"/> if nothing is.</summary>
    internal (string Property, string Reason)? Problem()
    {
        if (this.Layouts is null || this.Placeholders is null)
        {
            return (this.Layouts is null ? nameof(this.Layouts) : nameof(this.Placeholders), "is missing");
        }

        if (!Enum.IsDefined(this.ContentArea))
        {
            return (nameof(this.ContentArea), $"is {(int)this.ContentArea}, which is no content area");
        }

        if (this.TextLevels is { } levels)
        {
            foreach (var (name, level) in new[] { (nameof(levels.Heading), levels.Heading), (nameof(levels.Paragraph), levels.Paragraph), (nameof(levels.Bullet), levels.Bullet) })
            {
                if (level is < 1 or > 9)
                {
                    return ($"{nameof(this.TextLevels)}.{name}", $"is {level}; outline levels are 1 to 9");
                }
            }

            if (levels.Heading > levels.Bullet)
            {
                return ($"{nameof(this.TextLevels)}.{nameof(levels.Heading)}", $"is {levels.Heading}, deeper than {nameof(levels.Bullet)} ({levels.Bullet})");
            }
        }

        if (this.FooterPattern is { } pattern && FooterField().Matches(pattern).Select(m => m.Groups[1].Value).FirstOrDefault(f => !FooterFields.Contains(f)) is { } unknown)
        {
            return (nameof(this.FooterPattern), $"has the unknown field '{{{unknown}}}'; expected {string.Join(", ", FooterFields.Select(f => $"{{{f}}}"))}");
        }

        var seen = new HashSet<(string, string)>();
        var roles = new HashSet<(string, PlaceholderRole)>();
        foreach (var (index, role) in this.Placeholders.Index())
        {
            var property = $"{nameof(this.Placeholders)}[{index}]";
            if (role is null || string.IsNullOrWhiteSpace(role.Layout) || string.IsNullOrWhiteSpace(role.Placeholder))
            {
                return (property, "needs a layout and a placeholder name");
            }

            if (!Enum.IsDefined(role.Role))
            {
                return (property, $"has the unknown role {(int)role.Role}");
            }

            if (!seen.Add((role.Layout.ToUpperInvariant(), role.Placeholder.ToUpperInvariant())))
            {
                return (property, $"gives the placeholder '{role.Placeholder}' of the layout '{role.Layout}' a second role");
            }

            if (role.Role != PlaceholderRole.Body && !roles.Add((role.Layout.ToUpperInvariant(), role.Role)))
            {
                return (property, $"gives the layout '{role.Layout}' a second placeholder with the role {role.Role}; only Body can be given several times");
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="pattern"/> with its fields filled in by <paramref name="value"/>; separators (<c>|</c>,
    /// <c>·</c>, <c>–</c>, <c>—</c>) left at the ends by empty fields are trimmed, and two left side by side
    /// become one.
    /// </summary>
    internal static string FormatFooter(string pattern, Func<string, string?> value)
    {
        var text = new StringBuilder();
        var last = 0;
        foreach (Match field in FooterField().Matches(pattern))
        {
            text.Append(pattern, last, field.Index - last);
            var filled = value(field.Groups[1].Value) ?? string.Empty;
            text.Append(filled);
            last = field.Index + field.Length;
        }

        text.Append(pattern, last, pattern.Length - last);
        var result = DoubleSeparator().Replace(text.ToString(), m => m.Groups[1].Value + m.Groups[2].Value + " ");
        return EndSeparators().Replace(result, string.Empty);
    }

    [GeneratedRegex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex FooterField();

    // Two separators with nothing but spaces between them: the second one goes.
    [GeneratedRegex(@"(\s*)([|·–—])\s*(?:[|·–—]\s*)+", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleSeparator();

    [GeneratedRegex(@"^[\s|·–—]+|[\s|·–—]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EndSeparators();
}

/// <summary>The layouts for further kinds of slide, by name as PowerPoint shows them. A role left <see langword="null"/> is found by type where OOXML has one, else not at all.</summary>
public sealed record PptxLayoutRoles
{
    /// <summary>Two bodies side by side. <see langword="null"/>: a layout of type <c>twoObj</c> or <c>twoColTx</c>.</summary>
    public string? TwoContent { get; init; }

    /// <summary>Text beside a picture. <see langword="null"/>: a layout of type <c>picTx</c>.</summary>
    public string? TextAndPicture { get; init; }

    /// <summary>One picture over the content area.</summary>
    public string? Picture { get; init; }

    /// <summary>The agenda: a numbered list of the chapters.</summary>
    public string? Agenda { get; init; }

    /// <summary>A quote with its byline.</summary>
    public string? Quote { get; init; }

    /// <summary>One sentence, large.</summary>
    public string? Statement { get; init; }

    /// <summary>Contact people: pictures, each with a text under it.</summary>
    public string? Contact { get; init; }

    /// <summary>One chart. <see langword="null"/>: a layout of type <c>chart</c>.</summary>
    public string? Chart { get; init; }

    /// <summary>Two charts side by side.</summary>
    public string? TwoCharts { get; init; }

    /// <summary>Slides on a dark background (<c>{.dark}</c>). <see langword="null"/>: a layout named <c>Title Only Dark</c>.</summary>
    public string? Dark { get; init; }
}

/// <summary>The role of one placeholder of a layout.</summary>
/// <param name="Layout">The layout's name, as PowerPoint shows it.</param>
/// <param name="Placeholder">The placeholder's shape name, as the Selection Pane shows it; case-insensitive.</param>
/// <param name="Role">What it is for.</param>
public sealed record PptxPlaceholderRole(string Layout, string Placeholder, PlaceholderRole Role);

/// <summary>What a placeholder of a layout is for.</summary>
public enum PlaceholderRole
{
    /// <summary>The line under the title. Undeclared: a placeholder of type <c>subTitle</c>.</summary>
    Subtitle,

    /// <summary>Who said a quote, or who a slide is by. Only when declared.</summary>
    Byline,

    /// <summary>The number of a chapter on its divider. Only when declared.</summary>
    ChapterNumber,

    /// <summary>Text; several of one layout in the order declared. Undeclared: <c>body</c>, <c>obj</c> or untyped, in reading order.</summary>
    Body,

    /// <summary>A picture. Undeclared: a placeholder of type <c>pic</c>.</summary>
    Picture,

    /// <summary>The title of a chart. Only when declared.</summary>
    ChartTitle,

    /// <summary>The unit of a chart's values. Only when declared.</summary>
    ChartUnit,

    /// <summary>The source of a chart's data. Only when declared.</summary>
    ChartSource,

    /// <summary>Decoration the layout draws, such as a coloured bar: never written to, not even as a body.</summary>
    Decorative,
}

/// <summary>
/// The outline levels (1 to 9, as PowerPoint's <i>Increase List Level</i> counts them) text goes to in placeholders.
/// </summary>
/// <param name="Heading">A <c>###</c> heading, set in that level's style instead of bold.</param>
/// <param name="Paragraph">A paragraph, a quote line or a code line.</param>
/// <param name="Bullet">A top-level list item; nested items go one level deeper per level, at most to 9.</param>
public sealed record PptxTextLevels(int Heading, int Paragraph, int Bullet);

/// <summary>Where the content of slides is placed.</summary>
public enum PptxContentArea
{
    /// <summary>In the layout's body placeholder, else the master's.</summary>
    Placeholders,

    /// <summary>Inside the drawing guides of the slide master: between the outermost vertical guides, and between the horizontal guides below the title and above the footer.</summary>
    Guides,
}
