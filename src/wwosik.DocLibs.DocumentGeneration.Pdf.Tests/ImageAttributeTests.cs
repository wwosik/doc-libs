using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Operations.ClippingPaths;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary><c>{fit=cover focus=top .round .grey}</c> after an image in PDFs.</summary>
public class ImageAttributeTests
{
    private static readonly DocumentResource Portrait = Image("portrait.png", 300, 600, SKEncodedImageFormat.Png);
    private static readonly DocumentResource Landscape = Image("landscape.png", 800, 400, SKEncodedImageFormat.Png);
    private static readonly DocumentResource Photo = Image("photo.jpg", 120, 80, SKEncodedImageFormat.Jpeg);

    private static (GenerationResult Result, byte[] Pdf) Generate(string markdown)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown(markdown, Portrait, Landscape, Photo), output, new PdfOptions { TimeProvider = Samples.FixedTime });
        return (result, output.ToArray());
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

    private static int Count<T>(Page page) => page.Operations.Count(o => o is T);

    [Fact]
    public void Round_ClipsTheImageToACircle()
    {
        var (result, pdf) = Generate("![Team](landscape.png){.round}\n");

        Assert.False(result.HasNotes, result.ToString());
        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        Assert.Equal(1, Count<ModifyClippingByNonZeroWindingIntersect>(page));
        Assert.Equal(4, Count<AppendDualControlPointBezierCurve>(page));

        // The whole image is drawn, twice as wide as tall, around the square it shows through.
        var image = Assert.Single(page.GetImages());
        Assert.Equal(2, image.BoundingBox.Width / image.BoundingBox.Height, 2);
    }

    [Fact]
    public void Cover_InARow_TakesTheRowsHeight_AndIsCroppedFromTheTop()
    {
        var (result, pdf) = Generate("![a](landscape.png) ![b](portrait.png){fit=cover focus=top}\n");

        Assert.False(result.HasNotes, result.ToString());
        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        Assert.Equal(1, Count<ModifyClippingByNonZeroWindingIntersect>(page));
        var images = page.GetImages().OrderBy(i => i.BoundingBox.Left).ToList();
        var (landscape, portrait) = (images[0].BoundingBox, images[1].BoundingBox);

        // The portrait is drawn wider than its cell's height allows, its top at the row's top.
        Assert.Equal(landscape.Top, portrait.Top, 1);
        Assert.True(portrait.Bottom < landscape.Bottom, "the portrait runs below the row and is clipped there");
        Assert.Equal(0.5, portrait.Width / portrait.Height, 2);
    }

    [Fact]
    public void Cover_OnAnImageOfItsOwn_HasNoEffect_AndIsReported()
    {
        var (result, pdf) = Generate("# Report\n\nIntro.\n\n## Team\n\n![a](portrait.png){fit=cover focus=top}\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.AttributeIgnored, note.Code);
        Assert.Equal(7, note.Line);
        Assert.Contains("fit=cover", note.Message, StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(0, Count<ModifyClippingByNonZeroWindingIntersect>(document.GetPage(2)));
    }

    [Theory]
    [InlineData("![a](portrait.png){.caption}", ".caption")]
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
    [InlineData("portrait.png")]
    [InlineData("photo.jpg")]
    public void Grey_TurnsThePixelsGrey_AndEmbedsAnImageUsedTwiceOnce(string name)
    {
        var (_, pdf) = Generate($"![a]({name}){{.grey}}\n\nText.\n\n![b]({name}){{.grey}}\n\n![c]({name})\n");

        using var document = PdfDocument.Open(pdf);
        var images = document.GetPages().SelectMany(p => p.GetImages()).ToList();
        Assert.Equal(3, images.Count);
        var pixels = images.Select(Pixels).ToList();
        var tolerance = name.EndsWith(".jpg", StringComparison.Ordinal) ? 6 : 0;
        Assert.All(pixels[0], p => Assert.True(Math.Abs(p.Red - p.Green) <= tolerance && Math.Abs(p.Green - p.Blue) <= tolerance, p.ToString()));
        Assert.Contains(pixels[2], p => p.Red > p.Blue + 100); // the third is in colour
        Assert.Equal(2, Embedded(pdf));
    }

    [Fact]
    public void Grey_KeepsTheAlphaOfAPng()
    {
        var (_, pdf) = Generate("![a](portrait.png){.grey}\n");

        Assert.Matches(@"/SMask\s+\d+\s+\d+\s+R", System.Text.Encoding.Latin1.GetString(pdf)); // the transparent rows keep their soft mask
    }

    [Fact]
    public void WithoutTheNewSyntax_NothingIsClipped()
    {
        var (_, pdf) = Generate("![a](portrait.png) ![b](landscape.png)\n\n![c](landscape.png \"scale=0.5\")\n");

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(0, Count<ModifyClippingByNonZeroWindingIntersect>(document.GetPage(1)));
    }

    [Fact]
    public void Mistakes_AreErrors_ListingWhatIsValid()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("Text.\n\n![a](portrait.png){.blur}\n"));

        Assert.Contains("'{.blur}' at line 3 cannot be used: Unknown attribute '.blur'. Valid attributes: focus=", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfA_StaysPdfA_WithEveryAttribute()
    {
        var (_, pdf) = Generate("![a](portrait.png){.round .grey} ![b](landscape.png){fit=cover focus=right} ![c](photo.jpg){.grey}\n");

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(3, document.GetPage(1).GetImages().Count());
        Assert.Contains("pdfaid:part", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Samples.WriteForReview("image-attributes.pdf", pdf);
    }

    private static List<SKColor> Pixels(IPdfImage image)
    {
        // A JPEG is embedded as it is (DCT); anything else PdfPig turns back into a PNG.
        var encoded = image.TryGetPng(out var png) ? png : [.. image.RawBytes];
        using var bitmap = SKBitmap.Decode(encoded);
        return [.. bitmap.Pixels];
    }

    // The image XObjects in the file, soft masks not counted.
    private static int Embedded(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var all = System.Text.RegularExpressions.Regex.Matches(text, @"/Subtype\s*/Image").Count;
        var masks = System.Text.RegularExpressions.Regex.Matches(text, @"/SMask\s+\d+\s+\d+\s+R").Count;
        return all - masks;
    }
}
