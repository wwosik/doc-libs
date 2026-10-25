using System.Globalization;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Brand templates whose slides are dark, and slide colours that differ from PDF colours.</summary>
public class DarkTemplateTests
{
    private const string Everything = """
        ---
        labels.success: Done
        labels.danger: Late
        ---

        ## Everything {kicker="Check"}

        > [!NOTE]
        > A note.

        > [!WARNING]
        > A warning.

        `Orders` `Sales`

        Some ==marked== words.

        ::: panel
        A light panel.
        :::

        ::: panel dark
        A dark panel.
        :::

        ::: cards
        ### First
        **+12%**

        text

        ### Second
        more
        :::

        | Item | Status |
        | --- | --- |
        | ==Total== | `Done` |
        | B | `Late` |

        ```
        code = 1
        ```

        ::: statement
        A statement.
        :::
        """;

    // Dark backgrounds a veil can lie on: black, and a dark slate as a stand-in for a dark photo.
    private static readonly string[] Backdrops = ["000000", "2F3B45"];

    [Fact]
    public void Tone_IsReadFromTheTemplate()
    {
        using var dark = Samples.Open(DarkTemplate.Content);
        using var light = Samples.Open(Samples.TemplateBytes(Samples.BuiltIn("sample")));

        Assert.Equal(SlideTone.Dark, TemplateTone.Detect(dark.PresentationPart!));
        Assert.Equal(SlideTone.Light, TemplateTone.Detect(light.PresentationPart!));
    }

    [Fact]
    public void EverythingDrawn_IsWhiteOrHighlighted_OnTranslucentFills()
    {
        var pptx = Samples.Generate(Samples.Markdown(Everything), DarkTemplate.Options);
        Samples.WriteForReview("dark-everything.pptx", pptx);
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package).Select(s => s.Slide!).ToList();
        Assert.Equal(2, slides.Count); // too much for one slide: continued on a second with the same title

        var drawn = slides.SelectMany(s => s.Descendants<P.Shape>()).Where(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape is null).ToList();
        var textColours = drawn.SelectMany(s => s.Descendants<A.RunProperties>()).Select(Colour).Distinct().ToList();
        Assert.All(textColours, c => Assert.Contains(c, new[] { "FFFFFF", "F5C04A" }));
        Assert.DoesNotContain(slides.SelectMany(s => s.Descendants<A.Highlight>()), _ => true);
        var boxes = drawn.Where(s => s.ShapeProperties!.GetFirstChild<A.SolidFill>() is not null).Select(s => s.ShapeProperties!.GetFirstChild<A.SolidFill>()!).ToList();
        Assert.All(boxes.Where(f => f.RgbColorModelHex!.Val!.Value is "FFFFFF" or "000000"), f => Assert.NotNull(f.RgbColorModelHex!.GetFirstChild<A.Alpha>()));
        var cells = slides.SelectMany(s => s.Descendants<A.TableCell>()).ToList();
        Assert.All(cells.Select(c => c.TableCellProperties!.GetFirstChild<A.SolidFill>()).OfType<A.SolidFill>(), f => Assert.NotNull(f.Descendants<A.Alpha>().FirstOrDefault()));
        Assert.All(cells, c => Assert.True(c.TableCellProperties!.GetFirstChild<A.SolidFill>() is not null || c.TableCellProperties.GetFirstChild<A.NoFill>() is not null, "Every cell has a fill of its own."));
        Assert.All(cells.SelectMany(c => c.Descendants<A.RunProperties>()), r => Assert.Contains(Colour(r), new[] { "FFFFFF", "F5C04A" })); // marked: the highlight
        var kicker = drawn.First(s => s.InnerText == "Check").Descendants<A.RunProperties>().Single();
        Assert.Equal("F5C04A", Colour(kicker));
        Assert.All(slides, s => Assert.Null(s.CommonSlideData!.Background));
    }

    [Fact]
    public void ToneLight_OverridesTheTemplate()
    {
        var brand = DarkTemplate.Brand with { Pptx = DarkTemplate.Brand.Pptx! with { Tone = SlideTone.Light } };
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Everything), new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } }));

        Assert.NotEmpty(Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Highlight>()));
    }

    [Fact]
    public void DarkPalette_KeepsEveryTextReadable_OnAnyDarkBackground()
    {
        var palette = SlidePalette.ForDark(ResolvedBrand.ForSlides(DarkTemplate.Brand));
        var pairs = new List<(string Name, SlideColor Text, SlideColor Fill)>
        {
            ("box", palette.Text!.Value, palette.Surface),
            ("dark box", palette.Text!.Value, palette.DarkSurface),
            ("dark panel", palette.Text!.Value, palette.Dark),
            ("highlighted row", palette.Text!.Value, palette.HighlightSurface),
            ("table header", palette.DarkTable!.Value.Text, palette.DarkTable!.Value.Header),
            ("table band", palette.DarkTable!.Value.Text, palette.DarkTable!.Value.Band),
            ("marked text", palette.MarkText!.Value, new SlideColor("000000", Alpha: 0)),
            ("kicker", palette.Accent, new SlideColor("000000", Alpha: 0)),
        };
        pairs.AddRange(Enum.GetValues<LabelCategory>().Select(c => ($"label {c}", palette.Text!.Value, palette.LabelFill(c))));
        pairs.AddRange(Enum.GetValues<LabelCategory>().Select(c => ($"coloured words {c}", palette.WordColour(c), new SlideColor("000000", Alpha: 0))));

        foreach (var (name, text, fill) in pairs)
        {
            foreach (var backdrop in Backdrops)
            {
                var ratio = Contrast(text.Hex!, Over(fill, backdrop));
                Assert.True(ratio >= 4.5, $"{name} on {backdrop}: {ratio:F2}");
            }
        }
    }

    [Fact]
    public void SlideColors_AreUsedInDecks_ColorsInPdfs()
    {
        var brand = Samples.Brand with { SlideColors = new BrandColors { Primary = "AA3377" } };

        Assert.Equal("AA3377", ResolvedBrand.ForSlides(brand).Primary);
        Assert.Equal("0E7C86", ResolvedBrand.Resolve(brand).Primary);
        Assert.Equal(ResolvedBrand.Resolve(brand).Highlight, ResolvedBrand.ForSlides(brand).Highlight); // unset: from Colors
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("# Cover\n\n## A\n\nText\n"), new PptxOptions { Template = new BrandTemplateOptions { Brand = brand } }));
        var theme = package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!.ThemeElements!.ColorScheme!;
        Assert.Equal("AA3377", theme.Accent1Color!.RgbColorModelHex!.Val!.Value);
    }

    [Fact]
    public void Diagrams_OnDarkSlides_HaveWhiteTextOnDarkNodes()
    {
        var options = DarkTemplate.Options with { CodeBlockRenderers = [new MermaidRenderer()] };
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## D\n\n```mermaid\nflowchart LR\n  A[One] --> B[Two]\n```\n"), options));

        var group = Samples.Slides(package).Single().Slide!.Descendants<P.GroupShape>().Single();
        Assert.All(group.Descendants<A.RunProperties>(), r => Assert.Equal("FFFFFF", Colour(r)));
        var node = ResolvedBrand.Shade(ResolvedBrand.ForSlides(DarkTemplate.Brand).Primary, 0.45);
        Assert.Contains(group.Descendants<A.SolidFill>(), f => f.RgbColorModelHex?.Val?.Value == node);
        Assert.True(Contrast("FFFFFF", node) >= 4.5, node);
    }

    [Fact]
    public void Charts_OnDarkSlides_HaveWhiteText()
    {
        var chart = new ChartSpec(ChartKind.Bar, ["A", "B"], [new ChartSeries("Plan", [1, 2])]) { Title = "T", Colors = ["F5C04A"] };
        var options = DarkTemplate.Options with { CodeBlockRenderers = [Samples.Renderer(_ => new ChartGraphic(chart, new Drawing(10, 10, [])))] };
        var pptx = Samples.Generate(Samples.Markdown("## C\n\n```chart\nx\n```\n"), options);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var space = Samples.Slides(package).Single().ChartParts.Single().ChartSpace!;
        var colours = space.Descendants<A.DefaultRunProperties>().Select(r => r.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value).Distinct().ToList();
        Assert.Equal(["FFFFFF"], colours);
    }

    [Fact]
    public void LayoutsSample_RoundTrips_OnTheDarkTemplate()
    {
        var markdown = Samples.Markdown(File.ReadAllText(Samples.Path("layouts.md")).ReplaceLineEndings("\n"));
        var pptx = Samples.Generate(markdown, DarkTemplate.Options);
        Samples.WriteForReview("layouts-brand-dark.pptx", pptx);

        Samples.AssertValid(pptx);
        Samples.AssertMatchesExpected("layouts-brand-dark.roundtrip", new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default).Markdown);
    }

    private static string Colour(A.RunProperties properties) =>
        properties.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value ?? "inherited";

    // The fill laid over the backdrop, as one opaque colour.
    private static string Over(SlideColor fill, string backdrop)
    {
        var alpha = fill.Alpha / 100000.0;
        int Channel(string hex, int shift) => (int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) >> shift) & 0xFF;
        int Mixed(int shift) => (int)Math.Round((Channel(fill.Hex!, shift) * alpha) + (Channel(backdrop, shift) * (1 - alpha)));
        return string.Create(CultureInfo.InvariantCulture, $"{Mixed(16):X2}{Mixed(8):X2}{Mixed(0):X2}");
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (TemplateTone.Luminance(a), TemplateTone.Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
