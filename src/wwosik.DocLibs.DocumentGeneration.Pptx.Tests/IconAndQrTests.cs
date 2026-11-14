using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><c>qr:…</c> images and <c>:icon-name:</c> on slides.</summary>
public class IconAndQrTests
{
    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, options);
        var pptx = output.ToArray();
        Samples.AssertValid(pptx);
        return (result, pptx);
    }

    private static List<P.GroupShape> Groups(byte[] pptx, string name, out PresentationDocument package)
    {
        package = Samples.Open(pptx);
        return [.. Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.GroupShape>())
            .Where(g => g.NonVisualGroupShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith(name, StringComparison.Ordinal))];
    }

    private static long ContentWidth(PresentationDocument package) =>
        package.PresentationPart!.Presentation!.SlideSize!.Cx!.Value;

    [Fact]
    public void AQrCode_IsAWhiteSquareAndOnePath_WithItsCaption_AtMostAThirdOfTheWidth()
    {
        var (result, pptx) = Generate("# Deck\n\n## Survey\n\n![Survey](qr:https://example.com/survey \"caption\")\n");

        Assert.False(result.HasNotes, result.ToString());
        var group = Assert.Single(Groups(pptx, "Diagram ", out var package));
        using (package)
        {
            var shapes = group.Elements<P.Shape>().ToList();
            Assert.Equal(3, shapes.Count);
            Assert.Equal("FFFFFF", shapes[0].ShapeProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
            Assert.NotNull(shapes[1].ShapeProperties!.GetFirstChild<A.CustomGeometry>());
            Assert.Equal(Brand.Neutral.Colors.Primary.TrimStart('#').ToUpperInvariant(), shapes[1].ShapeProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
            Assert.Equal("Survey", shapes[2].TextBody!.InnerText);
            Assert.True(group.GroupShapeProperties!.TransformGroup!.Extents!.Cx!.Value <= ContentWidth(package) / 3);
        }

        Samples.WriteForReview("qr-and-icons.pptx", Generate(Review).Pptx);
    }

    [Fact]
    public void AQrCode_OnADarkSlide_StaysDarkOnWhite()
    {
        var (_, pptx) = Generate("# Deck\n\n## Survey {.dark}\n\n![Survey](qr:https://example.com/survey)\n");

        var group = Assert.Single(Groups(pptx, "Diagram ", out var package));
        using (package)
        {
            var shapes = group.Elements<P.Shape>().ToList();
            Assert.Equal(2, shapes.Count);
            Assert.Equal("FFFFFF", shapes[0].ShapeProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
            Assert.True(QrDrawing.Contrast(shapes[1].ShapeProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value!, "FFFFFF") >= 4.5);
        }
    }

    [Fact]
    public void AQrCode_TakesNoOtherAttributes_ThanScaleAndCaption()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("## S\n\n![S](qr:https://example.com){.round}\n"));

        Assert.Contains("scale=<number> and .caption", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIcon_AloneInACardsFirstParagraph_IsItsFigure_InTheAccentColour()
    {
        var (result, pptx) = Generate("# Deck\n\n## Why\n\n::: cards\n### Fast\n\n:icon-bolt:\n\nIt is quick.\n\n### Safe\n\n:icon-shield-check:\n\nIt is safe.\n:::\n");

        Assert.False(result.HasNotes, result.ToString());
        var icons = Groups(pptx, "Icon ", out var package);
        using (package)
        {
            Assert.Equal(2, icons.Count);
            var shape = icons[0].Elements<P.Shape>().Single();
            Assert.NotNull(shape.ShapeProperties!.GetFirstChild<A.CustomGeometry>());
            Assert.NotNull(shape.ShapeProperties.GetFirstChild<A.NoFill>());
            var outline = shape.ShapeProperties.GetFirstChild<A.Outline>()!;
            Assert.Equal(A.LineCapValues.Round, outline.CapType!.Value);
            Assert.NotNull(outline.GetFirstChild<A.SolidFill>());
            var text = Samples.Slides(package)[1].Slide!.InnerText;
            Assert.DoesNotContain(":icon-", text, StringComparison.Ordinal);
            Assert.Contains("It is quick.", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AListOfIconItems_IsAnIconList_WithoutBullets()
    {
        var (result, pptx) = Generate("# Deck\n\n## Why\n\n- :icon-check: Fast to set up\n- :icon-x: No lock-in\n- :icon-heart: Loved by users\n");

        Assert.False(result.HasNotes, result.ToString());
        var icons = Groups(pptx, "Icon ", out var package);
        using (package)
        {
            Assert.Equal(3, icons.Count);
            var slide = Samples.Slides(package)[1].Slide!;
            Assert.Empty(slide.Descendants<A.CharacterBullet>());
            var texts = slide.Descendants<P.Shape>().Where(s => s.TextBody is not null).Select(s => s.TextBody!.InnerText).ToList();
            Assert.Contains("Fast to set up", texts);
            Assert.Contains("Loved by users", texts);

            // Each icon sits left of its text, one per row, top to bottom.
            var tops = icons.Select(g => g.GroupShapeProperties!.TransformGroup!.Offset!.Y!.Value).ToList();
            Assert.Equal(tops.Order(), tops);
            Assert.Single(icons.Select(g => g.GroupShapeProperties!.TransformGroup!.Offset!.X!.Value).Distinct());
        }
    }

    [Fact]
    public void AnIconList_BuildsItemByItem()
    {
        var (_, pptx) = Generate("# Deck\n\n## Why\n\n{.build}\n\n- :icon-check: Fast\n- :icon-x: Cheap\n");

        using var package = Samples.Open(pptx);
        var timing = System.Xml.Linq.XElement.Parse(Samples.Slides(package)[1].Slide!.GetFirstChild<P.Timing>()!.OuterXml);
        System.Xml.Linq.XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var main = timing.Descendants(p + "cTn").Single(c => (string?)c.Attribute("nodeType") == "mainSeq");
        var clicks = main.Element(p + "childTnLst")!.Elements(p + "par").ToList();
        Assert.Equal(2, clicks.Count);

        // Each click shows an icon and its text.
        Assert.All(clicks, click => Assert.Equal(2, click.Descendants(p + "spTgt").Select(t => (string)t.Attribute("spid")!).Distinct().Count()));
    }

    [Fact]
    public void AListWhereOnlySomeItemsStartWithAnIcon_KeepsItsBullets_AndReportsTheIcon()
    {
        var (result, pptx) = Generate("# Deck\n\n## Why\n\n- :icon-check: Fast\n- Cheap\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.IconNotDrawn, note.Code);
        Assert.Equal(5, note.Line);
        Assert.Contains(":icon-check:", note.Message, StringComparison.Ordinal);
        using var package = Samples.Open(pptx);
        var text = Samples.Slides(package)[1].Slide!.InnerText;
        Assert.DoesNotContain("icon", text, StringComparison.Ordinal);
        Assert.Contains("Fast", text, StringComparison.Ordinal);
        Assert.Empty(Groups(pptx, "Icon ", out var again));
        again.Dispose();
    }

    [Fact]
    public void AnInlineIcon_IsLeftOut_AndReportedWithItsLine()
    {
        var (result, pptx) = Generate("# Deck\n\n## Status\n\nAll :icon-check: done.\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal((NoteCodes.IconNotDrawn, 5), (note.Code, note.Line));
        using var package = Samples.Open(pptx);
        Assert.Contains("All  done.", Samples.Slides(package)[1].Slide!.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownIcon_Throws_WithSuggestions()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("## S\n\n- :icon-chek: Done\n"));

        Assert.Contains("Unknown icon 'chek'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Did you mean check", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithIconsSwitchedOff_IconTextStaysText_AndIsNotChecked()
    {
        var (result, pptx) = Generate("# Deck\n\n## S\n\n- :icon-check: Done\n- :icon-chek: Typo\n", new PptxOptions { Icons = null });

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        var text = Samples.Slides(package)[1].Slide!.InnerText;
        Assert.Contains(":icon-check: Done", text, StringComparison.Ordinal);
        Assert.Contains(":icon-chek: Typo", text, StringComparison.Ordinal);
        Assert.Empty(Groups(pptx, "Icon ", out var again));
        again.Dispose();
    }

    [Fact]
    public void AnIconSetOfOurOwn_ReplacesTablers()
    {
        var set = new OwnIcons();
        var (_, pptx) = Generate("# Deck\n\n## S\n\n- :icon-dot: One\n- :icon-dot: Two\n", new PptxOptions { Icons = set });

        var icons = Groups(pptx, "Icon ", out var package);
        using (package)
        {
            Assert.Equal(2, icons.Count);
            Assert.Equal("Ellipse", icons[0].Elements<P.Shape>().Single().NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.Split(' ')[0]);
        }

        Assert.Throws<DocumentConversionException>(() => Generate("## S\n\n- :icon-check: One\n", new PptxOptions { Icons = set }));
    }

    [Fact]
    public void TheGuide_HasAnIconSection_UnlessIconsAreSwitchedOff()
    {
        Assert.Contains("## Icons", PptxGenerator.AuthoringGuide(), StringComparison.Ordinal);
        Assert.Contains("`bolt`", PptxGenerator.AuthoringGuide(), StringComparison.Ordinal);
        Assert.Contains("They are: `dot`.", PptxGenerator.AuthoringGuide(new PptxOptions { Icons = new OwnIcons() }), StringComparison.Ordinal);
        Assert.DoesNotContain("## Icons", PptxGenerator.AuthoringGuide(new PptxOptions { Icons = null }), StringComparison.Ordinal);
    }

    // Everything at once, for a person to open in PowerPoint.
    private const string Review = """
        # QR codes and icons

        ## Survey

        ![Scan for the survey](qr:https://example.com/survey?id=3 "caption")

        ## On dark {.dark}

        ![Survey](qr:https://example.com/survey){.caption}

        ## Why us

        - :icon-bolt: Fast to set up, in minutes rather than days
        - :icon-shield-check: Safe by default
        - :icon-heart: Loved by the people who use it

        ## Cards

        ::: cards
        ### Fast

        :icon-bolt:

        Minutes, not days.

        ### Safe

        :icon-shield-check:

        Checked at every step.

        ### Open

        :icon-lock-open:

        No lock-in.
        :::

        ## Inline

        This :icon-check: icon is left out of the slide's text.
        """;

    private sealed class OwnIcons : IIconSet
    {
        private static readonly Drawing Dot = new(10, 10, [new DrawingShape(new DrawingPath([new MoveTo(0, 0), new LineTo(10, 10)]), "000000", null, 0) { Kind = DrawingShapeKind.Ellipse }]);

        public IReadOnlyCollection<string> Names { get; } = ["dot"];

        public Drawing? Find(string name) => name == "dot" ? Dot : null;
    }
}
