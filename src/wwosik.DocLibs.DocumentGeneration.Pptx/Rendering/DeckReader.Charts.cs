using System.Globalization;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

internal sealed partial class DeckReader
{
    private const double ChartPadding = 0.04;

    private sealed record ChartSeries(string Name, Rgba Color, IReadOnlyList<double?> Values);

    /// <summary>
    /// A chart part as shapes, painted from its caches: exactly what <c>ChartWriter</c> writes — columns clustered or
    /// stacked, lines with markers, areas standard or stacked, the title, the unit, gridlines, category and value labels
    /// and the legend at the bottom. Any other chart kind is left out and reported.
    /// </summary>
    private List<SceneItem> Chart(XElement chartSpace, SlideParts context, Matrix frame, double width, double height)
    {
        var palette = context.Palette;
        var chart = chartSpace.Element(Ns.C + "chart");
        var plotArea = chart?.Element(Ns.C + "plotArea");
        var plot = plotArea?.Elements().FirstOrDefault(e => e.Name.LocalName is "barChart" or "lineChart" or "areaChart");
        var others = plotArea?.Elements().Where(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal) && e != plot).ToList() ?? [];
        foreach (var other in others)
        {
            this.notes.Unsupported($"charts of the kind {other.Name.LocalName}");
        }

        if (chart is null || plot is null)
        {
            return [];
        }

        var kind = plot.Name.LocalName;
        if (kind == "barChart" && plot.Element(Ns.C + "barDir")?.Attribute("val")?.Value == "bar")
        {
            this.notes.Unsupported("horizontal bar charts");
            return [];
        }

        var stacked = plot.Element(Ns.C + "grouping")?.Attribute("val")?.Value is "stacked" or "percentStacked";
        var series = plot.Elements(Ns.C + "ser").Select(s => this.Series(s, palette)).ToList();
        var categories = plot.Elements(Ns.C + "ser").FirstOrDefault()?.Element(Ns.C + "cat")?.Descendants(Ns.C + "pt")
            .OrderBy(p => Palette.Number(p, "idx")).Select(p => p.Element(Ns.C + "v")?.Value ?? string.Empty).ToList() ?? [];
        var count = Math.Max(categories.Count, series.Select(s => s.Values.Count).DefaultIfEmpty(0).Max());
        if (count == 0 || series.Count == 0)
        {
            return [];
        }

        // Text: the chart's text properties (colour, font).
        var textProperties = chartSpace.Element(Ns.C + "txPr")?.Descendants(Ns.A + "defRPr").FirstOrDefault();
        var textColour = palette.Color(textProperties?.Element(Ns.A + "solidFill")) ?? new Rgba(0x59, 0x59, 0x59);
        var family = palette.Theme.Font(textProperties?.Element(Ns.A + "latin")?.Attribute("typeface")?.Value ?? "+mn-lt");
        var items = new List<SceneItem>();
        var labelSize = 11 * 12700.0;

        SceneRun Run(string text, double size, bool bold, Rgba colour)
        {
            var font = new SceneFont(family, bold, false, this.fonts.Face(family, bold, false));
            return new SceneRun(0, FontLibrary.Width(font, text, size), text, font, size, colour, false, false, 0, null, null);
        }

        void Label(string text, double x, double baseline, string align, double size = 0, bool bold = false, Rgba? colour = null)
        {
            var run = Run(text, size > 0 ? size : labelSize, bold, colour ?? textColour);
            var left = align switch { "ctr" => x - (run.Width / 2), "r" => x - run.Width, _ => x };
            var matrix = frame * Matrix.Translate(left, 0);
            items.Add(new SceneShape(0, string.Empty, matrix, [], new SceneText([new SceneParagraph(0, [new SceneTextLine(baseline, [run])])]), matrix, null));
        }

        var top = height * ChartPadding;
        var bottom = height * (1 - ChartPadding);
        var left = width * ChartPadding;
        var right = width * (1 - ChartPadding);

        // Title.
        if (chart.Element(Ns.C + "autoTitleDeleted")?.Attribute("val")?.Value is not ("1" or "true")
            && chart.Element(Ns.C + "title") is { } title
            && string.Concat(title.Descendants(Ns.A + "t").Select(t => t.Value)) is { Length: > 0 } words)
        {
            var size = (Palette.Number(title.Descendants(Ns.A + "rPr").FirstOrDefault() ?? new XElement("x"), "sz") is > 0 and var sz ? sz : 1400) / 100 * 12700;
            Label(words, width / 2, top + size, "ctr", size, bold: true);
            top += size * 1.6;
        }

        // Legend at the bottom.
        if (chart.Element(Ns.C + "legend") is not null)
        {
            var entries = series.Select(s => (s, Run(s.Name, labelSize, false, textColour))).ToList();
            var swatch = labelSize * 0.7;
            var gap = labelSize;
            var total = entries.Sum(e => swatch + (labelSize * 0.3) + e.Item2.Width) + (gap * (entries.Count - 1));
            var x = (width - total) / 2;
            var baseline = bottom - (labelSize * 0.25);
            foreach (var (entry, run) in entries)
            {
                var box = frame * Matrix.Translate(x, baseline - swatch);
                items.Add(new SceneShape(0, string.Empty, box, this.Paths(null, swatch, swatch, new SolidFill(entry.Color), null), null, box, null));
                Label(entry.Name, x + swatch + (labelSize * 0.3), baseline, "l");
                x += swatch + (labelSize * 0.3) + run.Width + gap;
            }

            bottom -= labelSize * 1.8;
        }

        // Value axis scale.
        var totals = Enumerable.Range(0, count).Select(i => stacked
            ? (series.Sum(s => Math.Max(0, s.Values.ElementAtOrDefault(i) ?? 0)), series.Sum(s => Math.Min(0, s.Values.ElementAtOrDefault(i) ?? 0)))
            : (series.Max(s => s.Values.ElementAtOrDefault(i) ?? double.MinValue), series.Min(s => s.Values.ElementAtOrDefault(i) ?? double.MaxValue))).ToList();
        var high = totals.Max(t => t.Item1);
        var low = totals.Min(t => t.Item2);
        if (high == double.MinValue)
        {
            (high, low) = (1, 0);
        }

        var valueAxis = plotArea!.Element(Ns.C + "valAx");
        var minimum = valueAxis?.Element(Ns.C + "scaling")?.Element(Ns.C + "min") is { } min ? Palette.Number(min, "val") : (double?)null;
        var (axisLow, axisHigh, step) = Scale(minimum ?? (kind == "lineChart" ? low : Math.Min(0, low)), high, minimum is not null || kind != "lineChart");

        // Unit above the value labels.
        if (valueAxis?.Element(Ns.C + "title") is { } unit && string.Concat(unit.Descendants(Ns.A + "t").Select(t => t.Value)) is { Length: > 0 } unitText)
        {
            Label(unitText, left, top + labelSize, "l");
            top += labelSize * 1.6;
        }

        // Value labels and gridlines.
        var ticks = new List<double>();
        for (var v = axisLow; v <= axisHigh + (step / 1000); v += step)
        {
            ticks.Add(Math.Round(v / step) * step);
        }

        var valueWidth = ticks.Max(t => Run(Number(t), labelSize, false, textColour).Width);
        var plotLeft = left + valueWidth + (labelSize * 0.5);
        var plotBottom = bottom - (labelSize * 1.6);
        var plotTop = top + (labelSize * 0.5);
        double Y(double value) => plotBottom - ((value - axisLow) / (axisHigh - axisLow) * (plotBottom - plotTop));
        var grid = palette.Stroke(valueAxis?.Element(Ns.C + "majorGridlines")?.Element(Ns.C + "spPr")?.Element(Ns.A + "ln"), null, null);
        foreach (var tick in ticks)
        {
            var y = Y(tick);
            if (grid is not null)
            {
                items.Add(Line(frame, plotLeft, y, right, y, grid));
            }

            Label(Number(tick), plotLeft - (labelSize * 0.5), y + (labelSize * 0.35), "r");
        }

        // Category axis line and labels.
        var band = (right - plotLeft) / count;
        var categoryLine = palette.Stroke(plotArea.Element(Ns.C + "catAx")?.Element(Ns.C + "spPr")?.Element(Ns.A + "ln"), null, null);
        if (categoryLine is not null)
        {
            items.Add(Line(frame, plotLeft, Y(Math.Max(axisLow, Math.Min(0, axisHigh))), right, Y(Math.Max(axisLow, Math.Min(0, axisHigh))), categoryLine));
        }

        for (var i = 0; i < categories.Count; i++)
        {
            Label(categories[i], plotLeft + (band * (i + 0.5)), plotBottom + (labelSize * 1.2), "ctr");
        }

        var zero = Y(Math.Max(axisLow, Math.Min(0, axisHigh)));
        switch (kind)
        {
            case "barChart":
                var gapWidth = plot.Element(Ns.C + "gapWidth") is { } g ? Palette.Number(g, "val") / 100 : 1.5;
                var group = band / (1 + gapWidth);
                var bar = stacked ? group : group / series.Count;
                for (var i = 0; i < count; i++)
                {
                    var (above, below) = (0.0, 0.0);
                    for (var s = 0; s < series.Count; s++)
                    {
                        if (series[s].Values.ElementAtOrDefault(i) is not { } value)
                        {
                            continue;
                        }

                        var x = plotLeft + (band * i) + ((band - group) / 2) + (stacked ? 0 : bar * s);
                        double from, to;
                        if (stacked)
                        {
                            ref var start = ref value >= 0 ? ref above : ref below;
                            (from, to) = (start, start + value);
                            start += value;
                        }
                        else
                        {
                            (from, to) = (0, value);
                        }

                        var (y1, y2) = (Y(Math.Max(from, to)), Y(Math.Min(from, to)));
                        var box = frame * Matrix.Translate(x, y1);
                        items.Add(new SceneShape(0, string.Empty, box, this.Paths(null, bar, Math.Max(0, y2 - y1), new SolidFill(series[s].Color), null), null, box, null));
                    }
                }

                break;
            case "areaChart":
                var baseline = Enumerable.Repeat(0.0, count).ToArray();
                foreach (var s in series)
                {
                    var tops = Enumerable.Range(0, count).Select(i => baseline[i] + (s.Values.ElementAtOrDefault(i) ?? 0)).ToArray();
                    var segments = new List<PathSegment>();
                    for (var i = 0; i < count; i++)
                    {
                        var x = plotLeft + (band * (i + 0.5));
                        segments.Add(i == 0 ? new PathMove(x, Y(tops[i])) : new PathLine(x, Y(tops[i])));
                    }

                    for (var i = count - 1; i >= 0; i--)
                    {
                        segments.Add(new PathLine(plotLeft + (band * (i + 0.5)), stacked ? Y(baseline[i]) : zero));
                    }

                    segments.Add(PathClose.Instance);
                    items.Add(new SceneShape(0, string.Empty, frame, [new ScenePath(segments, new SolidFill(s.Color), null)], null, frame, null));
                    if (stacked)
                    {
                        baseline = tops;
                    }
                }

                break;
            default:
                var markers = plot.Element(Ns.C + "marker")?.Attribute("val")?.Value is not ("0" or "false");
                foreach (var (s, index) in series.Select((s, i) => (s, i)))
                {
                    var element = plot.Elements(Ns.C + "ser").ElementAt(index);
                    var width2 = element.Element(Ns.C + "spPr")?.Element(Ns.A + "ln") is { } ln ? Palette.Number(ln, "w") : 28575;
                    var stroke = new SceneStroke(width2 > 0 ? width2 : 28575, s.Color, null, "round", "round", null, null);
                    var segments = new List<PathSegment>();
                    var open = false;
                    for (var i = 0; i < count; i++)
                    {
                        if (s.Values.ElementAtOrDefault(i) is not { } value)
                        {
                            open = false;
                            continue;
                        }

                        var (x, y) = (plotLeft + (band * (i + 0.5)), Y(value));
                        segments.Add(open ? new PathLine(x, y) : new PathMove(x, y));
                        open = true;
                    }

                    items.Add(new SceneShape(0, string.Empty, frame, [new ScenePath(segments, null, stroke)], null, frame, null));
                    var marker = element.Element(Ns.C + "marker");
                    if (markers && marker?.Element(Ns.C + "symbol")?.Attribute("val")?.Value != "none")
                    {
                        var size = (marker?.Element(Ns.C + "size") is { } sz ? Palette.Number(sz, "val") : 5) * 12700;
                        for (var i = 0; i < count; i++)
                        {
                            if (s.Values.ElementAtOrDefault(i) is { } value)
                            {
                                var dot = frame * Matrix.Translate(plotLeft + (band * (i + 0.5)) - (size / 2), Y(value) - (size / 2));
                                items.Add(new SceneShape(0, string.Empty, dot, this.Paths(new XElement(Ns.A + "prstGeom", new XAttribute("prst", "ellipse")), size, size, new SolidFill(s.Color), null), null, dot, null));
                            }
                        }
                    }
                }

                break;
        }

        return items;
    }

    private ChartSeries Series(XElement series, Palette palette)
    {
        var name = string.Concat(series.Element(Ns.C + "tx")?.Descendants(Ns.C + "v").Select(v => v.Value) ?? []);
        var properties = series.Element(Ns.C + "spPr");
        var colour = palette.Color(properties?.Element(Ns.A + "solidFill"))
            ?? palette.Color(properties?.Element(Ns.A + "ln")?.Element(Ns.A + "solidFill"))
            ?? new Rgba(0x44, 0x72, 0xC4);
        var points = series.Element(Ns.C + "val")?.Descendants(Ns.C + "pt").ToList() ?? [];
        var count = series.Element(Ns.C + "val")?.Descendants(Ns.C + "ptCount").FirstOrDefault() is { } ptCount ? (int)Palette.Number(ptCount, "val") : points.Count;
        var values = new double?[Math.Max(count, points.Count == 0 ? 0 : (int)points.Max(p => Palette.Number(p, "idx")) + 1)];
        foreach (var point in points)
        {
            if (double.TryParse(point.Element(Ns.C + "v")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values[(int)Palette.Number(point, "idx")] = value;
            }
        }

        return new ChartSeries(name, colour, values);
    }

    private static SceneShape Line(Matrix frame, double x1, double y1, double x2, double y2, SceneStroke stroke) =>
        new(0, string.Empty, frame, [new ScenePath([new PathMove(x1, y1), new PathLine(x2, y2)], null, stroke)], null, frame, null);

    // A scale of round steps (1, 2 or 5 times a power of ten) with about five intervals.
    private static (double Low, double High, double Step) Scale(double low, double high, bool fixedLow)
    {
        if (high <= low)
        {
            high = low + 1;
        }

        var rough = (high - low) / 5;
        var power = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = new[] { 1, 2, 5, 10 }.Select(f => f * power).First(s => s >= rough);
        var start = fixedLow ? low : Math.Floor(low / step) * step;
        var end = Math.Ceiling(high / step) * step;
        if (end <= start)
        {
            end = start + step;
        }

        return (start, end, step);
    }

    private static string Number(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
}
