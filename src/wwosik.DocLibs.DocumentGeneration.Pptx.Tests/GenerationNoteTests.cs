using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>What a deck reports back: the slides written, and what was done to the Markdown.</summary>
public class GenerationNoteTests
{
    // A 1 × 1 pixel PNG: enlarged to fill any room it gets.
    private static readonly DocumentResource Tiny = new(
        "tiny.png",
        "image/png",
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));

    private static GenerationResult Generate(string markdown, PptxOptions? options = null, params DocumentResource[] resources)
    {
        using var output = new MemoryStream();
        return PptxGenerator.Generate(Samples.Markdown(markdown, resources), output, options);
    }

    [Fact]
    public void AnOrdinaryDeck_HasNoNotes_AndCountsItsSlides()
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown("# Deck\n\n## One\n\n- A point\n\n## Two\n\nText.\n"), output);

        Assert.False(result.HasNotes, result.ToString());
        Assert.Equal("No notes.", result.ToString());
        Assert.Equal(3, result.Pages);
        using var package = Samples.Open(output.ToArray());
        Assert.Equal(3, Samples.Slides(package).Count);
    }

    [Fact]
    public void ASplitSlide_IsReported_WithItsHeadingsLine_CountingFrontMatter()
    {
        var list = string.Concat(Enumerable.Range(1, 30).Select(i => $"- Point {i}\n  - with a detail\n"));
        var result = Generate($"---\ntitle: T\n---\n# Deck\n\n## Points\n\n{list}");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.SlideSplit, note.Code);
        Assert.Equal(6, note.Line);
        Assert.Contains("'Points'", note.Message, StringComparison.Ordinal);
        Assert.Equal(result.Pages - 1, int.Parse(note.Message.Split(" more slide")[0].Split(' ')[^1], System.Globalization.CultureInfo.InvariantCulture) + 1);
        Assert.StartsWith("- line 6: The slide 'Points'", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASlideSetAtTheSmallestSize_IsReported_ButSmallerStepsAboveItAreNot()
    {
        var codes = new List<string>();
        for (var count = 4; count < 40; count++)
        {
            var list = string.Concat(Enumerable.Range(1, count).Select(i => $"- Point number {i} with a few more words\n"));
            var result = Generate($"## Points\n\n{list}");
            if (result.Pages > 1)
            {
                break;
            }

            codes.AddRange(result.Notes.Select(n => n.Code));
        }

        Assert.Contains(NoteCodes.SlideShrunk, codes);
        Assert.All(codes, c => Assert.Equal(NoteCodes.SlideShrunk, c));
        Assert.True(codes.Count < 10, "only the lists at the smallest size are reported");
    }

    [Fact]
    public void OneItemTooBigForAnySlide_IsReportedOverfull()
    {
        var huge = string.Join(' ', Enumerable.Repeat("words that never end", 400));
        var result = Generate($"## Too much\n\n- {huge}\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.SlideOverfull, note.Code);
        Assert.Equal(1, note.Line);
        Assert.Equal(1, result.Pages);
    }

    [Fact]
    public void ATableWithOneHugeRow_IsReportedOverfull()
    {
        var huge = string.Join(' ', Enumerable.Repeat("cell text", 500));
        var result = Generate($"## Table\n\n| A | B |\n| - | - |\n| {huge} | {huge} |\n");

        Assert.Contains(result.Notes, n => n.Code == NoteCodes.SlideOverfull && n.Line == 1);
    }

    [Fact]
    public void ASmallImage_EnlargedMoreThanTwice_IsReported_UnlessItsScaleIsGiven()
    {
        var enlarged = Generate("## Picture\n\n![Dot](tiny.png)\n", null, Tiny);
        var scaled = Generate("## Picture\n\n![Dot](tiny.png \"scale=4\")\n", null, Tiny);
        var capped = Generate("## Picture\n\n![Dot](tiny.png)\n", new PptxOptions { MaxImageUpscale = 2 }, Tiny);

        var note = Assert.Single(enlarged.Notes);
        Assert.Equal(NoteCodes.ImageEnlarged, note.Code);
        Assert.Equal(1, note.Line);
        Assert.Contains("'tiny.png'", note.Message, StringComparison.Ordinal);
        Assert.Contains("4 times", note.Message, StringComparison.Ordinal);
        Assert.False(scaled.HasNotes, scaled.ToString());
        Assert.False(capped.HasNotes, capped.ToString());
    }

    [Fact]
    public void AnUnsupportedMermaidType_StaysCode_AndIsReportedAtItsFence()
    {
        var options = new PptxOptions { CodeBlockRenderers = [new MermaidRenderer()] };
        var result = Generate("## Journey\n\n```mermaid\njourney\n  title My day\n```\n", options);

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.CodeNotDrawn, note.Code);
        Assert.Equal(3, note.Line);
        Assert.Contains("'journey'", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MermaidKeepingCode_OnASyntaxError_IsReported()
    {
        var options = new PptxOptions { CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }] };
        var result = Generate("## Broken\n\n```mermaid\nnot mermaid\n```\n", options);

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.CodeNotDrawn, note.Code);
        Assert.Contains("Invalid mermaid header", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeInALanguageNobodyClaims_StaysCode_Silently()
    {
        var options = new PptxOptions { CodeBlockRenderers = [new MermaidRenderer()] };

        Assert.False(Generate("## Code\n\n```csharp\nvar x = 1;\n```\n", options).HasNotes);
    }

    [Fact]
    public void ARendererIgnoringReport_StillWorks_AndRequestsCompareWithoutIt()
    {
        CodeBlockRequest? seen = null;
        var options = new PptxOptions { CodeBlockRenderers = [Samples.Renderer(r => { seen = r; return null; })] };
        Generate("## Code\n\n```x\ny\n```\n", options);

        Assert.NotNull(seen?.Report);
        Assert.Equal(new CodeBlockRequest("x", "y", seen!.Brand) { Line = 3 }, seen);
    }

    [Fact]
    public void RawHtml_ShownAsText_IsReported_BlocksAndInline_CommentsNot()
    {
        var result = Generate("---\ntitle: T\n---\n## Html\n\n<div>Box</div>\n\nA <b>bold</b> word.\n\n<!-- a comment -->\n");

        Assert.Equal([(NoteCodes.HtmlAsText, 6), (NoteCodes.HtmlAsText, 8)], result.Notes.Select(n => (n.Code, n.Line)).Distinct());
        Assert.Contains("'<div>Box</div>'", result.Notes[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void TheSampleDeck_GeneratesWithoutNotes(string template)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Deck(), output, new PptxOptions { Template = Samples.BuiltIn(template) });

        Assert.False(result.HasNotes, result.ToString());
        Assert.True(result.Pages > 1);
    }
}
