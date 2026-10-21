namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

public class MermaidRendererTests
{
    private const string Flowchart = "flowchart LR\n  A[Start] --> B{Valid?}";

    public static TheoryData<string> DiagramTypes =>
        [.. Samples.MermaidBlocks("diagrams.md").Select(code => code.Split([' ', '\n'])[0]).Where(type => type != "journey")];

    [Theory]
    [MemberData(nameof(DiagramTypes))]
    public void EverySupportedType_MatchesItsGoldenDrawing(string type)
    {
        var code = Samples.MermaidBlocks("diagrams.md").Single(c => c.Split([' ', '\n'])[0] == type);

        var graphic = Assert.IsType<DrawingGraphic>(Render(code, Samples.Brand));

        Samples.AssertMatchesExpected("mermaid-" + type.Replace("-v2", string.Empty, StringComparison.Ordinal), DrawingFormat.Write(graphic.Drawing));
    }

    [Fact]
    public void BrandDiagramColours_FlowIntoTheDrawing()
    {
        var brand = new Brand
        {
            Name = "a",
            Colors = new BrandColors
            {
                Primary = "111111",
                Diagram = new BrandDiagramColors { Text = "222222", Line = "333333", Surface = "444444", Border = "555555" },
            },
        };

        var drawing = ((DrawingGraphic)Render(Flowchart, brand)!).Drawing;

        var start = drawing.Items.OfType<DrawingShape>().First(s => s.Kind == DrawingShapeKind.RoundedRectangle);
        Assert.Equal(("444444", "555555"), (start.Fill, start.Stroke));
        Assert.Contains(drawing.Items.OfType<DrawingShape>(), s => s.Stroke == "333333");
        Assert.All(drawing.Items.OfType<DrawingText>(), t => Assert.Equal("222222", t.Color));
    }

    [Fact]
    public void Defaults_ComeFromTheBrand()
    {
        var drawing = ((DrawingGraphic)Render(Flowchart, Samples.Brand)!).Drawing;

        var start = drawing.Items.OfType<DrawingShape>().First(s => s.Kind == DrawingShapeKind.RoundedRectangle);
        Assert.Equal(("E6F4F5", "0E7C86"), (start.Fill, start.Stroke)); // table band, primary
        Assert.Contains(drawing.Items.OfType<DrawingShape>(), s => s.Stroke == "6B7280"); // footer grey
    }

    [Theory]
    [InlineData("python", Flowchart)]
    [InlineData("", Flowchart)]
    [InlineData("mermaid", "journey\n  title T\n  section S\n    Task: 5: Me")]
    [InlineData("mermaid", "timeline\n  title T\n  2026 : Release")]
    public void OtherLanguagesAndTypes_StayCode(string language, string code) => Assert.Null(Render(code, Brand.Neutral, language));

    [Fact]
    public void Language_IsCaseInsensitive() => Assert.NotNull(Render(Flowchart, Brand.Neutral, "Mermaid"));

    [Fact]
    public void BrokenDiagram_ThrowsNamingTheLine_OrStaysCode()
    {
        var request = new CodeBlockRequest("mermaid", "this is not mermaid", Brand.Neutral) { Line = 12 };

        var error = Assert.Throws<ArgumentException>(() => new MermaidRenderer().Render(request));
        Assert.Contains("The mermaid code block at line 12 cannot be drawn: ", error.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid mermaid header", error.Message, StringComparison.Ordinal);

        Assert.Null(new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }.Render(request));
    }

    [Fact]
    public void Options_HaveNoGradientsOrShadows_AndOnlyTheSupportedTypes()
    {
        var options = MermaidRenderer.Options(ResolvedBrand.Resolve(Samples.Brand));

        Assert.Equal((false, 0, false), (options.Gradient, options.Elevation, options.Transparent));
        Assert.Equal("#FFFFFF", options.Bg);
        Assert.Equal("PT Sans Narrow", options.Font);
        Assert.Equal(["#0E7C86", "#B45309", "#5EB8BF", "#7C3AED", "#BE123C", "#4B5563"], options.DataPalette!);
        Assert.Equal(MermaidRenderer.Supported, options.AllowedDiagrams);
    }

    private static CodeBlockGraphic? Render(string code, Brand brand, string language = "mermaid") =>
        new MermaidRenderer().Render(new CodeBlockRequest(language, code, brand));
}
