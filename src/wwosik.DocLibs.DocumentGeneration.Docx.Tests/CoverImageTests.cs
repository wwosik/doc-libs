using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.Svg;
using Anchor = DocumentFormat.OpenXml.Drawing.Wordprocessing.Anchor;
using Extent = DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent;
using Inline = DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>Front matter <c>coverImage</c> on the cover of Word documents (E01-S08).</summary>
public class CoverImageTests
{
    // A4 in EMU: 16838 twips high.
    private const long A4Height = 16838L * 635;

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"60\" viewBox=\"0 0 120 60\"><rect width=\"120\" height=\"60\" fill=\"#3366CC\"/></svg>";

    private static readonly DocumentResource Mark = Samples.Image("diagram.png");

    private static (byte[] Docx, GenerationResult Result) Generate(string markdown, DocxOptions? options = null, params DocumentResource[] resources) =>
        Samples.GenerateWithResult(Samples.Markdown(markdown, [Mark, .. resources]), options);

    // The paragraphs of the cover: those before the first page break.
    private static List<Paragraph> Cover(WordprocessingDocument package)
    {
        var cover = new List<Paragraph>();
        foreach (var paragraph in package.MainDocumentPart!.Document!.Body!.Elements<Paragraph>())
        {
            cover.Add(paragraph);
            if (paragraph.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page))
            {
                break;
            }
        }

        return cover;
    }

    [Theory]
    [InlineData("small", 0.1)]
    [InlineData("medium", 1.0 / 6)]
    [InlineData("large", 0.25)]
    public void Below_IsAnInlinePictureUnderTheSubtitle_AtItsSize(string size, double share)
    {
        var (docx, result) = Generate($"---\ncoverImage: diagram.png\ncoverImageSize: {size}\n---\n# Report\n\nA subtitle\n\n## Body\n\nText.\n");

        Assert.False(result.HasNotes, result.ToString());
        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);
        var cover = Cover(package);
        var index = cover.FindIndex(p => p.Descendants<Inline>().Any());
        Assert.Equal("A subtitle", cover[index - 1].InnerText);
        var extent = cover[index].Descendants<Extent>().Single();
        Assert.Equal(A4Height * share, extent.Cy!.Value, A4Height / 1000.0);
        Samples.WriteForReview($"cover-image-{size}.docx", docx);
    }

    [Fact]
    public void Right_IsAnchoredInTheTitle_AtTheRightMargin()
    {
        var (docx, _) = Generate("---\ncoverImage: diagram.png\ncoverImagePosition: right\n---\n# Report\n\nA subtitle\n");

        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);
        var title = Cover(package).Single(p => p.Descendants<Anchor>().Any());
        Assert.Contains("Report", title.InnerText, StringComparison.Ordinal);
        var anchor = title.Descendants<Anchor>().Single();
        Assert.Equal("right", anchor.HorizontalPosition!.HorizontalAlignment!.Text);
        Assert.Equal((long)(16838 * 0.28) * 635, long.Parse(anchor.VerticalPosition!.PositionOffset!.Text));
        Assert.NotNull(anchor.GetFirstChild<DocumentFormat.OpenXml.Drawing.Wordprocessing.WrapSquare>());
        Samples.WriteForReview("cover-image-right.docx", docx);
    }

    [Fact]
    public void ASvg_GoesInAsAPng_WithItsSvgBeside()
    {
        var svg = new DocumentResource("mark.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(Svg));
        var (docx, _) = Generate("---\ncoverImage: mark.svg\n---\n# Report\n", new DocxOptions { ImageConverters = [new SvgImageConverter()] }, svg);

        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);
        var types = package.MainDocumentPart!.ImageParts.Select(p => p.ContentType).ToList();
        Assert.Contains("image/png", types);
        Assert.Contains("image/svg+xml", types);
    }

    [Fact]
    public void TheBrandsCoverImage_IsTheDefault_AndDrawingIdsStayUnique()
    {
        var brand = Samples.Brand with { CoverImage = new BrandImage(Mark.Content) };
        var (docx, _) = Generate("# Report\n\n![Photo](diagram.png)\n", new DocxOptions { Template = new BrandDocxTemplateOptions { Brand = brand } });

        Samples.AssertValid(docx);
        using var package = Samples.Open(docx);
        Assert.Single(Cover(package).SelectMany(p => p.Descendants<Inline>()));
        var ids = package.MainDocumentPart!.Document!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Select(d => d.Id!.Value).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AMissingResource_Throws_NamingTheKey()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("---\ncoverImage: nothing.png\n---\n# Report\n"));

        Assert.Contains("'coverImage'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutACover_TheImageIsReported()
    {
        var (docx, result) = Generate("---\ncoverImage: diagram.png\n---\nText only.\n");

        Assert.Equal(NoteCodes.CoverImageIgnored, Assert.Single(result.Notes).Code);
        using var package = Samples.Open(docx);
        Assert.Empty(package.MainDocumentPart!.ImageParts);
    }
}
