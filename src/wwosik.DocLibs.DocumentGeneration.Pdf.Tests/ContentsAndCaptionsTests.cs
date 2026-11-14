using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>The table of contents, heading numbers and captions of E03-S02; layouts with made-up widths.</summary>
public class ContentsAndCaptionsTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);
    private static readonly DocumentResource Photo = Samples.Image("photo.png");

    private static readonly PdfOptions Numbered = new() { HeadingNumbers = true };

    private static readonly PdfOptions WithContents = new() { TableOfContents = true, HeadingNumbers = true };

    // A `mermaid` block drawn as a red rectangle, so that a `Figure:` after it is a caption.
    private static readonly ICodeBlockRenderer Rectangles = Samples.Renderer(request => request.Language == "mermaid"
        ? new DrawingGraphic(new Drawing(400, 200, [new DrawingShape(DrawingPath.Rectangle(0, 0, 400, 200), "FF0000", null, 0)]))
        : null);

    private static DocumentModel Parse(string markdown, PdfOptions? options = null, GenerationNotes? notes = null, GeneratedTexts? texts = null) =>
        MarkdownParser.Parse(Samples.Markdown(markdown, Photo), options ?? new PdfOptions(), notes: notes, texts: texts);

    private static Layout Lay(DocumentModel model, GeneratedTexts? texts = null) =>
        new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null) { Texts = texts ?? GeneratedTexts.English }).Run(model);

    private static IEnumerable<HeadingBlock> Headings(DocumentModel model) => model.Blocks.OfType<HeadingBlock>();

    private static string Text(HeadingBlock heading) => string.Concat(heading.Runs.Select(r => r.Text));

    [Fact]
    public void HeadingNumbers_AreOffByDefault()
    {
        Assert.All(Headings(Parse("# Cover\n\n## A\n\n### B\n")), h => Assert.Null(h.Number));
    }

    [Fact]
    public void HeadingNumbers_NumberThreeLevelsFromTheTop_ButUnnumberedAndContainerHeadings()
    {
        var model = Parse("# Cover\n\n## A\n\n### B\n\n#### C\n\n##### D\n\n## E {.unnumbered}\n\n### F\n\n## G\n\n::: cards\n### Card\n\nText\n:::\n", Numbered);

        Assert.Equal(
            [("A", "1"), ("B", "1.1"), ("C", "1.1.1"), ("D", null), ("E", null), ("F", "1.2"), ("G", "2"), ("Card", null)],
            Headings(model).Select(h => (Text(h), h.Number)));
    }

    [Fact]
    public void HeadingNumbers_NeverCountASkippedLevelAsZero()
    {
        // As report.md: `##` sections before the first `#`; then a `###` straight under a `#`.
        var model = Parse("# Cover\n\n## Summary\n\n### Detail\n\n## Results\n\n# Outlook\n\n### Next\n", Numbered);

        Assert.Equal(["1", "1.1", "2", "3", "3.1"], Headings(model).Select(h => h.Number));
    }

    [Fact]
    public void Unnumbered_OnDeepHeadings_IsTakenOff_OtherBracesStayText()
    {
        var model = Parse("## A\n\n### Detail {.unnumbered}\n\n### Other {.dark}\n", Numbered);

        Assert.Equal([("A", "1"), ("Detail", null), ("Other {.dark}", "1.1")], Headings(model).Select(h => (Text(h), h.Number)));
    }

    [Fact]
    public void Headings_HangAfterTheirNumber_AndTheBookmarksCarryIt()
    {
        var title = string.Join(' ', Enumerable.Repeat("word", 40));
        var layout = Lay(Parse($"## {title}\n\n## Second\n", Numbered));
        var page = layout.Pages.Single().Items.OfType<TextItem>().ToList();

        var number = page.Single(t => t.Text == "1");
        var lines = page.Where(t => t.Text.StartsWith("word", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count >= 2);
        Assert.All(lines, l => Assert.Equal(lines[0].X, l.X, 6));
        Assert.True(lines[0].X > number.X + (number.Text.Length * number.Size * 0.5), "the title starts after the number");
        Assert.Equal(lines[0].Baseline, number.Baseline, 6);
        Assert.Equal(["1 " + title, "2 Second"], layout.Bookmarks.Select(b => b.Title));
    }

    [Fact]
    public void Contents_ListTheHeadingsToTheirDepth_WithLinksAndGeneratedAnchors()
    {
        var model = Parse("# Cover\n\n# One\n\n## Two {.unnumbered}\n\n### Three\n\n# Four\n\nText.[^1]\n\n[^1]: A note.\n", WithContents);

        var entries = model.Contents!.Entries;
        Assert.Equal([(1, "One", "1"), (2, "Two", null), (1, "Four", "2"), (1, "Notes", null)], entries.Select(e => (e.Level, Text(e.Heading), e.Heading.Number)));
        Assert.Equal("contents-1", entries[^1].Heading.Anchor);
        Assert.Equal(4, entries.Select(e => e.Heading.Anchor).Distinct().Count());
        Assert.True(entries[0].Chapter);
        Assert.False(entries[1].Chapter);

        var deeper = Parse("## A\n\n### B\n\n#### C\n\n##### D\n", WithContents with { TableOfContentsDepth = 3 });
        Assert.Equal(["A", "B", "C"], deeper.Contents!.Entries.Select(e => Text(e.Heading)));
        Assert.Null(Parse("Just text.\n", WithContents).Contents);
    }

    [Fact]
    public void Contents_ShowThePagesTheFooterShows_ForEveryEntry()
    {
        var filler = string.Concat(Enumerable.Range(1, 70).Select(i => $"Paragraph {i}.\n\n"));
        var markdown = $"# Cover\n\nSubtitle\n\n# One\n\n{filler}## Two\n\n{filler}### Three\n\n# Four\n\n{filler}";
        foreach (var depth in new[] { 1, 2, 3 })
        {
            var model = Parse(markdown, WithContents with { TableOfContentsDepth = depth });
            var layout = Lay(model);

            // The cover, then the contents, then the body on a page of its own.
            Assert.False(layout.Pages[1].IsCover);
            Assert.Contains(layout.Pages[1].Items.OfType<TextItem>(), t => t.Text == "Contents");
            Assert.Equal(2, layout.Anchors["one"].Page);
            var links = layout.Pages[1].Items.OfType<LinkItem>().ToList();
            Assert.Equal(model.Contents!.Entries.Select(e => "#" + e.Heading.Anchor), links.Select(l => l.Target));
            Assert.Equal(depth switch { 1 => 2, 2 => 3, _ => 4 }, links.Count);
            foreach (var link in links)
            {
                var number = layout.Pages[1].Items.OfType<TextItem>()
                    .Single(t => t.Baseline > link.Y && t.Baseline < link.Y + link.Height && t.X > A4.Right - 30 && char.IsDigit(t.Text[0]));
                Assert.Equal(layout.Anchors[link.Target[1..]].Page + 1, int.Parse(number.Text, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(A4.Right, number.X + (number.Text.Length * number.Size * 0.5), 6);
                Assert.Contains(layout.Pages[1].Items.OfType<TextItem>(), t => Math.Abs(t.Baseline - number.Baseline) < 1e-6 && t.Text.StartsWith(". .", StringComparison.Ordinal));
            }

            Assert.Contains(layout.Bookmarks, b => b.Title == "Contents" && b.Page == 1);
            Assert.DoesNotContain(model.Contents.Entries, e => Text(e.Heading) == "Contents");
        }
    }

    [Fact]
    public void Contents_WithoutACover_AreOnTheFirstPage_AndLongTitlesWrapBeforeTheNumbers()
    {
        var title = string.Join(' ', Enumerable.Repeat("word", 60));
        var layout = Lay(Parse($"## {title}\n\nText.\n", WithContents));
        var contents = layout.Pages[0].Items.OfType<TextItem>().ToList();

        Assert.Contains(contents, t => t.Text == "Contents");
        Assert.Equal(1, layout.Anchors[layout.Pages[0].Items.OfType<LinkItem>().Single().Target[1..]].Page);
        var words = contents.Where(t => t.Text.StartsWith("word", StringComparison.Ordinal)).ToList();
        Assert.True(words.Count >= 2);
        Assert.All(words, w => Assert.True(w.X + (w.Text.Length * w.Size * 0.5) <= A4.Right - (3 * 10.5 * 0.5) - 6 + 1e-6));
    }

    [Fact]
    public void Contents_ComeInTheDocumentLanguage()
    {
        var layout = Lay(Parse("# Cover\n\n## A\n", WithContents, texts: GeneratedTexts.German), GeneratedTexts.German);

        Assert.Contains(layout.Pages[1].Items.OfType<TextItem>(), t => t.Text == "Inhalt");
    }

    [Fact]
    public void AnEmptyTableOfContents_IsLeftOut_WithANote()
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown("---\ntoc: true\n---\n# Cover\n\nJust text.\n"), output, new PdfOptions { TimeProvider = Samples.FixedTime });

        var note = Assert.Single(result.Notes);
        Assert.Equal((NoteCodes.TocEmpty, 2), (note.Code, note.Line));
        Assert.Equal(1, result.Pages); // the cover alone, its subtitle "Just text."
    }

    [Fact]
    public void ImageTitles_AndCaptionParagraphs_AreNumberedCaptions_FiguresAndTablesApart()
    {
        var notes = new GenerationNotes();
        var model = Parse(
            "![Sales](photo.png \"Sales by region\")\n\n![Team](photo.png \"caption\")\n\n![Small](photo.png \"scale=0.5\")\n\n"
            + "| a | b |\n| - | - |\n| 1 | 2 |\n\nTable: Results, see [here](https://example.com)\n\n"
            + "```mermaid\ngraph TD\n```\n\nFigure: The flow\n\nTable: Nothing above\n",
            new PdfOptions { CodeBlockRenderers = [Rectangles] },
            notes);

        static string Describe(Caption? caption) => caption is null ? "-" : $"{caption.Kind} {caption.Number}: {string.Concat(caption.Text.Select(r => r.Text))}";
        Assert.Equal(
            ["Figure 1: Sales by region", "Figure 2: Team", "-", "Table 1: Results, see here", "Figure 3: The flow"],
            model.Blocks.Select(b => b switch
            {
                ImageBlock image => Describe(image.Caption),
                TableBlock table => Describe(table.Caption),
                DrawingBlock drawing => Describe(drawing.Caption),
                _ => null,
            }).OfType<string>());
        Assert.Contains(Assert.IsType<TableBlock>(model.Blocks[3]).Caption!.Text, r => r.Link == "https://example.com");

        // The caption paragraphs are taken; the one that follows nothing it captions stays, with its prefix and a note.
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(model.Blocks, b => b is ParagraphBlock));
        Assert.Equal("Table: Nothing above", string.Concat(paragraph.Runs.Select(r => r.Text)));
        var note = Assert.Single(notes.Result(0).Notes);
        Assert.Equal((NoteCodes.CaptionIgnored, 19), (note.Code, note.Line));
    }

    [Fact]
    public void ACaptionParagraph_AfterAnImageOrACodeBlockLeftAsCode_IsText()
    {
        var notes = new GenerationNotes();
        var model = Parse("![A](photo.png)\n\nFigure: Not a caption\n\n```mermaid\ngraph TD\n```\n\nFigure: Neither\n", notes: notes);

        Assert.Equal(2, model.Blocks.OfType<ParagraphBlock>().Count());
        Assert.Equal([NoteCodes.CaptionIgnored, NoteCodes.CaptionIgnored], notes.Result(0).Notes.Select(n => n.Code));
    }

    [Fact]
    public void FigureCaptions_GoUnderTheirVisual_OnItsPage()
    {
        // An image that fills what is left of the page but for a line moves to the next page with its caption.
        for (var fillers = 0; fillers < 50; fillers += 7)
        {
            var filler = string.Concat(Enumerable.Range(1, fillers).Select(i => $"Filler {i}.\n\n"));
            var layout = Lay(Parse($"{filler}![Photo](photo.png \"The photo\")\n\n```mermaid\ngraph\n```\n\nFigure: The rectangle\n", new PdfOptions { CodeBlockRenderers = [Rectangles] }));

            var (imagePage, image) = layout.Pages.Index().SelectMany(p => p.Item.Items.OfType<ImageItem>().Select(i => (p.Index, i))).Single();
            var (captionPage, caption) = Find(layout, "Figure 1:");
            Assert.Equal(imagePage, captionPage);
            Assert.True(caption.Baseline > image.Y + image.Height);
            Assert.Equal(A4.Left + (A4.ContentWidth / 2), caption.X + ((("Figure 1: The photo".Length) * 9 * 0.5) / 2), 6);

            var (shapePage, shape) = layout.Pages.Index().SelectMany(p => p.Item.Items.OfType<ShapeItem>().Select(s => (p.Index, s))).Single();
            var (figurePage, figure) = Find(layout, "Figure 2:");
            Assert.Equal(shapePage, figurePage);
            Assert.True(figure.Baseline > shape.Path.OfType<LineTo>().Max(l => l.Y));
        }
    }

    [Fact]
    public void TableCaptions_GoAbove_WithTheHeader_AndAreNotRepeated()
    {
        var rows = string.Concat(Enumerable.Range(1, 120).Select(i => $"| {i} | row |\n"));
        for (var fillers = 40; fillers < 50; fillers += 2)
        {
            var filler = string.Concat(Enumerable.Range(1, fillers).Select(i => $"Filler {i}.\n\n"));
            var layout = Lay(Parse($"{filler}| Key | Value |\n| --- | --- |\n{rows}\nTable: Many rows\n"));

            var (captionPage, caption) = Find(layout, "Table 1:");
            var (headerPage, header) = Find(layout, "Key");
            Assert.Equal(headerPage, captionPage);
            Assert.True(caption.Baseline < header.Baseline);
            Assert.Equal(headerPage, Find(layout, "1").Page);
            Assert.Single(layout.Pages.SelectMany(p => p.Items.OfType<TextItem>()), t => t.Text == "Table 1:");
            Assert.True(layout.Pages.Count(p => p.Items.OfType<TextItem>().Any(t => t.Text == "Key")) > 1, "the header repeats");
        }
    }

    [Fact]
    public void ImagesInARow_EachHaveTheirCaption_InTheirCell()
    {
        var layout = Lay(Parse("![A](photo.png \"First\") ![B](photo.png \"Second\")\n"));
        var images = layout.Pages[0].Items.OfType<ImageItem>().ToList();

        Assert.Equal(2, images.Count);
        var (_, first) = Find(layout, "Figure 1:");
        var (_, second) = Find(layout, "Figure 2:");
        Assert.True(first.X < images[1].X && second.X > images[0].X + images[0].Width);
        Assert.Equal(first.Baseline, second.Baseline, 6);
    }

    [Fact]
    public void Captions_ComeInTheDocumentLanguage()
    {
        var layout = Lay(Parse("![A](photo.png \"Umsatz\")\n\n| a |\n| - |\n| 1 |\n\nTable: Zahlen\n", texts: GeneratedTexts.German), GeneratedTexts.German);

        Assert.Equal(0, Find(layout, "Abbildung 1:").Page);
        Assert.Equal(0, Find(layout, "Tabelle 1:").Page);
    }

    [Fact]
    public void APdfWithContentsNumbersAndCaptions_HasNumberedBookmarks_AndStaysPdfA()
    {
        var markdown = "---\ntoc: true\nheadingNumbers: true\n---\n# Report\n\n## Summary\n\n![Photo](photo.png \"The photo\")\n\n## Results {.unnumbered}\n\n### Detail\n\n| a | b |\n| - | - |\n| 1 | 2 |\n\nTable: Numbers\n";
        var pdf = Samples.Generate(Samples.Markdown(markdown, Photo));
        Samples.WriteForReview("contents-and-captions.pdf", pdf);
        var raw = Encoding.Latin1.GetString(pdf);

        Assert.Contains("<pdfaid:part>2</pdfaid:part>", raw, StringComparison.Ordinal);
        Assert.Contains("<</F 4 /Subtype /Link ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/Type /Annot", raw, StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        Assert.True(document.TryGetBookmarks(out var bookmarks));
        Assert.Equal(["Report[Contents, 1 Summary, Results[1.1 Detail]]"], bookmarks.Roots.Select(Describe));
        var contents = document.GetPage(2).Text;
        Assert.Contains("1Summary", contents.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("Figure 1:", document.GetPage(3).Text, StringComparison.Ordinal);
        Assert.Contains("Table 1:", document.GetPage(3).Text, StringComparison.Ordinal);

        static string Describe(BookmarkNode node) =>
            node.Children.Count == 0 ? node.Title : $"{node.Title}[{string.Join(", ", node.Children.Select(Describe))}]";
    }

    private static (int Page, TextItem Item) Find(Layout layout, string text) =>
        layout.Pages.Index().SelectMany(p => p.Item.Items.OfType<TextItem>().Where(t => t.Text == text).Select(t => (p.Index, t))).First();

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
