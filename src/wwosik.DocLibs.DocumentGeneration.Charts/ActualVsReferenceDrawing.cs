namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// An <see cref="ActualVsReferenceChart"/>: one band per row, one bar per measure, all diverging around a shared zero
/// line. Measures of one series share a hue, the month solid and the year to date pale. A hue identifies the series and
/// nothing else — a bar behind its reference has the same colour as one ahead of it, because the sign is already in
/// which side of the zero line it sits on.
/// </summary>
internal static class ActualVsReferenceDrawing
{
    private const double PaddingBottom = 22;
    private const double PaddingLeft = 132;
    private const double PaddingRight = 52;
    private const double BarHeight = 11;
    private const double BarGap = 2;
    private const double BandPadding = 14;
    private const double LegendY = 68;
    private const double PaddingTop = 92;

    public static Drawing Draw(ActualVsReferenceChart chart, FinancialCanvas canvas)
    {
        var colors = canvas.Colors;
        var labels = canvas.Labels;
        var rows = chart.Rows;
        var width = canvas.Width;
        if (rows.Count == 0 || rows[0].Measures.Count == 0)
        {
            throw new ArgumentException("An actual-vs-reference chart needs at least one row with at least one measure.");
        }

        var measureCount = rows[0].Measures.Count;
        if (rows.Any(row => row.Measures.Count != measureCount))
        {
            throw new ArgumentException($"Every row of an actual-vs-reference chart needs the same measures; the first has {measureCount}.");
        }

        // The actual is a filled bar; the reference an outline over it, but only on the year-to-date measures: the
        // references are annual, so the same figure would otherwise be drawn twice per series, once against a month it
        // does not describe.
        var value = chart.Display == ReferenceDisplay.Value;
        double Actual(Measure m) => value ? m.Current / canvas.ScaleFactor : m.Change;
        double Reference(Measure m) => value ? m.ReferenceValue / canvas.ScaleFactor : m.Reference;
        string Format(double v) => value ? v.ToString("0.#", canvas.Options.Culture) + (canvas.Options.Unit.Length == 0 ? string.Empty : " " + canvas.Options.Unit) : canvas.Percent(v);

        var bandHeight = (measureCount * (BarHeight + BarGap)) + BandPadding;
        var plotHeight = rows.Count * bandHeight;
        var height = PaddingTop + plotHeight + PaddingBottom;
        var plotWidth = width - PaddingLeft - PaddingRight;

        // Only what is drawn shapes the axis, and of that, the bars carrying a reference set the scale together with
        // those references: they are what the chart is read against.
        var measures = rows.SelectMany(row => row.Measures).ToList();
        var anchors = measures.Where(m => m.YearToDate).SelectMany(m => new[] { Actual(m), Reference(m) }).ToList();
        if (anchors.Count == 0)
        {
            anchors = [.. measures.Select(Actual)];
        }

        var max = Math.Max(0, anchors.Max());
        var min = Math.Min(0, anchors.Min());

        // A bar without a reference may widen the axis by at most half of what the others need. One volatile month —
        // a small month a year ago makes hundreds of percent — would otherwise squash every other bar into the zero
        // line. Past that limit the bar is clipped at the plot edge and says so, its label still stating the true
        // figure.
        var span = Math.Max(max - min, 0.0001);
        var (allowedMax, allowedMin) = (max + (span * 0.5), min - (span * 0.5));
        var loose = measures.Where(m => !m.YearToDate).Select(Actual).ToList();
        max = Math.Max(max, loose.Where(v => v <= allowedMax).DefaultIfEmpty(max).Max());
        min = Math.Min(min, loose.Where(v => v >= allowedMin).DefaultIfEmpty(min).Min());

        // Padded so the value labels at the ends of the longest bars still have room.
        var range = max - min <= 0 ? 1 : max - min;
        max += range * 0.08;
        min -= range * 0.08;
        range = max - min;

        double X(double v) => PaddingLeft + ((v - min) / range * plotWidth);
        var plotRight = PaddingLeft + plotWidth;
        double ClampX(double v) => Math.Clamp(X(v), PaddingLeft, plotRight);
        var zeroX = X(0);

        // Series appear in the order of the first row, and each keeps its colour.
        var order = rows[0].Measures.Select(m => m.Series).Distinct().ToList();
        ResolvedSeriesColors SeriesOf(string series) => colors.Series[Math.Max(0, order.IndexOf(series)) % colors.Series.Count];
        string FillOf(Measure m) => m.YearToDate ? SeriesOf(m.Series).Pale : SeriesOf(m.Series).Fill;

        canvas.Heading(
            chart,
            value
                ? $"Filled bar: {labels.Current}{(canvas.Options.Unit.Length == 0 ? string.Empty : ", in " + canvas.Options.Unit)}. Outline: what {labels.Reference} implies."
                : $"Filled bar: {labels.Change} against {labels.Previous}. Outline: {labels.Reference}.",
            subtitleBaseline: 43);

        // One swatch per measure, in the order the bars appear inside a band, then the reference outline.
        var legendX = 20.0;
        foreach (var m in rows[0].Measures)
        {
            canvas.Rect(legendX, LegendY - 9, 10, 10, FillOf(m), SeriesOf(m.Series).Stroke, 1);
            canvas.Text(legendX + 14, LegendY, m.Label, 10, colors.Text);
            legendX += 24 + FinancialCanvas.TextWidth(m.Label, 10);
        }

        canvas.Rect(legendX, LegendY - 9, 10, 10, null, colors.Reference, 1.5);
        canvas.Text(legendX + 14, LegendY, labels.Reference, 10, colors.Text);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var bandTop = PaddingTop + (r * bandHeight);

            // A tint behind every other band: with several bars per row the eye needs the grouping.
            if (r % 2 == 1)
            {
                canvas.Rect(14, bandTop, width - 28, bandHeight, colors.Band, null, 0);
            }

            // Aggregate rows go bold and flush left; the rows under them stay right-aligned against the plot, which
            // reads as an indent.
            var labelX = row.Aggregate ? 14 : PaddingLeft - 10;
            var anchor = row.Aggregate ? TextAnchor.Start : TextAnchor.End;
            var lines = FinancialCanvas.Wrap(row.Label, 16);
            var first = bandTop + (bandHeight / 2) - (lines.Count * 6.0) + 4;
            for (var l = 0; l < lines.Count; l++)
            {
                canvas.Text(labelX, first + (l * 12), lines[l], 11, colors.Text, anchor, row.Aggregate);
            }

            // The exact references under the label: the outlines show where they are, this says what they are — one
            // figure per series, in its colour, so it is obvious which bars each belongs to.
            var references = order
                .Select(series => row.Measures.FirstOrDefault(m => m.Series == series && m.YearToDate) ?? row.Measures.First(m => m.Series == series))
                .SelectMany((m, i) => i == 0
                    ? new[] { new DrawingSpan(canvas.Percent(m.Reference)) { Color = SeriesOf(m.Series).Stroke } }
                    : [new DrawingSpan(" · "), new DrawingSpan(canvas.Percent(m.Reference)) { Color = SeriesOf(m.Series).Stroke }])
                .Prepend(new DrawingSpan(labels.Reference + ": "))
                .ToList();
            canvas.Text(labelX, first + (lines.Count * 12), references, 9, colors.Muted, anchor);

            for (var i = 0; i < row.Measures.Count; i++)
            {
                var m = row.Measures[i];
                var actual = Actual(m);
                var top = bandTop + (BandPadding / 2) + (i * (BarHeight + BarGap));
                var actualX = ClampX(actual);
                var clipped = Math.Abs(actualX - X(actual)) > 0.5;
                var stroke = SeriesOf(m.Series).Stroke;
                var text = Format(actual);
                var (left, right) = (Math.Min(actualX, zeroX), Math.Max(actualX, zeroX));
                canvas.Rect(left, top, Math.Max(right - left, 0.5), BarHeight, FillOf(m), stroke, 0.75, hover: new DrawingHover($"{row.Label} — {m.Label}: {text}{(clipped ? " (clipped)" : string.Empty)}", (left + right) / 2, top));

                // A clipped bar ends in an arrowhead pointing off the plot, so a bar that stops at the edge is never
                // read as a bar that ends there.
                if (clipped)
                {
                    var tip = actualX + (actual >= 0 ? 6 : -6);
                    canvas.Polygon([(actualX, top + 0.5), (tip, top + (BarHeight / 2)), (actualX, top + BarHeight - 0.5)], stroke, null, 0);
                }

                // The reference: an outline over the fill, so the two are read against each other — and only on the
                // year-to-date bar, the one the annual reference describes. The label goes on whichever bar reaches
                // further, so it never lands inside the other one.
                var outer = actualX;
                if (m.YearToDate)
                {
                    var referenceX = X(Reference(m));
                    var (rl, rr) = (Math.Min(referenceX, zeroX), Math.Max(referenceX, zeroX));
                    canvas.Rect(rl, top, Math.Max(rr - rl, 0.5), BarHeight, null, colors.Reference, 1.5, hover: new DrawingHover($"{row.Label} — {m.Label} {labels.Reference}: {Format(Reference(m))}", (rl + rr) / 2, top));
                    outer = Math.Abs(actualX - zeroX) >= Math.Abs(referenceX - zeroX) ? actualX : referenceX;
                }

                // A clipped bar's label turns inward — outward it would be off the canvas — and states the real figure,
                // not where the bar had to stop.
                var positive = actual >= 0;
                var labelAt = clipped ? actualX + (positive ? -5 : 5) : outer + (positive ? 5 : -5);
                canvas.Text(labelAt, top + BarHeight - 2, text, 9, colors.Text, clipped == positive ? TextAnchor.End : TextAnchor.Start);
            }
        }

        canvas.Line(zeroX, PaddingTop, zeroX, PaddingTop + plotHeight, colors.Axis, 1);
        return canvas.Finish(height);
    }
}
