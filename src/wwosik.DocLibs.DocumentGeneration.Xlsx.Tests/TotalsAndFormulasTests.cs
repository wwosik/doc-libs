using System.Globalization;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public class TotalsAndFormulasTests
{
    /// <summary>Orders: price, quantity, a formula for the amount, a currency formula, totals on every kind.</summary>
    public static XlsxWorkbook Orders(string? language = null) => new()
    {
        Title = "Orders",
        Language = language,
        Sheets =
        [
            new XlsxSheet
            {
                Name = "Orders",
                Title = "Orders",
                Columns =
                [
                    new XlsxColumn("Article") { Total = XlsxTotal.Count },
                    new XlsxColumn("Price", XlsxColumnType.Currency) { Currency = "EUR", Total = XlsxTotal.Average },
                    new XlsxColumn("Quantity", XlsxColumnType.Integer) { Total = XlsxTotal.Sum },
                    new XlsxColumn("Amount") { Total = XlsxTotal.Sum },
                    new XlsxColumn("Gross"),
                    new XlsxColumn("Lookup"),
                    new XlsxColumn("Ordered", XlsxColumnType.Date) { Total = XlsxTotal.Max },
                ],
                Rows =
                [
                    ["Pen", 2.5, 4, XlsxCell.Formula("[Price]*[Quantity]"), XlsxCell.Formula("=[Price]*1.19"), XlsxCell.Formula("XLOOKUP(\"Pen\",A6:A8,C6:C8)"), new DateOnly(2026, 3, 1)],
                    ["Ink", 10m, 2, XlsxCell.Formula("[Price]*[Quantity]"), XlsxCell.Formula("=[Price]*1.19"), XlsxCell.Formula("SUM(C{row}:C{row})"), new DateOnly(2026, 3, 5)],
                    ["Pad", 4, 10, XlsxCell.Formula("[Price]*[Quantity]"), XlsxCell.Formula("=[Price]*1.19"), XlsxCell.Formula("SUM(C{row}:C{row})"), new DateOnly(2026, 2, 20)],
                ],
            },
        ],
    };

    private static XlsxWorkbook Totals(params XlsxColumn[] columns) => new()
    {
        Sheets = [new XlsxSheet { Name = "T", Columns = columns, Rows = [[1, "a"], [2.5, "b"], [XlsxCell.Empty, "c"]] }],
    };

    [Fact]
    public void Orders_AreValid_WithTotalsAndFormulas()
    {
        var xlsx = Samples.Generate(Orders());
        Samples.WriteForReview("totals-and-formulas.xlsx", xlsx);

        Samples.AssertValid(xlsx);
        var sheet = Samples.Part(xlsx, "xl/worksheets/sheet2.xml");

        // Title, date, a blank row: the header is row 4, the data rows 5 to 7, the totals row 8.
        Assert.Contains("<c r=\"D5\" s=\"", sheet, StringComparison.Ordinal);
        Assert.Contains("><f>B5*C5</f></c>", sheet, StringComparison.Ordinal);
        Assert.Contains("><f>B7*1.19</f></c>", sheet, StringComparison.Ordinal);
        Assert.Contains("<f>_xlfn.XLOOKUP(\"Pen\",A6:A8,C6:C8)</f>", sheet, StringComparison.Ordinal);
        Assert.Contains("<f>SUM(C6:C6)</f>", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("_xlfn.SUM", sheet, StringComparison.Ordinal);
        Assert.Contains("fullCalcOnLoad=\"1\"", Samples.Part(xlsx, "xl/workbook.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Totals_AreSubtotals_WithCachedValues_UnderTheTable()
    {
        var xlsx = Samples.Generate(Orders());
        var sheet = Samples.Part(xlsx, "xl/worksheets/sheet2.xml");

        Assert.Contains("<f>SUBTOTAL(103,A5:A7)</f><v>3</v>", sheet, StringComparison.Ordinal);
        Assert.Contains("<f>SUBTOTAL(101,B5:B7)</f><v>5.5</v>", sheet, StringComparison.Ordinal);
        Assert.Contains("<f>SUBTOTAL(109,C5:C7)</f><v>16</v>", sheet, StringComparison.Ordinal);

        // Over formulas the total has no value: Excel computes it.
        Assert.Contains("<f>SUBTOTAL(109,D5:D7)</f></c>", sheet, StringComparison.Ordinal);
        var serial = XlsxPlan.Serial(new DateTime(2026, 3, 5)).ToString("R", CultureInfo.InvariantCulture);
        Assert.Contains($"<f>SUBTOTAL(104,G5:G7)</f><v>{serial}</v>", sheet, StringComparison.Ordinal);

        // The filter and the banding cover the data, not the totals; the dimension includes them.
        Assert.Contains("<autoFilter ref=\"A4:G7\"/>", sheet, StringComparison.Ordinal);
        Assert.Contains("<conditionalFormatting sqref=\"A5:G7\">", sheet, StringComparison.Ordinal);
        Assert.Contains("<dimension ref=\"A1:G8\"/>", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLabel_IsInTheFirstColumnWithoutATotal()
    {
        var english = Samples.Part(Samples.Generate(Orders()), "xl/sharedStrings.xml");
        var german = Samples.Part(Samples.Generate(Orders("de")), "xl/sharedStrings.xml");
        var own = Samples.Part(Samples.Generate(Orders() with { Sheets = [Orders().Sheets[0] with { TotalsLabel = "All orders" }] }), "xl/sharedStrings.xml");

        Assert.Contains(">Total<", english, StringComparison.Ordinal);
        Assert.Contains(">Summe<", german, StringComparison.Ordinal);
        Assert.Contains(">All orders<", own, StringComparison.Ordinal);
        var sheet = Samples.Part(Samples.Generate(Orders()), "xl/worksheets/sheet2.xml");
        Assert.Contains("<c r=\"E8\" s=\"", sheet, StringComparison.Ordinal);
        Assert.Matches("<c r=\"E8\" s=\"\\d+\" t=\"s\">", sheet);
    }

    [Fact]
    public void TheTotalsRow_IsBoldOnALightShadeOfTheHeader()
    {
        var colors = SheetColors.Of(ResolvedBrand.Resolve(Brand.Neutral));

        Assert.True(ResolvedBrand.Contrast(colors.Totals, SheetColors.Text) >= 7);
        Assert.NotEqual(colors.Header, colors.Totals);
        var styles = Samples.Part(Samples.Generate(Orders()), "xl/styles.xml");
        Assert.Contains($"<fgColor rgb=\"FF{colors.Totals}\"/>", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutFormulas_ThereIsNoFullCalcOnLoad()
    {
        var xlsx = Samples.Generate(Totals(new XlsxColumn("N", XlsxColumnType.Number) { Total = XlsxTotal.Sum }, new XlsxColumn("T") { Total = XlsxTotal.Count }));

        Samples.AssertValid(xlsx);
        Assert.DoesNotContain("fullCalcOnLoad", Samples.Part(xlsx, "xl/workbook.xml"), StringComparison.Ordinal);
        Assert.DoesNotContain("fullCalcOnLoad", Samples.Part(Samples.Generate(Samples.EveryType()), "xl/workbook.xml"), StringComparison.Ordinal);
        var sheet = Samples.Part(xlsx, "xl/worksheets/sheet1.xml");
        Assert.Contains("<f>SUBTOTAL(109,A2:A4)</f><v>3.5</v>", sheet, StringComparison.Ordinal);
        Assert.Contains("<f>SUBTOTAL(103,B2:B4)</f><v>3</v>", sheet, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(XlsxTotal.Average, "101", "<v>1.75</v>")]
    [InlineData(XlsxTotal.Min, "105", "<v>1</v>")]
    [InlineData(XlsxTotal.Max, "104", "<v>2.5</v>")]
    [InlineData(XlsxTotal.Count, "103", "<v>2</v>")]
    public void EveryTotal_HasItsFunctionAndValue(XlsxTotal total, string function, string value)
    {
        var sheet = Samples.Part(Samples.Generate(Totals(new XlsxColumn("N", XlsxColumnType.Number) { Total = total }, new XlsxColumn("T"))), "xl/worksheets/sheet1.xml");

        Assert.Contains($"<f>SUBTOTAL({function},A2:A4)</f>{value}", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAverageOfNothing_IsDivisionByZero()
    {
        var workbook = new XlsxWorkbook { Sheets = [new XlsxSheet { Name = "T", Columns = [new XlsxColumn("N", XlsxColumnType.Number) { Total = XlsxTotal.Average }], Rows = [[XlsxCell.Empty]] }] };
        var xlsx = Samples.Generate(workbook);

        Samples.AssertValid(xlsx);
        Assert.Contains("t=\"e\"><f>SUBTOTAL(101,A2:A2)</f><v>#DIV/0!</v>", Samples.Part(xlsx, "xl/worksheets/sheet1.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASheetWithoutRows_HasNoTotalsRow()
    {
        var workbook = new XlsxWorkbook { Sheets = [new XlsxSheet { Name = "T", Columns = [new XlsxColumn("N", XlsxColumnType.Number) { Total = XlsxTotal.Sum }] }] };

        Assert.DoesNotContain("SUBTOTAL", Samples.Part(Samples.Generate(workbook), "xl/worksheets/sheet1.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void ATotalOnTextOrBooleans_IsCountOnly()
    {
        var check = XlsxGenerator.Check(Totals(new XlsxColumn("N", XlsxColumnType.Number), new XlsxColumn("T") { Total = XlsxTotal.Sum }), Samples.Options);
        var boolean = XlsxGenerator.Check(new XlsxWorkbook { Sheets = [new XlsxSheet { Name = "B", Columns = [new XlsxColumn("B", XlsxColumnType.Boolean) { Total = XlsxTotal.Count }], Rows = [[true]] }] }, Samples.Options);

        Assert.Contains(check.Notes, n => n.Code == NoteCodes.ColumnInvalid && n.Message.Contains("a Text column cannot have the total Sum", StringComparison.Ordinal));
        Assert.True(boolean.IsClean);
    }

    [Fact]
    public void AFormulaColumn_IsNumber_OrCurrencyWhenItRefersOnlyToCurrency()
    {
        var plan = XlsxPlan.Of(Orders(), Samples.Options);
        var columns = plan.Sheets[0].Columns;

        Assert.Equal(XlsxColumnType.Number, columns[3].Format.Type);
        Assert.Equal(XlsxColumnType.Currency, columns[4].Format.Type);
        Assert.Equal(columns[1].Format.Code, columns[4].Format.Code);
        Assert.Equal(XlsxColumnType.Number, columns[5].Format.Type);
        Assert.Equal(XlsxAlign.Right, columns[3].Align);
    }

    [Fact]
    public void CurrencySpreads_ThroughFormulaColumns_AndADeclaredTypeWins()
    {
        var workbook = new XlsxWorkbook
        {
            Sheets =
            [
                new XlsxSheet
                {
                    Name = "C",
                    Columns =
                    [
                        new XlsxColumn("Twice"),
                        new XlsxColumn("Net"),
                        new XlsxColumn("Price", XlsxColumnType.Currency) { Currency = "USD" },
                        new XlsxColumn("Text", XlsxColumnType.Text),
                        new XlsxColumn("Own") { Currency = "CHF" },
                    ],
                    Rows = [[XlsxCell.Formula("[Net]*2"), XlsxCell.Formula("[Price]/1.19"), 10, XlsxCell.Formula("TEXT([Price],\"0\")"), XlsxCell.Formula("[Price]")]],
                },
            ],
        };
        var columns = XlsxPlan.Of(workbook, Samples.Options).Sheets[0].Columns;

        Assert.Equal(XlsxColumnType.Currency, columns[0].Format.Type);
        Assert.Equal(XlsxColumnType.Currency, columns[1].Format.Type);
        Assert.Equal(columns[2].Format.Code, columns[0].Format.Code);
        Assert.Equal(XlsxColumnType.Text, columns[3].Format.Type);
        Assert.Contains("CHF", columns[4].Format.Code, StringComparison.Ordinal);
        Assert.True(XlsxGenerator.Check(workbook, Samples.Options).IsClean);
    }

    private static XlsxWorkbook Broken() => new()
    {
        Sheets =
        [
            new XlsxSheet
            {
                Name = "Broken",
                Columns =
                [
                    new XlsxColumn("Price", XlsxColumnType.Number),
                    new XlsxColumn("A"),
                    new XlsxColumn("B"),
                    new XlsxColumn("C"),
                ],
                Rows =
                [
                    [1, XlsxCell.Formula("[Prise]*2"), XlsxCell.Formula("[Price]*"), XlsxCell.Formula("Price*2")],
                    [2, XlsxCell.Formula("[B]+1"), XlsxCell.Formula("[A]+1"), XlsxCell.Formula("MYADDIN([Price])+SUMM(1)")],
                    [3, XlsxCell.Formula("[A]"), XlsxCell.Formula("A{row}+B4"), XlsxCell.Formula("C4")],
                ],
            },
        ],
    };

    [Fact]
    public void Check_ReportsFormulas_WithSheetCellAndPosition()
    {
        var check = XlsxGenerator.Check(Broken(), Samples.Options);
        var messages = check.Notes.Select(n => n.Code + " " + n.Message).ToList();

        Assert.Contains(messages, m => m.StartsWith("formula-invalid Sheet 'Broken', row 1, column 'A' (B2): the formula '[Prise]*2' refers to [Prise] at character 1, but the sheet has no column of that header. Did you mean [Price]?", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("formula-invalid Sheet 'Broken', row 1, column 'B' (C2): the formula '[Price]*' cannot be read at character 9: the formula ends where a value is expected.", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("formula-invalid Sheet 'Broken', row 1, column 'C' (D2): the formula 'Price*2' holds the name 'Price' at character 1", StringComparison.Ordinal) && m.EndsWith("To refer to the column, write [Price].", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("formula-invalid Sheet 'Broken', row 2, column 'A' (B3): the formula refers to itself within its row at character 1: [A] → [B] → [A].", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("formula-invalid Sheet 'Broken', row 3, column 'A' (B4): the formula refers to itself within its row at character 1: [A] → [A].", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("function-unknown Sheet 'Broken', row 2, column 'C' (D3): the formula 'MYADDIN([Price])+SUMM(1)' calls MYADDIN at character 1", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("function-unknown Sheet 'Broken', row 2, column 'C' (D3): the formula 'MYADDIN([Price])+SUMM(1)' calls SUMM at character 18", StringComparison.Ordinal) && m.Contains("Did you mean SUM", StringComparison.Ordinal));

        Assert.Equal(5, check.Notes.Count(n => n.Code == NoteCodes.FormulaInvalid));
        Assert.Equal(2, check.Notes.Count(n => n.Code == NoteCodes.FunctionUnknown));
    }

    [Fact]
    public void AnUnknownFunction_IsOnlyAWarning()
    {
        var workbook = new XlsxWorkbook { Sheets = [new XlsxSheet { Name = "U", Columns = [new XlsxColumn("N", XlsxColumnType.Number), new XlsxColumn("F")], Rows = [[1, XlsxCell.Formula("myaddin([N])")], [2, XlsxCell.Formula("myaddin([N])")]] }] };
        using var output = new MemoryStream();

        var result = XlsxGenerator.Generate(workbook, output, Samples.Options);

        Assert.Equal([NoteCodes.FunctionUnknown], result.Notes.Select(n => n.Code));
        Assert.Contains("<f>myaddin(A2)</f>", Samples.Part(output.ToArray(), "xl/worksheets/sheet1.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ThrowsForInvalidFormulas()
    {
        var ex = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Broken()));

        Assert.Contains("[Prise]", ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFormulaTooLong_IsAnExcelLimit()
    {
        var workbook = new XlsxWorkbook { Sheets = [new XlsxSheet { Name = "L", Columns = [new XlsxColumn("F")], Rows = [[XlsxCell.Formula(string.Join('+', Enumerable.Repeat("1", 4_200)))]] }] };

        Assert.Contains(XlsxGenerator.Check(workbook, Samples.Options).Notes, n => n.Code == NoteCodes.ExcelLimit && n.Message.Contains("8,192", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_ShowsFormulasAsCode_AndTheTotalsInTheFoot()
    {
        var html = XlsxGenerator.Preview(Orders(), Samples.Options);

        Assert.Contains("<td class=\"num\"><code>=B5*C5</code></td>", html, StringComparison.Ordinal);
        Assert.Contains("<code>=XLOOKUP(&quot;Pen&quot;,A6:A8,C6:C8)</code>", html, StringComparison.Ordinal);
        Assert.Contains("<tfoot>\n<tr><td class=\"num\">3</td><td class=\"num\">€5.50</td><td class=\"num\">16</td><td class=\"num\"><code>=SUBTOTAL(109,D5:D7)</code></td><td>Total</td><td class=\"num\"></td><td>3/5/2026</td></tr>\n</tfoot>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Formula_KeepsOrDropsTheEqualsSign()
    {
        Assert.Equal("A1+1", XlsxCell.Formula(" =A1+1 ").Text);
        Assert.Equal("=A1+1", XlsxCell.Formula("A1+1").ToString());
        Assert.Throws<ArgumentException>(() => XlsxCell.Formula(" "));
    }

    [Fact]
    public void TheSameWorkbook_GivesTheSameBytes()
    {
        Assert.Equal(Samples.Generate(Orders()), Samples.Generate(Orders()));
    }
}
