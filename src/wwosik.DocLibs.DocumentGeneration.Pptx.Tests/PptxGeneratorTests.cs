using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

public class PptxGeneratorTests
{
    private static readonly PptxOptions WithDiagrams = new()
    {
        CodeBlockRenderers = [Samples.Renderer(request => request.Language == "mermaid" ? new ImageGraphic(Samples.Image("photo.png")) : null)],
    };

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_RoundTripsThroughThePptxConverter(string template)
    {
        var pptx = Samples.Generate(Samples.Deck(), WithDiagrams with { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"deck-{template}.pptx", pptx);

        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);

        Samples.AssertMatchesExpected($"deck-{template}.roundtrip", back.Markdown);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsSchemaValid(string template) =>
        Samples.AssertValid(Samples.Generate(Samples.Deck(), WithDiagrams with { Template = Samples.BuiltIn(template) }));

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void BuiltInTemplate_IsSchemaValid(string template) => Samples.AssertValid(Samples.TemplateBytes(Samples.BuiltIn(template)));

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_UsesALayoutPerKindOfSlide(string template)
    {
        using var package = Samples.Open(Samples.Generate(Samples.Deck(), WithDiagrams with { Template = Samples.BuiltIn(template) }));

        var layouts = Samples.Slides(package).Select(s => s.SlideLayoutPart!.SlideLayout!.Type!.Value).ToList();

        Assert.Equal(
            [
                P.SlideLayoutValues.Title,      // # Quarterly review
                P.SlideLayoutValues.Object,     // ## Agenda
                P.SlideLayoutValues.TitleOnly,  // ## Results (table)
                P.SlideLayoutValues.TitleOnly,  // ## Diagram
                P.SlideLayoutValues.TitleOnly,  // ## Two pictures, first
                P.SlideLayoutValues.TitleOnly,  // ## Two pictures, continuation
                P.SlideLayoutValues.TitleOnly,  // ## Flow (rendered diagram)
                P.SlideLayoutValues.SectionHeader, // # Outlook
                P.SlideLayoutValues.Object,     // ## Closing
            ],
            layouts);
    }

    [Fact]
    public void CallerTemplate_ItsOwnSlidesAreReplaced()
    {
        var template = Samples.Generate(Samples.Deck(), WithDiagrams);

        var pptx = Samples.Generate(
            Samples.Markdown("# Title\n\n## Only slide\n\n- point\n"),
            new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(template)) });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(2, Samples.Slides(package).Count);
        Assert.Empty(package.PresentationPart!.SlideParts.SelectMany(s => s.ImageParts));
    }

    [Fact]
    public void TemplateWithoutTitleOnlyLayout_StillGetsImageAndTableSlides()
    {
        var edited = Samples.EditedTemplate(new BrandTemplateOptions(), master => Samples.RemoveLayout(master, P.SlideLayoutValues.TitleOnly));

        var pptx = Samples.Generate(Samples.Deck(), WithDiagrams with { Template = new CustomTemplateOptions(new MemoryStream(edited)) });

        Samples.AssertValid(pptx);
        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);
        Assert.Contains("## Diagram\n\nThe chart below shows the trend.\n\n![Bar chart]", back.Markdown, StringComparison.Ordinal);
        Assert.Contains("| North | 1,200 | +4 % |", back.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void PotxTemplate_BecomesAPresentation()
    {
        using var template = new MemoryStream();
        template.Write(Samples.TemplateBytes(new BrandTemplateOptions()));
        using (var package = PresentationDocument.Open(template, true))
        {
            package.ChangeDocumentType(PresentationDocumentType.Template);
        }

        template.Position = 0;
        var pptx = Samples.Generate(Samples.Markdown("## Slide\n\nText\n"), new PptxOptions { Template = new CustomTemplateOptions(template) });

        using var result = Samples.Open(pptx);
        Assert.Equal(PresentationDocumentType.Presentation, result.DocumentType);
        Samples.AssertValid(pptx);
    }

    [Fact]
    public void Lists_KeepTheirLevelsAndNumbering()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Lists\n\n- a\n  - b\n\n3. c\n4. d\n\nplain\n")));

        var paragraphs = Body(Samples.Slides(package)[0]).Elements<A.Paragraph>().ToList();

        Assert.Equal(["a", "b", "c", "d", "plain"], paragraphs.Select(p => p.InnerText));
        Assert.Null(paragraphs[0].ParagraphProperties!.Level);
        Assert.Equal(1, paragraphs[1].ParagraphProperties!.Level!.Value);
        Assert.All(paragraphs[2..4], p => Assert.Equal(3, p.ParagraphProperties!.GetFirstChild<A.AutoNumberedBullet>()!.StartAt!.Value));
        Assert.NotNull(paragraphs[4].ParagraphProperties!.GetFirstChild<A.NoBullet>());
        Assert.Equal(0, paragraphs[4].ParagraphProperties!.LeftMargin!.Value);
    }

    [Fact]
    public void InlineMarkdown_BecomesRunFormatting()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Inline\n\n**b** *i* `c` ~~s~~ [l](https://example.com/x)\n")));
        var slide = Samples.Slides(package)[0];

        var runs = Body(slide).Descendants<A.Run>().Where(r => r.InnerText.Trim().Length > 0).ToDictionary(r => r.InnerText.Trim(), r => r.RunProperties!);

        Assert.True(runs["b"].Bold!.Value);
        Assert.True(runs["i"].Italic!.Value);
        Assert.Equal("Consolas", runs["c"].GetFirstChild<A.LatinFont>()!.Typeface!.Value);
        Assert.Equal(A.TextStrikeValues.SingleStrike, runs["s"].Strike!.Value);
        var link = runs["l"].GetFirstChild<A.HyperlinkOnClick>()!.Id!.Value!;
        Assert.Equal(new Uri("https://example.com/x"), slide.HyperlinkRelationships.Single(h => h.Id == link).Uri);
    }

    [Fact]
    public void Images_FillTheirRoom_UpToTheScaleInTheirTitle()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Deck(), WithDiagrams));
        var slides = Samples.Slides(package);

        // diagram.png is 480 px wide at scale=1.5, photo.png 20 px at scale=8; 9525 EMU per pixel.
        Assert.InRange(PictureWidth(slides[4]), (480 * 9525) + 1, (long)(480 * 1.5 * 9525));
        Assert.Equal(160 * 9525, PictureWidth(slides[5]));
    }

    [Fact]
    public void AnImageWithoutScale_IsEnlargedToItsRoom_UpToMaxImageUpscale()
    {
        var markdown = Samples.Markdown("## Picture\n\n![Chart](photo.png)\n", Samples.Image("photo.png"));

        using var enlarged = Samples.Open(Samples.Generate(markdown));
        using var natural = Samples.Open(Samples.Generate(markdown, new PptxOptions { MaxImageUpscale = 1 }));

        Assert.Equal(20 * 4 * 9525, PictureWidth(Samples.Slides(enlarged)[0])); // 20 px, enlarged 4 times
        Assert.Equal(20 * 9525, PictureWidth(Samples.Slides(natural)[0]));
    }

    [Fact]
    public void CodeBlockRenderers_GetLanguageAndCode_AndNullKeepsTheCode()
    {
        var calls = new List<(string Language, string Code)>();
        var options = new PptxOptions
        {
            CodeBlockRenderers =
            [
                Samples.Renderer(request =>
                {
                    calls.Add((request.Language, request.Code));
                    return null;
                }),
            ],
        };

        var pptx = Samples.Generate(Samples.Markdown("## Code\n\n```mermaid\ngraph TD;\n  A-->B;\n```\n"), options);

        Assert.Equal([("mermaid", "graph TD;\n  A-->B;")], calls);
        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);
        Assert.Equal("## Code\n\ngraph TD;\n\nA-->B;\n", back.Markdown);
    }

    [Fact]
    public void Drawings_BecomeAGroupOfEditableShapes()
    {
        var drawing = new Drawing(200, 100,
        [
            new DrawingShape(DrawingPath.Rectangle(0, 0, 100, 50), "E6F4F5", "0E7C86", 1) { Kind = DrawingShapeKind.RoundedRectangle, CornerRadius = 10 },
            new DrawingShape(new DrawingPath([new MoveTo(100, 25), new CubicTo(150, 25, 150, 75, 200, 75)]), null, "6B7280", 2) { Dash = [4, 2], RoundJoins = true },
            new DrawingText(50, 30, [new DrawingSpan("Bold", Bold: true), new DrawingSpan(" node")], 16, "1F2937") { Anchor = TextAnchor.Middle },
        ]);
        var options = new PptxOptions { CodeBlockRenderers = [Samples.Renderer(request => new DrawingGraphic(drawing))] };

        var pptx = Samples.Generate(Samples.Markdown("## Diagram\n\nAbove\n\n```x\ny\n```\n\nCaption\n"), options);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package).Single().Slide!;
        var group = Assert.Single(slide.CommonSlideData!.ShapeTree!.Elements<P.GroupShape>());
        var shapes = group.Elements<P.Shape>().ToList();
        Assert.Equal(3, shapes.Count);

        var node = shapes[0].ShapeProperties!;
        Assert.Equal(A.ShapeTypeValues.RoundRectangle, node.GetFirstChild<A.PresetGeometry>()!.Preset!.Value);
        Assert.Equal("val 20000", node.GetFirstChild<A.PresetGeometry>()!.AdjustValueList!.GetFirstChild<A.ShapeGuide>()!.Formula!.Value); // 10 of 50
        Assert.Equal("E6F4F5", node.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);

        var edge = shapes[1].ShapeProperties!;
        Assert.NotNull(edge.GetFirstChild<A.CustomGeometry>()!.Descendants<A.CubicBezierCurveTo>().SingleOrDefault());
        Assert.Equal(A.PathFillModeValues.None, edge.Descendants<A.Path>().Single().Fill!.Value);
        Assert.Equal([200000, 100000], edge.GetFirstChild<A.Outline>()!.Descendants<A.DashStop>().SelectMany(d => new[] { d.DashLength!.Value, d.SpaceLength!.Value }));
        Assert.NotNull(edge.GetFirstChild<A.Outline>()!.GetFirstChild<A.Round>());

        var runs = shapes[2].Descendants<A.Run>().ToList();
        Assert.Equal(["Bold", " node"], runs.Select(r => r.Text!.Text));
        Assert.Equal([true, false], runs.Select(r => r.RunProperties!.Bold?.Value ?? false));
        Assert.Equal(1800, runs[0].RunProperties!.FontSize!.Value); // 16 px enlarged by half: 18 pt
        Assert.Equal(A.TextAlignmentTypeValues.Center, shapes[2].Descendants<A.ParagraphProperties>().Single().Alignment!.Value);

        var text = string.Join("|", slide.Descendants<A.Text>().Select(t => t.Text));
        Assert.Contains("Above", text, StringComparison.Ordinal);
        Assert.Contains("Caption", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeDrawings_AreScaledDownWithTheirText()
    {
        var drawing = new Drawing(4000, 100, [new DrawingText(0, 50, "Wide", 16, "1F2937")]);
        var options = new PptxOptions { CodeBlockRenderers = [Samples.Renderer(request => new DrawingGraphic(drawing))] };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Diagram\n\n```x\ny\n```\n"), options));

        var group = Samples.Slides(package).Single().Slide!.Descendants<P.GroupShape>().Single();
        var extents = group.GroupShapeProperties!.TransformGroup!.Extents!;
        Assert.True(extents.Cx!.Value < 4000 * 9525, "Scaled down to the content area.");
        var points = group.Descendants<A.RunProperties>().Single().FontSize!.Value / 100.0;
        Assert.Equal(12.0 * extents.Cx.Value / (4000 * 9525), points, 1);
    }

    [Fact]
    public void CodeBlockRenderers_GetTheBrandAndTheLineAfterFrontMatter()
    {
        CodeBlockRequest? seen = null;
        var options = new PptxOptions { Brands = [Samples.Brand], CodeBlockRenderers = [Samples.Renderer(request => { seen = request; return null; })] };

        Samples.Generate(Samples.Markdown("---\ntemplate: sample\n---\n## A\n\n```mermaid\ngraph TD;\n```\n"), options);

        Assert.Equal(6, seen!.Line);
        Assert.Same(Samples.Brand, seen.Brand);
    }

    [Fact]
    public void Metadata_GoesIntoTheCoreProperties()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Deck(), WithDiagrams));

        Assert.Equal("Quarterly review", package.PackageProperties.Title);
        Assert.Equal("DocLibs", package.PackageProperties.Creator);
    }

    [Fact]
    public void EmptyMarkdown_GivesAValidDeckWithoutSlides()
    {
        var pptx = Samples.Generate(Samples.Markdown(string.Empty));

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Empty(Samples.Slides(package));
    }

    [Fact]
    public void MissingImage_ThrowsNamingThePath()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n\n![x](images/missing.png)\n")));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains("images/missing.png", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedImage_Throws()
    {
        var resource = new DocumentResource("a.svg", "image/svg+xml", "<svg/>"u8.ToArray());

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n\n![x](a.svg)\n", resource)));

        Assert.Contains("not a PNG, JPEG or GIF", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DamagedTemplate_Throws()
    {
        var options = new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream("PK\x03\x04 broken"u8.ToArray())) };

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n"), options));

        Assert.Equal(Format.Pptx, error.Format);
    }

    private static P.TextBody Body(SlidePart slide) =>
        slide.Slide!.Descendants<P.Shape>()
            .Single(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape is { Type: null, Index: not null })
            .TextBody!;

    private static long PictureWidth(SlidePart slide) =>
        slide.Slide!.Descendants<P.Picture>().Single().ShapeProperties!.Transform2D!.Extents!.Cx!.Value;
}
