using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// The classification marking (E03-S04) in decks: a text box named <c>Classification</c> in the top right corner of
/// every slide, cover, section, closing and dark slides included, and <c>cp:contentStatus</c>; no watermark.
/// </summary>
public class ClassificationMarkingTests
{
    private const string Deck = """
        ---
        {0}
        ---
        # Network expansion

        Status report

        # Progress

        ## Summary

        The upgrade is on schedule.

        ## Dark {{.dark}}

        ::: statement
        On track.
        :::

        ## Table

        | Site | Done |
        | ---- | ---: |
        | North | 8 |

        # Thank you {{.closing}}
        """;

    private static MarkdownDocument Markdown(string frontMatter) => Samples.Markdown(string.Format(System.Globalization.CultureInfo.InvariantCulture, Deck, frontMatter));

    private static PptxTemplateOptions Template(string name) => name switch
    {
        "custom" => new CustomTemplateOptions(new MemoryStream(Samples.TemplateBytes(Samples.BuiltIn("neutral")))),
        "dark" => new BrandTemplateOptions { Brand = DarkTemplate.Brand },
        _ => Samples.BuiltIn(name),
    };

    public static TheoryData<string> Templates => ["neutral", "sample", "custom", "dark"];

    private static P.Shape? Label(SlidePart slide) =>
        slide.Slide!.CommonSlideData!.ShapeTree!.Elements<P.Shape>().SingleOrDefault(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value == "Classification");

    private static string? Status(PresentationDocument package)
    {
        using var stream = package.CoreFilePropertiesPart!.GetStream();
        return XDocument.Load(stream).Root!.Element((XNamespace)"http://schemas.openxmlformats.org/package/2006/metadata/core-properties" + "contentStatus")?.Value;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EverySlide_HasTheLabelOnTop_InTheTopRightCorner(string template)
    {
        var pptx = Samples.Generate(Markdown("classification: confidential\ndraft: true"), new PptxOptions { Template = Template(template) });

        Samples.AssertValid(pptx);
        Samples.WriteForReview($"classification-{template}.pptx", pptx);
        using var package = Samples.Open(pptx);
        var size = package.PresentationPart!.Presentation!.SlideSize!;
        var slides = Samples.Slides(package);
        Assert.True(slides.Count >= 6);
        Assert.All(slides, slide =>
        {
            var label = Label(slide);
            Assert.NotNull(label);
            Assert.Same(label, slide.Slide!.CommonSlideData!.ShapeTree!.ChildElements.Last());
            Assert.True(label.NonVisualShapeProperties!.NonVisualShapeDrawingProperties!.TextBox!.Value);
            var run = label.Descendants<A.Run>().Single();
            Assert.Equal("Confidential · Draft", run.Text!.Text);
            Assert.True(run.RunProperties!.Bold!.Value);
            Assert.Equal(1067, run.RunProperties.FontSize!.Value);
            Assert.Equal("en", run.RunProperties.Language!.Value);
            Assert.Equal(A.TextAlignmentTypeValues.Right, label.Descendants<A.ParagraphProperties>().Single().Alignment!.Value);
            var offset = label.ShapeProperties!.Transform2D!.Offset!;
            var extents = label.ShapeProperties.Transform2D.Extents!;
            Assert.True(offset.X!.Value > size.Cx!.Value / 2, "right half");
            Assert.True(offset.Y!.Value < size.Cy!.Value / 4, "at the top");
            Assert.True(offset.X.Value + extents.Cx!.Value < size.Cx.Value, "inside the slide");
        });
        Assert.Equal("Confidential · Draft", Status(package));
    }

    [Fact]
    public void OnTheBuiltInCover_TheLabelIsBelowTheLogo()
    {
        using var package = Samples.Open(Samples.Generate(Markdown("classification: internal"), new PptxOptions { Template = Samples.BuiltIn("sample") }));

        var slides = Samples.Slides(package);
        var cover = slides[0];
        var logo = cover.SlideLayoutPart!.SlideLayout!.Descendants<P.Picture>().Single(p => p.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name == "Logo");
        var logoBottom = logo.ShapeProperties!.Transform2D!.Offset!.Y!.Value + logo.ShapeProperties.Transform2D.Extents!.Cy!.Value;
        Assert.True(Label(cover)!.ShapeProperties!.Transform2D!.Offset!.Y!.Value >= logoBottom);
        Assert.True(Label(slides[2])!.ShapeProperties!.Transform2D!.Offset!.Y!.Value < logoBottom / 2, "content slides hang it from the top");
    }

    [Fact]
    public void TheColour_IsDangerWhenConfidential_TheFooterColourOtherwise_WhiteOnDarkSlides()
    {
        static string Colour(SlidePart slide) => Label(slide)!.Descendants<A.RgbColorModelHex>().Single().Val!.Value!;

        var brand = ResolvedBrand.ForSlides(Samples.Brand);
        using (var confidential = Samples.Open(Samples.Generate(Markdown("classification: confidential"), new PptxOptions { Template = Samples.BuiltIn("sample") })))
        {
            var slides = Samples.Slides(confidential);
            Assert.Equal(brand.Labels.Danger, Colour(slides[0]));
            Assert.Equal(brand.Labels.Danger, Colour(slides[2]));
            Assert.Equal(ResolvedBrand.Mix(brand.Labels.Danger, 0.5), Colour(slides[1]));
        }

        using var draft = Samples.Open(Samples.Generate(Markdown("draft: true"), new PptxOptions { Template = Samples.BuiltIn("sample") }));
        var drafts = Samples.Slides(draft);
        Assert.Equal(brand.Footer, Colour(drafts[0]));
        Assert.Equal(brand.Footer, Colour(drafts[2]));
        Assert.Equal("FFFFFF", Colour(drafts[1]));
        Assert.Equal("FFFFFF", Colour(drafts[3]));
        Assert.Equal("FFFFFF", Colour(drafts[^1]));
        Assert.Equal("Draft", Label(drafts[0])!.InnerText);
    }

    [Fact]
    public void German_AndHostTexts_NameTheLevels()
    {
        using (var german = Samples.Open(Samples.Generate(Markdown("classification: confidential\ndraft: true\nlang: de"))))
        {
            Assert.Equal("Vertraulich · Entwurf", Label(Samples.Slides(german)[0])!.InnerText);
            Assert.Equal("de", Label(Samples.Slides(german)[0])!.Descendants<A.RunProperties>().Single().Language!.Value);
        }

        using (var internalDeck = Samples.Open(Samples.Generate(Markdown("classification: internal\nlang: de"))))
        {
            Assert.Equal("Intern", Label(Samples.Slides(internalDeck)[0])!.InnerText);
            Assert.Equal("Intern", Status(internalDeck));
        }

        var options = new PptxOptions
        {
            Texts = new Dictionary<string, GeneratedTexts> { ["en"] = GeneratedTexts.English with { Confidential = "Restricted" } },
        };
        using var host = Samples.Open(Samples.Generate(Markdown("classification: confidential"), options));
        Assert.Equal("Restricted", Label(Samples.Slides(host)[0])!.InnerText);
    }

    [Fact]
    public void TheHostsMarking_AppliesAndFrontMatterCannotLowerIt()
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Markdown("classification: public\ndraft: false"), output, new PptxOptions { Classification = Classification.Confidential, Draft = true });

        using var package = Samples.Open(output.ToArray());
        Assert.All(Samples.Slides(package), s => Assert.Equal("Confidential · Draft", Label(s)!.InnerText));
        var note = Assert.Single(result.Notes, n => n.Code == NoteCodes.ClassificationKept);
        Assert.Equal(2, note.Line);

        using var raised = Samples.Open(Samples.Generate(Markdown("classification: confidential"), new PptxOptions { Classification = Classification.Internal }));
        Assert.Equal("Confidential", Label(Samples.Slides(raised)[0])!.InnerText);
    }

    [Fact]
    public void AnUnknownValue_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Markdown("classification: secret")));

        Assert.Contains("internal", error.Message, StringComparison.Ordinal);
        Assert.Throws<DocumentConversionException>(() => Samples.Generate(Markdown("draft: maybe")));
    }

    [Fact]
    public void WithoutAMarking_NoLabelAndNoStatus()
    {
        using var package = Samples.Open(Samples.Generate(Markdown("classification: public")));

        Assert.All(Samples.Slides(package), s => Assert.Null(Label(s)));
        Assert.Null(Status(package));
    }
}
