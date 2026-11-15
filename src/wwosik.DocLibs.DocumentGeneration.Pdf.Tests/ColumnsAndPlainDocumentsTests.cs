using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>
/// <c>::: columns</c> side by side and plain documents without a cover or header (E03-S05), mostly with made-up widths:
/// every character is half as wide as the font size.
/// </summary>
public class ColumnsAndPlainDocumentsTests
{
    private const double Gap = 18;

    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static Layout Lay(DocumentModel model, bool plain = false, ResolvedBrand? brand = null, string? marking = null, string? watermark = null) =>
        new LayoutEngine(
            new FakeMetrics(),
            A4,
            new Design(brand ?? ResolvedBrand.Neutral, ShowTagline: false, "09.03.2026", "Report") { Plain = plain, Marking = marking, MarkingColor = "AA0000", Watermark = watermark })
            .Run(model);

    private static TextRun Run(string text) => new(text, RunStyle.None);

    private static ParagraphBlock Paragraph(string text) => new([Run(text)]);

    private static IEnumerable<Block> Filler(int count) => Enumerable.Range(1, count).Select(i => (Block)Paragraph("filler " + i));

    private static ColumnsBlock Columns(params IReadOnlyList<Block>[] columns) => new([.. columns.Select(_ => 1)], columns);

    private static IEnumerable<(int Page, TextItem Item)> Texts(Layout layout) =>
        layout.Pages.Index().SelectMany(p => p.Item.Items.OfType<TextItem>().Select(t => (p.Index, t)));

    private static (int Page, TextItem Item) Find(Layout layout, string text) => Texts(layout).First(t => t.Item.Text == text);

    private static DocumentModel Parse(string markdown, PdfOptions? options = null) =>
        MarkdownParser.Parse(Samples.Markdown(markdown), options ?? new PdfOptions());

    private static Cover Title(string text) => new([new TextRun(text, RunStyle.None)], [], "title");

    [Fact]
    public void TwoColumns_StandSideBySide_TopAligned_InEqualWidths()
    {
        var layout = Lay(new DocumentModel(null, [Columns([Paragraph("Left")], [Paragraph("Right")])]));

        var left = Find(layout, "Left").Item;
        var right = Find(layout, "Right").Item;
        Assert.Equal(left.Baseline, right.Baseline);
        Assert.Equal(A4.Left, left.X);
        Assert.Equal(A4.Left + ((A4.ContentWidth - Gap) / 2) + Gap, right.X, 6);
    }

    [Fact]
    public void TheRatio_SetsTheWidths_AndTextWrapsInItsColumn()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 60));
        var layout = Lay(new DocumentModel(null, [new ColumnsBlock([1, 2], [[Paragraph(words)], [Paragraph("Right")]])]));

        var second = A4.Left + ((A4.ContentWidth - Gap) / 3) + Gap;
        Assert.Equal(second, Find(layout, "Right").Item.X, 6);
        var leftLines = Texts(layout).Where(t => t.Item.Text.StartsWith("word", StringComparison.Ordinal)).ToList();
        Assert.True(leftLines.Select(t => t.Item.Baseline).Distinct().Count() > 3, "the long column wraps");
        Assert.All(leftLines, t => Assert.True(t.Item.X + (t.Item.Text.Length * t.Item.Size / 2) <= second - Gap + 1e-6, "inside the first column"));
    }

    [Fact]
    public void ThreeColumns_AndWhatFollows_StartsBelowTheTallest()
    {
        var layout = Lay(new DocumentModel(null, [
            Columns([Paragraph("A")], [Paragraph("B1"), Paragraph("B2"), Paragraph("B3")], [Paragraph("C")]),
            Paragraph("After"),
        ]));

        var (a, b, c) = (Find(layout, "A").Item, Find(layout, "B1").Item, Find(layout, "C").Item);
        Assert.Equal(a.Baseline, b.Baseline);
        Assert.Equal(a.Baseline, c.Baseline);
        Assert.True(a.X < b.X && b.X < c.X);
        var after = Find(layout, "After").Item;
        Assert.Equal(A4.Left, after.X);
        Assert.True(after.Baseline > Find(layout, "B3").Item.Baseline, "below the tallest column");
    }

    [Fact]
    public void ColumnsThatDoNotFit_MoveToTheNextPage_Together()
    {
        var moved = false;
        for (var fillers = 30; fillers < 60; fillers++)
        {
            var layout = Lay(new DocumentModel(null, [
                .. Filler(fillers),
                Columns([Paragraph("Short")], [.. Enumerable.Range(1, 8).Select(i => Paragraph("Tall " + i))]),
            ]));

            var (shortPage, shortItem) = Find(layout, "Short");
            Assert.All(Enumerable.Range(1, 8), i => Assert.Equal(shortPage, Find(layout, "Tall " + i).Page));
            Assert.Equal(shortItem.Baseline, Find(layout, "Tall 1").Item.Baseline);
            Assert.True(Find(layout, "Tall 8").Item.Baseline < A4.Bottom, "on the page");
            moved |= shortPage > Find(layout, "filler " + fillers).Page;
        }

        Assert.True(moved, "some columns move to the next page");
    }

    [Fact]
    public void ColumnsTallerThanAPage_FollowOneAnother()
    {
        var layout = Lay(new DocumentModel(null, [Columns([.. Filler(80)], [Paragraph("Right")])]));

        Assert.True(layout.Pages.Count > 1);
        var right = Find(layout, "Right");
        Assert.Equal(A4.Left, right.Item.X);
        Assert.Equal(Find(layout, "filler 80").Page, right.Page);
        Assert.True(right.Item.Baseline > Find(layout, "filler 80").Item.Baseline);
    }

    [Fact]
    public void ColumnsWithAPageBreak_FollowOneAnother()
    {
        var layout = Lay(new DocumentModel(null, [Columns([Paragraph("Left")], [new PageBreakBlock(), Paragraph("Right")])]));

        Assert.Equal(1, Find(layout, "Right").Page);
        Assert.Equal(A4.Left, Find(layout, "Right").Item.X);
    }

    [Fact]
    public void AHeadingAboveColumns_MovesWithThem()
    {
        var kept = false;
        for (var fillers = 30; fillers < 60; fillers++)
        {
            var layout = Lay(new DocumentModel(null, [
                .. Filler(fillers),
                new HeadingBlock(2, [Run("Parties")], "parties"),
                Columns([Paragraph("Seller")], [.. Enumerable.Range(1, 6).Select(i => Paragraph("Buyer " + i))]),
            ]));

            var heading = Find(layout, "Parties");
            Assert.Equal(heading.Page, Find(layout, "Seller").Page);
            kept |= heading.Page > Find(layout, "filler " + fillers).Page;
        }

        Assert.True(kept, "some headings move with their columns");
    }

    [Fact]
    public void AFootnoteInAColumn_IsAtTheFootOfItsPage()
    {
        var reference = new TextRun("1", RunStyle.Superscript) { Footnote = 1 };
        var model = new DocumentModel(null, [Columns([Paragraph("Left")], [new ParagraphBlock([Run("Right"), reference])])])
        {
            Footnotes = new Dictionary<int, FootnoteText> { [1] = new(1, [[Run("The note.")]], 3) },
        };

        var layout = Lay(model);

        var (page, note) = Find(layout, "The note.");
        Assert.Equal(0, page);
        Assert.Equal(A4.Left + 12, note.X);
        Assert.True(note.Baseline > A4.Bottom - 30);
        Assert.Single(layout.Pages[0].Items.OfType<LineItem>());
    }

    [Fact]
    public void ColumnsInAQuote_DrawItsRuleOnce()
    {
        var layout = Lay(new DocumentModel(null, [new QuoteBlock([Columns([Paragraph("Left")], [Paragraph("Right 1"), Paragraph("Right 2")])])]));

        var rule = Assert.Single(layout.Pages[0].Items.OfType<LineItem>());
        Assert.Equal(A4.Left + 3, rule.X1);
        Assert.True(rule.Y2 > Find(layout, "Right 2").Item.Baseline, "along the tallest column");
    }

    [Fact]
    public void TheParser_SplitsColumnsAtTheirRules()
    {
        var model = Parse("::: columns 1:2\nLeft\n\n---\n\nRight\n\nMore\n:::\n\nAfter\n");

        var columns = Assert.IsType<ColumnsBlock>(model.Blocks[0]);
        Assert.Equal([1, 2], columns.Widths);
        Assert.Equal(2, columns.Columns.Count);
        Assert.Single(columns.Columns[0]);
        Assert.Equal(2, columns.Columns[1].Count);
        Assert.IsType<ParagraphBlock>(model.Blocks[1]);
    }

    [Theory]
    [InlineData("::: columns\nOnly one\n:::\n", "holds two or three columns")]
    [InlineData("::: columns\nA\n\n---\n\nB\n\n---\n\nC\n\n---\n\nD\n:::\n", "holds two or three columns")]
    [InlineData("::: columns 1:2:1\nA\n\n---\n\nB\n:::\n", "gives 3 widths for 2 columns")]
    public void WrongColumns_AreErrors_AsInDecks(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainDocument_HasNoCoverAndNoHeader_AndStartsWithItsTitle()
    {
        var brand = ResolvedBrand.Resolve(Samples.Brand);
        var layout = Lay(new DocumentModel(Title("Invoice 7"), [Paragraph("Text"), new PageBreakBlock(), Paragraph("Second")]), plain: true, brand: brand);

        Assert.Equal(2, layout.Pages.Count);
        Assert.DoesNotContain(layout.Pages, p => p.IsCover);
        var title = Find(layout, "Invoice 7");
        Assert.Equal(0, title.Page);
        Assert.Equal(A4.Left, title.Item.X);
        Assert.Equal(20, title.Item.Size);
        Assert.Equal(brand.Primary, title.Item.Color);
        Assert.Equal(A4.Top + (20 * 1.05), title.Item.Baseline, 6);
        Assert.True(Find(layout, "Text").Item.Baseline > title.Item.Baseline);
        Assert.Equal(0, Find(layout, "Text").Page);

        // The logo beside the title, at the top right; no header title, no header logo.
        var logo = Assert.Single(layout.Pages[0].Items.OfType<ImageItem>());
        Assert.Equal(A4.Top, logo.Y);
        Assert.Equal(A4.Right, logo.X + logo.Width, 6);
        Assert.True(logo.Height <= 36 + 1e-6);
        Assert.Empty(layout.Pages[1].Items.OfType<ImageItem>());
        Assert.DoesNotContain(Texts(layout), t => t.Item.Baseline == 38);

        // The footer stays.
        Assert.Contains(Texts(layout), t => t.Page == 0 && t.Item.Text == "1 / 2");
        Assert.Contains(Texts(layout), t => t.Page == 1 && t.Item.Text == "2 / 2");
        Assert.Contains(Texts(layout), t => t.Page == 0 && t.Item.Text == "09.03.2026");
        Assert.Equal((0, A4.Top), layout.Anchors["title"]);
        Assert.Equal("Invoice 7", layout.Bookmarks[0].Title);
    }

    [Fact]
    public void APlainTitle_WrapsShortOfTheLogo_AndUsesTheFullWidthWithout()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("Title", 30));
        var withLogo = Lay(new DocumentModel(Title(long_), []), plain: true, brand: ResolvedBrand.Resolve(Samples.Brand));
        var without = Lay(new DocumentModel(Title(long_), []), plain: true);

        var logo = withLogo.Pages[0].Items.OfType<ImageItem>().Single();
        Assert.All(Texts(withLogo).Where(t => t.Item.Size == 20), t => Assert.True(t.Item.X + (t.Item.Text.Length * 10) <= logo.X - 12 + 1e-6));
        Assert.Empty(without.Pages[0].Items.OfType<ImageItem>());
        static int Lines(Layout layout) => Texts(layout).Where(t => t.Item.Size == 20).Select(t => t.Item.Baseline).Distinct().Count();
        Assert.True(Lines(without) < Lines(withLogo), "fewer lines without a logo");
    }

    [Fact]
    public void APlainDocument_KeepsItsMarking_AtTheTopRightOfEveryPage()
    {
        var layout = Lay(new DocumentModel(Title("Invoice"), [Paragraph("Text"), new PageBreakBlock(), Paragraph("Second")]), plain: true, brand: ResolvedBrand.Resolve(Samples.Brand), marking: "Confidential", watermark: "DRAFT");

        Assert.All(layout.Pages, page =>
        {
            var marking = Assert.Single(page.Items.OfType<TextItem>(), t => t.Text == "Confidential");
            Assert.Equal(38, marking.Baseline);
            Assert.Equal(A4.Right, marking.X + ("Confidential".Length * 4), 6);
            Assert.Equal("DRAFT", Assert.IsType<TextItem>(page.Items[0]).Text);
        });
    }

    [Fact]
    public void LayoutPlain_InTheFrontMatter_MakesNoCover_AndKeepsTheParagraphsAfterTheTitleAsText()
    {
        var model = Parse("# Invoice\n\nIssued today\n\n## Items\n", new PdfOptions { Layout = PdfLayout.Plain });

        Assert.NotNull(model.Cover);
        Assert.Empty(model.Cover.Subtitle);
        Assert.IsType<ParagraphBlock>(model.Blocks[0]);

        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\nlayout: plain\n---\n# Invoice\n\nIssued today\n\n## Items\n\nText\n")));
        Assert.Equal(1, document.NumberOfPages);
        var text = string.Concat(document.GetPage(1).Letters.Select(l => l.Value));
        Assert.StartsWith("InvoiceIssued", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("1 / 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainDocument_PutsItsContentsAfterTheTitle()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\nlayout: plain\ntoc: true\n---\n# Invoice\n\n## Parties\n\nText\n\n## Items\n\nMore\n")));

        Assert.Equal(1, document.NumberOfPages);
        var text = string.Concat(document.GetPage(1).Letters.Select(l => l.Value)).Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.StartsWith("InvoiceContentsParties", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutReport_IsTheDefault()
    {
        const string markdown = "# Report\n\nSubtitle\n\n## Part\n\nText\n";

        static string Pages(byte[] pdf)
        {
            using var document = PdfDocument.Open(pdf);
            return string.Join("\n", document.GetPages().Select(p => string.Concat(p.Letters.Select(l => $"{l.Value}{l.StartBaseLine.X:0.##},{l.StartBaseLine.Y:0.##}"))));
        }

        Assert.Equal(Pages(Samples.Generate(Samples.Markdown(markdown))), Pages(Samples.Generate(Samples.Markdown("---\nlayout: report\n---\n" + markdown))));
    }

    [Fact]
    public void AnUnknownLayout_IsAnError()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("---\nlayout: letter\n---\nText\n")));

        Assert.Contains("'layout'", error.Message, StringComparison.Ordinal);
        Assert.Contains("plain", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewPage_OnAPlainTitle_IsAnError()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("---\nlayout: plain\n---\n# Invoice {.newpage}\n\nText\n")));

        Assert.Contains("the title starts the first page", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Invoice_RoundTripsThroughThePdfConverter(string template)
    {
        var pdf = Samples.Generate(Samples.Document("invoice.md"), new PdfOptions { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"invoice-{template}.pdf", pdf);

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(2, document.NumberOfPages);
        var first = document.GetPage(1);
        var seller = first.Letters.First(l => l.Value == "S");
        var buyer = first.Letters.First(l => l.Value == "B");
        Assert.Equal(seller.StartBaseLine.Y, buyer.StartBaseLine.Y, 1);
        Assert.True(buyer.StartBaseLine.X > first.Width / 2, "the buyer stands right of the seller");
        Assert.Equal(template == "sample" ? 1 : 0, first.GetImages().Count());
        var raw = Encoding.Latin1.GetString(pdf);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", raw, StringComparison.Ordinal);

        Samples.AssertMatchesExpected($"invoice-{template}.roundtrip", Samples.RoundTrip(pdf));
    }

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
