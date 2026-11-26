using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// Slide lines for editor previews (E07-S07): the Markdown line each slide starts at, from the generator and from the
/// HTML deck drawn from its output. The script's <c>go</c> and <c>shown</c> messages are checked in a browser.
/// </summary>
public class SlideLineTests
{
    private static readonly string Points = string.Concat(Enumerable.Range(1, 30).Select(i => $"- Point {i} with a few more words to fill the slide\n"));

    private static readonly string Markdown =
        "---\ntitle: Harbour plan\nlang: en\n---\n\n# Harbour plan\n\nThe new route\n\n# Chapter one\n\n## Points\n\n"
        + Points
        + "\n## Short\n\nOne line.\n\n::: notes\nSay it.\n:::\n\n# Thank you {.closing}\n\nQuestions welcome\n";

    private static int LineOf(string heading) =>
        Markdown.Split('\n').Select((text, i) => (text, i)).First(l => l.text == heading).i + 1;

    private static (GenerationResult Result, byte[] Pptx) Generate()
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(Markdown), output);
        return (result, output.ToArray());
    }

    private static (string Html, GenerationResult Result) Html(byte[] pptx)
    {
        using var output = new MemoryStream();
        var result = HtmlDeck.Write(new MemoryStream(pptx), output);
        return (Encoding.UTF8.GetString(output.ToArray()), result);
    }

    // The deck with the line taken out of the slides at the given positions (from 0), or of all of them.
    private static byte[] WithoutLines(byte[] pptx, Func<int, bool> which)
    {
        using var edited = new MemoryStream();
        edited.Write(pptx);
        using (var package = PresentationDocument.Open(edited, true))
        {
            var slides = Samples.Slides(package);
            for (var i = 0; i < slides.Count; i++)
            {
                if (which(i))
                {
                    slides[i].Slide!.GetFirstChild<P.SlideExtensionList>()?.Remove();
                }
            }
        }

        return edited.ToArray();
    }

    [Fact]
    public void Generate_ReturnsTheLineOfEverySlide_SplitPartsTheirSourceSlides()
    {
        var (result, _) = Generate();

        Assert.Contains(result.Notes, n => n.Code == NoteCodes.SlideSplit);
        var lines = Assert.IsAssignableFrom<IReadOnlyList<int>>(result.SlideLines);
        Assert.Equal(result.Pages, lines.Count);
        var parts = result.Pages - 4;
        Assert.True(parts >= 2, "the points slide is split");
        Assert.Equal(
            [
                LineOf("# Harbour plan"),
                LineOf("# Chapter one"),
                .. Enumerable.Repeat(LineOf("## Points"), parts),
                LineOf("## Short"),
                LineOf("# Thank you {.closing}"),
            ],
            lines);
        Assert.Equal(6, lines[0]);
    }

    [Fact]
    public void TheLines_AreKeptInTheSlides_AsAValidExtension()
    {
        var (_, pptx) = Generate();

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var extension = Samples.Slides(package)[0].Slide!.GetFirstChild<P.SlideExtensionList>()!.GetFirstChild<P.SlideExtension>()!;
        Assert.Equal(SourceLines.Uri, extension.Uri?.Value);
        Assert.Contains($"line=\"{LineOf("# Harbour plan")}\"", extension.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlDeck_ReturnsTheGeneratorsLines_AndWritesThemOnTheSections()
    {
        var (result, pptx) = Generate();
        var (html, deck) = Html(pptx);

        Assert.Equal(result.SlideLines, deck.SlideLines);
        Assert.Equal(
            result.SlideLines!.Select(l => l.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Regex.Matches(html, "<section class=\"slide\"[^>]* data-line=\"(\\d+)\"").Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void ADeckWithoutLines_StillDraws_WithoutSlideLines()
    {
        var (result, pptx) = Generate();
        var (html, deck) = Html(WithoutLines(pptx, _ => true));

        Assert.Null(deck.SlideLines);
        Assert.Equal(result.Pages, deck.Pages);
        Assert.Equal(result.Pages, Regex.Matches(html, "<section class=\"slide\"").Count);
        Assert.DoesNotContain("data-line=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlideWithoutItsLine_IsZero_AmongSlidesWithTheirs()
    {
        var (result, pptx) = Generate();
        var (_, deck) = Html(WithoutLines(pptx, i => i == 1));

        Assert.Equal([result.SlideLines![0], 0, .. result.SlideLines.Skip(2)], deck.SlideLines!);
    }

    [Fact]
    public void Results_CompareTheirSlideLines()
    {
        Assert.Null(new GenerationResult([]) { Pages = 3 }.SlideLines);
        Assert.NotEqual(new GenerationResult([]) { Pages = 1 }, new GenerationResult([]) { Pages = 1, SlideLines = [3] });
        Assert.Equal(new GenerationResult([]) { Pages = 1, SlideLines = [3] }, new GenerationResult([]) { Pages = 1, SlideLines = [3] });
    }

    [Fact]
    public void TheScript_AnswersGo_AndReportsTheShownSlide()
    {
        Assert.Contains("data.kind === 'go'", HtmlDeckWriter.Script, StringComparison.Ordinal);
        Assert.Contains("kind: 'shown', slide: current + 1, line: info[current].line", HtmlDeckWriter.Script, StringComparison.Ordinal);
    }
}
