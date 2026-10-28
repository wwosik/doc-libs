namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A <see cref="DevelopmentChart"/>: two panels over one row of month slots — bars per month above, the year cumulated
/// (or its growth rate) below. The slot geometry is computed once and used by both panels: a month has to read straight
/// down from a bar to the line point below it, or the two stop being one picture. Each panel keeps its own value axis:
/// a cumulative figure is an order of magnitude larger than a monthly one, and a shared scale would flatten the upper
/// panel to nothing.
/// </summary>
internal static class DevelopmentDrawing
{
    private const double PaddingLeft = 52;

    // At least this wide, and as wide as the longest end-of-year label: each one names the series it belongs to, so
    // the figures can be read without matching colours to the legend.
    private const double MinPaddingRight = 68;

    // The lines panel is half again as tall as the bars panel. The upper panel's two bars a month are separated by
    // their own width, while the lower panel's lines and marks all start in the same corner and run close together
    // for the first months of the year: the extra room goes where the crowding is.
    private const double UpperHeight = 150;
    private const double LowerHeight = UpperHeight * 3 / 2;
    private const double UpperTop = 92;
    private const double LowerTop = UpperTop + UpperHeight + 34;

    public static Drawing Draw(DevelopmentChart chart, FinancialCanvas canvas)
    {
        var colors = canvas.Colors;
        var labels = canvas.Labels;
        var months = chart.Months;
        if (months.Count == 0)
        {
            throw new ArgumentException("A development chart needs at least one month.");
        }

        if (chart.LastActual < 0 || chart.LastActual >= months.Count)
        {
            throw new ArgumentException($"The last actual month is {chart.LastActual}, which is not a month of the chart (0 to {months.Count - 1}).");
        }

        var (current, previous) = (colors.Series[0], colors.Series[1 % colors.Series.Count]);
        var width = canvas.Width;
        var growth = chart.LowerPanel == DevelopmentPanel.Growth;

        // A growth panel is a rate, and an estimate is a cumulative figure: it has no place there.
        var estimates = growth ? [] : chart.Estimates.Where(e => e.Month >= 0 && e.Month < months.Count).ToList();
        var last = months.Count - 1;
        var lastActual = chart.LastActual;
        var projected = months.Any(m => m.Projection is not null || m.ProjectionCumulative is not null);
        var height = LowerTop + LowerHeight + 26;
        var paddingRight = Math.Max(MinPaddingRight, EndLabelTexts(chart, canvas).Max(text => FinancialCanvas.TextWidth(text, 9)) + 10);
        var plotWidth = width - PaddingLeft - paddingRight;
        var plotRight = PaddingLeft + plotWidth;
        var slot = plotWidth / months.Count;
        double Centre(int index) => PaddingLeft + ((index + 0.5) * slot);

        // Everything each panel draws, and nothing it does not: an undrawn figure must not pad an axis.
        var monthly = new List<double>();
        var cumulative = new List<double>();
        for (var i = 0; i < months.Count; i++)
        {
            var month = months[i];
            monthly.Add(month.Previous);
            monthly.Add(month.Reference);
            cumulative.Add(month.PreviousCumulative);
            cumulative.Add(month.ReferenceCumulative);
            if (month.Current is { } c)
            {
                monthly.Add(c);
            }

            // Every projection bar that is drawn: overlapping an actual from the compared-from month on, and alone
            // after the last actual — and no earlier one.
            if (month.Projection is { } p && (month.Current is null || (chart.ProjectionComparedFrom is { } from && i >= from)))
            {
                monthly.Add(p);
            }

            if (month.CurrentCumulative is { } cc)
            {
                cumulative.Add(cc);
            }

            // The projection line runs the whole year, so all of its points shape the axis.
            if (month.ProjectionCumulative is { } pc)
            {
                cumulative.Add(pc);
            }
        }

        // A mark off the top of the panel would be a mark nobody can read, so the axis carries it too.
        cumulative.AddRange(estimates.Select(e => e.Value));
        var rates = months.Where(m => m.Growth is not null).Select(m => m.Growth!.Value).Append(chart.ReferenceRate).ToList();

        var upper = FinancialCanvas.Axis(monthly.Min(), monthly.Max());
        var lower = growth ? FinancialCanvas.Axis(rates.Min(), rates.Max()) : FinancialCanvas.Axis(cumulative.Min(), cumulative.Max());
        double UpperY(double v) => upper.Y(v, UpperTop, UpperHeight);
        double LowerY(double v) => lower.Y(v, LowerTop, LowerHeight);

        // "Is the year ahead of last year" is a percentage question and both axes are in money, so the two answers
        // that matter are stated rather than left to be measured off the lines.
        canvas.Heading(
            chart,
            $"{labels.Current} vs {labels.Previous} {canvas.SignedPercent(chart.ChangeAtPeriod)} at {months[lastActual].Label}"
            + (chart.ChangeAtYearEnd is { } yearEnd ? $", {labels.Projection} vs {labels.Previous} {canvas.SignedPercent(yearEnd)} at {months[last].Label}." : "."));

        var legendX = 20.0;
        void Legend(string? fill, string stroke, IReadOnlyList<double>? dash, string text)
        {
            canvas.Rect(legendX, 57, 10, 10, fill, stroke, 1, dash);
            canvas.Text(legendX + 14, 66, text, 10, colors.Text);
            legendX += 20 + FinancialCanvas.TextWidth(text, 10);
        }

        Legend(previous.Strong, previous.Stroke, null, labels.Previous);
        Legend(current.Strong, current.Stroke, null, labels.Current);

        // The projection covers the whole year, so its entry names no month range: the boundary rule and the actuals'
        // own right edge say where the year stops.
        if (projected)
        {
            Legend(current.Pale, current.Stroke, FinancialCanvas.ProjectionDash, labels.Projection);
        }

        // One entry for both of the reference's forms: the grey stacked on the previous year above, the line below.
        // The label says where the figure comes from, which is what the stacking draws.
        Legend(colors.ReferenceFill, colors.Reference, null, $"{labels.Reference} ({labels.Previous} {canvas.SignedPercent(chart.ReferenceRate)})");

        // An estimate is a diamond and not another colour: like the projection it is this year as somebody expects it
        // to end up, so it keeps this year's hue and is told apart by shape.
        if (estimates.Count > 0)
        {
            canvas.Diamond(legendX + 5, 62, 5, current.Pale, current.Stroke);
            canvas.Text(legendX + 14, 66, labels.Estimate, 10, colors.Text);
        }

        void Panel(string caption, NumericAxis axis, double top, double panelHeight, Func<double, string> format)
        {
            canvas.Text(20, top - 10, caption, 10, colors.Muted);
            canvas.Gridlines(axis, v => axis.Y(v, top, panelHeight), PaddingLeft, plotRight, format, caption);
        }

        Panel("Per month", upper, UpperTop, UpperHeight, canvas.Value);
        if (growth)
        {
            Panel($"Cumulative growth {labels.Current} vs {labels.Previous}", lower, LowerTop, LowerHeight, canvas.Percent);
        }
        else
        {
            Panel("Cumulative", lower, LowerTop, LowerHeight, canvas.Value);
        }

        // Where the actuals end, drawn once and crossing both panels: it is the same instant in both pictures, and it
        // is what makes "dashed and pale means projection" legible.
        if (lastActual < last)
        {
            var boundary = PaddingLeft + ((lastActual + 1) * slot);
            var tooltip = $"Actuals end after {months[lastActual].Label}";
            foreach (var (top, panelHeight) in new[] { (UpperTop, UpperHeight), (LowerTop, LowerHeight) })
            {
                canvas.Line(boundary, top, boundary, top + panelHeight, colors.Gridline, 1.5, FinancialCanvas.BoundaryDash, new DrawingHover(tooltip, boundary, top));
            }
        }

        // Upper panel: two bars a month, the previous year beside the current one.
        var group = slot * 0.66;
        var bar = (group - 2) / 2;
        void Segment(double x, double from, double to, string fill, string stroke, IReadOnlyList<double>? dash, string tooltip)
        {
            var (y1, y2) = (UpperY(from), UpperY(to));
            canvas.Rect(x, Math.Min(y1, y2), bar, Math.Max(Math.Abs(y1 - y2), 0.6), fill, stroke, 0.75, dash, new DrawingHover(tooltip, x + (bar / 2), Math.Min(y1, y2)));
        }

        for (var i = 0; i < months.Count; i++)
        {
            var month = months[i];
            var left = Centre(i) - (group / 2);

            // The previous year is drawn for every month — it is complete — with the growth the reference asks for
            // stacked on it in grey. The annual rate has no monthly split, so a month's reference is the previous year
            // plus a percentage: stacking draws exactly that, and the top of the stack is the line the current bar
            // beside it is read against.
            Segment(left, 0, month.Previous, previous.Strong, previous.Stroke, null, $"{month.Label} {labels.Previous}: {canvas.Value(month.Previous)}");
            Segment(left, month.Previous, month.Reference, colors.ReferenceFill, colors.Reference, null, $"{month.Label} {labels.Reference}: {canvas.Value(month.Reference)}");

            // The current year: an actual through the last actual month, the projection's own month after it — the
            // same series either way, paler and dashed, never a third hue.
            var x = left + bar + 2;
            void ProjectionBar(double v) => Segment(x, 0, v, current.Pale, current.Stroke, FinancialCanvas.ProjectionDash, $"{month.Label} {labels.Projection}: {canvas.Value(v)}");
            void ActualBar(double v) => Segment(x, 0, v, current.Strong, current.Stroke, null, $"{month.Label} {labels.Current}: {canvas.Value(v)}");

            if (month.Current is { } actual)
            {
                // From the compared-from month on, the projection for a month that has happened is drawn against its
                // actual, in the same position, the two overlapping: the shorter bar goes in front, so neither hides
                // the other. A projection under the actual reads as a pale block inside it; one over it as pale
                // headroom above it.
                var overlapping = chart.ProjectionComparedFrom is { } from && i >= from ? month.Projection : null;
                if (overlapping is { } behind && behind > actual)
                {
                    ProjectionBar(behind);
                    ActualBar(actual);
                }
                else
                {
                    ActualBar(actual);
                    if (overlapping is { } front)
                    {
                        ProjectionBar(front);
                    }
                }
            }
            else if (i > lastActual && month.Projection is { } alone)
            {
                ProjectionBar(alone);
            }
        }

        // The month labels, once, under the lower panel: both panels stand over the same slots.
        for (var i = 0; i < months.Count; i++)
        {
            canvas.Text(Centre(i), LowerTop + LowerHeight + 14, months[i].Label, 9, colors.Text, TextAnchor.Middle, maxWidth: slot);
        }

        var bottom = LowerTop + LowerHeight + 4;
        if (growth)
        {
            GrowthPanel(chart, canvas, LowerY, Centre, plotRight, bottom);
            return canvas.Finish(height);
        }

        // Lower panel, cumulative. Every cumulative series starts at zero where the axis starts: a year to date is zero
        // before January has happened, so the lines rise out of the corner of the panel rather than appearing at the
        // first slot already part-grown.
        var origin = (PaddingLeft, LowerY(0));
        List<(double X, double Y)> Line(IEnumerable<(int Index, double Value)> points) => [origin, .. points.Select(p => (Centre(p.Index), LowerY(p.Value)))];
        void Points(IEnumerable<(int Index, double Value)> points, string label, string color)
        {
            foreach (var (index, v) in points)
            {
                canvas.HoverPoint(Centre(index), LowerY(v), $"{months[index].Label} {label}: {canvas.Value(v)}", color);
            }
        }

        // What has happened is a shaded area rather than a line: it is the one part of the year that is not a
        // projection, so it reads as ground the other series are measured over. Drawn first, so the lines cross it.
        var actuals = months.Take(lastActual + 1).Select((m, i) => (Index: i, Value: m.CurrentCumulative ?? 0)).ToList();
        canvas.Polygon([.. Line(actuals), (Centre(actuals[^1].Index), LowerY(0))], current.Strong, null, 0);
        Points(actuals, labels.Current, current.Stroke);

        // Both complete years are solid: the reference is the fixed line everything is measured against, the previous
        // year is what happened, and neither is an estimate of anything. Only the projection is dashed.
        var lineWidth = chart.CumulativeLineWidth;
        var references = months.Select((m, i) => (i, m.ReferenceCumulative)).ToList();
        canvas.Polyline(Line(references), colors.Reference, lineWidth);
        Points(references, labels.Reference, colors.Reference);
        var previousYear = months.Select((m, i) => (i, m.PreviousCumulative)).ToList();
        canvas.Polyline(Line(previousYear), previous.Stroke, lineWidth);
        Points(previousYear, labels.Previous, previous.Stroke);

        // The projection is its own line across the whole year, drawn last so it sits over the area. Where it runs
        // beside the actuals it is the projection's version of months that have happened, and the two saying different
        // things is the point.
        var projection = months.Select((m, i) => (Index: i, Value: m.ProjectionCumulative)).Where(p => p.Value is not null).Select(p => (p.Index, p.Value!.Value)).ToList();
        if (projection.Count > 0)
        {
            canvas.Polyline(Line(projection), current.Stroke, lineWidth, FinancialCanvas.ProjectionDash);
            Points(projection, labels.Projection, current.Stroke);
        }

        // An estimate: a diamond on the month it names, at the figure it states. It carries its own name and number,
        // so nothing about it has to be inferred from its position — a mark that states neither is read as a second bar.
        foreach (var estimate in estimates)
        {
            var (x, y) = (Centre(estimate.Month), LowerY(estimate.Value));
            var text = $"{estimate.Label} {canvas.Value(estimate.Value)}";
            var hover = new DrawingHover($"{estimate.Label}: {canvas.Value(estimate.Value)}", x, y);
            if (!chart.HideEstimateLabels)
            {
                // Below the diamond by default: an estimate sits after the last actual, where the area has ended and the
                // room underneath is empty, while the projection and reference run above. Above only when there is no
                // room below. Either way the label can land on a line, so it is written on a patch of background, drawn
                // before the diamond so the patch cannot clip it.
                var labelY = y + 15 > LowerTop + LowerHeight ? y - 9 : y + 15;
                var patch = FinancialCanvas.TextWidth(text, 9) + 6;
                canvas.Rect(x - (patch / 2), labelY - 10, patch, 15, colors.Patch, null, 0);
                canvas.Diamond(x, y, 4.5, current.Pale, current.Stroke, hover);
                canvas.Text(x, labelY, text, 9, current.Stroke, TextAnchor.Middle);
            }
            else
            {
                canvas.Diamond(x, y, 4.5, current.Pale, current.Stroke, hover);
            }
        }

        // The year-end figures — what the whole picture exists to compare — beside the ends of their lines.
        var ends = new List<(double Y, string Text, string Color)>
        {
            (LowerY(months[last].PreviousCumulative), $"{labels.Previous} {canvas.Value(months[last].PreviousCumulative)}", previous.Stroke),
            (LowerY(months[last].ReferenceCumulative), $"{labels.Reference} {canvas.Value(months[last].ReferenceCumulative)}", colors.Reference),
        };
        if (months[last].ProjectionCumulative is { } end)
        {
            ends.Add((LowerY(end), $"{labels.Projection} {canvas.Value(end)}", current.Stroke));
        }
        else if (lastActual == last && months[last].CurrentCumulative is { } actualEnd)
        {
            ends.Add((LowerY(actualEnd), $"{labels.Current} {canvas.Value(actualEnd)}", current.Stroke));
        }

        canvas.EndLabels(ends, plotRight + 5, bottom);
        return canvas.Finish(height);
    }

    // Every end label the lower panel may write, for the room they need.
    private static IEnumerable<string> EndLabelTexts(DevelopmentChart chart, FinancialCanvas canvas)
    {
        var labels = canvas.Labels;
        var last = chart.Months[^1];
        if (chart.LowerPanel == DevelopmentPanel.Growth)
        {
            yield return $"{labels.Reference} {canvas.Percent(chart.ReferenceRate)}";
            foreach (var rate in chart.Months.Where(m => m.Growth is not null).Select(m => m.Growth!.Value))
            {
                yield return $"{labels.Projection} {canvas.Percent(rate)}";
                yield return $"{labels.Current} {canvas.Percent(rate)}";
            }

            yield break;
        }

        yield return $"{labels.Previous} {canvas.Value(last.PreviousCumulative)}";
        yield return $"{labels.Reference} {canvas.Value(last.ReferenceCumulative)}";
        if (last.ProjectionCumulative is { } projection)
        {
            yield return $"{labels.Projection} {canvas.Value(projection)}";
        }

        if (last.CurrentCumulative is { } current)
        {
            yield return $"{labels.Current} {canvas.Value(current)}";
        }
    }

    // Lower panel, growth: the growth-trend line under the monthly bars. The reference is one flat annual rate, so it
    // is solid; the rate is solid through the last actual and dashed after it, the two segments meeting.
    private static void GrowthPanel(DevelopmentChart chart, FinancialCanvas canvas, Func<double, double> y, Func<int, double> centre, double plotRight, double bottom)
    {
        var colors = canvas.Colors;
        var labels = canvas.Labels;
        var months = chart.Months;
        var current = colors.Series[0];
        var referenceY = y(chart.ReferenceRate);
        canvas.Line(PaddingLeft, referenceY, plotRight, referenceY, colors.Reference, 1.5, hover: new DrawingHover($"{labels.Reference}: {canvas.Percent(chart.ReferenceRate)}", (PaddingLeft + plotRight) / 2, referenceY));

        List<(double X, double Y)> Points(IEnumerable<int> indices) => [.. indices.Select(i => (centre(i), y(months[i].Growth!.Value)))];
        var actual = Enumerable.Range(0, chart.LastActual + 1).Where(i => months[i].Growth is not null).ToList();
        canvas.Polyline(Points(actual), current.Stroke, 1.5);
        foreach (var i in actual)
        {
            canvas.HoverPoint(centre(i), y(months[i].Growth!.Value), $"{months[i].Label} {labels.Current} vs {labels.Previous}: {canvas.Percent(months[i].Growth!.Value)}", current.Stroke);
        }

        var projection = actual.Count > 0 ? new List<int> { actual[^1] } : [];
        projection.AddRange(Enumerable.Range(chart.LastActual + 1, months.Count - chart.LastActual - 1).Where(i => months[i].Growth is not null));
        if (projection.Count > 1)
        {
            canvas.Polyline(Points(projection), current.Stroke, 1.5, FinancialCanvas.ProjectionDash);
            foreach (var i in projection.Skip(1))
            {
                canvas.HoverPoint(centre(i), y(months[i].Growth!.Value), $"{months[i].Label} {labels.Projection} vs {labels.Previous}: {canvas.Percent(months[i].Growth!.Value)}", current.Stroke);
            }
        }

        var ends = new List<(double Y, string Text, string Color)> { (referenceY, $"{labels.Reference} {canvas.Percent(chart.ReferenceRate)}", colors.Reference) };
        if (actual.Count > 0)
        {
            var rate = months[actual[^1]].Growth!.Value;
            ends.Add((y(rate), $"{labels.Current} {canvas.Percent(rate)}", current.Stroke));
        }

        if (projection.Count > 1 && months[^1].Growth is { } yearEnd)
        {
            ends.Add((y(yearEnd), $"{labels.Projection} {canvas.Percent(yearEnd)}", current.Stroke));
        }

        canvas.EndLabels(ends, plotRight + 5, bottom);
    }
}
