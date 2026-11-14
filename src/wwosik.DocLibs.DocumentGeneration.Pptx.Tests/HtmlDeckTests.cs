using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.Svg;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Charts.Tests;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// The HTML deck (E07-S02): every sample deck on every template is drawn without leaving anything out (the DrawingML
/// contract), and the page is self-contained, escaped and guarded by its Content Security Policy.
/// </summary>
public partial class HtmlDeckTests
{
    /// <summary>What the samples do not cover: slide kinds, image attributes, QR codes, icons, charts, financial charts, callouts.</summary>
    private const string Features = """
        # Features

        ## Agenda {.agenda}

        - Where we are
        - What comes next

        ## {.quote}

        > The grid is ready for twice the solar power.
        > — Ann Example, Chief Engineer

        ## The grid is ready {.statement}

        Twice the solar power by 2027.

        ## Contact {.contact}

        - ![Ann](ann.png) **Ann Example** – Chief Engineer
          ann@example.com
        - **Ben Example**
          Project lead

        ## Pictures

        ![Round](photo.png){.round} ![Grey](photo.png){.grey} ![Cover](wide.png){fit=cover} ![Logo](logo.svg)

        ## Survey

        ![Survey](qr:https://example.com/survey "Scan me")

        ## Why

        ::: cards
        ### Fast

        :icon-bolt:

        It is quick.

        ### Safe

        :icon-shield-check:

        It is safe.
        :::

        ## Revenue {.chart}

        ```chart
        type: bar
        stacked: true
        title: Revenue
        unit: EUR m
        source: Annual report
        ---
        year, north, south
        2025, 3, 4
        2026, 4, 5
        ```

        Up by a third.

        ## Trend

        ```chart
        type: line
        ---
        month, orders
        Jan, 3
        Feb, -1
        Mar, 6
        ```

        ## Area

        ```chart
        type: area
        stacked: true
        ---
        month, a, b
        Jan, 3, 1
        Feb, 4, 2
        ```

        ## Development

        ```development
        ```

        ## Summary

        ```summary-table
        ```

        ## Shadow run

        - Same bookings, same results

        ::: comment
        Rounding is fixed in the next release.
        :::

        ## Tasks

        - [x] Done
        - [ ] Open
        """;

    private static readonly ICodeBlockRenderer Financial = Samples.Renderer(request => (request.Language switch
    {
        "development" => (FinancialChart)FinancialSamples.Development(),
        "summary-table" => FinancialSamples.Summary(),
        _ => null,
    }) is { } chart ? new DrawingGraphic(FinancialCharts.Draw(chart, FinancialSamples.Options with { Brand = request.Brand })) : null);

    public static TheoryData<string> Templates => ["neutral", "sample", "dark"];

    private static PptxOptions Options(string template) => (template == "dark" ? DarkTemplate.Options : new PptxOptions { Template = Samples.BuiltIn(template) }) with
    {
        CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }, new ChartRenderer(), Financial],
        ImageConverters = [new SvgImageConverter()],
    };

    private static IEnumerable<(string Name, MarkdownDocument Markdown)> SampleDecks()
    {
        var images = new[] { Samples.Image("diagram.png"), Samples.Image("photo.png") };
        foreach (var name in new[] { "deck.md", "layouts.md", "presenting.md", "schedules.md", "steps-and-risks.md", "diagrams.md" })
        {
            yield return (name, new MarkdownDocument(File.ReadAllText(Samples.Path(name)).ReplaceLineEndings("\n"), images, new Dictionary<string, string>()));
        }

        var svg = new DocumentResource("logo.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'><rect width='40' height='20' fill='#c00'/></svg>"u8.ToArray());
        yield return ("features", Samples.Markdown(Features, Samples.Image("photo.png"), TestImages.Png("ann.png", 120, 160), TestImages.Png("wide.png", 400, 100), svg));
    }

    private static (string Html, GenerationResult Result) Html(byte[] pptx, HtmlDeckOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = HtmlDeck.Write(new MemoryStream(pptx), output, options);
        return (Encoding.UTF8.GetString(output.ToArray()), result);
    }

    // The DrawingML contract: whatever the generator writes, the reader draws. A story that writes new DrawingML
    // extends the reader in the same change (ai/memory.md).
    [Theory]
    [MemberData(nameof(Templates))]
    public void EverySampleDeck_IsDrawn_WithNothingLeftOut(string template)
    {
        foreach (var (name, markdown) in SampleDecks())
        {
            var pptx = Samples.Generate(markdown, Options(template));
            var (html, result) = Html(pptx);
            Samples.WriteForReview($"html-{Path.GetFileNameWithoutExtension(name)}-{template}.html", Encoding.UTF8.GetBytes(html));
            Samples.WriteForReview($"html-{Path.GetFileNameWithoutExtension(name)}-{template}.pptx", pptx);

            Assert.True(result.Pages > 0, name);
            Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
            Assert.Equal(result.Pages, Regex.Matches(html, "<section class=\"slide\"").Count);
        }
    }

    [Fact]
    public void ThePage_IsSelfContained_AndGuardedByItsPolicy()
    {
        var (html, _) = Html(Samples.Generate(Samples.Deck()));

        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains($"script-src '{HtmlDeckWriter.ScriptHash}'", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("<script>" + HtmlDeckWriter.Script + "</script>", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<script"));
        Assert.DoesNotContain(Regex.Matches(html, "(src|href)=\"(?!data:|#)[^\"]*\""), m => !m.Value.StartsWith("href=\"https://", StringComparison.Ordinal));
        Assert.DoesNotMatch("<(link|iframe|object|embed|foreignObject)\\b", html);
        Assert.Contains("<title>Quarterly review</title>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Results\"", html, StringComparison.Ordinal);
        Assert.Contains("@page { size: 960pt 540pt; margin: 0; }", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SlideText_StaysText()
    {
        var pptx = Samples.Generate(Samples.Markdown("# Deck\n\n## A </script><img src=x onerror=alert(1)> & \"quotes\"\n\nText with <b>tags</b> and `</style>`.\n"));
        var (html, _) = Html(pptx, new HtmlDeckOptions { Title = "</title><script>alert(1)</script>" });

        Assert.Single(Regex.Matches(html, "<script"));
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</style>`", html, StringComparison.Ordinal);
        Assert.Contains("&lt;/script&gt;&lt;img src=x onerror=alert(1)&gt; &amp; &quot;quotes&quot;", html, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;/title&gt;&lt;script&gt;alert(1)&lt;/script&gt;</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_AreOnlyWebAndMail_AndOpenApart()
    {
        var pptx = Samples.Generate(Samples.Markdown("## Links\n\n[web](https://example.com/a?b=1&c=2), [mail](mailto:ann@example.com) and [script](javascript:alert(1)).\n"));
        var (html, _) = Html(pptx);

        Assert.Contains("<a href=\"https://example.com/a?b=1&amp;c=2\" target=\"_blank\" rel=\"noopener noreferrer\">", html, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:ann@example.com\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrandDeck_EmbedsEachFaceOnce_AndNeutralDecksNone()
    {
        var brand = Samples.Generate(Samples.Deck(), new PptxOptions { Template = Samples.BuiltIn("sample") });
        var (html, result) = Html(brand);
        var faces = Regex.Matches(html, "@font-face \\{ font-family: '([^']+)'; font-weight: (\\w+); font-style: (\\w+);").Select(m => m.Value).ToList();

        Assert.NotEmpty(faces);
        Assert.Equal(faces.Distinct().Count(), faces.Count);
        Assert.Contains(faces, f => f.Contains("Liberation Serif", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Notes, n => n.Message.Contains("Liberation Serif", StringComparison.Ordinal));

        var (neutral, _) = Html(Samples.Generate(Samples.Deck()));
        Assert.DoesNotContain("@font-face {", neutral, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Arial, &#39;Liberation Sans&#39;, sans-serif\"", neutral, StringComparison.Ordinal);

        var (without, _) = Html(brand, new HtmlDeckOptions { EmbedFonts = false });
        Assert.DoesNotContain("@font-face {", without, StringComparison.Ordinal);
    }

    [Fact]
    public void Pictures_AreWrittenOnce_AsSymbols()
    {
        var pptx = Samples.Generate(Samples.Markdown("## One\n\n![a](photo.png)\n\n## Two\n\n![b](photo.png)\n", Samples.Image("photo.png")));
        var (html, _) = Html(pptx);

        Assert.Single(Regex.Matches(html, "<symbol "));
        Assert.Equal(2, Regex.Matches(html, "<use href=\"#img1\"").Count);
    }

    [Fact]
    public void HiddenSlides_AreSkipped_AndNumbersStayTheDecks()
    {
        var pptx = Samples.Generate(Samples.Markdown("## One\n\nA\n\n## Two\n\nB\n\n## Three\n\nC\n"));
        pptx = Edit(pptx, (slides, _) => slides[1].Root!.SetAttributeValue("show", "0"));
        var (html, result) = Html(pptx);

        Assert.Equal(2, result.Pages);
        Assert.Contains("data-number=\"3\" aria-label=\"Three\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Two\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NotAPresentation_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => HtmlDeck.Write(new MemoryStream("not a zip"u8.ToArray()), new MemoryStream()));
        Assert.Equal(Format.Pptx, error.Format);

        using var docx = new MemoryStream();
        using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(docx, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            document.AddMainDocumentPart().Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new DocumentFormat.OpenXml.Wordprocessing.Body());
        }

        Assert.Equal(Format.Pptx, Assert.Throws<DocumentConversionException>(() => HtmlDeck.Write(new MemoryStream(docx.ToArray()), new MemoryStream())).Format);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheSlideSvgs_PaintWithSvgSkia(string template)
    {
        var pptx = Samples.Generate(Samples.Deck(), Options(template));
        var (deck, _) = DeckReader.Read(new MemoryStream(pptx));
        var fonts = deck.Faces.Select(f => (ReadOnlyMemory<byte>)f.File).ToList();
        foreach (var slide in deck.Slides)
        {
            var svg = SceneSvg.Write(deck, slide, SvgTarget.Painting);
            Assert.DoesNotContain("<use", svg, StringComparison.Ordinal);
            var png = SvgConverter.ToPng(Encoding.UTF8.GetBytes(svg), new SvgPngOptions { Width = 640, Fonts = fonts });
            using var bitmap = SkiaSharp.SKBitmap.Decode(png);
            Assert.Equal((640, 360), (bitmap.Width, bitmap.Height));
            Assert.True(bitmap.Pixels.Distinct().Count() > 2, $"slide {slide.Number} is blank");
            if (slide.Number <= 3)
            {
                Samples.WriteForReview($"html-painted-{template}-{slide.Number}.png", png);
            }
        }
    }

    /// <summary>Edits a deck's slides as XML: the slides in order and the master, written back.</summary>
    internal static byte[] Edit(byte[] pptx, Action<List<XDocument>, XDocument> edit, Action<DocumentFormat.OpenXml.Packaging.PresentationDocument>? parts = null)
    {
        using var buffer = new MemoryStream();
        buffer.Write(pptx);
        using (var package = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(buffer, true))
        {
            parts?.Invoke(package);
            var slideParts = Samples.Slides(package);
            var slides = slideParts.Select(Load).ToList();
            var masterPart = package.PresentationPart!.SlideMasterParts.Single();
            var master = Load(masterPart);
            edit(slides, master);
            for (var i = 0; i < slides.Count; i++)
            {
                Feed(slideParts[i], slides[i]);
            }

            Feed(masterPart, master);
        }

        return buffer.ToArray();
    }

    private static XDocument Load(DocumentFormat.OpenXml.Packaging.OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(stream);
    }

    private static void Feed(DocumentFormat.OpenXml.Packaging.OpenXmlPart part, XDocument xml)
    {
        using var data = new MemoryStream();
        xml.Save(data);
        data.Position = 0;
        part.FeedData(data);
    }
}
