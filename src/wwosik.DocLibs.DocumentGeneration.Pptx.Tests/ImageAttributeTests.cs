using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><c>{fit=cover focus=top .round .grey}</c> after an image on slides.</summary>
public class ImageAttributeTests
{
    private static readonly DocumentResource Portrait = TestImages.Png("portrait.png", 300, 600);
    private static readonly DocumentResource Landscape = TestImages.Png("landscape.png", 800, 400);

    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown, Portrait, Landscape, Samples.Image("photo.png")), output, options);
        return (result, output.ToArray());
    }

    private static List<P.Picture> Pictures(byte[] pptx, out PresentationDocument package)
    {
        package = Samples.Open(pptx);
        return [.. Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.Picture>())];
    }

    private static (long W, long H) Size(P.Picture picture) =>
        (picture.ShapeProperties!.Transform2D!.Extents!.Cx!.Value, picture.ShapeProperties.Transform2D.Extents.Cy!.Value);

    [Fact]
    public void Cover_FillsTheBox_AndCropsAPortraitFromItsTop()
    {
        var (result, pptx) = Generate("## Team\n\n![Team](portrait.png){fit=cover focus=top}\n");

        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.AttributeIgnored);
        Samples.AssertValid(pptx);
        var picture = Assert.Single(Pictures(pptx, out var package));
        using (package)
        {
            var (width, height) = Size(picture);
            Assert.True(width > height, "the box of a visual slide is wider than tall");
            var crop = picture.BlipFill!.SourceRectangle!;
            Assert.Equal(0, crop.Top?.Value ?? 0);
            Assert.Equal(0, crop.Left?.Value ?? 0);
            Assert.Equal(0, crop.Right?.Value ?? 0);

            // What stays of the 1:2 portrait has the box's shape.
            var visible = 1 - (crop.Bottom!.Value / 100000.0);
            Assert.Equal(width / (double)height, 300 / (600 * visible), 2);
            Assert.Equal(A.ShapeTypeValues.Rectangle, picture.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value);
        }
    }

    [Fact]
    public void Contain_IsTheDefault_WithoutACrop()
    {
        var (_, pptx) = Generate("## Team\n\n![Team](portrait.png){fit=contain}\n");

        var picture = Assert.Single(Pictures(pptx, out var package));
        using (package)
        {
            Assert.Null(picture.BlipFill!.SourceRectangle);
            var (width, height) = Size(picture);
            Assert.Equal(0.5, width / (double)height, 2);
        }
    }

    [Fact]
    public void Round_IsACircle_CroppedToASquare_AndStaysAPicture()
    {
        var (_, pptx) = Generate("## Team\n\n![Team](landscape.png){.round focus=left}\n");

        Samples.AssertValid(pptx);
        var picture = Assert.Single(Pictures(pptx, out var package));
        using (package)
        {
            var (width, height) = Size(picture);
            Assert.Equal(width, height);
            Assert.Equal(A.ShapeTypeValues.Ellipse, picture.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value);
            var crop = picture.BlipFill!.SourceRectangle!;
            Assert.Equal(0, crop.Left?.Value ?? 0);
            Assert.Equal(50000, crop.Right!.Value); // the right half of the 2:1 image goes
        }
    }

    [Fact]
    public void Grey_IsAGreyscaleEffectOnThePicture_AlsoOnAConvertedSvg()
    {
        var svg = new DocumentResource("logo.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'/>"u8.ToArray());
        var options = new PptxOptions { ImageConverters = [new FakeSvgConverter()] };
        using var output = new MemoryStream();
        PptxGenerator.Generate(Samples.Markdown("## Logos\n\n![a](photo.png){.grey} ![b](logo.svg){.grey}\n", Samples.Image("photo.png"), svg), output, options);

        var pptx = output.ToArray();
        Samples.AssertValid(pptx);
        var pictures = Pictures(pptx, out var package);
        using (package)
        {
            Assert.Equal(2, pictures.Count);
            Assert.All(pictures, p => Assert.NotNull(p.BlipFill!.Blip!.GetFirstChild<A.Grayscale>()));

            // The effect comes before the extension that carries the SVG, as the schema orders them.
            var blip = pictures[1].BlipFill!.Blip!;
            Assert.True(blip.ChildElements.ToList().IndexOf(blip.GetFirstChild<A.Grayscale>()!) < blip.ChildElements.ToList().IndexOf(blip.GetFirstChild<A.BlipExtensionList>()!));
        }
    }

    [Fact]
    public void ImagesInARow_CoverTheirCells()
    {
        var (_, pptx) = Generate("## Two\n\n![a](portrait.png){fit=cover} ![b](landscape.png){fit=cover .round}\n");

        Samples.AssertValid(pptx);
        var pictures = Pictures(pptx, out var package);
        using (package)
        {
            var (first, second) = (Size(pictures[0]), Size(pictures[1]));
            Assert.NotNull(pictures[0].BlipFill!.SourceRectangle);
            Assert.Equal(second.W, second.H);
            Assert.Equal(Math.Min(first.W, first.H), second.W);
        }
    }

    [Fact]
    public void TheBraces_NeverReachTheText()
    {
        var (_, pptx) = Generate("## Team\n\nBefore ![Team](photo.png){scale=1 .caption} after.\n");

        using var package = Samples.Open(pptx);
        var texts = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.Shape>()).Select(s => s.InnerText).ToList();
        Assert.DoesNotContain(texts, t => t.Contains('{', StringComparison.Ordinal));
        Assert.Contains("Team", texts); // .caption, as "caption" in the title
        Assert.Contains(texts, t => t.Contains("after.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("![a](photo.png) {.round}")]
    [InlineData("![a](photo.png){not attributes}")]
    [InlineData("![a](photo.png){}")]
    public void OtherBraces_StayText(string paragraph)
    {
        var (_, pptx) = Generate($"## Team\n\n{paragraph}\n");

        using var package = Samples.Open(pptx);
        var texts = Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.Shape>()).Select(s => s.InnerText);
        Assert.Contains(texts, t => t.Contains('{', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{.rounded}", "Unknown attribute '.rounded'. Valid attributes: focus=top|bottom|left|right|center, fit=contain|cover, scale=<number>, .round, .grey, .caption.")]
    [InlineData("{fit=fill}", "fit is contain or cover, not 'fill'.")]
    [InlineData("{focus=middle}", "focus is top, bottom, left, right or center, not 'middle'.")]
    [InlineData("{scale=big}", "scale is a number above 0")]
    [InlineData("{.grey .grey}", ".grey is given twice.")]
    [InlineData("{fit=cover fit=\"contain\"}", "fit is given twice.")]
    public void Mistakes_AreErrors_ListingWhatIsValid(string attributes, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate($"## Team\n\n![a](photo.png){attributes}\n"));

        Assert.Contains($"The image attributes '{attributes}' at line 3 cannot be used: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scale_InTheTitleAndTheAttributes_IsAnError()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("## Team\n\n![a](photo.png \"scale=1\"){scale=2}\n"));

        Assert.Contains("has scale both in its title and in its attributes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scale_InTheAttributes_MeansWhatItMeansInTheTitle()
    {
        var (_, titled) = Generate("## Team\n\n![a](photo.png \"scale=1\")\n");
        var (_, attributed) = Generate("## Team\n\n![a](photo.png){scale=1}\n");

        var a = Assert.Single(Pictures(titled, out var first));
        var b = Assert.Single(Pictures(attributed, out var second));
        using (first)
        using (second)
        {
            Assert.Equal(Size(a), Size(b));
        }
    }

    [Theory]
    [InlineData("![a](photo.png){focus=top}", "focus")]
    [InlineData("![a](portrait.png){fit=cover scale=2}", "scale")]
    [InlineData("### Logo ![a](photo.png){.round}", "'{.round}' have no effect")]
    public void AttributesWithoutEffect_AreReported(string markdown, string message)
    {
        var (result, _) = Generate($"## Team\n\n{markdown}\n");

        var note = Assert.Single(result.Notes, n => n.Code == NoteCodes.AttributeIgnored);
        Assert.Equal(3, note.Line);
        Assert.Contains(message, note.Message, StringComparison.Ordinal);
    }

    private sealed class FakeSvgConverter : IImageConverter
    {
        public ConvertedImage? Convert(DocumentResource image, double scale) =>
            image.Path.EndsWith(".svg", StringComparison.Ordinal)
                ? new ConvertedImage(TestImages.Png("logo.png", 80, 40).Content, "image/png", 40, 20)
                : null;
    }
}
