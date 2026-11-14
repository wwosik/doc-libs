using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The texts a generator writes itself, in one language: the titles of alerts, the heading of the footnotes, the word before a chart's source, the
/// month names and date formats of timelines and Gantt charts, the axis titles of risk matrices, the heading of a table of contents, the labels of captions, the classification marking, and more as generators gain them. Every property defaults to English, so a language of a host's own starts from
/// <see cref="English"/> with <c>with { … }</c> and stays complete when a later version adds a text.
/// </summary>
public sealed record GeneratedTexts
{
    // Before English and German, which use them: static fields are set in the order they are written.
    private static readonly IReadOnlyList<string> EnglishMonths = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly IReadOnlyList<string> GermanMonths = ["Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez."];

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
        MonthNames = GermanMonths,
        DayFormat = "{0}. {1} {2}",
        WeekFormat = "KW {0}",
        Likelihood = "Eintrittswahrscheinlichkeit",
        Impact = "Auswirkung",
        Contents = "Inhalt",
        Figure = "Abbildung",
        Table = "Tabelle",
        Internal = "Intern",
        Confidential = "Vertraulich",
        Draft = "Entwurf",
        Chart = "Diagramm",
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

    /// <summary>
    /// Unused for now: PDFs used to list their footnotes at the end under this heading, and print them at the foot of
    /// their page since E03-S03. Kept so that code setting it still compiles.
    /// </summary>
    public string Footnotes { get; init; } = "Notes";

    /// <summary>The word before the source under a chart (<c>source:</c>): "Source: Annual report".</summary>
    public string Source { get; init; } = "Source";

    /// <summary>
    /// The twelve month names, January first, as timelines and Gantt charts show dates: short, such as "Mar" or
    /// "März". A list of another length is an error when a date is shown.
    /// </summary>
    public IReadOnlyList<string> MonthNames { get; init; } = EnglishMonths;

    /// <summary>A day: <c>{0}</c> the day of the month, <c>{1}</c> the month name, <c>{2}</c> the year. "15 Mar 2026".</summary>
    public string DayFormat { get; init; } = "{0} {1} {2}";

    /// <summary>A month: <c>{0}</c> the month name, <c>{1}</c> the year. "Mar 2026".</summary>
    public string MonthFormat { get; init; } = "{0} {1}";

    /// <summary>A quarter: <c>{0}</c> its number from 1 to 4. "Q2"; a timeline adds the year after a space.</summary>
    public string QuarterFormat { get; init; } = "Q{0}";

    /// <summary>An ISO week on a Gantt chart's axis: <c>{0}</c> its number. "W12"; German "KW 12".</summary>
    public string WeekFormat { get; init; } = "W{0}";

    /// <summary>The title of a risk matrix's vertical axis: "Likelihood"; German "Eintrittswahrscheinlichkeit".</summary>
    public string Likelihood { get; init; } = "Likelihood";

    /// <summary>The title of a risk matrix's horizontal axis: "Impact"; German "Auswirkung".</summary>
    public string Impact { get; init; } = "Impact";

    /// <summary>The heading above a table of contents: "Contents"; German "Inhalt".</summary>
    public string Contents { get; init; } = "Contents";

    /// <summary>The label of a figure's caption, before its number: "Figure 3: …"; German "Abbildung".</summary>
    public string Figure { get; init; } = "Figure";

    /// <summary>The label of a table's caption, before its number: "Table 2: …"; German "Tabelle".</summary>
    public string Table { get; init; } = "Table";

    /// <summary>The alt text of a chart without a title, in Word documents: "Chart"; German "Diagramm".</summary>
    public string Chart { get; init; } = "Chart";

    /// <summary>The marking of an internal document (<c>classification: internal</c>): "Internal"; German "Intern".</summary>
    public string Internal { get; init; } = "Internal";

    /// <summary>The marking of a confidential document (<c>classification: confidential</c>): "Confidential"; German "Vertraulich".</summary>
    public string Confidential { get; init; } = "Confidential";

    /// <summary>The marking of a draft (<c>draft: true</c>), and the text of its watermark: "Draft"; German "Entwurf".</summary>
    public string Draft { get; init; } = "Draft";

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
