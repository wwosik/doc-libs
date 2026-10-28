namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A <see cref="GrowthTrendChart"/>: one panel, one line — the growth rate at every month against the flat annual
/// reference rate. The rate is a fraction, written as a percentage, so this panel has no scale or unit.
/// </summary>
internal static class GrowthTrendDrawing
{
    private const double PaddingLeft = 52;

    // Room for the end labels, one per line the panel draws: at least this, and as much as the longest needs.
    private const double MinPaddingRight = 96;
    private const double PlotHeight = 220;
    private const double PlotTop = 74;

    public static Drawing Draw(GrowthTrendChart chart, FinancialCanvas canvas)
    {
        var colors = canvas.Colors;
        var labels = canvas.Labels;
        var months = chart.Months;
        if (months.Count == 0)
        {
            throw new ArgumentException("A growth-trend chart needs at least one month.");
        }

        var current = colors.Series[0];
        var width = canvas.Width;
        var lastActual = Math.Clamp(chart.LastActual, -1, months.Count - 1);
        var bottom = PlotTop + PlotHeight;
        var endTexts = months.Where(m => m.Change is not null).Select(m => m.Change!.Value)
            .SelectMany(rate => new[] { $"{labels.Current} {canvas.Percent(rate)}", $"{labels.Projection} {canvas.Percent(rate)}" })
            .Append($"{labels.Reference} {canvas.Percent(chart.Reference)}");
        var plotWidth = width - PaddingLeft - Math.Max(MinPaddingRight, endTexts.Max(text => FinancialCanvas.TextWidth(text, 9)) + 10);
        var right = PaddingLeft + plotWidth;
        var slot = plotWidth / months.Count;
        double Centre(int index) => PaddingLeft + ((index + 0.5) * slot);

        // The axis is shaped by everything the panel draws — the drawn rates and the reference — and nothing it does not.
        var drawn = months.Where(m => m.Change is not null).Select(m => m.Change!.Value).Append(chart.Reference).ToList();
        var axis = FinancialCanvas.Axis(drawn.Min(), drawn.Max());
        double Y(double v) => axis.Y(v, PlotTop, PlotHeight);

        var actual = Enumerable.Range(0, lastActual + 1).Where(i => months[i].Change is not null).ToList();
        var projection = actual.Count > 0 ? new List<int> { actual[^1] } : [];
        projection.AddRange(Enumerable.Range(lastActual + 1, months.Count - lastActual - 1).Where(i => months[i].Change is not null));

        canvas.Heading(chart, $"Cumulative growth against {labels.Previous} by month, against {labels.Reference}.");

        // The actual line, its dashed projection where there is one, and the reference, which needs no dash: it is one
        // flat rate for the whole year, not a projection.
        var legendX = 20.0;
        void Legend(string stroke, IReadOnlyList<double>? dash, string text)
        {
            canvas.Line(legendX, 57, legendX + 16, 57, stroke, 2.5, dash);
            canvas.Text(legendX + 21, 60.5, text, 10, colors.Text);
            legendX += 27 + FinancialCanvas.TextWidth(text, 10);
        }

        Legend(current.Stroke, null, $"{labels.Current} vs {labels.Previous}");
        if (projection.Count > 1)
        {
            Legend(current.Stroke, FinancialCanvas.ProjectionDash, labels.Projection);
        }

        Legend(colors.Reference, null, $"{labels.Reference} ({canvas.SignedPercent(chart.Reference)})");

        canvas.Gridlines(axis, Y, PaddingLeft, right, canvas.Percent);

        var referenceY = Y(chart.Reference);
        canvas.Line(PaddingLeft, referenceY, right, referenceY, colors.Reference, 1.5, hover: new DrawingHover($"{labels.Reference}: {canvas.Percent(chart.Reference)}", PaddingLeft + (plotWidth / 2), referenceY));

        // Solid through the last actual, then a dashed continuation from the last actual point, so the two segments
        // meet rather than jump.
        List<(double X, double Y)> Points(IEnumerable<int> indices) => [.. indices.Select(i => (Centre(i), Y(months[i].Change!.Value)))];
        canvas.Polyline(Points(actual), current.Stroke, 1.5);
        foreach (var i in actual)
        {
            canvas.HoverPoint(Centre(i), Y(months[i].Change!.Value), $"{months[i].Label} {labels.Current} vs {labels.Previous}: {canvas.Percent(months[i].Change!.Value)}", current.Stroke);
        }

        if (projection.Count > 1)
        {
            canvas.Polyline(Points(projection), current.Stroke, 1.5, FinancialCanvas.ProjectionDash);
            foreach (var i in projection.Skip(1))
            {
                canvas.HoverPoint(Centre(i), Y(months[i].Change!.Value), $"{months[i].Label} {labels.Projection}: {canvas.Percent(months[i].Change!.Value)}", current.Stroke);
            }
        }

        for (var i = 0; i < months.Count; i++)
        {
            canvas.Text(Centre(i), bottom + 14, months[i].Label, 9, colors.Text, TextAnchor.Middle, maxWidth: slot);
        }

        var ends = new List<(double Y, string Text, string Color)>();
        if (actual.Count > 0)
        {
            var rate = months[actual[^1]].Change!.Value;
            ends.Add((Y(rate), $"{labels.Current} {canvas.Percent(rate)}", current.Stroke));
        }

        if (projection.Count > 1 && months[^1].Change is { } yearEnd)
        {
            ends.Add((Y(yearEnd), $"{labels.Projection} {canvas.Percent(yearEnd)}", current.Stroke));
        }

        ends.Add((referenceY, $"{labels.Reference} {canvas.Percent(chart.Reference)}", colors.Reference));
        canvas.EndLabels(ends, right + 5, bottom + 4);
        return canvas.Finish(bottom + 40);
    }
}
