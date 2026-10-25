using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A chart as a vector <see cref="Drawing"/>, for generators that draw rather than write native charts (PDFs): the
/// title on top, the value axis on the left with gridlines at round steps, the categories below, the legend at the
/// bottom. Text and lines in the brand's diagram colours, series in its diagram palette.
/// </summary>
internal static class ChartDrawing
{
    private const double Width = 640;
    private const double Height = 380;
    private const double LabelSize = 11;
    private const double TitleSize = 15;

    public static Drawing Draw(ChartSpec chart, ResolvedDiagramColors colors)
    {
        var items = new List<DrawingItem>();
        var top = 12.0;
        if (chart.Title is { } title)
        {
            items.Add(new DrawingText(Width / 2, top + TitleSize, [new DrawingSpan(title, Bold: true)], TitleSize, colors.Text) { Anchor = TextAnchor.Middle, MaxWidth = Width - 20 });
            top += TitleSize + 14;
        }

        if (chart.Unit is { } unit)
        {
            items.Add(new DrawingText(8, top + LabelSize, [new DrawingSpan(unit)], LabelSize, colors.Text));
            top += LabelSize + 8;
        }

        var (low, high, step) = Scale(chart);
        var legendTop = Height - 22;
        var plot = (Left: 56.0, Top: top + 6, Right: Width - 12, Bottom: legendTop - 26);
        double Y(double value) => plot.Bottom - ((value - low) / (high - low) * (plot.Bottom - plot.Top));

        // Gridlines and their values.
        for (var value = low; value <= high + (step / 2); value += step)
        {
            var y = Y(value);
            items.Add(new DrawingShape(Line(plot.Left, y, plot.Right, y), null, colors.Line, Math.Abs(value) < step / 2 ? 1.2 : 0.5));
            items.Add(new DrawingText(plot.Left - 6, y + (LabelSize * 0.35), [new DrawingSpan(Number(value))], LabelSize, colors.Text) { Anchor = TextAnchor.End });
        }

        var count = chart.Categories.Count;
        var group = (plot.Right - plot.Left) / count;
        double Centre(int i) => plot.Left + (group * (i + 0.5));
        for (var i = 0; i < count; i++)
        {
            items.Add(new DrawingText(Centre(i), plot.Bottom + 6 + LabelSize, [new DrawingSpan(chart.Categories[i])], LabelSize, colors.Text) { Anchor = TextAnchor.Middle, MaxWidth = group - 2 });
        }

        string Colour(int series) => chart.Colors.Count > 0 ? chart.Colors[series % chart.Colors.Count] : colors.Accent;
        switch (chart.Kind)
        {
            case ChartKind.Bar:
                Bars(chart, items, Colour, Y, Centre, group);
                break;
            case ChartKind.Line:
                Lines(chart, items, Colour, Y, Centre);
                break;
            case ChartKind.Area:
                Areas(chart, items, Colour, Y, Centre);
                break;
        }

        Legend(chart, items, Colour, legendTop, colors.Text);
        return new Drawing(Width, Height, items);
    }

    // The value axis: from 0 (or the lowest value, if below) to the highest, in about six round steps.
    internal static (double Low, double High, double Step) Scale(ChartSpec chart)
    {
        var values = chart.Stacked
            ? Enumerable.Range(0, chart.Categories.Count).Select(i => chart.Series.Sum(s => Math.Max(s.Values[i] ?? 0, 0))).ToList()
            : chart.Series.SelectMany(s => s.Values).OfType<double>().ToList();
        var lowest = Math.Min(0, chart.Series.SelectMany(s => s.Values).OfType<double>().DefaultIfEmpty(0).Min());
        var highest = Math.Max(0, values.DefaultIfEmpty(0).Max());
        if (highest - lowest <= 0)
        {
            highest = lowest + 1;
        }

        var rough = (highest - lowest) / 6;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * magnitude).First(s => s >= rough);
        return (Math.Floor(lowest / step) * step, Math.Ceiling(highest / step) * step, step);
    }

    private static void Bars(ChartSpec chart, List<DrawingItem> items, Func<int, string> colour, Func<double, double> y, Func<int, double> centre, double group)
    {
        var width = group * 0.7;
        for (var i = 0; i < chart.Categories.Count; i++)
        {
            var below = 0.0;
            for (var s = 0; s < chart.Series.Count; s++)
            {
                if (chart.Series[s].Values[i] is not { } value)
                {
                    continue;
                }

                if (chart.Stacked)
                {
                    var (from, to) = (below, below + Math.Max(value, 0));
                    items.Add(Bar(centre(i) - (width / 2), y(to), width, y(from) - y(to), colour(s)));
                    below = to;
                }
                else
                {
                    var bar = width / chart.Series.Count;
                    var x = centre(i) - (width / 2) + (s * bar);
                    var (a, b) = (y(Math.Max(value, 0)), y(Math.Min(value, 0)));
                    items.Add(Bar(x + 0.5, a, bar - 1, b - a, colour(s)));
                }
            }
        }
    }

    private static void Lines(ChartSpec chart, List<DrawingItem> items, Func<int, string> colour, Func<double, double> y, Func<int, double> centre)
    {
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var points = chart.Series[s].Values.Select((v, i) => v is { } value ? (X: centre(i), Y: y(value)) : ((double X, double Y)?)null).ToList();
            var segments = new List<PathSegment>();
            var pen = false;
            foreach (var point in points)
            {
                if (point is { } p)
                {
                    segments.Add(pen ? new LineTo(p.X, p.Y) : new MoveTo(p.X, p.Y));
                    pen = true;
                }
                else
                {
                    pen = false;
                }
            }

            items.Add(new DrawingShape(new DrawingPath(segments), null, colour(s), 2.5) { RoundJoins = true });
            foreach (var p in points.OfType<(double X, double Y)>())
            {
                items.Add(new DrawingShape(Circle(p.X, p.Y, 3.5), colour(s), null, 0) { Kind = DrawingShapeKind.Ellipse });
            }
        }
    }

    // Stacked areas sit on each other; overlapping ones are drawn back to front, each lighter, with its line on top.
    private static void Areas(ChartSpec chart, List<DrawingItem> items, Func<int, string> colour, Func<double, double> y, Func<int, double> centre)
    {
        var count = chart.Categories.Count;
        var floor = Enumerable.Repeat(0.0, count).ToArray();
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var values = chart.Series[s].Values.Select(v => v ?? 0).ToArray();
            var tops = chart.Stacked ? values.Select((v, i) => floor[i] + Math.Max(v, 0)).ToArray() : values;
            var bases = chart.Stacked ? floor : Enumerable.Repeat(0.0, count).ToArray();
            var segments = new List<PathSegment> { new MoveTo(centre(0), y(bases[0])) };
            segments.AddRange(Enumerable.Range(0, count).Select(i => new LineTo(centre(i), y(tops[i]))));
            segments.AddRange(Enumerable.Range(0, count).Reverse().Select(i => new LineTo(centre(i), y(bases[i]))));
            segments.Add(new ClosePath());
            var fill = chart.Stacked ? colour(s) : ResolvedBrand.Mix(colour(s), 0.55);
            items.Add(new DrawingShape(new DrawingPath(segments), fill, null, 0));
            items.Add(new DrawingShape(new DrawingPath([new MoveTo(centre(0), y(tops[0])), .. Enumerable.Range(1, count - 1).Select(i => new LineTo(centre(i), y(tops[i])))]), null, colour(s), 2) { RoundJoins = true });
            if (chart.Stacked)
            {
                floor = tops;
            }
        }
    }

    private static void Legend(ChartSpec chart, List<DrawingItem> items, Func<int, string> colour, double top, string text)
    {
        var widths = chart.Series.Select(s => 14 + (s.Name.Length * LabelSize * 0.55) + 18).ToList();
        var x = (Width - widths.Sum()) / 2;
        for (var s = 0; s < chart.Series.Count; s++)
        {
            items.Add(new DrawingShape(DrawingPath.Rectangle(x, top + 4, 10, 10), colour(s), null, 0) { Kind = DrawingShapeKind.Rectangle });
            items.Add(new DrawingText(x + 14, top + 4 + LabelSize - 1, [new DrawingSpan(chart.Series[s].Name)], LabelSize, text));
            x += widths[s];
        }
    }

    private static DrawingShape Bar(double x, double y, double width, double height, string colour) =>
        new(DrawingPath.Rectangle(x, y, Math.Max(width, 0), Math.Max(height, 0)), colour, null, 0) { Kind = DrawingShapeKind.Rectangle };

    private static DrawingPath Line(double x1, double y1, double x2, double y2) => new([new MoveTo(x1, y1), new LineTo(x2, y2)]);

    // A circle as four cubic curves, for generators that draw the path rather than an ellipse shape.
    private static DrawingPath Circle(double x, double y, double r)
    {
        var k = r * 0.5523;
        return new(
        [
            new MoveTo(x + r, y),
            new CubicTo(x + r, y + k, x + k, y + r, x, y + r),
            new CubicTo(x - k, y + r, x - r, y + k, x - r, y),
            new CubicTo(x - r, y - k, x - k, y - r, x, y - r),
            new CubicTo(x + k, y - r, x + r, y - k, x + r, y),
            new ClosePath(),
        ]);
    }

    private static string Number(double value) => Math.Round(value, 6).ToString("#,0.######", CultureInfo.InvariantCulture);
}
