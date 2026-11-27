using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>What a value written in Markdown reads as.</summary>
internal enum ValueKind
{
    Text,
    Number,
    Currency,
    Percent,
    Date,
    DateTime,
    Boolean,
}

/// <summary>A value written in Markdown, read in the document's culture.</summary>
/// <param name="Kind">What it reads as.</param>
/// <param name="Number">The number: a percentage as its fraction (<c>25 %</c> is 0.25).</param>
/// <param name="DateTime">The date, or the date and time.</param>
/// <param name="Boolean">The truth value.</param>
/// <param name="Currency">The ISO 4217 code of an amount.</param>
/// <param name="Decimals">The digits written after the decimal mark.</param>
internal readonly record struct ParsedValue(ValueKind Kind, double Number = 0, DateTime DateTime = default, bool Boolean = false, string? Currency = null, int Decimals = 0)
{
    public bool Whole => this.Kind == ValueKind.Number && this.Decimals == 0;
}

/// <summary>
/// Reads the values of a Markdown table in a culture: numbers with the culture's decimal mark and thousands separator in
/// groups of three, amounts with the culture's currency symbol or an ISO code, percentages, the culture's short dates and
/// ISO dates, dates with times, and truth values in the document's language and in English. A number with a leading zero
/// or more than 15 digits is text, as is a number written in another culture's way (<c>1,5</c> in English).
/// </summary>
internal sealed partial class XlsxValues
{
    // Symbols that name one currency wherever they are written; the culture's own symbol comes first.
    private static readonly (string Symbol, string Code)[] Symbols = [("€", "EUR"), ("$", "USD"), ("£", "GBP"), ("¥", "JPY"), ("₹", "INR"), ("₩", "KRW"), ("₺", "TRY"), ("zł", "PLN")];

    private static readonly Dictionary<string, (string[] True, string[] False)> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = (["true", "yes"], ["false", "no"]),
        ["de"] = (["wahr", "ja"], ["falsch", "nein"]),
        ["fr"] = (["vrai", "oui"], ["faux", "non"]),
        ["es"] = (["verdadero", "sí"], ["falso", "no"]),
        ["it"] = (["vero", "sì"], ["falso", "no"]),
        ["nl"] = (["waar", "ja"], ["onwaar", "nee"]),
        ["pl"] = (["prawda", "tak"], ["fałsz", "nie"]),
    };

    private readonly CultureInfo culture;
    private readonly Regex number;
    private readonly string decimalMark;
    private readonly string? cultureCurrency;
    private readonly string[] dates;
    private readonly string[] dateTimes;
    private readonly HashSet<string> trueWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> falseWords = new(StringComparer.OrdinalIgnoreCase);

    public XlsxValues(CultureInfo culture, string languageTag)
    {
        this.culture = culture;
        var info = culture.NumberFormat;
        this.decimalMark = info.NumberDecimalSeparator;
        var group = info.NumberGroupSeparator;
        var groupPattern = group.Length == 0 ? "," : group.All(c => char.IsWhiteSpace(c) || c is ' ' or ' ') ? "[   ]" : Regex.Escape(group);
        this.number = new Regex(
            $@"^(?<sign>[-+−])?(?<int>\d{{1,3}}(?:{groupPattern}\d{{3}})+|\d+)(?:{Regex.Escape(this.decimalMark)}(?<frac>\d+))?$",
            RegexOptions.CultureInvariant);
        this.cultureCurrency = Region(culture)?.ISOCurrencySymbol;

        var shortDate = culture.DateTimeFormat.ShortDatePattern;
        var relaxed = shortDate.Replace("dd", "d", StringComparison.Ordinal).Replace("MM", "M", StringComparison.Ordinal);
        this.dates = [.. new[] { shortDate, relaxed, "yyyy-MM-dd" }.Where(p => p.Contains("yyyy", StringComparison.Ordinal)).Distinct()];
        var times = new[] { culture.DateTimeFormat.ShortTimePattern, culture.DateTimeFormat.LongTimePattern, "HH:mm", "HH:mm:ss", "H:mm" }.Distinct().ToList();
        this.dateTimes = [.. this.dates.SelectMany(d => times.Select(t => d + " " + t)), "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

        foreach (var language in new[] { "en", languageTag.Split('-')[0] }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Words.TryGetValue(language, out var words))
            {
                this.trueWords.UnionWith(words.True);
                this.falseWords.UnionWith(words.False);
            }
        }
    }

    /// <summary>The culture's own currency, or <see langword="null"/> without a country.</summary>
    public string? CultureCurrency => this.cultureCurrency;

    /// <summary>What <paramref name="text"/> reads as; <see cref="ValueKind.Text"/> when it is nothing else.</summary>
    public ParsedValue Parse(string text)
    {
        var value = text.Trim();
        if (value.Length == 0)
        {
            return new(ValueKind.Text);
        }

        if (this.trueWords.Contains(value) || this.falseWords.Contains(value))
        {
            return new(ValueKind.Boolean, Boolean: this.trueWords.Contains(value));
        }

        if (this.Number(value) is { } plain)
        {
            return plain;
        }

        if (this.Percent(value) is { } percent)
        {
            return percent;
        }

        if (this.Amount(value) is { } amount)
        {
            return amount;
        }

        if (DateTime.TryParseExact(value, this.dates, this.culture, DateTimeStyles.None, out var date))
        {
            return new(ValueKind.Date, DateTime: date);
        }

        if (DateTime.TryParseExact(value, this.dateTimes, this.culture, DateTimeStyles.None, out var dateTime))
        {
            return new(ValueKind.DateTime, DateTime: dateTime);
        }

        return new(ValueKind.Text);
    }

    /// <summary>
    /// <paramref name="text"/> as a cell of a column of <paramref name="type"/> (and <paramref name="currency"/>, a
    /// currency column's code; <see langword="null"/>: the culture's); <see langword="null"/> when it does not fit. In a
    /// percent column a bare number is the fraction; in a currency column it is an amount.
    /// </summary>
    public XlsxCell? As(string text, XlsxColumnType type, string? currency)
    {
        if (type == XlsxColumnType.Text)
        {
            return XlsxCell.FromText(text);
        }

        var value = this.Parse(text);
        return (type, value.Kind) switch
        {
            (XlsxColumnType.Number, ValueKind.Number) => XlsxCell.FromNumber(value.Number),
            (XlsxColumnType.Integer, ValueKind.Number) when value.Whole => XlsxCell.FromNumber(value.Number),
            (XlsxColumnType.Currency, ValueKind.Number) => XlsxCell.FromNumber(value.Number),
            (XlsxColumnType.Currency, ValueKind.Currency) when value.Currency == (currency ?? this.cultureCurrency) => XlsxCell.FromNumber(value.Number),
            (XlsxColumnType.Percent, ValueKind.Percent or ValueKind.Number) => XlsxCell.FromNumber(value.Number),
            (XlsxColumnType.Date, ValueKind.Date) => XlsxCell.FromDate(DateOnly.FromDateTime(value.DateTime)),
            (XlsxColumnType.Date, ValueKind.DateTime) when value.DateTime.TimeOfDay == TimeSpan.Zero => XlsxCell.FromDate(DateOnly.FromDateTime(value.DateTime)),
            (XlsxColumnType.DateTime, ValueKind.Date or ValueKind.DateTime) => XlsxCell.FromDateTime(value.DateTime),
            (XlsxColumnType.Boolean, ValueKind.Boolean) => XlsxCell.FromBoolean(value.Boolean),
            _ => null,
        };
    }

    // A plain number in the culture's way.
    private ParsedValue? Number(string value)
    {
        var match = this.number.Match(value);
        if (!match.Success)
        {
            return null;
        }

        var whole = new string([.. match.Groups["int"].Value.Where(char.IsAsciiDigit)]);
        var fraction = match.Groups["frac"].Value;
        if ((whole.Length > 1 && whole[0] == '0') || whole.Length + fraction.Length > 15)
        {
            return null;
        }

        var invariant = new StringBuilder(whole);
        if (fraction.Length > 0)
        {
            invariant.Append('.').Append(fraction);
        }

        var number = double.Parse(invariant.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return new(ValueKind.Number, match.Groups["sign"].Value is "-" or "−" ? -number : number, Decimals: fraction.Length);
    }

    private ParsedValue? Percent(string value)
    {
        foreach (var symbol in new[] { "%", this.culture.NumberFormat.PercentSymbol }.Distinct())
        {
            if (value.Length > symbol.Length && value.EndsWith(symbol, StringComparison.Ordinal) && this.Number(Spaceless(value[..^symbol.Length])) is { } number)
            {
                return new(ValueKind.Percent, Math.Round(number.Number / 100, 15), Decimals: number.Decimals);
            }
        }

        return null;
    }

    // An amount: a number with a currency symbol or an ISO code before or after it, a sign before either.
    private ParsedValue? Amount(string value)
    {
        var negative = false;
        if (value[0] is '-' or '−')
        {
            negative = true;
            value = value[1..].TrimStart();
        }

        var candidates = new List<(string Symbol, string Code)>();
        if (this.cultureCurrency is { } own)
        {
            candidates.Add((this.culture.NumberFormat.CurrencySymbol, own));
        }

        candidates.AddRange(Symbols);
        if (IsoPattern().Match(value) is { Success: true } iso && XlsxFormat.IsKnownCurrency(iso.Groups["code"].Value))
        {
            candidates.Insert(0, (iso.Groups["code"].Value, iso.Groups["code"].Value));
        }

        foreach (var (symbol, code) in candidates)
        {
            string? rest = value.StartsWith(symbol, StringComparison.Ordinal) ? value[symbol.Length..]
                : value.EndsWith(symbol, StringComparison.Ordinal) ? value[..^symbol.Length]
                : null;
            if (rest is null || rest.Length == 0 || this.Number(Spaceless(rest)) is not { } number)
            {
                continue;
            }

            return new(ValueKind.Currency, negative ? -number.Number : number.Number, Currency: code, Decimals: number.Decimals);
        }

        return null;
    }

    private static string Spaceless(string text) => text.Trim().Trim(' ', ' ').Trim();

    private static RegionInfo? Region(CultureInfo culture)
    {
        try
        {
            return culture.IsNeutralCulture || culture.Name.Length == 0 ? null : new RegionInfo(culture.Name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(?<code>[A-Z]{3})(?![A-Za-z])|(?<![A-Za-z])(?<code>[A-Z]{3})$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoPattern();
}
