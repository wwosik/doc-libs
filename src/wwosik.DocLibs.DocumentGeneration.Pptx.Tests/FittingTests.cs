using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Nothing runs off a slide: too much is set smaller, then continued on slides with the same title.</summary>
public class FittingTests
{
    public static TheoryData<string> Templates => ["neutral", "sample", "brand-dark"];

    [Fact]
    public void AListLongerThanTheSlide_IsShrunkFirst_AndSplitOnlyLater()
    {
        // Lengthen the list until the slide no longer fits as it is: it is then shrunk, still on one slide.
        for (var count = 4; count < 40; count++)
        {
            var list = string.Concat(Enumerable.Range(1, count).Select(i => $"- Point number {i} with a few more words\n"));
            using var package = Samples.Open(Samples.Generate(Samples.Markdown($"## Points\n\n{list}")));
            if (Samples.Slides(package).Single().Slide!.Descendants<A.NormalAutoFit>().SingleOrDefault() is { } autofit)
            {
                Assert.InRange(autofit.FontScale!.Value, 50000, 99999);
                return;
            }
        }

        Assert.Fail("No list was shrunk on one slide before being split.");
    }

    [Fact]
    public void ALongList_ContinuesOnSlidesWithTheSameTitle_AndOptionallyASuffix()
    {
        var list = string.Concat(Enumerable.Range(1, 30).Select(i => $"- Point {i}\n  - with a detail\n"));
        var markdown = Samples.Markdown($"## Points\n\n{list}");

        using var plain = Samples.Open(Samples.Generate(markdown));
        using var marked = Samples.Open(Samples.Generate(markdown, new PptxOptions { ContinuationSuffix = " (cont.)" }));

        var titles = Samples.Slides(plain).Select(s => Samples.PlaceholderText(s, P.PlaceholderValues.Title)).ToList();
        Assert.True(titles.Count >= 2, $"{titles.Count} slides");
        Assert.All(titles, t => Assert.Equal("Points", t));
        Assert.Equal(["Points", .. Enumerable.Repeat("Points (cont.)", titles.Count - 1)], Samples.Slides(marked).Select(s => Samples.PlaceholderText(s, P.PlaceholderValues.Title)));
        var items = Samples.Slides(plain).SelectMany(s => s.Slide!.Descendants<A.Paragraph>()).Select(p => p.InnerText).Where(t => t.StartsWith("Point ", StringComparison.Ordinal)).ToList();
        Assert.Equal(30, items.Count);
        Assert.All(Samples.Slides(plain), s => Assert.StartsWith("Point ", s.Slide!.Descendants<A.Paragraph>().Select(p => p.InnerText).First(t => t.Length > 0 && t != "Points"), StringComparison.Ordinal)); // never a detail without its point
    }

    [Fact]
    public void ALongTable_ContinuesWithItsHeaderRow()
    {
        var rows = string.Concat(Enumerable.Range(1, 40).Select(i => $"| Item {i} | {i * 100} |\n"));
        using var package = Samples.Open(Samples.Generate(Samples.Markdown($"## Items\n\nAll items.\n\n| Name | Amount |\n| --- | --: |\n{rows}\nEnd of list.\n")));

        var slides = Samples.Slides(package);
        Assert.True(slides.Count >= 2, $"{slides.Count} slides");
        Assert.All(slides, s => Assert.Equal("Name", s.Slide!.Descendants<A.TableRow>().First().Descendants<A.Text>().First().Text));
        Assert.Equal(40, slides.Sum(s => s.Slide!.Descendants<A.TableRow>().Count() - 1));
        Assert.Contains("All items.", slides[0].Slide!.InnerText, StringComparison.Ordinal);
        Assert.Contains("End of list.", slides[^1].Slide!.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("All items.", slides[^1].Slide!.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverfullFlowSlide_IsShrunk_ThenSplitBetweenBlocks()
    {
        var panels = string.Concat(Enumerable.Range(1, 8).Select(i => $"::: panel\nPanel {i} with a sentence of text in it.\n:::\n\n"));
        using var package = Samples.Open(Samples.Generate(Samples.Markdown($"## Panels {{kicker=\"Many\"}}\n\n{panels}")));

        var slides = Samples.Slides(package);
        Assert.True(slides.Count >= 2, $"{slides.Count} slides");
        Assert.All(slides, s => Assert.Contains("Many", s.Slide!.InnerText, StringComparison.Ordinal));
        Assert.Equal(8, slides.Sum(s => s.Slide!.Descendants<P.Shape>().Count(sh => sh.InnerText.StartsWith("Panel ", StringComparison.Ordinal))));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void NoSampleDeck_RunsOffItsSlides(string template)
    {
        var options = (template == "brand-dark" ? DarkTemplate.Options : new PptxOptions { Template = Samples.BuiltIn(template) })
            with { CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }] };
        var layouts = Samples.Markdown(File.ReadAllText(Samples.Path("layouts.md")).ReplaceLineEndings("\n"));
        var diagrams = Samples.Markdown(File.ReadAllText(Samples.Path("diagrams.md")).ReplaceLineEndings("\n"));

        foreach (var document in new[] { Samples.Deck(), layouts, diagrams })
        {
            using var package = Samples.Open(Samples.Generate(document, options));
            var bottom = ContentBottom(package);
            foreach (var slide in Samples.Slides(package))
            {
                foreach (var (name, end) in Bottoms(slide))
                {
                    Assert.True(end <= bottom + 50000, $"{template}: '{name}' on '{Samples.PlaceholderText(slide, P.PlaceholderValues.Title)}' ends at {end}, below {bottom}.");
                }
            }
        }
    }

    // The bottom of the master's body placeholder, where the content area of the built-in layouts ends.
    private static long ContentBottom(PresentationDocument package)
    {
        var body = package.PresentationPart!.SlideMasterParts.First().SlideMaster!.CommonSlideData!.ShapeTree!.Elements<P.Shape>()
            .First(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape?.Type?.Value == P.PlaceholderValues.Body);
        var transform = body.ShapeProperties!.Transform2D!;
        return transform.Offset!.Y!.Value + transform.Extents!.Cy!.Value;
    }

    // Where every shape the generator placed ends: text boxes, boxes, pictures, tables and drawings; not placeholders.
    private static IEnumerable<(string Name, long Bottom)> Bottoms(SlidePart slide)
    {
        var tree = slide.Slide!.CommonSlideData!.ShapeTree!;
        foreach (var shape in tree.Elements<P.Shape>().Where(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape is null))
        {
            var t = shape.ShapeProperties!.Transform2D!;
            yield return (shape.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value!, t.Offset!.Y!.Value + t.Extents!.Cy!.Value);
        }

        foreach (var picture in tree.Elements<P.Picture>())
        {
            var t = picture.ShapeProperties!.Transform2D!;
            yield return ("picture", t.Offset!.Y!.Value + t.Extents!.Cy!.Value);
        }

        foreach (var frame in tree.Elements<P.GraphicFrame>())
        {
            yield return ("table", frame.Transform!.Offset!.Y!.Value + frame.Transform.Extents!.Cy!.Value);
        }

        foreach (var group in tree.Elements<P.GroupShape>())
        {
            var t = group.GroupShapeProperties!.TransformGroup!;
            yield return ("drawing", t.Offset!.Y!.Value + t.Extents!.Cy!.Value);
        }
    }
}
