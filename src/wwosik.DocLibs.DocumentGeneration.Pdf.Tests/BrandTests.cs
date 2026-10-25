using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

public class BrandTests
{
    private const string Markdown = "# Title\n\nSubtitle\n\n## Section\n\nText with `code`.\n";

    private static readonly PdfOptions Sample = new() { Template = new BrandPdfTemplateOptions { Brand = Samples.Brand } };

    [Fact]
    public void Cover_ShowsArtworkTaglineAndLogo_AndTheTaglineCanBeLeftOut()
    {
        using var with = PdfDocument.Open(Samples.Generate(Samples.Markdown(Markdown), Sample));
        using var without = PdfDocument.Open(Samples.Generate(
            Samples.Markdown(Markdown),
            Sample with { Template = new BrandPdfTemplateOptions { Brand = Samples.Brand, ShowTagline = false } }));

        Assert.Equal(3, with.GetPage(1).GetImages().Count());
        Assert.Equal(2, without.GetPage(1).GetImages().Count());
    }

    [Fact]
    public void ContentPages_ShowTheLogoAndTheFooterText()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown(Markdown), Sample));

        Assert.Single(document.GetPage(2).GetImages());
        Assert.Contains("Sample Corporation", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample Corporation", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandFonts_AreEmbedded_WithTheBrandMonoForCode()
    {
        var raw = System.Text.Encoding.Latin1.GetString(Samples.Generate(Samples.Markdown(Markdown), Sample));

        Assert.Contains("LiberationSerif", raw, StringComparison.Ordinal);
        Assert.Contains("Inconsolata", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("LiberationMono", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("LiberationSans", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutBrandMono_CodeIsLiberationMono()
    {
        var fonts = Samples.Brand.Fonts!;
        var brand = Samples.Brand with { Fonts = fonts with { Mono = null, MonoBold = null } };

        var raw = System.Text.Encoding.Latin1.GetString(Samples.Generate(
            Samples.Markdown(Markdown),
            new PdfOptions { Template = new BrandPdfTemplateOptions { Brand = brand } }));

        Assert.Contains("LiberationMono", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Inconsolata", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void TableCells_AreInTheTableFaces_WithCodeStayingMono()
    {
        var markdown = Samples.Markdown("## A\n\nBody text\n\n| Head | Code |\n| --- | --- |\n| cell *slanted* | `code` |\n");

        using var document = PdfDocument.Open(Samples.Generate(markdown, Sample));
        var letters = document.GetPages().SelectMany(p => p.Letters).ToList();

        string FontOf(string text) => letters.First(l => l.Value == text[0].ToString() && Word(letters, l).StartsWith(text, StringComparison.Ordinal)).FontName ?? string.Empty;
        Assert.Contains("LiberationSerif", FontOf("Body"), StringComparison.Ordinal);
        Assert.Contains("PTSans-NarrowBold", FontOf("Head"), StringComparison.Ordinal);
        Assert.Contains("PTSans-Narrow", FontOf("cell"), StringComparison.Ordinal);
        Assert.Contains("PTSans-Narrow", FontOf("slanted"), StringComparison.Ordinal); // no table italic: the upright face
        Assert.Contains("Inconsolata", FontOf("code"), StringComparison.Ordinal);
    }

    [Fact]
    public void TableFaces_MeasureTheColumns()
    {
        static int TableLines(Brand brand)
        {
            var resolved = ResolvedBrand.Resolve(brand);
            var engine = new LayoutEngine(FontSet.For(resolved), PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait), new Design(resolved, false, null, null));
            IReadOnlyList<TextRun> Cell(string text) => [new TextRun(text, RunStyle.None)];
            var words = string.Join(' ', Enumerable.Repeat("condensed", 60));
            var table = new TableBlock([[Cell("A"), Cell("B")], [Cell(words), Cell(words)]], [CellAlignment.Default, CellAlignment.Default], HasHeader: true);
            return engine.Run(new DocumentModel(null, [table])).Pages[0].Items.OfType<TextItem>().Select(t => t.Baseline).Distinct().Count();
        }

        var withoutTableFonts = Samples.Brand with { Fonts = Samples.Brand.Fonts! with { Table = null } };

        Assert.True(TableLines(Samples.Brand) < TableLines(withoutTableFonts), "The condensed face fits more words on a line.");
    }

    [Theory]
    [InlineData("neutral", 2)]
    [InlineData("sample without table fonts", 2)]
    [InlineData("sample", 3)] // text regular, table regular and bold
    public void EachFontFile_IsEmbeddedOnce(string brand, int files)
    {
        var options = new PdfOptions
        {
            Template = new BrandPdfTemplateOptions
            {
                Brand = brand switch
                {
                    "neutral" => Brand.Neutral,
                    "sample" => Samples.Brand,
                    _ => Samples.Brand with { Fonts = Samples.Brand.Fonts! with { Table = null } },
                },
            },
        };

        var raw = System.Text.Encoding.Latin1.GetString(Samples.Generate(Samples.Markdown("Text\n\n| Head |\n| --- |\n| cell |\n"), options));

        Assert.Equal(files, raw.Split("/FontFile2").Length - 1);
    }

    [Fact]
    public void ACharacterTheBrandFontLacks_Throws_OrIsReplaced()
    {
        var markdown = Samples.Markdown("## A\n\nDone ✓\n");

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(markdown, Sample));
        var replaced = Samples.RoundTrip(Samples.Generate(markdown, Sample with { ReplaceUnsupported = _ => "[x]" }));

        Assert.Contains("'✓' (U+2713)", error.Reason, StringComparison.Ordinal);
        Assert.Contains(@"Done \[x]", replaced, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatter_SelectsARegisteredBrand_AndItsKeys()
    {
        var options = new PdfOptions { Brands = [Samples.Brand] };

        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\ntemplate: Sample\nshowTagline: false\n---\n" + Markdown), options));

        Assert.Equal(2, document.GetPage(1).GetImages().Count());
        Assert.Contains("Sample Corporation", document.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatter_NamingTheCallersBrand_KeepsTheCallersValues()
    {
        var options = Sample with { Template = new BrandPdfTemplateOptions { Brand = Samples.Brand, ShowTagline = false } };

        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\ntemplate: sample\n---\n" + Markdown), options));

        Assert.Equal(2, document.GetPage(1).GetImages().Count());
    }

    [Fact]
    public void FrontMatter_SwitchingToNeutral_DropsTheBrand()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\ntemplate: neutral\n---\n" + Markdown), Sample));

        Assert.Empty(document.GetPages().SelectMany(p => p.GetImages()));
    }

    [Fact]
    public void FrontMatter_UnknownBrand_ThrowsListingTheKnownOnes()
    {
        var error = Assert.Throws<DocumentConversionException>(() =>
            Samples.Generate(Samples.Markdown("---\ntemplate: other\n---\n" + Markdown), new PdfOptions { Brands = [Samples.Brand] }));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains("'template' has the value 'other'; expected 'neutral' or 'sample'", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidBrand_ThrowsArgumentException()
    {
        var brand = new Brand { Name = "broken", Colors = new BrandColors { Primary = "teal" } };

        var error = Assert.Throws<ArgumentException>(() =>
            Samples.Generate(Samples.Markdown(Markdown), new PdfOptions { Template = new BrandPdfTemplateOptions { Brand = brand } }));

        Assert.Contains("Colors.Primary", error.Message, StringComparison.Ordinal);
    }

    // The text from a letter to the next space, on the letter's line.
    private static string Word(List<UglyToad.PdfPig.Content.Letter> letters, UglyToad.PdfPig.Content.Letter first) =>
        string.Concat(letters.SkipWhile(l => !ReferenceEquals(l, first)).TakeWhile(l => l.Value != " " && l.StartBaseLine.Y == first.StartBaseLine.Y).Select(l => l.Value));

    [Fact]
    public void SampleBrand_IsWrittenForReview()
    {
        Samples.WriteForReview("report-sample.pdf", Samples.Generate(Samples.Document("report.md"), Sample));
    }
}
