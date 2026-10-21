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

    private readonly List<KeyValuePair<string, string>> entries;
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

    /// <exception cref="DocumentConversionException">The front matter is malformed.</exception>
    public static FrontMatter Read(string markdown, DocumentFormat format)
    {
        var lines = markdown.Split('\n');
        if (lines.Length < 2 || Line(lines[0]) != "---" || Line(lines[1]).Trim().Length == 0)
        {
            return new FrontMatter(markdown, [], format);
        }

        var entries = new List<KeyValuePair<string, string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = Line(lines[i]);
            if (line is "---" or "...")
            {
                return new FrontMatter(string.Join('\n', lines[(i + 1)..]), entries, format, i + 1);
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
        }

        throw new DocumentConversionException(format, "The front matter that starts on the first line is never closed by '---'.");
    }

    /// <summary>
    /// Applies the entries to <paramref name="options"/>: <c>template</c> first, through
    /// <paramref name="withTemplate"/> (which returns <see langword="null"/> for an unknown name), then every other
    /// key but <c>title</c> and <c>author</c>, in any order, through <paramref name="with"/>. A key not in
    /// <paramref name="keys"/> of the options at that point throws, listing the valid ones.
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
                || key.Equals(AuthorKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var supported = keys(options);
            var known = supported.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw this.Error(
                    $"The front matter key '{key}' is not supported by the template '{templateName(options)}'. "
                    + $"Valid keys: {string.Join(", ", new[] { TemplateKey, TitleKey, AuthorKey }.Concat(supported))}.");
            options = with(options, known, value);
        }

        return options;
    }

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

    private DocumentConversionException Error(string reason) => new(this.format, reason);

    private string? Value(string key) =>
        this.entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value).FirstOrDefault();

    private static string Line(string line) => line.TrimEnd('\r').TrimEnd();

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0] ? value[1..^1] : value;

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9.]*)\s*:(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryPattern();
}
