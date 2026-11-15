using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// E06-S05: heading attributes are taken off before typography, so a deck has the same slides with and without
/// <c>typography: true</c>, apart from the typeset text.
/// </summary>
public class HeadingAttributeTypographyTests
{
    private static readonly string Markdown = $$"""
        # "Deck" -- start

        ## Status {kicker="Plan -- 2027..."}

        - One
        - Two

        ## Results {transition="fade"}

        - Three

        ## Two "lines" {layout="{{DarkTemplate.TwoLineLayout}}"}

        - Text

        ## Dark {.dark kicker="A"}

        ::: panel
        Box
        :::

        ## Plain {.unnumbered .newpage}

        Text.

        ### Detail {.unnumbered .newpage}

        More.

        # Thanks {.closing}
        """;

    [Theory]
    [InlineData("en", "“Deck” – start", "Plan – 2027…")]
    [InlineData("de", "„Deck“ – start", "Plan – 2027…")]
    public void QuotedAttributes_AreApplied_WithTypographyOn_AndTheSlidesStayTheSame(string language, string cover, string kicker)
    {
        var markdown = $"---\nlang: {language}\n---\n\n" + Markdown;
        using var off = Samples.Open(Samples.Generate(Samples.Markdown(markdown), DarkTemplate.Options));
        using var on = Samples.Open(Samples.Generate(Samples.Markdown(markdown), DarkTemplate.Options with { Typography = true }));

        Assert.Equal(Shape(off), Shape(on));
        var slides = Samples.Slides(on);
        Assert.Equal(cover, Samples.PlaceholderText(slides[0], P.PlaceholderValues.CenteredTitle) ?? Samples.PlaceholderText(slides[0], P.PlaceholderValues.Title));
        Assert.Equal(["Status", "Results", language == "de" ? "Two „lines“" : "Two “lines”", "Dark", "Plain"], slides.Skip(1).Take(5).Select(Title));
        Assert.Contains(kicker, Texts(slides[1]));
        Assert.Contains("Plan -- 2027...", Texts(Samples.Slides(off)[1]));
        Assert.Contains("A", Texts(slides[4]));
        Assert.Equal("fade", slides[2].Slide!.GetFirstChild<P.Transition>()?.FirstChild?.LocalName);
        Assert.Equal(DarkTemplate.TwoLineLayout, Samples.LayoutName(slides[3]));
        Assert.Contains("Detail", Texts(slides[5]));
        Assert.DoesNotContain(Samples.Slides(on).SelectMany(Texts), t => t.Contains('{', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("layouts.md")]
    [InlineData("presenting.md")]
    [InlineData("deck.md")]
    public void TheSamples_HaveTheSameSlidesLayoutsAndTransitions_WithTypographyOn(string sample)
    {
        var markdown = File.ReadAllText(Samples.Path(sample)).ReplaceLineEndings("\n");
        MarkdownDocument Document() => Samples.Markdown(markdown, Samples.Image("diagram.png"), Samples.Image("photo.png"));
        var options = new PptxOptions { CodeBlockRenderers = [new Charts.ChartRenderer()] };
        using var output = new MemoryStream();
        var offResult = PptxGenerator.Generate(Document(), output, options);
        using var off = Samples.Open(output.ToArray());
        using var onOutput = new MemoryStream();
        var onResult = PptxGenerator.Generate(Document(), onOutput, options with { Typography = true });
        using var on = Samples.Open(onOutput.ToArray());

        Assert.Equal(Shape(off), Shape(on));
        Assert.Equal(offResult.Notes.Select(n => n.Code).Where(c => c != NoteCodes.TypographySkipped), onResult.Notes.Select(n => n.Code).Where(c => c != NoteCodes.TypographySkipped));
        Assert.DoesNotContain(Samples.Slides(on).Select(Title), t => t.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void ImageAttributes_BuildMarkersAndContainerArguments_StillWork_WithTypographyOn()
    {
        const string Markdown = """
            ## Photos {kicker="Row"}

            {.build}

            ![One](diagram.png){fit=cover} ![Two](photo.png){.round}

            ## Side by side

            ::: columns 1:2
            The "text" column.

            ---

            ![Chart](diagram.png)
            :::
            """;
        MarkdownDocument Document() => Samples.Markdown(Markdown, Samples.Image("diagram.png"), Samples.Image("photo.png"));
        using var off = Samples.Open(Samples.Generate(Document()));
        using var on = Samples.Open(Samples.Generate(Document(), new PptxOptions { Typography = true }));

        Assert.Equal(Shape(off), Shape(on));
        Assert.Equal(Samples.Slides(off)[0].Slide!.GetFirstChild<P.Timing>()?.OuterXml, Samples.Slides(on)[0].Slide!.GetFirstChild<P.Timing>()?.OuterXml);
        Assert.Equal(Pictures(off), Pictures(on));
        Assert.Contains("The “text” column.", Samples.Slides(on).SelectMany(Texts));
        Assert.DoesNotContain(Samples.Slides(on).SelectMany(Texts), t => t.Contains('{', StringComparison.Ordinal));
    }

    // Per slide: its layout, its transition and how many shapes and pictures it has.
    private static List<string> Shape(PresentationDocument package) => [.. Samples.Slides(package).Select(s =>
        $"{Samples.LayoutName(s)} | {s.Slide!.GetFirstChild<P.Transition>()?.FirstChild?.LocalName} | {s.Slide.Descendants<P.Shape>().Count()} | {s.Slide.Descendants<P.Picture>().Count()}")];

    // Where and how large each picture is, with its crop.
    private static List<string> Pictures(PresentationDocument package) => [.. Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<P.Picture>())
        .Select(p => $"{p.ShapeProperties?.Transform2D?.Offset?.X} {p.ShapeProperties?.Transform2D?.Extents?.Cx} {p.BlipFill?.SourceRectangle?.OuterXml} {p.ShapeProperties?.Descendants<A.PresetGeometry>().FirstOrDefault()?.Preset?.Value}")];

    private static string Title(SlidePart slide) =>
        Samples.PlaceholderText(slide, P.PlaceholderValues.Title) ?? Samples.PlaceholderText(slide, P.PlaceholderValues.CenteredTitle) ?? string.Empty;

    private static IEnumerable<string> Texts(SlidePart slide) =>
        slide.Slide!.Descendants<A.Paragraph>().Select(p => string.Concat(p.Descendants<A.Text>().Select(t => t.Text)));
}
