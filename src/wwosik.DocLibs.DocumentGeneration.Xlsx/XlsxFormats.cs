using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// The number format of a column: Excel's format code, built from the culture's <see cref="NumberFormatInfo"/> and
/// <see cref="DateTimeFormatInfo"/>, and the same value as text the way Excel shows it in that culture (for the preview and
/// for measuring). Excel writes separators in its codes as <c>,</c> and <c>.</c> and shows them in the reader's own;
/// symbols, their position, decimals and the order of a date's parts come from the culture.
/// </summary>
internal sealed class XlsxFormat
{
    /// <summary>Excel's code for text, <c>@</c>, which keeps <c>01234</c> as written.</summary>
    public const string TextCode = "@";

    private static readonly ConcurrentDictionary<string, (string Symbol, int Decimals)?> Currencies = new(StringComparer.Ordinal);
    private static readonly Lazy<IReadOnlySet<string>> KnownCurrencies = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(Region).OfType<RegionInfo>().Select(r => r.ISOCurrencySymbol).ToHashSet(StringComparer.Ordinal));

    private readonly CultureInfo culture;
    private readonly NumberFormatInfo numbers;
    private readonly string datePattern;

    private XlsxFormat(XlsxColumnType type, string? code, CultureInfo culture, NumberFormatInfo numbers, string datePattern)
    {
        this.Type = type;
        this.Code = code;
        this.culture = culture;
        this.numbers = numbers;
        this.datePattern = datePattern;
    }

    public XlsxColumnType Type { get; }

    /// <summary>Excel's format code; <see langword="null"/> for booleans (General).</summary>
    public string? Code { get; }

    /// <summary>Whether values are numbers, right-aligned in the preview.</summary>
    public bool IsNumeric => this.Type is XlsxColumnType.Number or XlsxColumnType.Integer or XlsxColumnType.Currency or XlsxColumnType.Percent;

    /// <summary>The format of a column of <paramref name="type"/> in <paramref name="culture"/>.</summary>
    public static XlsxFormat For(XlsxColumnType type, int? decimals, string? currency, CultureInfo culture)
    {
        var info = (NumberFormatInfo)culture.NumberFormat.Clone();
        var date = DatePattern(culture, type == XlsxColumnType.DateTime);
        switch (type)
        {
            case XlsxColumnType.Number:
                info.NumberDecimalDigits = decimals ?? info.NumberDecimalDigits;
                return new(type, Grouped(info.NumberDecimalDigits), culture, info, date);
            case XlsxColumnType.Integer:
                info.NumberDecimalDigits = 0;
                return new(type, Grouped(0), culture, info, date);
            case XlsxColumnType.Currency:
                var (symbol, digits) = currency is null || currency == Region(culture)?.ISOCurrencySymbol
                    ? (info.CurrencySymbol, info.CurrencyDecimalDigits)
                    : Currency(currency, culture) ?? (currency, info.CurrencyDecimalDigits);
                info.CurrencySymbol = symbol;
                info.CurrencyDecimalDigits = decimals ?? digits;
                var number = Grouped(info.CurrencyDecimalDigits);
                var positive = Pattern(CurrencyPositive[Math.Clamp(info.CurrencyPositivePattern, 0, CurrencyPositive.Length - 1)], number, symbol);
                var negative = Pattern(CurrencyNegative[Math.Clamp(info.CurrencyNegativePattern, 0, CurrencyNegative.Length - 1)], number, symbol);
                return new(type, positive + ";" + negative, culture, info, date);
            case XlsxColumnType.Percent:
                info.PercentDecimalDigits = decimals ?? info.PercentDecimalDigits;
                var plain = Plain(info.PercentDecimalDigits);
                return new(
                    type,
                    Pattern(PercentPositive[Math.Clamp(info.PercentPositivePattern, 0, PercentPositive.Length - 1)], plain, "%")
                        + ";" + Pattern(PercentNegative[Math.Clamp(info.PercentNegativePattern, 0, PercentNegative.Length - 1)], plain, "%"),
                    culture,
                    info,
                    date);
            case XlsxColumnType.Date:
            case XlsxColumnType.DateTime:
                return new(type, ExcelDate(date), culture, info, date);
            case XlsxColumnType.Boolean:
                return new(type, null, culture, info, date);
            default:
                return new(type, TextCode, culture, info, date);
        }
    }

    /// <summary>Whether <paramref name="code"/> is an ISO 4217 code of a currency some country uses.</summary>
    public static bool IsKnownCurrency(string code)
    {
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        // Without culture data (invariant globalization), any three capitals pass.
        return KnownCurrencies.Value.Count == 0 || KnownCurrencies.Value.Contains(code);
    }

    /// <summary>The culture of <paramref name="tag"/>: a neutral language as its main country; the invariant culture for a tag .NET does not know.</summary>
    public static CultureInfo Culture(string tag)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            return culture.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(tag) : culture;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary><paramref name="cell"/> as Excel shows it in this format; a value of another kind invariantly.</summary>
    public string Display(XlsxCell cell)
    {
        switch (cell.Kind)
        {
            case XlsxCellKind.Number when this.Type is XlsxColumnType.Number or XlsxColumnType.Integer:
                return cell.Number.ToString("N", this.numbers);
            case XlsxCellKind.Number when this.Type == XlsxColumnType.Currency:
                return cell.Number.ToString("C", this.numbers);
            case XlsxCellKind.Number when this.Type == XlsxColumnType.Percent:
                return cell.Number.ToString("P", this.numbers);
            case XlsxCellKind.Date or XlsxCellKind.DateTime when this.Type is XlsxColumnType.Date or XlsxColumnType.DateTime:
                return cell.DateTime.ToString(this.datePattern, this.culture);
            case XlsxCellKind.Boolean:
                return cell.Boolean ? "TRUE" : "FALSE";
            default:
                return cell.ToString();
        }
    }

    // The positive and negative patterns of NumberFormatInfo: $ the symbol, n the number, - the sign.
    private static readonly string[] CurrencyPositive = ["$n", "n$", "$ n", "n $"];
    private static readonly string[] CurrencyNegative =
        ["($n)", "-$n", "$-n", "$n-", "(n$)", "-n$", "n-$", "n$-", "-n $", "-$ n", "n $-", "$ n-", "$ -n", "n- $", "($ n)", "(n $)", "$- n"];
    private static readonly string[] PercentPositive = ["n %", "n%", "%n", "% n"];
    private static readonly string[] PercentNegative = ["-n %", "-n%", "-%n", "%-n", "%n-", "n-%", "n%-", "-% n", "n %-", "% n-", "% -n", "n- %"];

    private static string Grouped(int decimals) => "#,##0" + (decimals > 0 ? "." + new string('0', decimals) : string.Empty);

    private static string Plain(int decimals) => "0" + (decimals > 0 ? "." + new string('0', decimals) : string.Empty);

    // A .NET pattern in Excel's terms: the number code, the symbol quoted (% stays Excel's percent), spaces escaped.
    private static string Pattern(string pattern, string number, string symbol)
    {
        var code = new StringBuilder();
        foreach (var c in pattern)
        {
            code.Append(c switch
            {
                'n' => number,
                '$' => "\"" + symbol.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"",
                ' ' => "\\ ",
                _ => c.ToString(),
            });
        }

        return code.ToString();
    }

    private static string DatePattern(CultureInfo culture, bool withTime) =>
        withTime ? culture.DateTimeFormat.ShortDatePattern + " " + culture.DateTimeFormat.ShortTimePattern : culture.DateTimeFormat.ShortDatePattern;

    // A .NET date pattern in Excel's terms: every literal escaped, so that Excel shows it as the culture writes it.
    internal static string ExcelDate(string pattern)
    {
        var code = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c is '\'' or '"')
            {
                var end = pattern.IndexOf(c, i + 1);
                var text = end < 0 ? pattern[(i + 1)..] : pattern[(i + 1)..end];
                if (text.Length > 0)
                {
                    code.Append('"').Append(text.Replace("\"", string.Empty, StringComparison.Ordinal)).Append('"');
                }

                i = end < 0 ? pattern.Length : end;
                continue;
            }

            if (c == '\\')
            {
                if (i + 1 < pattern.Length)
                {
                    code.Append('\\').Append(pattern[++i]);
                }

                continue;
            }

            var run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c)
            {
                run++;
            }

            var token = c switch
            {
                'd' => run switch { 1 => "d", 2 => "dd", 3 => "ddd", _ => "dddd" },
                'M' => run switch { 1 => "m", 2 => "mm", 3 => "mmm", _ => "mmmm" },
                'y' => run <= 2 ? "yy" : "yyyy",
                'h' or 'H' => run == 1 ? "h" : "hh",
                'm' => run == 1 ? "m" : "mm",
                's' => run == 1 ? "s" : "ss",
                't' => run == 1 ? "A/P" : "AM/PM",
                'f' or 'F' or 'z' or 'K' or 'g' => string.Empty,
                _ => null,
            };
            if (token is null)
            {
                foreach (var literal in pattern.AsSpan(i, run))
                {
                    code.Append('\\').Append(literal);
                }
            }
            else
            {
                code.Append(token);
            }

            i += run - 1;
        }

        return code.ToString().Trim();
    }

    // The symbol and decimals of a currency of another country: from a culture of that country, the culture's own language
    // first, then by name, so that the choice is the same on every run.
    private static (string Symbol, int Decimals)? Currency(string code, CultureInfo culture) =>
        Currencies.GetOrAdd(code + "|" + culture.TwoLetterISOLanguageName, _ =>
        {
            var users = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Where(c => Region(c)?.ISOCurrencySymbol == code)
                .OrderBy(c => c.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName ? 0 : c.TwoLetterISOLanguageName == "en" ? 1 : 2)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
            return users.Count == 0 ? null : (users[0].NumberFormat.CurrencySymbol, users[0].NumberFormat.CurrencyDecimalDigits);
        });

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
}
