using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public class ChartTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XlsxSheet Sales(params XlsxChart[] charts) => new()
    {
        Name = "Sales 2026",
        Title = "Sales by month",
        Columns =
        [
            new XlsxColumn("Month", XlsxColumnType.Date),
            new XlsxColumn("Region"),
            new XlsxColumn("Revenue", XlsxColumnType.Currency) { Currency = "EUR", Total = XlsxTotal.Sum },
            new XlsxColumn("Units", XlsxColumnType.Integer),
            new XlsxColumn("Share", XlsxColumnType.Percent),
            new XlsxColumn("Per unit"),
        ],
        Rows =
        [
            [new DateOnly(2026, 1, 1), "North", 1500.5m, 12, 0.25, XlsxCell.Formula("[Revenue]/[Units]")],
            [new DateOnly(2026, 2, 1), "South", 980m, 8, 0.5, XlsxCell.Formula("[Revenue]/[Units]")],
            [new DateOnly(2026, 3, 1), "East", XlsxCell.Empty, 10, 0.25, XlsxCell.Formula("[Revenue]/[Units]")],
        ],
        Charts = charts,
    };

    private static XlsxWorkbook Workbook(params XlsxChart[] charts) => new()
    {
        Title = "Figures",
        Language = "de",
        Sheets = [Sales(charts), new XlsxSheet { Name = "Other", Columns = [new XlsxColumn("A")], Rows = [["x"]] }],
    };

    private static XElement ChartXml(ChartPart part) => XDocument.Load(part.GetStream()).Root!;

    [Fact]
    public void ChartsBesideTheTable_ReferToItsCells_AndAreValid()
    {
        var xlsx = Samples.Generate(
            Workbook(
                new XlsxChart { Category = "Month", Series = ["Revenue"], Title = "Revenue", Source = "Ledger" },
                new XlsxChart { Category = "region", Series = ["Units", "Per unit"], Kind = XlsxChartKind.Bar, Stacked = true }),
            Samples.Options with { Brand = Samples.Brand });
        Samples.WriteForReview("charts-beside.xlsx", xlsx);
        Samples.AssertValid(xlsx);

        using var package = Samples.Open(xlsx);
        var sheet = package.WorkbookPart!.WorksheetParts.Single(p => p.Uri.OriginalString.EndsWith("sheet2.xml", StringComparison.Ordinal));
        var drawing = sheet.DrawingsPart!;
        Assert.Equal("rId2", sheet.GetIdOfPart(drawing));
        Assert.Contains("<drawing r:id=\"rId2\"/><legacyDrawingHF r:id=\"rId1\"/>", Samples.Part(xlsx, "xl/worksheets/sheet2.xml"), StringComparison.Ordinal);
        var charts = drawing.ChartParts.OrderBy(p => drawing.GetIdOfPart(p), StringComparer.Ordinal).Select(ChartXml).ToList();
        Assert.Equal(2, charts.Count);

        // Date categories as numbers in the date format, the axis linked to it; a currency series, its axis in EUR.
        var first = charts[0];
        Assert.Null(first.Element(C + "externalData"));
        Assert.Equal("'Sales 2026'!$A$5:$A$7", first.Descendants(C + "cat").Single().Descendants(C + "f").Single().Value);
        Assert.Equal("dd\\.mm\\.yyyy", first.Descendants(C + "cat").Single().Descendants(C + "formatCode").Single().Value);
        Assert.Equal("46023", first.Descendants(C + "cat").Single().Descendants(C + "v").First().Value);
        Assert.Equal("'Sales 2026'!$C$5:$C$7", first.Descendants(C + "val").Single().Descendants(C + "f").Single().Value);
        Assert.Equal("'Sales 2026'!$C$4", first.Descendants(C + "ser").Single().Element(C + "tx")!.Descendants(C + "f").Single().Value);
        Assert.Equal(2, first.Descendants(C + "val").Single().Descendants(C + "pt").Count());
        var valueAxis = first.Descendants(C + "valAx").Single().Element(C + "numFmt")!;
        Assert.Equal(("1", true), (valueAxis.Attribute("sourceLinked")!.Value, valueAxis.Attribute("formatCode")!.Value.Contains('€')));
        Assert.Equal("1", first.Descendants(C + "catAx").Single().Element(C + "numFmt")!.Attribute("sourceLinked")!.Value);
        var title = first.Element(C + "chart")!.Element(C + "title")!.Descendants(A + "p").ToList();
        Assert.Equal(["Revenue", "Quelle: Ledger"], title.Select(p => p.Value));
        Assert.Equal("1000", title[1].Descendants(A + "rPr").Single().Attribute("sz")!.Value);
        Assert.Equal(ResolvedBrand.Resolve(Samples.Brand).Diagram.Palette[0], first.Descendants(C + "ser").Single().Element(C + "spPr")!.Descendants(A + "srgbClr").First().Attribute("val")!.Value);

        // Text categories as strings; bars across, the first category on top; a formula series has no cache.
        var second = charts[1];
        Assert.Equal("bar", second.Descendants(C + "barDir").Single().Attribute("val")!.Value);
        Assert.Equal("stacked", second.Descendants(C + "grouping").Single().Attribute("val")!.Value);
        Assert.Equal("maxMin", second.Descendants(C + "catAx").Single().Descendants(C + "orientation").Single().Attribute("val")!.Value);
        Assert.NotNull(second.Descendants(C + "cat").First().Element(C + "strRef"));
        Assert.Equal(["North", "South", "East"], second.Descendants(C + "cat").First().Descendants(C + "v").Select(v => v.Value));
        Assert.Empty(second.Descendants(C + "val").Last().Descendants(C + "pt"));
        Assert.Equal("'Sales 2026'!$F$5:$F$7", second.Descendants(C + "val").Last().Descendants(C + "f").Single().Value);

        // Right of the table after a blank column, 8 by 16 cells, from the first data row, the second below the first.
        var anchors = XDocument.Load(drawing.GetStream()).Root!.Elements().ToList();
        XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        int At(XElement anchor, string end, string what) => int.Parse(anchor.Element(xdr + end)!.Element(xdr + what)!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal((7, 4, 15, 20), (At(anchors[0], "from", "col"), At(anchors[0], "from", "row"), At(anchors[0], "to", "col"), At(anchors[0], "to", "row")));
        Assert.Equal((7, 21), (At(anchors[1], "from", "col"), At(anchors[1], "from", "row")));
    }

    [Fact]
    public void AChartSheet_FollowsItsTable_IsLinkedFromTheContents_AndIsValid()
    {
        var xlsx = Samples.Generate(Workbook(
            new XlsxChart { Category = "Month", Series = ["Units"], Kind = XlsxChartKind.Line, Placement = XlsxChartPlacement.Sheet, Title = "Units" },
            new XlsxChart { Category = "Month", Series = ["Share"], Kind = XlsxChartKind.Area, Placement = XlsxChartPlacement.Sheet, SheetName = "Shares" }));
        Samples.WriteForReview("charts-sheet.xlsx", xlsx);
        Samples.AssertValid(xlsx);

        using var package = Samples.Open(xlsx);
        var names = package.WorkbookPart!.Workbook!.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(["Info", "Sales 2026", "Diagramm 1", "Shares", "Other"], names);
        Assert.Equal(2, package.WorkbookPart.ChartsheetParts.Count());
        var workbook = Samples.Part(xlsx, "xl/workbook.xml");
        Assert.Contains("<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"4\">'Other'!$1:$1</definedName>", workbook, StringComparison.Ordinal);

        var info = Samples.Part(xlsx, "xl/worksheets/sheet1.xml");
        Assert.Contains("location=\"'Diagramm 1'!A1\"", info, StringComparison.Ordinal);
        Assert.Contains("location=\"'Shares'!A1\"", info, StringComparison.Ordinal);
        var chart = ChartXml(package.WorkbookPart.ChartsheetParts.First().DrawingsPart!.ChartParts.Single());
        Assert.NotNull(chart.Descendants(C + "lineChart").SingleOrDefault());
        Assert.Equal("#,##0", chart.Descendants(C + "val").Single().Descendants(C + "formatCode").Single().Value);
        Assert.DoesNotContain(Samples.Part(xlsx, "xl/worksheets/sheet2.xml"), "<drawing ", StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameWorkbook_GivesTheSameBytes()
    {
        var workbook = Workbook(
            new XlsxChart { Category = "Month", Series = ["Units"] },
            new XlsxChart { Category = "Month", Series = ["Share"], Placement = XlsxChartPlacement.Sheet });
        Assert.Equal(Samples.Generate(workbook), Samples.Generate(workbook));
    }

    [Fact]
    public void Check_ReportsChartProblems_WithTheSheetAndTheChartsNumber()
    {
        var workbook = new XlsxWorkbook
        {
            Sheets =
            [
                Sales(
                    new XlsxChart { Category = "Months", Series = ["Units"] },
                    new XlsxChart { Category = "Month", Series = ["Region", "Nothing"] },
                    new XlsxChart { Category = "Month", Series = [] },
                    new XlsxChart { Category = "Month", Series = ["Units"], Placement = XlsxChartPlacement.Sheet, SheetName = "other" },
                    new XlsxChart { Category = "Month", Series = ["Units"], Placement = XlsxChartPlacement.Sheet, SheetName = "Bad/Name" }),
                new XlsxSheet { Name = "Other", Columns = [new XlsxColumn("A", XlsxColumnType.Number)], Charts = [new XlsxChart { Category = "A", Series = ["A"] }] },
            ],
        };

        var check = XlsxGenerator.Check(workbook, Samples.Options);
        var messages = check.Notes.Where(n => n.Code == NoteCodes.ChartInvalid).Select(n => n.Message).ToList();
        Assert.Collection(
            messages,
            m => Assert.StartsWith("Sheet 'Sales 2026', chart 1: the category 'Months' is not a column of the sheet. Did you mean 'Month'?", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Sheet 'Sales 2026', chart 2: the series 'Region' is a Text column", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Sheet 'Sales 2026', chart 2: the series 'Nothing' is not a column", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Sheet 'Sales 2026', chart 3: the chart has no series.", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Sheet 'Other', chart 1: the sheet has no rows to draw.", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Sheet 'Sales 2026', chart 4: its chart sheet's name 'other' is already the name of another sheet", m, StringComparison.Ordinal),
            m => Assert.StartsWith("Sheet 'Sales 2026', chart 5: its chart sheet's name 'Bad/Name': the name holds '/'", m, StringComparison.Ordinal));
        Assert.Throws<DocumentConversionException>(() => Samples.Generate(workbook));
    }

    [Fact]
    public void MoreSeriesThanColours_Warn_AndCheckCountsTheCharts()
    {
        var sheet = new XlsxSheet
        {
            Name = "Wide",
            Columns = [new XlsxColumn("K"), .. Enumerable.Range(1, 7).Select(i => new XlsxColumn($"S{i}", XlsxColumnType.Number))],
            Rows = [["a", 1, 2, 3, 4, 5, 6, 7]],
            Charts = [new XlsxChart { Category = "K", Series = [.. Enumerable.Range(1, 7).Select(i => $"S{i}")], Placement = XlsxChartPlacement.Sheet }],
        };
        var workbook = new XlsxWorkbook { Sheets = [sheet] };

        using var output = new MemoryStream();
        var result = XlsxGenerator.Generate(workbook, output, Samples.Options);
        Assert.Equal(2, result.Pages);
        var note = Assert.Single(result.Notes, n => n.Code == NoteCodes.ChartColorsRepeat);
        Assert.StartsWith("Sheet 'Wide', chart 1: the chart has 7 series, but the brand's diagram palette has 6 colours", note.Message, StringComparison.Ordinal);
        Samples.AssertValid(output.ToArray());

        var check = XlsxGenerator.Check(workbook, Samples.Options);
        Assert.Equal((1, 2), (check.Statistics.Diagrams, check.Statistics.Sheets));
    }

    [Fact]
    public void Preview_NamesEachChart_InALine()
    {
        var html = XlsxGenerator.Preview(Workbook(
            new XlsxChart { Category = "Month", Series = ["Revenue", "Units"], Title = "Revenue" },
            new XlsxChart { Category = "Month", Series = ["Units"], Kind = XlsxChartKind.Line, Placement = XlsxChartPlacement.Sheet }));

        Assert.Contains("</table>\n<p>Diagramm – Column – Revenue – Revenue, Units</p>\n</section>\n", html, StringComparison.Ordinal);
        Assert.Contains("<section>\n<h2>Diagramm 1</h2>\n<p>Diagramm – Line – Units</p>\n</section>\n", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkbookWithoutCharts_HasNoChartParts()
    {
        var xlsx = Samples.Generate(Samples.EveryType());
        Assert.DoesNotContain(Samples.Entries(xlsx), e => e.Contains("chart", StringComparison.OrdinalIgnoreCase) || e.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) && !e.Contains("vml", StringComparison.Ordinal));
    }
}
