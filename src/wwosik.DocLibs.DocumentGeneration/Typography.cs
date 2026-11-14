using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// How the generators set text in one language when typography is on (front matter <c>typography: true</c>, or the
/// options' <c>Typography</c>): which quotes straight quotes become, what a spaced hyphen becomes, and where a no-break
/// space keeps words together. Only characters are replaced: a space is never added or removed, so "10%" stays without
/// one and "10 %" gets a no-break space. Every property defaults to English, so a language of a host's own starts from
/// <see cref="English"/> with <c>with { … }</c>.
/// </summary>
public sealed record TypographyRules
{
    // Before English and German, which use them: static fields are set in the order they are written.
    private static readonly IReadOnlyList<string> DefaultUnits =
    [
        "%", "‰", "°C", "°F", "°", "€", "EUR", "USD", "CHF", "$", "£",
        "km", "m", "cm", "mm", "m²", "m³", "km²", "kg", "g", "t", "l", "ml",
        "h", "min", "s", "ms", "kB", "MB", "GB", "TB", "px", "pt",
    ];

    private static readonly IReadOnlyList<string> EnglishMonths =
    [
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        .. GeneratedTexts.English.MonthNames,
    ];

    private static readonly IReadOnlyList<string> GermanMonths =
    [
        "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
        .. GeneratedTexts.German.MonthNames,
    ];

    /// <summary>English, the default: “double” and ‘single’ quotes, the apostrophe ’, a spaced en dash.</summary>
    public static TypographyRules English { get; } = new();

    /// <summary>
    /// German: „double“ and ‚single‘ quotes, the apostrophe ’, a spaced en dash, no-break spaces inside abbreviations
    /// such as <c>z. B.</c> and after a day before its month (<c>13. November</c>).
    /// </summary>
    public static TypographyRules German { get; } = new()
    {
        DoubleQuotes = ("„", "“"),
        SingleQuotes = ("‚", "‘"),
        Abbreviations = ["z. B.", "d. h.", "u. a.", "i. d. R.", "o. Ä.", "u. U.", "v. a.", "S.", "Nr.", "Abs.", "§"],
        MonthNames = GermanMonths,
    };

    /// <summary>What a straight double quote becomes: opening (after a space, a bracket or a dash) and closing. English “ ”.</summary>
    public (string Opening, string Closing) DoubleQuotes { get; init; } = ("“", "”");

    /// <summary>What a straight single quote becomes when it is a quote, not an apostrophe. English ‘ ’.</summary>
    public (string Opening, string Closing) SingleQuotes { get; init; } = ("‘", "’");

    /// <summary>What a straight single quote becomes between letters or after a digit (<c>it's</c>, <c>'90s</c>, <c>5'</c>): ’.</summary>
    public string Apostrophe { get; init; } = "’";

    /// <summary>What a hyphen with a space on either side becomes, the spaces kept: the en dash –.</summary>
    public string Dash { get; init; } = "–";

    /// <summary>
    /// Abbreviations whose inner spaces become no-break spaces (<c>z. B.</c>); a space between one and a number after it
    /// (<c>S. 12</c>, <c>§ 3</c>) does too. Matched case-sensitively, not inside a word. English: none.
    /// </summary>
    public IReadOnlyList<string> Abbreviations { get; init; } = [];

    /// <summary>Units and currencies after a number: the space between them becomes a no-break space (<c>10 km</c>, <c>5 %</c>).</summary>
    public IReadOnlyList<string> Units { get; init; } = DefaultUnits;

    /// <summary>
    /// Month names, long and short: the space between a day and its month becomes a no-break space (<c>13 November</c>,
    /// <c>13. Nov.</c>). English and German have their month names and those of <see cref="GeneratedTexts.MonthNames"/>.
    /// </summary>
    public IReadOnlyList<string> MonthNames { get; init; } = EnglishMonths;
}

/// <summary>
/// The typography pass (<see cref="TypographyRules"/>): over the runs of a paragraph, heading, list item, table cell or
/// footnote, after the coloured words and icons. It works on the paragraph's text as one string with code masked, so a
/// quote that opens before <c>**bold**</c> and closes after it pairs, and never changes code, web and e-mail addresses
/// or HTML shown as text. A replacement the document's fonts lack is not made: the typed character stays, with one
/// <see cref="NoteCodes.TypographySkipped"/> note per character.
/// </summary>
internal sealed partial class Typography(TypographyRules rules, Func<int, bool> supported, GenerationNotes notes)
{
    // What masked text (code, addresses, HTML) is replaced by in the working string: no rule matches it.
    private const char Masked = '\u0001';

    private readonly HashSet<char> skipped = [];

    private Regex? abbreviations;
    private Regex? units;
    private Regex? dates;

    /// <summary>The rules this pass applies.</summary>
    public TypographyRules Rules => rules;

    /// <summary>
    /// The pass for a document, or <see langword="null"/> when typography is off. The rules of
    /// <paramref name="languageTag"/> are matched in full, then by language (<c>de-CH</c> → <c>de</c>), first among the
    /// host's (<paramref name="host"/>), then English and German; any other language gets the English rules.
    /// <paramref name="supported"/> says whether every face the text can be set in has a character; notes go to
    /// <paramref name="notes"/>, and <paramref name="on"/> false gives no pass.
    /// </summary>
    public static Typography? For(
        bool on, string languageTag, IReadOnlyDictionary<string, TypographyRules> host, Func<int, bool> supported, GenerationNotes notes)
    {
        if (!on)
        {
            return null;
        }

        var language = languageTag.Split('-')[0];
        var own = new Dictionary<string, TypographyRules>(host, StringComparer.OrdinalIgnoreCase);
        var rules = own.GetValueOrDefault(languageTag) ?? own.GetValueOrDefault(language)
            ?? (language.Equals("de", StringComparison.OrdinalIgnoreCase) ? TypographyRules.German : TypographyRules.English);
        return new Typography(rules, supported, notes);
    }

    /// <summary>Whether front matter's <c>typography</c> (<c>true</c> or <c>false</c>), else the options' value, switches it on.</summary>
    public static bool IsOn(FrontMatter frontMatter, bool option) =>
        frontMatter.Value(FrontMatter.TypographyKey) is { } value ? frontMatter.Bool(FrontMatter.TypographyKey, value) : option;

    /// <summary><paramref name="text"/>, such as a front-matter title, typeset as one run.</summary>
    public string Apply(string text, int line) =>
        this.Apply([text], t => t, _ => false, (_, typeset) => typeset, line)[0];

    /// <summary>
    /// <paramref name="runs"/> with their text (<paramref name="text"/>) typeset: as many runs, in the same order, each
    /// changed one made by <paramref name="withText"/>. A run <paramref name="isCode"/> says is code (or otherwise not
    /// text, such as a footnote reference) is left alone; <paramref name="line"/> is the paragraph's line, for notes.
    /// </summary>
    public List<T> Apply<T>(IReadOnlyList<T> runs, Func<T, string> text, Func<T, bool> isCode, Func<T, string, T> withText, int line)
    {
        var texts = runs.Select(text).ToList();
        var working = new StringBuilder();
        var owners = new List<int>();
        for (var i = 0; i < runs.Count; i++)
        {
            var code = isCode(runs[i]);
            foreach (var c in texts[i])
            {
                working.Append(code ? Masked : c);
                owners.Add(i);
            }
        }

        if (working.Length == 0)
        {
            return [.. runs];
        }

        // Web and e-mail addresses and HTML shown as text keep what was typed.
        var chars = working.ToString().ToCharArray();
        foreach (Match match in NotText().Matches(working.ToString()))
        {
            Array.Fill(chars, Masked, match.Index, match.Length);
        }

        var pass = new Pass(new string(chars), owners, this, line);
        pass.Dashes();
        pass.Ellipses();
        pass.Quotes();
        pass.NoBreakSpaces(this.Abbreviations(), this.Units(), this.Dates());
        if (!pass.Changed)
        {
            return [.. runs];
        }

        var result = new List<T>(runs.Count);
        var position = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var content = texts[i];
            var start = position;
            position += content.Length;
            var piece = pass.Piece(content, start);
            result.Add(piece == content ? runs[i] : withText(runs[i], piece));
        }

        return result;
    }

    // Whether the fonts have every character of a replacement; a missing one is reported once per document.
    private bool Supported(string replacement, int line)
    {
        foreach (var c in replacement)
        {
            if (!supported(c))
            {
                if (this.skipped.Add(c))
                {
                    notes.Add(
                        NoteCodes.TypographySkipped,
                        line,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The document's fonts lack '{c}' (U+{(int)c:X4}), so typography keeps the character as typed wherever it would use it."));
                }

                return false;
            }
        }

        return true;
    }

    private Regex? Abbreviations() => rules.Abbreviations.Count == 0 ? null : this.abbreviations ??= new Regex(
        @"(?<![\p{L}\p{N}])(?:" + Alternatives(rules.Abbreviations) + ")",
        RegexOptions.CultureInvariant);

    private Regex? Units() => rules.Units.Count == 0 ? null : this.units ??= new Regex(
        @"(?<=\p{Nd}) (?:" + Alternatives(rules.Units) + @")(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant);

    private Regex? Dates() => rules.MonthNames.Count == 0 ? null : this.dates ??= new Regex(
        @"(?<!\p{Nd})\p{Nd}{1,2}\.? (?:" + Alternatives(rules.MonthNames) + @")(?!\p{L})",
        RegexOptions.CultureInvariant);

    // Longest first, so "i. d. R." wins over a shorter abbreviation it starts with; one ending in a letter or digit not inside a word.
    private static string Alternatives(IEnumerable<string> words) => string.Join(
        "|",
        words.Where(w => w.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(w => w.Length)
            .Select(w => Regex.Escape(w) + (char.IsLetterOrDigit(w[^1]) ? @"(?![\p{L}\p{N}])" : string.Empty)));

    // Addresses (scheme://…, www.…, e-mail) and HTML tags or comments shown as text.
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://\S+|\bwww\.\S+|[^\s@]+@[^\s@]+\.[^\s@]+|<!--.*?-->|</?[A-Za-z][^<>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex NotText();

    /// <summary>One paragraph's replacements: at most one per character of the working string.</summary>
    private sealed class Pass(string text, List<int> owners, Typography typography, int line)
    {
        private readonly string?[] replacements = new string?[text.Length];
        private readonly bool[] removed = new bool[text.Length];
        private readonly bool[] opening = new bool[text.Length];

        public bool Changed { get; private set; }

        private TypographyRules Rules => typography.Rules;

        /// <summary><c>---</c> → em dash, <c>--</c> → en dash, a spaced hyphen → the rules' dash; never between digits or in arrows.</summary>
        public void Dashes()
        {
            for (var i = 0; i < text.Length;)
            {
                if (text[i] != '-')
                {
                    i++;
                    continue;
                }

                var end = i;
                while (end < text.Length && text[end] == '-')
                {
                    end++;
                }

                var length = end - i;
                var before = i > 0 ? text[i - 1] : ' ';
                var after = end < text.Length ? text[end] : ' ';
                if (length is 2 or 3 && !(char.IsDigit(before) && char.IsDigit(after)) && before != '<' && after != '>')
                {
                    this.Set(i, length == 3 ? "—" : "–", length);
                }
                else if (length == 1 && i > 0 && before == ' ' && end < text.Length && after == ' '
                    && !(i >= 2 && char.IsDigit(text[i - 2]) && end + 1 < text.Length && char.IsDigit(text[end + 1])))
                {
                    this.Set(i, this.Rules.Dash, 1);
                }

                i = end;
            }
        }

        /// <summary>Exactly three dots → the ellipsis.</summary>
        public void Ellipses()
        {
            for (var i = 0; i < text.Length;)
            {
                if (text[i] != '.')
                {
                    i++;
                    continue;
                }

                var end = i;
                while (end < text.Length && text[end] == '.')
                {
                    end++;
                }

                if (end - i == 3)
                {
                    this.Set(i, "…", 3);
                }

                i = end;
            }
        }

        /// <summary>
        /// A quote after the start, a space, an opening bracket, a dash or an opening quote opens; otherwise it closes. A
        /// single quote between letters, after a digit or before a digit at the start of a word is the apostrophe, and so
        /// is one after a letter while no single quote is open. A double quote after a digit (inches) stays.
        /// </summary>
        public void Quotes()
        {
            var singleOpen = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c is not ('"' or '\''))
                {
                    continue;
                }

                var before = i > 0 ? text[i - 1] : ' ';
                var after = i + 1 < text.Length ? text[i + 1] : ' ';
                var opens = i == 0 || char.IsWhiteSpace(before) || "([{-–—/".Contains(before, StringComparison.Ordinal) || this.opening[i - 1];
                if (c == '"')
                {
                    if (char.IsDigit(before) || (opens && char.IsWhiteSpace(after)))
                    {
                        continue;
                    }

                    this.opening[i] = opens;
                    this.Set(i, opens ? this.Rules.DoubleQuotes.Opening : this.Rules.DoubleQuotes.Closing, 1);
                }
                else if ((char.IsLetterOrDigit(before) && char.IsLetter(after)) || char.IsDigit(before) || (opens && char.IsDigit(after)))
                {
                    this.Set(i, this.Rules.Apostrophe, 1);
                }
                else if (opens)
                {
                    if (!char.IsWhiteSpace(after))
                    {
                        this.opening[i] = true;
                        singleOpen = true;
                        this.Set(i, this.Rules.SingleQuotes.Opening, 1);
                    }
                }
                else if (singleOpen)
                {
                    singleOpen = false;
                    this.Set(i, this.Rules.SingleQuotes.Closing, 1);
                }
                else
                {
                    this.Set(i, this.Rules.Apostrophe, 1);
                }
            }
        }

        /// <summary>The spaces inside abbreviations, between a number and its unit, and between a day and its month.</summary>
        public void NoBreakSpaces(Regex? abbreviations, Regex? units, Regex? dates)
        {
            foreach (Match match in abbreviations?.Matches(text) ?? Enumerable.Empty<Match>())
            {
                for (var k = match.Index; k < match.Index + match.Length; k++)
                {
                    this.Space(k);
                }

                var end = match.Index + match.Length;
                if (end + 1 < text.Length && char.IsDigit(text[end + 1]))
                {
                    this.Space(end);
                }
            }

            foreach (Match match in units?.Matches(text) ?? Enumerable.Empty<Match>())
            {
                this.Space(match.Index);
            }

            foreach (Match match in dates?.Matches(text) ?? Enumerable.Empty<Match>())
            {
                this.Space(match.Value.IndexOf(' ', StringComparison.Ordinal) + match.Index);
            }
        }

        /// <summary>The run's text from <paramref name="start"/> in the working string, with the replacements made.</summary>
        public string Piece(string content, int start)
        {
            var piece = new StringBuilder(content.Length);
            for (var k = 0; k < content.Length; k++)
            {
                if (!this.removed[start + k])
                {
                    piece.Append(this.replacements[start + k] ?? content[k].ToString());
                }
            }

            return piece.ToString();
        }

        private void Space(int index)
        {
            if (index >= 0 && index < text.Length && text[index] == ' ')
            {
                this.Set(index, " ", 1);
            }
        }

        // The characters from index replaced by replacement; only within one run, only where nothing was replaced, and
        // only when the fonts have it.
        private void Set(int index, string replacement, int length)
        {
            for (var k = index; k < index + length; k++)
            {
                if (this.replacements[k] is not null || this.removed[k] || owners[k] != owners[index])
                {
                    return;
                }
            }

            if (!typography.Supported(replacement, line))
            {
                return;
            }

            this.replacements[index] = replacement;
            for (var k = index + 1; k < index + length; k++)
            {
                this.removed[k] = true;
            }

            this.Changed = true;
        }
    }
}
