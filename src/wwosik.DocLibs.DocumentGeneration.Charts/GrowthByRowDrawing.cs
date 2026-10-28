namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A <see cref="GrowthByRowChart"/>: horizontal bars diverging from zero. The change is the only measure, so it takes
/// series 1; the reference bar and both overall lines are neutral reference furniture, never a series colour.
/// </summary>
internal static class GrowthByRowDrawing
{
    private const double PaddingBottom = 20;
    private const double PaddingLeft = 122;
    private const double PaddingRight = 8;
    private const double RowHeight = 34;
    private const double BarHeight = 17;
    private const double LegendY = 50;

    // The figures live in columns to the right of the plot, not beside the bars they belong to: four numbers a row
    // printed against the bars left it unclear which figure was which. The plot keeps the shapes, the table the figures.
    private const double ColumnWidth = 38;
    private const double ColumnGap = 12;

    // The column headers sit above the overall labels, which stay on their dashed lines.
    private const double PaddingTop = 100;
    private const double ColumnHeaderY = PaddingTop - 32;

    public static Drawing Draw(GrowthByRowChart chart, FinancialCanvas canvas)
    {
        var colors = canvas.Colors;
        var labels = canvas.Labels;
        var series = colors.Series[0];
        var rows = chart.Rows;
        var width = canvas.Width;

        // Two columns, in the order they are read: what happened, and what was meant to happen. The first is read
        // against the second, so it carries that verdict in its colour.
        var columns = new List<(string Header, Func<GrowthRow, string> Value, Func<GrowthRow, string> Color)>
        {
            (labels.Change, row => canvas.Percent(row.Change), row => canvas.Verdict(row.Change, row.Reference)),
            (labels.Reference, row => canvas.Percent(row.Reference), _ => colors.Reference),
        };

        var plotHeight = Math.Max(1, rows.Count) * RowHeight;
        var height = PaddingTop + plotHeight + PaddingBottom;
        var plotWidth = width - PaddingLeft - PaddingRight - ColumnGap - (columns.Count * ColumnWidth);
        var columnsLeft = PaddingLeft + plotWidth + ColumnGap;
        double ColumnRight(int column) => columnsLeft + ((column + 1) * ColumnWidth) - 4;

        var max = rows.Count == 0 ? 1 : Math.Max(0.0001, rows.Max(r => Math.Max(Math.Abs(r.Change), Math.Abs(r.Reference))));
        var min = rows.Count == 0 ? 0 : Math.Min(0, rows.Min(r => Math.Min(r.Change, r.Reference)));
        max = Math.Max(max, Math.Max(Math.Abs(chart.Overall.Change), Math.Abs(chart.Overall.Reference)));
        min = Math.Min(min, Math.Min(chart.Overall.Change, chart.Overall.Reference));
        var range = max - min <= 0 ? 1 : max - min;
        double X(double v) => PaddingLeft + ((v - min) / range * plotWidth);
        var zeroX = X(0);

        canvas.Heading(chart, string.Empty);

        // The legend is anchored to the left edge rather than to the plot, so narrowing the chart does not push its
        // last item off the canvas.
        var legendX = 20.0;
        void Legend(Action<double> swatch, string text)
        {
            swatch(legendX);
            canvas.Text(legendX + 17, LegendY, text, 11, colors.Text);
            legendX += 30 + FinancialCanvas.TextWidth(text, 11);
        }

        Legend(x => canvas.Rect(x, LegendY - 10, 12, 12, series.Fill, series.Stroke, 0.75), labels.Change);
        Legend(x => canvas.Rect(x, LegendY - 10, 12, 12, null, colors.Reference, 2), labels.Reference);
        Legend(x => canvas.Line(x, LegendY - 4, x + 12, LegendY - 4, colors.Reference, 1.5, FinancialCanvas.ReferenceDash), $"{labels.Overall} {labels.Reference}");
        Legend(x => canvas.Line(x, LegendY - 4, x + 12, LegendY - 4, series.Stroke, 1.5, FinancialCanvas.ReferenceDash), $"{labels.Overall} {labels.Change}");

        canvas.Line(zeroX, PaddingTop, zeroX, PaddingTop + plotHeight, colors.Axis, 1);

        // Nothing is drawn between the table and the plot: the gap is what separates them, and a rule there read as
        // an axis the bars were measured against.
        for (var c = 0; c < columns.Count; c++)
        {
            canvas.Text(ColumnRight(c), ColumnHeaderY, columns[c].Header, 9, colors.Muted, TextAnchor.End);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var centre = PaddingTop + (RowHeight * i) + (RowHeight / 2);
            var top = centre - (BarHeight / 2);
            var (changeX, referenceX) = (X(row.Change), X(row.Reference));
            var changeLeft = Math.Min(changeX, zeroX);
            var changeWidth = Math.Max(Math.Abs(changeX - zeroX), 0.5);
            var referenceLeft = Math.Min(referenceX, zeroX);
            var referenceWidth = Math.Max(Math.Abs(referenceX - zeroX), 0.5);

            // On the one row the chart emphasises, the change takes the emphasis fill: still series 1, still the same
            // measure — one row picked out of the group, not a second quantity.
            var tooltip = $"{row.Label} {labels.Change}: {canvas.Percent(row.Change)}";
            canvas.Rect(changeLeft, top, changeWidth, BarHeight, row.Emphasised ? colors.Emphasis : series.Fill, series.Stroke, 0.75, hover: new DrawingHover(tooltip, changeLeft + (changeWidth / 2), top));
            canvas.Rect(referenceLeft, top, referenceWidth, BarHeight, null, colors.Reference, 2, hover: new DrawingHover($"{row.Label} {labels.Reference}: {canvas.Percent(row.Reference)}", referenceLeft + (referenceWidth / 2), top));

            var lines = FinancialCanvas.Wrap(row.Label, 17);
            var first = centre + 4 - ((lines.Count - 1) * 6.5);
            for (var l = 0; l < lines.Count; l++)
            {
                canvas.Text(PaddingLeft - 8, first + (l * 13), lines[l], 11, colors.Text, TextAnchor.End, row.Emphasised);
            }

            for (var c = 0; c < columns.Count; c++)
            {
                canvas.Text(ColumnRight(c), centre + 3.5, columns[c].Value(row), 10, columns[c].Color(row), TextAnchor.End);
            }
        }

        // The two overall labels stay on their dashed lines, centred over them — but a line close to an edge would push
        // its label off the canvas or into the table, so the text slides back inside the plot while the line stays
        // where the value is.
        double LabelX(double lineX, string text)
        {
            var half = FinancialCanvas.TextWidth(text, 10) / 2;
            return Math.Clamp(lineX, PaddingLeft + half, Math.Max(PaddingLeft + half, PaddingLeft + plotWidth - half));
        }

        var overallReferenceX = X(chart.Overall.Reference);
        var referenceLabel = $"{labels.Overall} {labels.Reference} {canvas.Percent(chart.Overall.Reference)}";
        canvas.Line(overallReferenceX, PaddingTop, overallReferenceX, PaddingTop + plotHeight, colors.Reference, 1.5, FinancialCanvas.ReferenceDash, new DrawingHover(referenceLabel, overallReferenceX, PaddingTop));
        canvas.Text(LabelX(overallReferenceX, referenceLabel), PaddingTop - 6, referenceLabel, 10, colors.Reference, TextAnchor.Middle);

        var overallChangeX = X(chart.Overall.Change);
        var changeLabel = $"{labels.Overall} {labels.Change} {canvas.Percent(chart.Overall.Change)}";
        canvas.Line(overallChangeX, PaddingTop, overallChangeX, PaddingTop + plotHeight, series.Stroke, 1.5, FinancialCanvas.ReferenceDash, new DrawingHover(changeLabel, overallChangeX, PaddingTop + 14));
        canvas.Text(LabelX(overallChangeX, changeLabel), PaddingTop - 20, changeLabel, 10, series.Stroke, TextAnchor.Middle);

        return canvas.Finish(height);
    }
}
