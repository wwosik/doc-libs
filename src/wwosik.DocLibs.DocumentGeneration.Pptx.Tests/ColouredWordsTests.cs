using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><c>[words]{.success}</c>: words in a status colour.</summary>
public class ColouredWordsTests
{
    [Fact]
    public void Words_TakeTheirCategorysColour_Bold_WithoutTheirBrackets()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## S\n\nStatus: [on **schedule**]{.success} and [watch closely]{.warning}.\n")));
        var runs = Samples.Slides(package).Single().Slide!.Descendants<DocumentFormat.OpenXml.Presentation.Shape>()
            .Single(s => s.InnerText.StartsWith("Status", StringComparison.Ordinal)).Descendants<A.Run>().ToList();

        Assert.Equal("Status: on schedule and watch closely.", string.Concat(runs.Select(r => r.Text!.Text)));
        var labels = ResolvedBrand.Neutral.Labels;
        Assert.All(runs.Where(r => r.Text!.Text is "on " or "schedule"), r => Assert.Equal(labels.Success, Colour(r)));
        Assert.All(runs.Where(r => r.Text!.Text is "on " or "schedule" or "watch closely"), r => Assert.True(r.RunProperties!.Bold!.Value));
        Assert.Equal(labels.Warning, Colour(runs.Single(r => r.Text!.Text == "watch closely")));
        Assert.Null(runs.Single(r => r.Text!.Text == "Status: ").RunProperties!.GetFirstChild<A.SolidFill>());
    }

    [Theory]
    [InlineData("[a] {.success}")]
    [InlineData("[a]{.success extra}")]
    [InlineData("`[a]{.success}`")]
    [InlineData("[a](https://example.com){.x}")]
    public void OtherBracketsAndBraces_StayText(string text)
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown($"## S\n\n{text}\n")));

        Assert.All(Samples.Slides(package).Single().Slide!.Descendants<A.Run>(), r => Assert.Null(r.RunProperties!.GetFirstChild<A.SolidFill>()));
    }

    [Fact]
    public void AnUnknownColour_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## S\n\n[late]{.red}\n")));

        Assert.Contains("'[late]{.red}' names no colour. Valid: {.success}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnDarkSlides_TheColourIsLightened()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## S {kicker=\"K\"}\n\n[on schedule]{.success}\n"), DarkTemplate.Options));

        var run = Samples.Slides(package).Single().Slide!.Descendants<A.Run>().Single(r => r.Text!.Text == "on schedule");
        Assert.Equal(ResolvedBrand.Mix(ResolvedBrand.ForSlides(DarkTemplate.Brand).Labels.Success, 0.5), Colour(run));
    }

    private static string? Colour(A.Run run) => run.RunProperties!.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value;
}
