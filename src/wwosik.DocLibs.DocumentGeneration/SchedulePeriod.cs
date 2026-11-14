using System.Globalization;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>How precisely a <see cref="SchedulePeriod"/> was written.</summary>
internal enum PeriodKind
{
    /// <summary><c>2026</c>.</summary>
    Year,

    /// <summary><c>2026-Q2</c>.</summary>
    Quarter,

    /// <summary><c>2026-03</c>.</summary>
    Month,

    /// <summary><c>2026-03-15</c>.</summary>
    Day,
}

/// <summary>
/// A date as timelines and Gantt charts take it, in ISO form: a year, a quarter, a month or a day, each the period
/// from its first to its last day. Shown in the document's language through <see cref="GeneratedTexts"/>, without
/// <see cref="CultureInfo"/>, so hosts with invariant globalization show the same text.
/// </summary>
internal sealed partial record SchedulePeriod(DateOnly Start, DateOnly End, PeriodKind Kind)
{
    /// <summary>The accepted forms, for error messages.</summary>
    public const string Forms = "2026, 2026-Q2, 2026-03 or 2026-03-15";

    /// <summary>The period <paramref name="text"/> names; <see langword="null"/> when it is not one of <see cref="Forms"/>.</summary>
    public static SchedulePeriod? Parse(string text)
    {
        var match = Pattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        if (year < 1)
        {
            return null;
        }

        if (match.Groups["quarter"].Success)
        {
            var quarter = match.Groups["quarter"].Value[0] - '0';
            var first = new DateOnly(year, ((quarter - 1) * 3) + 1, 1);
            return new SchedulePeriod(first, first.AddMonths(3).AddDays(-1), PeriodKind.Quarter);
        }

        if (!match.Groups["month"].Success)
        {
            return new SchedulePeriod(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), PeriodKind.Year);
        }

        var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12)
        {
            return null;
        }

        if (!match.Groups["day"].Success)
        {
            var first = new DateOnly(year, month, 1);
            return new SchedulePeriod(first, first.AddMonths(1).AddDays(-1), PeriodKind.Month);
        }

        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        var date = new DateOnly(year, month, day);
        return new SchedulePeriod(date, date, PeriodKind.Day);
    }

    /// <summary>The period as the document's language shows it: "15 Mar 2026", "Mar 2026", "Q2 2026", "2026".</summary>
    /// <exception cref="ArgumentException"><see cref="GeneratedTexts.MonthNames"/> does not have twelve names.</exception>
    public string Display(GeneratedTexts texts) => this.Kind switch
    {
        PeriodKind.Day => Format(texts.DayFormat, this.Start.Day, Month(texts, this.Start.Month), this.Start.Year),
        PeriodKind.Month => Format(texts.MonthFormat, Month(texts, this.Start.Month), this.Start.Year),
        PeriodKind.Quarter => Format(texts.QuarterFormat, ((this.Start.Month - 1) / 3) + 1) + " " + Year(this.Start.Year),
        _ => Year(this.Start.Year),
    };

    /// <summary>The short name of <paramref name="month"/> (1 to 12) in the document's language.</summary>
    /// <exception cref="ArgumentException"><see cref="GeneratedTexts.MonthNames"/> does not have twelve names.</exception>
    public static string Month(GeneratedTexts texts, int month) =>
        texts.MonthNames is { Count: 12 } names
            ? names[month - 1]
            : throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"GeneratedTexts.MonthNames has {texts.MonthNames?.Count ?? 0} names; it needs twelve, January first."),
                nameof(texts));

    /// <summary>Invariant formatting: digits are never localized.</summary>
    public static string Format(string format, params object[] values) => string.Format(CultureInfo.InvariantCulture, format, values);

    private static string Year(int year) => year.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<year>[0-9]{4})(?:-(?:Q(?<quarter>[1-4])|(?<month>[0-9]{2})(?:-(?<day>[0-9]{2}))?))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
