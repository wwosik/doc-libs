using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// What every financial chart draws with: the items so far, the brand's chart colours, the number formats, and the
/// rules all of them share — the round-step axis, verdict colours, label wrapping and end labels pushed apart.
/// </summary>
internal sealed class FinancialCanvas
{
    /// <summary>The dash that means "projection, not an actual" — and nothing else on these charts.</summary>
    public static readonly IReadOnlyList<double> ProjectionDash = [3, 2];

    /// <summary>The dashed reference lines and the overall lines of the row charts.</summary>
    public static readonly IReadOnlyList<double> ReferenceDash = [5, 4];

    /// <summary>The rule where the actuals end, or where one year meets the next.</summary>
    public static readonly IReadOnlyList<double> BoundaryDash = [2, 3];

    private readonly List<DrawingItem> items = [];

    public FinancialCanvas(FinancialChartOptions options)
    {
        this.Options = options;
        this.Colors = ResolvedBrand.Resolve(options.Brand).Chart;
        this.Width = options.Width;
    }

    public FinancialChartOptions Options { get; }

    public ResolvedChartColors Colors { get; }

    public FinancialChartLabels Labels => this.Options.Labels;

    public double Width { get; set; }

    private CultureInfo Culture => this.Options.Culture;

    /// <summary>The drawing, on the chart surface when the brand has one (none on dark slides, where the slide shows through).</summary>
    public Drawing Finish(double height)
    {
        IReadOnlyList<DrawingItem> all = this.Colors.Surface is { } surface
            ? [Rect(0, 0, this.Width, height, surface, null, 0), .. this.items]
            : this.items;
        return new Drawing(this.Width, height, all);
    }

    public void Add(DrawingItem item) => this.items.Add(item);

    public void Rect(double x, double y, double width, double height, string? fill, string? stroke, double strokeWidth, IReadOnlyList<double>? dash = null, DrawingHover? hover = null) =>
        this.items.Add(Rect(x, y, width, height, fill, stroke, strokeWidth) with { Dash = dash, Hover = hover });

    public void Line(double x1, double y1, double x2, double y2, string stroke, double width, IReadOnlyList<double>? dash = null, DrawingHover? hover = null, bool round = false) =>
        this.items.Add(new DrawingShape(new DrawingPath([new MoveTo(x1, y1), new LineTo(x2, y2)]), null, stroke, width) { Dash = dash, Hover = hover, RoundJoins = round });

    public void Polyline(IReadOnlyList<(double X, double Y)> points, string stroke, double width, IReadOnlyList<double>? dash = null, bool round = false)
    {
        if (points.Count < 2)
        {
            return;
        }

        this.items.Add(new DrawingShape(new DrawingPath([new MoveTo(points[0].X, points[0].Y), .. points.Skip(1).Select(p => new LineTo(p.X, p.Y))]), null, stroke, width) { Dash = dash, RoundJoins = round });
    }

    public void Polygon(IReadOnlyList<(double X, double Y)> points, string? fill, string? stroke, double width, DrawingHover? hover = null) =>
        this.items.Add(new DrawingShape(new DrawingPath([new MoveTo(points[0].X, points[0].Y), .. points.Skip(1).Select(p => new LineTo(p.X, p.Y)), new ClosePath()]), fill, stroke, width) { Hover = hover });

    public void Dot(double x, double y, double radius, string fill) =>
        this.items.Add(new DrawingShape(Circle(x, y, radius), fill, null, 0) { Kind = DrawingShapeKind.Ellipse });

    /// <summary>
    /// A diamond: the estimate's mark, and the one shape on these charts that is neither a bar nor a line — which is
    /// how a reader tells a single stated figure from a series.
    /// </summary>
    public void Diamond(double x, double y, double radius, string fill, string stroke, DrawingHover? hover = null) =>
        this.Polygon([(x, y - radius), (x + radius, y), (x, y + radius), (x - radius, y)], fill, stroke, 1.25, hover);

    public void Text(double x, double baseline, string text, double size, string color, TextAnchor anchor = TextAnchor.Start, bool bold = false, double? maxWidth = null) =>
        this.items.Add(new DrawingText(x, baseline, [new DrawingSpan(text, bold)], size, color) { Anchor = anchor, MaxWidth = maxWidth });

    public void Text(double x, double baseline, IReadOnlyList<DrawingSpan> spans, double size, string color, TextAnchor anchor = TextAnchor.Start) =>
        this.items.Add(new DrawingText(x, baseline, spans, size, color) { Anchor = anchor });

    /// <summary>
    /// A point of a line, which has no area of its own: an invisible, larger hit area with a dot in the line's colour
    /// shown while hovered, so hovering is easy to land and confirms which point was found.
    /// </summary>
    public void HoverPoint(double x, double y, string text, string color) =>
        this.items.Add(new DrawingHotspot(x, y, 10) { Dot = color, Hover = new DrawingHover(text, x, y) });

    /// <summary>The title, with the period in brackets, and the subtitle (the chart's own unless the caller set one).</summary>
    public void Heading(FinancialChart chart, string subtitle, double x = 20, double subtitleBaseline = 44)
    {
        var title = string.IsNullOrWhiteSpace(chart.Period) ? chart.Title : $"{chart.Title} ({chart.Period})";
        this.Text(x, 26, title, 15, this.Colors.Text, bold: true, maxWidth: this.Width - x - 10);
        var line = chart.Subtitle ?? subtitle;
        if (line.Length > 0)
        {
            this.Text(x, subtitleBaseline, line, 10, this.Colors.Muted, maxWidth: this.Width - x - 10);
        }
    }

    // Numbers.

    /// <summary>A percentage as every chart writes one: one decimal when it has one.</summary>
    public string Percent(double value) => (value * 100).ToString("0.#", this.Culture) + "%";

    /// <summary>A percentage read as a difference: always signed, always one decimal, so a column of them lines up.</summary>
    public string SignedPercent(double value) => (value > 0 ? "+" : string.Empty) + (value * 100).ToString("0.0", this.Culture) + "%";

    /// <summary>A share, as a level: one decimal always and no sign, which tells it apart from the signed rows beneath it.</summary>
    public string Rate(double value) => (value * 100).ToString("0.0", this.Culture) + "%";

    /// <summary>
    /// A distance between two shares, in percentage points (<c>-1,2pp</c>): a third percentage in a column of shares
    /// would read as a third share rather than as a gap.
    /// </summary>
    public string SignedPoints(double value) => (value > 0 ? "+" : string.Empty) + (value * 100).ToString("0.0", this.Culture) + "pp";

    /// <summary>A value at the options' scale, with its unit.</summary>
    public string Value(double value) => this.Plain(value) + (this.Options.Unit.Length == 0 ? string.Empty : " " + this.Options.Unit);

    /// <summary>A value at the options' scale, without its unit.</summary>
    public string Plain(double value) => (value / this.ScaleFactor).ToString("0.#", this.Culture);

    /// <summary>A value at the options' scale as a table writes it: one decimal always, so the column lines up.</summary>
    public string TableValue(double value) => (value / this.ScaleFactor).ToString("0.0", this.Culture);

    public double ScaleFactor => this.Options.Scale is > 0 and var scale ? scale : 1;

    /// <summary>
    /// A figure's colour in a table of figures: ahead of what it is measured against, level with it, or behind. Only
    /// figures carry this — a bar keeps its series colour whichever side it lands on, so a hue on the plot still means
    /// a series and nothing else.
    /// </summary>
    public string Verdict(double value, double against) => (value - against) switch
    {
        var d when d > this.Options.Tolerance => this.Colors.Ahead,
        var d when d < -this.Options.Tolerance => this.Colors.Behind,
        _ => this.Colors.Level,
    };

    // Shared rules.

    /// <summary>
    /// An axis over <paramref name="min"/>..<paramref name="max"/> with a round step, always including zero — the bars
    /// grow from it — and padded so the topmost tick label and the end labels have room. The padding goes on before
    /// the rounding, so a series ending exactly on a round number gains a gridline above it rather than touching the
    /// top of its panel.
    /// </summary>
    public static NumericAxis Axis(double min, double max, int ticks = 5)
    {
        min = Math.Min(0, min);
        max = Math.Max(0, max);
        // At least 0.06: a growth-rate panel (fractions) keeps six points of headroom for its end labels.
        var padding = Math.Max(max - min, 1) * 0.06;
        var step = Step((max + padding - (min < 0 ? min - padding : min)) / ticks);
        var low = min < 0 ? Math.Floor((min - padding) / step) * step : 0;
        var high = Math.Ceiling((max + padding) / step) * step;
        return new NumericAxis(low, high <= low ? low + step : high, step);
    }

    /// <summary>The nearest round number at or above <paramref name="rough"/>: 1, 2, 2.5 or 5 times a power of ten.</summary>
    public static double Step(double rough)
    {
        if (rough <= 0 || double.IsNaN(rough) || double.IsInfinity(rough))
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var factor = (rough / magnitude) switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 2.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };
        return factor * magnitude;
    }

    /// <summary>
    /// The gridlines and tick labels of a value axis. The gridline at zero is the axis itself, so it is drawn in the
    /// axis colour — on a panel whose values are all positive, that is the baseline the bars stand on.
    /// </summary>
    public void Gridlines(NumericAxis axis, Func<double, double> y, double left, double right, Func<double, string> format, string? caption = null)
    {
        var ticks = (int)Math.Round((axis.Max - axis.Min) / axis.Step);
        for (var t = 0; t <= ticks; t++)
        {
            var value = axis.Min + (t * axis.Step);
            var at = y(value);
            var zero = Math.Abs(value) < axis.Step / 1000;
            var text = format(value);
            this.Line(left, at, right, at, zero ? this.Colors.Axis : this.Colors.Gridline, 1, hover: new DrawingHover(caption is null ? text : $"{caption}: {text}", (left + right) / 2, at));
            this.Text(left - 6, at + 3, text, 9, this.Colors.Muted, TextAnchor.End);
        }
    }

    /// <summary>
    /// The figures at the ends of lines, beside the lines they belong to: pushed at least 11 px apart where two end
    /// close together, and kept above <paramref name="bottom"/>. Each names its series, so none has to be matched to
    /// the legend by colour.
    /// </summary>
    public void EndLabels(List<(double Y, string Text, string Color)> labels, double x, double bottom)
    {
        if (labels.Count == 0)
        {
            return;
        }

        labels.Sort((a, b) => a.Y.CompareTo(b.Y));
        for (var i = 1; i < labels.Count; i++)
        {
            if (labels[i].Y - labels[i - 1].Y < 11)
            {
                labels[i] = (labels[i - 1].Y + 11, labels[i].Text, labels[i].Color);
            }
        }

        var overflow = labels[^1].Y - bottom;
        foreach (var (y, text, color) in labels)
        {
            this.Text(x, (overflow > 0 ? y - overflow : y) + 3, text, 9, color);
        }
    }

    /// <summary>A label wrapped by words at about <paramref name="width"/> characters; a longer word stays whole.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (candidate.Length > width && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>Roughly how wide a label is: the SVG is written, not measured, so this is an estimate.</summary>
    public static double TextWidth(string text, double size) => DrawingSvg.EstimateWidth(text, size);

    private static DrawingShape Rect(double x, double y, double width, double height, string? fill, string? stroke, double strokeWidth) =>
        new(DrawingPath.Rectangle(x, y, Math.Max(width, 0), Math.Max(height, 0)), fill, stroke, strokeWidth) { Kind = DrawingShapeKind.Rectangle };

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
}

/// <summary>A value axis: where its ends sit, and the interval between its gridlines.</summary>
internal readonly record struct NumericAxis(double Min, double Max, double Step)
{
    /// <summary>The y of <paramref name="value"/> on a panel from <paramref name="top"/>, <paramref name="height"/> tall.</summary>
    public double Y(double value, double top, double height) => top + height - ((value - this.Min) / (this.Max - this.Min) * height);
}
