using System.Buffers.Binary;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

public class FontEmbeddingTests
{
    [Fact]
    public void BrandFaces_AreEmbeddedObfuscated_AndRestoreToTheFonts()
    {
        var docx = Samples.Generate(Samples.Document("report.md"), new DocxOptions { Template = Samples.Template("sample") });
        Samples.WriteForReview("embedded-fonts-sample.docx", docx);
        using var package = Samples.Open(docx);
        var table = package.MainDocumentPart!.FontTablePart!;
        var font = table.Fonts!.Elements<Font>().Single(f => f.Name == "Liberation Serif");
        var faces = new (DocumentFormat.OpenXml.OpenXmlElement Element, string File)[]
        {
            (font.GetFirstChild<EmbedRegularFont>()!, "LiberationSerif-Regular.ttf"),
            (font.GetFirstChild<EmbedBoldFont>()!, "LiberationSerif-Bold.ttf"),
            (font.GetFirstChild<EmbedItalicFont>()!, "LiberationSerif-Italic.ttf"),
            (font.GetFirstChild<EmbedBoldItalicFont>()!, "LiberationSerif-BoldItalic.ttf"),
        };

        foreach (var (element, file) in faces)
        {
            var relationship = (FontRelationshipType)element;
            var part = (FontPart)table.GetPartById(relationship.Id!);
            Assert.Equal("application/vnd.openxmlformats-officedocument.obfuscatedFont", part.ContentType);
            using var stream = part.GetStream();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            var original = File.ReadAllBytes(Samples.Path(Path.Combine("brand-sample", "fonts", file)));
            Assert.NotEqual(original.AsSpan(0, 32).ToArray(), data.ToArray().AsSpan(0, 32).ToArray());
            Assert.Equal(original, DocxFontEmbedding.Obfuscate(data.ToArray(), relationship.FontKey!.Value!));
        }

        // Neither the table nor the code family is used by this document's styles.
        Assert.Equal(4, table.FontParts.Count());
        Assert.NotNull(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<EmbedTrueTypeFonts>());
        Samples.AssertValid(docx);
    }

    [Fact]
    public void Obfuscation_XorsTheFirst32BytesWithTheKeyReversed()
    {
        var font = new byte[40];
        var obfuscated = DocxFontEmbedding.Obfuscate(font, "{00112233-4455-6677-8899-AABBCCDDEEFF}");

        Assert.Equal(0xFF, obfuscated[0]);
        Assert.Equal(0xEE, obfuscated[1]);
        Assert.Equal(0x00, obfuscated[15]);
        Assert.Equal(0xFF, obfuscated[16]);
        Assert.All(obfuscated[32..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void EmbedFontsFalse_OnlyNamesTheFamily()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\nembedFonts: false\n---\n## A\n"), new DocxOptions { Template = Samples.Template("sample") }));

        Assert.Empty(package.MainDocumentPart!.FontTablePart!.FontParts);
        Assert.Contains(package.MainDocumentPart.FontTablePart.Fonts!.Elements<Font>(), f => f.Name == "Liberation Serif");
        Assert.Null(package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<EmbedTrueTypeFonts>());
    }

    [Fact]
    public void ARestrictedFace_IsLeftOut_AndARestrictedRegularFaceLeavesOutItsFamily()
    {
        var fonts = Samples.Brand.Fonts!;
        var restrictedBold = Samples.Brand with { Name = "bold-restricted", Fonts = fonts with { Bold = Restricted(fonts.Bold) } };
        var restrictedRegular = Samples.Brand with { Name = "regular-restricted", Fonts = fonts with { Regular = Restricted(fonts.Regular) } };

        using var bold = Samples.Open(Samples.Generate(Samples.Markdown("## A\n"), new DocxOptions { Template = new BrandDocxTemplateOptions { Brand = restrictedBold } }));
        using var regular = Samples.Open(Samples.Generate(Samples.Markdown("## A\n"), new DocxOptions { Template = new BrandDocxTemplateOptions { Brand = restrictedRegular } }));

        var font = bold.MainDocumentPart!.FontTablePart!.Fonts!.Elements<Font>().Single(f => f.Name == "Liberation Serif");
        Assert.Null(font.GetFirstChild<EmbedBoldFont>());
        Assert.NotNull(font.GetFirstChild<EmbedItalicFont>());
        Assert.Empty(regular.MainDocumentPart!.FontTablePart!.FontParts);
        Assert.Null(regular.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<EmbedTrueTypeFonts>());
    }

    [Fact]
    public void TheNeutralBrand_EmbedsNothing()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## A\n")));

        Assert.Empty(package.MainDocumentPart!.FontTablePart!.FontParts);
    }

    // The face with fsType 2: restricted licence embedding.
    private static BrandFont Restricted(BrandFont font)
    {
        var bytes = font.Content.ToArray();
        var (offset, _) = TrueTypeInfo.Table(bytes, "OS/2")!.Value;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset + 8), 2);
        return new BrandFont(bytes);
    }
}
