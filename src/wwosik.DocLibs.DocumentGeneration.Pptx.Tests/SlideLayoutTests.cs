using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Flow slides: heading attributes, containers, alerts, tags and table highlights.</summary>
public class SlideLayoutTests
{
    // The sample brand: primary 0E7C86, link B45309, table band E6F4F5 and line B8D8DB, highlight F59E0B, labels
    // success 15803D and danger BE123C (the others default), Inconsolata for code.
    private static readonly PptxOptions Sample = new() { Template = Samples.BuiltIn("sample") };

    private static MarkdownDocument Layouts() => Samples.Markdown(File.ReadAllText(Samples.Path("layouts.md")).ReplaceLineEndings("\n"));

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_RoundTripsThroughThePptxConverter(string template)
    {
        var pptx = Samples.Generate(Layouts(), new PptxOptions { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"layouts-{template}.pptx", pptx);

        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);

        Samples.AssertMatchesExpected($"layouts-{template}.roundtrip", back.Markdown);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsSchemaValid(string template) =>
        Samples.AssertValid(Samples.Generate(Layouts(), new PptxOptions { Template = Samples.BuiltIn(template) }));

    [Fact]
    public void Sample_HasOneSlidePerSection_TheDarkOneOnTheDarkLayout()
    {
        using var package = Samples.Open(Samples.Generate(Layouts()));

        var layouts = Samples.Slides(package).Select(Samples.LayoutName).ToList();

        Assert.Equal(["Title Slide", "Title Only", "Title Only", "Title Only", "Title Only", "Title Only", "Title Only Dark", "Title Only", "Title Only"], layouts);
    }

    [Fact]
    public void SectionsWithoutLayouts_StayContentAndTableSlides()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Text\n\n- a\n\n## Table\n\n| a |\n| - |\n| `x` |\n\n## Code\n\nRun `a` now.\n")));

        Assert.Equal(["Title and Content", "Title Only", "Title and Content"], Samples.Slides(package).Select(Samples.LayoutName));
        var cell = Samples.Slides(package)[1].Slide!.Descendants<A.TableCell>().Last().Descendants<A.RunProperties>().Single();
        Assert.Equal("Consolas", cell.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
    }

    [Fact]
    public void Kicker_SitsAboveTheTitle_WhichMovesDown()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Title {kicker=\"Label\"}\n\nText\n")));
        var slide = Samples.Slides(package)[0];

        var kicker = slide.Slide!.Descendants<P.Shape>().First(s => s.InnerText == "Label");
        var title = slide.Slide.Descendants<P.Shape>().Single(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape?.Type?.Value == P.PlaceholderValues.Title);

        Assert.Equal("Title", title.InnerText);
        Assert.Equal(A.TextCapsValues.All, kicker.Descendants<A.RunProperties>().Single().Capital!.Value);
        Assert.True(title.ShapeProperties!.Transform2D!.Offset!.Y!.Value > kicker.ShapeProperties!.Transform2D!.Offset!.Y!.Value);
    }

    [Fact]
    public void Braces_ThatAreNoAttributes_StayInTheTitle()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Sets {a, b}\n\nText\n")));

        Assert.Equal("Sets {a, b}", Samples.PlaceholderText(Samples.Slides(package)[0], P.PlaceholderValues.Title));
        Assert.Equal("Title and Content", Samples.LayoutName(Samples.Slides(package)[0]));
    }

    [Theory]
    [InlineData("## T {.wide}\n", "Unknown heading attribute '.wide'")]
    [InlineData("## T {kicker=\"a\" kicker=\"b\"}\n", "name kicker twice")]
    [InlineData("## T\n\n::: card\n- **a** b\n:::\n", "Unknown container '::: card'. Valid containers: cards, rows, panel, statement, columns.")]
    [InlineData("## T\n\n::: cards dark\n- **a** b\n:::\n", "takes no arguments")]
    [InlineData("## T\n\n::: cards\nText first\n\n### Card\n:::\n", "each card starts with a ### heading")]
    [InlineData("## T\n\n::: rows\n- plain item\n:::\n", "starts with **bold** text")]
    [InlineData("## T\n\n> [!HINT]\n> x\n", "Unknown alert '[!HINT]'")]
    public void Mistakes_Throw(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cards_AreOutlinedRoundedBoxesWithoutFill_WithTitleFigureAndText()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T\n\n::: cards\n### One\n**26**\n\nfirst\n\n### Two\nsecond\n:::\n"), Sample));
        var slide = Samples.Slides(package)[0].Slide!;

        var boxes = Boxes(slide);
        Assert.Equal(2, boxes.Count);
        Assert.All(boxes, b => Assert.Equal(A.ShapeTypeValues.RoundRectangle, b.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value));
        Assert.All(boxes, b => Assert.NotNull(b.ShapeProperties!.GetFirstChild<A.NoFill>()));
        Assert.All(boxes, b => Assert.Equal("B8D8DB", b.ShapeProperties!.GetFirstChild<A.Outline>()!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value));
        Assert.Equal(boxes[0].ShapeProperties!.Transform2D!.Offset!.Y!.Value, boxes[1].ShapeProperties!.Transform2D!.Offset!.Y!.Value);
        Assert.Equal(boxes[0].ShapeProperties!.Transform2D!.Extents!.Cy!.Value, boxes[1].ShapeProperties!.Transform2D!.Extents!.Cy!.Value);
        var figure = slide.Descendants<A.Run>().Single(r => r.InnerText == "26").RunProperties!;
        Assert.Equal(3200, figure.FontSize!.Value);
        Assert.Equal("B45309", figure.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
    }

    [Fact]
    public void CardsAndRows_TakeTheListForm()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(
            "## T\n\n::: cards\n- **One** – first\n- **Two:** second\n:::\n\n::: rows\n- **Term** – description\n- **Other** - more\n:::\n")));
        var slide = Samples.Slides(package)[0].Slide!;

        var texts = slide.Descendants<P.Shape>()
            .Where(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape is not { } placeholder || placeholder.Type?.Value == P.PlaceholderValues.Title)
            .Select(s => s.InnerText)
            .Where(t => t.Length > 0);
        Assert.Equal(["T", "One", "first", "Two", "second", "Term", "description", "Other", "more"], texts);
        Assert.Equal(2, Boxes(slide).Count(b => b.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value == A.ShapeTypeValues.RoundRectangle));
        Assert.Equal(3, Rules(slide).Count); // above each row and below the last
    }

    [Fact]
    public void DarkPanel_HasThePrimaryFill_AndWhiteText()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T\n\n::: panel dark\nInside\n:::\n"), Sample));
        var slide = Samples.Slides(package)[0].Slide!;

        Assert.Equal("0E7C86", Fill(Boxes(slide).Single()));
        Assert.Equal("FFFFFF", slide.Descendants<A.Run>().Single(r => r.InnerText == "Inside").RunProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
    }

    [Fact]
    public void Callouts_HaveABar_InThePrimaryOrTheHighlightColour()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T\n\n> [!NOTE]\n> Note\n\n> [!WARNING]\n> Careful\n"), Sample));
        var slide = Samples.Slides(package)[0].Slide!;

        var fills = Boxes(slide).Select(Fill).ToList();
        Assert.Equal(["E6F4F5", "0E7C86", "E6F4F5", "F59E0B"], fills); // band and bar, twice: primary, then highlight
        Assert.Null(slide.Descendants<A.Run>().Single(r => r.InnerText == "Note").RunProperties!.Italic);
    }

    [Fact]
    public void Tags_AreChips_AndLabelledTagsPutTheLabelBesideTheText()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T\n\n`a` `b` `c`\n\n::: panel\n- `x` first\n- `yy` second\n:::\n"), Sample));
        var slide = Samples.Slides(package)[0].Slide!;

        var chips = slide.Descendants<P.Shape>().Where(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("Chip", StringComparison.Ordinal)).ToList();
        Assert.Equal(["a", "b", "c", "x", "yy"], chips.Select(c => c.InnerText));
        Assert.Equal("Inconsolata", chips[0].Descendants<A.LatinFont>().Single().Typeface!.Value);
        Assert.True(chips[3].Descendants<A.RunProperties>().Single().Bold!.Value);
        var first = slide.Descendants<P.Shape>().Single(s => s.InnerText == "first");
        Assert.True(first.ShapeProperties!.Transform2D!.Offset!.X!.Value > chips[3].ShapeProperties!.Transform2D!.Offset!.X!.Value);
    }

    [Fact]
    public void ACodeListOutsideAContainer_StaysAList()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T {kicker=\"K\"}\n\n- `--force` overwrites\n")));

        Assert.DoesNotContain(Samples.Slides(package)[0].Slide!.Descendants<P.Shape>(), s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("Chip", StringComparison.Ordinal));
    }

    [Fact]
    public void Tables_HighlightMarkedRows_AndTintLabelCellsByCategory_OnFlowSlides()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(
            "---\nlabels.success: Done\nlabels.danger: Late; Blocked\n---\n\n## T {kicker=\"K\"}\n\n| a | b |\n| - | - |\n| ==x== | `Done` |\n| y | `Late` |\n| z | `Later` |\n\nSome ==marked== text.\n"), Sample));
        var slide = Samples.Slides(package)[0].Slide!;

        var rows = slide.Descendants<A.TableRow>().ToList();
        string? CellFill(int row, int column) => rows[row].Elements<A.TableCell>().ElementAt(column).TableCellProperties!.GetFirstChild<A.SolidFill>()?.RgbColorModelHex!.Val!.Value;
        Assert.Equal(ResolvedBrand.Mix("F59E0B", 0.15), CellFill(1, 0)); // the highlight at 15 %
        Assert.Null(CellFill(2, 0));
        Assert.Equal(Tint("15803D"), CellFill(1, 1)); // success: the brand's green
        Assert.Equal(Tint("BE123C"), CellFill(2, 1)); // danger: the brand's red
        Assert.Equal(Tint("9CA3AF"), CellFill(3, 1)); // no category: neutral, the default grey
        var label = rows[1].Elements<A.TableCell>().Last().Descendants<A.RunProperties>().Single();
        Assert.True(label.Bold!.Value);
        Assert.Null(label.GetFirstChild<A.SolidFill>());
        var marked = slide.Descendants<A.Run>().Single(r => r.InnerText == "marked").RunProperties!;
        Assert.True(marked.Bold!.Value);
        Assert.Equal(ResolvedBrand.Mix("F59E0B", 0.15), marked.GetFirstChild<A.Highlight>()!.RgbColorModelHex!.Val!.Value);
        Assert.Null(marked.GetFirstChild<A.SolidFill>());
    }

    [Theory]
    [InlineData("---\nlabels.ok: Done\n---\n## T\n", "names no label category")]
    [InlineData("---\nlabels.success: Done\nlabels.info: Done\n---\n## T\n", "'Done' is assigned to two categories")]
    public void LabelMistakes_Throw(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeBlocks_OnFlowSlides_SitOnABox_WithMoreLineSpacing()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T {kicker=\"K\"}\n\n```\na = 1\nb = 2\n```\n"), Sample));
        var slide = Samples.Slides(package)[0].Slide!;

        Assert.Equal(ResolvedBrand.Mix("B8D8DB", 0.30), Fill(Boxes(slide).Single()));
        var lines = slide.Descendants<A.Paragraph>().Where(p => p.InnerText is "a = 1" or "b = 2").ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, p => Assert.Equal(130000, p.ParagraphProperties!.GetFirstChild<A.LineSpacing>()!.SpacingPercent!.Val!.Value));
    }

    [Fact]
    public void AFlowSlide_StacksSeveralTables()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T {kicker=\"K\"}\n\n| a |\n| - |\n| 1 |\n\n| b |\n| - |\n| 2 |\n")));

        var tables = Samples.Slides(package).Single().Slide!.Descendants<P.GraphicFrame>().ToList();
        Assert.Equal(2, tables.Count);
        Assert.True(tables[1].Transform!.Offset!.Y!.Value > tables[0].Transform!.Offset!.Y!.Value);
    }

    [Fact]
    public void Statement_IsLargeAndBold()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## T\n\n::: statement\nBig claim.\n\nSmall print.\n:::\n")));
        var slide = Samples.Slides(package)[0].Slide!;

        var big = slide.Descendants<A.Run>().Single(r => r.InnerText == "Big claim.").RunProperties!;
        Assert.Equal(2600, big.FontSize!.Value);
        Assert.True(big.Bold!.Value);
        Assert.Equal(1600, slide.Descendants<A.Run>().Single(r => r.InnerText == "Small print.").RunProperties!.FontSize!.Value);
    }

    [Fact]
    public void DarkSlide_OnACallersTemplate_GetsADarkBackground_AndWhiteText()
    {
        var template = new MemoryStream(Samples.EditedTemplate(new BrandTemplateOptions(), master => RemoveDarkLayout(master)));
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Dark {.dark}\n\nText\n"), new PptxOptions { Template = new CustomTemplateOptions(template) }));
        var slide = Samples.Slides(package)[0];

        Assert.Equal("Title Only", Samples.LayoutName(slide));
        var background = slide.Slide!.CommonSlideData!.Background!.BackgroundProperties!.GetFirstChild<A.SolidFill>()!.SchemeColor!.Val!.Value;
        Assert.Equal(A.SchemeColorValues.Text2, background);
        Assert.All(
            slide.Slide.Descendants<A.Run>().Where(r => r.InnerText is "Dark" or "Text"),
            r => Assert.Equal("FFFFFF", r.RunProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value));
    }

    [Fact]
    public void DarkSlide_OnTheBuiltInTemplate_UsesTheDarkLayout()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Dark {.dark}\n\nText\n"), Sample));
        var slide = Samples.Slides(package)[0];

        Assert.Equal("Title Only Dark", Samples.LayoutName(slide));
        Assert.Null(slide.Slide!.CommonSlideData!.Background);
        Assert.Equal("0E7C86", slide.SlideLayoutPart!.SlideLayout!.CommonSlideData!.Background!.Descendants<A.RgbColorModelHex>().Single().Val!.Value);
    }

    private static string Tint(string color) => ResolvedBrand.Mix(color, 0.22);

    private static List<P.Shape> Boxes(P.Slide slide) =>
        slide.Descendants<P.Shape>().Where(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("Box", StringComparison.Ordinal)).ToList();

    private static List<P.Shape> Rules(P.Slide slide) =>
        Boxes(slide).Where(b => b.ShapeProperties!.Transform2D!.Extents!.Cy!.Value == 12700).ToList();

    private static string Fill(P.Shape shape) => shape.ShapeProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value!;

    private static void RemoveDarkLayout(SlideMasterPart master)
    {
        var layout = master.SlideLayoutParts.Single(l => l.SlideLayout!.CommonSlideData!.Name!.Value == "Title Only Dark");
        var relationshipId = master.GetIdOfPart(layout);
        master.SlideMaster!.SlideLayoutIdList!.Elements<P.SlideLayoutId>().Single(id => id.RelationshipId == relationshipId).Remove();
        master.DeletePart(layout);
    }
}
