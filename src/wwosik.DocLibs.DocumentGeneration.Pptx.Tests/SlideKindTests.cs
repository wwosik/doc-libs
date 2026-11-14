using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Slide kinds, <c>::: conclusion</c>, <c>::: comment</c> and two columns of text on a two-content layout (E02-S05).</summary>
public class SlideKindTests
{
    private const string Kinds = """
        # Deck

        ## Agenda {.agenda}

        - Where we are
        - What comes next

        ## {.quote}

        > The grid is ready for twice the solar power.
        > — Ann Example, Chief Engineer

        ## The grid is ready {.statement}

        Twice the solar power by 2027.

        ## Contact {.contact}

        - ![Ann](ann.png) **Ann Example** – Chief Engineer
          ann@example.com
        - **Ben Example**
          Project lead

        ## Revenue {.chart}

        ```chart
        ```

        Up by a third.
        """;

    private static readonly ChartSpec Revenue = new(ChartKind.Bar, ["2025", "2026"], [new ChartSeries("Revenue", [3, 4])])
    {
        Title = "Revenue",
        Unit = "EUR m",
        Source = "Annual report",
    };

    private static PptxOptions WithCharts(PptxOptions options, ChartSpec? chart = null) => options with
    {
        CodeBlockRenderers = [Samples.Renderer(r => r.Language == "chart" ? new ChartGraphic(chart ?? Revenue, new Drawing(100, 50, [])) : null)],
    };

    private static (byte[] Pptx, GenerationResult Result) Generate(string markdown, PptxOptions options)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown, TestImages.Png("ann.png", 120, 160)), output, options);
        return (output.ToArray(), result);
    }

    private static P.PlaceholderShape? PlaceholderOf(DocumentFormat.OpenXml.OpenXmlElement shape) =>
        shape.Descendants<P.ApplicationNonVisualDrawingProperties>().FirstOrDefault()?.PlaceholderShape;

    // The text of the slide's shape in the placeholder the layout's shape named `name` stands for.
    private static string? TextOf(SlidePart slide, string name)
    {
        var placeholder = slide.SlideLayoutPart!.SlideLayout!.Descendants<P.Shape>()
            .Single(s => s.NonVisualShapeProperties!.NonVisualDrawingProperties!.Name!.Value == name).Descendants<P.PlaceholderShape>().Single();
        return slide.Slide!.Descendants<P.Shape>()
            .Where(s => PlaceholderOf(s) is { } p && p.Index?.Value == placeholder.Index?.Value && p.Type?.Value == placeholder.Type?.Value)
            .Select(s => s.InnerText).SingleOrDefault();
    }

    [Fact]
    public void TheParser_MakesASlideOfEachKind()
    {
        var slides = DeckParser.Parse(Samples.Markdown(Kinds, TestImages.Png("ann.png", 120, 160)), WithCharts(PptxOptions.Default));

        var agenda = Assert.IsType<AgendaSlide>(slides[1]);
        Assert.Equal([ParagraphKind.Numbered, ParagraphKind.Numbered], agenda.Items.Select(i => i.Kind));
        var quote = Assert.IsType<QuoteSlide>(slides[2]);
        Assert.Empty(quote.Title);
        Assert.Equal("The grid is ready for twice the solar power.", string.Concat(quote.Quote.Single().Runs.Select(r => r.Text)));
        Assert.All(quote.Quote.Single().Runs, r => Assert.True(r.Style.HasFlag(RunStyle.Italic)));
        Assert.Equal("Ann Example, Chief Engineer", string.Concat(quote.Byline!.Select(r => r.Text)));
        var statement = Assert.IsType<StatementSlide>(slides[3]);
        Assert.Equal("The grid is ready", statement.Title.Single().Text);
        Assert.Single(statement.Text);
        var contact = Assert.IsType<ContactSlide>(slides[4]);
        Assert.Equal(["Ann Example", "Ben Example"], contact.People.Select(p => string.Concat(p.Name.Select(r => r.Text))));
        Assert.Equal(["Chief Engineer", "ann@example.com"], contact.People[0].Lines.Select(l => string.Concat(l.Runs.Select(r => r.Text))));
        Assert.Equal("ann.png", contact.People[0].Photo!.Resource.Path);
        Assert.Null(contact.People[1].Photo);
        Assert.All(contact.People[1].Name, r => Assert.False(r.Style.HasFlag(RunStyle.Bold)));
        var chart = Assert.IsType<ChartSlide>(slides[5]);
        Assert.Single(chart.Charts);
        Assert.Single(chart.Text);
    }

    [Theory]
    [InlineData("> Ship it.\n\n— Ann", "Ship it.", "Ann")]
    [InlineData("> Ship it.\n> -- Ann, CEO", "Ship it.", "Ann, CEO")]
    [InlineData("> Ship it.\n>\n> – Ann", "Ship it.", "Ann")]
    [InlineData("> Ship it,\n> and soon.", "Ship it, and soon.", null)]
    public void AQuote_TakesItsBylineFromTheLineAfterIt_OrItsLastLine(string body, string quote, string? byline)
    {
        var slide = Assert.IsType<QuoteSlide>(DeckParser.Parse(Samples.Markdown($"## Q {{.quote}}\n\n{body}\n"), PptxOptions.Default).Single());

        Assert.Equal(quote, string.Join(" ", slide.Quote.Select(p => string.Concat(p.Runs.Select(r => r.Text)))));
        Assert.Equal(byline, slide.Byline is null ? null : string.Concat(slide.Byline.Select(r => r.Text)));
    }

    [Fact]
    public void AnEmptyAgenda_ListsTheChapters_WithTheirNumbersWhenNumbered()
    {
        const string Deck = "# Deck\n\n## Agenda {.agenda}\n\n# Market\n\n## A\n\nText\n\n# Plan\n\n# Thank you {.closing}\n";

        var plain = Assert.IsType<AgendaSlide>(DeckParser.Parse(Samples.Markdown(Deck), PptxOptions.Default)[1]);
        var numbered = Assert.IsType<AgendaSlide>(DeckParser.Parse(Samples.Markdown(Deck), PptxOptions.Default, numberChapters: true)[1]);

        Assert.Equal(["Market", "Plan"], plain.Items.Select(i => string.Concat(i.Runs.Select(r => r.Text))));
        Assert.All(plain.Items, i => Assert.Equal(ParagraphKind.Numbered, i.Kind));
        Assert.Equal(["01  Market", "02  Plan"], numbered.Items.Select(i => string.Concat(i.Runs.Select(r => r.Text))));
        Assert.All(numbered.Items, i => Assert.Equal(ParagraphKind.Plain, i.Kind));
    }

    [Fact]
    public void AnAgendaWithoutListOrChapters_KeepsItsTitle_WithANote()
    {
        var (pptx, result) = Generate("# Deck\n\n## Agenda {.agenda}\n", PptxOptions.Default);

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.AgendaEmpty, note.Code);
        Assert.Equal(3, note.Line);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal(TemplateBuilder.AgendaLayoutName, Samples.LayoutName(slide));
        Assert.Equal("Agenda", Samples.PlaceholderText(slide, P.PlaceholderValues.Title));
    }

    [Theory]
    [InlineData("## Q {.quote}\n\nNo quote here.\n", "A '{.quote}' slide holds a quote")]
    [InlineData("## Q {.quote}\n\n> A\n\nB\n", "A '{.quote}' slide holds a quote")]
    [InlineData("## C {.contact}\n\nAnn, Ben\n", "A '{.contact}' slide holds one list")]
    [InlineData("## A {.agenda}\n\nIntro\n\n- a\n", "A '{.agenda}' slide holds one list")]
    [InlineData("## S {.statement}\n\n| a |\n| - |\n| 1 |\n", "A '{.statement}' slide is its heading")]
    [InlineData("## R {.chart}\n\n| a |\n| - |\n| 1 |\n", "A '{.chart}' slide holds one or two ```chart blocks")]
    [InlineData("## R {.chart}\n\nText only\n", "A '{.chart}' slide holds one or two ```chart blocks")]
    [InlineData("## T\n\n::: comment\nNote\n:::\n\nText\n", "it cannot be the first block")]
    [InlineData("## T\n\n::: conclusion\nSo.\n:::\n\nText\n", "as its last block")]
    [InlineData("## T\n\nText\n\n::: conclusion\nA\n:::\n\n::: conclusion\nB\n:::\n", "as its last block")]
    [InlineData("## T\n\n::: cards\n### A\nx\n\n::: comment\nNote\n:::\n:::\n", "at the top level of a slide")]
    [InlineData("## T\n\nText\n\n::: comment\n| a |\n| - |\n| 1 |\n:::\n", "A '::: comment' holds text")]
    [InlineData("# Agenda {.agenda}\n", "{.agenda} goes on a '##' heading")]
    [InlineData("## T {.quote .agenda}\n\n> A\n", "name two kinds of slide")]
    [InlineData("## T {.statement .dark}\n", "combine .statement with .dark")]
    [InlineData("## T\n\n::: conclusion dark\nSo.\n:::\n", "takes no arguments")]
    public void Mistakes_Throw_NamingWhatIsAllowed(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Equal(wwosik.DocLibs.DocumentConversions.DocumentFormat.Pptx, error.Format);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void OnTheBuiltInMaster_EachKindFillsTheLayoutsPlaceholders(string brand)
    {
        var (pptx, result) = Generate(Kinds, WithCharts(new PptxOptions { Template = Samples.BuiltIn(brand) }));
        Samples.WriteForReview($"kinds-{brand}.pptx", pptx);

        Samples.AssertValid(pptx);
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.LayoutFallback);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(
            ["Title Slide", TemplateBuilder.AgendaLayoutName, TemplateBuilder.QuoteLayoutName, TemplateBuilder.StatementLayoutName, TemplateBuilder.ContactLayoutName, TemplateBuilder.ChartLayoutName],
            slides.Select(Samples.LayoutName));

        // Agenda: numbered, in the body.
        var agenda = slides[1];
        Assert.Equal("Where we areWhat comes next", TextOf(agenda, "Text Placeholder 2"));
        Assert.All(agenda.Slide!.Descendants<A.Paragraph>().Where(p => p.InnerText.Length > 0 && PlaceholderOf(p.Ancestors<P.Shape>().First())?.Type?.Value != P.PlaceholderValues.Title && p.Ancestors<P.Shape>().First().InnerText.StartsWith("Where", StringComparison.Ordinal)),
            p => Assert.NotNull(p.ParagraphProperties!.GetFirstChild<A.AutoNumberedBullet>()));

        // Quote: no title placeholder on the layout, the quote in the body and the byline in its own.
        var quote = slides[2];
        Assert.Equal("The grid is ready for twice the solar power.", TextOf(quote, "Text Placeholder 1"));
        Assert.Equal("Ann Example, Chief Engineer", TextOf(quote, TemplateBuilder.BylineName));
        Assert.Null(Samples.PlaceholderText(quote, P.PlaceholderValues.Title));

        // Statement: the heading in the title placeholder, the text in the body.
        var statement = slides[3];
        Assert.Equal("The grid is ready", Samples.PlaceholderText(statement, P.PlaceholderValues.Title));
        Assert.Equal("Twice the solar power by 2027.", TextOf(statement, "Text Placeholder 2"));

        // Contact: the photo cropped into the first picture placeholder, the people's text into the first two bodies.
        var contact = slides[4];
        var picture = contact.Slide!.Descendants<P.Picture>().Single();
        Assert.Equal(1U, PlaceholderOf(picture)!.Index!.Value);
        Assert.NotNull(picture.BlipFill!.SourceRectangle);
        Assert.Equal("Ann ExampleChief Engineerann@example.com", TextOf(contact, "Text Placeholder 7"));
        Assert.Equal("Ben ExampleProject lead", TextOf(contact, "Text Placeholder 8"));
        Assert.Null(TextOf(contact, "Text Placeholder 9"));
        Assert.True(contact.Slide.Descendants<P.Shape>().First(s => s.InnerText.StartsWith("Ann", StringComparison.Ordinal))
            .Descendants<A.Run>().First().RunProperties!.Bold!.Value);

        // Chart: in the chart placeholder; its title, unit and source in theirs, and out of the chart part.
        var chart = slides[5];
        var frame = chart.Slide!.Descendants<P.GraphicFrame>().Single();
        Assert.Equal(P.PlaceholderValues.Chart, PlaceholderOf(frame)!.Type!.Value);
        Assert.Equal("Revenue", TextOf(chart, TemplateBuilder.ChartTitleName));
        Assert.Equal("EUR m", TextOf(chart, TemplateBuilder.ChartUnitName));
        Assert.Equal("Source: Annual report", TextOf(chart, TemplateBuilder.ChartSourceName));
        var space = chart.ChartParts.Single().ChartSpace!;
        Assert.Empty(space.Descendants<DocumentFormat.OpenXml.Drawing.Charts.Title>());
        Assert.Contains(chart.Slide.Descendants<P.Shape>(), s => s.InnerText == "Up by a third.");
    }

    [Fact]
    public void OnATemplateWithoutTheLayouts_EachKindIsAFlowSlide_WithANote_InReadableColours()
    {
        var (pptx, result) = Generate(Kinds + "\n## Two {.chart}\n\n```chart\n```\n\n```chart\n```\n", WithCharts(DarkTemplate.Options));
        Samples.WriteForReview("kinds-brand-dark.pptx", pptx);

        Samples.AssertValid(pptx);

        // brand-dark has no declared roles: only its chart layout is found, by type.
        Assert.Equal(
            ["Agenda", string.Empty, "The grid is ready", "Contact", "Two"],
            result.Notes.Where(n => n.Code == NoteCodes.LayoutFallback).Select(n => n.Message.Split('\'')[1]));
        Assert.Contains(result.Notes, n => n.Code == NoteCodes.LayoutFallback && n.Message.Contains("a two-chart layout", StringComparison.Ordinal));
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(["Title Only", "Title Only", "Title Only", "Title Only", "Chart", "Title Only"], slides.Skip(1).Select(Samples.LayoutName));

        // The statement's heading is its first block, and no title is written; the quote's byline is right-aligned.
        Assert.Null(Samples.PlaceholderText(slides[3], P.PlaceholderValues.Title));
        Assert.Contains(slides[3].Slide!.Descendants<P.Shape>(), s => s.InnerText == "The grid is ready");
        var byline = slides[2].Slide!.Descendants<A.Paragraph>().Single(p => p.InnerText == "Ann Example, Chief Engineer");
        Assert.Equal(A.TextAlignmentTypeValues.Right, byline.ParagraphProperties!.Alignment!.Value);

        // The contact cards carry the photo; the two charts stand side by side.
        Assert.Single(slides[4].Slide!.Descendants<P.Picture>());
        var frames = slides[6].Slide!.Descendants<P.GraphicFrame>().Select(f => f.Transform!).ToList();
        Assert.Equal(2, frames.Count);
        Assert.Equal(frames[0].Offset!.Y!.Value, frames[1].Offset!.Y!.Value);
        Assert.True(frames[1].Offset!.X!.Value > frames[0].Offset!.X!.Value);

        // Everything drawn outside placeholders is white or in the highlight.
        var drawn = slides.SelectMany(s => s.Slide!.Descendants<P.Shape>()).Where(s => PlaceholderOf(s) is null);
        Assert.All(drawn.SelectMany(s => s.Descendants<A.RunProperties>()), r => Assert.Contains(r.GetFirstChild<A.SolidFill>()?.RgbColorModelHex?.Val?.Value ?? "inherited", new[] { "FFFFFF", "F5C04A" }));
    }

    [Fact]
    public void OnATemplateDeclaringTheRoles_ChartTitleUnitAndSourceGoIntoItsPlaceholders()
    {
        var roles = RolesTemplate.Roles with
        {
            Placeholders =
            [
                .. RolesTemplate.Roles.Placeholders,
                new(RolesTemplate.Chart, TemplateBuilder.ChartTitleName, PlaceholderRole.ChartTitle),
                new(RolesTemplate.Chart, TemplateBuilder.ChartUnitName, PlaceholderRole.ChartUnit),
                new(RolesTemplate.Chart, TemplateBuilder.ChartSourceName, PlaceholderRole.ChartSource),
            ],
        };
        var options = WithCharts(new PptxOptions { Template = new BrandTemplateOptions { Brand = RolesTemplate.Brand with { Pptx = new PptxBrandTemplate(RolesTemplate.Content) { Roles = roles } } } });
        var (pptx, result) = Generate("# Deck\n\n## Revenue {.chart}\n\n```chart\n```\n\n## {.quote}\n\n> Ship it.\n\n— Ann\n", options);
        Samples.WriteForReview("kinds-brand-roles.pptx", pptx);

        Samples.AssertValid(pptx);
        Assert.Empty(result.Notes);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal([RolesTemplate.Chart, RolesTemplate.Quote], slides.Skip(1).Select(Samples.LayoutName));
        Assert.Equal("Revenue", TextOf(slides[1], TemplateBuilder.ChartTitleName));
        Assert.Equal("EUR m", TextOf(slides[1], TemplateBuilder.ChartUnitName));
        Assert.Equal("Source: Annual report", TextOf(slides[1], TemplateBuilder.ChartSourceName));
        Assert.Equal("Ann", TextOf(slides[2], TemplateBuilder.BylineName));
    }

    [Fact]
    public void TwoCharts_GoOnTheTwoChartLayout_KeepingTheirTitlesAndSources()
    {
        var other = Revenue with { Title = "Costs", Source = "Ledger" };
        var charts = new Queue<ChartSpec>([Revenue, other]);
        var options = new PptxOptions { CodeBlockRenderers = [Samples.Renderer(r => r.Language == "chart" ? new ChartGraphic(charts.Dequeue(), new Drawing(100, 50, [])) : null)] };
        var (pptx, _) = Generate("# Deck\n\n## Money {.chart}\n\n```chart\n```\n\n```chart\n```\n", options);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal(TemplateBuilder.TwoContentLayoutName, Samples.LayoutName(slide));
        Assert.Equal(2, slide.ChartParts.Count());
        Assert.All(slide.ChartParts, c => Assert.NotEmpty(c.ChartSpace!.Descendants<DocumentFormat.OpenXml.Drawing.Charts.Title>()));
        Assert.Contains(slide.Slide!.Descendants<P.Shape>(), s => s.InnerText == "Source: Annual report");
        Assert.Contains(slide.Slide!.Descendants<P.Shape>(), s => s.InnerText == "Source: Ledger");
    }

    [Fact]
    public void SixPeople_ContinueOnASecondSlide_AndALongAgendaSplits()
    {
        var people = string.Concat(Enumerable.Range(1, 6).Select(i => $"- **Person {i}** – Role {i}\n"));
        var items = string.Concat(Enumerable.Range(1, 20).Select(i => $"- Point {i} of the agenda\n"));
        var (pptx, result) = Generate($"# Deck\n\n## Team {{.contact}}\n\n{people}\n## Agenda {{.agenda}}\n\n{items}", PptxOptions.Default);
        Samples.WriteForReview("kinds-continued.pptx", pptx);

        Samples.AssertValid(pptx);
        Assert.Equal(2, result.Notes.Count(n => n.Code == NoteCodes.SlideSplit));
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.SlideOverfull);
        using var package = Samples.Open(pptx);
        var slides = Samples.Slides(package);
        Assert.Equal(
            [TemplateBuilder.ContactLayoutName, TemplateBuilder.ContactLayoutName],
            slides.Skip(1).Take(2).Select(Samples.LayoutName));
        Assert.Contains(slides[2].Slide!.Descendants<P.Shape>(), s => s.InnerText == "Person 6Role 6");
        var agendas = slides.Skip(3).ToList();
        Assert.True(agendas.Count >= 2);
        Assert.All(agendas, s => Assert.Equal(TemplateBuilder.AgendaLayoutName, Samples.LayoutName(s)));

        // The continuation numbers on.
        var first = agendas[1].Slide!.Descendants<A.AutoNumberedBullet>().First();
        Assert.True(first.StartAt!.Value > 1);
    }

    [Fact]
    public void AConclusion_SitsAtTheFoot_AndOnASplitSlideOnTheLastPart()
    {
        var (pptx, _) = Generate("# Deck\n\n## Short\n\n- One point\n\n::: conclusion\nSo we go ahead.\n:::\n", PptxOptions.Default);
        var points = string.Concat(Enumerable.Range(1, 30).Select(i => $"- Point {i}\n"));
        var (split, result) = Generate($"# Deck\n\n## Long\n\n{points}\n::: conclusion\nSo we go ahead.\n:::\n", PptxOptions.Default);
        Samples.WriteForReview("conclusion.pptx", pptx);

        Samples.AssertValid(pptx);
        Samples.AssertValid(split);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal("Title Only", Samples.LayoutName(slide));
        var text = slide.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "So we go ahead.");
        Assert.True(text.Descendants<A.RunProperties>().Single().Bold!.Value);
        var band = slide.Slide.Descendants<P.Shape>().Where(s => s.InnerText.Length == 0 && PlaceholderOf(s) is null)
            .Select(s => s.ShapeProperties!.Transform2D!).OrderByDescending(t => t.Extents!.Cy!.Value).First();
        var point = slide.Slide.Descendants<P.Shape>().Single(s => s.InnerText == "One point").ShapeProperties!.Transform2D!;
        var bottom = band.Offset!.Y!.Value + band.Extents!.Cy!.Value;
        Assert.True(band.Offset.Y.Value > point.Offset!.Y!.Value + (2 * point.Extents!.Cy!.Value), "The band is at the foot, well below the text.");
        Assert.True(bottom > 5_000_000, $"The band ends at {bottom}.");

        Assert.Contains(result.Notes, n => n.Code == NoteCodes.SlideSplit);
        using var parts = Samples.Open(split);
        var slides = Samples.Slides(parts).Skip(1).ToList();
        Assert.True(slides.Count >= 2);
        Assert.DoesNotContain(slides[0].Slide!.Descendants<P.Shape>(), s => s.InnerText == "So we go ahead.");
        Assert.Contains(slides[^1].Slide!.Descendants<P.Shape>(), s => s.InnerText == "So we go ahead.");
    }

    [Fact]
    public void AComment_OverlapsTheTopRightOfItsBlock_InsideTheContentArea_AndMovesWithIt()
    {
        var (pptx, _) = Generate("# Deck\n\n## Status\n\nFirst paragraph.\n\nSecond paragraph.\n\n::: comment\nThis one matters.\n:::\n", PptxOptions.Default);
        Samples.WriteForReview("comment.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        var callout = slide.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "This one matters.");
        Assert.Equal(A.ShapeTypeValues.WedgeRoundRectangleCallout, callout.ShapeProperties!.GetFirstChild<A.PresetGeometry>()!.Preset!.Value);
        var box = callout.ShapeProperties.Transform2D!;
        var second = slide.Slide.Descendants<P.Shape>().Single(s => s.InnerText == "Second paragraph.").ShapeProperties!.Transform2D!;
        var first = slide.Slide.Descendants<P.Shape>().Single(s => s.InnerText == "First paragraph.").ShapeProperties!.Transform2D!;

        // The second paragraph is a text box of its own; the callout overlaps its top, at the right of the content area.
        Assert.True(second.Offset!.Y!.Value > first.Offset!.Y!.Value);
        Assert.True(box.Offset!.Y!.Value < second.Offset.Y.Value + second.Extents!.Cy!.Value);
        Assert.True(box.Offset.Y.Value + box.Extents!.Cy!.Value > second.Offset.Y.Value);
        Assert.Equal(second.Offset.X!.Value + second.Extents.Cx!.Value, box.Offset.X!.Value + box.Extents.Cx!.Value);
        Assert.True(box.Extents.Cx.Value >= 2286000);

        // On a split slide the commented block and its comment stay together.
        var points = string.Concat(Enumerable.Range(1, 26).Select(i => $"- Point {i}\n"));
        var (split, _) = Generate($"# Deck\n\n## Long\n\n{points}\nThe last word.\n\n::: comment\nRead this.\n:::\n", PptxOptions.Default);
        using var parts = Samples.Open(split);
        var last = Samples.Slides(parts)[^1].Slide!;
        Assert.Contains(last.Descendants<P.Shape>(), s => s.InnerText == "The last word.");
        Assert.Contains(last.Descendants<P.Shape>(), s => s.InnerText == "Read this.");
    }

    [Fact]
    public void TwoColumnsOfText_GoIntoADeclaredTwoContentLayout_AndNowhereElse()
    {
        const string Markdown = "# Deck\n\n## Two views\n\n::: columns\n**Before:** weekly releases.\n\n---\n\n**After:** daily.\n:::\n";

        var (declared, result) = Generate(Markdown, RolesTemplate.Options);
        var (builtIn, _) = Generate(Markdown, PptxOptions.Default);
        var (undeclared, _) = Generate(Markdown, DarkTemplate.Options);
        Samples.WriteForReview("kinds-two-columns.pptx", declared);

        Samples.AssertValid(declared);
        Assert.Empty(result.Notes);
        using (var package = Samples.Open(declared))
        {
            var slide = Samples.Slides(package)[1];
            Assert.Equal(RolesTemplate.TwoColumns, Samples.LayoutName(slide));

            // RolesTemplate declares "Content Placeholder 3" (idx 2) as its first body.
            Assert.Equal("Before: weekly releases.", TextOf(slide, "Content Placeholder 3"));
            Assert.Equal("After: daily.", TextOf(slide, "Content Placeholder 2"));
        }

        using (var package = Samples.Open(builtIn))
        {
            Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(package)[1]));
        }

        using (var package = Samples.Open(undeclared))
        {
            Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(package)[1]));
        }
    }

    [Fact]
    public void AKindWithLayout_UsesThatLayout_AndKeepsItsKicker()
    {
        var (pptx, _) = Generate("# Deck\n\n## One message {.statement kicker=\"Point\" layout=\"Statement\"}\n\nBelow.\n", RolesTemplate.Options);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal("Statement", Samples.LayoutName(slide));
        Assert.Equal("One message", Samples.PlaceholderText(slide, P.PlaceholderValues.Title));
        Assert.Contains(slide.Slide!.Descendants<P.Shape>(), s => s.InnerText == "Point" && PlaceholderOf(s) is null);
    }

    [Fact]
    public void AChartSlideWithoutARenderer_IsTheFlowSlideOfItsBlocks()
    {
        var (pptx, result) = Generate("# Deck\n\n## Revenue {.chart}\n\n```chart\nYear,Revenue\n2026,4\n```\n", PptxOptions.Default);

        Samples.AssertValid(pptx);
        Assert.Empty(result.Notes);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[1];
        Assert.Equal("Title Only", Samples.LayoutName(slide));
        Assert.Contains(slide.Slide!.Descendants<P.Shape>(), s => s.InnerText.Contains("2026,4", StringComparison.Ordinal));
    }
}
