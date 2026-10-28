using System.IO.Compression;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class BrandPackTests : IDisposable
{
    private static readonly string Pack = Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample");

    private readonly string scratch = Path.Combine(AppContext.BaseDirectory, "scratch-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Directory_LoadsEveryPartOfTheSample()
    {
        var brand = BrandPack.Load(Pack);

        Assert.Equal("sample", brand.Name);
        Assert.Equal("Sample Corporation", brand.FooterText);
        Assert.Equal("#0E7C86", brand.Colors.Primary);
        Assert.Equal("#B45309", brand.Colors.Link);
        Assert.Equal("#E6F4F5", brand.Colors.TableBand);
        Assert.Equal(["#0E7C86", "#5EB8BF"], brand.Colors.Gradient);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Pack, "logo.png")), brand.Logo!.Content.ToArray());
        Assert.NotNull(brand.LogoOnDark);
        Assert.NotNull(brand.CoverArtwork);
        Assert.NotNull(brand.Tagline);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Pack, "fonts", "mono", "Inconsolata-Regular.ttf")), brand.Fonts!.Mono!.Content.ToArray());
        Assert.Null(brand.Pptx);
        Assert.Equal("Liberation Serif", ResolvedBrand.Resolve(brand).FontFamily);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Pack, "fonts", "table", "PT_Sans-Narrow-Web-Bold.ttf")), brand.Fonts.Table!.Bold.Content.ToArray());
        Assert.Null(brand.Fonts.Table.Italic);
        Assert.Equal("PT Sans Narrow", ResolvedBrand.Resolve(brand).TableFontFamily);
        Assert.Equal(6, brand.Colors.Diagram!.Palette!.Count);
    }

    [Fact]
    public void DiagramColours_AreLoaded()
    {
        var brand = BrandPack.Load(Files("""
            { "name": "a", "colors": { "primary": "#111111",
              "diagram": { "text": "#1", "line": "#2", "accent": "#3", "surface": "#4", "border": "#5", "palette": ["#6", "#7"] } } }
            """));

        Assert.Equal(
            new BrandDiagramColors { Text = "#1", Line = "#2", Accent = "#3", Surface = "#4", Border = "#5", Palette = ["#6", "#7"] },
            brand.Colors.Diagram);
    }

    [Fact]
    public void HighlightAndLabelColours_AreLoaded()
    {
        var brand = BrandPack.Load(Files("""
            { "name": "a", "colors": { "primary": "#111111", "highlight": "#222222",
              "labels": { "success": "#1", "info": "#2", "warning": "#3", "danger": "#4", "neutral": "#5" } } }
            """));

        Assert.Equal("#222222", brand.Colors.Highlight);
        Assert.Equal(new BrandLabelColors { Success = "#1", Info = "#2", Warning = "#3", Danger = "#4", Neutral = "#5" }, brand.Colors.Labels);
    }

    [Fact]
    public void ChartColours_AreLoaded()
    {
        var brand = BrandPack.Load(Files("""
            { "name": "a", "colors": { "primary": "#111111",
              "chart": { "series": ["#1", "#2"], "categories": ["#3"], "reference": "#4", "ahead": "#5", "surface": "#6", "tooltipFill": "#7" } } }
            """));

        Assert.Equal(
            new BrandChartColors { Series = ["#1", "#2"], Categories = ["#3"], Reference = "#4", Ahead = "#5", Surface = "#6", TooltipFill = "#7" },
            brand.Colors.Chart);
    }

    [Fact]
    public void TableFonts_AreLoadedWithTheirOptionalKeys()
    {
        var brand = BrandPack.Load(Files(
            """
            { "name": "a", "colors": { "primary": "#111111" },
              "fonts": { "regular": "r", "bold": "r", "italic": "r", "boldItalic": "r",
                         "table": { "regular": "t", "bold": "tb", "italic": "ti", "boldItalic": "tbi", "family": "House Narrow" } } }
            """,
            ("r", [1]), ("t", [2]), ("tb", [3]), ("ti", [4]), ("tbi", [5])));

        var table = brand.Fonts!.Table!;
        Assert.Equal([2], table.Regular.Content.ToArray());
        Assert.Equal([3], table.Bold.Content.ToArray());
        Assert.Equal([4], table.Italic!.Content.ToArray());
        Assert.Equal([5], table.BoldItalic!.Content.ToArray());
        Assert.Equal("House Narrow", table.Family);
    }

    [Fact]
    public void Zip_And_EmbeddedResources_GiveTheSameBrand()
    {
        var fromDirectory = BrandPack.Load(Pack);

        using var zip = new MemoryStream();
        ZipFile.CreateFromDirectory(Pack, zip);
        zip.Position = 0;

        Assert.Equal(fromDirectory, BrandPack.Load(zip));
        Assert.Equal(fromDirectory, BrandPack.Load(typeof(BrandPackTests).Assembly, "Brand."));
    }

    [Fact]
    public void Callback_GetsPackRelativeSlashPaths()
    {
        var asked = new List<string>();

        BrandPack.Load(path =>
        {
            asked.Add(path);
            return File.OpenRead(Path.Combine(Pack, path));
        });

        Assert.Contains("brand.json", asked);
        Assert.Contains("fonts/LiberationSerif-Regular.ttf", asked);
    }

    [Fact]
    public void PptxTemplate_IsLoadedWithItsLayouts()
    {
        var brand = BrandPack.Load(Files(
            """{ "name": "a", "colors": { "primary": "#111111" }, "pptx": { "template": "t.potx", "layouts": { "title": "Cover", "sectionHeader": "Divider" } } }""",
            ("t.potx", [1, 2, 3])));

        Assert.Equal([1, 2, 3], brand.Pptx!.Content.ToArray());
        Assert.Equal("Cover", brand.Pptx.TitleLayout);
        Assert.Equal("Divider", brand.Pptx.SectionHeaderLayout);
        Assert.Null(brand.Pptx.ContentLayout);
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAllowed()
    {
        var brand = BrandPack.Load(Files("""
            {
              // the house colours
              "name": "a",
              "colors": { "primary": "#111111", },
            }
            """));

        Assert.Equal("a", brand.Name);
    }

    [Theory]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "colour": 1 }""", "colour")]
    [InlineData("""{ "name": "a", "colors": { "primry": "#111111" } }""", "primry")]
    [InlineData("""{ "colors": { "primary": "#111111" } }""", "'name' is missing")]
    [InlineData("""{ "name": "a" }""", "'colors' is missing")]
    [InlineData("""{ "name": "a", "colors": { } }""", "'colors.primary' is missing")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "fonts": { "regular": "r.ttf" } }""", "'fonts.regular' names 'r.ttf', which is not in the brand pack")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "fonts": { "regular": "r", "bold": "r", "italic": "r", "boldItalic": "r", "table": { "regular": "r" } } }""", "'fonts.table.bold' is missing")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "fonts": { "regular": "r", "bold": "r", "italic": "r", "boldItalic": "r", "table": { "regular": "r", "bold": "r", "light": "r" } } }""", "light")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "fonts": { "regular": "r", "bold": "r", "italic": "r", "boldItalic": "r", "table": { "regular": "r", "bold": "x" } } }""", "'fonts.table.bold' names 'x'")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111", "diagram": { "fill": "#222222" } } }""", "fill")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "images": { "logo": "../logo.png" } }""", "'images.logo' is '../logo.png'")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "images": { "logo": "/etc/logo.png" } }""", "'images.logo' is '/etc/logo.png'")]
    [InlineData("""{ "name": "a", "colors": { "primary": "#111111" }, "pptx": { } }""", "'pptx.template' is missing")]
    [InlineData("""{ "name": """, "brand.json")]
    public void Mistakes_ThrowNamingTheKey(string json, string expected)
    {
        var error = Assert.Throws<InvalidDataException>(() => BrandPack.Load(Files(json, ("r", [1]))));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingManifest_Throws()
    {
        var error = Assert.Throws<InvalidDataException>(() => BrandPack.Load(_ => null));

        Assert.Contains("no brand.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Directory_RefusesALinkLeadingOutOfThePack()
    {
        var outside = Path.Combine(this.scratch, "outside.png");
        var pack = Path.Combine(this.scratch, "pack");
        Directory.CreateDirectory(pack);
        File.Copy(Path.Combine(Pack, "logo.png"), outside);
        File.CreateSymbolicLink(Path.Combine(pack, "logo.png"), outside);
        File.WriteAllText(Path.Combine(pack, "brand.json"), """{ "name": "a", "colors": { "primary": "#111111" }, "images": { "logo": "logo.png" } }""");

        var error = Assert.Throws<InvalidDataException>(() => BrandPack.Load(pack));

        Assert.Contains("'logo.png' leads out of the brand pack", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileContents_AreCheckedWhenTheBrandIsUsed()
    {
        var brand = BrandPack.Load(Files(
            """{ "name": "a", "colors": { "primary": "#111111" }, "images": { "logo": "logo.png" } }""",
            ("logo.png", "not an image"u8.ToArray())));

        Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(brand));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.scratch))
        {
            Directory.Delete(this.scratch, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static Func<string, Stream?> Files(string json, params (string Path, byte[] Content)[] files) => path =>
        path == BrandPack.ManifestFile ? new MemoryStream(Encoding.UTF8.GetBytes(json))
        : files.FirstOrDefault(f => f.Path == path) is { Content: { } content } ? new MemoryStream(content)
        : null;
}
