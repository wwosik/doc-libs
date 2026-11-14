using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary><c>qr:…</c> images and <c>:icon-name:</c> in PDFs.</summary>
public class IconAndQrTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static DocumentModel Parse(string markdown, PdfOptions? options = null, GenerationNotes? notes = null) =>
        MarkdownParser.Parse(Samples.Markdown(markdown), options ?? new PdfOptions(), notes: notes);

    private static Layout Lay(DocumentModel model) =>
        new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null)).Run(model);

    private static (GenerationResult Result, byte[] Pdf) Generate(string markdown, PdfOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown(markdown), output, (options ?? new PdfOptions()) with { TimeProvider = Samples.FixedTime });
        return (result, output.ToArray());
    }

    [Fact]
    public void AQrCode_IsTwoPaths_AWhiteSquareAndItsModules_WithItsCaptionUnder()
    {
        var model = Parse("![Survey](qr:https://example.com/survey \"caption\")\n");

        var drawing = Assert.IsType<DrawingBlock>(Assert.Single(model.Blocks)).Drawing;
        var items = Lay(model).Pages.Single().Items;
        var shapes = items.OfType<ShapeItem>().ToList();
        Assert.Equal(2, shapes.Count);
        Assert.Equal("FFFFFF", shapes[0].Fill);
        Assert.Equal(ResolvedBrand.Neutral.Primary, shapes[1].Fill);
        Assert.Contains(items.OfType<TextItem>(), t => t.Text == "Survey");

        // Natural size: 4 px a module, a pixel 0.75 pt.
        Assert.Equal(drawing.Width * 0.75, shapes[0].Path.OfType<LineTo>().Max(p => p.X) - shapes[0].Path.OfType<MoveTo>().Single().X, 3);
    }

    [Fact]
    public void AQrCode_StaysPdfA_AndCarriesItsCaption()
    {
        var (result, pdf) = Generate("# Report\n\nIntro.\n\n## Survey\n\n![Scan for the survey](qr:https://example.com/survey?id=3){.caption scale=1.5}\n");

        Assert.False(result.HasNotes, result.ToString());
        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        Assert.Contains("Scan for the survey", document.GetPage(2).Text, StringComparison.Ordinal);
        Samples.WriteForReview("qr-and-icons.pdf", Generate(Review).Pdf);
    }

    [Fact]
    public void AParagraphOfOneIcon_IsTheIconAlone_AtTheFigureSize_InTheAccentColour()
    {
        var model = Parse(":icon-bolt:\n\nText.\n");

        Assert.IsType<IconBlock>(model.Blocks[0]);
        var shape = Lay(model).Pages.Single().Items.OfType<ShapeItem>().Single();
        Assert.Equal(ResolvedBrand.Neutral.Primary, shape.Stroke);
        Assert.Null(shape.Fill);
        Assert.True(shape.RoundJoins);
        Assert.Equal(2 * 32 / 24.0, shape.Width, 6);
        Assert.True(shape.Path.OfType<MoveTo>().Min(p => p.X) >= A4.Left);
    }

    [Fact]
    public void AListItemStartingWithAnIcon_GetsTheIconForItsMarker()
    {
        var model = Parse("- :icon-check: Done\n- Open\n");

        var list = Assert.IsType<ListBlock>(Assert.Single(model.Blocks));
        Assert.NotNull(list.Icons![0]);
        Assert.Null(list.Icons[1]);
        var first = Assert.IsType<ParagraphBlock>(list.Items[0][0]);
        Assert.Equal("Done", Assert.Single(first.Runs).Text);

        var page = Lay(model).Pages.Single();
        var icon = page.Items.OfType<ShapeItem>().Single();
        Assert.Equal(ResolvedBrand.Neutral.Primary, icon.Stroke);
        var done = page.Items.OfType<TextItem>().Single(t => t.Text == "Done");
        Assert.True(icon.Path.OfType<MoveTo>().Max(p => p.X) < done.X, "the icon stands where the bullet would");
        Assert.Contains(page.Items.OfType<TextItem>(), t => t.Text == "•");
    }

    [Fact]
    public void AnInlineIcon_IsOneEmAndTwoThinSpaces_InTheTextColour_AndKeptWithTheWordAfterIt()
    {
        var runs = Assert.IsType<ParagraphBlock>(Assert.Single(Parse("All :icon-check: done.\n").Blocks)).Runs;

        var icon = runs.Single(r => r.Icon is not null);
        Assert.True(icon.Style.HasFlag(RunStyle.Icon));
        Assert.Equal(string.Empty, icon.Text);

        var engine = new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null));
        var line = Assert.Single(engine.Wrap(runs, 400, 12, RunStyle.None));
        Assert.Equal(12 * (1 + (2 / 6.0)), line.Pieces.Single(p => p.Icon is not null).Width, 6);

        // Too narrow for the line: the icon moves to the next line with "done.", not alone.
        var lines = engine.Wrap(runs, 60, 12, RunStyle.None);
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines[1].Pieces, p => p.Icon is not null);
        Assert.Contains(lines[1].Pieces, p => p.Text == "done.");

        var page = Lay(new DocumentModel(null, [new ParagraphBlock(runs)])).Pages.Single();
        Assert.Equal(ResolvedBrand.Neutral.ThemeColors[0], page.Items.OfType<ShapeItem>().Single().Stroke);
    }

    [Fact]
    public void IconsInHeadingsAndTableCells_AreLeftOut_AndReported()
    {
        var notes = new GenerationNotes();
        var model = Parse("## Status :icon-check:\n\n| A |\n|---|\n| :icon-x: no |\n", notes: notes);

        var heading = Assert.IsType<HeadingBlock>(model.Blocks[0]);
        Assert.Equal("Status", string.Concat(heading.Runs.Select(r => r.Text)));
        Assert.DoesNotContain(heading.Runs, r => r.Icon is not null);
        var table = Assert.IsType<TableBlock>(model.Blocks[1]);
        Assert.Equal("no", Assert.Single(table.Rows[1][0]).Text);
        var result = notes.Result(1);
        Assert.Equal([(NoteCodes.IconNotDrawn, 1), (NoteCodes.IconNotDrawn, 5)], result.Notes.Select(n => (n.Code, n.Line)));
    }

    [Fact]
    public void AnUnknownIcon_Throws_WithSuggestions()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Parse("Text :icon-chek:.\n"));

        Assert.Contains("Unknown icon 'chek' at line 1. Did you mean check", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithIconsSwitchedOff_IconTextStaysText()
    {
        var model = Parse("- :icon-check: Done :icon-chek:\n", new PdfOptions { Icons = null });

        var list = Assert.IsType<ListBlock>(Assert.Single(model.Blocks));
        Assert.Null(list.Icons);
        Assert.Equal(":icon-check: Done :icon-chek:", Assert.Single(((ParagraphBlock)list.Items[0][0]).Runs).Text);
    }

    [Fact]
    public void AnIconSetOfOurOwn_ReplacesTablers()
    {
        var dot = new Drawing(10, 10, [new DrawingShape(DrawingPath.Rectangle(0, 0, 10, 10), "000000", null, 0)]);
        var set = new OwnIcons(dot);

        var model = Parse(":icon-dot:\n", new PdfOptions { Icons = set });

        Assert.Same(dot, Assert.IsType<IconBlock>(Assert.Single(model.Blocks)).Icon);
        Assert.Equal(ResolvedBrand.Neutral.Primary, Lay(model).Pages.Single().Items.OfType<ShapeItem>().Single().Fill);
        Assert.Throws<DocumentConversionException>(() => Parse(":icon-check:\n", new PdfOptions { Icons = set }));
    }

    private const string Review = """
        # QR codes and icons

        Icons and QR codes, drawn as vectors.

        ## Survey

        ![Scan for the survey](qr:https://example.com/survey?id=3 "caption")

        ## Why us

        - :icon-bolt: Fast to set up, in minutes rather than days
        - :icon-shield-check: Safe by default
        - A plain item, with its bullet

        ## Cards

        :icon-heart:

        Loved by the people who use it. Inline icons :icon-check: sit on the line, :icon-arrow-right: at the height of
        a capital letter, and **bold :icon-star: text** or a [link :icon-external-link:](https://example.com) takes them in
        its colour.
        """;

    private sealed class OwnIcons(Drawing dot) : IIconSet
    {
        public IReadOnlyCollection<string> Names { get; } = ["dot"];

        public Drawing? Find(string name) => name == "dot" ? dot : null;
    }

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
