using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>
/// Dark PDFs (E03-S06): only on request, by <see cref="BrandPdfTemplateOptions.Tone"/> or <c>tone: dark</c>; every page
/// in the dark background, light text, the logo for dark backgrounds, diagrams for a dark background, still PDF/A-2b.
/// </summary>
public class DarkPagesTests
{
    private const string Rich =
        "# Network expansion\n\nStatus report\n\n## Summary\n\nText with a [link](https://example.com) and a note[^1], `code`, "
        + "[done]{.success} and [late]{.danger}.\n\n- [ ] open\n- [x] done\n\n> quoted\n\n> [!WARNING]\n> careful\n\n"
        + "| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n\n```csharp\nvar x = \"text\"; // comment\n```\n\n"
        + "```mermaid\nflowchart LR\n  A[Start] --> B[End]\n```\n\n---\n\n## Next\n\nMore to do.\n\n[^1]: The note.\n";

    private static PdfOptions Options(Brand brand, PdfTone tone, bool draft = false) => new()
    {
        Template = new BrandPdfTemplateOptions { Brand = brand, Tone = tone },
        CodeBlockRenderers = [new MermaidRenderer()],
        HighlightCode = true,
        Draft = draft,
        Classification = draft ? Classification.Confidential : Classification.Public,
    };

    private static string Hex(Letter letter)
    {
        var (r, g, b) = letter.Color.ToRGBValues();
        int Channel(double c) => (int)Math.Round(c * 255);
        return string.Create(CultureInfo.InvariantCulture, $"{Channel(r):X2}{Channel(g):X2}{Channel(b):X2}");
    }

    private static string Horizontal(Page page) =>
        string.Concat(page.Letters.Where(l => l.TextOrientation == TextOrientation.Horizontal).Select(l => l.Value));

    [Fact]
    public void ToneIsLightByDefault_AndALightPdfIgnoresTheSlideColours()
    {
        Assert.Equal(PdfTone.Light, new BrandPdfTemplateOptions().Tone);
        var withSlides = Samples.Brand with { SlideColors = new BrandColors { Primary = "111827" }, Name = "sample" };
        static PdfOptions Default(Brand brand) => Options(brand, PdfTone.Light) with { Template = new BrandPdfTemplateOptions { Brand = brand } };

        Assert.Equal(Masked(Samples.Generate(Samples.Markdown(Rich), Default(Samples.Brand))), Masked(Samples.Generate(Samples.Markdown(Rich), Default(withSlides))));
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void EveryPage_StartsWithTheBackground_AndEveryTextReaches4Point5To1(string name)
    {
        var brand = name == "sample" ? Samples.Brand : Brand.Neutral;
        var (resolved, design) = DarkPages.For(brand);
        var pdf = Samples.Generate(Samples.Markdown(Rich), Options(brand, PdfTone.Dark));
        Samples.WriteForReview($"dark-{name}.pdf", pdf);

        Assert.True(ResolvedBrand.Contrast(design.Background, "FFFFFF") >= 7);
        Assert.Equal("FFFFFF", resolved.ThemeColors[0]);
        using var document = PdfDocument.Open(pdf);
        Assert.True(document.NumberOfPages >= 2);
        foreach (var page in document.GetPages())
        {
            // The code box, a white veil over the background, is the lightest surface text stands on.
            Assert.All(page.Letters.Where(l => l.Value.Trim().Length > 0), l =>
                Assert.True(ResolvedBrand.Contrast(Hex(l), design.CodeBackground) >= 4.5, $"'{l.Value}' in {Hex(l)} on page {page.Number}"));
        }

        Assert.Contains("Network expansion", Horizontal(document.GetPage(1)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLayout_PaintsEveryPageIncludingTheCover_UnderTheWatermark()
    {
        var (brand, dark) = DarkPages.For(Samples.Brand);
        var geometry = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);
        var blocks = new List<Block> { new ParagraphBlock([new TextRun("One", RunStyle.None)]), new PageBreakBlock(), new ParagraphBlock([new TextRun("Two", RunStyle.None)]) };
        var layout = new LayoutEngine(
            new FakeMetrics(),
            geometry,
            new Design(brand, ShowTagline: false, null, "Report") { Background = dark.Background, Watermark = "DRAFT" })
            .Run(new DocumentModel(new Cover([new TextRun("Title", RunStyle.None)], [], null), blocks));

        Assert.Equal(3, layout.Pages.Count);
        Assert.All(layout.Pages, page =>
        {
            Assert.Equal(new RectItem(0, 0, geometry.Width, geometry.Height, dark.Background), page.Items[0]);
            var watermark = Assert.IsType<TextItem>(page.Items[1]);
            Assert.Equal(DarkPages.Over("FFFFFF", 0.15, dark.Background), watermark.Color);
        });

        // The logo for dark backgrounds, on the cover and in the header.
        var onDark = ResolvedBrand.Resolve(Samples.Brand).LogoOnDark;
        Assert.NotNull(onDark);
        Assert.All(layout.Pages, page => Assert.Contains(page.Items.OfType<ImageItem>(), i => ReferenceEquals(i.Key, onDark)));
    }

    [Fact]
    public void TheBackground_IsTheSlideColoursPrimary()
    {
        var brand = Samples.Brand with { SlideColors = new BrandColors { Primary = "1E1B4B" } };

        Assert.Equal("1E1B4B", DarkPages.For(brand).Design.Background);
        // A primary colour too light for white text is darkened to 7:1.
        Assert.NotEqual("0E7C86", DarkPages.For(Samples.Brand).Design.Background);
    }

    [Fact]
    public void ADarkDraft_StaysPdfA2b_WithoutTransparency()
    {
        var pdf = Samples.Generate(Samples.Markdown(Rich), Options(Samples.Brand, PdfTone.Dark, draft: true));
        var raw = Encoding.Latin1.GetString(pdf);
        Samples.WriteForReview("dark-sample-draft.pdf", pdf);

        Assert.Contains("<pdfaid:part>2</pdfaid:part>", raw, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/SMask", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/CA ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/ca ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/Group", raw, StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        Assert.All(document.GetPages(), p => Assert.Equal("DRAFT", string.Concat(p.Letters.Where(l => l.TextOrientation != TextOrientation.Horizontal).Select(l => l.Value))));
        Assert.All(document.GetPages().SelectMany(p => p.Letters), l => Assert.DoesNotContain("Helvetica", l.FontName, StringComparison.Ordinal));
    }

    [Fact]
    public void TheFrontMatter_SetsTheTone_AndATemplateSwitchKeepsIt()
    {
        static byte[] Pdf(string frontMatter, PdfTone tone = PdfTone.Light) =>
            Samples.Generate(Samples.Markdown($"---\n{frontMatter}\n---\n{Rich}"), Options(Brand.Neutral, tone) with { Brands = [Samples.Brand] });

        var light = Samples.Generate(Samples.Markdown(Rich), Options(Brand.Neutral, PdfTone.Light));
        var dark = Samples.Generate(Samples.Markdown(Rich), Options(Brand.Neutral, PdfTone.Dark));
        Assert.Equal(Masked(dark), Masked(Pdf("tone: dark")));
        Assert.Equal(Masked(light), Masked(Pdf("tone: light", PdfTone.Dark)));

        var sampleDark = Samples.Generate(Samples.Markdown(Rich), Options(Samples.Brand, PdfTone.Dark));
        Assert.Equal(Masked(sampleDark), Masked(Pdf("template: sample", PdfTone.Dark)));

        var error = Assert.Throws<DocumentConversionException>(() => Pdf("tone: dusk"));
        Assert.Contains("tone", error.Message, StringComparison.Ordinal);
    }

    // The document's ID is random: everything else must match.
    private static string Masked(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Replace(Encoding.Latin1.GetString(pdf), @"/ID \[[^\]]*\]", string.Empty);

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
