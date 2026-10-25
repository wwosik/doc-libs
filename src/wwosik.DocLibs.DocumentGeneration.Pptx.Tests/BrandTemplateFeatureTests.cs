using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>A brand's own template: its footer, its layouts by name, its closing layout, and bullets on level 1.</summary>
public class BrandTemplateFeatureTests
{
    private const string Deck = "# Cover\n\n## Points\n\n- One\n- Two\n\n# Thank you {.closing}\n\nQuestions welcome\n";

    [Fact]
    public void FooterText_GoesIntoTheFooterPlaceholder_ButNotOnCoverOrClosing()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Deck), DarkTemplate.Options));
        var slides = Samples.Slides(package);

        Assert.Equal([null, "Made-up Corporation", null], slides.Select(s => Samples.PlaceholderText(s, P.PlaceholderValues.Footer)));
    }

    [Fact]
    public void ShowFooterFalse_LeavesItEmpty()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\nshowFooter: false\n---\n" + Deck), DarkTemplate.Options));

        Assert.All(Samples.Slides(package), s => Assert.Null(Samples.PlaceholderText(s, P.PlaceholderValues.Footer)));
    }

    [Fact]
    public void Closing_UsesTheClosingLayout_FoundByName_OrNamedByTheBrand()
    {
        using var byName = Samples.Open(Samples.Generate(Samples.Markdown(Deck), DarkTemplate.Options));
        var named = DarkTemplate.Brand with { Pptx = DarkTemplate.Brand.Pptx! with { ClosingLayout = DarkTemplate.TwoLineLayout } };
        using var byBrand = Samples.Open(Samples.Generate(Samples.Markdown(Deck), new PptxOptions { Template = new BrandTemplateOptions { Brand = named } }));

        var closing = Samples.Slides(byName)[^1];
        Assert.Equal(DarkTemplate.ClosingLayout, Samples.LayoutName(closing));
        Assert.Equal("Thank you", Samples.PlaceholderText(closing, P.PlaceholderValues.CenteredTitle));
        Assert.Null(Samples.PlaceholderText(closing, P.PlaceholderValues.SlideNumber));
        Assert.Equal(DarkTemplate.TwoLineLayout, Samples.LayoutName(Samples.Slides(byBrand)[^1]));
    }

    [Fact]
    public void LayoutAttribute_PicksTheTemplatesLayout_ForPlainAndFlowSlides()
    {
        var markdown = $"## A long title over two lines {{layout=\"{DarkTemplate.TwoLineLayout.ToLowerInvariant()}\"}}\n\n- Text\n\n## Flow {{kicker=\"K\" layout=\"{DarkTemplate.TwoLineLayout}\"}}\n\n::: panel\nBox\n:::\n";
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(markdown), DarkTemplate.Options));
        var slides = Samples.Slides(package);

        Assert.All(slides, s => Assert.Equal(DarkTemplate.TwoLineLayout, Samples.LayoutName(s)));
        Assert.Equal("A long title over two lines", Samples.PlaceholderText(slides[0], P.PlaceholderValues.Title));
    }

    [Theory]
    [InlineData("## T {layout=\"Nope\"}\n\nText\n", "brand-dark", "The template has no layout named 'Nope'")]
    [InlineData("## T {layout=\"Title Only\"}\n\nText\n", "neutral", "the built-in design chooses its layouts itself")]
    [InlineData("## T {.closing}\n\nText\n", "brand-dark", "{.closing} goes on a '#' heading")]
    [InlineData("# T {.dark}\n\nText\n", "brand-dark", "go on '##' headings")]
    public void Mistakes_Throw(string markdown, string template, string message)
    {
        var options = template == "brand-dark" ? DarkTemplate.Options : new PptxOptions { Template = Samples.BuiltIn(template) };

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown), options));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("---\nlistBullets: always\n---\n", true)]
    public void ListBulletsAlways_WritesBulletsOnLevelOne(string frontMatter, bool bullets)
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(frontMatter + "## Points\n\n- One\n- Two\n"), DarkTemplate.Options));

        var paragraphs = Samples.Slides(package).Single().Slide!.Descendants<P.Shape>()
            .Single(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape is { } placeholder
                && (placeholder.Type is null || placeholder.Type.Value == P.PlaceholderValues.Body))
            .Descendants<A.Paragraph>().ToList();
        Assert.All(paragraphs, p => Assert.Equal(bullets, p.ParagraphProperties?.GetFirstChild<A.CharacterBullet>() is not null));
    }

    [Fact]
    public void ListBulletsOption_WorksWithoutFrontMatter()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Points\n\n- One\n"), DarkTemplate.Options with { ListBullets = ListBullets.Always }));

        Assert.NotNull(Samples.Slides(package).Single().Slide!.Descendants<A.CharacterBullet>().FirstOrDefault());
    }
}
