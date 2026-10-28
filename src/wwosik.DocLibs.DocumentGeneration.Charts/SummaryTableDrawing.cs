namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// A <see cref="SummaryTable"/>: no plot and no axis — a header band and a grid of figures, right-aligned so that a
/// column reads down as one comparison. The only colour is the verdict of the comparison rows.
/// </summary>
internal static class SummaryTableDrawing
{
    private const double PaddingLeft = 20;
    private const double PaddingRight = 20;
    private const double LabelWidth = 170;
    private const double ColumnWidth = 88;
    private const double CellPadding = 10;

    // What separates the total from the columns it totals, and one metric's block from the next.
    private const double TotalGap = 24;
    private const double BlockGap = 18;

    private const double HeaderTop = 62;
    private const double HeaderHeight = 24;
    private const double BlockLabelHeight = 22;
    private const double RowHeight = 22;

    private enum Row
    {
        Value,
        Change,
        Gap,
        Rate,
        PreviousRate,
        RatePoints,
        RateGap,
    }

    public static Drawing Draw(SummaryTable table, FinancialCanvas canvas)
    {
        var colors = canvas.Colors;
        var labels = canvas.Labels;
        var definition = table.Columns;
        if (definition.Count == 0)
        {
            throw new ArgumentException("A summary table needs at least one column.");
        }

        if (table.Blocks.FirstOrDefault(b => b.Cells.Count != definition.Count) is { } uneven)
        {
            throw new ArgumentException($"The block '{uneven.Metric}' has {uneven.Cells.Count} cells for {definition.Count} columns.");
        }

        // What the value row draws as 0,0: half of the last digit it prints.
        var roundsToZero = canvas.ScaleFactor * 0.05;

        // Rounds to nothing at the table's own scale. This decides whether a rest column is drawn at all, and it is
        // deliberately not the test below: fold the two together and a rest column that is merely small would vanish
        // rather than show its figure.
        bool Nothing(SummaryCell cell) => Math.Abs(cell.Current) < roundsToZero && Math.Abs(cell.Previous) < roundsToZero;

        // Whether a cell states its rates and differences at all. Only a rest column can be a negligible remainder:
        // only a total is a thing to be a remainder of.
        bool Silent(SummaryBlock block, int column)
        {
            if (Nothing(block.Cells[column]))
            {
                return true;
            }

            if (!definition[column].Rest)
            {
                return false;
            }

            var total = Enumerable.Range(0, definition.Count).Where(i => definition[i].Total).Select(i => Math.Abs(block.Cells[i].Current)).FirstOrDefault();
            return total > 0 && Math.Abs(block.Cells[column].Current) < total * canvas.Options.RestShare;
        }

        // The rest column earns its place only when it has something in it: it exists so that the columns add up, and
        // where everything adds up without it, a column of zeroes is one more thing to read and nothing to learn. Every
        // other column is drawn whatever it holds — a category that sold nothing is a fact the reader is looking for.
        var drawn = Enumerable.Range(0, definition.Count)
            .Where(i => !definition[i].Rest || table.Blocks.Any(b => !Nothing(b.Cells[i])))
            .ToList();
        var columns = drawn.Select(i => definition[i]).ToList();
        var lefts = new double[columns.Count];
        var x = PaddingLeft + LabelWidth;
        for (var i = 0; i < columns.Count; i++)
        {
            lefts[i] = x;
            x += ColumnWidth + (columns[i].Total ? TotalGap : 0);
        }

        canvas.Width = lefts[^1] + ColumnWidth + PaddingRight;
        var width = canvas.Width;
        double Right(int column) => lefts[column] + ColumnWidth - CellPadding;

        // The rows of one block in the order they are drawn, and which reading each holds: a rate block reads down from
        // the two shares to the two distances between them; an amount block has no shares to compare.
        var unit = canvas.Options.Unit.Length == 0 ? string.Empty : $" ({canvas.Options.Unit})";
        List<(Row Kind, string Label)> RowsOf(SummaryBlock block) => block.RateOf is not { } of
            ? [(Row.Value, labels.Value + unit), (Row.Change, $"vs {labels.Previous}"), (Row.Gap, $"vs {labels.Reference} ({canvas.SignedPercent(block.Reference)})")]
            : [
                (Row.Value, labels.Value + unit),
                (Row.Rate, $"{labels.Current} % of {of}"),
                (Row.PreviousRate, $"{labels.Previous} % of {of}"),
                (Row.RatePoints, $"vs {labels.Previous}"),

                // The reference is points on the share, so the label says so: '+1,2pp', never '+1,2%', which in a block
                // of percentages would read as a growth rate.
                (Row.RateGap, $"vs {labels.Reference} ({canvas.SignedPoints(block.Reference)})"),
            ];

        var rows = table.Blocks.Select(RowsOf).ToList();
        var heights = rows.Select(r => BlockLabelHeight + (r.Count * RowHeight)).ToList();
        var height = HeaderTop + HeaderHeight + heights.Sum() + (Math.Max(0, table.Blocks.Count - 1) * BlockGap) + 18;

        canvas.Heading(table, $"Year to date: change against {labels.Previous}, and how far it is from {labels.Reference}.", PaddingLeft, 43);

        // The total column takes the darker header, so which column is the whole and which are its parts is read
        // before any figure is.
        for (var i = 0; i < columns.Count; i++)
        {
            canvas.Rect(lefts[i], HeaderTop, ColumnWidth, HeaderHeight, columns[i].Total ? colors.TotalHeader : colors.TableHeader, null, 0);
            canvas.Text(Right(i), HeaderTop + 16, columns[i].Label, 10, "FFFFFF", TextAnchor.End, true, ColumnWidth - (2 * CellPadding));
        }

        // A share on its own, drawn plain; a column with no denominator has no share to state.
        (string Text, string Color) Level(double? share) => share is { } s ? (canvas.Rate(s), colors.Text) : ("–", colors.Muted);

        // A distance between two shares, coloured by the same rule as every other comparison on the table.
        (string Text, string Color) Points(double? share, double? against) =>
            share is { } s && against is { } a ? (canvas.SignedPoints(s - a), canvas.Verdict(s, a)) : ("–", colors.Muted);

        var blockTop = HeaderTop + HeaderHeight;
        for (var b = 0; b < table.Blocks.Count; b++)
        {
            var block = table.Blocks[b];
            var rowsTop = blockTop + BlockLabelHeight;
            var blockRows = rows[b];

            // The pale fill behind the total's figures, for the same reason its header is darker.
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Total)
                {
                    canvas.Rect(lefts[i], rowsTop, ColumnWidth, blockRows.Count * RowHeight, colors.TotalFill, null, 0);
                }
            }

            canvas.Text(PaddingLeft, blockTop + 16, block.Metric, 12, colors.Text, bold: true, maxWidth: LabelWidth);
            for (var r = 0; r < blockRows.Count; r++)
            {
                var baseline = rowsTop + (r * RowHeight) + 15;
                canvas.Text(PaddingLeft + 12, baseline, blockRows[r].Label, 11, colors.Muted, maxWidth: LabelWidth - 14);
                for (var i = 0; i < columns.Count; i++)
                {
                    var cell = block.Cells[drawn[i]];

                    // A cell that states no differences is one whose figures round to zero at the table's scale, or a
                    // remainder too small to be a proportion of anything: a percentage between two such amounts is
                    // arithmetic rather than information, and in red or green it would be the loudest thing on the table.
                    // Levels are never coloured — an amount or a share on its own has no verdict.
                    var silent = Silent(block, drawn[i]);
                    var (text, color) = blockRows[r].Kind switch
                    {
                        Row.Value => (canvas.TableValue(cell.Current), colors.Text),
                        _ when silent => ("–", colors.Muted),
                        Row.Rate => Level(cell.Rate),
                        Row.PreviousRate => Level(cell.PreviousRate),
                        Row.RatePoints => Points(cell.Rate, cell.PreviousRate),
                        Row.RateGap => Points(cell.Rate, cell.ReferenceRate),
                        Row.Change => (canvas.SignedPercent(cell.Change), canvas.Verdict(cell.Change, 0)),

                        // An amount block's reference row is a distance between two growth rates — points, written with
                        // a % because every other figure in that block is one.
                        _ => (canvas.SignedPercent(cell.Change - cell.Reference), canvas.Verdict(cell.Change, cell.Reference)),
                    };
                    canvas.Text(Right(i), baseline, text, 11, color, TextAnchor.End, columns[i].Total);
                }

                var ruleY = rowsTop + ((r + 1) * RowHeight);
                canvas.Line(PaddingLeft, ruleY, width - PaddingRight, ruleY, colors.Gridline, 1);
            }

            blockTop += heights[b] + BlockGap;
            if (b < table.Blocks.Count - 1)
            {
                var divider = blockTop - (BlockGap / 2);
                canvas.Line(PaddingLeft, divider, width - PaddingRight, divider, colors.Axis, 1, [4, 3]);
            }
        }

        return canvas.Finish(height);
    }
}
