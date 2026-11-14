using System.Runtime.CompilerServices;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The scene syntax both generators share, and the still drawing documents show.</summary>
public class SceneTests
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    private static List<Scene> Parse(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        SceneSyntax.Check(document, DocumentFormat.Pdf, b => b.Line + 1);
        return [.. document.OfType<CustomContainer>().Where(SceneSyntax.IsScene).Select(c => SceneSyntax.Parse(c, markdown, b => b.Line + 1, DocumentFormat.Pdf))];
    }

    private static Scene One(string lines) => Parse($"## S\n\n::: scene\n{lines}\n:::\n").Single();

    [Fact]
    public void Lines_BecomeShapesAndConnectors_WithTheirStyles()
    {
        var scene = One("box api 65 34 30 22 \"API\" .accent step=1\n\n% a comment\nellipse db 122 34.5 30 22 \"Data\\nbase\" .bold .large exit=3\narrow api db \"calls\" .small name=call\nline db api");

        Assert.Equal(["api", "db"], scene.Shapes.Select(s => s.Name));
        var api = scene.Shapes[0];
        Assert.Equal((SceneKind.Box, 65.0, 34.0, 30.0, 22.0, "API", 4), (api.Kind, api.X, api.Y, api.W, api.H, api.Label, api.Line));
        Assert.Equal(new SceneStyle(SceneRole.Accent, false, SceneTextSize.Normal, 1, null, BuildEffect.Appear), api.Style);
        var db = scene.Shapes[1];
        Assert.Equal((SceneKind.Ellipse, 34.5, "Data\nbase", 7), (db.Kind, db.Y, db.Label, db.Line));
        Assert.Equal((true, 24, (int?)3), (db.Style.Bold, db.Style.Points, db.Style.Exit));
        Assert.Equal(["call", "db-api"], scene.Connectors.Select(c => c.Name));
        Assert.Equal((true, "calls", 12), (scene.Connectors[0].Arrow, scene.Connectors[0].Label, scene.Connectors[0].Style.Points));
        Assert.False(scene.Connectors[1].Arrow);

        // Connectors come in with their later shape and leave with the earlier.
        Assert.Equal(((int?)1, (int?)3), (scene.Connectors[0].Style.Step, scene.Connectors[0].Style.Exit));
        Assert.Equal([1, 3], scene.Clicks);
    }

    [Theory]
    [InlineData("box a 10 10 10 10\nbox b 40 12 10 10", "Right Left")]
    [InlineData("box a 40 10 10 10\nbox b 10 12 10 10", "Left Right")]
    [InlineData("box a 10 10 10 10\nbox b 12 40 10 10", "Bottom Top")]
    [InlineData("box a 10 40 10 10\nbox b 12 10 10 10", "Top Bottom")]
    [InlineData("box a 10 10 10 10\nbox b 30 40 10 10", "Bottom Top")]
    [InlineData("box a 10 10 10 10\nbox b 40 30 10 10", "Right Left")]
    public void AConnector_RunsBetweenTheFacingSides(string shapes, string sides)
    {
        var scene = One(shapes + "\narrow a b");

        Assert.Equal(sides, string.Join(" ", scene.Sides(scene.Connectors[0]) is var (from, to) ? [from, to] : Array.Empty<SceneSide>()));
    }

    [Fact]
    public void TheStill_IsTheFinalState_AsAGoldenDrawing()
    {
        var markdown = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "scenes.md")).ReplaceLineEndings("\n");
        var drawings = Parse(markdown).Select(s => DrawingFormat.Write(SceneDrawing.Final(s, ResolvedBrand.Neutral)));

        AssertMatchesExpected("scenes.expected.txt", string.Join("\n", drawings));
    }

    [Fact]
    public void TheStill_LeavesOutWhatHasGone_AndConnectorsOfShapesThatHaveGone()
    {
        var drawing = SceneDrawing.Final(One("box a 10 30 20 10 \"A\"\nbox b 60 30 20 10 \"B\" exit=1\narrow a b\nbox c 10 60 20 10 \"C\" step=2"), ResolvedBrand.Neutral);

        Assert.Equal(["A", "C"], drawing.Items.OfType<DrawingText>().Select(t => t.Text));
        Assert.Equal(2, drawing.Items.OfType<DrawingShape>().Count());
        drawing.Validate();
    }

    [Fact]
    public void ARoleFillsTheShape_WithTextByContrast()
    {
        var brand = ResolvedBrand.Neutral;
        var scene = One("box a 0 0 10 10 \"x\"\nbox b 10 0 10 10 \"x\" .primary\nbox c 20 0 10 10 \"x\" .outline\nbox d 30 0 10 10 \"x\" .warning\ntext e 40 0 10 10 \"x\" .danger");

        Assert.Equal((brand.TableBand, brand.Primary, brand.Diagram.Text), SceneDrawing.Colours(scene.Shapes[0], brand));
        Assert.Equal((brand.Primary, (string?)null, "FFFFFF"), SceneDrawing.Colours(scene.Shapes[1], brand));
        Assert.Equal(((string?)null, brand.Primary, brand.Diagram.Text), SceneDrawing.Colours(scene.Shapes[2], brand));
        Assert.Equal(brand.Labels.Warning, SceneDrawing.Colours(scene.Shapes[3], brand).Fill);
        Assert.Equal(((string?)null, (string?)null, brand.Labels.Danger), SceneDrawing.Colours(scene.Shapes[4], brand));
    }

    [Fact]
    public void ContainersAndGuides_KnowTheScene()
    {
        Assert.Contains("scene", LayoutSyntax.Containers.Split(", "));
        LayoutSyntax.CheckContainer("scene", string.Empty, DocumentFormat.Pdf);
        Assert.Throws<DocumentConversionException>(() => LayoutSyntax.CheckContainer("scene", "dark", DocumentFormat.Pdf));
    }

    private static void AssertMatchesExpected(string name, string actual, [CallerFilePath] string caller = "")
    {
        if (Environment.GetEnvironmentVariable("DOCLIBS_UPDATE_EXPECTED") == "1")
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(caller)!, "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name)).ReplaceLineEndings("\n"), actual);
    }
}
