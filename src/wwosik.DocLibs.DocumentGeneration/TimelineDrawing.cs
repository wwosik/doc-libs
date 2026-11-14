using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>::: timeline</c>: a list of 2 to 8 entries, each <c>- **2026-03** Title: description</c>, drawn 960 px wide and
/// as tall as its labels need: a horizontal line with a dot per entry, evenly spaced in date order (not to scale, so
/// labels never collide). The date is above the line, title and description below; with more than five entries every
/// other entry has them the other way round. Entries whose period ended before today are grey, the next one has a ring.
/// </summary>
internal static class TimelineDrawing
{
    /// <summary>The fewest and most entries.</summary>
    public const int MinEntries = 2;

    /// <inheritdoc cref="MinEntries"/>
    public const int MaxEntries = 8;

    /// <summary>The width in pixels.</summary>
    public const double Width = 960;

    // Entries above this count alternate above and below the line.
    private const int OneSideMax = 5;
    private const double DotRadius = 8;
    private const double RingRadius = 14;
    private const double DateSize = 15;
    private const double TitleSize = 17;
    private const double DescriptionSize = 14;
    private const double LineStep = 20;
    private const int TitleLines = 2;
    private const int DescriptionLines = 3;

    // Baselines from the line: the date above or below it, the first label line below it, the last label line above it.
    private const double DateAbove = 22;
    private const double DateBelow = 36;
    private const double LabelsBelow = 62;
    private const double LabelsAbove = 50;
    private const double Margin = 6;

    private sealed record Entry(SchedulePeriod Period, List<DrawingSpan> Title, List<DrawingSpan>? Description);

    /// <summary>Draws the container's entries.</summary>
    /// <exception cref="DocumentConversionException">Not one list of 2 to 8 dated entries.</exception>
    public static Drawing Draw(CustomContainer container, VisualContext context)
    {
        var entries = Entries(container, context);
        var alternate = entries.Count > OneSideMax;
        var slot = Width / entries.Count;
        var room = (alternate ? 2 * slot : slot) - 16;

        // Labels below the line, the date above; with alternating entries, every other one the other way round.
        bool Below(int i) => !alternate || i % 2 == 0;
        var labels = entries.Select(entry => DrawingTexts.Lines(entry.Title.Select(s => s with { Bold = true }).ToList(), TitleSize, room, TitleLines)
                .Select(l => (Spans: l, Size: TitleSize))
                .Concat(entry.Description is null ? [] : DrawingTexts.Lines(entry.Description, DescriptionSize, room, DescriptionLines).Select(l => (Spans: l, Size: DescriptionSize)))
                .ToList())
            .ToList();

        // As tall as the labels need: the room above the line and below it.
        double Extent(bool below) => Math.Max(
            entries.Select((_, i) => Below(i) != below ? (below ? DateBelow + 5 : DateAbove + DateSize) : 0).Max(),
            labels.Select((l, i) => Below(i) == below && l.Count > 0 ? (below ? LabelsBelow + 5 : LabelsAbove + TitleSize) + ((l.Count - 1) * LineStep) : 0).Max());
        var lineY = Extent(below: false) + Margin;
        var height = lineY + Extent(below: true) + Margin;

        var colors = context.Brand.Diagram;
        var next = entries.FindIndex(e => e.Period.End >= context.Today);
        var items = new List<DrawingItem>
        {
            new DrawingShape(new DrawingPath([new MoveTo(8, lineY), new LineTo(Width - 8, lineY)]), null, colors.Line, 3) { RoundJoins = true },
        };
        for (var i = 0; i < entries.Count; i++)
        {
            var x = slot * (i + 0.5);
            var past = next < 0 || i < next;
            var ink = past ? colors.Line : colors.Text;
            if (i == next)
            {
                items.Add(Circle(x, lineY, RingRadius, null, colors.Accent, 2.5));
            }

            items.Add(Circle(x, lineY, DotRadius, past ? colors.Line : colors.Accent, null, 0));
            var below = Below(i);
            items.Add(new DrawingText(x, below ? lineY - DateAbove : lineY + DateBelow, entries[i].Period.Display(context.Texts), DateSize, i == next ? colors.Accent : ink, bold: true)
            {
                Anchor = TextAnchor.Middle,
                MaxWidth = slot - 12,
            });

            var baseline = below ? lineY + LabelsBelow : lineY - LabelsAbove - ((labels[i].Count - 1) * LineStep);
            foreach (var (spans, size) in labels[i])
            {
                items.Add(new DrawingText(x, baseline, spans, size, ink) { Anchor = TextAnchor.Middle, MaxWidth = room });
                baseline += LineStep;
            }
        }

        return new Drawing(Width, height, items);
    }

    private static DrawingShape Circle(double x, double y, double radius, string? fill, string? stroke, double width) =>
        new(Ellipse(x, y, radius), fill, stroke, width) { Kind = DrawingShapeKind.Ellipse };

    /// <summary>A circle as four Bézier arcs.</summary>
    internal static DrawingPath Ellipse(double x, double y, double radius)
    {
        var k = radius * 0.5523;
        return new DrawingPath(
        [
            new MoveTo(x + radius, y),
            new CubicTo(x + radius, y + k, x + k, y + radius, x, y + radius),
            new CubicTo(x - k, y + radius, x - radius, y + k, x - radius, y),
            new CubicTo(x - radius, y - k, x - k, y - radius, x, y - radius),
            new CubicTo(x + k, y - radius, x + radius, y - k, x + radius, y),
            new ClosePath(),
        ]);
    }

    private static List<Entry> Entries(CustomContainer container, VisualContext context)
    {
        var at = VisualContainers.At(container, context);
        var usage = "It holds one list of 2 to 8 entries, each starting with a bold date: '- **2026-03** Title: description'.";
        if (container.Count != 1 || container[0] is not ListBlock list)
        {
            throw context.Error($"The '::: timeline'{at} holds {(container.Count == 0 ? "nothing" : "something else than one list")}. {usage}");
        }

        var items = list.OfType<ListItemBlock>().ToList();
        if (items.Count is < MinEntries or > MaxEntries)
        {
            throw context.Error($"The '::: timeline'{at} has {items.Count} {(items.Count == 1 ? "entry" : "entries")}; it takes {MinEntries} to {MaxEntries}. {usage}");
        }

        var entries = new List<Entry>();
        foreach (var item in items)
        {
            var itemAt = VisualContainers.At(item, context);
            if (item.Count != 1 || item[0] is not ParagraphBlock { Inline.FirstChild: EmphasisInline { DelimiterCount: 2 } date })
            {
                throw context.Error($"The timeline entry{itemAt} does not start with a bold date. {usage}");
            }

            var written = DrawingTexts.Plain(date.FirstChild);
            var period = SchedulePeriod.Parse(written)
                ?? throw context.Error($"The timeline entry{itemAt} has the date '{DrawingTexts.Quote(written)}'; write it as {SchedulePeriod.Forms}.");
            var (title, description) = DrawingTexts.Split(DrawingTexts.Spans(date.NextSibling), ':');
            if (title.Count == 0)
            {
                throw context.Error($"The timeline entry{itemAt} has no title after its date. {usage}");
            }

            entries.Add(new Entry(period, title, description is { Count: > 0 } ? description : null));
        }

        // Stable: equal dates keep the order they are written in.
        return [.. entries.OrderBy(e => e.Period.Start)];
    }
}
