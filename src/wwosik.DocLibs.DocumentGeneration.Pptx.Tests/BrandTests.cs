using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

public class BrandTests
{
    private const string Sections = "# Cover\n\nSubtitle\n\n## Text\n\n- point\n\n# Part two\n\n## More\n\nText\n";

    // A JPEG header with a 32 × 16 frame: all the generator reads of it.
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x20, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0xFF, 0xD9];

    private static readonly BrandTemplateOptions Sample = new() { Brand = Samples.Brand };

    [Fact]
    public void Cover_HasArtworkLogoAndTagline_AndTheTaglineCanBeLeftOut()
    {
        using var with = Samples.Open(Samples.TemplateBytes(Sample));
        using var without = Samples.Open(Samples.TemplateBytes(Sample with { ShowTagline = false }));

        Assert.Equal(["Artwork", "Logo", "Tagline"], PictureNames(Layout(with, P.SlideLayoutValues.Title)));
        Assert.Equal(["Artwork", "Logo"], PictureNames(Layout(without, P.SlideLayoutValues.Title)));
    }

    [Fact]
    public void Images_KeepTheirAspectRatio_AndTheArtworkSitsBottomRight_BelowTheText()
    {
        using var package = Samples.Open(Samples.TemplateBytes(Sample));
        var cover = Layout(package, P.SlideLayoutValues.Title);

        var logo = Extent(cover, "Logo");
        Assert.Equal(3.0, (double)logo.Width / logo.Height, 2); // logo.png is 240 × 80

        var artwork = Extent(cover, "Artwork");
        Assert.InRange(artwork.X + artwork.Width, 12191999, 12192000);
        Assert.InRange(artwork.Y + artwork.Height, 6857999, 6858000);
        Assert.True(artwork.Y >= 4480560 + 1005840, "The artwork starts below the subtitle.");
    }

    [Fact]
    public void SectionDivider_HasTheLogoOnDark_AndTheMasterTheLogoAndFooterText()
    {
        using var package = Samples.Open(Samples.TemplateBytes(Sample));
        var master = package.PresentationPart!.SlideMasterParts.Single();

        Assert.Equal(["Logo"], PictureNames(Layout(package, P.SlideLayoutValues.SectionHeader)));
        Assert.Contains("Logo", master.SlideMaster!.Descendants<P.Picture>().Select(p => p.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name!.Value));
        Assert.Contains("Sample Corporation", master.SlideMaster.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_HasTheBrandsColoursAndFontFamily()
    {
        using var package = Samples.Open(Samples.TemplateBytes(Sample));
        var theme = package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!;

        Assert.Equal("sample", theme.Name!.Value);
        Assert.Equal("Liberation Serif", theme.ThemeElements!.FontScheme!.MajorFont!.LatinFont!.Typeface!.Value);
        Assert.Equal("Liberation Serif", theme.ThemeElements.FontScheme.MinorFont!.LatinFont!.Typeface!.Value);
        Assert.Equal("0E7C86", theme.ThemeElements.ColorScheme!.Dark2Color!.RgbColorModelHex!.Val!.Value);
        Assert.Equal("B45309", theme.ThemeElements.ColorScheme.Hyperlink!.RgbColorModelHex!.Val!.Value);
    }

    [Fact]
    public void WithoutBrandFonts_TheThemeKeepsArial()
    {
        using var package = Samples.Open(Samples.TemplateBytes(new BrandTemplateOptions()));

        var fonts = package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!.ThemeElements!.FontScheme!;
        Assert.Equal("Arial", fonts.MajorFont!.LatinFont!.Typeface!.Value);
    }

    [Fact]
    public void TableCells_NameTheTableFamily_ExceptCode()
    {
        var markdown = Samples.Markdown("## A\n\nText\n\n| Head | Code |\n| --- | --- |\n| cell | `code` |\n");
        var withBrandTemplate = Samples.Brand with { Pptx = new PptxBrandTemplate(Samples.TemplateBytes(new BrandTemplateOptions())) };

        foreach (var brand in new[] { Samples.Brand, withBrandTemplate })
        {
            using var package = Samples.Open(Samples.Generate(markdown, new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } }));
            var runs = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Table>()).SelectMany(t => t.Descendants<A.Run>()).ToList();

            Assert.All(runs.Where(r => r.Text!.Text != "code"), r => Assert.Equal("PT Sans Narrow", r.RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value));
            Assert.Equal("Consolas", runs.Single(r => r.Text!.Text == "code").RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
            Assert.All(Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.Shape>()).SelectMany(s => s.Descendants<A.RunProperties>()), p => Assert.Null(p.GetFirstChild<A.LatinFont>()));
        }
    }

    [Fact]
    public void WithoutTableFonts_TableCellsKeepTheThemeFont()
    {
        var markdown = Samples.Markdown("## A\n\n| Head |\n| --- |\n| cell |\n");
        var brand = Samples.Brand with { Fonts = Samples.Brand.Fonts! with { Table = null } };

        foreach (var options in new[] { new PptxOptions(), new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } } })
        {
            using var package = Samples.Open(Samples.Generate(markdown, options));

            Assert.Empty(Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.LatinFont>()));
        }
    }

    [Fact]
    public void JpegImages_AreStoredAsJpeg()
    {
        var brand = new Brand { Name = "jpeg", Colors = new BrandColors { Primary = "111111" }, Logo = new BrandImage(Jpeg) };

        using var package = Samples.Open(Samples.TemplateBytes(new BrandTemplateOptions { Brand = brand }));

        Assert.All(package.GetAllParts().OfType<ImagePart>(), part => Assert.Equal("image/jpeg", part.ContentType));
        var logo = Extent(Layout(package, P.SlideLayoutValues.Title), "Logo");
        Assert.Equal(2.0, (double)logo.Width / logo.Height, 2);
    }

    [Fact]
    public void BrandTemplate_IsUsedWithItsLayoutNames_AndNeverGetsADate()
    {
        var potx = Samples.TemplateBytes(new BrandTemplateOptions());
        var brand = Samples.Brand with { Pptx = new PptxBrandTemplate(potx) { ContentLayout = "Title Only" } };
        var options = new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## A\n\nText\n"), options));

        Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(package)[0]));
        Assert.Equal("neutral", package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!.Name!.Value);
        Assert.Null(Samples.PlaceholderText(Samples.Slides(package)[0], P.PlaceholderValues.DateAndTime));
    }

    [Fact]
    public void BrandTemplate_TakesNoTemplateKeysFromFrontMatter()
    {
        var brand = Samples.Brand with { Pptx = new PptxBrandTemplate(Samples.TemplateBytes(new BrandTemplateOptions())) };

        var error = Assert.Throws<DocumentConversionException>(() =>
            Samples.Generate(Samples.Markdown("---\nshowDate: false\n---\n## A\n"), new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } }));

        Assert.Contains("'showDate' is not supported by the template 'sample'", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatter_SelectsARegisteredBrand()
    {
        var options = new PptxOptions { Brands = [Samples.Brand] };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\ntemplate: sample\nshowTagline: false\n---\n" + Sections), options));

        Assert.Equal("sample", package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!.Name!.Value);
        Assert.Equal(["Artwork", "Logo"], PictureNames(Layout(package, P.SlideLayoutValues.Title)));
    }

    [Fact]
    public void FrontMatter_UnknownBrand_ThrowsListingTheKnownOnes()
    {
        var error = Assert.Throws<DocumentConversionException>(() =>
            Samples.Generate(Samples.Markdown("---\ntemplate: other\n---\n## A\n"), new PptxOptions { Brands = [Samples.Brand] }));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains("expected 'neutral' or 'sample'", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoBrandsWithOneName_Throw() =>
        Assert.Throws<ArgumentException>(() =>
            Samples.Generate(Samples.Markdown("## A\n"), new PptxOptions { Brands = [Samples.Brand, Samples.Brand with { FooterText = "Other" }] }));

    [Fact]
    public void SampleBrand_IsWrittenForReview() =>
        Samples.WriteForReview("deck-sample.pptx", Samples.Generate(Samples.Deck(), new PptxOptions { Template = Sample }));

    private static SlideLayoutPart Layout(PresentationDocument package, P.SlideLayoutValues type) =>
        package.PresentationPart!.SlideMasterParts.Single().SlideLayoutParts.Single(l => l.SlideLayout!.Type!.Value == type);

    private static List<string> PictureNames(SlideLayoutPart layout) =>
        layout.SlideLayout!.Descendants<P.Picture>().Select(p => p.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name!.Value!).ToList();

    private static (long X, long Y, long Width, long Height) Extent(SlideLayoutPart layout, string picture)
    {
        var transform = layout.SlideLayout!.Descendants<P.Picture>()
            .Single(p => p.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name!.Value == picture)
            .ShapeProperties!.Transform2D!;
        return (transform.Offset!.X!.Value, transform.Offset.Y!.Value, transform.Extents!.Cx!.Value, transform.Extents.Cy!.Value);
    }
}
