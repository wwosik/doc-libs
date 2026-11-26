using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The cover image's keys, the brand's default, and how it is made smaller to keep clear of the cover's pictures.</summary>
public class CoverImageTests
{
    private static readonly string Pack = Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample");
    private static readonly byte[] Logo = File.ReadAllBytes(Path.Combine(Pack, "logo.png"));

    private static MarkdownDocument Document(string markdown) =>
        new(markdown, [new DocumentResource("mark.png", "image/png", Logo)], new Dictionary<string, string>());

    private static CoverImage? Resolve(string markdown, ResolvedBrand? brand = null)
    {
        var frontMatter = FrontMatter.Read(markdown, DocumentFormat.Pdf);
        return CoverImage.Resolve(frontMatter, Document(markdown), brand, DocumentFormat.Pdf);
    }

    [Fact]
    public void TheFrontMatter_NamesAResource_WithSizeAndPosition()
    {
        var image = Resolve("---\ncoverImage: mark.png\ncoverImageSize: large\ncoverImagePosition: right\n---\n# T\n")!;

        Assert.Equal("mark.png", image.Resource.Path);
        Assert.Equal(CoverImageSize.Large, image.Size);
        Assert.Equal(CoverImagePosition.Right, image.Position);
        Assert.Equal(0.25, image.HeightShare);
        Assert.Equal(2, image.Line);
        Assert.True(image.FromFrontMatter);
    }

    [Fact]
    public void Defaults_AreMediumAndBelow()
    {
        var image = Resolve("---\ncoverImage: mark.png\n---\n# T\n")!;

        Assert.Equal(1.0 / 6, image.HeightShare);
        Assert.Equal(CoverImagePosition.Below, image.Position);
        Assert.Equal(0.1, (image with { Size = CoverImageSize.Small }).HeightShare);
    }

    [Fact]
    public void AMissingResource_ThrowsNamingTheKeyAndThePath()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Resolve("---\ncoverImage: missing.png\n---\n# T\n"));

        Assert.Contains("'coverImage'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'missing.png'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("coverImageSize: huge", "'coverImageSize' has the value 'huge'; expected 'small' or 'medium' or 'large'")]
    [InlineData("coverImagePosition: left", "'coverImagePosition' has the value 'left'; expected 'below' or 'right'")]
    public void AnInvalidValue_Throws(string line, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Resolve($"---\n{line}\n---\n# T\n"));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBrandsImage_IsTheDefault_AndTheFrontMatterOverridesIt()
    {
        var brand = ResolvedBrand.Resolve(new Brand { Name = "a", CoverImage = new BrandImage(Logo) });

        var own = Resolve("# T\n", brand)!;
        Assert.False(own.FromFrontMatter);
        Assert.Equal(Logo, own.Resource.Content.ToArray());

        Assert.Equal("mark.png", Resolve("---\ncoverImage: mark.png\n---\n# T\n", brand)!.Resource.Path);
        Assert.Null(Resolve("# T\n"));
    }

    [Fact]
    public void ABrandsCoverImage_MustBeAPngOrJpeg()
    {
        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(new Brand { Name = "a", CoverImage = new BrandImage("<svg/>"u8.ToArray()) }));

        Assert.Contains("CoverImage", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrandPack_LoadsItsCoverImage()
    {
        var brand = BrandPack.Load(path => path switch
        {
            BrandPack.ManifestFile => new MemoryStream(Encoding.UTF8.GetBytes("""{ "name": "a", "colors": { "primary": "#111111" }, "images": { "coverImage": "mark.png" } }""")),
            "mark.png" => new MemoryStream(Logo),
            _ => null,
        });

        Assert.Equal(new BrandImage(Logo), brand.CoverImage);
    }

    [Fact]
    public void Place_KeepsTheAspectRatio_WithinItsLimits()
    {
        Assert.Equal((10.0, 20.0, 60.0, 30.0), CoverImage.Place(10, 20, false, 30, 2, 100, 1000, [], 1));
        Assert.Equal((10.0, 20.0, 40.0, 20.0), CoverImage.Place(10, 20, false, 30, 2, 40, 1000, [], 1));
        Assert.Equal((40.0, 20.0, 60.0, 30.0), CoverImage.Place(100, 20, true, 30, 2, 100, 1000, [], 1));
    }

    [Fact]
    public void Place_IsMadeSmaller_NeverMovedOverAnObstacle()
    {
        var placed = CoverImage.Place(0, 0, false, 100, 1, 1000, 1000, [(50, 50, 10, 10)], 1)!.Value;

        Assert.Equal((0.0, 0.0), (placed.X, placed.Y));
        Assert.Equal(50, placed.Height, 3);
        Assert.Null(CoverImage.Place(0, 0, false, 100, 1, 1000, 1000, [(0, 0, 10, 10)], 1));
    }

    [Fact]
    public void TheKeys_AreAcceptedByEveryTemplate()
    {
        var frontMatter = FrontMatter.Read("---\ncoverImage: mark.png\ncoverImageSize: small\ncoverImagePosition: below\n---\n", DocumentFormat.Pdf);

        var applied = frontMatter.Apply("options", (o, _) => o, "'a'", _ => "a", _ => [], (o, _, _) => o + "!");

        Assert.Equal("options", applied);
    }
}
