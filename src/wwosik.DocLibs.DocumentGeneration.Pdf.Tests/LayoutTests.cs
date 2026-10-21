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
        var engine = new LayoutEngine(new FakeMetrics(), A4, new Design(Brand.Neutral, ShowClaim: false, null, HeaderTitle: "Done ✓"), _ => "[x]");

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

    private static LayoutEngine Engine(string? date = null, Func<string, string>? replace = null) =>
        new(new FakeMetrics(), A4, new Design(Brand.Neutral, ShowClaim: false, date, HeaderTitle: null), replace);

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
