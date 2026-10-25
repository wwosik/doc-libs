using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

public class TemplateTests
{
    private const string Sections = "# Cover\n\nSubtitle\n\n## Text\n\n- point\n\n# Part two\n\nAbout it\n\n## More\n\nText\n";

    private static readonly TimeProvider Fixed = new FixedTime(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void DefaultTemplate_IsNeutral()
    {
        Assert.IsType<BrandTemplateOptions>(PptxOptions.Default.Template);
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Sections)));

        Assert.Equal("neutral", package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!.Name!.Value);
    }

    [Fact]
    public void BuiltInTemplate_CarriesNothingButDesign()
    {
        using var package = Samples.Open(Samples.TemplateBytes(new BrandTemplateOptions()));

        Assert.Empty(package.PresentationPart!.SlideParts);
        Assert.Null(package.CustomFilePropertiesPart);
        Assert.Null(package.CoreFilePropertiesPart);
        Assert.Null(package.ExtendedFilePropertiesPart);
    }

    [Fact]
    public void NeutralTemplate_HasNoImages()
    {
        using var package = Samples.Open(Samples.TemplateBytes(new BrandTemplateOptions()));

        Assert.Empty(Media(package));
        Assert.Empty(PictureNames(Cover(package)));
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Tables_UseAStyleDefinedInTheTemplate(string template)
    {
        using var package = Samples.Open(Samples.Generate(Samples.Deck(), new PptxOptions { Template = Samples.BuiltIn(template) }));

        var used = package.PresentationPart!.SlideParts.SelectMany(s => s.Slide!.Descendants<A.TableStyleId>()).Select(id => id.Text).Distinct().Single();
        var defined = package.PresentationPart.TableStylesPart!.TableStyleList!.Elements<A.TableStyleEntry>().Select(s => s.StyleId!.Value);
        Assert.Contains(used, defined);
    }

    [Fact]
    public void Deck_WithoutImagesOfItsOwn_IsWellBelowOneMegabyte_WithoutEmbeddedFonts()
    {
        var withFonts = Brand.Neutral with { Name = "plain", Fonts = Samples.Brand.Fonts };
        var pptx = Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = new BrandTemplateOptions { Brand = withFonts, EmbedFonts = false } });
        var embedded = Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = new BrandTemplateOptions { Brand = withFonts } });

        Assert.True(pptx.Length < 600_000, $"{pptx.Length} bytes");
        Assert.True(embedded.Length < 1_500_000, $"{embedded.Length} bytes with the four faces of Liberation Serif");
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void FirstHeading_IsTheCover_LaterOnesAreSectionDividers(string template)
    {
        var pptx = Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = Samples.BuiltIn(template) });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(
            [P.SlideLayoutValues.Title, P.SlideLayoutValues.Object, P.SlideLayoutValues.SectionHeader, P.SlideLayoutValues.Object],
            slides.Select(s => s.SlideLayoutPart!.SlideLayout!.Type!.Value));
        Assert.Equal("Part two", Samples.PlaceholderText(slides[2], P.PlaceholderValues.Title));
        Assert.Contains("About it", slides[2].Slide!.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateWithoutSectionLayout_UsesTheTitleLayoutForDividers()
    {
        var edited = Samples.EditedTemplate(new BrandTemplateOptions(), master => Samples.RemoveLayout(master, P.SlideLayoutValues.SectionHeader));

        var pptx = Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(edited)) });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(P.SlideLayoutValues.Title, Samples.Slides(package)[2].SlideLayoutPart!.SlideLayout!.Type!.Value);
    }

    [Fact]
    public void SectionLayoutWithoutTitlePlaceholder_TakesTheTitleInItsBody_AndTheSubtitleBelow()
    {
        var edited = Samples.EditedTemplate(new BrandTemplateOptions(), master =>
        {
            var section = master.SlideLayoutParts.Single(l => l.SlideLayout!.Type!.Value == P.SlideLayoutValues.SectionHeader).SlideLayout!;
            section.Descendants<P.Shape>().Single(s => s.Descendants<P.PlaceholderShape>().Any(p => p.Type?.Value == P.PlaceholderValues.Title)).Remove();
        });

        var pptx = Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(edited)) });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[2];
        Assert.Equal("Part two", Samples.PlaceholderText(slide, P.PlaceholderValues.Body));
        var subtitle = slide.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "About it");
        var body = slide.SlideLayoutPart!.SlideLayout!.Descendants<P.Shape>().Single(s => s.Descendants<P.PlaceholderShape>().Any(p => p.Type?.Value == P.PlaceholderValues.Body));
        Assert.True(subtitle.ShapeProperties!.Transform2D!.Offset!.Y!.Value > body.ShapeProperties!.Transform2D!.Offset!.Y!.Value);
    }

    [Fact]
    public void EverySlideButTheCover_ShowsItsNumberAndTheDate()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Sections), new PptxOptions { TimeProvider = Fixed }));
        var slides = Samples.Slides(package);

        Assert.Null(Samples.PlaceholderText(slides[0], P.PlaceholderValues.SlideNumber));
        Assert.Null(Samples.PlaceholderText(slides[0], P.PlaceholderValues.DateAndTime));
        Assert.Equal(["2", "3", "4"], slides[1..].Select(s => Samples.PlaceholderText(s, P.PlaceholderValues.SlideNumber)));
        Assert.All(slides[1..], s => Assert.Equal("09.03.2026", Samples.PlaceholderText(s, P.PlaceholderValues.DateAndTime)));
        Assert.Equal("slidenum", slides[1].Slide!.Descendants<A.Field>().Single().Type!.Value);
    }

    [Fact]
    public void ShowDateFalse_LeavesTheDateOut()
    {
        var options = new BrandTemplateOptions { ShowDate = false };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = options }));

        Assert.All(Samples.Slides(package), s => Assert.Null(Samples.PlaceholderText(s, P.PlaceholderValues.DateAndTime)));
        Assert.Equal("2", Samples.PlaceholderText(Samples.Slides(package)[1], P.PlaceholderValues.SlideNumber));
    }

    [Fact]
    public void CallerTemplate_NeverGetsADate()
    {
        var template = Samples.TemplateBytes(new BrandTemplateOptions());

        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(template)) }));

        Assert.All(Samples.Slides(package), s => Assert.Null(Samples.PlaceholderText(s, P.PlaceholderValues.DateAndTime)));
    }

    [Fact]
    public void LayoutNames_PickLayoutsInATemplateWithoutLayoutTypes()
    {
        var untyped = Samples.EditedTemplate(new BrandTemplateOptions(), master =>
        {
            foreach (var layout in master.SlideLayoutParts)
            {
                layout.SlideLayout!.Type = null;
                layout.SlideLayout.CommonSlideData!.Name = "Layout " + layout.SlideLayout.CommonSlideData.Name!.Value;
            }
        });
        var options = new PptxOptions
        {
            Template = new CustomTemplateOptions(new MemoryStream(untyped))
            {
                Layouts = new PptxLayoutNames
                {
                    Title = "Layout Title Slide",
                    Content = "Layout Title and Content",
                    TitleOnly = "Layout Title Only",
                    SectionHeader = "Layout Section Header",
                },
            },
        };

        var pptx = Samples.Generate(Samples.Deck(), options);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(
            ["Layout Title Slide", "Layout Title and Content", "Layout Title Only", "Layout Title Only", "Layout Title Only",
                "Layout Title Only", "Layout Title and Content", "Layout Section Header", "Layout Title and Content"],
            Samples.Slides(package).Select(Samples.LayoutName));
    }

    [Fact]
    public void UnknownLayoutName_ThrowsListingTheLayouts()
    {
        var template = Samples.TemplateBytes(new BrandTemplateOptions());
        var options = new PptxOptions
        {
            Template = new CustomTemplateOptions(new MemoryStream(template)) { Layouts = new PptxLayoutNames { Content = "Titel und Inhalt" } },
        };

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n"), options));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains("'Titel und Inhalt'", error.Reason, StringComparison.Ordinal);
        Assert.Contains("'Title and Content'", error.Reason, StringComparison.Ordinal);
    }

    private static List<ImagePart> Media(PresentationDocument package) =>
        package.GetAllParts().OfType<ImagePart>().Distinct().ToList();

    private static SlideLayoutPart Cover(PresentationDocument package) =>
        package.PresentationPart!.SlideMasterParts.Single().SlideLayoutParts.Single(l => l.SlideLayout!.Type!.Value == P.SlideLayoutValues.Title);

    private static List<string> PictureNames(SlideLayoutPart layout) =>
        layout.SlideLayout!.Descendants<P.Picture>().Select(p => p.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name!.Value!).ToList();

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
