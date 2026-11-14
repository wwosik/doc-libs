using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><c>::: steps</c>, <c>::: riskmatrix</c> and Harvey balls on slides.</summary>
public class StepsAndRisksTests
{
    private static MarkdownDocument Sample() => Samples.Markdown(File.ReadAllText(Samples.Path("steps-and-risks.md")).ReplaceLineEndings("\n"));

    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, new PptxOptions());
        var pptx = output.ToArray();
        Samples.AssertValid(pptx);
        return (result, pptx);
    }

    private static P.GroupShape Drawing(PresentationDocument package, int slide) =>
        Samples.Slides(package)[slide].Slide!.Descendants<P.GroupShape>()
            .Single(g => g.NonVisualGroupShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("Diagram ", StringComparison.Ordinal));

    private static List<A.TableCell> Cells(PresentationDocument package, int slide) =>
        [.. Samples.Slides(package)[slide].Slide!.Descendants<A.TableCell>()];

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_RoundTripsThroughThePptxConverter(string template)
    {
        var pptx = Samples.Generate(Sample(), new PptxOptions { Template = Samples.BuiltIn(template) });
        Samples.AssertValid(pptx);
        Samples.WriteForReview($"steps-and-risks-{template}.pptx", pptx);

        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);

        Samples.AssertMatchesExpected($"steps-and-risks-{template}.roundtrip", back.Markdown);
    }

    [Fact]
    public void Steps_AreAFlowSlideOfEditableChevrons()
    {
        var (result, pptx) = Generate("# Deck\n\n## Programme\n\n::: steps\n- **Analyse** – the process\n- **Design** {.current}\n- **Build**\n:::\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(package)[1]));
        var shapes = Drawing(package, 1).Elements<P.Shape>().ToList();
        Assert.Equal(3, shapes.Count(s => s.ShapeProperties!.GetFirstChild<A.CustomGeometry>() is not null));
        Assert.Equal("Analyse|the process|Design|Build", string.Join("|", shapes.Where(s => s.TextBody is not null).Select(s => s.TextBody!.InnerText)));
    }

    [Fact]
    public void ARiskMatrix_IsADrawing_WithGermanAxisTitles()
    {
        var (_, pptx) = Generate("---\nlang: de\n---\n\n## Risiken\n\n::: riskmatrix\n| Risiko | W | A |\n|---|---|---|\n| Lieferverzug | 4 | 3 |\n:::\n");

        using var package = Samples.Open(pptx);
        var text = Drawing(package, 0).InnerText;
        Assert.Contains("Eintrittswahrscheinlichkeit", text, StringComparison.Ordinal);
        Assert.Contains("Auswirkung", text, StringComparison.Ordinal);
        Assert.Contains("Lieferverzug", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("## Options\n\n| Option | Cost |\n|---|:---:|\n| Buy | [{0}] |\n")]
    [InlineData("## Options\n\nText.\n\n::: panel\nIn a layout.\n:::\n\n| Option | Cost |\n|---|:---:|\n| Buy | [{0}] |\n")]
    public void AHarveyBall_IsItsGlyph_InTheSymbolFont_OnPlainAndFlowSlides(string markdown)
    {
        foreach (var (written, glyph) in new[] { ("0/4", "\u25CB"), ("1/4", "\u25D4"), ("2/4", "\u25D1"), ("1/2", "\u25D1"), ("3/4", "\u25D5"), ("4/4", "\u25CF") })
        {
            var (_, pptx) = Generate(markdown.Replace("{0}", written, StringComparison.Ordinal));

            using var package = Samples.Open(pptx);
            var cell = Cells(package, 0)[^1];
            Assert.Equal(glyph, cell.InnerText);
            var properties = cell.Descendants<A.RunProperties>().Single();
            Assert.Equal("Segoe UI Symbol", properties.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
            Assert.Equal("Segoe UI Symbol", properties.GetFirstChild<A.EastAsianFont>()!.Typeface!.Value);
            Assert.Equal("Segoe UI Symbol", properties.GetFirstChild<A.ComplexScriptFont>()!.Typeface!.Value);
            Assert.Equal(A.TextAlignmentTypeValues.Center, cell.Descendants<A.ParagraphProperties>().Single().Alignment!.Value);
        }
    }

    [Fact]
    public void ACellWithMoreThanTheBall_StaysText_AndTheSymbolFontIsNeverEmbedded()
    {
        var (_, pptx) = Generate("## Options\n\n| A | B | C |\n|---|---|---|\n| [3/4] done | \\[3/4] | [2/3] |\n");

        using var package = Samples.Open(pptx);
        Assert.Equal(["[3/4] done", "[3/4]", "[2/3]"], Cells(package, 0)[^3..].Select(c => c.InnerText));
        Assert.DoesNotContain(package.PresentationPart!.Presentation!.Descendants<P.EmbeddedFont>(), f => f.Font?.Typeface?.Value == "Segoe UI Symbol");
    }

    [Theory]
    [InlineData("## Plan\n\n::: steps\n- **A**\n:::\n", "at line 3 has 1 step; it takes 2 to 7")]
    [InlineData("## Risks\n\n::: riskmatrix\n| R | L | I |\n|---|---|---|\n| A | 6 | 1 |\n:::\n", "at line 6 has the likelihood '6'")]
    public void Mistakes_ThrowInTheDecksFormat(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnADarkSlide_TheStepsDescriptionsAreLight()
    {
        var (_, pptx) = Generate("## Plan {.dark}\n\n::: steps\n- **A** – first\n- **B** – second\n:::\n");

        using var package = Samples.Open(pptx);
        var description = Drawing(package, 0).Elements<P.Shape>().Single(s => s.TextBody?.InnerText == "first");
        Assert.Equal("FFFFFF", description.Descendants<A.RunProperties>().First().GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
    }
}
