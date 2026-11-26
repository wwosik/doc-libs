using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.Svg;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Front matter <c>coverImage</c> on the title slide of decks and HTML decks (E01-S08).</summary>
public class CoverImageTests
{
    private const long SlideHeight = 6858000;

    private static readonly DocumentResource Mark = TestImages.Png("mark.png", 400, 200);

    private static (byte[] Pptx, GenerationResult Result) Generate(string markdown, PptxOptions? options = null, params DocumentResource[] resources)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown, [Mark, .. resources]), output, options);
        return (output.ToArray(), result);
    }

    // The slide's pictures that are not placeholders' and come from the slide, not its layout.
    private static List<(long X, long Y, long W, long H)> Pictures(SlidePart slide) =>
        [.. slide.Slide!.Descendants<P.Picture>()
            .Select(p => p.ShapeProperties!.Transform2D!)
            .Select(t => (t.Offset!.X!.Value, t.Offset.Y!.Value, t.Extents!.Cx!.Value, t.Extents.Cy!.Value))];

    private static (long X, long Y, long W, long H) Box(OpenXmlPartContainer part, P.PlaceholderValues type) =>
        part switch
        {
            SlideLayoutPart layout => layout.SlideLayout!.Descendants<P.Shape>(),
            SlidePart slide => slide.Slide!.Descendants<P.Shape>(),
            _ => [],
        }
        is var shapes && shapes.First(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == type).ShapeProperties!.Transform2D is { } t
            ? (t.Offset!.X!.Value, t.Offset.Y!.Value, t.Extents!.Cx!.Value, t.Extents.Cy!.Value)
            : (0, 0, 0, 0);

    [Theory]
    [InlineData("small", 0.1)]
    [InlineData("medium", 1.0 / 6)]
    [InlineData("large", 0.25)]
    public void TheImage_IsOnTheTitleSlide_UnderTheSubtitle(string size, double share)
    {
        var (pptx, result) = Generate($"---\ncoverImage: mark.png\ncoverImageSize: {size}\n---\n# Deck\n\nA subtitle\n\n## Slide\n\nText.\n");

        Samples.AssertValid(pptx);
        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        var picture = Assert.Single(Pictures(slides[0]));
        Assert.Empty(Pictures(slides[1]));
        var subtitle = Box(slides[0].SlideLayoutPart!, P.PlaceholderValues.SubTitle);
        var title = Box(slides[0].SlideLayoutPart!, P.PlaceholderValues.CenteredTitle);
        Assert.Equal(title.X, picture.X);
        Assert.True(picture.Y > subtitle.Y, "under the subtitle's first line");
        Assert.Equal(2.0, picture.W / (double)picture.H, 2);

        // There is not room below the subtitle for every size: the built-in cover keeps the foot of the slide free.
        Assert.True(picture.H <= SlideHeight * share + 1);
        Samples.WriteForReview($"cover-image-{size}.pptx", pptx);
    }

    [Fact]
    public void Right_StandsRightOfTheTitle_WhichIsNarrowed()
    {
        var (pptx, result) = Generate("---\ncoverImage: mark.png\ncoverImagePosition: right\ncoverImageSize: large\n---\n# Deck\n\nA subtitle\n");

        Samples.AssertValid(pptx);
        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0];
        var picture = Assert.Single(Pictures(slide));
        var layoutTitle = Box(slide.SlideLayoutPart!, P.PlaceholderValues.CenteredTitle);
        var title = Box(slide, P.PlaceholderValues.CenteredTitle);
        Assert.InRange(picture.X + picture.W - (layoutTitle.X + layoutTitle.W), -1000, 1000);
        Assert.InRange(picture.H - (long)(SlideHeight * 0.25), -1000, 1000);
        Assert.True(title.X + title.W < picture.X, "the title ends left of the image");
        Assert.True(picture.Y >= layoutTitle.Y);
        Samples.WriteForReview("cover-image-right.pptx", pptx);
    }

    [Fact]
    public void TheImage_NeverCoversTheBrandsPictures()
    {
        var brand = Samples.Brand with { CoverImage = new BrandImage(Mark.Content) };
        var (pptx, _) = Generate("# Deck\n\nA subtitle\n", new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0];
        var picture = Assert.Single(Pictures(slide));
        foreach (var other in slide.SlideLayoutPart!.SlideLayout!.Descendants<P.Picture>().Select(p => p.ShapeProperties!.Transform2D!))
        {
            var (x, y, w, h) = (other.Offset!.X!.Value, other.Offset.Y!.Value, other.Extents!.Cx!.Value, other.Extents.Cy!.Value);
            Assert.False(picture.X < x + w && x < picture.X + picture.W && picture.Y < y + h && y < picture.Y + picture.H, "the image covers a picture of the layout");
        }

        Samples.WriteForReview("cover-image-sample.pptx", pptx);
    }

    [Fact]
    public void ADeclaredPicturePlaceholder_OfTheTitleLayout_TakesIt()
    {
        var template = Samples.EditedTemplate(new BrandTemplateOptions(), master =>
        {
            var layout = master.SlideLayoutParts.Single(l => l.SlideLayout!.Type!.Value == P.SlideLayoutValues.Title);
            layout.SlideLayout!.CommonSlideData!.ShapeTree!.Append(new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 40, Name = "Partner Logo" },
                    new P.NonVisualShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Picture, Index = 13 })),
                new P.ShapeProperties(new A.Transform2D(new A.Offset { X = 8000000, Y = 300000 }, new A.Extents { Cx = 2000000, Cy = 2000000 })),
                new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph())));
            layout.SlideLayout.Save();
        });
        var roles = new PptxTemplateRoles { Placeholders = [new("Title Slide", "Partner Logo", PlaceholderRole.Picture)] };
        var (pptx, _) = Generate("---\ncoverImage: mark.png\n---\n# Deck\n", new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(template)) { Roles = roles } });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var picture = Samples.Slides(package)[0].Slide!.Descendants<P.Picture>().Single();
        Assert.Equal(13u, picture.NonVisualPictureProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape!.Index!.Value);
        Assert.Equal(8000000, picture.ShapeProperties!.Transform2D!.Offset!.X!.Value);
        Assert.Equal(1000000, picture.ShapeProperties.Transform2D.Extents!.Cy!.Value);
    }

    [Fact]
    public void ASvg_IsAVector_AndDrawnInTheHtmlDeck()
    {
        var svg = new DocumentResource("mark.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'><rect width='40' height='20' fill='#c00'/></svg>"u8.ToArray());
        var (pptx, _) = Generate("---\ncoverImage: mark.svg\n---\n# Deck\n", new PptxOptions { ImageConverters = [new SvgImageConverter()] }, svg);

        Samples.AssertValid(pptx);
        using (var package = Samples.Open(pptx))
        {
            var slide = Samples.Slides(package)[0];
            Assert.Contains(slide.ImageParts, p => p.ContentType == "image/svg+xml");
        }

        using var output = new MemoryStream();
        var result = HtmlDeck.Write(new MemoryStream(pptx), output);
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
        Assert.Matches(new Regex("<img|<image", RegexOptions.CultureInvariant), Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void AMissingResource_Throws_NamingTheKey()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("---\ncoverImage: nothing.png\n---\n# Deck\n"));

        Assert.Contains("'coverImage'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'nothing.png'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeckWithoutATitleSlide_ReportsTheImage()
    {
        var (pptx, result) = Generate("---\ncoverImage: mark.png\n---\n## Slide\n\nText.\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.CoverImageIgnored, note.Code);
        Assert.Equal(2, note.Line);
        using var package = Samples.Open(pptx);
        Assert.Empty(Pictures(Samples.Slides(package)[0]));
    }

    [Fact]
    public void ADeckWithoutTheKeys_IsUnchanged()
    {
        var (plain, _) = Generate("---\nlang: en\n---\n# Deck\n\nA subtitle\n");
        var (sized, _) = Generate("---\ncoverImagePosition: right\n---\n# Deck\n\nA subtitle\n");

        using var a = Samples.Open(plain);
        using var b = Samples.Open(sized);
        Assert.Equal(Samples.Slides(a)[0].Slide!.OuterXml, Samples.Slides(b)[0].Slide!.OuterXml);
    }
}
