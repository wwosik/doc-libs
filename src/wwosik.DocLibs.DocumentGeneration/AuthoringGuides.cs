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
    /// <param name="keys">The keys besides <c>title</c>, <c>author</c> and <c>template</c>, with their values and meaning.</param>
    public static string FrontMatter(string templates, IEnumerable<(string Key, string Meaning)> keys)
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

            """.ReplaceLineEndings("\n"));
        text.Append(CultureInvariant($"| `template` | The design: {templates.Replace("'", "`", StringComparison.Ordinal)}. |\n"));
        foreach (var (key, meaning) in keys)
        {
            text.Append(CultureInvariant($"| `{key}` | {meaning} |\n"));
        }

        text.Append("\nAn unknown key, a key given twice or an invalid value is an error that lists the valid keys.\n");
        return text.ToString();
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
