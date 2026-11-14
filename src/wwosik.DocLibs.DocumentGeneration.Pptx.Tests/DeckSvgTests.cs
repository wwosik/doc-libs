using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.Svg;
using wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>The slide SVGs for previews (E07-S03): one per visible slide, final build state, raster pictures, the faces they name.</summary>
public partial class DeckSvgTests
{
    private const string Namespaces = "xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main'";

    private static string Svg(SlideSvg slide) => Encoding.UTF8.GetString(slide.Svg.Span);

    [GeneratedRegex("font-family=\"([^\"]+)\"")]
    private static partial Regex FontFamily();

    [Fact]
    public void EveryVisibleSlide_IsOneSvg_InPoints()
    {
        var pptx = Samples.Generate(Samples.Markdown("## One\n\nA\n\n## Two\n\nB\n\n## Three\n\nC\n"));
        pptx = HtmlDeckTests.Edit(pptx, (slides, _) => slides[1].Root!.SetAttributeValue("show", "0"));
        var result = DeckSvg.Write(new MemoryStream(pptx));

        Assert.Equal([(1, "One"), (3, "Three")], result.Slides.Select(s => (s.Number, s.Title)));
        Assert.Equal(2, result.Pages);
        Assert.All(result.Slides, s => Assert.Equal((960, 540), (s.Width, s.Height)));
        var svg = Svg(result.Slides[0]);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 960 540\" width=\"960pt\" height=\"540pt\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<use", svg, StringComparison.Ordinal);
        XDocument.Parse(svg);
    }

    [Fact]
    public void Slides_PicksTheSlidesToWrite()
    {
        var pptx = Samples.Generate(Samples.Markdown("## One\n\nA\n\n## Two\n\nB\n\n## Three\n\nC\n"));
        var result = DeckSvg.Write(new MemoryStream(pptx), new DeckSvgOptions { Slides = [3, 1] });

        Assert.Equal([1, 3], result.Slides.Select(s => s.Number));
    }

    [Fact]
    public void BuildSlides_ShowEveryStep()
    {
        var markdown = Samples.Markdown(File.ReadAllText(Samples.Path("presenting.md")).ReplaceLineEndings("\n"));
        var result = DeckSvg.Write(new MemoryStream(Samples.Generate(markdown)));

        var building = Svg(result.Slides.Single(s => s.Title == "Building the plan"));
        foreach (var text in new[] { "Collect the schedules", "one per route", "Let the planner read them", "The new planner goes live in autumn.", "This line comes in on one click" })
        {
            Assert.Contains(text, building, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pictures_AreRaster_NeverTheSvgAlternative()
    {
        var logo = new DocumentResource("logo.svg", "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'><rect width='40' height='20' fill='#c00'/></svg>"u8.ToArray());
        var pptx = Samples.Generate(Samples.Markdown("## Logo\n\n![Logo](logo.svg)\n", logo), new PptxOptions { ImageConverters = [new SvgImageConverter()] });
        var svg = Svg(Assert.Single(DeckSvg.Write(new MemoryStream(pptx)).Slides));

        Assert.Contains("href=\"data:image/png;base64,", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("image/svg", svg, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void EveryFamily_NamesAFace_InFonts_EachOnce(string template)
    {
        var result = DeckSvg.Write(new MemoryStream(Samples.Generate(Samples.Deck(), new PptxOptions { Template = Samples.BuiltIn(template) })));

        var families = result.Fonts.Select(f => TrueTypeInfo.FamilyName(f.ToArray())).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = result.Slides.SelectMany(s => FontFamily().Matches(Svg(s)).Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.NotEmpty(used);
        Assert.All(used, family => Assert.Contains(family, families));
        Assert.Equal(result.Fonts.Count, result.Fonts.Select(f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f.Span))).Distinct().Count());
        Assert.Equal("Liberation Sans", TrueTypeInfo.FamilyName(result.LabelFont.ToArray()));
        if (template == "sample")
        {
            Assert.Contains("Liberation Serif", used);
        }
        else
        {
            Assert.Equal(["Liberation Mono", "Liberation Sans"], used.Order());
        }
    }

    [Fact]
    public void AMissingFamily_IsPaintedInLiberationSans_AndReported()
    {
        var pptx = Samples.Generate(Samples.Markdown("## T\n\nx\n"));
        pptx = HtmlDeckTests.Edit(pptx, (slides, _) => slides[0].Descendants(Ns.P + "spTree").Single().Add(XElement.Parse(
            $"<p:sp {Namespaces}><p:nvSpPr><p:cNvPr id='51' name='Box'/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='3000000' cy='600000'/></a:xfrm><a:prstGeom prst='rect'/></p:spPr>"
            + "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr b='1'><a:latin typeface='Made Up Sans'/></a:rPr><a:t>Hello</a:t></a:r></a:p></p:txBody></p:sp>")));
        var result = DeckSvg.Write(new MemoryStream(pptx));

        Assert.Contains("font-family=\"Liberation Sans\" font-size=\"18\" font-weight=\"bold\"", Svg(result.Slides[0]), StringComparison.Ordinal);
        Assert.Contains(result.Fonts, f => f.Span.SequenceEqual(StandInFonts.SansBold));
        Assert.Contains(result.Notes, n => n.Code == NoteCodes.FontMissing && n.Message.Contains("Made Up Sans", StringComparison.Ordinal));
        Assert.True(result.HasNotes);
    }

    [Fact]
    public void NotAPresentation_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => DeckSvg.Write(new MemoryStream("not a zip"u8.ToArray())));
        Assert.Equal(Format.Pptx, error.Format);
    }
}
