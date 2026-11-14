using System.Globalization;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>::: gantt</c>: one pipe table of 1 to 20 tasks, the first three columns by position task, start and end
/// (headers free, further columns ignored), drawn as bars on a week, month or quarter grid. A row without an end, or
/// with the same end as start, is a milestone (a diamond). A bar that ended before today is grey; a line marks today
/// when it is within the span.
/// </summary>
internal static class GanttDrawing
{
    /// <summary>The most rows.</summary>
    public const int MaxRows = 20;

    /// <summary>The width in pixels.</summary>
    public const double Width = 960;

    /// <summary>The height of a row in pixels.</summary>
    public const double RowHeight = 28;

    /// <summary>The height of the axis above the rows in pixels.</summary>
    public const double AxisHeight = 50;

    // Spans up to these lengths get the finer grid when none is asked for.
    private const int WeekGridDays = 70;
    private const int MonthGridMonths = 18;

    // More grid units than this show every second (third, …) label only.
    private const int MaxLabels = 26;

    private const double ChartLeft = 240;
    private const double ChartRight = Width - 10;
    private const double NameSize = 15;
    private const double AxisSize = 13;
    private const double BarHeight = 16;
    private const double DiamondRadius = 9;

    private sealed record Task(List<DrawingSpan> Name, bool Mono, DateOnly Start, DateOnly End, bool Milestone);

    /// <summary>Draws the container's table; <paramref name="grid"/> is <c>week</c>, <c>month</c>, <c>quarter</c> or, for one chosen from the span, <see langword="null"/>.</summary>
    /// <exception cref="DocumentConversionException">Not one table of 1 to 20 rows with valid dates.</exception>
    public static Drawing Draw(CustomContainer container, VisualContext context, string? grid)
    {
        var tasks = Tasks(container, context);
        var first = tasks.Min(t => t.Start);
        var last = tasks.Max(t => t.End);
        grid ??= Choose(first, last);
        var units = Units(first, last, grid);
        var axisStart = units[0];
        var axisEnd = Next(units[^1], grid);
        var days = (double)(axisEnd.DayNumber - axisStart.DayNumber);
        double X(DateOnly date) => ChartLeft + ((date.DayNumber - axisStart.DayNumber) / days * (ChartRight - ChartLeft));

        var colors = context.Brand.Diagram;
        var height = AxisHeight + (tasks.Count * RowHeight) + 6;
        var items = new List<DrawingItem>();

        // Every other row banded, so a bar is easy to follow to its name.
        for (var i = 1; i < tasks.Count; i += 2)
        {
            items.Add(new DrawingShape(DrawingPath.Rectangle(0, AxisHeight + (i * RowHeight), Width, RowHeight), colors.Surface, null, 0) { Kind = DrawingShapeKind.Rectangle });
        }

        // The axis: years above the grid's labels, a rule under them, a line at each unit's start.
        var step = (int)Math.Ceiling(units.Count / (double)MaxLabels);
        int? year = null;
        for (var i = 0; i < units.Count; i++)
        {
            var (left, right) = (X(units[i]), X(Next(units[i], grid)));
            var unitYear = grid == "week" ? ISOWeek.GetYear(units[i].ToDateTime(TimeOnly.MinValue)) : units[i].Year;
            if (unitYear != year)
            {
                year = unitYear;
                items.Add(new DrawingText(left + 3, 16, unitYear.ToString(CultureInfo.InvariantCulture), AxisSize, colors.Text, bold: true));
            }

            if (i > 0)
            {
                items.Add(new DrawingShape(new DrawingPath([new MoveTo(left, 22), new LineTo(left, height - 4)]), null, colors.Line, 0.5));
            }

            if (i % step == 0)
            {
                items.Add(new DrawingText((left + Math.Min(ChartRight, X(Next(units[Math.Min(units.Count - 1, i + step - 1)], grid)))) / 2, 38, Label(units[i], grid, context.Texts), AxisSize, colors.Text)
                {
                    Anchor = TextAnchor.Middle,
                    MaxWidth = Math.Max(1, ((right - left) * step) - 4),
                });
            }
        }

        items.Add(new DrawingShape(new DrawingPath([new MoveTo(0, AxisHeight - 4), new LineTo(Width, AxisHeight - 4)]), null, colors.Line, 1));

        var palette = colors.Palette;
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var middle = AxisHeight + (i * RowHeight) + (RowHeight / 2);
            items.Add(new DrawingText(4, middle + 5, task.Name, NameSize, colors.Text) { MaxWidth = ChartLeft - 14, Mono = task.Mono });
            var fill = task.End < context.Today ? colors.Line : palette[i % palette.Count];
            if (task.Milestone)
            {
                var x = (X(task.Start) + X(task.Start.AddDays(1))) / 2;
                items.Add(new DrawingShape(
                    new DrawingPath([new MoveTo(x, middle - DiamondRadius), new LineTo(x + DiamondRadius, middle), new LineTo(x, middle + DiamondRadius), new LineTo(x - DiamondRadius, middle), new ClosePath()]),
                    fill,
                    null,
                    0));
            }
            else
            {
                var (left, right) = (X(task.Start), X(task.End.AddDays(1)));
                items.Add(new DrawingShape(DrawingPath.Rectangle(left, middle - (BarHeight / 2), Math.Max(2, right - left), BarHeight), fill, null, 0)
                {
                    Kind = DrawingShapeKind.RoundedRectangle,
                    CornerRadius = 3,
                });
            }
        }

        if (context.Today >= axisStart && context.Today < axisEnd)
        {
            var x = (X(context.Today) + X(context.Today.AddDays(1))) / 2;
            items.Add(new DrawingShape(new DrawingPath([new MoveTo(x, AxisHeight - 8), new LineTo(x, height - 2)]), null, context.Brand.Highlight, 2));
        }

        return new Drawing(Width, height, items);
    }

    /// <summary>The grid for a span when none is asked for: up to 10 weeks weeks, up to 18 months months, else quarters.</summary>
    internal static string Choose(DateOnly first, DateOnly last) =>
        last.DayNumber - first.DayNumber + 1 <= WeekGridDays ? "week"
        : ((last.Year * 12) + last.Month) - ((first.Year * 12) + first.Month) + 1 <= MonthGridMonths ? "month"
        : "quarter";

    /// <summary>The first day of every unit of the grid from <paramref name="first"/> to <paramref name="last"/>.</summary>
    internal static List<DateOnly> Units(DateOnly first, DateOnly last, string grid)
    {
        var unit = grid switch
        {
            "week" => first.AddDays(-(((int)first.DayOfWeek + 6) % 7)),
            "month" => new DateOnly(first.Year, first.Month, 1),
            _ => new DateOnly(first.Year, (((first.Month - 1) / 3) * 3) + 1, 1),
        };
        var units = new List<DateOnly>();
        for (; unit <= last; unit = Next(unit, grid))
        {
            units.Add(unit);
        }

        return units;
    }

    /// <summary>A unit's label: "W12" / "KW 12", a month name, "Q2".</summary>
    internal static string Label(DateOnly unit, string grid, GeneratedTexts texts) => grid switch
    {
        "week" => SchedulePeriod.Format(texts.WeekFormat, ISOWeek.GetWeekOfYear(unit.ToDateTime(TimeOnly.MinValue))),
        "month" => SchedulePeriod.Month(texts, unit.Month),
        _ => SchedulePeriod.Format(texts.QuarterFormat, ((unit.Month - 1) / 3) + 1),
    };

    private static DateOnly Next(DateOnly unit, string grid) => grid switch
    {
        "week" => unit.AddDays(7),
        "month" => unit.AddMonths(1),
        _ => unit.AddMonths(3),
    };

    private static List<Task> Tasks(CustomContainer container, VisualContext context)
    {
        var at = VisualContainers.At(container, context);
        var usage = "It holds one table whose first three columns are the task, its start and its end, such as '| Build | 2026-02-16 | 2026-05-29 |'.";
        if (container.Count != 1 || container[0] is not Table table)
        {
            throw context.Error($"The '::: gantt'{at} holds {(container.Count == 0 ? "nothing" : "something else than one table")}. {usage}");
        }

        var rows = table.OfType<TableRow>().Where(r => !r.IsHeader).ToList();
        if (rows.Count is 0 or > MaxRows)
        {
            throw context.Error($"The '::: gantt'{at} has {rows.Count} rows; it takes 1 to {MaxRows}. Split a longer plan into several charts.");
        }

        var tasks = new List<Task>();
        foreach (var row in rows)
        {
            var rowAt = VisualContainers.At(row, context);
            var cells = row.OfType<TableCell>().ToList();
            var name = Cell(cells, 0);
            var spans = name?.Inline is { } inline ? DrawingTexts.Trim(DrawingTexts.Spans(inline.FirstChild)) : [];
            if (spans.Count == 0)
            {
                throw context.Error($"The Gantt row{rowAt} has no task in its first column. {usage}");
            }

            var start = Date(Cell(cells, 1), "start", rowAt, context)
                ?? throw context.Error($"The Gantt row{rowAt} has no start in its second column. {usage}");
            var end = Date(Cell(cells, 2), "end", rowAt, context);
            if (end is not null && end.End < start.Start)
            {
                throw context.Error($"The Gantt row{rowAt} ends ({end.Display(context.Texts)}) before it starts ({start.Display(context.Texts)}).");
            }

            var milestone = end is null || end == start;
            tasks.Add(new Task(spans, DrawingTexts.IsCode(name!.Inline!.FirstChild), start.Start, milestone ? start.Start : end!.End, milestone));
        }

        return tasks;
    }

    private static ParagraphBlock? Cell(List<TableCell> cells, int index) =>
        index < cells.Count ? cells[index].OfType<ParagraphBlock>().FirstOrDefault() : null;

    private static SchedulePeriod? Date(ParagraphBlock? cell, string what, string at, VisualContext context)
    {
        var written = DrawingTexts.Plain(cell).Trim();
        return written.Length == 0 ? null
            : SchedulePeriod.Parse(written)
                ?? throw context.Error($"The Gantt row{at} has the {what} '{DrawingTexts.Quote(written)}'; write it as {SchedulePeriod.Forms}.");
    }
}
