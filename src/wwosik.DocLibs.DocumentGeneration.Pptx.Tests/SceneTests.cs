using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><c>::: scene</c>: shapes on a 160 × 90 grid over the slide, glued connectors, steps as builds, names for Morph.</summary>
public class SceneTests
{
    private const long Unit = 76200;
    private static readonly XNamespace PresentationML = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static MarkdownDocument Sample() => Samples.Markdown(File.ReadAllText(Samples.Path("scenes.md")).ReplaceLineEndings("\n"));

    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, options ?? new PptxOptions());
        var pptx = output.ToArray();
        Samples.AssertValid(pptx);
        return (result, pptx);
    }

    private static string Scene(string lines) => $"## Scene\n\n::: scene\n{lines}\n:::\n";

    private static List<OpenXmlElementInfo> Items(SlidePart slide) =>
        [.. slide.Slide!.CommonSlideData!.ShapeTree!.ChildElements
            .Select(e => (Element: e, Properties: e.Descendants<P.NonVisualDrawingProperties>().FirstOrDefault()))
            .Where(i => i.Properties?.Name?.Value?.StartsWith("!!", StringComparison.Ordinal) == true)
            .Select(i => new OpenXmlElementInfo(i.Properties!.Name!.Value![2..], i.Properties.Id!.Value, i.Element))];

    private static (long X, long Y, long W, long H) Box(OpenXmlElementInfo item)
    {
        var transform = item.Element.Descendants<A.Transform2D>().First();
        return (transform.Offset!.X!.Value, transform.Offset.Y!.Value, transform.Extents!.Cx!.Value, transform.Extents.Cy!.Value);
    }

    // The clicks of the main sequence: per click its effects as "class preset name", joined by " + ".
    private static List<string> Clicks(SlidePart slide, List<OpenXmlElementInfo> items)
    {
        if (slide.Slide!.GetFirstChild<P.Timing>() is not { } timing)
        {
            return [];
        }

        var names = items.ToDictionary(i => i.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), i => i.Name);
        var main = XElement.Parse(timing.OuterXml).Descendants(PresentationML + "cTn").Single(c => (string?)c.Attribute("nodeType") == "mainSeq");
        return [.. main.Element(PresentationML + "childTnLst")!.Elements(PresentationML + "par").Select(click => string.Join(" + ", click
            .Descendants(PresentationML + "cTn").Where(c => c.Attribute("presetID") is not null)
            .Select(c => $"{c.Attribute("nodeType")!.Value} {c.Attribute("presetClass")!.Value}{c.Attribute("presetID")!.Value} {names[c.Descendants(PresentationML + "spTgt").First().Attribute("spid")!.Value]}")))];
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsValid_WithoutNotes_AndRoundTripsThroughThePptxConverter(string template)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Sample(), output, new PptxOptions { Template = Samples.BuiltIn(template) });
        var pptx = output.ToArray();
        Samples.AssertValid(pptx);
        Samples.WriteForReview($"scenes-{template}.pptx", pptx);
        Assert.False(result.HasNotes, result.ToString());

        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);

        Samples.AssertMatchesExpected($"scenes-{template}.roundtrip", back.Markdown);
    }

    [Fact]
    public void Shapes_SitAtTheirGridPlaces_NamedForMorph_OnTheTitleOnlyLayout()
    {
        var (result, pptx) = Generate("## How it works\n\n::: scene\nbox api 65 34 30 22 \"API\" .accent\nround r 0 70 160 20\nellipse e 10.5 22 20 20\ntext t 2 80 40 6 \"Note\"\n:::\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0];
        Assert.Equal("Title Only", Samples.LayoutName(slide));
        Assert.Equal("How it works", Samples.PlaceholderText(slide, P.PlaceholderValues.Title));
        var items = Items(slide);
        Assert.Equal(["api", "r", "e", "t"], items.Select(i => i.Name));
        Assert.Equal((65 * Unit, 34 * Unit, 30 * Unit, 22 * Unit), Box(items[0]));
        Assert.Equal((0L, 70 * Unit, 160 * Unit, 20 * Unit), Box(items[1]));
        Assert.Equal((800100L, 22 * Unit, 20 * Unit, 20 * Unit), Box(items[2]));
        Assert.Equal(
            [A.ShapeTypeValues.Rectangle, A.ShapeTypeValues.RoundRectangle, A.ShapeTypeValues.Ellipse, A.ShapeTypeValues.Rectangle],
            items.Select(i => i.Element.Descendants<A.PresetGeometry>().Single().Preset!.Value));
        Assert.Equal("API", items[0].Element.Descendants<A.Paragraph>().Single().InnerText);
        Assert.True(items[3].Element.Descendants<P.NonVisualShapeDrawingProperties>().Single().TextBox?.Value);
        Assert.IsType<A.NoFill>(items[3].Element.Descendants<P.ShapeProperties>().Single().ChildElements[2]);
    }

    [Fact]
    public void OnA4To3Template_TheGridStretchesPerAxis()
    {
        var template = Samples.TemplateBytes(Samples.BuiltIn("neutral"));
        using var edited = new MemoryStream();
        edited.Write(template);
        using (var package = PresentationDocument.Open(edited, true))
        {
            package.PresentationPart!.Presentation!.SlideSize = new P.SlideSize { Cx = 9144000, Cy = 6858000 };
        }

        var (_, pptx) = Generate(Scene("box a 16 9 32 18"), new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(edited.ToArray())) });

        using var result = Samples.Open(pptx);
        Assert.Equal((914400L, 685800L, 1828800L, 1371600L), Box(Items(Samples.Slides(result)[0]).Single()));
    }

    [Fact]
    public void Connectors_AreGluedToTheFacingSides_AndFollowTheShapes()
    {
        var (_, pptx) = Generate(Scene(
            "box a 10 30 20 10\nbox b 60 30 20 10\nellipse c 60 60 20 10\nbox d 10 60 20 10\n"
            + "arrow a b \"calls\"\narrow b c\nline c d .accent\narrow d a name=back"));

        using var package = Samples.Open(pptx);
        var items = Items(Samples.Slides(package)[0]);
        var ids = items.ToDictionary(i => i.Name, i => i.Id);
        string Glue(string name)
        {
            var connector = items.Single(i => i.Name == name).Element;
            var (start, end) = (connector.Descendants<A.StartConnection>().Single(), connector.Descendants<A.EndConnection>().Single());
            return $"{items.Single(i => i.Id == start.Id!.Value).Name}:{start.Index!.Value} {items.Single(i => i.Id == end.Id!.Value).Name}:{end.Index!.Value}";
        }

        Assert.Equal(["a", "b", "c", "d", "a-b", "a-b-label", "b-c", "c-d", "back"], items.Select(i => i.Name));
        Assert.Equal("a:3 b:1", Glue("a-b"));
        Assert.Equal("b:2 c:0", Glue("b-c"));
        Assert.Equal("c:2 d:3", Glue("c-d"));
        Assert.Equal("d:0 a:2", Glue("back"));
        Assert.All(items.Where(i => i.Name is "a-b" or "b-c" or "c-d" or "back"), i => Assert.IsType<P.ConnectionShape>(i.Element));
        Assert.Equal(A.ShapeTypeValues.StraightConnector1, items.Single(i => i.Name == "a-b").Element.Descendants<A.PresetGeometry>().Single().Preset!.Value);
        Assert.NotNull(items.Single(i => i.Name == "a-b").Element.Descendants<A.TailEnd>().SingleOrDefault());
        Assert.Empty(items.Single(i => i.Name == "c-d").Element.Descendants<A.TailEnd>());

        // From the right side of a to the left of b; from c leftwards to d, flipped; from d up to a, flipped.
        Assert.Equal((30 * Unit, 35 * Unit, 30 * Unit, 0L), Box(items.Single(i => i.Name == "a-b")));
        var leftwards = items.Single(i => i.Name == "c-d").Element.Descendants<A.Transform2D>().Single();
        Assert.True(leftwards.HorizontalFlip?.Value);
        Assert.True(items.Single(i => i.Name == "back").Element.Descendants<A.Transform2D>().Single().VerticalFlip?.Value);
        Assert.Equal("calls", items.Single(i => i.Name == "a-b-label").Element.InnerText);
        Assert.True(Box(items.Single(i => i.Name == "a-b-label")).Y + Box(items.Single(i => i.Name == "a-b-label")).H <= 35 * Unit);
    }

    [Fact]
    public void StepsAndExits_AreClicksInOrderOfTheirNumbers_WithGapsClosed_AndEqualNumbersTogether()
    {
        var (_, pptx) = Generate(Scene(
            "box a 10 30 20 10\nbox b 60 30 20 10 step=5 effect=fly\nbox c 110 30 20 10 step=2 exit=9 effect=wipe\n"
            + "text t 10 60 60 6 \"Gone\" exit=5\narrow a b"));

        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0];
        var items = Items(slide);
        Assert.Equal(
            [
                "clickEffect entr22 c",
                "clickEffect entr2 b + withEffect exit1 t + withEffect entr1 a-b",
                "clickEffect exit1 c",
            ],
            Clicks(slide, items));
    }

    [Fact]
    public void AConnector_ComesAndGoesWithItsShapes_UnlessItSaysOtherwise()
    {
        var (_, pptx) = Generate(Scene("box a 10 30 20 10 step=1\nbox b 60 30 20 10 step=2 exit=3\narrow a b\nline b a name=early step=1"));

        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0];
        Assert.Equal(
            [
                "clickEffect entr1 a + withEffect entr1 early",
                "clickEffect entr1 b + withEffect entr1 a-b",
                "clickEffect exit1 b + withEffect exit1 a-b + withEffect exit1 early",
            ],
            Clicks(slide, Items(slide)));
    }

    [Fact]
    public void ALabel_IsShrunkToFit_DownTo70Percent_ThenReported()
    {
        var (result, pptx) = Generate(Scene("box fits 10 30 20 10 \"A label that is long\"\nbox small 10 50 8 4 \"A label far too long for this box\"\nbox ok 60 30 30 10 \"Short\" .large"));

        using var package = Samples.Open(pptx);
        var sizes = Items(Samples.Slides(package)[0]).Select(i => i.Element.Descendants<A.RunProperties>().First().FontSize!.Value).ToList();
        Assert.InRange(sizes[0], 1150, 1550);
        Assert.Equal(1100, sizes[1]);
        Assert.Equal(2400, sizes[2]);
        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.SlideOverfull, note.Code);
        Assert.Equal(5, note.Line);
        Assert.Contains("'small'", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelsKeepTheirCharacters_AndBreakAtBackslashN()
    {
        var (_, pptx) = Generate(Scene("box a 10 30 40 20 \"*not* [bold] \\\"quoted\\\"\\nsecond line\" .bold"));

        using var package = Samples.Open(pptx);
        var shape = Items(Samples.Slides(package)[0]).Single().Element;
        Assert.Equal(["*not* [bold] \"quoted\"", "second line"], shape.Descendants<A.Run>().Select(r => r.InnerText));
        Assert.Single(shape.Descendants<A.Break>());
        Assert.All(shape.Descendants<A.Run>(), r => Assert.True(r.RunProperties!.Bold?.Value));
    }

    [Fact]
    public void Roles_FillTheShape_WithTextByContrast()
    {
        var (_, pptx) = Generate(Scene("box d 0 30 20 10 \"x\"\nbox p 20 30 20 10 \"x\" .primary\nbox o 40 30 20 10 \"x\" .outline\nbox s 60 30 20 10 \"x\" .success"));

        using var package = Samples.Open(pptx);
        var items = Items(Samples.Slides(package)[0]);
        string? Fill(OpenXmlElementInfo item) => item.Element.Descendants<P.ShapeProperties>().Single().GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value;
        string? Ink(OpenXmlElementInfo item) => item.Element.Descendants<A.RunProperties>().Single().GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value;
        var brand = ResolvedBrand.Neutral;
        Assert.Equal(brand.TableBand, Fill(items[0]));
        Assert.Equal(brand.Primary, items[0].Element.Descendants<A.Outline>().Single().GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
        Assert.Equal(brand.Primary, Fill(items[1]));
        Assert.Equal("FFFFFF", Ink(items[1]));
        Assert.Null(Fill(items[2]));
        Assert.Equal(brand.Labels.Success, Fill(items[3]));
    }

    [Fact]
    public void MorphingSlides_ShareTheShapesNames_AndTheSecondMorphs()
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(Sample(), output, new PptxOptions());

        using var package = Samples.Open(output.ToArray());
        var slides = Samples.Slides(package);
        var (first, second) = (Items(slides[1]).Select(i => i.Name).ToList(), Items(slides[2]).Select(i => i.Name).ToList());
        Assert.Contains("api", first);
        Assert.Contains("api", second);
        Assert.Contains("client-api", first);
        Assert.Contains("morph", slides[2].Slide!.OuterXml, StringComparison.Ordinal);
        Assert.DoesNotContain("morph", slides[1].Slide!.OuterXml, StringComparison.Ordinal);
        Assert.NotNull(slides[1].NotesSlidePart);
    }

    [Theory]
    [InlineData("bx a 1 1 2 2", "'bx a 1 1 2 2' at line 4 starts with 'bx'")]
    [InlineData("box a 1 1 2", "at line 4 needs a name and its place on the grid")]
    [InlineData("box a 1 1 2 2\nbox a 5 5 2 2", "at line 5 repeats the name 'a'")]
    [InlineData("box a 150 1 20 2", "at line 4 runs outside the grid of 160 × 90 units")]
    [InlineData("box a 1 1 2 x", "at line 4 has 'x' as its h")]
    [InlineData("box a 1 1 2 2 \"open", "at line 4 has a label without its closing quote")]
    [InlineData("box a 1 1 2 2 .wide", "at line 4 has '.wide', which a shape does not take")]
    [InlineData("box a 1 1 2 2 .primary .accent", "at line 4 has two colours")]
    [InlineData("box a 1 1 2 2 step=0", "a step is a whole number from 1 to 99")]
    [InlineData("box a 1 1 2 2 step=2 exit=2", "has exit=2, which is not after its step=2")]
    [InlineData("box a 1 1 2 2 effect=fly", "has effect= without step=")]
    [InlineData("box 1a 1 1 2 2", "has the name '1a'")]
    [InlineData("box a 1 1 2 2\narrow a b", "The connector 'a-b' at line 5 joins 'b', which is no shape")]
    [InlineData("box a 1 1 2 2\nbox b 5 5 2 2\narrow a b\narrow a b", "at line 7 repeats the name 'a-b'")]
    [InlineData("box a 1 1 2 2 exit=1\nbox b 5 5 2 2\narrow a b step=2", "would leave on step 1, before it comes in on step 2")]
    [InlineData("text a 1 1 2 2", "is a text without its text")]
    [InlineData("% only a comment", "The '::: scene' at line 3 has no shapes")]
    public void Mistakes_ThrowNamingTheLine(string lines, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(Scene(lines)));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("## S\n\nText.\n\n::: scene\nbox a 1 1 2 2\n:::\n", "has more than the scene (line 3)")]
    [InlineData("## S\n\n{.build}\n\n::: scene\nbox a 1 1 2 2\n:::\n", "has a build marker")]
    [InlineData("## S\n\n::: scene\nbox a 1 1 2 2\n:::\n\n::: scene\nbox b 1 1 2 2\n:::\n", "has 2 scenes (lines 3, 7)")]
    [InlineData("## S\n\n:::: panel\n::: scene\nbox a 1 1 2 2\n:::\n::::\n", "is inside another block")]
    [InlineData("# S\n\n::: scene\nbox a 1 1 2 2\n:::\n", "is under a '#' heading")]
    [InlineData("::: scene\nbox a 1 1 2 2\n:::\n", "is before the first heading")]
    [InlineData("## S {.dark}\n\n::: scene\nbox a 1 1 2 2\n:::\n", "takes the heading attributes layout=")]
    [InlineData("## S\n\n::: scene wide\nbox a 1 1 2 2\n:::\n", "takes no arguments")]
    public void AScene_IsASlideOfItsOwn(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASceneSlide_KeepsItsNotesFooterAndTransition_AndIsNeverShrunkOrSplit()
    {
        var (result, pptx) = Generate("---\ntransition: fade\n---\n\n## S\n\n::: scene\n" + string.Concat(Enumerable.Range(0, 40).Select(i => $"box b{i} {i * 4} {20 + (i % 10)} 4 4\n")) + ":::\n\n::: notes\nSay it.\n:::\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        var slide = Assert.Single(Samples.Slides(package));
        Assert.Equal(40, Items(slide).Count);
        Assert.Contains("Say it.", slide.NotesSlidePart!.NotesSlide!.InnerText, StringComparison.Ordinal);
        Assert.NotNull(slide.Slide!.Transition);
    }

    [Fact]
    public void Check_CountsScenesAsDiagramsAndSlidesOfTheirKind()
    {
        var check = PptxGenerator.Check(Sample());

        Assert.Null(check.Error);
        Assert.Equal(3, check.Statistics.Diagrams);
        Assert.Equal(3, check.Statistics.SlidesByKind["scene"]);
    }

    [Fact]
    public void TheHtmlDeck_NamesTheShapesForMorph_ThePreviewsDoNot()
    {
        var pptx = Samples.Generate(Sample());
        using var output = new MemoryStream();
        HtmlDeck.Write(new MemoryStream(pptx), output);
        var html = System.Text.Encoding.UTF8.GetString(output.ToArray());
        var svg = string.Concat(DeckSvg.Write(new MemoryStream(pptx)).Slides.Select(s => System.Text.Encoding.UTF8.GetString(s.Svg.Span)));

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "data-name=\"!!api\"").Count);
        Assert.Contains("data-name=\"!!client-api\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-name", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", html, StringComparison.Ordinal);
        Assert.Contains("case 'morph':", HtmlDeckWriter.Script, StringComparison.Ordinal);
    }

    private sealed record OpenXmlElementInfo(string Name, uint Id, DocumentFormat.OpenXml.OpenXmlElement Element);
}
