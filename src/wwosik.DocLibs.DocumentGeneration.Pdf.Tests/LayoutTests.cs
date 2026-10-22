using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>
/// The layout with made-up widths: every character is half as wide as the font size; the fonts lack ✓ and everything
/// outside the BMP, and Mono also lacks ‖.
/// </summary>
public class LayoutTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    [Fact]
    public void Wrap_BreaksAtSpaces_AcrossStyles()
    {
        var lines = Engine().Wrap([Run("aaa "), new TextRun("bbb", RunStyle.Bold), Run(" ccc")], 40, 10, RunStyle.None);

        Assert.Equal(["aaa bbb", "ccc"], lines.Select(Text));
    }

    [Fact]
    public void Wrap_BreaksAWordWiderThanTheLineByCharacters()
    {
        var lines = Engine().Wrap([Run("abcdefghij")], 20, 10, RunStyle.None);

        Assert.Equal(["abcd", "efgh", "ij"], lines.Select(Text));
    }

    [Fact]
    public void Wrap_HonoursHardBreaks()
    {
        var lines = Engine().Wrap([Run("one"), TextRun.Break, Run("two")], 400, 10, RunStyle.None);

        Assert.Equal(["one", "two"], lines.Select(Text));
    }

    [Fact]
    public void ColumnWidths_GiveEachColumnItsLongestWord_AndShareTheRestByContent()
    {
        var table = Table(["a", "bbbbbbbbbb"], ["a a a a a a a a a a a a a a a a a a a a", "b"]);

        var widths = Engine().ColumnWidths(table, 200);

        Assert.Equal(200, widths.Sum(), 3);
        Assert.True(widths[1] >= (10 * 4.75) + 8, $"{widths[1]}"); // "bbbbbbbbbb" at 9.5 pt plus padding
        Assert.True(widths[0] > widths[1]);
    }

    [Fact]
    public void Heading_IsKeptWithWhatFollows()
    {
        var blocks = Filler(43).Append(new HeadingBlock(2, [Run("Heading")], null)).Append(new ParagraphBlock([Run("after")])).ToList();

        var layout = Engine().Run(new DocumentModel(null, blocks));

        Assert.Equal(PageOf(layout, "Heading"), PageOf(layout, "after"));
        Assert.Equal(1, PageOf(layout, "Heading"));
    }

    [Fact]
    public void LaterTopHeadings_StartANewPage()
    {
        var layout = Engine().Run(new DocumentModel(null, [new HeadingBlock(1, [Run("One")], null), new HeadingBlock(1, [Run("Two")], null)]));

        Assert.Equal(0, PageOf(layout, "One"));
        Assert.Equal(1, PageOf(layout, "Two"));
    }

    [Fact]
    public void Table_RepeatsItsHeaderOnEveryPage()
    {
        var rows = new[] { new[] { "Header" } }.Concat(Enumerable.Range(1, 120).Select(i => new[] { "row" + i })).ToArray();

        var layout = Engine().Run(new DocumentModel(null, [Table(rows)]));

        Assert.True(layout.Pages.Count > 1);
        Assert.All(layout.Pages, p => Assert.Single(p.Items.OfType<TextItem>(), t => t.Text == "Header"));
        Assert.Equal(Enumerable.Range(1, 120).Select(i => "row" + i), layout.Pages.SelectMany(p => p.Items.OfType<TextItem>()).Select(t => t.Text).Where(t => t.StartsWith("row", StringComparison.Ordinal)));
    }

    [Fact]
    public void Table_SplitsARowTallerThanAPage()
    {
        var tall = string.Join(' ', Enumerable.Range(1, 3000).Select(i => "w" + i));

        var layout = Engine().Run(new DocumentModel(null, [Table(["Key", "Value"], ["tall", tall])]));

        Assert.True(layout.Pages.Count >= 2);
        Assert.All(layout.Pages, p => Assert.Contains(p.Items.OfType<TextItem>(), t => t.Text == "Value"));
        var words = layout.Pages.SelectMany(p => p.Items.OfType<TextItem>()).SelectMany(t => t.Text.Split(' ')).Where(w => w.StartsWith('w')).ToList();
        Assert.Equal(3000, words.Count);
    }

    [Fact]
    public void Image_ThatDoesNotFit_MovesToTheNextPage()
    {
        var image = new ImageBlock(new DocumentResource("a.png", "image/png", new byte[] { 1 }), new ImageInfo(ImageFormat.Png, 400, 300), 1.0);

        var layout = Engine().Run(new DocumentModel(null, [.. Filler(33), image]));

        var placed = layout.Pages.Select((p, i) => (Page: i, Image: p.Items.OfType<ImageItem>().SingleOrDefault())).Single(p => p.Image is not null);
        Assert.Equal(1, placed.Page);
        Assert.Equal(A4.Top, placed.Image!.Y);
        Assert.Equal(300, placed.Image.Width, 3); // 400 px at 96 DPI
    }

    [Fact]
    public void Image_TallerThanAPage_IsScaledDown()
    {
        var image = new ImageBlock(new DocumentResource("a.png", "image/png", new byte[] { 1 }), new ImageInfo(ImageFormat.Png, 100, 4000), 1.0);

        var item = Engine().Run(new DocumentModel(null, [image])).Pages.Single().Items.OfType<ImageItem>().Single();

        Assert.Equal(A4.Bottom - A4.Top, item.Height, 3);
    }

    [Fact]
    public void Bookmarks_TakeTheCoverAndHeadingLevels()
    {
        var model = new DocumentModel(
            new Cover([Run("Cover")], [], "cover"),
            [new HeadingBlock(2, [Run("Two")], "two"), new HeadingBlock(3, [Run("Three")], null), new HeadingBlock(1, [Run("One")], "one")]);

        var layout = Engine().Run(model);

        Assert.Equal([("Cover", 0, 0), ("Two", 1, 1), ("Three", 2, 1), ("One", 0, 2)], layout.Bookmarks.Select(b => (b.Title, b.Level, b.Page)));
        Assert.Equal([0, 1, 2], new[] { "cover", "two", "one" }.Select(a => layout.Anchors[a].Page));
    }

    [Fact]
    public void Footer_NumbersEveryPageButTheCover()
    {
        var layout = Engine(date: "09.03.2026").Run(new DocumentModel(new Cover([Run("Cover")], [], null), [new ParagraphBlock([Run("text")])]));

        Assert.DoesNotContain(layout.Pages[0].Items.OfType<TextItem>(), t => t.Text.Contains(" / ", StringComparison.Ordinal));
        Assert.Contains(layout.Pages[1].Items.OfType<TextItem>(), t => t.Text == "2 / 2");
        Assert.Contains(layout.Pages[1].Items.OfType<TextItem>(), t => t.Text == "09.03.2026");
    }

    [Fact]
    public void MissingGlyph_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Engine().Wrap([Run("a✓")], 100, 10, RunStyle.None));

        Assert.Contains("U+2713", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Replacement_GoesWhereTheCharacterWas_InHeadingsTablesCodeAndTheFooter()
    {
        var calls = new List<string>();
        var engine = Engine(date: "✓", replace: c => { calls.Add(c); return "[x]"; });
        var model = new DocumentModel(null,
        [
            new HeadingBlock(2, [Run("Done ✓")], "done"),
            Table(["Head ✓"], ["cell ✓"]),
            new CodeBlock(["a ‖ b"]),
            new ParagraphBlock([Run("body ‖")]),
            .. Filler(60),
        ]);

        var layout = engine.Run(model);

        var texts = layout.Pages.SelectMany(p => p.Items.OfType<TextItem>()).Select(t => t.Text).ToList();
        Assert.Contains("Done [x]", texts);
        Assert.Contains("Head [x]", texts);
        Assert.Contains("cell [x]", texts);
        Assert.Contains("a [x] b", texts);
        Assert.Contains("body ‖", texts);
        Assert.True(layout.Pages.Count > 1);
        Assert.Equal(layout.Pages.Count, texts.Count(t => t == "[x]")); // the date, on every page
        Assert.Equal(["✓", "✓", "✓", "‖", "✓"], calls); // once per occurrence, the footer once for all pages
    }

    [Fact]
    public void Replacement_ShowsInTheBookmark_AndTheAnchorStays()
    {
        var engine = Engine(replace: _ => "[x]");

        var layout = engine.Run(new DocumentModel(new Cover([Run("Cover ✓")], [], "cover-"), [new HeadingBlock(2, [Run("Done ✓")], "done-")]));

        Assert.Equal(["Cover [x]", "Done [x]"], layout.Bookmarks.Select(b => b.Title));
        Assert.Equal(["cover-", "done-"], layout.Anchors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Replacement_AlsoGivesTheTitle()
    {
        var engine = new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, HeaderTitle: "Done ✓"), _ => "[x]");

        var layout = engine.Run(new DocumentModel(null, [new ParagraphBlock([Run("text")])]));

        Assert.Equal("Done [x]", layout.Title);
        Assert.Contains(layout.Pages[0].Items.OfType<TextItem>(), t => t.Text == "Done [x]");
    }

    [Fact]
    public void Replacement_ThatTheFontLacksToo_Throws()
    {
        var engine = Engine(replace: _ => "✓");

        var error = Assert.Throws<DocumentConversionException>(() => engine.Run(new DocumentModel(null, [new ParagraphBlock([Run("a ✓")])])));

        Assert.Contains("The replacement '✓' for '✓' has the character '✓' (U+2713)", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_BreaksALongWordBetweenTextElements()
    {
        var lines = Engine().Wrap([Run("ae\u0301ioe\u0301u")], 15, 10, RunStyle.None);

        Assert.Equal(["ae\u0301", "io", "e\u0301u"], lines.Select(Text));
    }

    [Fact]
    public void Code_BreaksBetweenTextElements()
    {
        var code = "x" + string.Concat(Enumerable.Repeat("e\u0301", 200));

        var layout = Engine().Run(new DocumentModel(null, [new CodeBlock([code])]));

        var lines = layout.Pages[0].Items.OfType<TextItem>().Where(t => t.Face == FontFace.Mono).Select(t => t.Text).ToList();
        Assert.True(lines.Count > 1);
        Assert.Equal(code, string.Concat(lines));
        Assert.All(lines.Skip(1), l => Assert.StartsWith("e\u0301", l, StringComparison.Ordinal));
    }

    [Fact]
    public void Drawing_IsShrunkToTheContentWidth_WithItsLinesAndDashes()
    {
        var drawing = new Drawing(2000, 100, [new DrawingShape(DrawingPath.Rectangle(0, 0, 2000, 100), null, "000000", 4) { Dash = [8, 4] }]);

        var layout = Engine().Run(new DocumentModel(null, [new DrawingBlock(drawing)]));

        var shape = Assert.Single(layout.Pages[0].Items.OfType<ShapeItem>());
        var scale = A4.ContentWidth / 2000;
        Assert.Equal((Math.Round(A4.Left, 6), A4.Top), Point(shape.Path[0]));
        Assert.Equal(A4.Right, ((LineTo)shape.Path[1]).X, 6);
        Assert.Equal(A4.Top + (100 * scale), ((LineTo)shape.Path[2]).Y, 6);
        Assert.Equal(4 * scale, shape.Width, 6);
        Assert.Equal([Math.Round(8 * scale, 6), Math.Round(4 * scale, 6)], shape.Dash!.Select(d => Math.Round(d, 6)));
    }

    [Fact]
    public void SmallDrawing_IsCentred_AtAPixelPerThreeQuarterPoint()
    {
        var drawing = new Drawing(100, 40, [new DrawingShape(DrawingPath.Rectangle(0, 0, 100, 40), "FF0000", null, 0)]);

        var layout = Engine().Run(new DocumentModel(null, [new DrawingBlock(drawing)]));

        var shape = Assert.Single(layout.Pages[0].Items.OfType<ShapeItem>());
        Assert.Equal((Math.Round(A4.Left + ((A4.ContentWidth - 75) / 2), 6), A4.Top), Point(shape.Path[0]));
        Assert.Equal((Math.Round(A4.Left + ((A4.ContentWidth + 75) / 2), 6), A4.Top), Point(shape.Path[1]));
    }

    [Fact]
    public void TallDrawing_FitsThePage_AndMovesToTheNextOneIfNeeded()
    {
        var drawing = new Drawing(100, 5000, [new DrawingShape(DrawingPath.Rectangle(0, 0, 100, 5000), "FF0000", null, 0)]);

        var layout = Engine().Run(new DocumentModel(null, [new ParagraphBlock([Run("before")]), new DrawingBlock(drawing)]));

        var shape = Assert.Single(layout.Pages[1].Items.OfType<ShapeItem>());
        Assert.Equal(A4.Top, ((MoveTo)shape.Path[0]).Y, 6);
        Assert.Equal(A4.Bottom, ((LineTo)shape.Path[2]).Y, 6);
    }

    [Theory]
    [InlineData(50, 10.5)]  // 75 pt of text in 37.5 pt: shrunk, but not below 70 %
    [InlineData(90, 13.5)]  // in 67.5 pt: shrunk to fit
    [InlineData(200, 15)]   // fits
    public void Labels_ShrinkToTheirRoom_DownTo70Percent(double room, double size)
    {
        var text = new DrawingText(100, 50, "abcdefghij", 20, "111111") { Anchor = TextAnchor.Middle, MaxWidth = room };

        var layout = Engine().Run(new DocumentModel(null, [new DrawingBlock(new Drawing(200, 100, [text]))]));

        var item = Assert.Single(layout.Pages[0].Items.OfType<TextItem>(), t => t.Text == "abcdefghij");
        var left = A4.Left + ((A4.ContentWidth - 150) / 2);
        Assert.Equal(size, item.Size, 6);
        Assert.Equal(left + 75 - (10 * size * 0.5 / 2), item.X, 6);
        Assert.Equal(A4.Top + 37.5, item.Baseline, 6);
        Assert.Equal(("111111", FontFace.TableRegular), (item.Color, item.Face));
    }

    [Fact]
    public void LabelSpans_FollowEachOther_InTheirFaces()
    {
        var text = new DrawingText(0, 20, [new DrawingSpan("ab", Bold: true), new DrawingSpan("cd")], 20, "111111");
        var mono = new DrawingText(0, 40, "x", 20, "111111") { Mono = true };

        var layout = Engine().Run(new DocumentModel(null, [new DrawingBlock(new Drawing(100, 50, [text, mono]))]));

        var items = layout.Pages[0].Items.OfType<TextItem>().Where(t => t.Text is "ab" or "cd" or "x").ToList();
        Assert.Equal([FontFace.TableBold, FontFace.TableRegular, FontFace.Mono], items.Select(i => i.Face));
        Assert.Equal(items[0].X + (2 * 15 * 0.5), items[1].X, 6);
    }

    [Fact]
    public void LabelCharacters_TheFontLacks_Throw()
    {
        var drawing = new Drawing(100, 50, [new DrawingText(0, 20, "done ✓", 12, "111111")]);

        var error = Assert.Throws<DocumentConversionException>(() => Engine().Run(new DocumentModel(null, [new DrawingBlock(drawing)])));

        Assert.Contains("(TableRegular)", error.Reason, StringComparison.Ordinal);
    }

    public static TheoryData<string> FollowingBlocks => ["drawing", "image", "table", "code", "list", "quote", "paragraph", "heading and drawing", "rule"];

    [Theory]
    [MemberData(nameof(FollowingBlocks))]
    public void Headings_StayWithTheStartOfWhatFollows_WhereverThePageBreaks(string following)
    {
        for (var fillers = 0; fillers < 60; fillers++)
        {
            var layout = Engine().Run(new DocumentModel(null, [.. Filler(fillers), new HeadingBlock(2, [Run("Heading")], null), .. Following(following)]));

            var heading = PageOf(layout, "Heading");
            var content = following switch
            {
                "drawing" or "heading and drawing" => layout.Pages.ToList().FindIndex(p => p.Items.OfType<ShapeItem>().Any()),
                "image" => layout.Pages.ToList().FindIndex(p => p.Items.OfType<ImageItem>().Any()),
                "rule" => layout.Pages.ToList().FindIndex(p => p.Items.OfType<LineItem>().Any(l => l.Width == 0.75)),
                _ => PageOf(layout, "first"),
            };
            Assert.True(heading == content, $"With {fillers} fillers the heading is on page {heading} and the {following} starts on page {content}.");
            if (following == "heading and drawing")
            {
                Assert.Equal(heading, PageOf(layout, "Second"));
            }
        }
    }

    [Fact]
    public void AVisualTooTallForItsHeading_IsShrunkToShareThePage()
    {
        var drawing = new Drawing(100, 5000, [new DrawingShape(DrawingPath.Rectangle(0, 0, 100, 5000), "FF0000", null, 0)]);

        var layout = Engine().Run(new DocumentModel(null, [new ParagraphBlock([Run("before")]), new HeadingBlock(2, [Run("Heading")], null), new HeadingBlock(3, [Run("Second")], null), new DrawingBlock(drawing)]));

        var shape = Assert.Single(layout.Pages[1].Items.OfType<ShapeItem>());
        Assert.Equal(1, PageOf(layout, "Heading"));
        Assert.Equal(1, PageOf(layout, "Second"));
        Assert.Equal(A4.Bottom, Math.Round(((LineTo)shape.Path[2]).Y, 6));
        Assert.True(((MoveTo)shape.Path[0]).Y > A4.Top + 30, "Below both headings.");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(120)]
    public void Paragraphs_NeverLeaveASingleLineOnAPage(int count)
    {
        for (var fillers = 0; fillers < 60; fillers++)
        {
            var layout = Engine().Run(new DocumentModel(null, [.. Filler(fillers), Lines(count)]));

            var perPage = Enumerable.Range(0, count)
                .Select(i => PageOf(layout, $"line{i:000}"))
                .GroupBy(page => page)
                .Select(g => g.Count())
                .ToList();
            Assert.True(perPage.All(n => n >= 2), $"With {fillers} fillers, {count} lines split as {string.Join('+', perPage)}.");
            Assert.True(count > 3 || perPage.Count == 1, $"With {fillers} fillers, {count} lines split as {string.Join('+', perPage)}.");
        }
    }

    [Fact]
    public void ListMarkers_MoveWithTheirFirstLine()
    {
        for (var fillers = 0; fillers < 60; fillers++)
        {
            var layout = Engine().Run(new DocumentModel(null, [.. Filler(fillers), new ListBlock(false, 1, [[Lines(3)]])]));

            Assert.Equal(PageOf(layout, "•"), PageOf(layout, "line000"));
        }
    }

    // One word per line, through hard breaks.
    private static ParagraphBlock Lines(int count) =>
        new([.. Enumerable.Range(0, count).SelectMany(i => i == 0 ? [Run($"line{i:000}")] : new[] { TextRun.Break, Run($"line{i:000}") })]);

    private static IEnumerable<Block> Following(string kind) => kind switch
    {
        "drawing" => [new DrawingBlock(new Drawing(400, 300, [new DrawingShape(DrawingPath.Rectangle(0, 0, 400, 300), "FF0000", null, 0)]))],
        "image" => [new ImageBlock(new DocumentResource("i.png", "image/png", new byte[1]), new ImageInfo(ImageFormat.Png, 400, 300), 1)],
        "table" => [Table(["first", "b"], ["c", "d"], ["e", "f"])],
        "code" => [new CodeBlock(["first", "second", "third"])],
        "list" => [new ListBlock(false, 1, [[new ParagraphBlock([Run("first"), TextRun.Break, Run("x"), TextRun.Break, Run("y"), TextRun.Break, Run("z")])]])],
        "quote" => [new QuoteBlock([new ParagraphBlock([Run("first"), TextRun.Break, Run("x"), TextRun.Break, Run("y")])])],
        "paragraph" => [new ParagraphBlock([Run("first"), TextRun.Break, Run("x"), TextRun.Break, Run("y")])],
        "heading and drawing" => [new HeadingBlock(3, [Run("Second")], null), .. Following("drawing")],
        _ => [new RuleBlock()],
    };

    private static (double X, double Y) Point(PathSegment segment) =>
        segment.Points.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).Single();

    private static LayoutEngine Engine(string? date = null, Func<string, string>? replace = null) =>
        new(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, date, HeaderTitle: null), replace);

    private static TextRun Run(string text) => new(text, RunStyle.None);

    private static string Text(TextLine line) => string.Concat(line.Pieces.Select(p => p.Text));

    private static IEnumerable<Block> Filler(int count) => Enumerable.Range(1, count).Select(i => new ParagraphBlock([Run("filler " + i)]));

    private static TableBlock Table(params string[][] rows) => new(
        [.. rows.Select(r => (IReadOnlyList<IReadOnlyList<TextRun>>)[.. r.Select(c => (IReadOnlyList<TextRun>)[Run(c)])])],
        [.. rows[0].Select(_ => CellAlignment.Default)],
        HasHeader: true);

    private static int PageOf(Layout layout, string text) =>
        layout.Pages.Select((p, i) => (p, i)).First(x => x.p.Items.OfType<TextItem>().Any(t => t.Text == text)).i;

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) =>
            character.IsBmp && character.Value != '✓' && !(face == FontFace.Mono && character.Value == '‖');
    }
}
