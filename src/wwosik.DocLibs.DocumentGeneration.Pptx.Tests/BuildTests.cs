using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Builds: build markers and <c>build: lists</c>, written as PowerPoint animations.</summary>
public class BuildTests
{
    private static readonly XNamespace PresentationML = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static byte[] Generate(string markdown, PptxOptions? options = null) => Samples.Generate(Samples.Markdown(markdown), options);

    private static MarkdownDocument Sample(string name) => Samples.Markdown(File.ReadAllText(Samples.Path(name)).ReplaceLineEndings("\n"));

    private static string TitleOf(SlidePart slide) => Samples.PlaceholderText(slide, P.PlaceholderValues.Title)
        ?? Samples.PlaceholderText(slide, P.PlaceholderValues.CenteredTitle) ?? string.Empty;

    // The clicks of the slide's main sequence, each as its effects joined by " + ": the preset class and id, then the
    // targeted paragraphs' text joined by "|", or the whole shape's kind and text in brackets.
    private static List<string> Clicks(SlidePart slide)
    {
        if (slide.Slide!.GetFirstChild<P.Timing>() is not { } timing)
        {
            return [];
        }

        var shapes = slide.Slide.CommonSlideData!.ShapeTree!.ChildElements
            .Select(e => (Element: e, Properties: e.Descendants<P.NonVisualDrawingProperties>().FirstOrDefault()))
            .Where(s => s.Properties is not null)
            .ToDictionary(s => s.Properties!.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), s => (s.Element, Name: s.Properties!.Name!.Value!));
        var root = XElement.Parse(timing.OuterXml);
        var main = root.Descendants(PresentationML + "cTn").Single(c => (string?)c.Attribute("nodeType") == "mainSeq");
        return main.Element(PresentationML + "childTnLst")!.Elements(PresentationML + "par").Select(click =>
            string.Join(" + ", click.Descendants(PresentationML + "cTn").Where(c => c.Attribute("presetID") is not null).Select(effect =>
            {
                var target = effect.Descendants(PresentationML + "spTgt").First();
                var (element, name) = shapes[(string)target.Attribute("spid")!];
                var kind = $"{effect.Attribute("presetClass")!.Value}{effect.Attribute("presetID")!.Value}";
                if (target.Descendants(PresentationML + "pRg").SingleOrDefault() is { } range)
                {
                    var paragraphs = element.Descendants<A.Paragraph>().ToList();
                    var (start, end) = ((int)range.Attribute("st")!, (int)range.Attribute("end")!);
                    return $"{kind} {string.Join("|", paragraphs.Skip(start).Take(end - start + 1).Select(p => p.InnerText))}";
                }

                return $"{kind} [{name.Split(' ')[0]}]{element.InnerText}";
            }))).ToList();
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void TheListsSample_IsValid(string template)
    {
        var pptx = Samples.Generate(Sample("presenting-lists.md"), new PptxOptions { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"presenting-lists-{template}.pptx", pptx);
        Samples.AssertValid(pptx);
    }

    [Fact]
    public void AContentSlide_BuildsByParagraph_InTheOrderOfItsMarkers()
    {
        using var package = Samples.Open(Samples.Generate(Sample("presenting.md")));
        var slide = Samples.Slides(package).Single(s => TitleOf(s) == "Building the plan");

        Assert.Equal(
            [
                "entr1 Collect the schedules",
                "entr1 Write them as documents|one per route",
                "entr1 Let the planner read them",
                "entr2 The new planner goes live in autumn.",
                "entr1 This line comes in on one click and goes on the next.",
                "exit1 This line comes in on one click and goes on the next.",
                "emph26 Remember the deadline.",
            ],
            Clicks(slide));
        var build = Assert.Single(slide.Slide!.GetFirstChild<P.Timing>()!.BuildList!.Elements<P.BuildParagraph>());
        Assert.Equal(P.ParagraphBuildValues.Paragraph, build.Build!.Value);

        // The timing goes after the transition, as the schema orders them.
        Assert.IsType<P.Transition>(slide.Slide.GetFirstChild<P.Timing>()!.PreviousSibling());
    }

    [Fact]
    public void EachPartOfASplitSlide_PlaysItsOwnItems_FromItsFirstClick()
    {
        using var package = Samples.Open(Samples.Generate(Sample("presenting.md")));
        var parts = Samples.Slides(package).Where(s => TitleOf(s) == "Every berth, one by one").ToList();

        Assert.Equal(2, parts.Count);
        var clicks = parts.Select(Clicks).ToList();
        Assert.Equal(24, clicks.Sum(c => c.Count));
        Assert.All(clicks, c => Assert.All(c, click => Assert.StartsWith("entr22 Berth ", click, StringComparison.Ordinal)));
        Assert.StartsWith("entr22 Berth 1:", clicks[0][0], StringComparison.Ordinal);
        Assert.StartsWith($"entr22 Berth {clicks[0].Count + 1}:", clicks[1][0], StringComparison.Ordinal);
        Assert.Contains("<p:pRg st=\"0\" end=\"0\"", parts[1].Slide!.GetFirstChild<P.Timing>()!.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public void AVisualSlide_BuildsTheTextAbove_TheVisual_AndTheTextBelow()
    {
        using var package = Samples.Open(Samples.Generate(Sample("presenting.md")));
        var slide = Samples.Slides(package).Single(s => TitleOf(s) == "Arrivals, step by step");

        var clicks = Clicks(slide);
        Assert.Equal(3, clicks.Count);
        Assert.Equal("entr1 Arrivals per quarter, with the total below:", clicks[0]);
        Assert.StartsWith("entr22 [Table]", clicks[1], StringComparison.Ordinal);
        Assert.EndsWith("QuarterShipsQ1412Q2455", clicks[1], StringComparison.Ordinal);
        Assert.Equal("entr1 The total is 867.", clicks[2]);
        Assert.Single(slide.Slide!.GetFirstChild<P.Timing>()!.BuildList!.Elements<P.BuildGraphics>());
    }

    [Fact]
    public void AFlowSlide_BuildsCardByCard_AndMarkersInsideContainers_BuildWhatFollowsThem()
    {
        using var package = Samples.Open(Samples.Generate(Sample("presenting.md")));
        var slide = Samples.Slides(package).Single(s => TitleOf(s) == "Who we talked to");
        var clicks = Clicks(slide);

        Assert.Equal(5, clicks.Count);
        Assert.Equal("entr1 [Box] + entr1 [TextBox]Planners + entr1 [TextBox]Write documents instead of filling tables.", clicks[0]);
        Assert.Contains("[TextBox]Captains", clicks[1], StringComparison.Ordinal);
        Assert.Contains("[TextBox]Agents", clicks[2], StringComparison.Ordinal);
        Assert.Equal("entr1 The rollout follows in autumn.", clicks[3]);
        Assert.StartsWith("emph26 [Box] + emph26 [Box] + emph26 [TextBox]", clicks[4], StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnsBuildColumnByColumn_AndAStatementGoesAsAWhole()
    {
        using var package = Samples.Open(Samples.Generate(Sample("presenting.md")));
        var slide = Samples.Slides(package).Single(s => TitleOf(s) == "Before and after");

        Assert.Equal(
            ["entr1 [TextBox]Before: tables, typed again every week.", "entr1 [TextBox]After: documents, read by the planner.", "exit1 [TextBox]Fewer tables, fewer mistakes."],
            Clicks(slide));
    }

    [Fact]
    public void ImagesInARow_BuildImageByImage()
    {
        var markdown = "## Photos {kicker=\"Row\"}\n\n{.build}\n\n![One](diagram.png) ![Two](photo.png)\n";
        var pptx = Samples.Generate(Samples.Markdown(markdown, Samples.Image("diagram.png"), Samples.Image("photo.png")));

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(["entr1 [Picture]", "entr1 [Picture]"], Clicks(Samples.Slides(package)[0]));
        Assert.Null(Samples.Slides(package)[0].Slide!.GetFirstChild<P.Timing>()!.BuildList);
    }

    [Fact]
    public void RowsAndLabelledTags_BuildItemByItem_AndOtherBlocksAsAWhole()
    {
        var markdown = "## Plan {kicker=\"Rows\"}\n\n{.build}\n\n::: rows\n- **One** – first\n- **Two** – second\n:::\n\n::: panel\n{.build}\n\n- `A` first\n- `B` second\n:::\n\n{.build}\n\n`x` `y` `z`\n";
        var pptx = Generate(markdown);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var clicks = Clicks(Samples.Slides(package)[0]);
        Assert.Equal(5, clicks.Count);
        Assert.Equal("entr1 [Box] + entr1 [TextBox]One + entr1 [TextBox]first", clicks[0]);
        Assert.Equal("entr1 [Box] + entr1 [TextBox]Two + entr1 [TextBox]second + entr1 [Box]", clicks[1]);
        Assert.Equal("entr1 [Chip]A + entr1 [TextBox]first", clicks[2]);
        Assert.Equal("entr1 [Chip]B + entr1 [TextBox]second", clicks[3]);
        Assert.Equal("entr1 [Chip]x + entr1 [Chip]y + entr1 [Chip]z", clicks[4]);
    }

    [Fact]
    public void AParagraphWithAnImage_BuildsItsTextAndTheImageTogether()
    {
        var pptx = Samples.Generate(Samples.Markdown("## Plan\n\n{.frag}\n\nBefore ![Diagram](diagram.png) after\n", Samples.Image("diagram.png")));

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(["entr1 Before + entr1 [Picture] + entr1 after"], Clicks(Samples.Slides(package)[0]));
    }

    [Fact]
    public void CombinedClasses_PlayAsConsecutiveClicks_InTheirWrittenOrder()
    {
        var pptx = Generate("## Plan\n\n{.emphasize .build=wipe .exit}\n\n- One\n- Two\n");

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(["emph26 One|Two", "entr22 One", "entr22 Two", "exit1 One|Two"], Clicks(Samples.Slides(package)[0]));
    }

    [Fact]
    public void AStepWithSeveralShapes_StartsTheOthersWithTheFirst()
    {
        var pptx = Generate("## Plan {kicker=\"Cards\"}\n\n{.frag}\n\n::: cards\n### One\nText.\n:::\n");

        using var package = Samples.Open(pptx);
        var xml = XElement.Parse(Samples.Slides(package)[0].Slide!.GetFirstChild<P.Timing>()!.OuterXml);
        Assert.Equal(
            ["clickEffect", "withEffect", "withEffect"],
            xml.Descendants(PresentationML + "cTn").Where(c => c.Attribute("presetID") is not null).Select(c => (string)c.Attribute("nodeType")!));
    }

    [Fact]
    public void TheCoverAndADarkSlide_Build_AndEveryDeckIsValid()
    {
        var markdown = "# Deck\n\n{.build=fly}\n\nA subtitle that flies in\n\n## Dark {.dark}\n\n{.build}\n\n- One\n- Two\n\n{.frag}\n\n```\ncode\n```\n";
        foreach (var options in new[] { PptxOptions.Default, new PptxOptions { Template = Samples.BuiltIn("sample") }, DarkTemplate.Options })
        {
            var pptx = Generate(markdown, options);
            Samples.AssertValid(pptx);
            using var package = Samples.Open(pptx);
            var slides = Samples.Slides(package);
            Assert.Equal(["entr2 A subtitle that flies in"], Clicks(slides[0]));
            Assert.Equal(["entr1 One", "entr1 Two", "entr1 [Box] + entr1 [TextBox]code"], Clicks(slides[1]));
        }
    }

    [Fact]
    public void ADrawingAndAChart_BuildAsAWhole()
    {
        var drawing = new Drawing(100, 50, [new DrawingShape(DrawingPath.Rectangle(0, 0, 100, 50), "E6F4F5", "0E7C86", 1), new DrawingText(50, 30, [new DrawingSpan("Box")], 16, "1F2937")]);
        var chart = new ChartSpec(ChartKind.Bar, ["A", "B"], [new ChartSeries("S", [1, 2])]);
        var renderer = Samples.Renderer(request => request.Language switch
        {
            "drawing" => new DrawingGraphic(drawing),
            "chart" => new ChartGraphic(chart, drawing),
            _ => null,
        });
        var pptx = Generate("## Drawing\n\n{.frag}\n\n```drawing\n```\n\n## Chart\n\n{.emphasize}\n\n```chart\n```\n", new PptxOptions { CodeBlockRenderers = [renderer] });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(["entr1 [Diagram]Box"], Clicks(slides[0]));
        Assert.Equal(["emph26 [Chart]"], Clicks(slides[1]));
        Assert.Single(slides[1].Slide!.GetFirstChild<P.Timing>()!.BuildList!.Elements<P.BuildGraphics>());
    }

    [Fact]
    public void BuildLists_BuildsEveryTopLevelList_AndAListWithItsOwnMarkerKeepsIt()
    {
        using var package = Samples.Open(Samples.Generate(Sample("presenting-lists.md")));
        var slides = Samples.Slides(package);
        List<string> ClicksOf(string title) => Clicks(slides.Single(s => TitleOf(s) == title));

        Assert.Equal(["entr1 Collect the schedules", "entr1 Write them as documents|one per route", "entr1 Let the planner read them"], ClicksOf("A list builds item by item"));
        Assert.Equal(["entr1 All of these|come in together"], ClicksOf("A list with its own marker keeps it"));
        Assert.Empty(ClicksOf("Text does not build"));
        Assert.Equal(["entr1 But this one builds", "entr1 item by item"], ClicksOf("Lists in containers do not either"));
    }

    [Fact]
    public void BuildLists_FromTheOptions_IsTheSameAsFromTheFrontMatter_AndTheFrontMatterWins()
    {
        var markdown = "## Plan\n\n- One\n- Two\n";
        using var fromOptions = Samples.Open(Generate(markdown, new PptxOptions { Build = DeckBuild.Lists }));
        Assert.Equal(["entr1 One", "entr1 Two"], Clicks(Samples.Slides(fromOptions)[0]));
        using var overridden = Samples.Open(Generate("---\nbuild: none\n---\n" + markdown, new PptxOptions { Build = DeckBuild.Lists }));
        Assert.Empty(Clicks(Samples.Slides(overridden)[0]));
        var error = Assert.Throws<DocumentConversionException>(() => Generate("---\nbuild: all\n---\n" + markdown));
        Assert.Contains("'build'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EachEffect_IsWrittenAsTheReference()
    {
        var reference = new System.Text.StringBuilder(
            "# Build effects\n\nWhat the generator writes for each marker, as PowerPoint writes its presets. Compared by the tests; "
            + "regenerated with `DOCLIBS_UPDATE_EXPECTED=1`.\n");
        string Timing(byte[] pptx, int slide = 0)
        {
            Samples.AssertValid(pptx);
            using var package = Samples.Open(pptx);
            return Samples.Slides(package)[slide].Slide!.GetFirstChild<P.Timing>()!.OuterXml;
        }

        // The whole timing once: a paragraph of a content slide.
        reference.Append($"\n## The timing of `{{.build}}` on one paragraph\n\n```xml\n{XElement.Parse(Timing(Generate("## One\n\n{.build}\n\nText.\n")))}\n```\n");
        foreach (var marker in new[] { "{.build=appear}", "{.build=fly}", "{.build=wipe}", "{.frag}", "{.exit}", "{.emphasize}" })
        {
            var effect = XElement.Parse(Timing(Generate($"## One\n\n{marker}\n\nText.\n"))).Descendants(PresentationML + "cTn").Single(c => c.Attribute("presetID") is not null);
            reference.Append($"\n## The effect of `{marker}`\n\n```xml\n{effect}\n```\n");
        }

        // The build list of a whole shape, a table and a picture (which has none).
        var lists = XElement.Parse(Timing(Samples.Generate(Samples.Markdown(
            "## One {kicker=\"K\"}\n\n{.frag}\n\n::: panel\nText.\n:::\n\n{.frag}\n\n| A |\n| - |\n| 1 |\n\n{.frag}\n\n![Diagram](diagram.png)\n",
            Samples.Image("diagram.png"))))).Element(PresentationML + "bldLst");
        reference.Append($"\n## The build list of a panel, a table and a picture\n\n```xml\n{lists}\n```\n");
        Samples.AssertMatchesExpected("build-effects", reference.ToString().Replace(" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"", string.Empty, StringComparison.Ordinal).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("## A\n\n{.build}\n\n## B\n\nText.\n", "followed by a heading")]
    [InlineData("## A\n\n{.build}\n\n### B\n\nText.\n", "followed by a heading")]
    [InlineData("## A\n\nText.\n\n{.build}\n\n::: notes\nSay it.\n:::\n", "followed by speaker notes")]
    [InlineData("## A\n\n{.build}\n\n{.frag}\n\nText.\n", "followed by another marker")]
    [InlineData("## A\n\nText.\n\n{.build}\n", "followed by nothing to build")]
    [InlineData("## A {kicker=\"K\"}\n\n::: panel\nText.\n\n{.frag}\n:::\n", "followed by nothing to build")]
    [InlineData("## A\n\n- Item\n\n  {.build}\n\n  More.\n", "inside a list")]
    [InlineData("## A\n\n> {.build}\n>\n> Quoted.\n", "inside a quote")]
    [InlineData("## A\n\nText.\n\n::: notes\n{.build}\n\nSay it.\n:::\n", "inside speaker notes")]
    public void AStrayMarker_Throws_NamingItsLine(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("on line ", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{.note}", ".note")]
    [InlineData("{.build=zoom}", ".build=zoom")]
    [InlineData("{.build .dark}", ".dark")]
    [InlineData("{width=\"50%\"}", "width=\"50%\"")]
    public void AnUnknownMarker_Throws_ListingTheValidOnes(string marker, string unknown)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate($"## A\n\n{marker}\n\nText.\n"));
        Assert.Contains($"Unknown build marker '{unknown}'", error.Message, StringComparison.Ordinal);
        Assert.Contains(".build, .build=appear, .build=fly, .build=wipe, .frag, .exit, .emphasize", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"json\": 1}")]
    [InlineData("\\{.build}")]
    [InlineData("{.build} and more")]
    public void OtherBraces_StayText(string paragraph)
    {
        using var package = Samples.Open(Generate($"## A\n\n{paragraph}\n"));
        var slide = Samples.Slides(package)[0];
        Assert.Null(slide.Slide!.GetFirstChild<P.Timing>());
        Assert.Contains("{", Samples.PlaceholderText(slide, P.PlaceholderValues.Body) ?? slide.Slide.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeckWithoutMarkers_HasNoTiming_AndBuildNoneChangesNothing()
    {
        var document = Samples.Deck();
        var options = new PptxOptions { Template = new BrandTemplateOptions { ShowDate = false } };
        var pptx = Samples.Generate(document, options);
        using var package = Samples.Open(pptx);
        Assert.All(Samples.Slides(package), s => Assert.Null(s.Slide!.GetFirstChild<P.Timing>()));

        var none = Samples.Generate(document with { Markdown = "---\nbuild: none\n---\n" + document.Markdown }, options);
        Assert.Equal(PresentingTests.Parts(pptx), PresentingTests.Parts(none));
    }

    [Fact]
    public void TheConverter_DoesNotReadBuildsBack()
    {
        var pptx = Generate("## Plan\n\n{.build}\n\n- One\n- Two\n");
        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default).Markdown;
        Assert.DoesNotContain("{.", back, StringComparison.Ordinal);
        Assert.Contains("One", back, StringComparison.Ordinal);
    }
}
