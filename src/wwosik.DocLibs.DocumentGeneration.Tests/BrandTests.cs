namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class BrandTests
{
    private static readonly BrandImage Png = new(File.ReadAllBytes(Sample("logo.png")));

    private static readonly BrandFont Serif = new(File.ReadAllBytes(Sample("fonts/LiberationSerif-Regular.ttf")));

    private static readonly BrandFont Narrow = new(File.ReadAllBytes(Sample("fonts/table/PT_Sans-Narrow-Web-Regular.ttf")));

    private static readonly BrandFont NarrowBold = new(File.ReadAllBytes(Sample("fonts/table/PT_Sans-Narrow-Web-Bold.ttf")));

    private static readonly BrandFonts SerifFonts = new() { Regular = Serif, Bold = Serif, Italic = Serif, BoldItalic = Serif };

    [Fact]
    public void MinimalBrand_DerivesEveryColourFromThePrimary()
    {
        var brand = ResolvedBrand.Resolve(new Brand { Name = "Acme", Colors = new BrandColors { Primary = "#0b5fff" } });

        Assert.Equal("acme", brand.Name);
        Assert.Equal("0B5FFF", brand.Primary);
        Assert.Equal("0B5FFF", brand.Link);
        Assert.Equal("0B5FFF", brand.TableHeader);
        Assert.Equal("E7EFFF", brand.TableBand);
        Assert.Equal("6B7280", brand.Footer);
        Assert.Equal("D1D5DB", brand.TableLine);
        Assert.Equal(["0B5FFF", "6D9FFF"], brand.Gradient);
        Assert.Equal(["000000", "FFFFFF", "0B5FFF", "E7EFFF", "0B5FFF", "0F766E", "B45309", "7C3AED", "BE123C", "4B5563", "0B5FFF", "6D28D9"], brand.ThemeColors);
        Assert.Null(brand.Fonts);
        Assert.Equal("Arial", brand.FontFamily);
    }

    [Fact]
    public void GradientDefault_RunsToTheLinkColour_WhenItDiffers()
    {
        var brand = ResolvedBrand.Resolve(new Brand { Name = "a", Colors = new BrandColors { Primary = "111111", Link = "222222" } });

        Assert.Equal(["111111", "222222"], brand.Gradient);
    }

    [Fact]
    public void Neutral_KeepsItsColours()
    {
        var brand = ResolvedBrand.Neutral;

        Assert.Equal("neutral", brand.Name);
        Assert.Equal("1F2937", brand.Primary);
        Assert.Equal("1D4ED8", brand.Link);
        Assert.Equal(["1F2937", "1D4ED8"], brand.Gradient);
        Assert.Null(brand.Logo);
        Assert.Null(brand.FooterText);
    }

    [Theory]
    [InlineData("acme corp", "Name")]
    [InlineData("-acme", "Name")]
    [InlineData("", "Name")]
    public void InvalidName_Throws(string name, string property)
    {
        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = name }));

        Assert.Contains(property, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("blue", "Colors.Primary")]
    [InlineData("#12345", "Colors.Primary")]
    [InlineData("#1234567", "Colors.Primary")]
    public void InvalidColour_ThrowsNamingTheProperty(string color, string property)
    {
        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Colors = new BrandColors { Primary = color } }));

        Assert.Contains(property, error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{color}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeOfTheWrongLength_Throws()
    {
        var colors = new BrandColors { Primary = "111111", Theme = ["000000", "FFFFFF"] };

        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Colors = colors }));

        Assert.Contains("Colors.Theme has 2 colours", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GradientOfOneColour_Throws()
    {
        var colors = new BrandColors { Primary = "111111", Gradient = ["111111"] };

        Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Colors = colors }));
    }

    [Fact]
    public void ImageThatIsNotPngOrJpeg_ThrowsNamingTheProperty()
    {
        var gif = new BrandImage("GIF89a\x01\x00\x01\x00\x00\x00"u8.ToArray());

        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Tagline = gif }));

        Assert.Contains("Tagline is not a PNG or JPEG image", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fonts_TakeTheirFamilyFromTheRegularFace_UnlessGiven()
    {
        var fonts = new BrandFonts { Regular = Serif, Bold = Serif, Italic = Serif, BoldItalic = Serif };

        Assert.Equal("Liberation Serif", ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts }).FontFamily);
        Assert.Equal("House Serif", ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts with { Family = "House Serif" } }).FontFamily);
    }

    [Fact]
    public void MonoBold_FallsBackToMono()
    {
        var fonts = new BrandFonts { Regular = Serif, Bold = Serif, Italic = Serif, BoldItalic = Serif, Mono = Serif };

        var resolved = ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts }).Fonts!;

        Assert.Same(resolved.Mono, resolved.MonoBold);
    }

    [Fact]
    public void MissingTextFace_ThrowsNamingIt()
    {
        var fonts = new BrandFonts { Regular = Serif, Bold = Serif, Italic = Serif, BoldItalic = null! };

        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts }));

        Assert.Contains("Fonts.BoldItalic is missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CffFont_IsRejectedWithAHint()
    {
        var cff = new BrandFont("OTTO\0\x01\0\0\0\0\0\0"u8.ToArray());
        var fonts = new BrandFonts { Regular = Serif, Bold = cff, Italic = Serif, BoldItalic = Serif };

        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts }));

        Assert.Contains("Fonts.Bold has CFF (PostScript) outlines", error.Message, StringComparison.Ordinal);
        Assert.Contains("convert it to a TrueType font", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileThatIsNotAFont_IsRejected()
    {
        var fonts = new BrandFonts { Regular = new BrandFont(Png.Content), Bold = Serif, Italic = Serif, BoldItalic = Serif };

        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts }));

        Assert.Contains("Fonts.Regular is not a TrueType font", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTableFonts_TablesUseTheTextFaces()
    {
        var resolved = ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = SerifFonts });

        Assert.Same(resolved.Fonts!.Regular, resolved.Fonts.TableRegular);
        Assert.Same(resolved.Fonts.Bold, resolved.Fonts.TableBold);
        Assert.Same(resolved.Fonts.Italic, resolved.Fonts.TableItalic);
        Assert.Same(resolved.Fonts.BoldItalic, resolved.Fonts.TableBoldItalic);
        Assert.Equal("Liberation Serif", resolved.Fonts.TableFamily);
        Assert.False(resolved.Fonts.HasTableFonts);
        Assert.Null(resolved.TableFontFamily);
    }

    [Fact]
    public void TableItalics_FallBackToTheUprightTableFaces()
    {
        var fonts = SerifFonts with { Table = new BrandTableFonts { Regular = Narrow, Bold = NarrowBold } };

        var resolved = ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = fonts });

        Assert.Equal(Narrow.Content.ToArray(), resolved.Fonts!.TableRegular);
        Assert.Equal(NarrowBold.Content.ToArray(), resolved.Fonts.TableBold);
        Assert.Same(resolved.Fonts.TableRegular, resolved.Fonts.TableItalic);
        Assert.Same(resolved.Fonts.TableBold, resolved.Fonts.TableBoldItalic);
        Assert.True(resolved.Fonts.HasTableFonts);
    }

    [Fact]
    public void TableFamily_IsReadFromTheTableRegularFace_UnlessGiven()
    {
        var table = new BrandTableFonts { Regular = Narrow, Bold = NarrowBold };

        Assert.Equal("PT Sans Narrow", ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = SerifFonts with { Table = table } }).TableFontFamily);
        Assert.Equal("House Narrow", ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = SerifFonts with { Table = table with { Family = " House Narrow " } } }).TableFontFamily);
        Assert.Equal("Liberation Serif", ResolvedBrand.Resolve(new Brand { Name = "a", Fonts = SerifFonts with { Table = table } }).FontFamily);
    }

    [Fact]
    public void TableFaces_AreValidatedLikeTheTextFaces()
    {
        var cff = new BrandFont("OTTO\0\x01\0\0\0\0\0\0"u8.ToArray());

        var wrongFace = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(
            new Brand { Name = "a", Fonts = SerifFonts with { Table = new BrandTableFonts { Regular = Narrow, Bold = cff } } }));
        var missingFace = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(
            new Brand { Name = "a", Fonts = SerifFonts with { Table = new BrandTableFonts { Regular = Narrow, Bold = null! } } }));

        Assert.Contains("Fonts.Table.Bold has CFF (PostScript) outlines", wrongFace.Message, StringComparison.Ordinal);
        Assert.Contains("Fonts.Table.Bold is missing", missingFace.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TableFonts_CompareByContent()
    {
        var a = SerifFonts with { Table = new BrandTableFonts { Regular = Narrow, Bold = new BrandFont(NarrowBold.Content.ToArray()) } };
        var b = SerifFonts with { Table = new BrandTableFonts { Regular = Narrow, Bold = new BrandFont(NarrowBold.Content.ToArray()) } };

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Table = b.Table! with { Italic = Narrow } });
        Assert.NotEqual(a, SerifFonts);
    }

    [Fact]
    public void DiagramColours_DeriveFromTheBrand()
    {
        var brand = ResolvedBrand.Resolve(new Brand { Name = "a", Colors = new BrandColors { Primary = "#0b5fff", Footer = "777777" } });

        var d = brand.Diagram;
        Assert.Equal(("000000", "777777", "0B5FFF", "E7EFFF", "0B5FFF"), (d.Text, d.Line, d.Accent, d.Surface, d.Border)); // text, footer, primary, band, primary
        Assert.Equal(["0B5FFF", "0F766E", "B45309", "7C3AED", "BE123C", "4B5563"], brand.Diagram.Palette);
    }

    [Fact]
    public void DiagramColours_AreTakenAndChecked()
    {
        var colors = new BrandColors { Primary = "111111", Diagram = new BrandDiagramColors { Text = "#222222", Surface = "333333", Palette = ["#444444"] } };

        var diagram = ResolvedBrand.Resolve(new Brand { Name = "a", Colors = colors }).Diagram;
        var wrong = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Colors = colors with { Diagram = new BrandDiagramColors { Palette = ["#444444", "teal"] } } }));
        var empty = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", Colors = colors with { Diagram = new BrandDiagramColors { Palette = [] } } }));

        Assert.Equal(("222222", "333333", "111111"), (diagram.Text, diagram.Surface, diagram.Border));
        Assert.Equal(["444444"], diagram.Palette);
        Assert.Contains("Colors.Diagram.Palette[1] 'teal' is not a colour", wrong.Message, StringComparison.Ordinal);
        Assert.Contains("Colors.Diagram.Palette needs at least one colour", empty.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagramColours_CompareByContent()
    {
        var a = new BrandColors { Primary = "111111", Diagram = new BrandDiagramColors { Palette = ["222222"] } };
        var b = new BrandColors { Primary = "111111", Diagram = new BrandDiagramColors { Palette = ["222222"] } };

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Diagram = new BrandDiagramColors { Palette = ["333333"] } });
        Assert.NotEqual(a, b with { Diagram = null });
    }

    [Fact]
    public void Brands_CompareByContent()
    {
        var a = new Brand { Name = "a", Logo = new BrandImage(Png.Content.ToArray()), Colors = new BrandColors { Primary = "111111", Gradient = ["111111", "222222"] } };
        var b = new Brand { Name = "a", Logo = new BrandImage(Png.Content.ToArray()), Colors = new BrandColors { Primary = "111111", Gradient = ["111111", "222222"] } };

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { FooterText = "x" });
    }

    [Fact]
    public void Registry_FindsNeutralTheCurrentBrandAndTheRegisteredOnes_IgnoringCase()
    {
        var current = new Brand { Name = "Current" };
        var acme = new Brand { Name = "acme" };

        var registry = BrandRegistry.Of(current, [acme]);

        Assert.Same(Brand.Neutral, registry.Find("NEUTRAL"));
        Assert.Same(current, registry.Find("current"));
        Assert.Same(acme, registry.Find("Acme"));
        Assert.Null(registry.Find("other"));
        Assert.Equal("'acme', 'Current' or 'neutral'", registry.Expected);
    }

    [Fact]
    public void Registry_RejectsTwoDifferentBrandsWithOneName()
    {
        var error = Assert.Throws<ArgumentException>(() => BrandRegistry.Of(null, [new Brand { Name = "acme" }, new Brand { Name = "ACME", FooterText = "x" }]));

        Assert.Contains("Two different brands are named 'ACME'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_RejectsABrandCalledNeutral() =>
        Assert.Throws<ArgumentException>(() => BrandRegistry.Of(null, [new Brand { Name = "neutral", FooterText = "x" }]));

    private static string Sample(string path) => Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample", path);
}
