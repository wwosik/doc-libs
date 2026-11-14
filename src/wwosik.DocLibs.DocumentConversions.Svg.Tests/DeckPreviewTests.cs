using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Charts.Tests;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using wwosik.DocLibs.DocumentGeneration.Pptx;
using wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

namespace wwosik.DocLibs.DocumentConversions.Svg.Tests;

/// <summary>
/// Deck previews (E07-S03): slides painted from <see cref="DeckSvg"/>'s SVGs, the contact sheet, and the sample decks
/// against their committed golden images (<c>Samples/*.expected.png</c>, 640 px).
/// </summary>
public class DeckPreviewTests
{
    /// <summary>A table, a chart, a Mermaid diagram, a financial chart and an SVG image: what the sample decks do not cover.</summary>
    private const string Features = """
        # Previews

        ## Regions

        | Region | 2025 | 2026 |
        | ------ | ---: | ---: |
        | North  |    3 |    4 |
        | South  |    4 |    5 |

        ## Revenue {.chart}

        ```chart
        type: bar
        title: Revenue
        unit: EUR m
        ---
        year, north, south
        2025, 3, 4
        2026, 4, 5
        ```

        ## Flow

        ```mermaid
        flowchart LR
          A[Plan] --> B[Build] --> C[Ship]
        ```

        ## Development

        ```development
        ```

        ## Logo

        ![Logo](logo.svg)
        """;

    private static readonly ICodeBlockRenderer Financial = Samples.Renderer(request => request.Language == "development"
        ? new DrawingGraphic(FinancialCharts.Draw(FinancialSamples.Development(), FinancialSamples.Options with { Brand = request.Brand }))
        : null);

    public static TheoryData<string, string> Goldens => new()
    {
        { "deck", "neutral" }, { "deck", "sample" }, { "deck", "dark" },
        { "layouts", "neutral" },
        { "features", "neutral" },
    };

    private static PptxOptions Options(string template) => (template == "dark" ? DarkTemplate.Options : new PptxOptions { Template = Samples.BuiltIn(template) }) with
    {
        CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }, new ChartRenderer(), Financial],
        ImageConverters = [new SvgImageConverter()],
        TimeProvider = new FixedTime(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)),
    };

    private static MarkdownDocument Sample(string name)
    {
        if (name == "features")
        {
            var logo = new DocumentResource("logo.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40'><rect width='80' height='40' rx='8' fill='#c00'/><circle cx='20' cy='20' r='12' fill='#fff'/></svg>"u8.ToArray());
            return Samples.Markdown(Features, logo);
        }

        return new MarkdownDocument(
            File.ReadAllText(Samples.Path(name + ".md")).ReplaceLineEndings("\n"),
            [Samples.Image("diagram.png"), Samples.Image("photo.png")],
            new Dictionary<string, string>());
    }

    private static DeckSvgResult Svgs(byte[] pptx, DeckSvgOptions? options = null) => DeckSvg.Write(new MemoryStream(pptx), options);

    private static SKBitmap Bitmap(ReadOnlyMemory<byte> png) => SKBitmap.Decode(png.Span);

    [Fact]
    public void Slides_ArePainted_AtTheWidth_InTheSlidesAspectRatio()
    {
        var pptx = Samples.Generate(Samples.Markdown("## One\n\nA\n\n## Two\n\nB\n"));
        var svgs = Svgs(pptx);
        var previews = DeckPreview.Render(svgs.Slides, new DeckPreviewOptions { Fonts = svgs.Fonts });

        Assert.Equal([(1, "One"), (2, "Two")], previews.Slides.Select(s => (s.Number, s.Title)));
        using (var bitmap = Bitmap(previews.Slides[0].Png))
        {
            Assert.Equal((1280, 720), (bitmap.Width, bitmap.Height));
        }

        // 4:3.
        using var buffer = new MemoryStream();
        buffer.Write(pptx);
        using (var package = PresentationDocument.Open(buffer, true))
        {
            var size = package.PresentationPart!.Presentation!.SlideSize!;
            size.Cx = 9144000;
            size.Cy = 6858000;
        }

        var square = Svgs(buffer.ToArray());
        using var small = Bitmap(DeckPreview.Render(square.Slides, new DeckPreviewOptions { Width = 400, Fonts = square.Fonts }).Slides[1].Png);
        Assert.Equal((400, 300), (small.Width, small.Height));
        Assert.Equal((720, 540), (square.Slides[0].Width, square.Slides[0].Height));
    }

    [Fact]
    public void ASlideWithoutBackground_IsPaintedOnTheBackgroundColour()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 9\" width=\"16pt\" height=\"9pt\"/>"u8.ToArray();
        var previews = DeckPreview.Render([new SlideSvg(1, "x", svg, 16, 9)], new DeckPreviewOptions { Width = 32, Background = "102030" });

        using var bitmap = Bitmap(previews.Slides[0].Png);
        Assert.Equal(new SKColor(0x10, 0x20, 0x30), bitmap.GetPixel(5, 5));
    }

    [Fact]
    public void ABrandDeck_IsPaintedInItsEmbeddedFace()
    {
        // The sample brand's code face, Inconsolata, is embedded in the deck and is no system font.
        var svgs = Svgs(Samples.Generate(Samples.Markdown("## Plan\n\n```text\nfor each route: plan(route)\n```\n"), Options("sample")));
        var mono = svgs.Fonts.Where(f => TrueTypeInfo.FamilyName(f.Span) == "Inconsolata").ToList();
        Assert.NotEmpty(mono);
        Assert.Contains("font-family=\"Inconsolata\"", System.Text.Encoding.UTF8.GetString(svgs.Slides[0].Svg.Span), StringComparison.Ordinal);

        var painted = DeckPreview.Render(svgs.Slides, new DeckPreviewOptions { Width = 640, Fonts = svgs.Fonts });
        var withoutMono = DeckPreview.Render(svgs.Slides, new DeckPreviewOptions { Width = 640, Fonts = [.. svgs.Fonts.Except(mono)] });
        Samples.WriteForReview("preview-embedded-face.png", painted.Slides[0].Png.ToArray());

        // The embedded face draws the code: without it, the same slide paints differently.
        Assert.False(painted.Slides[0].Png.Span.SequenceEqual(withoutMono.Slides[0].Png.Span));
    }

    [Fact]
    public void AnUnknownFamily_IsPaintedInLiberationSans()
    {
        using var buffer = new MemoryStream();
        buffer.Write(Samples.Generate(Samples.Markdown("## Plan\n\nThe grid is ready for twice the solar power.\n")));
        using (var package = PresentationDocument.Open(buffer, true))
        {
            var theme = package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!;
            theme.ThemeElements!.FontScheme!.MinorFont!.LatinFont!.Typeface = "Made Up Sans";
        }

        var svgs = Svgs(buffer.ToArray());
        Assert.Contains(svgs.Notes, n => n.Code == NoteCodes.FontMissing && n.Message.Contains("Made Up Sans", StringComparison.Ordinal));
        Assert.DoesNotContain("Made Up Sans", System.Text.Encoding.UTF8.GetString(svgs.Slides[0].Svg.Span), StringComparison.Ordinal);
        var sans = svgs.Fonts.Where(f => TrueTypeInfo.FamilyName(f.Span) == "Liberation Sans").ToList();
        Assert.Equal(svgs.Fonts.Count, sans.Count);

        var painted = DeckPreview.Render(svgs.Slides, new DeckPreviewOptions { Width = 640, Fonts = svgs.Fonts });
        using var bitmap = Bitmap(painted.Slides[0].Png);
        Assert.True(Dark(bitmap, new SKRectI(0, 80, 640, 130)), "the body text is not painted");
    }

    [Fact]
    public void ASlideThatCannotBePainted_NamesItsNumber()
    {
        var error = Assert.Throws<DocumentConversionException>(() => DeckPreview.Render([new SlideSvg(7, "Broken", "<svg"u8.ToArray(), 960, 540)]));

        Assert.Equal(DocumentFormat.Svg, error.Format);
        Assert.Contains("Slide 7", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => DeckPreview.Render([], new DeckPreviewOptions { Width = SvgConverter.MaxPixels + 1 }));
    }

    private static DeckPreviewResult Thumbnails(int count, int width = 64, int height = 36)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(0x20, 0x40, 0x80));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = data.ToArray();
        return new DeckPreviewResult([.. Enumerable.Range(1, count).Select(n => new SlidePreview(n, $"Slide {n}", png))]);
    }

    [Fact]
    public void TheContactSheet_LaysOutColumnsAndRows_WithNumbers()
    {
        var label = DeckSvg.Write(new MemoryStream(Samples.Generate(Samples.Markdown("## T\n")))).LabelFont;
        var sheet = DeckPreview.ContactSheet(Thumbnails(5), new ContactSheetOptions { Columns = 2, ThumbnailWidth = 100, Gap = 10, LabelFont = label });

        // 2 columns of 100 with gaps of 10; 3 rows of 56 + a 16 px label line (10 px type) + 10.
        using var bitmap = Bitmap(sheet);
        Assert.Equal((230, 256), (bitmap.Width, bitmap.Height));
        Assert.Equal(new SKColor(0xF2, 0xF2, 0xF2), bitmap.GetPixel(2, 2));
        Assert.Equal(new SKColor(0x20, 0x40, 0x80), bitmap.GetPixel(60, 40));
        Assert.Equal(new SKColor(0x20, 0x40, 0x80), bitmap.GetPixel(60, 200));
        Assert.Equal(new SKColor(0xF2, 0xF2, 0xF2), bitmap.GetPixel(170, 120 + 82)); // no sixth slide
        Assert.True(Dark(bitmap, new SKRectI(10, 66, 110, 82)), "the first label is missing");

        var unlabelled = DeckPreview.ContactSheet(Thumbnails(5), new ContactSheetOptions { Columns = 2, ThumbnailWidth = 100, Gap = 10, Labels = false });
        using var plain = Bitmap(unlabelled);
        Assert.Equal((230, 208), (plain.Width, plain.Height));

        // Without a label font, SkiaSharp's default typeface; a platform without one gets no labels.
        using var fallback = Bitmap(DeckPreview.ContactSheet(Thumbnails(5), new ContactSheetOptions { Columns = 2, ThumbnailWidth = 100, Gap = 10 }));
        Assert.Equal(SKTypeface.Default.GlyphCount > 0 ? 256 : 208, fallback.Height);

        // Fewer slides than columns: the sheet is as wide as the slides.
        using var two = Bitmap(DeckPreview.ContactSheet(Thumbnails(2), new ContactSheetOptions { ThumbnailWidth = 100, Gap = 10, Labels = false }));
        Assert.Equal((230, 76), (two.Width, two.Height));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private static bool Dark(SKBitmap bitmap, SKRectI area)
    {
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (bitmap.GetPixel(x, y).Red < 0x90)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void AnOversizeContactSheet_IsRefused_NamingAWidthThatFits()
    {
        var slides = Thumbnails(100, 64, 36);
        var options = new ContactSheetOptions { Columns = 1, Labels = false };
        var error = Assert.Throws<ArgumentException>(() => DeckPreview.ContactSheet(slides, options));
        var match = System.Text.RegularExpressions.Regex.Match(error.Message, "ThumbnailWidth of at most (\\d+)");
        Assert.True(match.Success, error.Message);
        var fits = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        using var sheet = Bitmap(DeckPreview.ContactSheet(slides, options with { ThumbnailWidth = fits }));
        Assert.True(sheet.Height <= SvgConverter.MaxPixels);
        Assert.Throws<ArgumentException>(() => DeckPreview.ContactSheet(slides, options with { ThumbnailWidth = fits + 1 }));
        Assert.Throws<ArgumentException>(() => DeckPreview.ContactSheet(new DeckPreviewResult([])));
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void TheSampleDecks_MatchTheirGoldenImages(string sample, string template)
    {
        var svgs = Svgs(Samples.Generate(Sample(sample), Options(template)));
        Assert.DoesNotContain(svgs.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
        var previews = DeckPreview.Render(svgs.Slides, new DeckPreviewOptions { Width = 640, Fonts = svgs.Fonts });
        var sheet = DeckPreview.ContactSheet(previews, new ContactSheetOptions { LabelFont = svgs.LabelFont });
        Samples.WriteForReview($"preview-{sample}-{template}-sheet.png", sheet);

        var failures = new List<string>();
        foreach (var slide in previews.Slides)
        {
            var name = $"{sample}-{template}-{slide.Number}";
            Samples.WriteForReview($"preview-{name}.png", slide.Png.ToArray());
            if (Golden.Compare(name, slide.Png) is { } failure)
            {
                failures.Add(failure);
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        var committed = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), $"{sample}-{template}-*.expected.png").Length;
        Assert.True(Golden.Updating || committed == previews.Slides.Count, $"{committed} golden images for {previews.Slides.Count} slides");
    }
}

/// <summary>
/// Golden images: <c>Samples/&lt;name&gt;.expected.png</c>, compared with a tight tolerance so rasteriser differences
/// between platforms pass and real changes fail; <c>DOCLIBS_UPDATE_EXPECTED=1</c> rewrites them.
/// </summary>
internal static class Golden
{
    /// <summary>The mean absolute difference per channel may be at most 1 % of the range.</summary>
    public const double MeanTolerance = 0.01;

    /// <summary>At most 0.5 % of the pixels may differ by more than 25 % in some channel.</summary>
    public const double OffTolerance = 0.005;

    /// <summary>What counts as a pixel off: a channel differing by more than 25 %.</summary>
    public const int OffBy = 64;

    /// <summary>Whether the tests write the golden images instead of comparing.</summary>
    public static bool Updating => Environment.GetEnvironmentVariable(Samples.UpdateVariable) == "1";

    /// <summary>A description of the difference, or <see langword="null"/> when the image matches.</summary>
    public static string? Compare(string name, ReadOnlyMemory<byte> png, [CallerFilePath] string caller = "")
    {
        var file = name + ".expected.png";
        if (Updating)
        {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(caller)!, "Samples"));
            File.WriteAllBytes(Path.Combine(folder.FullName, file), png.ToArray());
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Samples", file);
        if (!File.Exists(path))
        {
            return $"{file} is missing; run the tests with {Samples.UpdateVariable}=1 to write it.";
        }

        using var expected = SKBitmap.Decode(path);
        using var actual = SKBitmap.Decode(png.Span);
        if ((expected.Width, expected.Height) != (actual.Width, actual.Height))
        {
            return $"{name}: {actual.Width} × {actual.Height} pixels, expected {expected.Width} × {expected.Height}.";
        }

        var (sum, off) = (0L, 0);
        var a = expected.Pixels;
        var b = actual.Pixels;
        for (var i = 0; i < a.Length; i++)
        {
            var (dr, dg, db) = (Math.Abs(a[i].Red - b[i].Red), Math.Abs(a[i].Green - b[i].Green), Math.Abs(a[i].Blue - b[i].Blue));
            sum += dr + dg + db;
            if (Math.Max(dr, Math.Max(dg, db)) > OffBy)
            {
                off++;
            }
        }

        var mean = sum / (3.0 * a.Length * 255);
        var share = (double)off / a.Length;
        if (mean <= MeanTolerance && share <= OffTolerance)
        {
            return null;
        }

        Samples.WriteForReview($"preview-{name}.expected.png", File.ReadAllBytes(path));
        return $"{name}: mean difference {mean:P2} (at most {MeanTolerance:P0}), {share:P2} of pixels off (at most {OffTolerance:P1}); see .tmp/review/preview-{name}.png.";
    }
}
