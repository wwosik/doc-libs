using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The texts a generator writes itself, in one language: the titles of alerts, the heading of the footnotes and the word before a chart's source, and
/// more as generators gain them. Every property defaults to English, so a language of a host's own starts from
/// <see cref="English"/> with <c>with { … }</c> and stays complete when a later version adds a text.
/// </summary>
public sealed record GeneratedTexts
{
    /// <summary>English, the default.</summary>
    public static GeneratedTexts English { get; } = new();

    /// <summary>German.</summary>
    public static GeneratedTexts German { get; } = new()
    {
        Note = "Hinweis",
        Tip = "Tipp",
        Important = "Wichtig",
        Warning = "Warnung",
        Caution = "Vorsicht",
        Footnotes = "Anmerkungen",
        Source = "Quelle",
    };

    /// <summary>The title of <c>> [!NOTE]</c>.</summary>
    public string Note { get; init; } = "Note";

    /// <summary>The title of <c>> [!TIP]</c>.</summary>
    public string Tip { get; init; } = "Tip";

    /// <summary>The title of <c>> [!IMPORTANT]</c>.</summary>
    public string Important { get; init; } = "Important";

    /// <summary>The title of <c>> [!WARNING]</c>.</summary>
    public string Warning { get; init; } = "Warning";

    /// <summary>The title of <c>> [!CAUTION]</c>.</summary>
    public string Caution { get; init; } = "Caution";

    /// <summary>The heading above the footnotes at the end of a PDF.</summary>
    public string Footnotes { get; init; } = "Notes";

    /// <summary>The word before the source under a chart (<c>source:</c>): "Source: Annual report".</summary>
    public string Source { get; init; } = "Source";

    /// <summary>The title of an alert of the kind <paramref name="kind"/> (<c>NOTE</c>, <c>TIP</c>, …).</summary>
    internal string Alert(string kind) => kind.ToUpperInvariant() switch
    {
        "TIP" => this.Tip,
        "IMPORTANT" => this.Important,
        "WARNING" => this.Warning,
        "CAUTION" => this.Caution,
        _ => this.Note,
    };
}

/// <summary>
/// The document language: the tag from front matter (<c>lang</c>) or the options, and the texts for it. A tag is
/// matched in full, then by its language alone (<c>de-CH</c> → <c>de</c>), first among the host's texts, then English
/// and German; a tag nothing matches gets the English texts and a <see cref="NoteCodes.LanguageUnknown"/> note.
/// </summary>
internal sealed partial record DocumentLanguage(string Tag, GeneratedTexts Texts)
{
    /// <summary>The key in front matter.</summary>
    public const string Key = "lang";

    private static readonly Dictionary<string, GeneratedTexts> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = GeneratedTexts.English,
        ["de"] = GeneratedTexts.German,
    };

    /// <summary>
    /// The language of <paramref name="frontMatter"/>'s <c>lang</c>, else of <paramref name="fallback"/>, the
    /// options' language.
    /// </summary>
    /// <exception cref="DocumentConversionException">The tag is not a language tag.</exception>
    public static DocumentLanguage Resolve(
        string? frontMatter, string fallback, IReadOnlyDictionary<string, GeneratedTexts> texts, GenerationNotes notes, DocumentFormat format)
    {
        var tag = (frontMatter ?? fallback).Trim();
        if (!TagPattern().IsMatch(tag))
        {
            throw new DocumentConversionException(
                format,
                frontMatter is null
                    ? $"The language '{tag}' is not a language tag such as 'en', 'de' or 'de-CH'."
                    : $"The front matter key 'lang' has the value '{tag}'; expected a language tag such as 'en', 'de' or 'de-CH'.");
        }

        var language = tag.Split('-')[0];
        var host = new Dictionary<string, GeneratedTexts>(texts, StringComparer.OrdinalIgnoreCase);
        var found = host.GetValueOrDefault(tag) ?? host.GetValueOrDefault(language) ?? BuiltIn.GetValueOrDefault(tag) ?? BuiltIn.GetValueOrDefault(language);
        if (found is null)
        {
            notes.Add(
                NoteCodes.LanguageUnknown,
                0,
                $"The language '{tag}' has no texts of its own, so the texts the generator writes itself are in English. Use 'en' or 'de'.");
        }

        return new DocumentLanguage(tag, found ?? GeneratedTexts.English);
    }

    // BCP 47 in the shape documents use: a language of two or three letters, then subtags such as a region or script.
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
