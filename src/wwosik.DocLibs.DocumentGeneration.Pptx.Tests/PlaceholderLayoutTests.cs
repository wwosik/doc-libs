using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Numbered chapter dividers and <c>layout="…"</c> across a layout's placeholders (E01-S07).</summary>
public class PlaceholderLayoutTests
{
    private const string Chapters = "# Deck\n\nSubtitle\n\n## One\n\n- a\n\n# Market\n\nWhere we are\n\n## Two\n\n- b\n\n# Plan\n\n# Thank you {.closing}\n";

    private static (byte[] Pptx, GenerationResult Result) Generate(string markdown, PptxOptions options, params DocumentResource[] resources)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown, resources), output, options);
        return (output.ToArray(), result);
    }

    private static P.PlaceholderShape? PlaceholderOf(DocumentFormat.OpenXml.OpenXmlElement shape) =>
        shape.Descendants<P.ApplicationNonVisualDrawingProperties>().FirstOrDefault()?.PlaceholderShape;

    // The shapes of a slide in a placeholder with an index, but the footer's.
    private static List<(uint Index, DocumentFormat.OpenXml.OpenXmlElement Shape)> IndexedPlaceholders(SlidePart slide) =>
        [.. slide.Slide!.CommonSlideData!.ShapeTree!.ChildElements
            .Select(e => (Placeholder: PlaceholderOf(e), Shape: e))
            .Where(p => p.Placeholder?.Index is not null
                && !(p.Placeholder.Type?.Value is { } type && (type == P.PlaceholderValues.Footer || type == P.PlaceholderValues.SlideNumber || type == P.PlaceholderValues.DateAndTime)))
            .Select(p => (p.Placeholder!.Index!.Value, p.Shape))];

    private static string? TextAt(SlidePart slide, uint index) =>
        IndexedPlaceholders(slide).Where(p => p.Index == index).Select(p => p.Shape.InnerText).FirstOrDefault();

    private static PptxOptions RolesWith(PptxTemplateRoles roles) => new()
    {
        Template = new BrandTemplateOptions { Brand = RolesTemplate.Brand with { Pptx = new PptxBrandTemplate(RolesTemplate.Content) { Roles = roles } } },
    };

    [Fact]
    public void TheParser_NumbersEveryDividerAfterTheCover_ButNotTheClosingSlide()
    {
        var slides = DeckParser.Parse(Samples.Markdown(Chapters), PptxOptions.Default, numberChapters: true).OfType<TitleSlide>().ToList();
        var off = DeckParser.Parse(Samples.Markdown(Chapters), PptxOptions.Default).OfType<TitleSlide>();

        Assert.Equal([null, 1, 2, null], slides.Select(s => s.Number));
        Assert.All(off, s => Assert.Null(s.Number));
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void OnTheBuiltInMaster_TheNumberGoesIntoTheChapterNumberPlaceholder(string brand)
    {
        var pptx = Samples.Generate(Samples.Markdown("---\nnumberChapters: true\n---\n" + Chapters), new PptxOptions { Template = Samples.BuiltIn(brand) });
        Samples.WriteForReview($"chapters-{brand}.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        string? Number(SlidePart slide) => slide.Slide!.Descendants<P.Shape>()
            .Where(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("Chapter Number", StringComparison.Ordinal))
            .Select(s => s.InnerText).SingleOrDefault();
        Assert.Equal([null, null, "01", null, "02", null], slides.Select(Number));

        // The placeholder of the section layout, with its index; the subtitle stays where it was.
        var divider = slides[2];
        var chapter = divider.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "01");
        var layoutPlaceholder = divider.SlideLayoutPart!.SlideLayout!.Descendants<P.Shape>()
            .Single(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value == TemplateBuilder.ChapterNumberName)
            .Descendants<P.PlaceholderShape>().Single();
        Assert.Equal(layoutPlaceholder.Index!.Value, PlaceholderOf(chapter)!.Index!.Value);
        Assert.Contains(divider.Slide.Descendants<P.Shape>(), s => s.InnerText == "Where we are");
    }

    [Fact]
    public void WithoutNumberChapters_DecksHaveNoNumbers()
    {
        var pptx = Samples.Generate(Samples.Markdown(Chapters));

        using var package = Samples.Open(pptx);
        Assert.DoesNotContain(Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.Shape>()), s => s.InnerText is "01" or "02");
    }

    [Fact]
    public void ASectionLayoutWithoutANumberPlaceholder_GetsTheNumberInATextBoxAboveTheTitle()
    {
        var options = DarkTemplate.Options;
        var pptx = Samples.Generate(Samples.Markdown("---\nnumberChapters: true\n---\n" + Chapters), options);
        Samples.WriteForReview("chapters-brand-dark.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var divider = Samples.Slides(package)[2];
        var box = divider.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "01");
        Assert.True(box.NonVisualShapeProperties!.NonVisualShapeDrawingProperties!.TextBox?.Value);
        var title = divider.SlideLayoutPart!.SlideLayout!.Descendants<P.Shape>()
            .Single(s => PlaceholderOf(s)?.Type?.Value == P.PlaceholderValues.Title).ShapeProperties!.Transform2D!;
        var transform = box.ShapeProperties!.Transform2D!;
        Assert.Equal(title.Offset!.Y!.Value, transform.Offset!.Y!.Value + transform.Extents!.Cy!.Value);
        var run = box.Descendants<A.Run>().Single().RunProperties!;
        Assert.Equal("+mj-lt", run.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
        Assert.NotNull(run.GetFirstChild<A.SolidFill>());
    }

    [Fact]
    public void TheDesignSwitchesNumbersOn_AndFrontMatterOverridesIt()
    {
        var options = RolesWith(RolesTemplate.Roles with { NumberChapters = true });

        using var on = Samples.Open(Samples.Generate(Samples.Markdown(Chapters), options));
        using var off = Samples.Open(Samples.Generate(Samples.Markdown("---\nnumberChapters: false\n---\n" + Chapters), options));

        Assert.Contains(Samples.Slides(on)[2].Slide!.Descendants<P.Shape>(), s => s.InnerText == "01");
        Assert.DoesNotContain(Samples.Slides(off).SelectMany(s => s.Slide!.Descendants<P.Shape>()), s => s.InnerText is "01" or "02");
    }

    [Fact]
    public void TwoTextParts_FillTwoBodies_InTheDeclaredOrder()
    {
        var (pptx, result) = Generate($"# Deck\n\n## Offers {{layout=\"{RolesTemplate.TwoColumns}\"}}\n\n- Basic\n\n---\n\n- Premium\n", RolesTemplate.Options);
        Samples.WriteForReview("placeholders-two-parts.pptx", pptx);

        Samples.AssertValid(pptx);
        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal(RolesTemplate.TwoColumns, Samples.LayoutName(slide));

        // RolesTemplate declares "Content Placeholder 3" (idx 2) first.
        Assert.Equal("Basic", TextAt(slide, 2));
        Assert.Equal("Premium", TextAt(slide, 1));
    }

    [Fact]
    public void MorePartsThanBodies_ContinueOnASlideOnTheSameLayout()
    {
        var (pptx, result) = Generate($"# Deck\n\n## Offers {{layout=\"{RolesTemplate.TwoColumns}\"}}\n\nOne\n\n---\n\nTwo\n\n---\n\nThree\n", RolesTemplate.Options);

        Samples.AssertValid(pptx);
        Assert.Equal(NoteCodes.SlideSplit, Assert.Single(result.Notes).Code);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(3, slides.Count);
        Assert.Equal([RolesTemplate.TwoColumns, RolesTemplate.TwoColumns], slides.Skip(1).Select(Samples.LayoutName));
        Assert.Equal("Three", TextAt(slides[2], 2));

        // The placeholder left over is not written.
        Assert.Null(TextAt(slides[2], 1));
    }

    [Fact]
    public void AnImage_IsCroppedIntoThePicturePlaceholder_WithANote()
    {
        var wide = TestImages.Png("wide.png", 400, 100);
        var (pptx, result) = Generate("# Deck\n\n## View {layout=\"Picture\"}\n\n![The view](wide.png)\n", RolesTemplate.Options, wide);
        Samples.WriteForReview("placeholders-picture.pptx", pptx);

        Samples.AssertValid(pptx);
        Assert.Contains(result.Notes, n => n.Code == NoteCodes.ImageCropped && n.Message.Contains("wide.png", StringComparison.Ordinal));
        using var package = Samples.Open(pptx);
        var picture = Samples.Slides(package)[1].Slide!.Descendants<P.Picture>().Single();
        var placeholder = PlaceholderOf(picture)!;
        Assert.Equal(P.PlaceholderValues.Picture, placeholder.Type!.Value);
        Assert.Equal(1U, placeholder.Index!.Value);
        Assert.Null(picture.ShapeProperties!.Transform2D);

        // The Picture layout's box is 10896600 × 4370832 EMU; the 4:1 image loses the same share on either side.
        var crop = picture.BlipFill!.SourceRectangle!;
        var expected = (int)Math.Round((1 - (10896600 / 4370832.0 / 4)) / 2 * 100000);
        Assert.InRange(crop.Left!.Value, expected - 2, expected + 2);
        Assert.Equal(crop.Left.Value, crop.Right!.Value);
        Assert.Equal(0, crop.Top?.Value ?? 0);
    }

    [Fact]
    public void FocusChoosesWhatStays_AndARowFillsSeveralPictures()
    {
        var wide = TestImages.Png("wide.png", 400, 100);
        var square = TestImages.Png("square.png", 100, 100);
        var (pptx, result) = Generate(
            "# Deck\n\n## Left {layout=\"Picture\"}\n\n![Left](wide.png){focus=left}\n\n## People {layout=\"Contact\"}\n\n![A](square.png) ![B](square.png)\n",
            RolesTemplate.Options,
            wide,
            square);

        Samples.AssertValid(pptx);
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.AttributeIgnored);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        var crop = slides[1].Slide!.Descendants<P.Picture>().Single().BlipFill!.SourceRectangle!;
        Assert.Equal(0, crop.Left?.Value ?? 0);
        Assert.True(crop.Right!.Value > 0);
        Assert.Equal([1U, 2U], slides[2].Slide!.Descendants<P.Picture>().Select(p => PlaceholderOf(p)!.Index!.Value));
    }

    [Fact]
    public void AChart_GoesIntoTheChartPlaceholder()
    {
        var chart = new ChartSpec(ChartKind.Bar, ["A", "B"], [new ChartSeries("S", [1, 2])]);
        var drawing = new Drawing(100, 50, []);
        var options = RolesTemplate.Options with { CodeBlockRenderers = [Samples.Renderer(r => r.Language == "chart" ? new ChartGraphic(chart, drawing) : null)] };
        var (pptx, _) = Generate($"# Deck\n\n## Revenue {{layout=\"{RolesTemplate.Chart}\"}}\n\n```chart\n```\n", options);
        Samples.WriteForReview("placeholders-chart.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        var frame = slide.Slide!.Descendants<P.GraphicFrame>().Single();
        var placeholder = PlaceholderOf(frame)!;
        Assert.Equal(P.PlaceholderValues.Chart, placeholder.Type!.Value);
        var layoutBox = slide.SlideLayoutPart!.SlideLayout!.Descendants<P.Shape>()
            .Single(s => PlaceholderOf(s)?.Type?.Value == P.PlaceholderValues.Chart).ShapeProperties!.Transform2D!;
        Assert.Equal(layoutBox.Offset!.X!.Value, frame.Transform!.Offset!.X!.Value);
        Assert.Equal(layoutBox.Extents!.Cy!.Value, frame.Transform.Extents!.Cy!.Value);

        // Its chart-text placeholders stay empty.
        Assert.DoesNotContain(slide.Slide.Descendants<P.Shape>(), s => PlaceholderOf(s)?.Type?.Value == P.PlaceholderValues.Body);
    }

    [Fact]
    public void ATable_GoesIntoAnObjBody_FittedIntoItsBox()
    {
        var (pptx, _) = Generate($"# Deck\n\n## Prices {{layout=\"{RolesTemplate.TwoColumns}\"}}\n\nWhat it costs\n\n---\n\n| Plan | Price |\n| ---- | ----: |\n| Basic | 10 |\n", RolesTemplate.Options);
        Samples.WriteForReview("placeholders-table.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal("What it costs", TextAt(slide, 2));
        var table = slide.Slide!.Descendants<P.GraphicFrame>().Single();
        var left = slide.SlideLayoutPart!.SlideLayout!.Descendants<P.Shape>()
            .Single(s => PlaceholderOf(s)?.Index?.Value == 1U).ShapeProperties!.Transform2D!;
        Assert.Equal(left.Offset!.X!.Value, table.Transform!.Offset!.X!.Value);
        Assert.Equal(left.Extents!.Cx!.Value, table.Transform.Extents!.Cx!.Value);
        Assert.Null(PlaceholderOf(table));
    }

    [Theory]
    [InlineData("::: panel\nBox\n:::\n\n---\n\nMore\n", "")]
    [InlineData("> [!NOTE]\n> Careful\n\n---\n\nMore\n", "")]
    [InlineData("Text\n\n---\n\nMore\n", " .dark")]
    [InlineData("Text\n\n---\n\nMore\n", " kicker=\"K\"")]
    public void FlowBlocks_InASplitLayoutSection_Throw(string body, string attributes)
    {
        var markdown = $"# Deck\n\n## Mixed {{layout=\"{RolesTemplate.TwoColumns}\"{attributes}}}\n\n{body}";

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown), RolesTemplate.Options));
        Assert.Contains("use a flow slide", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentTheLayoutHasNoPlaceholderFor_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown("# Deck\n\n## View {layout=\"Picture\"}\n\nText\n\n---\n\nMore\n"),
            RolesTemplate.Options));
        Assert.Contains("The layout 'Picture' has no placeholder for text", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALayoutSectionWithoutSplitOrPictureVisual_StaysAsBefore()
    {
        var image = TestImages.Png("chart.png", 300, 200);
        var (pptx, _) = Generate(
            $"# Deck\n\n## Text {{layout=\"{RolesTemplate.TwoColumns}\"}}\n\n- A\n- B\n\n## Image {{layout=\"{RolesTemplate.TwoColumns}\"}}\n\nAbove\n\n![Chart](chart.png)\n\n"
            + "## Caption {layout=\"Picture with Caption\"}\n\n| a | b |\n| - | - |\n| 1 | 2 |\n\n![Chart](chart.png)\n",
            RolesTemplate.Options,
            image);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);

        // Text in the first body in tree order only; the image as a picture of its own, below a text box, as before E01-S07.
        Assert.Equal("AB", TextAt(slides[1], 1));
        Assert.Null(TextAt(slides[1], 2));
        var picture = slides[2].Slide!.Descendants<P.Picture>().Single();
        Assert.Null(PlaceholderOf(picture));
        Assert.NotNull(picture.ShapeProperties!.Transform2D);

        // A table finds no `obj` body on Picture with Caption, so that section keeps its two visual slides.
        Assert.Equal(5, slides.Count);
        Assert.Null(PlaceholderOf(slides[4].Slide!.Descendants<P.Picture>().Single()));
    }

    [Fact]
    public void ATextPartThatDoesNotFitItsPlaceholder_IsShrunk_AndReportedWhenStillOverfull()
    {
        var image = TestImages.Png("square.png", 100, 100);
        var shortList = string.Concat(Enumerable.Range(1, 9).Select(i => $"- Point {i}\n"));
        var longList = string.Concat(Enumerable.Range(1, 60).Select(i => $"- Point {i} with a few more words\n"));
        var (pptx, result) = Generate(
            $"# Deck\n\n## Short {{layout=\"Picture with Caption\"}}\n\n{shortList}\n![Square](square.png)\n\n## Long {{layout=\"Picture with Caption\"}}\n\n{longList}\n![Square](square.png)\n",
            RolesTemplate.Options,
            image);

        Samples.AssertValid(pptx);
        Assert.Equal([NoteCodes.SlideOverfull], result.Notes.Where(n => n.Code.StartsWith("slide", StringComparison.Ordinal)).Select(n => n.Code));
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(3, slides.Count);
        var shrunk = slides[2].Slide!.Descendants<A.NormalAutoFit>().Single();
        Assert.True(shrunk.FontScale!.Value < 100000);
    }
}
