using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary><c>::: steps</c>, <c>::: riskmatrix</c> and Harvey balls as drawings.</summary>
public class StepsAndRiskDrawingTests
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();
    private static readonly ResolvedBrand Neutral = ResolvedBrand.Neutral;

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    private static Drawing Draw(string markdown, GeneratedTexts? texts = null, ResolvedBrand? brand = null)
    {
        var container = Markdown.Parse(markdown, Pipeline).OfType<CustomContainer>().Single();
        LayoutSyntax.CheckContainer(container.Info!, container.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
        return VisualContainers.Draw(
            container,
            new VisualContext(brand ?? Neutral, new DateOnly(2026, 4, 15), texts ?? GeneratedTexts.English, b => b.Line + 1, DocumentFormat.Pdf));
    }

    private static string Error(string markdown) => Assert.Throws<DocumentConversionException>(() => Draw(markdown)).Message;

    private static List<DrawingText> Texts(Drawing drawing) => [.. drawing.Items.OfType<DrawingText>()];

    // The chevrons: the paths that are neither rectangles nor ellipses.
    private static List<DrawingShape> Chevrons(Drawing drawing) =>
        [.. drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Path)];

    private static string Steps(int count, int? current = null) =>
        "::: steps\n" + string.Concat(Enumerable.Range(0, count).Select(i => $"- **Step {i + 1}** – what happens{(i == current ? " {.current}" : string.Empty)}\n")) + ":::\n";

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void Steps_AreChevronsInOneRow_FillingTheWidth(int count)
    {
        var drawing = Draw(Steps(count));

        var chevrons = Chevrons(drawing);
        Assert.Equal(count, chevrons.Count);
        Assert.Equal(StepsDrawing.Width, drawing.Width);
        Assert.Equal(0, chevrons[0].Path.Bounds.X, 6);
        Assert.Equal(StepsDrawing.Width, chevrons[^1].Path.Bounds.X + chevrons[^1].Path.Bounds.Width, 6);

        // The first has a flat left end, the others a notch.
        Assert.Equal(6, chevrons[0].Path.Segments.Count);
        Assert.All(chevrons.Skip(1), c => Assert.Equal(7, c.Path.Segments.Count));
    }

    [Fact]
    public void WithoutCurrent_AllStepsAreAlike_InTheAccentColour()
    {
        var drawing = Draw(Steps(4));

        Assert.All(Chevrons(drawing), c => Assert.Equal((Neutral.Diagram.Accent, (string?)null), (c.Fill!, c.Stroke)));
        var titles = Texts(drawing).Where(t => t.Text.StartsWith("Step", StringComparison.Ordinal)).ToList();
        Assert.All(titles, t => Assert.Equal(("FFFFFF", true), (t.Color, t.Spans.All(s => s.Bold))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Current_MarksItsStep_TheOnesBeforeDone_TheOnesAfterOutlined(int current)
    {
        var drawing = Draw(Steps(4, current));
        var diagram = Neutral.Diagram;

        var chevrons = Chevrons(drawing);
        for (var i = 0; i < chevrons.Count; i++)
        {
            var expected = i < current ? (diagram.Accent, (string?)null)
                : i == current ? (Neutral.Highlight, null)
                : (diagram.Surface, diagram.Border);
            Assert.Equal(expected, (chevrons[i].Fill!, chevrons[i].Stroke));
        }

        var titles = Texts(drawing).Where(t => t.Text.StartsWith("Step", StringComparison.Ordinal)).ToList();
        Assert.Equal([.. Enumerable.Range(0, 4).Select(i => i == current)], titles.Select(t => t.Spans.All(s => s.Bold)));
        Assert.Equal([.. Enumerable.Range(0, 4).Select(i => i <= current ? "FFFFFF" : diagram.Text)], titles.Select(t => t.Color));
        Assert.DoesNotContain(Texts(drawing), t => t.Text.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void ATitleAndItsDescription_AreSplitAtTheDash_DescriptionsBelowTheChevrons()
    {
        var drawing = Draw("::: steps\n- **Analyse** – understand the process\n- **Design**: agree the picture\n- **Build**\n:::\n");

        Assert.Equal(["Analyse", "understand the process", "Design", "agree the picture", "Build"], Texts(drawing).Select(t => t.Text));
        var chevronBottom = Chevrons(drawing)[0].Path.Bounds.Y + Chevrons(drawing)[0].Path.Bounds.Height;
        Assert.True(Texts(drawing)[1].Baseline > chevronBottom);
        Assert.All(Texts(drawing), t => Assert.NotNull(t.MaxWidth));
    }

    [Fact]
    public void Steps_WithoutDescriptions_AreOnlyAsTallAsTheChevrons()
    {
        var withDescriptions = Draw(Steps(3));
        var without = Draw("::: steps\n- **A**\n- **B**\n:::\n");

        Assert.True(without.Height < withDescriptions.Height);
        Assert.True(without.Height < 80);
    }

    [Fact]
    public void OnADarkSlide_WhereTheHighlightIsTheAccent_TheCurrentStepIsOutlined()
    {
        var dark = ResolvedBrand.Resolve(ResolvedBrand.DarkSlideBrand(Brand.Neutral));
        Assert.Equal(dark.Highlight, dark.Diagram.Accent);

        var chevrons = Chevrons(Draw(Steps(3, 1), brand: dark));

        Assert.Equal(dark.Diagram.Text, chevrons[1].Stroke);
        Assert.Null(chevrons[0].Stroke);
    }

    [Theory]
    [InlineData(1, "has 1 step; it takes 2 to 7")]
    [InlineData(8, "has 8 steps; it takes 2 to 7")]
    public void TooFewOrTooManySteps_Throw(int count, string expected) =>
        Assert.Contains("at line 1 " + expected, Error(Steps(count)), StringComparison.Ordinal);

    [Theory]
    [InlineData("::: steps\n- **A** {.current}\n- **B** {.current}\n:::\n", "at line 3 is the second one marked {.current}")]
    [InlineData("::: steps\n- **A** {.done}\n- **B**\n:::\n", "at line 2 has the attribute '{.done}'; a step takes only {.current}")]
    [InlineData("::: steps\n- A\n- **B**\n:::\n", "at line 2 does not start with a bold title")]
    [InlineData("::: steps\nText.\n:::\n", "something else than one list")]
    [InlineData("::: steps big\n- **A**\n- **B**\n:::\n", "'::: steps big' takes no arguments")]
    public void Mistakes_InSteps_Throw(string markdown, string expected) =>
        Assert.Contains(expected, Error(markdown), StringComparison.Ordinal);

    private const string Matrix = """
        ::: riskmatrix
        | Risk              | Likelihood | Impact |
        |-------------------|------------|--------|
        | Supplier delay    | 4          | 3      |
        | Key person leaves | 2          | 5      |
        | Budget overrun    | 4          | 3      |
        | Licence costs     | 1          | 2      |
        :::
        """;

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(25, 2)]
    public void Scores_FallIntoFixedBands(int score, int band) => Assert.Equal(band, RiskMatrixDrawing.Band(score));

    [Fact]
    public void TheGrid_IsTintedByScore_LikelihoodUpAndImpactRight()
    {
        var drawing = Draw(Matrix);
        var labels = Neutral.Labels;

        var cells = drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Rectangle).ToList();
        Assert.Equal(25, cells.Count);

        // Bottom left is likelihood 1, impact 1; top right likelihood 5, impact 5.
        var bottomLeft = cells.MaxBy(c => c.Path.Bounds.Y - c.Path.Bounds.X)!;
        var topRight = cells.MaxBy(c => c.Path.Bounds.X - c.Path.Bounds.Y)!;
        Assert.Equal(ResolvedBrand.Mix(labels.Success, 0.22), bottomLeft.Fill);
        Assert.Equal(ResolvedBrand.Mix(labels.Danger, 0.22), topRight.Fill);
        Assert.Equal(8, cells.Count(c => c.Fill == ResolvedBrand.Mix(labels.Success, 0.22)));
        Assert.Equal(7, cells.Count(c => c.Fill == ResolvedBrand.Mix(labels.Warning, 0.22)));
        Assert.Equal(10, cells.Count(c => c.Fill == ResolvedBrand.Mix(labels.Danger, 0.22)));
    }

    [Fact]
    public void EachRisk_IsANumberedDotInItsCell_SideBySideWhenTheyShareOne_AndInTheLegend()
    {
        var drawing = Draw(Matrix);
        var labels = Neutral.Labels;

        var dots = drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Ellipse).ToList();
        Assert.Equal(8, dots.Count);
        var numbers = Texts(drawing).Where(t => t.Color == "FFFFFF").ToList();
        Assert.Equal(["1", "3", "2", "4", "1", "2", "3", "4"], numbers.Select(t => t.Text));

        // Supplier delay and budget overrun share likelihood 4, impact 3: side by side on one line.
        var (one, three) = (numbers[0], numbers[1]);
        Assert.Equal(one.Baseline, three.Baseline);
        Assert.True(three.X > one.X);
        Assert.Equal(labels.Danger, dots[0].Fill);
        Assert.Equal(labels.Success, dots[3].Fill);

        var legend = Texts(drawing).Where(t => t.Color == Neutral.Diagram.Text && t.Size == 15 && !t.Spans[0].Bold).Select(t => t.Text);
        Assert.Equal(["Supplier delay", "Key person leaves", "Budget overrun", "Licence costs"], legend);
        Assert.True(drawing.Width >= drawing.Height);
    }

    [Fact]
    public void AxisTitles_AreInTheDocumentLanguage()
    {
        var english = Texts(Draw(Matrix)).Select(t => t.Text).ToList();
        var german = Texts(Draw(Matrix, GeneratedTexts.German)).Select(t => t.Text).ToList();

        Assert.Contains("Likelihood", english);
        Assert.Contains("Impact", english);
        Assert.Contains("Eintrittswahrscheinlichkeit", german);
        Assert.Contains("Auswirkung", german);
        Assert.Equal(2, english.Count(t => t == "5"));
    }

    [Fact]
    public void FifteenRisksInOneCell_AllFit()
    {
        var markdown = "::: riskmatrix\n| R | L | I |\n|---|---|---|\n" + string.Concat(Enumerable.Range(1, 15).Select(i => $"| Risk {i} | 3 | 3 |\n")) + ":::\n";

        var dots = Draw(markdown).Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Ellipse).Take(15).ToList();

        var (left, right) = (dots.Min(d => d.Path.Bounds.X), dots.Max(d => d.Path.Bounds.X + d.Path.Bounds.Width));
        var (top, bottom) = (dots.Min(d => d.Path.Bounds.Y), dots.Max(d => d.Path.Bounds.Y + d.Path.Bounds.Height));
        Assert.True(right - left <= 72 && bottom - top <= 72, $"{right - left} × {bottom - top}");
    }

    [Theory]
    [InlineData("| A | 0 | 3 |", "at line 4 has the likelihood '0'; write a whole number from 1 to 5")]
    [InlineData("| A | 3 | 6 |", "at line 4 has the impact '6'; write a whole number from 1 to 5")]
    [InlineData("| A | high | 2 |", "has the likelihood 'high'")]
    [InlineData("| A | 2 | |", "has no impact")]
    [InlineData("| | 2 | 2 |", "has no risk in its first column")]
    public void ValuesOutOfRange_Throw(string row, string expected) =>
        Assert.Contains(expected, Error($"::: riskmatrix\n| R | L | I |\n|---|---|---|\n{row}\n:::\n"), StringComparison.Ordinal);

    [Fact]
    public void SixteenRisks_Throw() =>
        Assert.Contains(
            "has 16 rows; it takes 1 to 15",
            Error("::: riskmatrix\n| R | L | I |\n|---|---|---|\n" + string.Concat(Enumerable.Range(1, 16).Select(i => $"| Risk {i} | 3 | 3 |\n")) + ":::\n"),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("[0/4]", 0)]
    [InlineData("[1/4]", 1)]
    [InlineData("[2/4]", 2)]
    [InlineData("[1/2]", 2)]
    [InlineData("[3/4]", 3)]
    [InlineData("[4/4]", 4)]
    [InlineData(" [3/4] ", 3)]
    public void HarveyBalls_AreQuartersAndAHalf(string cell, int quarters)
    {
        Assert.Equal(quarters, HarveyBall.Quarters(cell));
        Assert.Equal(quarters, CellQuarters(cell));
    }

    [Theory]
    [InlineData("[5/4]")]
    [InlineData("[2/3]")]
    [InlineData("[3/4] done")]
    [InlineData("about [3/4]")]
    [InlineData("\\[3/4]")]
    [InlineData("`[3/4]`")]
    [InlineData("**[3/4]**")]
    public void OtherCells_AreNoHarveyBall(string cell) => Assert.Null(CellQuarters(cell));

    [Fact]
    public void TheGlyphs_AreTheQuarterCircles() =>
        Assert.Equal("\u25CB\u25D4\u25D1\u25D5\u25CF", string.Concat(Enumerable.Range(0, 5).Select(HarveyBall.Glyph)));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    public void TheDrawing_IsARing_AndASectorFromTwelveOClock(int quarters, int shapes)
    {
        var drawing = HarveyBall.Drawing(quarters);
        drawing.Validate();

        var items = drawing.Items.OfType<DrawingShape>().ToList();
        Assert.Equal(shapes, items.Count);
        Assert.Equal((null, "000000"), (items[0].Fill, items[0].Stroke));
        if (quarters is 1 or 3)
        {
            Assert.Equal(new MoveTo(12, 12), items[1].Path.Segments[0]);
            Assert.Equal(new LineTo(12, 1.5), items[1].Path.Segments[1]);
            Assert.Equal(quarters, items[1].Path.Segments.OfType<CubicTo>().Count());
        }
    }

    private static int? CellQuarters(string cell)
    {
        var table = Markdown.Parse($"| A |\n|---|\n| {cell} |\n", Pipeline).OfType<Table>().Single();
        var row = table.OfType<TableRow>().Single(r => !r.IsHeader);
        return HarveyBall.Quarters(((TableCell)row[0]).Descendants<LeafBlock>());
    }
}
