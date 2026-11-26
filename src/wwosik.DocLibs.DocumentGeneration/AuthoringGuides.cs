using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The parts the generators' authoring guides share: their Markdown templates (embedded resources
/// <c>AuthoringGuide.*.md</c>), the front matter section, the characters to avoid in the brand's fonts, and the
/// renderers' sections. A guide is Markdown that starts at <c>##</c>, written for whoever writes the document.
/// </summary>
internal static partial class AuthoringGuides
{
    /// <summary>
    /// Symbols writers reach for, each with replacements in order of preference; the last is ASCII, which every font
    /// has. The guides name the symbols the document's fonts lack, with the first replacement they have.
    /// </summary>
    internal static readonly IReadOnlyList<(string Symbol, string[] Instead)> Symbols =
    [
        ("✓", ["[x]"]), ("✔", ["[x]"]), ("✗", ["[ ]"]), ("✘", ["[ ]"]), ("☐", ["[ ]"]), ("☑", ["[x]"]),
        ("●", ["•", "*"]), ("○", ["•", "*"]), ("■", ["•", "*"]), ("□", ["•", "*"]), ("▪", ["•", "*"]), ("◦", ["–", "-"]),
        ("★", ["*"]), ("☆", ["*"]),
        ("→", ["->"]), ("←", ["<-"]), ("↔", ["<->"]), ("↑", ["up"]), ("↓", ["down"]),
        ("▲", ["↑", "up"]), ("▼", ["↓", "down"]), ("►", ["→", "->"]), ("◄", ["←", "<-"]),
        ("⇒", ["→", "->"]), ("⇐", ["←", "<-"]), ("⇔", ["↔", "<->"]),
        ("≡", ["="]), ("≠", ["!="]), ("≈", ["~"]), ("‖", ["|"]), ("─", ["-"]), ("│", ["|"]),
    ];

    /// <summary>The template <c>AuthoringGuide.<paramref name="name"/>.md</c> embedded in <paramref name="assembly"/>.</summary>
    public static string Template(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream($"AuthoringGuide.{name}.md")
            ?? throw new InvalidOperationException($"The embedded authoring guide '{name}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// <paramref name="template"/> with every <c>{{name}}</c> replaced by its value; a placeholder that is a line of its
    /// own and gets an empty value leaves no blank lines behind.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        var filled = PlaceholderPattern().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var value)
            ? value.Trim('\n')
            : throw new InvalidOperationException($"The authoring guide has no value for '{m.Value}'."));
        return BlankLinesPattern().Replace(filled, "\n\n").Trim('\n') + "\n";
    }

    /// <summary>The front matter section: the syntax, then one table row per key.</summary>
    /// <param name="templates">The names <c>template</c> takes, as <see cref="BrandRegistry.Expected"/> lists them.</param>
    /// <param name="language">The options' document language, the default of <c>lang</c>.</param>
    /// <param name="keys">The keys besides <c>title</c>, <c>author</c>, <c>description</c>, <c>keywords</c>, <c>lang</c>, <c>typography</c>, <c>highlight</c> and <c>template</c>, with their values and meaning.</param>
    /// <param name="typography">The options' typography, the default of <c>typography</c>.</param>
    /// <param name="highlight">The options' code highlighting, the default of <c>highlight</c>.</param>
    /// <param name="audience">The version the options set, which the front matter's <c>audience</c> cannot change.</param>
    public static string FrontMatter(
        string templates, string language, IEnumerable<(string Key, string Meaning)> keys, bool typography = false, bool highlight = false, Audience? audience = null)
    {
        var text = new StringBuilder("""
            ## Front matter

            Settings go in a block at the very top, before anything else. One `key: value` per line, no
            nesting and no lists. Leave out what the defaults already do: every key is optional.

            ```markdown
            ---
            title: Network expansion 2027
            author: Planning team
            ---
            ```

            | Key | Values |
            | --- | ------ |
            | `title` | The document title in the file's properties. Default: the first `#` heading. |
            | `author` | The author in the file's properties. |
            | `description` | A description in the file's properties. |
            | `keywords` | Keywords in the file's properties, separated by commas. |

            """.ReplaceLineEndings("\n"));
        text.Append(CultureInvariant(
            $"| `lang` | The document language, such as `en`, `de` or `de-CH`: of the texts the generator writes itself, and of spell checking. Default `{language}`. |\n"));
        text.Append(CultureInvariant(
            $"| `typography` | `true` sets typographic quotes, dashes, `…` and no-break spaces (`10 km`) for the language; type straight quotes. Default `{(typography ? "true" : "false")}`. |\n"));
        text.Append(CultureInvariant(
            $"| `highlight` | `true` colours fenced code in these languages: {CodeLanguages.List()}. Default `{(highlight ? "true" : "false")}`. |\n"));
        text.Append(CultureInvariant($"| `template` | The design: {templates.Replace("'", "`", StringComparison.Ordinal)}. |\n"));
        text.Append($"| `{CoverImage.Key}`, `{CoverImage.SizeKey}`, `{CoverImage.PositionKey}` | An image on the cover, one of your image paths; `small`, `medium` or `large`; `below` or `right` of the title. |\n");
        foreach (var (key, meaning) in keys)
        {
            text.Append(CultureInvariant($"| `{key}` | {meaning} |\n"));
        }

        text.Append(audience is { } fixedVersion
            ? CultureInvariant($"| `{DocumentGeneration.FrontMatter.AudienceKey}` | No effect here: the host writes the `{fixedVersion.ToString().ToLowerInvariant()}` version. |\n")
            : $"| `{DocumentGeneration.FrontMatter.AudienceKey}` | `customer`, `internal` or `trainer`: the version, see *Versions*. Default `customer`. |\n");

        text.Append("\nAn unknown key, a key given twice or an invalid value is an error that lists the valid keys.\n");
        return text.ToString();
    }

    /// <summary>
    /// The section on versions (<see cref="Audience"/>): the containers and markers, and which version the host writes
    /// when its options set one.
    /// </summary>
    public static string Versions(Audience? audience)
    {
        var picked = audience is { } fixedVersion
            ? CultureInvariant($"here the host writes `{fixedVersion.ToString().ToLowerInvariant()}`")
            : "picked by `audience:`";
        var text = new StringBuilder(CultureInvariant($$"""
            ## Versions

            One Markdown holds three versions, {{picked}}: `customer`, `internal`, which adds what is marked
            internal, and `trainer`, which adds solutions too.

            - `::: internal` … `:::`, a line `{.internal}` before a block, or `{.internal}` on a `#` or `##` heading (the
              whole chapter or slide) mark what is internal.
            - `::: exercise` shows to everyone as "Exercise 1"; a `::: solution` right after it, as "Solution 1", to the
              trainer only.

            Versions but `customer` are marked internal unless `classification` is set.

            """).ReplaceLineEndings("\n"));
        return text.ToString();
    }

    /// <summary>
    /// The front matter rows of <c>classification</c> and <c>draft</c>, with the host's defaults: <paramref name="where"/>
    /// says where the marking shows, <paramref name="watermark"/> what else a draft gets (<see langword="null"/>: nothing).
    /// </summary>
    public static ((string Key, string Meaning) Classification, (string Key, string Meaning) Draft) MarkingKeys(
        Classification classification, bool draft, string where, string? watermark)
    {
        var name = classification.ToString().ToLowerInvariant();
        var lower = classification == DocumentGeneration.Classification.Public ? "." : "; lower values have no effect.";
        var also = watermark is null ? string.Empty : " and " + watermark;
        return (
            (DocumentGeneration.FrontMatter.ClassificationKey, $"`public`, `internal` or `confidential`: a marking {where}. Default `{name}`{lower}"),
            (DocumentGeneration.FrontMatter.DraftKey, draft
                ? "Always `true` here: the host marks a draft."
                : $"`true` adds \"Draft\" to the marking{also}. Default `false`."));
    }

    /// <summary>
    /// The characters section: the <see cref="Symbols"/> the fonts lack, with what to write instead, in a table, or
    /// nothing when they have them all.
    /// </summary>
    /// <param name="fonts">Whether the fonts of body text and tables have a character.</param>
    /// <param name="code">Whether the font of code has a character.</param>
    /// <param name="intro">The section's first paragraph: what happens to a character the fonts lack.</param>
    public static string Characters(Func<int, bool> fonts, Func<int, bool> code, string intro)
    {
        var rows = new StringBuilder();
        bool Everywhere(string text) => text.EnumerateRunes().All(r => fonts(r.Value) && code(r.Value));
        foreach (var (symbol, replacements) in Symbols)
        {
            var character = char.ConvertToUtf32(symbol, 0);
            var (inText, inCode) = (fonts(character), code(character));
            var instead = replacements.FirstOrDefault(Everywhere) ?? replacements[^1];
            if (!inText || !inCode)
            {
                var where = (inText, inCode) switch
                {
                    (false, true) => " (fine in code)",
                    (true, false) => " (in code only)",
                    _ => string.Empty,
                };
                rows.Append(CultureInvariant($"| {symbol}{where} | `{instead.Replace("|", "\\|", StringComparison.Ordinal)}` |\n"));
            }
        }

        return rows.Length == 0 ? intro + "\n" : intro + "\n\n| Avoid | Write instead |\n| ----- | ------------- |\n" + rows;
    }

    /// <summary>The sections of the renderers that have one, in order.</summary>
    public static string Renderers(IEnumerable<ICodeBlockRenderer> renderers) =>
        string.Join("\n\n", renderers.Select(r => r.AuthoringGuide?.Trim('\n')).Where(g => !string.IsNullOrEmpty(g)));

    /// <summary>Common Tabler names, for the guides; the full catalogue is on Tabler's site.</summary>
    internal static readonly IReadOnlyList<string> CommonIcons =
    [
        "check", "x", "star", "heart", "bolt", "rocket", "target", "flag", "bulb", "lock",
        "users", "calendar", "clock", "mail", "chart-bar", "trending-up", "cloud", "search", "settings", "world",
    ];

    /// <summary>
    /// The guide's section on <c>:icon-name:</c>: <paramref name="places"/> says where the format draws icons. Empty when
    /// icons are switched off; for a set other than Tabler's, its own names (the first 20).
    /// </summary>
    public static string Icons(IIconSet? set, string places)
    {
        if (set is null)
        {
            return string.Empty;
        }

        var tabler = ReferenceEquals(set, TablerIcons.Outline);
        var names = tabler ? CommonIcons : [.. set.Names.Order(StringComparer.Ordinal).Take(20)];
        var source = tabler
            ? $"of [Tabler Icons](https://tabler.io/icons) ({TablerIcons.Version}, outline)"
            : "of the icons set up for you";
        var list = tabler
            ? "Common: " + string.Join(", ", names.Select(n => $"`{n}`")) + "."
            : (set.Names.Count > names.Count ? "Among them: " : "They are: ") + string.Join(", ", names.Select(n => $"`{n}`")) + ".";
        return "## Icons\n\n"
            + $"`:icon-name:` draws the icon of that name {source}, such as `:icon-circle-check:`. {places} "
            + "An unknown name is an error.\n\n"
            + list + "\n";
    }

    /// <summary>
    /// The notes a generator of <paramref name="format"/> may give back, each with what to change in the Markdown, as a
    /// list under an introduction; leaving out <paramref name="without"/>, codes that cannot occur with the options.
    /// </summary>
    public static string Notes(DocumentConversions.DocumentFormat format, params string[] without)
    {
        string List(bool check) => string.Join("\n", NoteCodes.All
            .Where(n => n.Formats.Contains(format) && !without.Contains(n.Code) && NoteCodes.CheckOnly.Contains(n.Code) == check)
            .Select(n => $"- `{n.Code}`: {n.Advice}" + (NoteCodes.FormatAdvice.TryGetValue((n.Code, format), out var more) ? " " + more : string.Empty)));
        return "The document is still written, but whoever passes it to you may also pass on notes about it, each with its "
            + "line. Each names what happened:\n\n"
            + List(check: false)
            + "\n\nA check before generating writes nothing and returns these notes too, and also:\n\n"
            + List(check: true);
    }

    /// <summary>
    /// The <em>Limits</em> section: the limits of <paramref name="checks"/> that apply to <paramref name="format"/>, which a
    /// check holds the document to; empty when none is set.
    /// </summary>
    public static string Limits(CheckOptions checks, DocumentConversions.DocumentFormat format)
    {
        var deck = format == DocumentConversions.DocumentFormat.Pptx;
        var limits = new List<string>();
        if (deck && checks.MaxBulletsPerSlide is { } bullets)
        {
            limits.Add(CultureInvariant($"at most {bullets} bullets on a slide, counting every level"));
        }

        if (deck && checks.MaxWordsPerSlide is { } words)
        {
            limits.Add(CultureInvariant($"at most {words} words on a slide, its title and speaker notes not counted"));
        }

        if (!deck && checks.MaxWordsPerParagraph is { } paragraph)
        {
            limits.Add(CultureInvariant($"at most {paragraph} words in a paragraph"));
        }

        if (checks.MaxTitleLength is { } title)
        {
            limits.Add(CultureInvariant($"at most {title} characters in a {(deck ? "slide title" : "heading")}"));
        }

        if (checks.RequireAltText)
        {
            limits.Add("alt text for every image");
        }

        return limits.Count == 0 ? string.Empty
            : "## Limits\n\nA check before generating reports what goes beyond these: "
                + string.Join("; ", limits) + ". Keep to them from the start.";
    }

    /// <summary>How many words a guide has: runs of letters or digits.</summary>
    public static int Words(string guide) => WordPattern().Count(guide);

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);

    [GeneratedRegex(@"\{\{([A-Za-z]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLinesPattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
