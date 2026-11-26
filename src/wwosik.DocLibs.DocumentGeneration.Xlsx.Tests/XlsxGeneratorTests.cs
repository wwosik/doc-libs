using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public class XlsxGeneratorTests
{
    [Fact]
    public void EveryColumnType_OnThreeSheets_IsValid()
    {
        var xlsx = Samples.Generate(Samples.EveryType());
        Samples.WriteForReview("every-type.xlsx", xlsx);

        Samples.AssertValid(xlsx);
        using var package = Samples.Open(xlsx);
        var names = package.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(["Info", "Sales", "Stock", "Staff"], names);
    }

    [Fact]
    public void WithTheSampleBrand_LogoAndHeaderPicture_AreValid()
    {
        var xlsx = Samples.Generate(
            Samples.EveryType() with { Marking = new XlsxMarking(Classification.Confidential, Draft: true) },
            Samples.Options with { Brand = Samples.Brand });
        Samples.WriteForReview("every-type-sample.xlsx", xlsx);

        Samples.AssertValid(xlsx);
        var entries = Samples.Entries(xlsx);
        Assert.Contains(entries, e => e.StartsWith("xl/drawings/", StringComparison.Ordinal) && e.EndsWith(".vml", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.StartsWith("xl/media/", StringComparison.Ordinal));
        var sales = Samples.Part(xlsx, "xl/worksheets/sheet2.xml");
        Assert.Contains("&amp;CConfidential · Draft", sales.Replace("&amp;\"PT Sans,Bold\"&amp;9", string.Empty, StringComparison.Ordinal).Replace("&amp;\"Liberation Serif,Bold\"&amp;9", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("&amp;R&amp;G", sales, StringComparison.Ordinal);
        Assert.Contains("<legacyDrawingHF", sales, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarking_IsNeverAProperty()
    {
        var xlsx = Samples.Generate(Samples.EveryType() with { Marking = new XlsxMarking(Classification.Confidential) });

        Assert.DoesNotContain(Samples.Entries(xlsx), e => e.Contains("custom", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Confidential", Samples.Part(xlsx, "docProps/core.xml"), StringComparison.Ordinal);
        Assert.Contains("Confidential", Samples.Part(xlsx, "xl/sharedStrings.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeader_IsFrozenFiltered_AndBandedByAFormula()
    {
        var xlsx = Samples.Generate(Samples.EveryType());
        var sales = Samples.Part(xlsx, "xl/worksheets/sheet2.xml");

        // Title, description, date, a blank row: the header is row 5.
        Assert.Contains("<pane ySplit=\"5\" topLeftCell=\"A6\" activePane=\"bottomLeft\" state=\"frozen\"/>", sales, StringComparison.Ordinal);
        Assert.Contains("<autoFilter ref=\"A5:F8\"/>", sales, StringComparison.Ordinal);
        Assert.Contains("<conditionalFormatting sqref=\"A6:F8\">", sales, StringComparison.Ordinal);
        Assert.Contains("<formula>MOD(ROW()-5,2)=0</formula>", sales, StringComparison.Ordinal);
        Assert.Contains("showGridLines=\"0\"", sales, StringComparison.Ordinal);

        // A sheet without a title starts with its header.
        var stock = Samples.Part(xlsx, "xl/worksheets/sheet3.xml");
        Assert.Contains("<pane ySplit=\"1\" topLeftCell=\"A2\"", stock, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintSetup_IsLandscapeOnePageWide_WithTheHeaderRepeated()
    {
        var xlsx = Samples.Generate(Samples.EveryType(), Samples.Options with { PageSize = XlsxPageSize.Letter });
        var sales = Samples.Part(xlsx, "xl/worksheets/sheet2.xml");
        var workbook = Samples.Part(xlsx, "xl/workbook.xml");

        Assert.Contains("<pageSetup paperSize=\"1\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>", sales, StringComparison.Ordinal);
        Assert.Contains("<pageSetUpPr fitToPage=\"1\"/>", sales, StringComparison.Ordinal);
        Assert.Contains("<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"1\">'Sales'!$5:$5</definedName>", workbook, StringComparison.Ordinal);
        Assert.Contains("Quarterly figures", sales, StringComparison.Ordinal);
        Assert.Contains("&amp;R&amp;9Page &amp;P of &amp;N", sales, StringComparison.Ordinal);
    }

    [Fact]
    public void GermanTexts_NameTheInfoSheetAndThePages()
    {
        var xlsx = Samples.Generate(Samples.EveryType() with { Language = "de" });

        Assert.Contains("Seite &amp;P von &amp;N", Samples.Part(xlsx, "xl/worksheets/sheet2.xml"), StringComparison.Ordinal);
        Assert.Contains(">Autor<", Samples.Part(xlsx, "xl/sharedStrings.xml"), StringComparison.Ordinal);
        Assert.Contains("<dc:language>de</dc:language>", Samples.Part(xlsx, "docProps/core.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void TextStaysText_WithoutTheWarning()
    {
        var xlsx = Samples.Generate(Samples.EveryType());
        var stock = Samples.Part(xlsx, "xl/worksheets/sheet3.xml");
        var strings = Samples.Part(xlsx, "xl/sharedStrings.xml");
        using var package = Samples.Open(xlsx);
        var sheet = (WorksheetPart)package.WorkbookPart!.GetPartById("rId3");
        var cell = sheet.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().Single(c => c.CellReference == "A2");
        var format = package.WorkbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<DocumentFormat.OpenXml.Spreadsheet.CellFormat>().ElementAt((int)cell.StyleIndex!.Value);

        Assert.Equal(DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString, cell.DataType!.Value);
        Assert.Contains(">01234<", strings, StringComparison.Ordinal);
        Assert.Equal(49u, format.NumberFormatId!.Value);
        Assert.Contains("<ignoredError sqref=\"A1:D1 A2:A4\" numberStoredAsText=\"1\"/>", stock, StringComparison.Ordinal);
        Assert.Contains("B&amp;C &lt;x&gt;", strings, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_AreTyped()
    {
        var xlsx = Samples.Generate(Samples.EveryType());
        var stock = Samples.Part(xlsx, "xl/worksheets/sheet3.xml");
        var sales = Samples.Part(xlsx, "xl/worksheets/sheet2.xml");

        Assert.Contains("<c r=\"B2\" s=\"", stock, StringComparison.Ordinal);
        Assert.Contains("><v>46037</v></c>", stock, StringComparison.Ordinal); // 2026-01-15
        Assert.Contains("><v>46082.604166666664</v></c>", stock, StringComparison.Ordinal); // 2026-03-01 14:30
        Assert.Contains("t=\"b\"><v>1</v>", stock, StringComparison.Ordinal);
        Assert.Contains("><v>15000.5</v></c>", sales, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de-DE", "#,##0.00\\ \"€\"", "#,##0.00\\ \"$\"", "dd\\.mm\\.yyyy", "0.0\\ %")]
    [InlineData("fr-FR", "#,##0.00\\ \"€\"", "#,##0.00\\ \"$\"", "dd\\/mm\\/yyyy", "0.0\\ %")]
    [InlineData("en-US", "\"€\"#,##0.00", "\"$\"#,##0.00", "m\\/d\\/yyyy", "0.0%")]
    [InlineData("en-GB", "\"€\"#,##0.00", "\"$\"#,##0.00", "dd\\/mm\\/yyyy", "0.0%")]
    public void Formats_FollowTheCulture(string culture, string euro, string dollar, string date, string percent)
    {
        var xlsx = Samples.Generate(Samples.EveryType(), Samples.Options with { Culture = CultureInfo.GetCultureInfo(culture) });
        using var package = Samples.Open(xlsx);
        var codes = package.WorkbookPart!.WorkbookStylesPart!.Stylesheet.NumberingFormats!
            .Elements<DocumentFormat.OpenXml.Spreadsheet.NumberingFormat>().Select(f => f.FormatCode!.Value!).ToList();

        Assert.Contains(codes, c => c.StartsWith(euro + ";", StringComparison.Ordinal));
        Assert.Contains(codes, c => c.StartsWith(dollar + ";", StringComparison.Ordinal));
        Assert.Contains(date, codes);
        Assert.Contains(codes, c => c.StartsWith(percent + ";", StringComparison.Ordinal));
        Assert.Contains("#,##0.000", codes);
        Assert.Contains("#,##0", codes);
        Samples.AssertValid(xlsx);
    }

    [Fact]
    public void TheLanguage_PicksTheCulture()
    {
        var xlsx = Samples.Generate(Samples.EveryType() with { Language = "de" });

        Assert.Contains("formatCode=\"dd\\.mm\\.yyyy\"", Samples.Part(xlsx, "xl/styles.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void SameWorkbookAndClock_GiveTheSameBytes()
    {
        var options = Samples.Options with { Brand = Samples.Brand };
        var one = Samples.Generate(Samples.EveryType(), options);
        Thread.Sleep(2100);
        var two = Samples.Generate(Samples.EveryType(), options);

        Assert.Equal(one, two);
    }

    [Fact]
    public void CoreProperties_HoldTitleSubjectCreatorDescriptionAndLanguage()
    {
        var core = Samples.Part(Samples.Generate(Samples.EveryType()), "docProps/core.xml");

        Assert.Contains("<dc:title>Quarterly figures</dc:title>", core, StringComparison.Ordinal);
        Assert.Contains("<dc:subject>First quarter 2026</dc:subject>", core, StringComparison.Ordinal);
        Assert.Contains("<dc:creator>DocLibs</dc:creator>", core, StringComparison.Ordinal);
        Assert.Contains("<dc:description>Sales, stock", core, StringComparison.Ordinal);
        Assert.Contains("<dc:language>en</dc:language>", core, StringComparison.Ordinal);
        Assert.Contains("2026-03-09T10:30:00Z", core, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInfoSheet_IsLeftOut_WithoutTitleAndDescription_OrWhenSwitchedOff()
    {
        var plain = Samples.EveryType() with { Title = null, Description = null };

        Assert.Equal(3, XlsxGenerator.Generate(plain, new MemoryStream(), Samples.Options).Pages);
        Assert.Equal(4, XlsxGenerator.Generate(plain, new MemoryStream(), Samples.Options with { Info = true }).Pages);
        Assert.Equal(3, XlsxGenerator.Generate(Samples.EveryType(), new MemoryStream(), Samples.Options with { Info = false }).Pages);
    }

    [Fact]
    public void TheInfoSheet_LinksEverySheet()
    {
        var info = Samples.Part(Samples.Generate(Samples.EveryType()), "xl/worksheets/sheet1.xml");

        Assert.Contains("location=\"'Sales'!A1\"", info, StringComparison.Ordinal);
        Assert.Contains("location=\"'Staff'!A1\"", info, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInfoSheet_TakesAFreeName()
    {
        var workbook = Samples.EveryType() with { Sheets = [new XlsxSheet { Name = "info", Columns = [new XlsxColumn("A")] }] };
        var xlsx = Samples.Generate(workbook);

        Assert.Contains("name=\"Info 2\"", Samples.Part(xlsx, "xl/workbook.xml"), StringComparison.Ordinal);
        Samples.AssertValid(xlsx);
    }

    [Fact]
    public void ADarkBrandsColours_AreDarkenedOntoWhite()
    {
        var light = new Brand { Name = "light", Colors = new BrandColors { Primary = "FFE066", TableHeader = "A5F3FC", TableBand = "1E293B" } };
        var plan = XlsxPlan.Of(Samples.EveryType(), Samples.Options with { Brand = light });

        Assert.True(ResolvedBrand.Contrast(plan.Colors.Title, "FFFFFF") >= 4.5);
        Assert.True(ResolvedBrand.Contrast(plan.Colors.Header, "FFFFFF") >= 4.5);
        Assert.True(ResolvedBrand.Contrast(plan.Colors.Band, "000000") >= 4.5);
        Samples.AssertValid(Samples.Generate(Samples.EveryType(), Samples.Options with { Brand = light }));
    }

    [Fact]
    public void Widths_AreMeasured_WithinSixToFifty()
    {
        var workbook = new XlsxWorkbook
        {
            Sheets =
            [
                new XlsxSheet
                {
                    Name = "W",
                    Columns = [new XlsxColumn("A"), new XlsxColumn("Long"), new XlsxColumn("Set") { Width = 80 }],
                    Rows = [["x", new string('w', 200), "y"]],
                },
            ],
        };
        var plan = XlsxPlan.Of(workbook, Samples.Options);

        Assert.Equal(6, plan.Sheets[0].Columns[0].Width);
        Assert.Equal(50, plan.Sheets[0].Columns[1].Width);
        Assert.Equal(80, plan.Sheets[0].Columns[2].Width);
    }

    [Fact]
    public void AMissingCharacter_GivesANote()
    {
        var workbook = new XlsxWorkbook
        {
            Sheets = [new XlsxSheet { Name = "S", Columns = [new XlsxColumn("Name")], Rows = [["中文"], ["中"]] }],
        };
        using var output = new MemoryStream();
        var result = XlsxGenerator.Generate(workbook, output, Samples.Options with { Brand = Samples.Brand });

        var notes = result.Notes.Where(n => n.Code == NoteCodes.CharacterMissing).ToList();
        Assert.Equal(2, notes.Count);
        Assert.Contains("(A2)", notes[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALowerWorkbookMarking_KeepsTheHosts()
    {
        using var output = new MemoryStream();
        var result = XlsxGenerator.Generate(
            Samples.EveryType() with { Marking = new XlsxMarking(Classification.Internal) },
            output,
            Samples.Options with { Marking = new XlsxMarking(Classification.Confidential) });

        Assert.Equal(NoteCodes.ClassificationKept, Assert.Single(result.Notes).Code);
    }

    [Fact]
    public void AnUnknownBrand_Throws()
    {
        var ex = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.EveryType() with { Brand = "acme" }));

        Assert.Equal(wwosik.DocLibs.DocumentConversions.DocumentFormat.Xlsx, ex.Format);
        Assert.Contains("'acme'", ex.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1900-01-01", 1)]
    [InlineData("1900-02-28", 59)]
    [InlineData("1900-03-01", 61)]
    [InlineData("2026-01-15", 46037)]
    public void Serials_KeepExcelsLeapDay(string date, double serial)
    {
        var value = DateTime.Parse(date, CultureInfo.InvariantCulture);
        Assert.Equal(serial, XlsxPlan.Serial(value));
    }
}
