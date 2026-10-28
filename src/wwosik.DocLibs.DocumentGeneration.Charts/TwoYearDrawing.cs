namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A <see cref="TwoYearChart"/>: one panel, two years of month slots, one line per category. Colour is the only thing
/// telling the lines apart, so the legend is not optional here, and nothing else on the plot is allowed a hue: the
/// gridlines, the year rule and the axes stay grey.
/// </summary>
internal static class TwoYearDrawing
{
    public const int MaxCategories = 6;

    private const double PaddingLeft = 52;

    // Nothing is written past the plot: the lines end where the months do, and the legend identifies them.
    private const double PaddingRight = 16;

    // Taller than a panel of the development chart: six lines over one axis need the room to stay apart.
    private const double PlotHeight = 250;

    private const double LegendTop = 62;
    private const double LegendRow = 16;
    private const double Swatch = 16;

    public static Drawing Draw(TwoYearChart chart, FinancialCanvas canvas)
    {
        var colors = canvas.Colors;
        var width = canvas.Width;
        var slots = chart.Months.Count;
        if (slots == 0)
        {
            throw new ArgumentException("A two-year chart needs at least one month.");
        }

        if (chart.Series.FirstOrDefault(s => s.Values.Count != slots) is { } uneven)
        {
            throw new ArgumentException($"The category '{uneven.Label}' has {uneven.Values.Count} values for {slots} months.");
        }

        var hues = Colours(chart, colors);
        var plotWidth = width - PaddingLeft - PaddingRight;
        var right = PaddingLeft + plotWidth;
        var slot = plotWidth / slots;
        double Centre(int index) => PaddingLeft + ((index + 0.5) * slot);

        canvas.Heading(chart, $"Month by month, {chart.PreviousYear} and {chart.CurrentYear}.");

        // The legend first, because how many rows it takes decides where the plot starts: its entries flow left to
        // right and wrap at the chart's width.
        var rows = 1;
        var x = 20.0;
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var label = chart.Series[s].Label;
            var entry = Swatch + 5 + FinancialCanvas.TextWidth(label, 10);
            if (x > 20 && x + entry > width - 20)
            {
                rows++;
                x = 20;
            }

            var y = LegendTop + ((rows - 1) * LegendRow);
            canvas.Line(x, y, x + Swatch, y, hues[s], 2.5, round: true);
            canvas.Text(x + Swatch + 5, y + 3.5, label, 10, colors.Text);
            x += entry + 14;
        }

        var top = LegendTop + (rows * LegendRow) + 14;
        var bottom = top + PlotHeight;
        var drawn = chart.Series.SelectMany(s => s.Values).OfType<double>().ToList();
        var axis = FinancialCanvas.Axis(drawn.Count == 0 ? 0 : drawn.Min(), drawn.Count == 0 ? 1 : drawn.Max());
        double Y(double v) => axis.Y(v, top, PlotHeight);

        // With monthly figures a category can be negative — a return booked in another month than its sale — so the
        // line the lines cross is drawn as the axis it is.
        canvas.Gridlines(axis, Y, PaddingLeft, right, canvas.Value);

        // Where one year ends and the next begins: a rule and not a gap, because December to January is one step of
        // the same series and the shape of that step is part of what the chart is for.
        var half = slots / 2;
        var boundary = PaddingLeft + (half * slot);
        canvas.Line(boundary, top, boundary, bottom, colors.Gridline, 1.5, FinancialCanvas.BoundaryDash);

        // A run of consecutive months is one line, so the months this year has not reached leave the line ending
        // rather than dropping to the axis. A single month between gaps would draw nothing as a line, so it is a dot.
        for (var s = 0; s < chart.Series.Count; s++)
        {
            var series = chart.Series[s];
            var run = new List<(int Index, double Value)>();
            void Flush()
            {
                if (run.Count > 1)
                {
                    canvas.Polyline([.. run.Select(p => (Centre(p.Index), Y(p.Value)))], hues[s], 1.6, round: true);
                }
                else if (run.Count == 1)
                {
                    canvas.Dot(Centre(run[0].Index), Y(run[0].Value), 1.8, hues[s]);
                }

                foreach (var (index, v) in run)
                {
                    canvas.HoverPoint(Centre(index), Y(v), $"{chart.Months[index]} {series.Label}: {canvas.Value(v)}", hues[s]);
                }

                run.Clear();
            }

            for (var i = 0; i < slots; i++)
            {
                if (series.Values[i] is { } v)
                {
                    run.Add((i, v));
                }
                else
                {
                    Flush();
                }
            }

            Flush();
        }

        // The month labels, and under them the year each half belongs to: the months repeat, so the year says which
        // January is which.
        for (var i = 0; i < slots; i++)
        {
            canvas.Text(Centre(i), bottom + 13, chart.Months[i], 8, colors.Text, TextAnchor.Middle, maxWidth: slot);
        }

        canvas.Text(PaddingLeft + (half * slot / 2), bottom + 30, chart.PreviousYear, 10, colors.Muted, TextAnchor.Middle, bold: true);
        canvas.Text(boundary + ((slots - half) * slot / 2), bottom + 30, chart.CurrentYear, 10, colors.Muted, TextAnchor.Middle, bold: true);
        return canvas.Finish(bottom + 42);
    }

    /// <summary>
    /// Which colour each category is drawn in: the category colours handed out in order of how high its line runs,
    /// highest first.
    /// </summary>
    /// <remarks>
    /// Hue goes by where a line sits, not by where its category is listed, because what a reader has to separate is two
    /// lines near each other: two categories of a similar size run over one another for the whole chart, while one at
    /// the top and one at the bottom never meet. The category colours are ordered so that consecutive ones differ most,
    /// so ranking by height puts the strongest difference exactly where the lines are closest. A category's colour is
    /// therefore a property of the chart it is on, and one category overtaking another swaps their hues — the legend
    /// names every line, and telling neighbouring lines apart is the thing worth optimising for.
    /// </remarks>
    public static IReadOnlyList<string> Colours(TwoYearChart chart, ResolvedChartColors colors)
    {
        if (chart.Series.Count > MaxCategories)
        {
            throw new ArgumentException($"A two-year chart draws at most {MaxCategories} categories; this one has {chart.Series.Count}.");
        }

        // The mean of what is drawn: a category with fewer months is ranked by its own, not pushed down by the months
        // it does not have.
        static double Height(TwoYearSeries series) => series.Values.OfType<double>().DefaultIfEmpty(0).Average();

        var hues = new string[chart.Series.Count];
        var rank = 0;
        foreach (var index in Enumerable.Range(0, chart.Series.Count).OrderByDescending(i => Height(chart.Series[i])).ThenBy(i => i))
        {
            hues[index] = colors.Categories[rank++ % colors.Categories.Count];
        }

        return hues;
    }
}
