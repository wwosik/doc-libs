using System.Runtime.CompilerServices;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public class MarkdownTests
{
    private const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    [Fact]
    public void FeatureSample_OpensWithoutRepair_WithoutNotes()
    {
        var (xlsx, result) = Generate(Sample());

        Samples.AssertValid(xlsx);
        Assert.Empty(result.Notes);
        Assert.Equal(5, result.Pages);
        Samples.WriteForReview("markdown-features.xlsx", xlsx);
        var workbook = Read(Sample());
        Assert.Equal(["Sales", "Orders", "Months"], workbook.Sheets.Select(s => s.Name));
        Assert.Equal("Sales by region", workbook.Sheets[0].Title);
        Assert.Equal("Net revenue per region.", workbook.Sheets[0].Description);
        Assert.Equal("First quarter 2026", workbook.Subtitle);
        Assert.Equal(new DateOnly(2026, 3, 31), workbook.Date);
        Assert.Equal(Classification.Internal, workbook.Marking!.Classification);
        Assert.Equal("Sales, orders and stock of the first quarter, with every kind of column.", workbook.Description);
    }

    [Fact]
    public void FeatureSample_MatchesTheExpectedPreview()
    {
        AssertMatchesExpected("features.preview", XlsxGenerator.Preview(Sample(), Samples.Options));
    }

    [Fact]
    public void Attributes_SetTypesAndSettings()
    {
        var sales = Read(Sample()).Sheets[0];

        Assert.Equal(
            [XlsxColumnType.Text, XlsxColumnType.Integer, XlsxColumnType.Currency, XlsxColumnType.Percent, XlsxColumnType.Number, XlsxColumnType.Date, XlsxColumnType.DateTime, XlsxColumnType.Boolean, XlsxColumnType.Text],
            sales.Columns.Select(c => c.Type!.Value));
        Assert.Equal(18, sales.Columns[0].Width);
        Assert.Equal(XlsxAlign.Left, sales.Columns[0].Align);
        Assert.Equal(XlsxAlign.Right, sales.Columns[1].Align);
        Assert.Equal(XlsxAlign.Center, sales.Columns[7].Align);
        Assert.Equal(XlsxAlign.Right, sales.Columns[2].Align);
        Assert.Equal("EUR", sales.Columns[2].Currency);
        Assert.Equal([XlsxTotal.Sum, XlsxTotal.Sum, XlsxTotal.Sum, XlsxTotal.Average, XlsxTotal.Count], sales.Columns.Select(c => c.Total).OfType<XlsxTotal>());
        Assert.Equal(1200, sales.Rows[0][1].Number);
        Assert.Equal(0.25, sales.Rows[0][3].Number, 10);
        Assert.Equal("01234", sales.Rows[0][8].Text);
        Assert.Equal("B|C", sales.Rows[2][8].Text);
        Assert.True(sales.Rows[2][7].Boolean);
        Assert.Equal(new DateTime(2025, 6, 1), sales.Rows[1][5].DateTime);
        Assert.Equal(new DateTime(2026, 3, 1, 14, 30, 0), sales.Rows[0][6].DateTime);
    }

    [Fact]
    public void Formulas_AndInferredCurrency()
    {
        var orders = Read(Sample()).Sheets[1];

        Assert.Equal(XlsxColumnType.Currency, orders.Columns[1].Type);
        Assert.Equal("USD", orders.Columns[1].Currency);
        Assert.Equal(XlsxColumnType.Integer, orders.Columns[2].Type);
        Assert.Null(orders.Columns[3].Type);
        Assert.Equal(XlsxCellKind.Formula, orders.Rows[0][3].Kind);
        Assert.Equal("[Price]*[Quantity]", orders.Rows[0][3].Text);
        Assert.Equal(XlsxColumnType.Date, orders.Columns[4].Type);
        Assert.Equal(XlsxColumnType.Boolean, orders.Columns[5].Type);
    }

    [Fact]
    public void Formulas_KeepWhatMarkdownWouldTakeApart()
    {
        var sheet = Read(Doc("## S\n\n| A | B | C |\n| - | - | - |\n| 1 | 2 | =[A]*[B]*2 |\n| 3 | 4 | =IF([A]>1,\"a\\|b\",\"c\") |\n")).Sheets[0];

        Assert.Equal("[A]*[B]*2", sheet.Rows[0][2].Text);
        Assert.Equal("IF([A]>1,\"a|b\",\"c\")", sheet.Rows[1][2].Text);
    }

    [Fact]
    public void Charts_InBothForms()
    {
        var workbook = Read(Sample());
        var sales = Assert.Single(workbook.Sheets[0].Charts);
        Assert.Equal("Region", sales.Category);
        Assert.Equal(["Revenue"], sales.Series);
        Assert.Equal("Ledger", sales.Source);
        var orders = Assert.Single(workbook.Sheets[1].Charts);
        Assert.Equal(XlsxChartKind.Bar, orders.Kind);
        Assert.Equal(XlsxChartPlacement.Sheet, orders.Placement);
        Assert.Equal("Quantities", orders.SheetName);

        var months = workbook.Sheets[2];
        Assert.Equal("Orders by month, from the controlling report.", months.Description);
        Assert.Equal(["month", "plan", "actual"], months.Columns.Select(c => c.Header));
        Assert.Equal([XlsxColumnType.Text, XlsxColumnType.Number, XlsxColumnType.Number], months.Columns.Select(c => c.Type!.Value));
        Assert.Equal(1, months.Columns[2].Decimals);
        Assert.Equal(132.5, months.Rows[0][2].Number);
        var chart = Assert.Single(months.Charts);
        Assert.Equal(XlsxChartKind.Line, chart.Kind);
        Assert.Equal(["plan", "actual"], chart.Series);
    }

    [Fact]
    public void DataChart_UnderATable_GoesToASheetOfItsOwn()
    {
        var markdown = "## Data\n\n| A | B |\n| - | - |\n| x | 1 |\n\n```chart\n---\nm, v\nJan, 1\n```\n";
        var (xlsx, result) = Generate(Doc(markdown));

        Samples.AssertValid(xlsx);
        var note = Assert.Single(result.Notes);
        Assert.Equal((NoteCodes.SheetSplit, 7), (note.Code, note.Line));
        Assert.Equal(["Data", "Data 2"], Read(Doc(markdown)).Sheets.Select(s => s.Name));
    }

    [Theory]
    [InlineData("en", "1,234.50", "$1,234.50", "25%", "3/9/2026", "2026-03-09 14:30", "true", "1,200")]
    [InlineData("de", "1.234,50", "1.234,50 €", "25 %", "09.03.2026", "2026-03-09 14:30", "wahr", "1.200")]
    public void Inference_GivesTheSameTypes_InEachLanguage(string lang, string number, string amount, string percent, string date, string dateTime, string boolean, string integer)
    {
        var rows = string.Join("\n", Enumerable.Range(0, 3).Select(_ => $"| {number} | {amount} | {percent} | {date} | {dateTime} | {boolean} | {integer} | 0123 |"));
        var markdown = $"---\nlang: {lang}\n---\n\n## S\n\n| N | C | P | D | T | B | I | X |\n| - | - | - | - | - | - | - | - |\n{rows}\n";
        var sheet = Read(Doc(markdown)).Sheets[0];

        Assert.Equal(
            [XlsxColumnType.Number, XlsxColumnType.Currency, XlsxColumnType.Percent, XlsxColumnType.Date, XlsxColumnType.DateTime, XlsxColumnType.Boolean, XlsxColumnType.Integer, XlsxColumnType.Text],
            sheet.Columns.Select(c => c.Type!.Value));
        Assert.Equal(1234.5, sheet.Rows[0][0].Number);
        Assert.Equal(1234.5, sheet.Rows[0][1].Number);
        Assert.Equal(new DateTime(2026, 3, 9), sheet.Rows[0][3].DateTime);
        Assert.Equal(1200, sheet.Rows[0][6].Number);
        Assert.Empty(Generate(Doc(markdown)).Result.Notes);
    }

    [Fact]
    public void ColumnMixed_KeepsTheOddValuesAsText_WithTheirLine()
    {
        var markdown = "## S\n\n| Amount |\n| - |\n| 1.5 |\n| 2 |\n| 1,5 |\n";
        var (xlsx, result) = Generate(Doc(markdown));

        Samples.AssertValid(xlsx);
        var note = Assert.Single(result.Notes);
        Assert.Equal((NoteCodes.ColumnMixed, 7), (note.Code, note.Line));
        var sheet = Read(Doc(markdown)).Sheets[0];
        Assert.Equal(XlsxColumnType.Number, sheet.Columns[0].Type);
        Assert.Equal("1,5", sheet.Rows[2][0].Text);
    }

    [Fact]
    public void MostlyText_StaysText()
    {
        var sheet = Read(Doc("## S\n\n| A |\n| - |\n| 1 |\n| x |\n| y |\n")).Sheets[0];

        Assert.Equal(XlsxColumnType.Text, sheet.Columns[0].Type);
        Assert.Equal("1", sheet.Rows[0][0].Text);
    }

    [Fact]
    public void SheetSplit_AndContentIgnored_ComeWithTheirLines()
    {
        var markdown = "## Sales\n\n| A |\n| - |\n| x |\n\nAbove the second.\n\n| B |\n| - |\n| y |\n\n- a list\n\n### Deeper\n\n> quote\n\n```js\nx\n```\n\n## Empty\n\nNo table here.\n";
        var (xlsx, result) = Generate(Doc(markdown));

        Samples.AssertValid(xlsx);
        Assert.Equal(
            [(NoteCodes.SheetSplit, 9), (NoteCodes.ContentIgnored, 13), (NoteCodes.ContentIgnored, 15), (NoteCodes.ContentIgnored, 17), (NoteCodes.ContentIgnored, 19), (NoteCodes.ContentIgnored, 23)],
            result.Notes.Select(n => (n.Code, n.Line)));
        var sheets = Read(Doc(markdown)).Sheets;
        Assert.Equal(["Sales", "Sales 2"], sheets.Select(s => s.Name));
        Assert.Equal("Above the second.", sheets[1].Description);
    }

    [Fact]
    public void TableBeforeAnyHeading_IsTheSheetTable()
    {
        Assert.Equal("Table", Read(Doc("| A |\n| - |\n| x |\n")).Sheets[0].Name);
        Assert.Equal("Tabelle", Read(Doc("---\nlang: de\n---\n| A |\n| - |\n| x |\n")).Sheets[0].Name);
    }

    [Fact]
    public void UnknownAttribute_IsColumnInvalid_WithASuggestion()
    {
        var markdown = "## S\n\n| Amount {.curency totl=sum} |\n| - |\n| 1 |\n";
        var check = XlsxGenerator.Check(Doc(markdown), Samples.Options);

        Assert.NotNull(check.Error);
        Assert.Equal([NoteCodes.ColumnInvalid, NoteCodes.ColumnInvalid], check.Notes.Select(n => n.Code));
        Assert.All(check.Notes, n => Assert.Equal(3, n.Line));
        Assert.Contains("'.currency'", check.Notes[0].Message, StringComparison.Ordinal);
        Assert.Contains("'total'", check.Notes[1].Message, StringComparison.Ordinal);
        var ex = Assert.Throws<DocumentConversionException>(() => Generate(Doc(markdown)));
        Assert.Equal(Format.Xlsx, ex.Format);
        Assert.Contains("line 3: ", ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredType_RejectsValuesThatDoNotFit()
    {
        var check = XlsxGenerator.Check(Doc("## S\n\n| A {.number} |\n| - |\n| 1 |\n| x |\n"), Samples.Options);

        var problem = Assert.Single(check.Notes);
        Assert.Equal((NoteCodes.CellType, 6), (problem.Code, problem.Line));
    }

    [Fact]
    public void ColumnChart_WithoutATable_IsChartInvalid()
    {
        var check = XlsxGenerator.Check(Doc("## S\n\n```chart\ncategory: A\nseries: B\n```\n"), Samples.Options);

        Assert.Contains(check.Notes, n => n.Code == NoteCodes.ChartInvalid && n.Line == 3);
    }

    [Fact]
    public void ChartColumns_AreCheckedByThePlan()
    {
        var check = XlsxGenerator.Check(Doc("## S\n\n| A | B |\n| - | - |\n| x | 1 |\n\n```chart\ncategory: A\nseries: C\ntyp: line\n```\n"), Samples.Options);

        var problem = Assert.Single(check.Notes);
        Assert.Contains("'type'", problem.Message, StringComparison.Ordinal);
        Assert.Contains("chart-invalid", XlsxGenerator.Check(Doc("## S\n\n| A | B |\n| - | - |\n| x | 1 |\n\n```chart\ncategory: A\nseries: C\n```\n"), Samples.Options).Notes.Single().Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Versions_LeaveOutInternalSheets()
    {
        var markdown = "## Public\n\n| A |\n| - |\n| x |\n\n## Team {.internal}\n\n| B |\n| - |\n| y |\n";

        Assert.Equal(["Public"], Read(Doc(markdown)).Sheets.Select(s => s.Name));
        Assert.Equal(["Public", "Team"], Read(Doc(markdown), Samples.Options with { Audience = Audience.Internal }).Sheets.Select(s => s.Name));
        Assert.Equal(["Public", "Team"], Read(Doc("---\naudience: internal\n---\n" + markdown)).Sheets.Select(s => s.Name));
    }

    [Fact]
    public void FrontMatter_InfoShowDateAndKeysOfOtherFormats()
    {
        var markdown = "---\ninfo: false\nshowDate: false\ntoc: true\ntransition: fade\n---\n# Title\n\n## S\n\n| A |\n| - |\n| x |\n";
        var (xlsx, _) = Generate(Doc(markdown));

        Samples.AssertValid(xlsx);
        Assert.DoesNotContain("<h2>Title</h2>", XlsxGenerator.Preview(Doc(markdown), Samples.Options), StringComparison.Ordinal);
        Assert.Contains("'colour'", Assert.Throws<DocumentConversionException>(() => Generate(Doc("---\ncolour: red\n---\n| A |\n| - |\n| x |\n"))).Reason, StringComparison.Ordinal);
        Assert.NotNull(XlsxGenerator.Check(Doc("---\ninfo: maybe\n---\n| A |\n| - |\n| x |\n"), Samples.Options).Error);
    }

    [Fact]
    public void ForOptions_IsAnXlsxGenerator_ForTheTool()
    {
        var generator = XlsxGenerator.ForOptions(Samples.Options);
        using var output = new MemoryStream();

        Assert.Equal(("xlsx", ".xlsx"), (generator.Format, generator.FileExtension));
        Assert.Equal(XlsxGenerator.AuthoringGuide(Samples.Options), generator.AuthoringGuide());
        Assert.Equal(5, generator.Generate(Sample(), output).Pages);
        Samples.AssertValid(output.ToArray());
        Assert.Null(generator.Check(Sample()).Error);
        Assert.Single(new DocumentTool([generator]).Generators);
    }

    internal static MarkdownDocument Sample() =>
        Doc(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "features.md")).ReplaceLineEndings("\n"));

    internal static MarkdownDocument Doc(string markdown) => new(markdown, [], new Dictionary<string, string>());

    internal static (byte[] Xlsx, GenerationResult Result) Generate(MarkdownDocument document, XlsxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = XlsxGenerator.Generate(document, output, options ?? Samples.Options);
        return (output.ToArray(), result);
    }

    internal static void AssertMatchesExpected(string sample, string actual, [CallerFilePath] string caller = "")
    {
        var name = sample + ".expected." + (actual.StartsWith('<') ? "html" : "md");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(caller)!, "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name)).ReplaceLineEndings("\n"), actual);
    }

    private static XlsxWorkbook Read(MarkdownDocument document, XlsxOptions? options = null) => XlsxMarkdown.Read(document, options ?? Samples.Options).Workbook;
}
