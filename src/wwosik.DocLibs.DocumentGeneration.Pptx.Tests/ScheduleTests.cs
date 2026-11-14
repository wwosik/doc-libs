using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><c>::: timeline</c> and <c>::: gantt</c> on slides.</summary>
public class ScheduleTests
{
    private static readonly TimeProvider March = new FixedTime(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    private static MarkdownDocument Schedules() => Samples.Markdown(File.ReadAllText(Samples.Path("schedules.md")).ReplaceLineEndings("\n"));

    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, (options ?? new PptxOptions()) with { TimeProvider = options?.TimeProvider ?? March });
        var pptx = output.ToArray();
        Samples.AssertValid(pptx);
        return (result, pptx);
    }

    private static P.GroupShape Drawing(PresentationDocument package, int slide) =>
        Samples.Slides(package)[slide].Slide!.Descendants<P.GroupShape>()
            .Single(g => g.NonVisualGroupShapeProperties!.NonVisualDrawingProperties!.Name!.Value!.StartsWith("Diagram ", StringComparison.Ordinal));

    private static List<string?> Fills(P.GroupShape group) =>
        [.. group.Elements<P.Shape>().Select(s => s.ShapeProperties!.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value)];

    private static string Text(P.GroupShape group) =>
        string.Join("|", group.Elements<P.Shape>().Where(s => s.TextBody is not null).Select(s => s.TextBody!.InnerText));

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_RoundTripsThroughThePptxConverter(string template)
    {
        var pptx = Samples.Generate(Schedules(), new PptxOptions { Template = Samples.BuiltIn(template), TimeProvider = March });
        Samples.AssertValid(pptx);
        Samples.WriteForReview($"schedules-{template}.pptx", pptx);

        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);

        Samples.AssertMatchesExpected($"schedules-{template}.roundtrip", back.Markdown);
    }

    [Fact]
    public void ATimeline_IsAFlowSlideOfEditableShapes()
    {
        var (result, pptx) = Generate("# Deck\n\n## Roadmap\n\n::: timeline today=2026-04-15\n- **2026-01** Kick-off\n- **2026-06-30** Go-live: all regions\n:::\n");

        Assert.False(result.HasNotes, result.ToString());
        using var package = Samples.Open(pptx);
        Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(package)[1]));
        var group = Drawing(package, 1);
        Assert.Equal("Jan 2026|Kick-off|30 Jun 2026|Go-live|all regions", Text(group));
        Assert.Equal(3, group.Elements<P.Shape>().Count(s => s.ShapeProperties!.GetFirstChild<A.PresetGeometry>()?.Preset?.Value == A.ShapeTypeValues.Ellipse));
    }

    [Fact]
    public void WithoutToday_TheClockDecides()
    {
        const string Markdown = "## Plan\n\n::: timeline\n- **2026-02** Concept\n- **2026-05** Pilot\n:::\n";
        var neutral = ResolvedBrand.Neutral.Diagram;

        var (_, march) = Generate(Markdown);
        var (_, june) = Generate(Markdown, new PptxOptions { TimeProvider = new FixedTime(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)) });

        using (var package = Samples.Open(march))
        {
            Assert.Equal([neutral.Line, neutral.Accent], Fills(Drawing(package, 0)).OfType<string>());
        }

        using (var package = Samples.Open(june))
        {
            Assert.DoesNotContain(neutral.Accent, Fills(Drawing(package, 0)));
        }
    }

    [Fact]
    public void OnADarkSlide_TheTextIsLight()
    {
        var (_, pptx) = Generate("## Plan {.dark}\n\n::: gantt today=2026-03-01\n| Task | Start | End |\n|---|---|---|\n| Build | 2026-02 | 2026-04 |\n:::\n");

        using var package = Samples.Open(pptx);
        var task = Drawing(package, 0).Elements<P.Shape>().Single(s => s.TextBody?.InnerText == "Build");
        Assert.Equal("FFFFFF", task.Descendants<A.RunProperties>().First().GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value);
    }

    [Fact]
    public void InGerman_MonthsAndWeeksAreGerman()
    {
        var (_, pptx) = Generate("---\nlang: de\n---\n\n## Plan\n\n::: gantt grid=week\n| Aufgabe | Von | Bis |\n|---|---|---|\n| Bau | 2026-03-16 | 2026-03-27 |\n:::\n\n## Meilensteine\n\n::: timeline\n- **2026-03-15** Start\n- **2026-05** Pilot\n:::\n");

        using var package = Samples.Open(pptx);
        Assert.Contains("KW 12", Text(Drawing(package, 0)), StringComparison.Ordinal);
        Assert.Contains("15. März 2026", Text(Drawing(package, 1)), StringComparison.Ordinal);
    }

    [Fact]
    public void Mistakes_ThrowInTheDecksFormat()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("## Plan\n\n::: timeline\n- **2026-01** Start\n:::\n"));

        Assert.Equal(Format.Pptx, error.Format);
        Assert.Contains("at line 3 has 1 entry; it takes 2 to 8", error.Message, StringComparison.Ordinal);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
