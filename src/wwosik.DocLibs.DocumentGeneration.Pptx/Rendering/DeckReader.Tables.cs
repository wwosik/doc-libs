using System.Globalization;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

internal sealed partial class DeckReader
{
    /// <summary>The id of the built-in <i>Medium Style 2 – Accent 1</i>, which PowerPoint knows without a definition in the package.</summary>
    internal const string MediumStyle2Accent1 = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";

    private const double DefaultMarginX = 91440;
    private const double DefaultMarginY = 45720;

    // PowerPoint's definition of Medium Style 2 – Accent 1, as it writes it into tableStyles.xml when the style is used.
    private static readonly XElement BuiltInStyle = XElement.Parse(
        "<a:tblStyle xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' styleId='" + MediumStyle2Accent1 + "' styleName='Medium Style 2 - Accent 1'>"
        + "<a:wholeTbl><a:tcTxStyle><a:fontRef idx='minor'><a:prstClr val='black'/></a:fontRef><a:schemeClr val='dk1'/></a:tcTxStyle><a:tcStyle><a:tcBdr>"
        + string.Concat(new[] { "left", "right", "top", "bottom", "insideH", "insideV" }.Select(side =>
            $"<a:{side}><a:ln w='12700' cmpd='sng'><a:solidFill><a:schemeClr val='lt1'/></a:solidFill></a:ln></a:{side}>"))
        + "</a:tcBdr><a:fill><a:solidFill><a:schemeClr val='accent1'><a:tint val='20000'/></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:wholeTbl>"
        + "<a:band1H><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val='accent1'><a:tint val='40000'/></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:band1H>"
        + "<a:band2H><a:tcStyle><a:tcBdr/></a:tcStyle></a:band2H>"
        + "<a:band1V><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val='accent1'><a:tint val='40000'/></a:schemeClr></a:solidFill></a:fill></a:tcStyle></a:band1V>"
        + "<a:band2V><a:tcStyle><a:tcBdr/></a:tcStyle></a:band2V>"
        + "<a:lastCol><a:tcTxStyle b='on'><a:fontRef idx='minor'><a:prstClr val='black'/></a:fontRef><a:schemeClr val='lt1'/></a:tcTxStyle><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val='accent1'/></a:solidFill></a:fill></a:tcStyle></a:lastCol>"
        + "<a:firstCol><a:tcTxStyle b='on'><a:fontRef idx='minor'><a:prstClr val='black'/></a:fontRef><a:schemeClr val='lt1'/></a:tcTxStyle><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:schemeClr val='accent1'/></a:solidFill></a:fill></a:tcStyle></a:firstCol>"
        + "<a:lastRow><a:tcTxStyle b='on'><a:fontRef idx='minor'><a:prstClr val='black'/></a:fontRef><a:schemeClr val='lt1'/></a:tcTxStyle><a:tcStyle><a:tcBdr><a:top><a:ln w='38100' cmpd='sng'><a:solidFill><a:schemeClr val='lt1'/></a:solidFill></a:ln></a:top></a:tcBdr><a:fill><a:solidFill><a:schemeClr val='accent1'/></a:solidFill></a:fill></a:tcStyle></a:lastRow>"
        + "<a:firstRow><a:tcTxStyle b='on'><a:fontRef idx='minor'><a:prstClr val='black'/></a:fontRef><a:schemeClr val='lt1'/></a:tcTxStyle><a:tcStyle><a:tcBdr><a:bottom><a:ln w='38100' cmpd='sng'><a:solidFill><a:schemeClr val='lt1'/></a:solidFill></a:ln></a:bottom></a:tcBdr><a:fill><a:solidFill><a:schemeClr val='accent1'/></a:solidFill></a:fill></a:tcStyle></a:firstRow>"
        + "</a:tblStyle>");

    private sealed record Cell(XElement Element, int Row, int Column, int RowSpan, int ColumnSpan);

    /// <summary>
    /// A table as shapes in the frame's coordinates: cell fills and text, then the borders, with the table style's parts
    /// applied for the <c>tblPr</c> flags, rows grown to fit their text.
    /// </summary>
    private List<SceneItem> Table(XElement table, SlideParts context, Matrix frame, double frameWidth)
    {
        var palette = context.Palette;
        var properties = table.Element(Ns.A + "tblPr");
        bool Flag(string name) => properties?.Attribute(name)?.Value is "1" or "true";
        var style = this.TableStyle(properties?.Element(Ns.A + "tableStyleId")?.Value?.Trim());
        var columns = table.Element(Ns.A + "tblGrid")?.Elements(Ns.A + "gridCol").Select(c => Palette.Number(c, "w")).ToList() ?? [];
        var rows = table.Elements(Ns.A + "tr").ToList();
        if (columns.Count == 0 || rows.Count == 0)
        {
            return [];
        }

        // Cells and their spans; merged-away cells are left out.
        var cells = new List<Cell>();
        for (var r = 0; r < rows.Count; r++)
        {
            var c = 0;
            foreach (var tc in rows[r].Elements(Ns.A + "tc"))
            {
                if (tc.Attribute("hMerge")?.Value is not ("1" or "true") && tc.Attribute("vMerge")?.Value is not ("1" or "true"))
                {
                    cells.Add(new Cell(tc, r, c, Math.Max(1, (int)Palette.Number(tc, "rowSpan")), Math.Max(1, (int)Palette.Number(tc, "gridSpan"))));
                }

                c++;
            }
        }

        var lastRow = rows.Count - 1;
        var lastColumn = columns.Count - 1;

        // The parts of the table style a cell takes, lowest precedence first.
        List<XElement> Parts(int row, int column)
        {
            var parts = new List<XElement?> { style?.Element(Ns.A + "wholeTbl") };
            var bandRow = row - (Flag("firstRow") ? 1 : 0);
            var bandColumn = column - (Flag("firstCol") ? 1 : 0);
            if (Flag("bandCol") && bandColumn >= 0)
            {
                parts.Add(style?.Element(Ns.A + (bandColumn % 2 == 0 ? "band1V" : "band2V")));
            }

            if (Flag("bandRow") && bandRow >= 0 && !(Flag("lastRow") && row == lastRow))
            {
                parts.Add(style?.Element(Ns.A + (bandRow % 2 == 0 ? "band1H" : "band2H")));
            }

            if (Flag("lastCol") && column == lastColumn)
            {
                parts.Add(style?.Element(Ns.A + "lastCol"));
            }

            if (Flag("firstCol") && column == 0)
            {
                parts.Add(style?.Element(Ns.A + "firstCol"));
            }

            if (Flag("lastRow") && row == lastRow)
            {
                parts.Add(style?.Element(Ns.A + "lastRow"));
            }

            if (Flag("firstRow") && row == 0)
            {
                parts.Add(style?.Element(Ns.A + "firstRow"));
            }

            return [.. parts.OfType<XElement>()];
        }

        // Text first, to grow rows to fit.
        var heights = rows.Select(r => Palette.Number(r, "h")).ToList();
        var texts = new Dictionary<Cell, (XElement? Body, TextSources Sources, double Width)>();
        foreach (var cell in cells)
        {
            var width = columns.Skip(cell.Column).Take(cell.ColumnSpan).Sum();
            var cellProperties = cell.Element.Element(Ns.A + "tcPr");
            var parts = Parts(cell.Row, cell.Column);
            var body = cell.Element.Element(Ns.A + "txBody");
            var margins = new XElement(
                Ns.A + "bodyPr",
                new XAttribute("lIns", Margin(cellProperties, "marL", DefaultMarginX)),
                new XAttribute("rIns", Margin(cellProperties, "marR", DefaultMarginX)),
                new XAttribute("tIns", Margin(cellProperties, "marT", DefaultMarginY)),
                new XAttribute("bIns", Margin(cellProperties, "marB", DefaultMarginY)),
                new XAttribute("anchor", cellProperties?.Attribute("anchor")?.Value ?? "t"));
            var sources = new TextSources([body?.Element(Ns.A + "lstStyle"), TextStyle(parts), this.defaultTextStyle], [margins]);
            texts[cell] = (body, sources, width);
            if (cell.RowSpan == 1 && context.Text.Layout(body, sources, width, heights[cell.Row]) is { } laid)
            {
                heights[cell.Row] = Math.Max(heights[cell.Row], laid.Height);
            }
        }

        var xs = new List<double> { 0 };
        foreach (var w in columns)
        {
            xs.Add(xs[^1] + w);
        }

        var ys = new List<double> { 0 };
        foreach (var h in heights)
        {
            ys.Add(ys[^1] + h);
        }

        var items = new List<SceneItem>();
        var borders = new List<SceneItem>();
        foreach (var cell in cells)
        {
            var (x, y) = (xs[cell.Column], ys[cell.Row]);
            var (w, h) = (xs[Math.Min(cell.Column + cell.ColumnSpan, columns.Count)] - x, ys[Math.Min(cell.Row + cell.RowSpan, rows.Count)] - y);
            var matrix = frame * Matrix.Translate(x, y);
            var cellProperties = cell.Element.Element(Ns.A + "tcPr");
            var parts = Parts(cell.Row, cell.Column);
            var fill = palette.Fill(cellProperties, null, null);
            if (fill is null)
            {
                foreach (var part in parts)
                {
                    var tcStyle = part.Element(Ns.A + "tcStyle");
                    fill = tcStyle?.Element(Ns.A + "fill") is { } own ? palette.Fill(own, null, null) ?? fill
                        : tcStyle?.Element(Ns.A + "fillRef") is { } reference ? palette.Fill(new XElement("x", palette.Theme.Style("fillStyleLst", (int)Palette.Number(reference, "idx"))), palette.Color(reference), null) ?? fill
                        : fill;
                }
            }

            var paths = this.Paths(null, w, h, fill, null);
            var (body, sources, _) = texts[cell];
            var text = context.Text.Layout(body, sources, w, h)?.Text;
            if (paths.Count > 0 || text is not null)
            {
                items.Add(new SceneShape(0, string.Empty, matrix, paths, text, matrix, null));
            }

            // Borders: the cell's own, else the style's for the edge the cell is on.
            var lastR = cell.Row + cell.RowSpan - 1 == lastRow;
            var lastC = cell.Column + cell.ColumnSpan - 1 == lastColumn;
            foreach (var (own, outer, inner, isOuter, x1, y1, x2, y2) in new[]
            {
                ("lnL", "left", "insideV", cell.Column == 0, 0.0, 0.0, 0.0, h),
                ("lnR", "right", "insideV", lastC, w, 0.0, w, h),
                ("lnT", "top", "insideH", cell.Row == 0, 0.0, 0.0, w, 0.0),
                ("lnB", "bottom", "insideH", lastR, 0.0, h, w, h),
            })
            {
                var line = cellProperties?.Element(Ns.A + own);
                if (line is null)
                {
                    foreach (var part in parts)
                    {
                        var region = part.Name.LocalName;
                        // A border is outer when it is on the edge of the part's region: a row for the first and last
                        // row, a column for the first and last column, the table for the rest.
                        var regionOuter = region switch
                        {
                            "firstRow" or "lastRow" => own is "lnT" or "lnB" || (own == "lnL" ? cell.Column == 0 : lastC),
                            "firstCol" or "lastCol" => own is "lnL" or "lnR" || (own == "lnT" ? cell.Row == 0 : lastR),
                            _ => isOuter,
                        };
                        var border = part.Element(Ns.A + "tcStyle")?.Element(Ns.A + "tcBdr")?.Element(Ns.A + (regionOuter ? outer : inner));
                        if (border?.Element(Ns.A + "ln") is { } styled)
                        {
                            line = styled;
                        }
                        else if (border?.Element(Ns.A + "lnRef") is { } reference)
                        {
                            line = palette.Theme.Style("lnStyleLst", (int)Palette.Number(reference, "idx")) is { } themed
                                ? new XElement(themed.Name, themed.Attributes(), new XElement(Ns.A + "solidFill", reference.Elements().FirstOrDefault()), themed.Elements().Where(e => !e.Name.LocalName.EndsWith("Fill", StringComparison.Ordinal)))
                                : line;
                        }
                    }
                }

                if (palette.Stroke(line, null, null) is { } stroke)
                {
                    borders.Add(new SceneShape(0, string.Empty, matrix, [new ScenePath([new PathMove(x1, y1), new PathLine(x2, y2)], null, stroke)], null, matrix, null));
                }
            }
        }

        _ = frameWidth;
        items.AddRange(borders);
        return items;
    }

    private static string Margin(XElement? properties, string name, double fallback) =>
        (properties?.Attribute(name)?.Value is { } value ? value : fallback.ToString(CultureInfo.InvariantCulture));

    // The text properties of the style parts as a list style: bold, italic and colour of the highest part that sets them.
    private static XElement TextStyle(List<XElement> parts)
    {
        string? bold = null, italic = null;
        XElement? colour = null;
        foreach (var part in parts)
        {
            if (part.Element(Ns.A + "tcTxStyle") is not { } text)
            {
                continue;
            }

            bold = text.Attribute("b")?.Value ?? bold;
            italic = text.Attribute("i")?.Value ?? italic;
            colour = text.Elements().FirstOrDefault(e => e.Name.LocalName is "srgbClr" or "schemeClr" or "sysClr" or "prstClr" or "scrgbClr" or "hslClr") ?? colour;
        }

        var run = new XElement(Ns.A + "defRPr");
        if (bold is not null)
        {
            run.SetAttributeValue("b", bold == "on" ? "1" : bold == "off" ? "0" : bold);
        }

        if (italic is not null)
        {
            run.SetAttributeValue("i", italic == "on" ? "1" : italic == "off" ? "0" : italic);
        }

        if (colour is not null)
        {
            run.Add(new XElement(Ns.A + "solidFill", colour));
        }

        return new XElement(Ns.A + "lstStyle", Enumerable.Range(1, 9).Select(level => new XElement(Ns.A + $"lvl{level}pPr", run)));
    }

    private XElement? TableStyle(string? id)
    {
        if (id is null)
        {
            id = this.tableStyles?.Attribute("def")?.Value;
        }

        if (id is null)
        {
            return null;
        }

        if (this.tableStyles?.Elements(Ns.A + "tblStyle").FirstOrDefault(s => s.Attribute("styleId")?.Value == id) is { } defined)
        {
            return defined;
        }

        if (id == MediumStyle2Accent1)
        {
            return BuiltInStyle;
        }

        this.notes.Unsupported($"the built-in table style {id} (drawn as a plain grid)");
        return null;
    }
}
