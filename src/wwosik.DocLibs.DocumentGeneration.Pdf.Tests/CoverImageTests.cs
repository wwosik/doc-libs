using SkiaSharp;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Front matter <c>coverImage</c> on the cover page (E01-S08).</summary>
public class CoverImageTests
{
    private const double A4Height = 841.89;

    private static readonly DocumentResource Mark = Png("mark.png", 200, 100);

    private static DocumentResource Png(string name, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(30, 90, 200));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new DocumentResource(name, "image/png", data.ToArray());
    }

    private static (GenerationResult Result, byte[] Pdf) Generate(string markdown, PdfOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown(markdown, Mark), output, (options ?? new PdfOptions()) with { TimeProvider = Samples.FixedTime });
        return (result, output.ToArray());
    }

    private static UglyToad.PdfPig.Core.PdfRectangle CoverImageBox(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return Assert.Single(document.GetPage(1).GetImages()).BoundingBox;
    }

    [Theory]
    [InlineData("small", 0.1)]
    [InlineData("medium", 1.0 / 6)]
    [InlineData("large", 0.25)]
    public void TheImage_IsOnTheCover_AtItsSize(string size, double share)
    {
        var (result, pdf) = Generate($"---\ncoverImage: mark.png\ncoverImageSize: {size}\n---\n# Report\n\nA subtitle\n\n## Body\n\nText.\n");

        Assert.False(result.HasNotes, result.ToString());
        var box = CoverImageBox(pdf);
        Assert.Equal(A4Height * share, box.Height, 1);
        Assert.Equal(2, box.Width / box.Height, 2);
        Assert.Contains("pdfaid:part", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Samples.WriteForReview($"cover-image-{size}.pdf", pdf);
    }

    [Fact]
    public void Below_IsUnderTheSubtitle_LeftAlignedWithTheTitle()
    {
        var (_, pdf) = Generate("---\ncoverImage: mark.png\n---\n# Report\n\nA subtitle\n");

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        var box = Assert.Single(page.GetImages()).BoundingBox;
        var subtitle = page.GetWords().Single(w => w.Text == "subtitle").BoundingBox;
        var title = page.GetWords().Single(w => w.Text == "Report").BoundingBox;
        Assert.True(box.Top < subtitle.Bottom, "the image is under the subtitle");
        Assert.Equal(title.Left, box.Left, 0);
    }

    [Fact]
    public void Right_StandsRightOfTheTitle_TopAligned()
    {
        var (_, pdf) = Generate("---\ncoverImage: mark.png\ncoverImagePosition: right\n---\n# A report with a long title that wraps\n\nA subtitle\n");

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        var box = Assert.Single(page.GetImages()).BoundingBox;
        var words = page.GetWords().ToList();
        Assert.True(words.All(w => w.BoundingBox.Right < box.Left), "the title wraps short of the image");
        Assert.InRange(box.Top - words.First(w => w.Text == "A").BoundingBox.Top, -1.5, 1.5);
        Assert.InRange(box.Right - (page.Width - 56.69), -0.5, 0.5);
    }

    [Fact]
    public void TheImage_NeverCoversTheBrandsArtwork_ButIsMadeSmaller()
    {
        var subtitle = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Subtitle line {i}"));
        var (_, pdf) = Generate($"---\ncoverImage: mark.png\ncoverImageSize: large\n---\n# Report\n\n{subtitle}\n", new PdfOptions { Template = Samples.BuiltIn("sample") });

        using var document = PdfDocument.Open(pdf);
        var images = document.GetPage(1).GetImages().Select(i => i.BoundingBox).ToList();
        var mark = images.Single(b => Math.Abs((b.Width / b.Height) - 2) < 0.01 && b.Left < 100);
        Assert.True(mark.Height < A4Height * 0.25 - 1, "made smaller");
        Assert.All(images.Where(b => b != mark), other => Assert.False(other.Left < mark.Right && mark.Left < other.Right && other.Bottom < mark.Top && mark.Bottom < other.Top, "it covers another image"));
    }

    [Fact]
    public void OnDarkPages_TheImageIsDrawnOverTheBackground_AndClearOfTheLogo()
    {
        var brand = Samples.Brand with { CoverImage = new BrandImage(Mark.Content) };
        var (result, pdf) = Generate("---\ntone: dark\ncoverImagePosition: right\n---\n# Report\n\nA subtitle\n", new PdfOptions { Template = new BrandPdfTemplateOptions { Brand = brand } });

        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.CoverImageIgnored);
        Assert.Contains("pdfaid:part", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        var images = document.GetPage(1).GetImages().Select(i => i.BoundingBox).ToList();
        var mark = images.Single(b => Math.Abs((b.Width / b.Height) - 2) < 0.01);
        Assert.All(images.Where(b => b != mark), other => Assert.False(other.Left < mark.Right && mark.Left < other.Right && other.Bottom < mark.Top && mark.Bottom < other.Top, "it covers another image"));
        Samples.WriteForReview("cover-image-dark.pdf", pdf);
    }

    [Fact]
    public void TheBrandsCoverImage_IsTheDefault()
    {
        var brand = Samples.Brand with { CoverImage = new BrandImage(Mark.Content) };
        var (_, pdf) = Generate("# Report\n", new PdfOptions { Template = new BrandPdfTemplateOptions { Brand = brand } });
        var (_, without) = Generate("# Report\n", new PdfOptions { Template = Samples.BuiltIn("sample") });

        using var with = PdfDocument.Open(pdf);
        using var plain = PdfDocument.Open(without);
        Assert.Equal(plain.GetPage(1).GetImages().Count() + 1, with.GetPage(1).GetImages().Count());
    }

    [Fact]
    public void ASvg_IsConvertedByTheImageConverters()
    {
        var svg = new DocumentResource("mark.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'/>"u8.ToArray());
        var converter = new PngConverter(Mark);
        using var output = new MemoryStream();
        PdfGenerator.Generate(Samples.Markdown("---\ncoverImage: mark.svg\n---\n# Report\n", svg), output, new PdfOptions { ImageConverters = [converter] });

        Assert.Equal(A4Height / 6, CoverImageBox(output.ToArray()).Height, 1);
        Assert.Contains(converter.Scales, s => s > 2);
    }

    [Fact]
    public void AMissingResource_Throws_NamingTheKey()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("---\ncoverImage: nothing.png\n---\n# Report\n"));

        Assert.Contains("'coverImage'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'nothing.png'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Text only.\n", "report")]
    [InlineData("# Report\n\nText.\n", "plain")]
    public void WithoutACover_TheImageIsReported(string body, string layout)
    {
        var (result, pdf) = Generate($"---\ncoverImage: mark.png\nlayout: {layout}\n---\n{body}");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.CoverImageIgnored, note.Code);
        Assert.Equal(2, note.Line);
        using var document = PdfDocument.Open(pdf);
        Assert.Empty(document.GetPage(1).GetImages());
    }

    [Fact]
    public void ADocumentWithoutTheKeys_IsUnchanged()
    {
        var (_, plain) = Generate("# Report\n\nA subtitle\n");
        var (_, sized) = Generate("---\ncoverImageSize: large\n---\n# Report\n\nA subtitle\n");

        using var a = PdfDocument.Open(plain);
        using var b = PdfDocument.Open(sized);
        Assert.Equal(a.GetPage(1).GetWords().Select(w => w.BoundingBox), b.GetPage(1).GetWords().Select(w => w.BoundingBox));
        Assert.Empty(b.GetPage(1).GetImages());
    }

    // Takes any image and returns `image`, at the size asked for, recording the scales.
    private sealed class PngConverter(DocumentResource image) : IImageConverter
    {
        public List<double> Scales { get; } = [];

        public ConvertedImage? Convert(DocumentResource resource, double scale)
        {
            this.Scales.Add(scale);
            return new ConvertedImage(image.Content, image.ContentType, 40, 20);
        }
    }
}
