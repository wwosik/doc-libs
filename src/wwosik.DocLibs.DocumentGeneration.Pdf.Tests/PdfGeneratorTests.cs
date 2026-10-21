using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

public class PdfGeneratorTests
{
    private static readonly PdfOptions WithDiagrams = new()
    {
        RenderCodeBlock = (language, code) => language == "mermaid" ? Samples.Image("photo.png") : null,
    };

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Report_RoundTripsThroughThePdfConverter(string template)
    {
        var pdf = Samples.Generate(Samples.Document("report.md"), WithDiagrams with { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"report-{template}.pdf", pdf);

        Samples.AssertMatchesExpected($"report-{template}.roundtrip", Samples.RoundTrip(pdf));
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void LongDocument_RoundTripsThroughThePdfConverter(string template)
    {
        var pdf = Samples.Generate(Samples.Document("long.md"), new PdfOptions { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"long-{template}.pdf", pdf);

        Samples.AssertMatchesExpected($"long-{template}.roundtrip", Samples.RoundTrip(pdf));
    }

    [Fact]
    public void Output_IsPdfA2b_WithEmbeddedFonts()
    {
        var pdf = Samples.Generate(Samples.Document("report.md"), WithDiagrams);
        var raw = System.Text.Encoding.Latin1.GetString(pdf);

        Assert.Contains("<pdfaid:part>2</pdfaid:part>", raw, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", raw, StringComparison.Ordinal);
        Assert.Contains("/OutputIntents", raw, StringComparison.Ordinal);
        Assert.Contains("<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Quarterly review</rdf:li></rdf:Alt></dc:title>", raw, StringComparison.Ordinal);
        Assert.Contains("<dc:creator><rdf:Seq><rdf:li>DocLibs</rdf:li></rdf:Seq></dc:creator>", raw, StringComparison.Ordinal);
        Assert.Contains("<</F 4 /Subtype /Link ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/Type /Annot", raw, StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        var fonts = document.GetPages().SelectMany(p => p.Letters).Select(l => l.FontName).Distinct().ToList();
        Assert.All(fonts, f => Assert.Contains("Liberation", f, StringComparison.Ordinal));
        Assert.Contains("/FontFile2", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/Helvetica", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentInfo_ComesFromFrontMatter_Metadata_OrTheCover()
    {
        using (var fromCover = PdfDocument.Open(Samples.Generate(Samples.Document("report.md"))))
        {
            Assert.Equal("Quarterly review", fromCover.Information.Title);
            Assert.Equal("DocLibs", fromCover.Information.Author);
        }

        var document = Samples.Document("report.md");
        using var fromFrontMatter = PdfDocument.Open(Samples.Generate(document with { Markdown = "---\ntitle: Override\nauthor: Team VT\n---\n" + document.Markdown }));
        Assert.Equal("Override", fromFrontMatter.Information.Title);
        Assert.Equal("Team VT", fromFrontMatter.Information.Author);
    }

    [Fact]
    public void LongNonAsciiTitleAndAuthor_StillFitTheMetadata()
    {
        var title = string.Concat(Enumerable.Repeat("Größe & Übermaß <Zażółć> ", 20)).Trim();
        var markdown = $"---\ntitle: \"{title}\"\nauthor: Ελληνικά\n---\n## A\n";

        var pdf = Samples.Generate(Samples.Markdown(markdown));

        using var document = PdfDocument.Open(pdf);
        Assert.Equal(title, document.Information.Title);
        Assert.Contains("<dc:creator><rdf:Seq><rdf:li>Ελληνικά</rdf:li></rdf:Seq></dc:creator>", System.Text.Encoding.UTF8.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Bookmarks_FollowTheHeadings()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Document("report.md")));

        Assert.True(document.TryGetBookmarks(out var bookmarks));
        var roots = bookmarks.Roots.Select(Describe).ToList();
        Assert.Equal(
            [
                "Quarterly review[Summary[A sub-heading], Results[Fourth level[Fifth level[Sixth level]]]]",
                "Outlook",
            ],
            roots);
    }

    [Fact]
    public void Links_GoToTheWebAndToHeadings()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Document("report.md")));

        var links = document.GetPage(2).GetHyperlinks();
        Assert.Contains(links, l => l.Uri == "https://example.com" && l.Text.Contains("link", StringComparison.Ordinal));
        var raw = System.Text.Encoding.Latin1.GetString(Samples.Generate(Samples.Document("report.md")));
        Assert.Contains("/Dest", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Cover_IsPageOne_AndEveryLaterTopHeadingStartsAPage()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Document("report.md"), WithDiagrams));

        Assert.Contains("Quarterly review", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.StartsWith("Outlook", Text(document.GetPage(document.NumberOfPages)), StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_ShowsDateAndPageNumbers_ButNotOnTheCover()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Document("report.md")));
        var count = document.NumberOfPages;

        Assert.DoesNotContain($"1 / {count}", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("09.03.2026", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains($"2 / {count}", document.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("---\nshowDate: false\n---\n")]
    [InlineData("---\ntemplate: neutral\nshowDate: false\n---\n")]
    public void ShowDateFalse_LeavesTheDateOut(string frontMatter)
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown(frontMatter + "# T\n\n## A\n\nText\n")));

        Assert.DoesNotContain("09.03.2026", document.GetPage(2).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatter_SetsPageSizeAndOrientation()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\npageSize: Letter\norientation: landscape\n---\n## A\n")));

        var page = document.GetPage(1);
        Assert.Equal(792, page.Width, 1);
        Assert.Equal(612, page.Height, 1);
    }

    [Fact]
    public void FrontMatter_TemplateNeutral_HasNoImages()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown("---\ntemplate: neutral\n---\n# T\n\n## A\n\nText\n")));

        Assert.Empty(document.GetPages().SelectMany(p => p.GetImages()));
    }

    [Fact]
    public void Characters_BeyondWinAnsi_SurviveTheRoundTrip()
    {
        const string text = "Zażółć gęślą jaźń. Příliš žluťoučký kůň. Größe 12 €. Ελληνικά, русский → ←";

        var back = Samples.RoundTrip(Samples.Generate(Samples.Markdown("## A\n\n" + text + "\n")));

        Assert.Contains(text, back, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreThan255DistinctCharacters_Work()
    {
        var characters = Enumerable.Range(0x21, 0x7E - 0x20).Concat(Enumerable.Range(0xA1, 0x17F - 0xA0)).Concat(Enumerable.Range(0x391, 0x3A9 - 0x391))
            .Where(c => c is not (0x3A2 or '#' or '*' or '_' or '`' or '\\' or '[' or ']' or '<' or '>' or '|' or '~' or '!' or '&'))
            .Select(c => (char)c)
            .ToArray();
        Assert.True(characters.Length > 300);
        var text = string.Join(' ', characters.Chunk(40).Select(c => new string(c)));

        var pdf = Samples.Generate(Samples.Markdown("## A\n\n" + text + "\n"));

        using var document = PdfDocument.Open(pdf);
        var letters = string.Concat(document.GetPage(1).Letters.Select(l => l.Value));
        Assert.All(characters, c => Assert.Contains(c, letters));
    }

    [Fact]
    public void MissingGlyph_ThrowsNamingTheCharacter()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## Done ✓\n")));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains("'✓' (U+2713)", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingGlyph_OutsideTheBmp_IsNamedAsOneCodePoint()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n\nSmile 😀\n")));

        Assert.Contains("'😀' (U+1F600) is not in the font (Regular)", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceUnsupported_ReplacesInBodyTablesHeadingsAndCode()
    {
        var calls = new List<string>();
        var options = new PdfOptions { ReplaceUnsupported = c => { calls.Add(c); return c == "‖" ? "|" : "[x]"; } };
        const string markdown = "# Done ✓\n\n## Check ✓\n\nBody ✓ and ‖, 漢字.\n\n| Head ✗ |\n|---|\n| cell ✓ |\n\n```\ncode ✓ ‖\n```\n";

        var pdf = Samples.Generate(Samples.Markdown(markdown), options);
        Samples.WriteForReview("replace-unsupported.pdf", pdf);

        using var document = PdfDocument.Open(pdf);
        var text = string.Concat(document.GetPages().Select(p => p.Text));
        Assert.Contains("Done [x]", text, StringComparison.Ordinal);
        Assert.Contains("Check [x]", text, StringComparison.Ordinal);
        Assert.Contains("Body [x] and ‖, [x][x].", text, StringComparison.Ordinal); // Sans has ‖
        Assert.Contains("Head [x]", text, StringComparison.Ordinal);
        Assert.Contains("cell [x]", text, StringComparison.Ordinal);
        Assert.Contains("code [x] |", text, StringComparison.Ordinal); // Mono does not
        Assert.Equal(["✓", "✓", "✓", "漢", "字", "✗", "✓", "✓", "‖", "✓"], calls); // the last is the header title
        Assert.Equal("Done [x]", document.Information.Title);
        Assert.True(document.TryGetBookmarks(out var bookmarks));
        Assert.Equal(["Done [x][Check [x]]"], bookmarks.Roots.Select(Describe));
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", System.Text.Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("❤️")]
    [InlineData("👍🏽")]
    [InlineData("👨‍👩‍👧")]
    public void ReplaceUnsupported_GetsAWholeEmojiInOneCall(string emoji)
    {
        var calls = new List<string>();

        var text = Samples.RoundTrip(Samples.Generate(
            Samples.Markdown($"## A\n\nok {emoji} ok\n"),
            new PdfOptions { ReplaceUnsupported = c => { calls.Add(c); return "?"; } }));

        Assert.Equal([emoji], calls);
        Assert.Contains("ok ? ok", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceUnsupported_KeepsTheAnchorsLinksGoTo()
    {
        var pdf = Samples.Generate(
            Samples.Markdown("## Done ✓\n\nText\n\n## Next\n\n[back](#done-)\n"),
            new PdfOptions { ReplaceUnsupported = _ => "[x]" });

        // A link to an unknown anchor gets no annotation at all.
        using var document = PdfDocument.Open(pdf);
        Assert.Single(document.GetPage(1).GetAnnotations(), a => a.Type == UglyToad.PdfPig.Annotations.AnnotationType.Link);
    }

    [Fact]
    public void ReplaceUnsupported_ThatTheFontLacksToo_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown("## A\n\nDone ✓\n"),
            new PdfOptions { ReplaceUnsupported = _ => "✗" }));

        Assert.Contains("The replacement '✗' for '✓' has the character '✗' (U+2717)", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCodeBlock_ReplacesTheBlock_AndNullKeepsTheCode()
    {
        var calls = new List<string>();
        var keep = new PdfOptions { RenderCodeBlock = (language, code) => { calls.Add(language); return null; } };
        var neutral = WithDiagrams with { Template = new NeutralPdfTemplateOptions() };

        var kept = Samples.RoundTrip(Samples.Generate(Samples.Markdown("## A\n\n```mermaid\ngraph TD;\n```\n"), keep));
        using var replaced = PdfDocument.Open(Samples.Generate(Samples.Markdown("## A\n\n```mermaid\ngraph TD;\n```\n"), neutral));

        Assert.Equal(["mermaid"], calls);
        Assert.Contains("graph TD;", kept, StringComparison.Ordinal);
        Assert.Single(replaced.GetPage(1).GetImages());
        Assert.DoesNotContain("graph", replaced.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingImage_ThrowsNamingThePath()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n\n![x](images/missing.png)\n")));

        Assert.Contains("images/missing.png", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedImage_Throws()
    {
        var gif = new DocumentResource("a.gif", "image/gif", "GIF89a\x01\x00\x01\x00\x00\x00\x00"u8.ToArray());

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("## A\n\n![x](a.gif)\n", gif)));

        Assert.Contains("not a PNG or JPEG", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatterMistake_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("---\npageSize: A5\n---\n## A\n")));

        Assert.Contains("'pageSize' has the value 'A5'; expected 'A4' or 'A3' or 'Letter'", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyMarkdown_GivesOneEmptyPage()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown(string.Empty)));

        Assert.Equal(1, document.NumberOfPages);
    }

    private static string Describe(BookmarkNode node) =>
        node.Title + (node.Children.Count > 0 ? "[" + string.Join(", ", node.Children.Select(Describe)) + "]" : string.Empty);

    private static string Text(UglyToad.PdfPig.Content.Page page) => string.Concat(page.Letters.Select(l => l.Value)).TrimStart();
}
