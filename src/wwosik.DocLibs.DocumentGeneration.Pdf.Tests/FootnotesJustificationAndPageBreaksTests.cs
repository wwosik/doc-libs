using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>
/// Footnotes at the foot of their page, justified text and page breaks (E03-S03), mostly with made-up widths: every
/// character is half as wide as the font size.
/// </summary>
public class FootnotesJustificationAndPageBreaksTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static readonly TextRun One = Reference(1);

    private static TextRun Reference(int number) => new(number.ToString(System.Globalization.CultureInfo.InvariantCulture), RunStyle.Superscript) { Footnote = number };

    private static Layout Lay(DocumentModel model, bool justify = false, GenerationNotes? notes = null) =>
        new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null) { Justify = justify }, notes: notes).Run(model);

    private static DocumentModel Model(IEnumerable<Block> blocks, params (int Number, string Text)[] notes) =>
        new(null, [.. blocks]) { Footnotes = notes.ToDictionary(n => n.Number, n => new FootnoteText(n.Number, [[Run(n.Text)]], 10 + n.Number)) };

    private static TextRun Run(string text) => new(text, RunStyle.None);

    private static IEnumerable<Block> Filler(int count) => Enumerable.Range(1, count).Select(i => new ParagraphBlock([Run("filler " + i)]));

    private static IEnumerable<(int Page, TextItem Item)> Texts(Layout layout) =>
        layout.Pages.Index().SelectMany(p => p.Item.Items.OfType<TextItem>().Select(t => (p.Index, t)));

    private static (int Page, TextItem Item) Find(Layout layout, string text) => Texts(layout).First(t => t.Item.Text == text);

    // The reference: the raised number in the text, at 10.5 pt × 0.65.
    private static (int Page, TextItem Item) ReferenceOf(Layout layout, int number) =>
        Texts(layout).First(t => t.Item.Text == number.ToString(System.Globalization.CultureInfo.InvariantCulture) && Math.Abs(t.Item.Size - (10.5 * 0.65)) < 1e-6);

    private static DocumentModel Parse(string markdown, PdfOptions? options = null, GenerationNotes? notes = null) =>
        MarkdownParser.Parse(Samples.Markdown(markdown), options ?? new PdfOptions(), notes: notes);

    [Fact]
    public void ANote_IsAtTheFootOfThePageOfItsReference_UnderAShortRule()
    {
        var layout = Lay(Model([new ParagraphBlock([Run("Text"), One, Run(" here.")]), .. Filler(3)], (1, "The note.")));

        var (page, note) = Find(layout, "The note.");
        Assert.Equal(ReferenceOf(layout, 1).Page, page);
        Assert.Equal(8.5, note.Size);
        Assert.True(note.Baseline > Texts(layout).Where(t => t.Item.Text.StartsWith("filler", StringComparison.Ordinal)).Max(t => t.Item.Baseline) + 100, "at the foot of the page");
        Assert.True(note.Baseline < A4.Bottom + 0.01);
        var rule = Assert.Single(layout.Pages[page].Items.OfType<LineItem>());
        Assert.Equal(A4.ContentWidth / 3, rule.X2 - rule.X1, 6);
        Assert.True(rule.Y1 < note.Baseline - 8.5);
        var number = layout.Pages[page].Items.OfType<TextItem>().Single(t => t.Text == "1" && t.Size == 8.5 * 0.65);
        Assert.True(number.X < note.X && number.Baseline < note.Baseline, "the number is raised before the note");
    }

    [Fact]
    public void ALineWithAReference_MovesToTheNextPage_WhenItsNoteDoesNotFit()
    {
        var moved = false;
        for (var fillers = 30; fillers < 60; fillers++)
        {
            var blocks = Filler(fillers).Append(new ParagraphBlock([Run("Text"), One])).Append(new ParagraphBlock([Run("after")])).ToList();
            var layout = Lay(Model(blocks, (1, "The note.")));
            var plain = Lay(Model(blocks));

            var reference = ReferenceOf(layout, 1);
            var (page, note) = Find(layout, "The note.");
            Assert.Equal(reference.Page, page);
            Assert.True(Texts(layout).Where(t => t.Page == page && t.Item.Size == 10.5).All(t => t.Item.Baseline < note.Baseline - 10), $"With {fillers} fillers the text runs into the note.");
            moved |= reference.Page != ReferenceOf(plain, 1).Page;
        }

        Assert.True(moved, "some reference fits only without its note");
    }

    [Fact]
    public void ReferencesInTableRowsHeadingsAndCaptions_PutTheirNotesOnTheirPage()
    {
        var table = new TableBlock(
            [[[Run("Header")]], .. Enumerable.Range(1, 70).Select(i => (IReadOnlyList<IReadOnlyList<TextRun>>)[i == 60 ? [Run("row"), Reference(2)] : [Run("row " + i)]])],
            [CellAlignment.Default],
            HasHeader: true);
        var heading = new HeadingBlock(2, [Run("Heading"), Reference(1)], "heading");
        var layout = Lay(Model([heading, new ParagraphBlock([Run("text")]), table], (1, "Heading note."), (2, "Row note.")));

        Assert.Equal(Find(layout, "Heading").Page, Find(layout, "Heading note.").Page);
        Assert.Equal(Find(layout, "row").Page, Find(layout, "Row note.").Page);
        Assert.NotEqual(Find(layout, "Heading").Page, Find(layout, "row").Page);

        // Bookmarks leave the reference out.
        Assert.Equal("Heading", Assert.Single(layout.Bookmarks).Title);
    }

    [Fact]
    public void ReferencesAndNotes_LinkToEachOther()
    {
        var layout = Lay(Model([.. Filler(20), new ParagraphBlock([Run("Text"), One])], (1, "The note.")));

        var reference = ReferenceOf(layout, 1);
        var links = layout.Pages[reference.Page].Items.OfType<LinkItem>().ToList();
        var forward = links.Single(l => l.Target == "#footnote:1");
        Assert.Equal(reference.Item.X, forward.X, 6);
        var back = links.Single(l => l.Target == "#footnote-reference:1");
        Assert.Equal(reference.Page, layout.Anchors["footnote:1"].Page);
        Assert.Equal(reference.Page, layout.Anchors["footnote-reference:1"].Page);
        Assert.True(layout.Anchors["footnote:1"].Y > layout.Anchors["footnote-reference:1"].Y);
        Assert.True(back.Y > forward.Y);

        // No underline: a reference does not look like a link.
        Assert.DoesNotContain(layout.Pages[reference.Page].Items.OfType<LineItem>(), l => l.Y1 == l.Y2 && l.X1 >= reference.Item.X - 0.01 && l.X1 < reference.Item.X + 5 && l.Width == 0.5);
    }

    [Fact]
    public void ANote_IsPrintedOnce_AtItsFirstReference()
    {
        var layout = Lay(Model([new ParagraphBlock([Run("One"), One]), .. Filler(80), new ParagraphBlock([Run("Again"), One])], (1, "The note.")));

        Assert.Single(Texts(layout), t => t.Item.Text == "The note.");
        Assert.Equal(0, Find(layout, "The note.").Page);
        Assert.NotEqual(0, Find(layout, "Again").Page);
    }

    [Fact]
    public void ALongNote_SplitsAcrossPages_NeverBeforeItsSecondLine_AndIsReported()
    {
        // 50 lines of the note, each one word wide.
        var words = string.Join(" ", Enumerable.Range(1, 50).Select(i => $"w{i:00}" + new string('x', 100)));
        var splits = 0;
        for (var fillers = 0; fillers < 80; fillers += 3)
        {
            var notes = new GenerationNotes();
            var blocks = Filler(fillers).Append(new ParagraphBlock([Run("Text"), One])).Append(new ParagraphBlock([Run("More"), Reference(2)]))
                .Concat(Filler(30)).Append(new ParagraphBlock([Run("Later"), Reference(3)])).ToList();
            var layout = Lay(Model(blocks, (1, words), (2, "Short note."), (3, "Third note.")), notes: notes);

            var lines = Texts(layout).Where(t => t.Item.Text.StartsWith('w') && t.Item.Size == 8.5).ToList();
            Assert.Equal(50, lines.Count);
            var reference = ReferenceOf(layout, 1).Page;
            Assert.True(lines.Count(l => l.Page == reference) >= 2, $"With {fillers} fillers the note splits before its second line.");
            Assert.Equal(lines.OrderBy(l => l.Page).ThenBy(l => l.Item.Baseline).Select(l => l.Item.Text), lines.Select(l => l.Item.Text));
            Assert.Equal(reference, Find(layout, "Short note.").Page);

            // The rest of a split note comes before the notes of the page it continues on.
            var split = lines.Select(l => l.Page).Distinct().Count() > 1;
            var third = Find(layout, "Third note.");
            Assert.All(lines.Where(l => l.Page == third.Page), l => Assert.True(l.Item.Baseline < third.Item.Baseline));
            var reported = notes.Result(layout.Pages.Count).Notes.Where(n => n.Code == NoteCodes.FootnoteSplit).ToList();
            Assert.Equal(split ? 1 : 0, reported.Count);
            Assert.All(reported, n => Assert.Equal(11, n.Line));
            splits += split ? 1 : 0;
            Assert.All(layout.Pages.Index(), p => Assert.True(
                p.Item.Items.OfType<TextItem>().Where(t => t.Size == 10.5).All(t => t.Baseline < A4.Bottom - NotesTop(p.Item)),
                $"With {fillers} fillers text and notes overlap on page {p.Index}."));
        }

        Assert.True(splits > 5, $"{splits} splits");
    }

    // How far above the bottom the notes of a page start: its rule.
    private static double NotesTop(LaidOutPage page) =>
        page.Items.OfType<LineItem>().Where(l => l.Y1 == l.Y2 && l.Width == 0.5).Select(l => A4.Bottom - l.Y1).DefaultIfEmpty(0).Max();

    [Fact]
    public void NotesOfTheCover_AreAtTheFootOfTheCover()
    {
        var model = new DocumentModel(new Cover([Run("Title"), One], [[Run("Subtitle"), Reference(2)]], null), [new ParagraphBlock([Run("Body")])])
        {
            Footnotes = new Dictionary<int, FootnoteText>
            {
                [1] = new(1, [[Run("Title note.")]], 1),
                [2] = new(2, [[Run("Subtitle note.")]], 2),
            },
        };

        var layout = Lay(model);

        Assert.True(layout.Pages[0].IsCover);
        Assert.Equal(0, Find(layout, "Title note.").Page);
        Assert.Equal(0, Find(layout, "Subtitle note.").Page);
    }

    [Fact]
    public void Notes_ComeFromTheMarkdown_NumberedThroughTheDocument_WithListsAndCodeAsText()
    {
        var model = Parse("# Cover\n\n## A\n\nOne[^x] and two[^y].\n\n## B\n\nThree[^x].\n\n[^y]: Second.\n[^x]: First, see[^y].\n\n    - item\n\n[^z]: Never used.\n");

        Assert.Equal([1, 2], model.Footnotes.Keys.Order());
        Assert.Equal(["First, see2."], model.Footnotes[1].Paragraphs.Take(1).Select(p => string.Concat(p.Select(r => r.Text))));
        Assert.All(model.Footnotes.Values.SelectMany(f => f.Paragraphs).SelectMany(p => p), r => Assert.Null(r.Footnote));
        Assert.Equal("• item", string.Concat(model.Footnotes[1].Paragraphs[1].Select(r => r.Text)));
        var runs = model.Blocks.OfType<ParagraphBlock>().First().Runs;
        Assert.Equal([1, 2], runs.Where(r => r.Footnote is not null).Select(r => r.Footnote!.Value));
        Assert.DoesNotContain(model.Blocks, b => b is HeadingBlock h && h.Runs.Any(r => r.Text == "Notes"));

        // Adjacent references stay apart.
        // A reference is a run of its own, never joined with the next.
        var apart = Parse("Text[^a] [^b].\n\n[^a]: A.\n[^b]: B.\n").Blocks.OfType<ParagraphBlock>().Single().Runs;
        Assert.Equal(["Text", "1", " ", "2", "."], apart.Select(r => r.Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Justified_EveryInnerLineEndsAtTheRightMargin_WithoutChangingTheLineBreaks(bool inList)
    {
        var words = string.Join(" ", Enumerable.Range(1, 120).Select(i => new string('a', 3 + (i * 7 % 9))));
        Block block = inList ? new ListBlock(false, 1, [[new ParagraphBlock([Run(words)])]]) : new ParagraphBlock([Run(words)]);
        var heading = new HeadingBlock(2, [Run(words[..300])], null);
        var plain = Lay(Model([heading, block]));
        var justified = Lay(Model([heading, block]), justify: true);

        static List<(int, double)> Baselines(Layout layout) => [.. Texts(layout).Select(t => (t.Page, t.Item.Baseline)).Distinct()];
        Assert.Equal(Baselines(plain), Baselines(justified));

        static List<double> Ends(Layout layout, double size) => [.. Texts(layout)
            .Where(t => t.Item.Size == size && t.Item.Text != "•")
            .GroupBy(t => (t.Page, t.Item.Baseline))
            .Select(g => g.Max(t => t.Item.X + (t.Item.Text.Length * size * 0.5)))];
        var ends = Ends(justified, 10.5);
        Assert.True(ends.Count > 5);
        Assert.All(ends[..^1], end => Assert.Equal(A4.Right, end, 0.01));
        Assert.Equal(Ends(plain, 10.5)[^1], ends[^1], 6); // the last line stays left-aligned
        Assert.True(ends[^1] < A4.Right - 5);

        // Headings are never justified.
        Assert.Equal(Ends(plain, 16), Ends(justified, 16));
    }

    [Fact]
    public void Justified_LeavesLinesBeforeBreaks_SingleWords_AndLooseLinesRagged()
    {
        var width = A4.ContentWidth / 5.25; // the characters a line holds
        var single = new string('b', (int)(width * 0.6));
        var loose = $"{new string('c', (int)(width * 0.3))} {new string('d', (int)(width * 0.3))} {new string('e', (int)(width * 0.9))}";
        var paragraph = new ParagraphBlock([Run("short line"), TextRun.Break, Run(single + " " + new string('f', (int)(width * 0.8))), TextRun.Break, Run(loose)]);

        static List<(int, string, double, double)> Items(Layout layout) => [.. Texts(layout).Select(t => (t.Page, t.Item.Text, t.Item.X, t.Item.Baseline))];

        // A line before a hard break, one word on a line, a line whose one space would grow past 3×, and the last line.
        Assert.Equal(Items(Lay(Model([paragraph]))), Items(Lay(Model([paragraph]), justify: true)));
        Assert.Equal(5, Texts(Lay(Model([paragraph]))).Select(t => t.Item.Baseline).Distinct().Count() - 1); // and the footer
    }

    [Fact]
    public void Justified_LinksAreUnderlinedAcrossTheirGaps()
    {
        var link = new TextRun(string.Join(" ", Enumerable.Range(1, 40).Select(i => "linked" + i)), RunStyle.None, "https://example.com");
        var layout = Lay(Model([new ParagraphBlock([link, Run(" end of it all")])]), justify: true);

        var underlines = layout.Pages[0].Items.OfType<LineItem>().Where(l => l.Width == 0.5).ToList();
        var first = underlines.Where(l => l.Y1 == underlines[0].Y1).OrderBy(l => l.X1).ToList();
        for (var i = 1; i < first.Count; i++)
        {
            Assert.Equal(first[i - 1].X2, first[i].X1, 6);
        }

        Assert.Equal(A4.Right, first[^1].X2, 0.01);
    }

    [Fact]
    public void PageBreaks_StartANewPage_ButNeverAnEmptyOne()
    {
        var layout = Lay(Model([new PageBreakBlock(), new ParagraphBlock([Run("one")]), new PageBreakBlock(), new PageBreakBlock(), new ParagraphBlock([Run("two")]), new PageBreakBlock()]));

        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(0, Find(layout, "one").Page);
        Assert.Equal(1, Find(layout, "two").Page);
    }

    [Fact]
    public void NewPage_OnAnyHeading_StartsAPage_AndTheHeadingBeforeIsNotKeptWithIt()
    {
        var layout = Lay(Model([
            new HeadingBlock(2, [Run("A")], null),
            new ParagraphBlock([Run("text")]),
            new HeadingBlock(3, [Run("B")], null) { NewPage = true },
            new ParagraphBlock([Run("more")]),
            new HeadingBlock(2, [Run("C")], null),
            new HeadingBlock(4, [Run("D")], null) { NewPage = true },
        ]));

        Assert.Equal([0, 0, 1, 1, 1, 2], new[] { "A", "text", "B", "more", "C", "D" }.Select(t => Find(layout, t).Page));
    }

    [Theory]
    [InlineData("## A\n\nText.\n\n::: pagebreak\n:::\n\nAfter.\n")]
    [InlineData("## A\n\nText.\n\n::: pagebreak\nAfter.\n:::\n")]
    [InlineData("## A\n\nText.\n\n::: pagebreak\n\nAfter.\n")]
    [InlineData("## A\n\nText.\n\n### Detail {.newpage}\n\nAfter.\n")]
    public void PageBreaks_FromTheMarkdown_ClosedOrNot(string markdown)
    {
        var model = Parse(markdown);
        var layout = Lay(model);

        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(1, Find(layout, "After.").Page);
        Assert.DoesNotContain(Texts(layout), t => t.Item.Text.Contains('{', StringComparison.Ordinal) || t.Item.Text.Contains(':', StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownClass_OnADeepHeading_StaysText_AndNewPageOnTheCover_Throws()
    {
        var heading = Parse("## A\n\n### Detail {.wide}\n").Blocks.OfType<HeadingBlock>().Last();
        Assert.Equal("Detail {.wide}", string.Concat(heading.Runs.Select(r => r.Text)));
        Assert.False(heading.NewPage);

        var error = Assert.Throws<DocumentConversionException>(() => Parse("# Cover {.newpage}\n\nText.\n"));
        Assert.Contains("{.newpage} cannot go on the cover heading", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APageBreak_EndsTheCoverSubtitle()
    {
        var model = Parse("# Cover\n\nSubtitle.\n\n::: pagebreak\n:::\n\nBody.\n");

        Assert.Single(model.Cover!.Subtitle);
        Assert.IsType<PageBreakBlock>(model.Blocks[0]);
        Assert.Equal(2, Lay(model).Pages.Count);
    }

    [Fact]
    public void ALongNote_InAGeneratedPdf_IsReportedWithItsLine()
    {
        var note = string.Join(" ", Enumerable.Range(1, 900).Select(i => "word" + i));
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown($"## A\n\n{string.Concat(Enumerable.Range(1, 30).Select(i => $"Paragraph {i}.\n\n"))}Text[^1].\n\n[^1]: {note}\n"), output);

        var split = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.FootnoteSplit, split.Code);
        Assert.Equal(65, split.Line);
        using var document = PdfDocument.Open(output.ToArray());
        var pages = document.GetPages().Where(p => p.Text.Contains("word1", StringComparison.Ordinal) || p.Text.Contains("word900", StringComparison.Ordinal)).ToList();
        Assert.True(pages.Count >= 2);
    }

    [Fact]
    public void AJustifiedPdfWithFootnotesAndPageBreaks_StaysPdfA()
    {
        var markdown = "---\njustify: true\n---\n# Report\n\nA subtitle[^c].\n\n## Summary\n\n"
            + string.Concat(Enumerable.Range(1, 12).Select(i => $"Paragraph {i} has enough words to fill more than one line of the page, so that justification shows: the spaces grow until the line ends at the margin[^{i}].\n\n"))
            + "::: pagebreak\n:::\n\n### Detail {.newpage}\n\n| a | b |\n| - | - |\n| cell[^t] | 2 |\n\n"
            + string.Concat(Enumerable.Range(1, 12).Select(i => $"[^{i}]: Note {i}, which is long enough to be justified too when it runs over the width of the page, as notes often do in a report.\n")) + "[^t]: A cell note.\n[^c]: A cover note.\n";
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown(markdown), output, new PdfOptions { TimeProvider = Samples.FixedTime });
        var pdf = output.ToArray();
        Samples.WriteForReview("footnotes-justified.pdf", pdf);
        var raw = Encoding.Latin1.GetString(pdf);

        Assert.False(result.HasNotes, result.ToString());
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", raw, StringComparison.Ordinal);
        Assert.Contains("<</F 4 /Subtype /Link ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/Type /Annot", raw, StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        Assert.Contains("A cover note.", document.GetPage(1).Text, StringComparison.Ordinal);
        var detail = document.GetPages().First(p => p.Text.Contains("Detail", StringComparison.Ordinal));
        Assert.Contains("A cell note.", detail.Text, StringComparison.Ordinal);
        Assert.Contains("Note 12", string.Concat(document.GetPages().Select(p => p.Text)), StringComparison.Ordinal);
        Assert.All(document.GetPages(), p => Assert.True(p.Letters.Count > 0, "no empty page"));
    }

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
