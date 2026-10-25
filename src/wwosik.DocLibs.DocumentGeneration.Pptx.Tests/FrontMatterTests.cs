using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

public class FrontMatterTests
{
    [Fact]
    public void Block_MakesNoSlide()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\nshowDate: false\n---\n# Title\n")));
        Assert.Single(Samples.Slides(package));
    }

    [Fact]
    public void Keys_OverrideTheCallersValues()
    {
        var options = new PptxOptions { Template = new BrandTemplateOptions { ShowDate = true } };

        var template = Apply("---\nShowDate: FALSE\n---\n", options.Template);

        Assert.Equal(new BrandTemplateOptions { ShowDate = false }, template);
    }

    [Fact]
    public void TemplateKey_NamingTheCallersTemplate_KeepsItsValues()
    {
        var template = Apply("---\ntemplate: Neutral\n---\n", new BrandTemplateOptions { ShowDate = false });

        Assert.Equal(new BrandTemplateOptions { ShowDate = false }, template);
    }

    [Fact]
    public void TemplateKey_NamingAnotherTemplate_StartsFromItsDefaults_ThenAppliesTheKeys()
    {
        var custom = new CustomTemplateOptions(new MemoryStream()) { Layouts = new PptxLayoutNames { Title = "Cover" } };

        var template = Apply("---\nshowDate: false\ntemplate: neutral\n---\n", custom);

        Assert.Equal(new BrandTemplateOptions { ShowDate = false }, template);
    }

    [Fact]
    public void TemplateKey_ReplacesACallersTemplate_WhichIsNotRead()
    {
        var options = new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream("not a template"u8.ToArray())) };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\ntemplate: neutral\n---\n## A\n"), options));

        Assert.Equal("neutral", package.PresentationPart!.SlideMasterParts.Single().ThemePart!.Theme!.Name!.Value);
    }

    [Fact]
    public void LayoutKeys_NameTheLayoutsOfACallersTemplate()
    {
        var template = Samples.TemplateBytes(new BrandTemplateOptions());
        var options = new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(template)) };

        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\nlayouts.content: Title Only\n---\n## A\n\nText\n"), options));

        Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(package)[0]));
    }

    [Fact]
    public void TitleAndAuthor_WinOverTheMetadata()
    {
        var document = Samples.Deck() with { Markdown = "---\ntitle: From front matter\nauthor: Team Docs\n---\n" + Samples.Deck().Markdown };

        using var package = Samples.Open(Samples.Generate(document));

        Assert.Equal("From front matter", package.PackageProperties.Title);
        Assert.Equal("Team Docs", package.PackageProperties.Creator);
    }

    [Fact]
    public void ShowDate_FromFrontMatter_ReachesTheSlides()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\nshowDate: false\n---\n## A\n")));

        Assert.Null(Samples.PlaceholderText(Samples.Slides(package)[0], P.PlaceholderValues.DateAndTime));
    }

    [Theory]
    [InlineData("---\ncolour: blue\n---\n", "'colour' is not supported by the template 'neutral'. Valid keys: template, title, author, showDate, showTagline, embedFonts, listBullets.")]
    [InlineData("---\nshowLogo: true\n---\n", "'showLogo' is not supported by the template 'neutral'")]
    [InlineData("---\nlayouts.title: Cover\n---\n", "'layouts.title' is not supported by the template 'neutral'")]
    [InlineData("---\nshowDate: yes\n---\n", "'showDate' has the value 'yes'; expected 'true' or 'false'")]
    [InlineData("---\ntemplate: corporate\n---\n", "'template' has the value 'corporate'; expected 'neutral'")]
    [InlineData("---\ntitle: a\nTitle: b\n---\n", "'Title' appears twice")]
    [InlineData("---\ntitle: a\njust text\n---\n", "'just text' is not 'key: value'")]
    [InlineData("---\ntitle:\n---\n", "'title' has no value")]
    [InlineData("---\ntitle: a\n## Slide\n", "never closed")]
    public void Mistakes_ThrowNamingTheKey(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown + "## Slide\n")));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains(expected, error.Reason, StringComparison.Ordinal);
    }

    private static PptxTemplateOptions Apply(string markdown, PptxTemplateOptions template) =>
        PptxTemplateOptions.Apply(FrontMatter.Read(markdown, Format.Pptx), template);
}
