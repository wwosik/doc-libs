using Markdig;
using Markdig.Extensions.CustomContainers;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary><c>::: timeline</c> and <c>::: gantt</c> as drawings, and the dates they take.</summary>
public class ScheduleDrawingTests
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();
    private static readonly ResolvedBrand Neutral = ResolvedBrand.Neutral;

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    private static Drawing Draw(string markdown, DateOnly? today = null, GeneratedTexts? texts = null)
    {
        var container = Markdown.Parse(markdown, Pipeline).OfType<CustomContainer>().Single();
        LayoutSyntax.CheckContainer(container.Info!, container.Arguments?.Trim() ?? string.Empty, DocumentFormat.Pdf);
        return VisualContainers.Draw(
            container,
            new VisualContext(Neutral, today ?? new DateOnly(2026, 4, 15), texts ?? GeneratedTexts.English, b => b.Line + 1, DocumentFormat.Pdf));
    }

    private static List<DrawingText> Texts(Drawing drawing) => [.. drawing.Items.OfType<DrawingText>()];

    private static List<DrawingShape> Shapes(Drawing drawing, DrawingShapeKind kind) =>
        [.. drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == kind)];

    private const string Timeline = """
        ::: timeline
        - **2026-06-30** Go-live: all regions
        - **2026-01** Kick-off
        - **2026-03** Design review: scope and budget agreed
        - **2026-Q4** Review
        :::
        """;

    [Theory]
    [InlineData("2026", "2026-01-01", "2026-12-31", "2026")]
    [InlineData("2026-Q2", "2026-04-01", "2026-06-30", "Q2 2026")]
    [InlineData("2026-02", "2026-02-01", "2026-02-28", "Feb 2026")]
    [InlineData("2026-03-15", "2026-03-15", "2026-03-15", "15 Mar 2026")]
    public void APeriod_RunsFromItsFirstToItsLastDay_AndIsShownInEnglish(string written, string start, string end, string shown)
    {
        var period = SchedulePeriod.Parse(written)!;

        Assert.Equal((DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)), (period.Start, period.End));
        Assert.Equal(shown, period.Display(GeneratedTexts.English));
    }

    [Theory]
    [InlineData("2026-03-15", "15. März 2026")]
    [InlineData("2026-01-05", "5. Jan. 2026")]
    [InlineData("2026-10", "Okt. 2026")]
    [InlineData("2026-Q3", "Q3 2026")]
    public void InGerman_MonthNamesAndTheDayAreGerman(string written, string shown) =>
        Assert.Equal(shown, SchedulePeriod.Parse(written)!.Display(GeneratedTexts.German));

    [Theory]
    [InlineData("March 2026")]
    [InlineData("2026-13")]
    [InlineData("2026-02-30")]
    [InlineData("2026-Q5")]
    [InlineData("15.03.2026")]
    [InlineData("26-03")]
    public void OtherForms_AreNoPeriod(string written) => Assert.Null(SchedulePeriod.Parse(written));

    [Fact]
    public void AHostsTexts_WithoutMonthNamesOfTheirOwn_AreEnglish()
    {
        var texts = GeneratedTexts.English with { Note = "Notiz" };

        Assert.Equal("Mar 2026", SchedulePeriod.Parse("2026-03")!.Display(texts));
    }

    [Fact]
    public void TheTimeline_IsInDateOrder_PastEntriesGrey_TheNextOneRinged()
    {
        var drawing = Draw(Timeline, today: new DateOnly(2026, 4, 15));

        var texts = Texts(drawing);
        Assert.Equal(["Jan 2026", "Mar 2026", "30 Jun 2026", "Q4 2026"], texts.Where(t => t.Size == 15).Select(t => t.Text));
        var titles = texts.Where(t => t.Size == 17).ToList();
        Assert.Equal(["Kick-off", "Design review", "Go-live", "Review"], titles.Select(t => t.Text));
        Assert.All(titles, t => Assert.True(t.Spans.All(s => s.Bold)));
        Assert.Equal(["scope and budget agreed", "all regions"], texts.Where(t => t.Size == 14).Select(t => t.Text));

        // Two past entries in the line colour, then the next one ringed, all evenly spaced.
        var circles = Shapes(drawing, DrawingShapeKind.Ellipse);
        var dots = circles.Where(c => c.Fill is not null).ToList();
        Assert.Equal([Neutral.Diagram.Line, Neutral.Diagram.Line, Neutral.Diagram.Accent, Neutral.Diagram.Accent], dots.Select(d => d.Fill));
        var ring = Assert.Single(circles, c => c.Fill is null);
        Assert.Equal(dots[2].Path.Bounds.X + (dots[2].Path.Bounds.Width / 2), ring.Path.Bounds.X + (ring.Path.Bounds.Width / 2), 6);
        var centres = dots.Select(d => d.Path.Bounds.X + (d.Path.Bounds.Width / 2)).ToList();
        Assert.Equal([120.0, 360.0, 600.0, 840.0], centres.Select(c => Math.Round(c, 6)));
        Assert.Equal(titles[0].Color, Neutral.Diagram.Line);
        Assert.Equal(titles[2].Color, Neutral.Diagram.Text);
        // As tall as the labels: a date above the line, a title and one line of description below it.
        Assert.Equal((960.0, 43 + 87 + 6.0), (drawing.Width, drawing.Height));
    }

    [Fact]
    public void TodayOnTheContainer_WinsOverTheClock()
    {
        var drawing = Draw(Timeline.Replace("::: timeline", "::: timeline today=2027-01-01", StringComparison.Ordinal), today: new DateOnly(2026, 1, 1));

        var circles = Shapes(drawing, DrawingShapeKind.Ellipse);
        Assert.All(circles, c => Assert.Equal(Neutral.Diagram.Line, c.Fill));
        Assert.DoesNotContain(circles, c => c.Fill is null);
    }

    [Fact]
    public void BeforeEveryEntry_TheFirstIsTheNext()
    {
        var circles = Shapes(Draw(Timeline, today: new DateOnly(2025, 12, 1)), DrawingShapeKind.Ellipse);

        Assert.Equal(5, circles.Count);
        Assert.Null(circles[0].Fill);
        Assert.DoesNotContain(circles.Skip(1), c => c.Fill == Neutral.Diagram.Line);
    }

    [Fact]
    public void MoreThanFiveEntries_Alternate_AboveAndBelowTheLine()
    {
        var drawing = Draw("::: timeline\n" + string.Concat(Enumerable.Range(1, 6).Select(m => $"- **2026-0{m}** Step {m}\n")) + ":::\n");

        var titles = Texts(drawing).Where(t => t.Size == 17).ToList();
        var line = ((DrawingShape)drawing.Items[0]).Path.Bounds.Y;
        Assert.True(titles[0].Baseline > line);
        Assert.True(titles[1].Baseline < line);
        Assert.True(titles[2].Baseline > line);
        Assert.Equal(["Jan 2026", "Feb 2026"], Texts(drawing).Where(t => t.Size == 15).Take(2).Select(t => t.Text));
        Assert.True(Texts(drawing).Single(t => t.Text == "Feb 2026").Baseline > line);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void TooFewOrTooManyEntries_Throw_NamingTheLine(int count)
    {
        var markdown = "Text.\n\n::: timeline\n" + string.Concat(Enumerable.Range(1, count).Select(m => $"- **2026-0{m}** Step\n")) + ":::\n";

        var error = Assert.Throws<DocumentConversionException>(() => Draw(markdown));

        Assert.Contains("at line 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("2 to 8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateInAnotherForm_Throws_NamingTheLineAndTheForms()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Draw("::: timeline\n- **2026-01** Start\n- **March 2026** Review\n:::\n"));

        Assert.EndsWith(
            "The timeline entry at line 3 has the date 'March 2026'; write it as 2026, 2026-Q2, 2026-03 or 2026-03-15.",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("::: timeline\nText.\n:::\n", "something else than one list")]
    [InlineData("::: timeline\n- 2026-01 Start\n- **2026-02** End\n:::\n", "does not start with a bold date")]
    [InlineData("::: timeline\n- **2026-01**\n- **2026-02** End\n:::\n", "no title")]
    public void OtherContent_Throws(string markdown, string expected) =>
        Assert.Contains(expected, Assert.Throws<DocumentConversionException>(() => Draw(markdown)).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("::: timeline grid=month", "takes today=YYYY-MM-DD, not 'grid=month'")]
    [InlineData("::: timeline today=2026-03", "today=YYYY-MM-DD expected")]
    [InlineData("::: gantt grid=day", "grid=week|month|quarter expected")]
    [InlineData("::: gantt today", "not 'today'")]
    [InlineData("::: gantt grid=week grid=month", "grid twice")]
    public void WrongArguments_Throw_ListingTheValidOnes(string opening, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(
            () => LayoutSyntax.CheckContainer(opening.Split(' ')[1], string.Join(' ', opening.Split(' ').Skip(2)), DocumentFormat.Pptx));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Equal(DocumentFormat.Pptx, error.Format);
    }

    private const string Gantt = """
        ::: gantt
        | Task     | Start      | End        |
        |----------|------------|------------|
        | Analysis | 2026-01-05 | 2026-02-13 |
        | **Build** | 2026-02-16 | 2026-05-29 |
        | Go-live  | 2026-06-01 |            |
        :::
        """;

    [Fact]
    public void TheGantt_HasABarPerTask_ADiamondForAMilestone_AndALineForToday()
    {
        var drawing = Draw(Gantt, today: new DateOnly(2026, 3, 2));

        var bars = Shapes(drawing, DrawingShapeKind.RoundedRectangle);
        Assert.Equal(2, bars.Count);
        Assert.Equal(Neutral.Diagram.Line, bars[0].Fill);
        Assert.Equal(Neutral.Diagram.Palette[1], bars[1].Fill);
        Assert.True(bars[1].Path.Bounds.X > bars[0].Path.Bounds.X + bars[0].Path.Bounds.Width);
        var diamond = Assert.Single(drawing.Items.OfType<DrawingShape>(), s => s.Kind == DrawingShapeKind.Path && s.Fill is not null);
        Assert.Equal(5, diamond.Path.Segments.Count);
        Assert.Equal(Neutral.Diagram.Palette[2], diamond.Fill);
        var today = Assert.Single(drawing.Items.OfType<DrawingShape>(), s => s.Stroke == Neutral.Highlight);
        Assert.InRange(today.Path.Bounds.X, bars[1].Path.Bounds.X, bars[1].Path.Bounds.X + bars[1].Path.Bounds.Width);

        // 24 weeks: months, with their year once.
        var labels = Texts(drawing).Select(t => t.Text).ToList();
        Assert.Equal(["2026", "Jan", "Feb", "Mar", "Apr", "May", "Jun"], labels.Take(7).Where(l => l is "2026" || l.Length == 3));
        Assert.Contains("Build", labels);
        Assert.True(Texts(drawing).Single(t => t.Text == "Build").Spans.Single().Bold);
        Assert.Equal((960.0, 50 + (3 * 28) + 6.0), (drawing.Width, drawing.Height));
    }

    [Fact]
    public void TodayOutsideTheSpan_DrawsNoLine()
    {
        var drawing = Draw(Gantt, today: new DateOnly(2027, 3, 2));

        Assert.DoesNotContain(drawing.Items.OfType<DrawingShape>(), s => s.Stroke == Neutral.Highlight);
        Assert.All(Shapes(drawing, DrawingShapeKind.RoundedRectangle), b => Assert.Equal(Neutral.Diagram.Line, b.Fill));
    }

    [Theory]
    [InlineData("2026-03-02", "2026-05-10", "week")]
    [InlineData("2026-03-02", "2026-05-11", "month")]
    [InlineData("2026-01-01", "2027-06-30", "month")]
    [InlineData("2026-01-01", "2027-07-01", "quarter")]
    public void WithoutAGrid_TheSpanChoosesIt(string first, string last, string grid) =>
        Assert.Equal(grid, GanttDrawing.Choose(DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(last, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void AWeekGrid_ShowsIsoWeeks_WAndKw()
    {
        var markdown = "::: gantt grid=week\n| T | S | E |\n|---|---|---|\n| Sprint | 2026-03-16 | 2026-03-27 |\n:::\n";

        var english = Texts(Draw(markdown)).Select(t => t.Text).ToList();
        var german = Texts(Draw(markdown, texts: GeneratedTexts.German)).Select(t => t.Text).ToList();

        Assert.Contains("W12", english);
        Assert.Contains("W13", english);
        Assert.Contains("KW 12", german);
        Assert.DoesNotContain("W11", english);
    }

    [Fact]
    public void AQuarterGrid_AndGermanMonthNames()
    {
        var quarters = Texts(Draw(Gantt.Replace("::: gantt", "::: gantt grid=quarter", StringComparison.Ordinal))).Select(t => t.Text).ToList();
        var months = Texts(Draw(Gantt, texts: GeneratedTexts.German)).Select(t => t.Text).ToList();

        Assert.Contains("Q1", quarters);
        Assert.Contains("Q2", quarters);
        Assert.DoesNotContain("Jan", quarters);
        Assert.Contains("März", months);
        Assert.Contains("Juni", months);
    }

    [Fact]
    public void TheFirstThreeColumns_CountByPosition_WhateverTheirHeaders()
    {
        var drawing = Draw("::: gantt\n| Aufgabe | Von | Bis | Owner |\n|---|---|---|---|\n| Bau | 2026-02 | 2026-04 | Ann |\n:::\n");

        Assert.Single(Shapes(drawing, DrawingShapeKind.RoundedRectangle));
        Assert.DoesNotContain(Texts(drawing), t => t.Text == "Ann");
    }

    [Fact]
    public void AnEndEqualToItsStart_IsAMilestone()
    {
        var drawing = Draw("::: gantt\n| T | S | E |\n|---|---|---|\n| Demo | 2026-03-27 | 2026-03-27 |\n| Work | 2026-03-02 | 2026-03-27 |\n:::\n");

        Assert.Single(Shapes(drawing, DrawingShapeKind.RoundedRectangle));
        Assert.Single(drawing.Items.OfType<DrawingShape>(), s => s.Kind == DrawingShapeKind.Path && s.Fill is not null);
    }

    [Theory]
    [InlineData(0, "has 0 rows; it takes 1 to 20")]
    [InlineData(21, "has 21 rows; it takes 1 to 20")]
    public void TooFewOrTooManyRows_Throw(int rows, string expected)
    {
        var markdown = "::: gantt\n| T | S | E |\n|---|---|---|\n" + string.Concat(Enumerable.Range(1, rows).Select(i => $"| T{i} | 2026-01 | 2026-02 |\n")) + ":::\n";
        if (rows == 0)
        {
            markdown = "::: gantt\n| T | S | E |\n|---|---|---|\n:::\n";
        }

        Assert.Contains(expected, Assert.Throws<DocumentConversionException>(() => Draw(markdown)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("| Build | 2026-03-01 | 2026-02-01 |", "at line 4 ends (1 Feb 2026) before it starts (1 Mar 2026)")]
    [InlineData("| Build | soon | 2026-02-01 |", "at line 4 has the start 'soon'; write it as 2026, 2026-Q2, 2026-03 or 2026-03-15")]
    [InlineData("| Build |  | 2026-02-01 |", "has no start")]
    [InlineData("|  | 2026-01 | 2026-02-01 |", "has no task")]
    public void WrongRows_Throw_NamingTheLine(string row, string expected)
    {
        var markdown = "::: gantt\n| T | S | E |\n|---|---|---|\n" + row + "\n:::\n";

        Assert.Contains(expected, Assert.Throws<DocumentConversionException>(() => Draw(markdown)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADrawnContainer_IsAValidDrawing()
    {
        Draw(Timeline).Validate();
        Draw(Gantt).Validate();
    }
}
