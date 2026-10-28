using DocumentFormat.OpenXml.Office2019.Drawing.SVG;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SkiaSharp;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentConversions.Svg.Tests;

public class SvgConverterTests
{
    private const string Squares = """
        <svg xmlns="http://www.w3.org/2000/svg" width="40" height="20">
          <rect x="0" y="0" width="20" height="20" fill="#FF0000"/>
          <rect x="20" y="0" width="20" height="20" fill="#0000FF"/>
        </svg>
        """;

    [Fact]
    public void ToPng_DrawsTheSvg_AtTwiceItsSize_OnTransparency()
    {
        using var bitmap = SKBitmap.Decode(SvgConverter.ToPng(Squares));

        Assert.Equal((80, 40), (bitmap.Width, bitmap.Height));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(10, 20));
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(70, 20));
    }

    [Fact]
    public void Size_ComesFromWidthAndHeight_ElseTheViewBox_AndTheOptionsCanSetIt()
    {
        const string ViewBoxOnly = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 300 150\"><rect width=\"300\" height=\"150\" fill=\"#00FF00\"/></svg>";

        Assert.Equal((40.0, 20.0), SvgConverter.Size(System.Text.Encoding.UTF8.GetBytes(Squares)));
        Assert.Equal((300.0, 150.0), SvgConverter.Size(System.Text.Encoding.UTF8.GetBytes(ViewBoxOnly)));
        using var wide = SKBitmap.Decode(SvgConverter.ToPng(ViewBoxOnly, new SvgPngOptions { Width = 600 }));
        using var one = SKBitmap.Decode(SvgConverter.ToPng(ViewBoxOnly, new SvgPngOptions { Scale = 1 }));
        Assert.Equal((600, 300), (wide.Width, wide.Height));
        Assert.Equal((300, 150), (one.Width, one.Height));
    }

    [Fact]
    public void ABackground_FillsWhatTheSvgLeavesOpen()
    {
        const string Dot = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><circle cx=\"5\" cy=\"5\" r=\"1\" fill=\"#000000\"/></svg>";

        using var transparent = SKBitmap.Decode(SvgConverter.ToPng(Dot));
        using var white = SKBitmap.Decode(SvgConverter.ToPng(Dot, new SvgPngOptions { Background = "#FFFFFF" }));

        Assert.Equal(0, transparent.GetPixel(0, 0).Alpha);
        Assert.Equal(SKColors.White, white.GetPixel(0, 0));
    }

    [Fact]
    public void TextIsDrawn_InAFontPassedIn_BeforeTheSystemsFonts()
    {
        var narrow = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "fonts", "PT_Sans-Narrow-Web-Regular.ttf"));
        const string Text = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"40\"><text x=\"0\" y=\"30\" font-family=\"PT Sans Narrow\" font-size=\"24\">Wide wide wide wide</text></svg>";

        var system = InkWidth(SvgConverter.ToPng(Text));
        var given = InkWidth(SvgConverter.ToPng(Text, new SvgPngOptions { Fonts = [narrow] }));

        Assert.True(system > 100, $"text drawn in a system font: {system} px");
        Assert.True(given < system * 0.9, $"the narrow font sets it narrower: {given} px against {system} px");
    }

    [Theory]
    [InlineData("not an svg at all")]
    [InlineData("<html><body/></html>")]
    public void ASvgThatCannotBeDrawn_Throws_AsAnSvgProblem(string svg)
    {
        var error = Assert.Throws<DocumentConversionException>(() => SvgConverter.ToPng(svg));

        Assert.Equal(DocumentFormat.Svg, error.Format);
        Assert.StartsWith("The SVG cannot be converted: ", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingOutsideTheSvg_IsLoaded_AndScriptsNeverRun()
    {
        var red = Path.Combine(Path.GetTempPath(), $"doclibs-{Guid.NewGuid():N}.png");
        using (var image = new SKBitmap(10, 10))
        {
            image.Erase(SKColors.Red);
            File.WriteAllBytes(red, image.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        try
        {
            var svg = $"""
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="10" height="10">
                  <script>document.documentElement.setAttribute('width', '500');</script>
                  <image x="0" y="0" width="10" height="10" href="{new Uri(red).AbsoluteUri}" xlink:href="{new Uri(red).AbsoluteUri}"/>
                </svg>
                """;

            using var bitmap = SKBitmap.Decode(SvgConverter.ToPng(svg));

            Assert.Equal(20, bitmap.Width);
            Assert.Equal(0, bitmap.GetPixel(10, 10).Alpha);
        }
        finally
        {
            File.Delete(red);
        }
    }

    [Fact]
    public void LinksInsideTheSvg_AndDataUris_StillWork()
    {
        using var green = new SKBitmap(2, 2);
        green.Erase(SKColors.Lime);
        var data = "data:image/png;base64," + Convert.ToBase64String(green.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="20" height="10">
              <defs><linearGradient id="g"><stop offset="0" stop-color="#0000FF"/><stop offset="1" stop-color="#0000FF"/></linearGradient></defs>
              <rect width="10" height="10" style="fill: url(#g)"/>
              <image x="10" y="0" width="10" height="10" xlink:href="{data}"/>
            </svg>
            """;

        using var bitmap = SKBitmap.Decode(SvgConverter.ToPng(svg, new SvgPngOptions { Scale = 1 }));

        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(5, 5));
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(15, 5));
    }

    [Fact]
    public void ASvgImage_InADeck_IsAPngWithTheSvgBesideIt()
    {
        var document = new MarkdownDocument("## Plan\n\n![Plan](images/plan.svg)\n", [new DocumentResource("images/plan.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(Squares))], new Dictionary<string, string>());
        using var output = new MemoryStream();
        PptxGenerator.Generate(document, output, new PptxOptions { ImageConverters = [new SvgImageConverter()] });

        using var package = PresentationDocument.Open(new MemoryStream(output.ToArray()), false);
        var slide = package.PresentationPart!.SlideParts.Single(p => p.Slide!.Descendants<A.Blip>().Any());
        var blip = slide.Slide!.Descendants<A.Blip>().Single();
        var vector = blip.Descendants<SVGBlip>().Single();
        Assert.Equal("image/png", slide.GetPartById(blip.Embed!.Value!).ContentType);
        Assert.Equal("image/svg+xml", slide.GetPartById(vector.Embed!.Value!).ContentType);
        Assert.Empty(new OpenXmlValidator().Validate(package));
    }

    [Fact]
    public void AChartSvg_InADeck_KeepsItsTooltipsOutOfThePicture()
    {
        var svg = wwosik.DocLibs.DocumentGeneration.Charts.FinancialCharts.ToSvg(wwosik.DocLibs.DocumentGeneration.Charts.Tests.FinancialSamples.TwoYear(), wwosik.DocLibs.DocumentGeneration.Charts.Tests.FinancialSamples.Options);
        var document = new MarkdownDocument("## Sales\n\n![Sales by product line](sales.svg)\n", [new DocumentResource("sales.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(svg))], new Dictionary<string, string>());
        using var output = new MemoryStream();
        PptxGenerator.Generate(document, output, new PptxOptions { ImageConverters = [new SvgImageConverter()] });
        WriteForReview("svg-image.pptx", output.ToArray());

        // Hidden tooltips stay hidden in the PNG: the corner above the first gridline label is empty.
        using var bitmap = SKBitmap.Decode(SvgConverter.ToPng(svg, new SvgPngOptions { Scale = 1 }));
        Assert.Equal(SKColors.White, bitmap.GetPixel(bitmap.Width - 3, 3));
    }

    [Fact]
    public void ASvgImage_InAPdf_IsPlacedAtItsNaturalSize()
    {
        var document = new MarkdownDocument("# Plan\n\n![Plan](plan.svg)\n", [new DocumentResource("plan.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(Squares))], new Dictionary<string, string>());
        using var output = new MemoryStream();
        PdfGenerator.Generate(document, output, new PdfOptions { ImageConverters = [new SvgImageConverter()] });

        using var pdf = PdfDocument.Open(output.ToArray());
        var image = pdf.GetPages().SelectMany(p => p.GetImages()).Single();
        Assert.Equal(80, image.WidthInSamples); // drawn at twice its size
        Assert.Equal(30, image.BoundingBox.Width, 1); // placed at 40 px = 30 pt
    }

    [Fact]
    public void WithoutAConverter_ASvgImage_StaysAnError()
    {
        var document = new MarkdownDocument("# Plan\n\n![Plan](plan.svg)\n", [new DocumentResource("plan.svg", "image/svg+xml", System.Text.Encoding.UTF8.GetBytes(Squares))], new Dictionary<string, string>());

        var pdf = Assert.Throws<DocumentConversionException>(() => PdfGenerator.Generate(document, new MemoryStream()));
        var deck = Assert.Throws<DocumentConversionException>(() => PptxGenerator.Generate(document, new MemoryStream()));

        Assert.Contains("is not a PNG or JPEG", pdf.Reason, StringComparison.Ordinal);
        Assert.Contains("is not a PNG, JPEG or GIF", deck.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADiagramTheLibraryDraws_BecomesAPng_ViaItsSvg()
    {
        var graphic = new MermaidRenderer().Render(new CodeBlockRequest("mermaid", "flowchart LR\n  A[Order] --> B[Invoice]\n", Brand.Neutral));
        var drawing = Assert.IsType<DrawingGraphic>(graphic).Drawing;

        using var bitmap = SKBitmap.Decode(SvgConverter.ToPng(DrawingSvg.Write(drawing)));

        Assert.InRange(bitmap.Width, Math.Floor(drawing.Width * 2), Math.Ceiling(drawing.Width * 2));
        Assert.True(InkWidth(bitmap) > bitmap.Width / 2);
    }

    // Writes a generated file to .tmp/review/ at the repo root, for a human to open.
    private static void WriteForReview(string name, byte[] content, [System.Runtime.CompilerServices.CallerFilePath] string caller = "")
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, "..", "..", ".tmp", "review"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name), content);
    }

    // How wide the drawn (not transparent) part of an image is.
    private static int InkWidth(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return InkWidth(bitmap);
    }

    private static int InkWidth(SKBitmap bitmap)
    {
        var columns = Enumerable.Range(0, bitmap.Width).Where(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).Alpha > 0)).ToList();
        return columns.Count == 0 ? 0 : columns[^1] - columns[0] + 1;
    }
}
