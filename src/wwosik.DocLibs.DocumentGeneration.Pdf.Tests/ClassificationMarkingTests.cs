using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>
/// The classification marking (E03-S04): in the header of every page but the cover, above the cover title, and a draft's
/// diagonal watermark under the content of every page. Layout tests use made-up widths: every character is half as
/// wide as the font size.
/// </summary>
public class ClassificationMarkingTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static Layout Lay(DocumentModel model, string? marking, string? watermark = null, string? title = "Report", ResolvedBrand? brand = null) =>
        new LayoutEngine(
            new FakeMetrics(),
            A4,
            new Design(brand ?? ResolvedBrand.Neutral, ShowTagline: false, null, title) { Marking = marking, MarkingColor = "AA0000", Watermark = watermark })
            .Run(model);

    private static DocumentModel Model(bool cover, int pages = 3)
    {
        var blocks = new List<Block>();
        for (var i = 0; i < pages; i++)
        {
            blocks.Add(new PageBreakBlock());
            blocks.Add(new ParagraphBlock([new TextRun("Page " + i, RunStyle.None)]));
        }

        return new DocumentModel(cover ? new Cover([new TextRun("The title", RunStyle.None)], [], null) : null, blocks);
    }

    private static string Markdown(string frontMatter) =>
        $"---\n{frontMatter}\n---\n# Network expansion\n\nStatus report\n\n## Summary\n\nThe upgrade is on schedule.\n\n## Next\n\nMore to do.\n";

    private static PdfDocument Open(string frontMatter, PdfOptions? options = null) =>
        PdfDocument.Open(Samples.Generate(Samples.Markdown(Markdown(frontMatter)), options));

    private static string Text(Page page) => string.Concat(page.Letters.Select(l => l.Value));

    private static string Horizontal(Page page) => string.Concat(page.Letters.Where(l => l.TextOrientation == TextOrientation.Horizontal).Select(l => l.Value));

    private static string Rotated(Page page) => string.Concat(page.Letters.Where(l => l.TextOrientation != TextOrientation.Horizontal).Select(l => l.Value));

    [Fact]
    public void TheMarking_IsInTheHeaderOfEveryPageButTheCover_LeftOfTheLogo()
    {
        var layout = Lay(Model(cover: true), "Confidential · Draft", brand: ResolvedBrand.Resolve(Samples.Brand));

        Assert.True(layout.Pages[0].IsCover);
        Assert.Equal(4, layout.Pages.Count);
        foreach (var page in layout.Pages.Skip(1))
        {
            var marking = Assert.Single(page.Items.OfType<TextItem>(), t => t.Text == "Confidential · Draft");
            var logo = page.Items.OfType<ImageItem>().Single(i => i.Y == 24);
            Assert.Equal(FontFace.Bold, marking.Face);
            Assert.Equal(8, marking.Size);
            Assert.Equal(38, marking.Baseline);
            Assert.Equal("AA0000", marking.Color);
            Assert.Equal(logo.X - 12, marking.X + ("Confidential · Draft".Length * 4), 6);
        }
    }

    [Fact]
    public void WithoutALogo_TheMarkingEndsAtTheRightMargin()
    {
        var layout = Lay(Model(cover: false), "Internal");

        var marking = Assert.Single(layout.Pages[0].Items.OfType<TextItem>(), t => t.Text == "Internal");
        Assert.Equal(A4.Right, marking.X + ("Internal".Length * 4), 6);
    }

    [Fact]
    public void TheHeaderTitle_IsFittedBesideTheMarking()
    {
        var title = string.Concat(Enumerable.Repeat("Long title ", 20));

        var plain = Lay(Model(cover: false, pages: 1), null, title: title).Pages[0].Items.OfType<TextItem>().Single(t => t.Baseline == 38);
        var marked = Lay(Model(cover: false, pages: 1), "Confidential", title: title).Pages[0].Items.OfType<TextItem>().Where(t => t.Baseline == 38).ToList();

        var fitted = marked.Single(t => t.Text != "Confidential");
        var marking = marked.Single(t => t.Text == "Confidential");
        Assert.EndsWith("…", fitted.Text, StringComparison.Ordinal);
        Assert.True(fitted.Text.Length < plain.Text.Length);
        Assert.True(fitted.X + (fitted.Text.Length * 4) <= marking.X - 12 + 1e-6, "the title ends before the marking");
    }

    [Fact]
    public void OnTheCover_TheMarkingIsAboveTheTitle_AtItsLeftMargin()
    {
        var layout = Lay(Model(cover: true), "Confidential");

        var cover = layout.Pages[0].Items.OfType<TextItem>().ToList();
        var marking = Assert.Single(cover, t => t.Text == "Confidential");
        var title = cover.First(t => t.Size == 30);
        Assert.Equal(title.X, marking.X);
        Assert.Equal(10, marking.Size);
        Assert.Equal(FontFace.Bold, marking.Face);
        Assert.True(marking.Baseline < title.Baseline - 30, "above the title's first line");
        Assert.Equal(A4.Height * 0.28 - 4, marking.Baseline, 6);
    }

    [Fact]
    public void TheWatermark_IsTheFirstItemOfEveryPage_Diagonal_AndCentred()
    {
        var layout = Lay(Model(cover: true), "Draft", "DRAFT");

        Assert.All(layout.Pages, page =>
        {
            var watermark = Assert.IsType<TextItem>(page.Items[0]);
            Assert.Equal("DRAFT", watermark.Text);
            Assert.Equal(45, watermark.Angle);
            Assert.Equal(FontFace.Regular, watermark.Face);
            Assert.Single(page.Items.OfType<TextItem>(), t => t.Angle != 0);
        });

        var item = (TextItem)layout.Pages[1].Items[0];
        var length = "DRAFT".Length * item.Size * 0.5;
        var diagonal = Math.Sqrt((A4.Width * A4.Width) + (A4.Height * A4.Height));
        Assert.Equal(0.7 * diagonal, length, 6);

        // The middle of the baseline, raised by half the cap height, is the centre of the page.
        var c = Math.Cos(Math.PI / 4);
        var middleX = item.X + (length / 2 * c) - (item.Size * 0.35 * c);
        var middleY = item.Baseline - (length / 2 * c) - (item.Size * 0.35 * c);
        Assert.Equal(A4.Width / 2, middleX, 6);
        Assert.Equal(A4.Height / 2, middleY, 6);
    }

    [Fact]
    public void WithoutAMarking_NothingIsAdded()
    {
        var plain = Lay(Model(cover: true), null);

        Assert.All(plain.Pages, p => Assert.DoesNotContain(p.Items.OfType<TextItem>(), t => t.Angle != 0 || t.Face == FontFace.Bold && t.Size == 8));
    }

    [Fact]
    public void AConfidentialDraft_ShowsItsMarkingAndWatermark_AsExtractableText()
    {
        using var document = Open("classification: confidential\ndraft: true");

        var pages = document.GetPages().ToList();
        Assert.True(pages.Count >= 2);
        Assert.Contains("Confidential · Draft", Horizontal(pages[0]), StringComparison.Ordinal);
        Assert.All(pages.Skip(1), p => Assert.Contains("Confidential · Draft", Horizontal(p), StringComparison.Ordinal));
        Assert.All(pages, p => Assert.Equal("DRAFT", Rotated(p)));

        // The rotated letters run up at 45°, from the lower left towards the upper right.
        var letters = pages[1].Letters.Where(l => l.TextOrientation != TextOrientation.Horizontal).ToList();
        Assert.Equal(45, letters[0].BoundingBox.Rotation, 0);
        Assert.True(letters[^1].StartBaseLine.X > letters[0].StartBaseLine.X && letters[^1].StartBaseLine.Y > letters[0].StartBaseLine.Y);
        // The middle of the baseline lies right of and below the centre of the page by half the cap height at 45°.
        var middle = ((letters[0].StartBaseLine.X + letters[^1].EndBaseLine.X) / 2, (letters[0].StartBaseLine.Y + letters[^1].EndBaseLine.Y) / 2);
        Assert.InRange(middle.Item1, A4.Width / 2, (A4.Width / 2) + 80);
        Assert.InRange(middle.Item2, (A4.Height / 2) - 80, A4.Height / 2);
    }

    [Fact]
    public void TheDraftPdf_StaysPdfA2b_WithEveryFontEmbedded()
    {
        var pdf = Samples.Generate(Samples.Markdown(Markdown("classification: confidential\ndraft: true")));
        var raw = Encoding.Latin1.GetString(pdf);

        Assert.Contains("<pdfaid:part>2</pdfaid:part>", raw, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/SMask", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/CA ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/ca ", raw, StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        Assert.All(document.GetPages().SelectMany(p => p.Letters), l => Assert.Contains("Liberation", l.FontName, StringComparison.Ordinal));
        Assert.DoesNotContain("/Helvetica", raw, StringComparison.Ordinal);
        Samples.WriteForReview("classification-confidential-draft.pdf", pdf);
        Samples.WriteForReview("classification-internal-sample.pdf", Samples.Generate(Samples.Markdown(Markdown("classification: internal\nlang: de")), new PdfOptions { Template = Samples.BuiltIn("sample") }));
    }

    [Fact]
    public void TheWatermark_IsLeftOutOfTheConvertedMarkdown()
    {
        var markdown = Samples.RoundTrip(Samples.Generate(Samples.Markdown(Markdown("draft: true"))));

        Assert.DoesNotContain("DRAFT", markdown, StringComparison.Ordinal);
        Assert.Contains("The upgrade is on schedule.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void German_GivesVertraulichInternAndEntwurf()
    {
        using (var confidential = Open("classification: confidential\ndraft: true\nlang: de"))
        {
            var pages = confidential.GetPages().ToList();
            Assert.Contains("Vertraulich · Entwurf", Horizontal(pages[1]), StringComparison.Ordinal);
            Assert.Equal("ENTWURF", Rotated(pages[1]));
        }

        using var internalDocument = Open("classification: internal\nlang: de");
        Assert.Contains("Intern", Horizontal(internalDocument.GetPage(2)), StringComparison.Ordinal);
    }

    [Fact]
    public void HostTexts_ReplaceTheLevelsNames()
    {
        var options = new PdfOptions
        {
            Language = "en-GB",
            Texts = new Dictionary<string, GeneratedTexts> { ["en"] = GeneratedTexts.English with { Confidential = "Strictly confidential", Draft = "Preliminary" } },
        };

        using var document = Open("classification: confidential\ndraft: true", options);

        Assert.Contains("Strictly confidential · Preliminary", Horizontal(document.GetPage(2)), StringComparison.Ordinal);
        Assert.Equal("PRELIMINARY", Rotated(document.GetPage(2)));
    }

    [Fact]
    public void TheHostsMarking_AppliesWithoutFrontMatter_AndFrontMatterCannotLowerIt()
    {
        var options = new PdfOptions { Classification = Classification.Confidential, Draft = true };
        using var output = new MemoryStream();

        var result = PdfGenerator.Generate(Samples.Markdown(Markdown("classification: internal\ndraft: false")), output, options);

        using var document = PdfDocument.Open(output.ToArray());
        Assert.Contains("Confidential · Draft", Horizontal(document.GetPage(2)), StringComparison.Ordinal);
        var note = Assert.Single(result.Notes, n => n.Code == NoteCodes.ClassificationKept);
        Assert.Equal(2, note.Line);
    }

    [Fact]
    public void FrontMatter_RaisesTheHostsClassification()
    {
        using var document = Open("classification: confidential", new PdfOptions { Classification = Classification.Internal });

        Assert.Contains("Confidential", Horizontal(document.GetPage(2)), StringComparison.Ordinal);
        Assert.DoesNotContain("Internal", Text(document.GetPage(2)), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownClassification_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(Markdown("classification: secret"))));

        Assert.Contains("public", error.Message, StringComparison.Ordinal);
        Assert.Contains("confidential", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APublicDocument_IsTheSameAsOneWithoutTheKeys()
    {
        static string Pages(byte[] pdf)
        {
            using var document = PdfDocument.Open(pdf);
            return string.Join("\n", document.GetPages().Select(p => string.Concat(p.Letters.Select(l => $"{l.Value}{l.StartBaseLine.X:0.##},{l.StartBaseLine.Y:0.##}"))));
        }

        Assert.Equal(
            Pages(Samples.Generate(Samples.Markdown(Markdown("title: Same")))),
            Pages(Samples.Generate(Samples.Markdown(Markdown("title: Same\nclassification: public\ndraft: false")))));
    }

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
