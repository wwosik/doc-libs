using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A <c>---</c> block of <c>key: value</c> lines at the very top of the Markdown. It looks like YAML but is only
/// that flat subset: no nesting, no lists. An opening <c>---</c> followed by a blank line is a thematic break.
/// Each generator applies the entries to its own options with <see cref="Apply"/>; mistakes throw
/// <see cref="DocumentConversionException"/> with the generator's format.
/// </summary>
internal sealed partial class FrontMatter
{
    public const string TemplateKey = "template";
    public const string TitleKey = "title";
    public const string AuthorKey = "author";

    /// <summary>The prefix of the keys that assign status labels to categories: <c>labels.success: Done; Delivered</c>.</summary>
    public const string LabelsPrefix = "labels.";

    /// <summary>The classification marking, <c>public</c>, <c>internal</c> or <c>confidential</c> (<see cref="Marking"/>).</summary>
    public const string ClassificationKey = "classification";

    /// <summary>The draft marking, <c>true</c> or <c>false</c> (<see cref="Marking"/>).</summary>
    public const string DraftKey = "draft";

    /// <summary>Typography in the document language, <c>true</c> or <c>false</c> (<see cref="Typography"/>).</summary>
    public const string TypographyKey = "typography";

    /// <summary>A table of contents, <c>true</c> or <c>false</c> (E03-S02).</summary>
    public const string TocKey = "toc";

    /// <summary>The heading levels a table of contents lists, <c>1</c>, <c>2</c> or <c>3</c>.</summary>
    public const string TocDepthKey = "tocDepth";

    /// <summary>Heading numbers, <c>true</c> or <c>false</c> (<see cref="HeadingNumbering"/>).</summary>
    public const string HeadingNumbersKey = "headingNumbers";

    /// <summary>Justified body text, <c>true</c> or <c>false</c> (E03-S03).</summary>
    public const string JustifyKey = "justify";

    /// <summary>
    /// The keys that concern documents only. A template that does not list them among its keys (a deck's) accepts them,
    /// checks their values and ignores them, so one Markdown serves every format.
    /// </summary>
    public static readonly IReadOnlyList<string> DocumentKeys = [TocKey, TocDepthKey, HeadingNumbersKey, JustifyKey];

    private readonly List<KeyValuePair<string, string>> entries;
    private readonly Dictionary<string, int> lines = new(StringComparer.OrdinalIgnoreCase);
    private readonly DocumentFormat format;

    private FrontMatter(string body, List<KeyValuePair<string, string>> entries, DocumentFormat format, int bodyLine = 0)
    {
        this.Body = body;
        this.BodyLine = bodyLine;
        this.entries = entries;
        this.format = format;
    }

    /// <summary>The Markdown without the front matter.</summary>
    public string Body { get; }

    /// <summary>How many lines the front matter takes: a line of <see cref="Body"/> is that much further down the Markdown.</summary>
    public int BodyLine { get; }

    public string? Title => this.Value(TitleKey);

    public string? Author => this.Value(AuthorKey);

    /// <summary>The document language, <c>lang</c>, as written.</summary>
    public string? Language => this.Value(DocumentLanguage.Key);

    /// <exception cref="DocumentConversionException">The front matter is malformed.</exception>
    public static FrontMatter Read(string markdown, DocumentFormat format)
    {
        var lines = markdown.Split('\n');
        if (lines.Length < 2 || Line(lines[0]) != "---" || Line(lines[1]).Trim().Length == 0)
        {
            return new FrontMatter(markdown, [], format);
        }

        var entries = new List<KeyValuePair<string, string>>();
        var keyLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = Line(lines[i]);
            if (line is "---" or "...")
            {
                var result = new FrontMatter(string.Join('\n', lines[(i + 1)..]), entries, format, i + 1);
                foreach (var (name, number) in keyLines)
                {
                    result.lines[name] = number;
                }

                return result;
            }

            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = EntryPattern().Match(line);
            if (!match.Success)
            {
                throw new DocumentConversionException(format, $"The front matter line '{line.Trim()}' is not 'key: value'.");
            }

            var key = match.Groups[1].Value;
            if (entries.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DocumentConversionException(format, $"The front matter key '{key}' appears twice.");
            }

            var value = Unquote(match.Groups[2].Value.Trim());
            if (value.Length == 0)
            {
                throw new DocumentConversionException(format, $"The front matter key '{key}' has no value.");
            }

            entries.Add(new(key, value));
            keyLines[key] = i + 1;
        }

        throw new DocumentConversionException(format, "The front matter that starts on the first line is never closed by '---'.");
    }

    /// <summary>
    /// Applies the entries to <paramref name="options"/>: <c>template</c> first, through
    /// <paramref name="withTemplate"/> (which returns <see langword="null"/> for an unknown name), then every other
    /// key but the shared ones (<c>title</c>, <c>author</c>, <c>lang</c>, <c>classification</c>, <c>draft</c>,
    /// <c>typography</c>, <c>labels.*</c>), in any order, through <paramref name="with"/>. One of <see cref="DocumentKeys"/> that is not in
    /// <paramref name="keys"/> is checked and skipped; any other key not in <paramref name="keys"/> of the options at
    /// that point throws, listing the valid ones.
    /// </summary>
    /// <param name="options">The caller's options.</param>
    /// <param name="withTemplate">The options with the named template: unchanged if it is already that template.</param>
    /// <param name="expectedTemplates">The template names for the error message, e.g. <c>'neutral'</c>.</param>
    /// <param name="templateName">The front-matter name of the options' current template.</param>
    /// <param name="keys">The keys the options support, besides <c>template</c>, <c>title</c> and <c>author</c>.</param>
    /// <param name="with">The options with a key (spelled as in <paramref name="keys"/>) set to a value.</param>
    /// <exception cref="DocumentConversionException">A key is unknown or unsupported, or a value is invalid.</exception>
    public T Apply<T>(
        T options,
        Func<T, string, T?> withTemplate,
        string expectedTemplates,
        Func<T, string> templateName,
        Func<T, IReadOnlyList<string>> keys,
        Func<T, string, string, T> with)
        where T : class
    {
        if (this.Value(TemplateKey) is { } name)
        {
            options = withTemplate(options, name) ?? throw this.Error($"The front matter key 'template' has the value '{name}'; expected {expectedTemplates}.");
        }

        foreach (var (key, value) in this.entries)
        {
            if (key.Equals(TemplateKey, StringComparison.OrdinalIgnoreCase)
                || key.Equals(TitleKey, StringComparison.OrdinalIgnoreCase)
                || key.Equals(AuthorKey, StringComparison.OrdinalIgnoreCase)
                || key.Equals(DocumentLanguage.Key, StringComparison.OrdinalIgnoreCase)
                || key.Equals(ClassificationKey, StringComparison.OrdinalIgnoreCase)
                || key.Equals(DraftKey, StringComparison.OrdinalIgnoreCase)
                || key.Equals(TypographyKey, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(LabelsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var supported = keys(options);
            if (DocumentKeys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } documentKey
                && !supported.Contains(documentKey, StringComparer.OrdinalIgnoreCase))
            {
                this.DocumentValue(documentKey, value);
                continue;
            }

            var known = supported.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw this.Error(
                    $"The front matter key '{key}' is not supported by the template '{templateName(options)}'. "
                    + $"Valid keys: {string.Join(", ", new[] { TemplateKey, TitleKey, AuthorKey, DocumentLanguage.Key, ClassificationKey, DraftKey, TypographyKey }.Concat(supported))}.");
            options = with(options, known, value);
        }

        return options;
    }

    /// <summary>
    /// The status labels the <c>labels.&lt;category&gt;</c> keys assign, each key naming one or more labels separated by
    /// <c>;</c>. Label texts are matched exactly; categories case-insensitively.
    /// </summary>
    /// <exception cref="DocumentConversionException">A category is unknown, or a label is assigned twice.</exception>
    public IReadOnlyDictionary<string, LabelCategory> Labels()
    {
        var labels = new Dictionary<string, LabelCategory>(StringComparer.Ordinal);
        foreach (var (key, value) in this.entries.Where(e => e.Key.StartsWith(LabelsPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var name = key[LabelsPrefix.Length..];
            var category = Enum.GetValues<LabelCategory>().FirstOrDefault(c => c.ToString().Equals(name, StringComparison.OrdinalIgnoreCase), (LabelCategory)(-1));
            if (category < 0)
            {
                throw this.Error(
                    $"The front matter key '{key}' names no label category. "
                    + $"Valid keys: {string.Join(", ", Enum.GetValues<LabelCategory>().Select(c => LabelsPrefix + c.ToString().ToLowerInvariant()))}.");
            }

            foreach (var label in value.Split(';').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                if (!labels.TryAdd(label, category))
                {
                    throw this.Error($"The label '{label}' is assigned to two categories in the front matter.");
                }
            }
        }

        return labels;
    }

    /// <summary>The value of one of <see cref="DocumentKeys"/>: a <see cref="bool"/>, or the depth for <c>tocDepth</c>.</summary>
    /// <exception cref="DocumentConversionException">The value is not one the key takes.</exception>
    public int DocumentValue(string key, string value) => key == TocDepthKey
        ? this.OneOf(key, value, ("1", 1), ("2", 2), ("3", 3))
        : this.Bool(key, value) ? 1 : 0;

    /// <exception cref="DocumentConversionException">The value is neither <c>true</c> nor <c>false</c>.</exception>
    public bool Bool(string key, string value) => this.OneOf(key, value, ("true", true), ("false", false));

    /// <summary>The result paired with <paramref name="value"/>, compared case-insensitively.</summary>
    /// <exception cref="DocumentConversionException">The value is none of the choices.</exception>
    public TResult OneOf<TResult>(string key, string value, params (string Value, TResult Result)[] choices)
    {
        foreach (var (choice, result) in choices)
        {
            if (choice.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }
        }

        throw this.Error(
            $"The front matter key '{key}' has the value '{value}'; expected {string.Join(" or ", choices.Select(c => $"'{c.Value}'"))}.");
    }

    /// <summary>The line of <paramref name="key"/> in the Markdown, from 1; 0 if the front matter does not have it.</summary>
    public int LineOf(string key) => this.lines.GetValueOrDefault(key);

    private DocumentConversionException Error(string reason) => new(this.format, reason);

    /// <summary>The value of a key, as written; <see langword="null"/> if the front matter does not have it.</summary>
    public string? Value(string key) =>
        this.entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value).FirstOrDefault();

    private static string Line(string line) => line.TrimEnd('\r').TrimEnd();

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0] ? value[1..^1] : value;

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9.]*)\s*:(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryPattern();
}

/// <summary>The categories of status labels on slides; each has a colour in <see cref="BrandLabelColors"/>.</summary>
internal enum LabelCategory
{
    Success,
    Info,
    Warning,
    Danger,
    Neutral,
}
