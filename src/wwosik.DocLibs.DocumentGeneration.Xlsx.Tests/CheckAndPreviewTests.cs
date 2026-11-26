using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public class CheckAndPreviewTests
{
    private static XlsxWorkbook Invalid() => new()
    {
        Title = "Broken",
        Sheets =
        [
            new XlsxSheet
            {
                Name = "Sales",
                Title = "Sales",
                Columns =
                [
                    new XlsxColumn("Region"),
                    new XlsxColumn("Units", XlsxColumnType.Integer),
                    new XlsxColumn("Amount", XlsxColumnType.Number) { Currency = "EUR", Decimals = 12 },
                    new XlsxColumn("Paid", XlsxColumnType.Currency) { Currency = "XYZ" },
                ],
                Rows =
                [
                    ["North", 1.5, 3, 4],
                    [12, "many", 3, 4, "extra"],
                    ["West", 2, 3, new DateOnly(1850, 1, 1)],
                ],
            },
            new XlsxSheet { Name = "sales", Columns = [new XlsxColumn("")] },
            new XlsxSheet { Name = "A sheet name far longer than thirty-one", Columns = [new XlsxColumn("A")] },
            new XlsxSheet { Name = "Q1/Q2", Columns = [new XlsxColumn("A")], Rows = [[new string('x', 40_000)]] },
        ],
    };

    [Fact]
    public void Check_ReportsEveryProblem_WithSheetAndCell()
    {
        var check = XlsxGenerator.Check(Invalid(), Samples.Options);

        Assert.False(check.IsClean);
        Assert.NotNull(check.Error);
        var codes = check.Notes.Select(n => n.Code).ToList();
        Assert.Contains(NoteCodes.CellType, codes);
        Assert.Contains(NoteCodes.SheetName, codes);
        Assert.Contains(NoteCodes.ColumnInvalid, codes);
        Assert.Contains(NoteCodes.RowTooWide, codes);
        Assert.Contains(NoteCodes.ExcelLimit, codes);

        // The title block of 'Sales' is a title and the date: the header is row 4, the first row row 5.
        Assert.Contains(check.Notes, n => n.Message.StartsWith("Sheet 'Sales', row 1, column 'Units' (B5):", StringComparison.Ordinal));
        Assert.Contains(check.Notes, n => n.Message.StartsWith("Sheet 'Sales', row 2, column 'Region' (A6):", StringComparison.Ordinal));
        Assert.Contains(check.Notes, n => n.Message.StartsWith("Sheet 'Sales', row 2 (E6):", StringComparison.Ordinal));
        Assert.Contains(check.Notes, n => n.Message.Contains("(D7)", StringComparison.Ordinal) && n.Code == NoteCodes.ExcelLimit);
        Assert.Contains(check.Notes, n => n.Message.Contains("'Q1/Q2', row 1, column 'A' (A2)", StringComparison.Ordinal) && n.Code == NoteCodes.ExcelLimit);
        Assert.Equal(13, check.Notes.Count(n => n.Code != NoteCodes.CharacterMissing));
    }

    [Fact]
    public void Generate_ThrowsTheFirstTenProblems()
    {
        var ex = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Invalid()));

        Assert.Equal(wwosik.DocLibs.DocumentConversions.DocumentFormat.Xlsx, ex.Format);
        Assert.StartsWith("The workbook has 13 problems:", ex.Reason, StringComparison.Ordinal);
        Assert.Equal(11, ex.Reason.Split("\n- ").Length - 1);
        Assert.EndsWith("… and 3 more.", ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_IsCappedAt500()
    {
        var rows = Enumerable.Range(0, 800).Select(_ => (IReadOnlyList<XlsxCell>)["text"]).ToList();
        var workbook = new XlsxWorkbook { Sheets = [new XlsxSheet { Name = "N", Columns = [new XlsxColumn("N", XlsxColumnType.Number)], Rows = rows }] };

        Assert.Equal(500, XlsxGenerator.Check(workbook, Samples.Options).Notes.Count);
    }

    [Fact]
    public void Check_OfAValidWorkbook_IsClean_WithStatistics()
    {
        var check = XlsxGenerator.Check(Samples.EveryType(), Samples.Options);

        Assert.True(check.IsClean, check.ToString());
        Assert.Equal(4, check.Statistics.Sheets);
        Assert.Equal(3, check.Statistics.Tables);
        Assert.Equal(0, check.Statistics.Diagrams);
        Assert.True(check.Statistics.Words > 30);
        Assert.StartsWith("4 sheets, ", check.Statistics.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Check_OfAnUnknownBrand_IsAnError()
    {
        var check = XlsxGenerator.Check(Samples.EveryType() with { Brand = "acme" }, Samples.Options);

        Assert.Contains("'acme'", check.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_IsPlainEncodedHtml()
    {
        var workbook = Samples.EveryType() with
        {
            Title = "<script>alert(1)</script>",
            Sheets = [.. Samples.EveryType().Sheets, new XlsxSheet { Name = "X", Title = "a\" style=\"x", Columns = [new XlsxColumn("<b>")], Rows = [["<img src=x onerror=y>"]] }],
        };
        var html = XlsxGenerator.Preview(workbook, Samples.Options);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\" style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" style=\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Equal(5, html.Split("<section>").Length - 1);
    }

    [Fact]
    public void Preview_ShowsValuesAsExcelDoes()
    {
        var html = XlsxGenerator.Preview(Samples.EveryType() with { Language = "de-DE" }, Samples.Options);

        Assert.Matches(@"<td class=""num"">15\.000,50\s€</td>", html);
        Assert.Contains("<td class=\"num\">1.200</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>15.01.2026</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>01234</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>TRUE</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_StopsAtMaxRows()
    {
        var html = XlsxGenerator.Preview(Samples.EveryType(), Samples.Options, maxRows: 1);

        Assert.Contains("… +2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("South", html, StringComparison.Ordinal);
    }
}
