using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Writes a <see cref="ChartSpec"/> as a native chart of a deck or a Word document: a chart part, written as XML here so
/// it can be reviewed like code, with its data in an embedded workbook, so that <em>Edit Data</em> works. Shared source,
/// linked into the PPTX and DOCX generators; colours come as DrawingML fills, so that each format keeps its own colours
/// (theme colours and transparency on slides).
/// </summary>
internal static class ChartWriter
{
    /// <summary>The <c>a:graphicData</c> URI of a chart.</summary>
    public const string GraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private const string Sheet = "Sheet1";

    /// <summary>
    /// Adds the chart to <paramref name="owner"/>, a slide or a Word document's main part, and returns the id of its
    /// relationship.
    /// </summary>
    /// <remarks>
    /// <paramref name="text"/> colours the chart's text (on dark slides: white) as an <c>a:solidFill</c> element,
    /// <see langword="null"/> leaves the theme's; <paramref name="grid"/>, also an <c>a:solidFill</c>, colours the
    /// gridlines and the category axis; <paramref name="font"/> is the family of all its text, <see langword="null"/> for
    /// the theme font.
    /// </remarks>
    public static string Add(OpenXmlPart owner, ChartSpec chart, string? text, string grid, string? font)
    {
        var part = owner.AddNewPart<ChartPart>();
        var workbook = part.AddEmbeddedPackagePart(EmbeddedPackagePartType.Xlsx);
        using (var data = new MemoryStream())
        {
            Workbook(data, chart);
            data.Position = 0;
            workbook.FeedData(data);
        }

        using (var xml = new MemoryStream(Encoding.UTF8.GetBytes(Xml(chart, part.GetIdOfPart(workbook), text, grid, font))))
        {
            part.FeedData(xml);
        }

        return owner.GetIdOfPart(part);
    }

    /// <summary>
    /// Writes the chart into <paramref name="part"/>, an empty chart part, with its series referring to the workbook cells
    /// <paramref name="cells"/> names: formulas (<c>c:f</c>) with a cache of the values from <paramref name="chart"/>, and
    /// no workbook of its own, so that changing a cell changes the chart. The value axis takes the number format of the
    /// first series' cells. <paramref name="text"/>, <paramref name="grid"/> and <paramref name="font"/> are as for
    /// <see cref="Add"/>.
    /// </summary>
    public static void Write(ChartPart part, ChartSpec chart, ChartCells cells, string? text, string grid, string? font)
    {
        using var xml = new MemoryStream(Encoding.UTF8.GetBytes(Xml(chart, null, text, grid, font, cells)));
        part.FeedData(xml);
    }

    /// <summary>An <c>a:solidFill</c> of an RGB colour, upper-case hex without <c>#</c>.</summary>
    public static string Solid(string hex) => $"<a:solidFill><a:srgbClr val=\"{hex}\"/></a:solidFill>";

    // The chart part; with cells, the series refer to them and there is no embedded workbook.
    private static string Xml(ChartSpec chart, string? workbook, string? text, string grid, string? font, ChartCells? cells = null)
    {
        var axes = "<c:axId val=\"111\"/><c:axId val=\"222\"/>";
        var series = string.Concat(chart.Series.Select((s, i) => cells is null ? Series(chart, s, i) : Series(chart, s, i, cells)));
        var across = cells is { Horizontal: true } && chart.Kind == ChartKind.Bar;
        var plot = chart.Kind switch
        {
            ChartKind.Bar => $"<c:barChart><c:barDir val=\"{(across ? "bar" : "col")}\"/>"
                + $"<c:grouping val=\"{(chart.Stacked ? "stacked" : "clustered")}\"/><c:varyColors val=\"0\"/>{series}"
                + $"<c:gapWidth val=\"60\"/>{(chart.Stacked ? "<c:overlap val=\"100\"/>" : string.Empty)}{axes}</c:barChart>",
            ChartKind.Line => $"<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>{series}<c:marker val=\"1\"/>{axes}</c:lineChart>",
            _ => $"<c:areaChart><c:grouping val=\"{(chart.Stacked ? "stacked" : "standard")}\"/><c:varyColors val=\"0\"/>{series}{axes}</c:areaChart>",
        };
        var title = cells is not null ? Title(chart.Title, cells.Note, text, font)
            : chart.Title is { } words
            ? $"<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr>{Run("defRPr", 1400, true, text, font)}</a:pPr>"
                + $"<a:r>{Run("rPr", 1400, true, text, font)}<a:t>{Escape(words)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>"
                + "<c:autoTitleDeleted val=\"0\"/>"
            : "<c:autoTitleDeleted val=\"1\"/>";
        var unit = chart.Unit is { } u
            ? $"<c:title><c:tx><c:rich><a:bodyPr rot=\"0\" vert=\"horz\"/><a:lstStyle/><a:p><a:pPr>{Run("defRPr", 1100, false, text, font)}</a:pPr>"
                + $"<a:r>{Run("rPr", 1100, false, text, font)}<a:t>{Escape(u)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>"
            : string.Empty;
        var labels = TextProperties(1100, text, font);

        // Bars and areas start at zero (or below, for negative values), so that their heights compare honestly.
        var lowest = chart.Series.SelectMany(s => s.Values).OfType<double>().DefaultIfEmpty(0).Min();
        var minimum = chart.Kind == ChartKind.Line ? string.Empty
            : $"<c:min val=\"{Math.Min(0, Math.Floor(lowest)).ToString(CultureInfo.InvariantCulture)}\"/>";
        var line = $"<a:ln w=\"9525\">{grid}</a:ln>";

        // With cells: the categories' and values' formats come from the cells; across, the categories read from the top
        // and the value axis stands at the bottom.
        var categoryFormat = cells?.CategoryNumbers is not null && cells.CategoryFormat is { } c
            ? $"<c:numFmt formatCode=\"{Escape(c)}\" sourceLinked=\"1\"/>"
            : "<c:numFmt formatCode=\"General\" sourceLinked=\"0\"/>";
        var valueFormat = cells is not null
            ? $"<c:numFmt formatCode=\"{Escape(cells.ValueFormats.FirstOrDefault() ?? "General")}\" sourceLinked=\"1\"/>"
            : "<c:numFmt formatCode=\"General\" sourceLinked=\"0\"/>";
        var orientation = across ? "maxMin" : "minMax";
        var (categoryPosition, valuePosition) = across ? ("l", "b") : ("b", "l");
        var valueCrosses = across ? "max" : "autoZero";
        var external = workbook is null ? string.Empty : $"<c:externalData r:id=\"{workbook}\"><c:autoUpdate val=\"0\"/></c:externalData>";
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" "
            + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<c:date1904 val=\"0\"/><c:roundedCorners val=\"0\"/>"
            + $"<c:chart>{title}<c:plotArea><c:layout/>{plot}"
            + $"<c:catAx><c:axId val=\"111\"/><c:scaling><c:orientation val=\"{orientation}\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{categoryPosition}\"/>"
            + $"{categoryFormat}<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"low\"/>"
            + $"<c:spPr>{line}</c:spPr>{labels}<c:crossAx val=\"222\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/>"
            + "<c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>"
            + $"<c:valAx><c:axId val=\"222\"/><c:scaling><c:orientation val=\"minMax\"/>{minimum}</c:scaling><c:delete val=\"0\"/><c:axPos val=\"{valuePosition}\"/>"
            + $"<c:majorGridlines><c:spPr><a:ln w=\"6350\">{grid}</a:ln></c:spPr></c:majorGridlines>{unit}"
            + $"{valueFormat}<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>"
            + $"<c:spPr><a:ln><a:noFill/></a:ln></c:spPr>{labels}<c:crossAx val=\"111\"/><c:crosses val=\"{valueCrosses}\"/><c:crossBetween val=\"between\"/></c:valAx>"
            + "<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr></c:plotArea>"
            + $"<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/>{labels}</c:legend>"
            + "<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>"
            + $"<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>{TextProperties(1100, text, font)}"
            + $"{external}</c:chartSpace>";
    }

    // The title of a chart on cells: the title, and the note as a second, smaller paragraph; neither, no title.
    private static string Title(string? title, string? note, string? text, string? font)
    {
        if (title is null && note is null)
        {
            return "<c:autoTitleDeleted val=\"1\"/>";
        }

        var paragraphs = new StringBuilder();
        if (title is not null)
        {
            paragraphs.Append($"<a:p><a:pPr>{Run("defRPr", 1400, true, text, font)}</a:pPr><a:r>{Run("rPr", 1400, true, text, font)}<a:t>{Escape(title)}</a:t></a:r></a:p>");
        }

        if (note is not null)
        {
            paragraphs.Append($"<a:p><a:pPr>{Run("defRPr", 1000, false, text, font)}</a:pPr><a:r>{Run("rPr", 1000, false, text, font)}<a:t>{Escape(note)}</a:t></a:r></a:p>");
        }

        return $"<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/>{paragraphs}</c:rich></c:tx><c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/>";
    }

    // One series on workbook cells: its name from its header cell, its categories and values from their ranges, each with a cache.
    private static string Series(ChartSpec chart, ChartSeries series, int index, ChartCells cells)
    {
        var count = chart.Categories.Count;
        var colour = chart.Colors.Count > 0 ? chart.Colors[index % chart.Colors.Count] : "4472C4";
        var fill = chart.Kind == ChartKind.Line
            ? $"<c:spPr><a:ln w=\"28575\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{colour}\"/></a:solidFill><a:round/></a:ln></c:spPr>"
                + $"<c:marker><c:symbol val=\"circle\"/><c:size val=\"6\"/><c:spPr><a:solidFill><a:srgbClr val=\"{colour}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr></c:marker>"
            : $"<c:spPr><a:solidFill><a:srgbClr val=\"{colour}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>"
                + (chart.Kind == ChartKind.Bar ? "<c:invertIfNegative val=\"0\"/>" : string.Empty);
        var prefix = Escape(cells.Prefix);
        var categories = cells.CategoryNumbers is { } numbers
            ? $"<c:numRef><c:f>{prefix}{cells.Categories}</c:f><c:numCache><c:formatCode>{Escape(cells.CategoryFormat ?? "General")}</c:formatCode>"
                + $"<c:ptCount val=\"{count}\"/>{Points(numbers)}</c:numCache></c:numRef>"
            : $"<c:strRef><c:f>{prefix}{cells.Categories}</c:f><c:strCache><c:ptCount val=\"{count}\"/>"
                + string.Concat(chart.Categories.Select((c, i) => c.Length == 0 ? string.Empty : $"<c:pt idx=\"{i}\"><c:v>{Escape(c)}</c:v></c:pt>"))
                + "</c:strCache></c:strRef>";
        var format = index < cells.ValueFormats.Count ? cells.ValueFormats[index] ?? "General" : "General";
        return $"<c:ser><c:idx val=\"{index}\"/><c:order val=\"{index}\"/>"
            + $"<c:tx><c:strRef><c:f>{prefix}{cells.Names[index]}</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{Escape(series.Name)}</c:v></c:pt></c:strCache></c:strRef></c:tx>"
            + fill
            + $"<c:cat>{categories}</c:cat>"
            + $"<c:val><c:numRef><c:f>{prefix}{cells.Values[index]}</c:f><c:numCache><c:formatCode>{Escape(format)}</c:formatCode>"
            + $"<c:ptCount val=\"{count}\"/>{Points(series.Values)}</c:numCache></c:numRef></c:val>"
            + (chart.Kind == ChartKind.Line ? "<c:smooth val=\"0\"/>" : string.Empty)
            + "</c:ser>";
    }

    private static string Points(IReadOnlyList<double?> values) => string.Concat(values.Select((v, i) => v is { } value
        ? $"<c:pt idx=\"{i}\"><c:v>{value.ToString("R", CultureInfo.InvariantCulture)}</c:v></c:pt>"
        : string.Empty));

    // One series: its name, colour, categories and values, with references into the embedded sheet.
    private static string Series(ChartSpec chart, ChartSeries series, int index)
    {
        var column = ColumnName(index + 1);
        var last = chart.Categories.Count + 1;
        var colour = chart.Colors.Count > 0 ? chart.Colors[index % chart.Colors.Count] : "4472C4";
        var fill = chart.Kind == ChartKind.Line
            ? $"<c:spPr><a:ln w=\"28575\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{colour}\"/></a:solidFill><a:round/></a:ln></c:spPr>"
                + $"<c:marker><c:symbol val=\"circle\"/><c:size val=\"6\"/><c:spPr><a:solidFill><a:srgbClr val=\"{colour}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr></c:marker>"
            : $"<c:spPr><a:solidFill><a:srgbClr val=\"{colour}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>"
                + (chart.Kind == ChartKind.Bar ? "<c:invertIfNegative val=\"0\"/>" : string.Empty);
        var categories = string.Concat(chart.Categories.Select((c, i) => $"<c:pt idx=\"{i}\"><c:v>{Escape(c)}</c:v></c:pt>"));
        var values = string.Concat(series.Values.Select((v, i) => v is { } value
            ? $"<c:pt idx=\"{i}\"><c:v>{value.ToString("R", CultureInfo.InvariantCulture)}</c:v></c:pt>"
            : string.Empty));
        return $"<c:ser><c:idx val=\"{index}\"/><c:order val=\"{index}\"/>"
            + $"<c:tx><c:strRef><c:f>{Sheet}!${column}$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>{Escape(series.Name)}</c:v></c:pt></c:strCache></c:strRef></c:tx>"
            + fill
            + $"<c:cat><c:strRef><c:f>{Sheet}!$A$2:$A${last}</c:f><c:strCache><c:ptCount val=\"{chart.Categories.Count}\"/>{categories}</c:strCache></c:strRef></c:cat>"
            + $"<c:val><c:numRef><c:f>{Sheet}!${column}$2:${column}${last}</c:f><c:numCache><c:formatCode>General</c:formatCode>"
            + $"<c:ptCount val=\"{chart.Categories.Count}\"/>{values}</c:numCache></c:numRef></c:val>"
            + (chart.Kind == ChartKind.Line ? "<c:smooth val=\"0\"/>" : string.Empty)
            + "</c:ser>";
    }

    private static string TextProperties(int size, string? colour, string? font) =>
        $"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr>{Run("defRPr", size, false, colour, font)}</a:pPr><a:endParaRPr lang=\"en-US\"/></a:p></c:txPr>";

    private static string Run(string element, int size, bool bold, string? colour, string? font) =>
        $"<a:{element} lang=\"en-US\" sz=\"{size}\" b=\"{(bold ? 1 : 0)}\">"
        + (colour ?? string.Empty)
        + (font is not null ? $"<a:latin typeface=\"{Escape(font)}\"/>" : string.Empty)
        + $"</a:{element}>";

    // The data as a sheet: categories in column A, a column per series, the names in row 1.
    private static void Workbook(Stream output, ChartSpec chart)
    {
        using var document = SpreadsheetDocument.Create(output, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new X.Workbook();
        var sheet = workbook.AddNewPart<WorksheetPart>();
        var data = new X.SheetData();
        X.Cell Text(string reference, string value) => new(new X.InlineString(new X.Text(value))) { CellReference = reference, DataType = X.CellValues.InlineString };
        var header = new X.Row { RowIndex = 1 };
        header.Append(Text("A1", string.Empty));
        header.Append(chart.Series.Select((s, i) => Text($"{ColumnName(i + 1)}1", s.Name)));
        data.Append(header);
        for (var r = 0; r < chart.Categories.Count; r++)
        {
            var index = (uint)(r + 2);
            var row = new X.Row { RowIndex = index };
            row.Append(Text($"A{index}", chart.Categories[r]));
            for (var s = 0; s < chart.Series.Count; s++)
            {
                if (chart.Series[s].Values[r] is { } value)
                {
                    row.Append(new X.Cell(new X.CellValue(value.ToString("R", CultureInfo.InvariantCulture))) { CellReference = $"{ColumnName(s + 1)}{index}", DataType = X.CellValues.Number });
                }
            }

            data.Append(row);
        }

        sheet.Worksheet = new X.Worksheet(data);
        workbook.Workbook.AppendChild(new X.Sheets(new X.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = Sheet }));
    }

    // 0 → A, 25 → Z, 26 → AA.
    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private static string Escape(string text) => SecurityElement.Escape(XmlSafe(text));

    // XML 1.0 has no place for control characters; tabs stay. The deck writer's rule, so that decks keep their bytes.
    private static string XmlSafe(string text) =>
        text.Any(c => char.IsControl(c) && c != '\t') ? new string(text.Where(c => !char.IsControl(c) || c == '\t').ToArray()) : text;
}
