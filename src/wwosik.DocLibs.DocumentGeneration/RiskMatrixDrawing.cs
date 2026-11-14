using System.Globalization;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>::: riskmatrix</c>: one pipe table of 1 to 15 risks, the first three columns by position risk, likelihood and
/// impact (each 1 to 5; headers free, further columns ignored), drawn as a 5 × 5 grid, likelihood up and impact right.
/// Each cell is tinted by its score, likelihood × impact: 1–4 in the success colour, 5–9 in the warning colour, 10–25 in
/// the danger colour. Each risk is a numbered dot in its cell, and a numbered legend beside the grid names them.
/// </summary>
internal static class RiskMatrixDrawing
{
    /// <summary>The most risks.</summary>
    public const int MaxRisks = 15;

    /// <summary>The width in pixels.</summary>
    public const double Width = 960;

    /// <summary>The highest score in the success band and in the warning band; above is the danger band.</summary>
    public const int LowMax = 4;

    /// <inheritdoc cref="LowMax"/>
    public const int MediumMax = 9;

    private const int Levels = 5;
    private const double Cell = 72;
    private const double GridLeft = 60;
    private const double GridTop = 34;
    private const double GridSize = Levels * Cell;
    private const double LegendLeft = GridLeft + GridSize + 40;
    private const double LegendDot = 11;
    private const double LegendText = LegendLeft + 32;
    private const double TitleSize = 15;
    private const double ValueSize = 14;
    private const double NameSize = 15;
    private const double LineStep = 20;
    private const int NameLines = 2;
    private const string White = "FFFFFF";

    // How pale a cell is: the band's colour at this share on white, as status labels tint table cells.
    private const double Tint = 0.22;

    private sealed record Risk(List<DrawingSpan> Name, int Likelihood, int Impact);

    /// <summary>The band of a score: 0 success (1–4), 1 warning (5–9), 2 danger (10–25).</summary>
    public static int Band(int score) => score <= LowMax ? 0 : score <= MediumMax ? 1 : 2;

    /// <summary>Draws the container's table.</summary>
    /// <exception cref="DocumentConversionException">Not one table of 1 to 15 rows with likelihood and impact from 1 to 5.</exception>
    public static Drawing Draw(CustomContainer container, VisualContext context)
    {
        var risks = Risks(container, context);
        var colors = context.Brand.Diagram;
        var labels = context.Brand.Labels;
        string BandColour(int likelihood, int impact) => Band(likelihood * impact) switch
        {
            0 => labels.Success,
            1 => labels.Warning,
            _ => labels.Danger,
        };
        double CellLeft(int impact) => GridLeft + ((impact - 1) * Cell);
        double CellTop(int likelihood) => GridTop + ((Levels - likelihood) * Cell);

        var items = new List<DrawingItem>
        {
            new DrawingText(GridLeft, 18, context.Texts.Likelihood, TitleSize, colors.Text, bold: true) { MaxWidth = Width - GridLeft },
        };

        // The cells, a little apart so the background shows between them; the values along both axes.
        for (var likelihood = 1; likelihood <= Levels; likelihood++)
        {
            for (var impact = 1; impact <= Levels; impact++)
            {
                items.Add(new DrawingShape(DrawingPath.Rectangle(CellLeft(impact) + 1.5, CellTop(likelihood) + 1.5, Cell - 3, Cell - 3), ResolvedBrand.Mix(BandColour(likelihood, impact), Tint), null, 0)
                {
                    Kind = DrawingShapeKind.Rectangle,
                });
            }

            items.Add(new DrawingText(GridLeft - 12, CellTop(likelihood) + (Cell / 2) + (ValueSize * 0.35), Number(likelihood), ValueSize, colors.Text) { Anchor = TextAnchor.End });
            items.Add(new DrawingText(CellLeft(likelihood) + (Cell / 2), GridTop + GridSize + 20, Number(likelihood), ValueSize, colors.Text) { Anchor = TextAnchor.Middle });
        }

        items.Add(new DrawingText(GridLeft + (GridSize / 2), GridTop + GridSize + 44, context.Texts.Impact, TitleSize, colors.Text, bold: true)
        {
            Anchor = TextAnchor.Middle,
            MaxWidth = GridSize + GridLeft,
        });

        // The dots, in rows of up to three (four when a cell holds more than nine), in the order the risks are written.
        foreach (var group in risks.Select((risk, i) => (Risk: risk, Number: i + 1)).GroupBy(r => (r.Risk.Likelihood, r.Risk.Impact)))
        {
            var members = group.ToList();
            var perRow = members.Count <= 3 ? members.Count : members.Count <= 9 ? 3 : 4;
            var rows = (int)Math.Ceiling(members.Count / (double)perRow);
            var step = Math.Min(24, (Cell - 6) / Math.Max(perRow, rows));
            var radius = Math.Min(11, (step / 2) - 1);
            var (left, top) = (CellLeft(group.Key.Impact), CellTop(group.Key.Likelihood));
            for (var i = 0; i < members.Count; i++)
            {
                var (row, column) = (i / perRow, i % perRow);
                var inRow = Math.Min(perRow, members.Count - (row * perRow));
                var x = left + (Cell / 2) + ((column - ((inRow - 1) / 2.0)) * step);
                var y = top + (Cell / 2) + ((row - ((rows - 1) / 2.0)) * step);
                items.AddRange(Dot(x, y, radius, members[i].Number, BandColour(group.Key.Likelihood, group.Key.Impact)));
            }
        }

        // The legend: each risk's number on its dot, and its name.
        var baseline = GridTop + 4;
        for (var i = 0; i < risks.Count; i++)
        {
            var name = DrawingTexts.Lines(risks[i].Name, NameSize, Width - LegendText - 4, NameLines);
            items.AddRange(Dot(LegendLeft + LegendDot, baseline - (NameSize * 0.35), LegendDot, i + 1, BandColour(risks[i].Likelihood, risks[i].Impact)));
            foreach (var line in name)
            {
                items.Add(new DrawingText(LegendText, baseline, line, NameSize, colors.Text) { MaxWidth = Width - LegendText - 4 });
                baseline += LineStep;
            }

            baseline += 8;
        }

        var height = Math.Max(GridTop + GridSize + 52, baseline);
        return new Drawing(Width, height, items);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<DrawingItem> Dot(double x, double y, double radius, int number, string fill)
    {
        var size = Math.Round(radius * 1.1, 1);
        yield return new DrawingShape(TimelineDrawing.Ellipse(x, y, radius), fill, White, 1) { Kind = DrawingShapeKind.Ellipse };
        yield return new DrawingText(x, y + (size * 0.35), Number(number), size, White, bold: true) { Anchor = TextAnchor.Middle, MaxWidth = (radius * 2) - 2 };
    }

    private static List<Risk> Risks(CustomContainer container, VisualContext context)
    {
        var at = VisualContainers.At(container, context);
        var usage = "It holds one table whose first three columns are the risk, its likelihood and its impact from 1 to 5, such as '| Supplier delay | 4 | 3 |'.";
        if (container.Count != 1 || container[0] is not Table table)
        {
            throw context.Error($"The '::: riskmatrix'{at} holds {(container.Count == 0 ? "nothing" : "something else than one table")}. {usage}");
        }

        var rows = table.OfType<TableRow>().Where(r => !r.IsHeader).ToList();
        if (rows.Count is 0 or > MaxRisks)
        {
            throw context.Error($"The '::: riskmatrix'{at} has {rows.Count} rows; it takes 1 to {MaxRisks}. Split a longer list into several matrices.");
        }

        var risks = new List<Risk>();
        foreach (var row in rows)
        {
            var rowAt = VisualContainers.At(row, context);
            var cells = row.OfType<TableCell>().ToList();
            var name = Column(cells, 0);
            var spans = name?.Inline is { } inline ? DrawingTexts.Trim(DrawingTexts.Spans(inline.FirstChild)) : [];
            if (spans.Count == 0)
            {
                throw context.Error($"The risk-matrix row{rowAt} has no risk in its first column. {usage}");
            }

            risks.Add(new Risk(spans, Level(Column(cells, 1), "likelihood", rowAt, context), Level(Column(cells, 2), "impact", rowAt, context)));
        }

        return risks;
    }

    private static ParagraphBlock? Column(List<TableCell> cells, int index) =>
        index < cells.Count ? cells[index].OfType<ParagraphBlock>().FirstOrDefault() : null;

    private static int Level(ParagraphBlock? cell, string what, string at, VisualContext context)
    {
        var written = DrawingTexts.Plain(cell).Trim();
        return written.Length == 1 && written[0] is >= '1' and <= '5'
            ? written[0] - '0'
            : throw context.Error(written.Length == 0
                ? $"The risk-matrix row{at} has no {what}; write a whole number from 1 to 5."
                : $"The risk-matrix row{at} has the {what} '{DrawingTexts.Quote(written)}'; write a whole number from 1 to 5.");
    }
}
