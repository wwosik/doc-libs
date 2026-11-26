using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.Svg;
using WordDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>E09-S05: <c>{fit=cover focus=top .round .grey}</c> after an image in Word, as in PDFs.</summary>
public class ImageStyleTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"60\" viewBox=\"0 0 120 60\"><rect x=\"0\" y=\"0\" width=\"60\" height=\"60\" fill=\"#CC3333\"/><rect x=\"60\" y=\"0\" width=\"60\" height=\"60\" fill=\"#3366CC\"/></svg>";

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Asvg = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static readonly DocumentResource Portrait = Image("portrait.png", 300, 600, SKEncodedImageFormat.Png);
    private static readonly DocumentResource Landscape = Image("landscape.png", 800, 400, SKEncodedImageFormat.Png);
    private static readonly DocumentResource Photo = Image("photo.jpg", 120, 80, SKEncodedImageFormat.Jpeg);

    private static (GenerationResult Result, byte[] Docx) Generate(string markdown, DocxOptions? options = null, params DocumentResource[] more)
    {
        var (docx, result) = Samples.GenerateWithResult(Samples.Markdown(markdown, [Portrait, Landscape, Photo, .. more]), options);
        Samples.AssertValid(docx);
        return (result, docx);
    }

    // An image in colour: red on the left, blue on the right, a green band at the top; half transparent at the bottom.
    private static DocumentResource Image(string name, int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = format == SKEncodedImageFormat.Png && y > height * 0.9 ? (byte)128 : (byte)255;
                bitmap.SetPixel(x, y, y < height / 10 ? new SKColor(20, 200, 40, alpha) : x < width / 2 ? new SKColor(220, 30, 30, alpha) : new SKColor(30, 30, 220, alpha));
            }
        }

        using var data = bitmap.Encode(format, 95);
        return new DocumentResource(name, format == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg", data.ToArray());
    }

    // The pictures of the body, in order.
    private static List<XElement> Pictures(WordprocessingDocument package) =>
        [.. package.MainDocumentPart!.Document!.Body!.Descendants<WordDrawing>().Select(d => XElement.Parse(d.OuterXml))];

    private static (long Cx, long Cy) Extent(XElement picture)
    {
        var extent = picture.Descendants(Wp + "extent").Single();
        return (long.Parse(extent.Attribute("cx")!.Value, CultureInfo.InvariantCulture), long.Parse(extent.Attribute("cy")!.Value, CultureInfo.InvariantCulture));
    }

    // The crop of a picture, l t r b in thousandths of a percent; null without `a:srcRect`.
    private static int[]? Crop(XElement picture) => picture.Descendants(A + "srcRect").SingleOrDefault() is { } rect
        ? [.. new[] { "l", "t", "r", "b" }.Select(n => int.Parse(rect.Attribute(n)!.Value, CultureInfo.InvariantCulture))]
        : null;

    private static string Shape(XElement picture) => picture.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value;

    private static List<SKColor> Pixels(WordprocessingDocument package, XElement picture)
    {
        var part = package.MainDocumentPart!.GetPartById(picture.Descendants(A + "blip").Single().Attribute(R + "embed")!.Value);
        using var stream = part.GetStream();
        using var bitmap = SKBitmap.Decode(stream);
        return [.. bitmap.Pixels];
    }

    [Fact]
    public void Round_CropsToTheSquareByFocus_AndDrawsAnEllipse()
    {
        var (result, docx) = Generate("![Team](landscape.png){.round}\n\n![Left](landscape.png){.round focus=left}\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(docx);
        var pictures = Pictures(package);
        Assert.All(pictures, p => Assert.Equal("ellipse", Shape(p)));
        Assert.All(pictures, p => Assert.Equal(Extent(p).Cx, Extent(p).Cy));
        Assert.Equal(400 * 9525, Extent(pictures[0]).Cx);
        Assert.Equal([25000, 0, 25000, 0], Crop(pictures[0]));
        Assert.Equal([0, 0, 50000, 0], Crop(pictures[1]));

        // The pixels are the image's own: one image part for both.
        Assert.Single(package.MainDocumentPart!.ImageParts);
    }

    [Fact]
    public void Cover_InARow_TakesTheRowsHeight_AndIsCroppedFromTheTop()
    {
        var (result, docx) = Generate("![a](landscape.png) ![b](portrait.png){fit=cover focus=top}\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(docx);
        var pictures = Pictures(package);
        var (landscape, portrait) = (Extent(pictures[0]), Extent(pictures[1]));
        Assert.Null(Crop(pictures[0]));
        Assert.Equal(landscape.Cy, portrait.Cy);
        Assert.InRange(portrait.Cx - landscape.Cx, -2 * 9525, 2 * 9525); // both fill their cells across
        var crop = Crop(pictures[1])!;
        Assert.Equal(0, crop[0]);
        Assert.Equal(0, crop[1]);
        Assert.Equal(0, crop[2]);

        // Of the portrait (1:2) a box of 2:1 stays: three quarters of its height are cut off at the bottom.
        Assert.InRange(crop[3], 74000, 76000);
        Assert.Equal("rect", Shape(pictures[1]));
    }

    [Fact]
    public void Cover_AndRound_InARow_IsTheLargestCircleInItsBox()
    {
        var (result, docx) = Generate("![a](landscape.png) ![b](portrait.png){fit=cover .round}\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(docx);
        var pictures = Pictures(package);
        var round = Extent(pictures[1]);
        Assert.Equal(round.Cx, round.Cy);
        Assert.Equal(Extent(pictures[0]).Cy, round.Cy);
        Assert.Equal("ellipse", Shape(pictures[1]));
        Assert.Equal([0, 25000, 0, 25000], Crop(pictures[1]));
    }

    [Fact]
    public void Cover_OnAnImageOfItsOwn_HasNoEffect_AndIsReported()
    {
        var (result, docx) = Generate("# Report\n\nIntro.\n\n## Team\n\n![a](portrait.png){fit=cover focus=top}\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.AttributeIgnored, note.Code);
        Assert.Equal(7, note.Line);
        Assert.Contains("fit=cover", note.Message, StringComparison.Ordinal);
        using var package = Samples.Open(docx);
        Assert.Null(Crop(Pictures(package).Single()));
    }

    [Theory]
    [InlineData("![a](portrait.png){focus=left}", "focus")]
    [InlineData("![a](portrait.png){fit=cover scale=0.5} ![b](landscape.png)", "scale")]
    public void AttributesWithoutEffect_AreReported(string markdown, string name)
    {
        var (result, _) = Generate($"Text.\n\n{markdown}\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.AttributeIgnored, note.Code);
        Assert.Contains($"attribute {name} in", note.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("portrait.png", "png")]
    [InlineData("photo.jpg", "jpeg")]
    public void Grey_TurnsThePixelsGrey_AndEmbedsAnImageUsedTwiceOnce(string name, string extension)
    {
        var (result, docx) = Generate($"![a]({name}){{.grey}}\n\nText.\n\n![b]({name}){{.grey}}\n\n![c]({name})\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(docx);
        var pictures = Pictures(package);
        Assert.Equal(3, pictures.Count);
        var tolerance = extension == "jpeg" ? 6 : 0;
        Assert.All(Pixels(package, pictures[0]), p => Assert.True(Math.Abs(p.Red - p.Green) <= tolerance && Math.Abs(p.Green - p.Blue) <= tolerance, p.ToString()));
        Assert.Contains(Pixels(package, pictures[2]), p => p.Red > p.Blue + 100);
        Assert.Equal(2, package.MainDocumentPart!.ImageParts.Count());
        Assert.All(package.MainDocumentPart.ImageParts, p => Assert.Equal("image/" + extension, p.ContentType));
        Assert.Equal(Extent(pictures[0]), Extent(pictures[2]));
    }

    [Fact]
    public void Grey_KeepsTheAlphaOfAPng()
    {
        var (_, docx) = Generate("![a](portrait.png){.grey}\n");

        using var package = Samples.Open(docx);
        Assert.Contains(Pixels(package, Pictures(package).Single()), p => p.Alpha == 128);
    }

    [Fact]
    public void Grey_OnAnSvg_IsOnlyItsGreyPng_WithANote()
    {
        var svg = new DocumentResource("shape.svg", "image/svg+xml", Encoding.UTF8.GetBytes(Svg));
        var (result, docx) = Generate("![Shape](shape.svg){.grey}\n\n![Again](shape.svg)\n", new DocxOptions { ImageConverters = [new SvgImageConverter()] }, svg);

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.AttributeIgnored, note.Code);
        Assert.Contains("grey PNG", note.Message, StringComparison.Ordinal);
        using var package = Samples.Open(docx);
        var pictures = Pictures(package);
        Assert.Empty(pictures[0].Descendants(Asvg + "svgBlip"));
        Assert.Single(pictures[1].Descendants(Asvg + "svgBlip"));
        Assert.All(Pixels(package, pictures[0]), p => Assert.True(p.Red == p.Green && p.Green == p.Blue, p.ToString()));
        Assert.Equal(120 * 9525, Extent(pictures[0]).Cx);
        Assert.Equal(Extent(pictures[1]), Extent(pictures[0]));
    }

    [Fact]
    public void Grey_InAFootnote_IsEmbeddedInTheFootnotes()
    {
        var (_, docx) = Generate("Text.[^1]\n\n![a](photo.jpg){.grey}\n\n[^1]: See ![b](photo.jpg){.grey}\n");

        using var package = Samples.Open(docx);
        Assert.Single(package.MainDocumentPart!.ImageParts);
        Assert.Single(package.MainDocumentPart.FootnotesPart!.ImageParts);
    }

    [Fact]
    public void WithoutTheNewSyntax_NothingIsCropped()
    {
        var (_, docx) = Generate("![a](portrait.png) ![b](landscape.png)\n\n![c](landscape.png \"scale=0.5\")\n");

        using var package = Samples.Open(docx);
        Assert.All(Pictures(package), p =>
        {
            Assert.Null(Crop(p));
            Assert.Equal("rect", Shape(p));
        });
    }

    [Fact]
    public void EveryAttribute_Together_IsValid()
    {
        var (_, docx) = Generate("![a](portrait.png){.round .grey} ![b](landscape.png){fit=cover focus=right} ![c](photo.jpg){.grey}\n\n![d](landscape.png){.round .grey}\n");

        using var package = Samples.Open(docx);
        Assert.Equal(4, Pictures(package).Count);
        Samples.WriteForReview("image-styles.docx", docx);
    }
}
