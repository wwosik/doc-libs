using System.Text.Json;
using SkiaSharp;
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

namespace wwosik.DocLibs.DocumentConversions.Svg.Tests;

/// <summary>The contact sheet <see cref="DocumentTool"/> attaches to the decks it writes (E05-S06).</summary>
public class DeckContactSheetTests
{
    private static readonly DocumentTool Tool = new(
        [PptxGenerator.ForOptions(), PdfGenerator.ForOptions()],
        new DocumentToolOptions { Preview = new DeckContactSheet() });

    private static string Arguments(string format, string markdown) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["format"] = format, ["markdown"] = markdown });

    private static string Slides(int count) =>
        "# Plan\n\n" + string.Concat(Enumerable.Range(1, count).Select(i => $"## Step {i}\n\n- Point {i}\n\n"));

    [Fact]
    public void ADeck_GetsAContactSheet_FourColumnsWide()
    {
        using var output = new MemoryStream();
        var run = Tool.Run(Arguments("pptx", Slides(6)), [], output);

        Assert.True(run.Succeeded, run.Reply);
        Assert.Equal(7, run.Result!.Pages);
        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal("attached", reply.RootElement.GetProperty("contactSheet").GetString());
        Assert.False(reply.RootElement.TryGetProperty("contactSheetNote", out _));

        using var sheet = SKBitmap.Decode(run.ContactSheet);
        Assert.NotNull(sheet);
        Assert.Equal(16 + (4 * (320 + 16)), sheet.Width);
        var two = new DeckContactSheet().ContactSheet(output.ToArray(), run.Result);
        Assert.Equal(run.ContactSheet, two);
    }

    [Fact]
    public void TheSameDeck_GivesTheSamePng()
    {
        var first = Tool.Run(Arguments("pptx", Slides(3)), [], Stream.Null);
        var second = Tool.Run(Arguments("pptx", Slides(3)), [], Stream.Null);

        Assert.NotNull(first.ContactSheet);
        Assert.Equal(first.ContactSheet, second.ContactSheet);
    }

    [Fact]
    public void ALongDeck_ShowsItsFirstSixtySlides()
    {
        var run = Tool.Run(Arguments("pptx", Slides(IDeckPreview.MaxSlides + 3)), [], Stream.Null);
        var sixty = Tool.Run(Arguments("pptx", Slides(IDeckPreview.MaxSlides - 1)), [], Stream.Null);

        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal("The contact sheet shows slides 1 to 60 of 64.", reply.RootElement.GetProperty("contactSheetNote").GetString());
        Assert.Equal(IDeckPreview.MaxSlides, sixty.Result!.Pages);
        using var sheet = SKBitmap.Decode(run.ContactSheet);
        using var expected = SKBitmap.Decode(sixty.ContactSheet);
        Assert.Equal((expected.Width, expected.Height), (sheet.Width, sheet.Height));
    }

    [Fact]
    public void APdf_GetsNone()
    {
        var run = Tool.Run(Arguments("pdf", Slides(2)), [], Stream.Null);

        Assert.True(run.Succeeded, run.Reply);
        Assert.Null(run.ContactSheet);
        Assert.DoesNotContain("contactSheet", run.Reply, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyDeck_GivesNoSheet() =>
        Assert.Null(new DeckContactSheet().ContactSheet(ReadOnlyMemory<byte>.Empty, new GenerationResult([])));
}
