using System.Buffers.Binary;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

public class FontEmbeddingTests
{
    private const string Text = "# Cover\n\n## Text\n\n- *point* and **bold**\n";
    private const string Everything = "# Cover\n\n## Text\n\nText with `code`\n\n## Table\n\n| Head |\n| --- |\n| cell |\n";

    private static readonly PptxOptions Sample = new() { Template = Samples.BuiltIn("sample") };

    [Fact]
    public void BrandDeck_EmbedsTheFourTextFaces_ByDefault()
    {
        var pptx = Samples.Generate(Samples.Markdown(Text), Sample);
        Samples.WriteForReview("embedded-fonts-sample.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var presentation = package.PresentationPart!.Presentation!;
        Assert.True(presentation.EmbedTrueTypeFonts?.Value);
        var font = Assert.Single(Embedded(package));
        Assert.Equal("Liberation Serif", font.Font!.Typeface!.Value);
        Assert.All(Faces(package, font), part => Assert.Equal("application/x-fontdata", part.ContentType));
        Assert.Equal(
            [(400, 0, "Regular"), (700, 0, "Bold"), (400, 1, "Italic"), (700, 1, "Bold Italic")],
            Faces(package, font).Select(part => Header(part)).Select(h => (h.Weight, h.Italic, h.Style)));
    }

    [Fact]
    public void EmbeddedFace_IsAnUncompressedEmbeddedOpenType_OfTheWholeFont()
    {
        var font = ResolvedBrand.Resolve(Samples.Brand).Fonts!.Regular;

        var eot = FontEmbedding.EmbeddedOpenType(font);

        Assert.Equal((uint)eot.Length, BinaryPrimitives.ReadUInt32LittleEndian(eot));
        Assert.Equal((uint)font.Length, BinaryPrimitives.ReadUInt32LittleEndian(eot.AsSpan(4)));
        Assert.Equal(0x00020001u, BinaryPrimitives.ReadUInt32LittleEndian(eot.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(eot.AsSpan(12))); // no subsetting, compression or XOR
        Assert.Equal(0x504C, BinaryPrimitives.ReadUInt16LittleEndian(eot.AsSpan(34)));
        Assert.Equal(font, eot[^font.Length..]);
        Assert.Equal("Liberation Serif", Header(eot).Family);
    }

    [Fact]
    public void TableAndCodeFamilies_AreEmbedded_OnlyWhenASlideUsesThem()
    {
        using var everything = Samples.Open(Samples.Generate(Samples.Markdown(Everything), Sample));
        using var text = Samples.Open(Samples.Generate(Samples.Markdown(Text), Sample));

        var families = Embedded(everything).ToDictionary(f => f.Font!.Typeface!.Value!, f => Faces(everything, f).Count);
        Assert.Equal(new Dictionary<string, int> { ["Liberation Serif"] = 4, ["PT Sans Narrow"] = 2, ["Inconsolata"] = 2 }, families);
        Assert.Equal(8, everything.PresentationPart!.FontParts.Count());
        Assert.Equal(["Liberation Serif"], Embedded(text).Select(f => f.Font!.Typeface!.Value));
    }

    [Fact]
    public void EmbeddedFamilies_AreTheOnesTheDeckNames()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Everything), Sample));

        var named = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.LatinFont>()).Select(f => f.Typeface!.Value).ToHashSet();
        named.Add(package.PresentationPart!.SlideMasterParts.First().ThemePart!.Theme!.ThemeElements!.FontScheme!.MinorFont!.LatinFont!.Typeface!.Value);

        Assert.Equal(named, Embedded(package).Select(f => f.Font!.Typeface!.Value).ToHashSet());
    }

    [Theory]
    [InlineData("option")]
    [InlineData("front matter")]
    [InlineData("neutral")]
    public void NothingIsEmbedded_WhenSwitchedOff_OrWithoutBrandFonts(string how)
    {
        var (markdown, options) = how switch
        {
            "option" => (Everything, new PptxOptions { Template = new BrandTemplateOptions { Brand = Samples.Brand, EmbedFonts = false } }),
            "front matter" => ("---\nembedFonts: false\n---\n" + Everything, Sample),
            _ => (Everything, new PptxOptions()),
        };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown(markdown), options));

        Assert.Empty(package.PresentationPart!.FontParts);
        Assert.Null(package.PresentationPart.Presentation!.EmbeddedFontList);
        Assert.Null(package.PresentationPart.Presentation.EmbedTrueTypeFonts);
    }

    [Fact]
    public void CustomTemplates_GetNothing_ABrandsOwnTemplateGetsItsFonts()
    {
        var builtIn = Samples.TemplateBytes(Samples.BuiltIn("neutral"));
        var withOwnTemplate = Samples.Brand with { Pptx = new PptxBrandTemplate(builtIn) };

        using var custom = Samples.Open(Samples.Generate(Samples.Markdown(Everything), new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(builtIn)) }));
        using var own = Samples.Open(Samples.Generate(Samples.Markdown(Everything), new PptxOptions { Template = new BrandTemplateOptions { Brand = withOwnTemplate } }));
        using var off = Samples.Open(Samples.Generate(Samples.Markdown("---\nembedFonts: false\n---\n" + Everything), new PptxOptions { Template = new BrandTemplateOptions { Brand = withOwnTemplate } }));

        Assert.Empty(custom.PresentationPart!.FontParts);
        Assert.Equal(["Liberation Serif", "PT Sans Narrow", "Inconsolata"], Embedded(own).Select(f => f.Font!.Typeface!.Value));
        Assert.Empty(off.PresentationPart!.FontParts);
    }

    [Fact]
    public void FacesThatFallBack_AreNotEmbeddedTwice()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Everything), Sample));

        var table = Embedded(package).Single(f => f.Font!.Typeface!.Value == "PT Sans Narrow");
        Assert.NotNull(table.RegularFont);
        Assert.NotNull(table.BoldFont);
        Assert.Null(table.ItalicFont);
        Assert.Null(table.BoldItalicFont);
        Assert.Equal(8, package.PresentationPart!.FontParts.Count()); // 4 text, 2 table, 2 mono
    }

    [Fact]
    public void RestrictedFaces_AreLeftOut_AndTheirFamilyWithARestrictedRegularFace()
    {
        var fonts = Samples.Brand.Fonts!;
        var italicRestricted = Samples.Brand with { Fonts = fonts with { Italic = Restricted(fonts.Italic) } };
        var regularRestricted = Samples.Brand with { Fonts = fonts with { Regular = Restricted(fonts.Regular) } };

        using var italic = Samples.Open(Samples.Generate(Samples.Markdown(Text), new PptxOptions { Template = new BrandTemplateOptions { Brand = italicRestricted } }));
        using var regular = Samples.Open(Samples.Generate(Samples.Markdown(Text), new PptxOptions { Template = new BrandTemplateOptions { Brand = regularRestricted } }));

        var font = Assert.Single(Embedded(italic));
        Assert.Null(font.ItalicFont);
        Assert.NotNull(font.BoldItalicFont);
        Assert.Empty(Embedded(regular));
        Assert.Empty(regular.PresentationPart!.FontParts);
    }

    [Fact]
    public void CodeFont_TheCallersWins_AndTheBrandMonoIsThenNotEmbedded()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Everything), Sample with { CodeFont = "Courier New" }));

        var code = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Run>()).Single(r => r.Text!.Text == "code");
        Assert.Equal("Courier New", code.RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
        Assert.DoesNotContain("Inconsolata", Embedded(package).Select(f => f.Font!.Typeface!.Value));
    }

    [Fact]
    public void WithoutBrandMono_CodeIsConsolas()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Everything)));

        var code = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Run>()).Single(r => r.Text!.Text == "code");
        Assert.Equal("Consolas", code.RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
    }

    private static List<P.EmbeddedFont> Embedded(PresentationDocument package) =>
        package.PresentationPart!.Presentation!.EmbeddedFontList?.Elements<P.EmbeddedFont>().ToList() ?? [];

    private static List<FontPart> Faces(PresentationDocument package, P.EmbeddedFont font) =>
        new string?[] { font.RegularFont?.Id, font.BoldFont?.Id, font.ItalicFont?.Id, font.BoldItalicFont?.Id }
            .OfType<string>()
            .Select(id => (FontPart)package.PresentationPart!.GetPartById(id))
            .ToList();

    private static (int Weight, int Italic, string Family, string Style) Header(FontPart part)
    {
        using var stream = part.GetStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Header(buffer.ToArray());
    }

    // The weight, italic byte and first two names of an EOT header (version 0x00020001), whose names follow 80 fixed bytes.
    private static (int Weight, int Italic, string Family, string Style) Header(byte[] eot)
    {
        var offset = 80;
        string Name()
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(eot.AsSpan(offset + 2));
            var name = Encoding.Unicode.GetString(eot, offset + 4, size);
            offset += 4 + size;
            return name;
        }

        var family = Name();
        var style = Name();
        return ((int)BinaryPrimitives.ReadUInt32LittleEndian(eot.AsSpan(28)), eot[27], family, style);
    }

    private static BrandFont Restricted(BrandFont font)
    {
        var bytes = font.Content.ToArray();
        var (os2, _) = TrueTypeInfo.Table(bytes, "OS/2")!.Value;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(os2 + 8), 0x0002);
        return new BrandFont(bytes);
    }
}
