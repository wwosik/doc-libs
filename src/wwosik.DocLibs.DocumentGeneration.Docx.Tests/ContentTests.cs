using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using static wwosik.DocLibs.DocumentGeneration.Docx.Tests.DocxGeneratorTests;
using W14 = DocumentFormat.OpenXml.Office2010.Word;
using WordDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

public class ContentTests
{
    private const string Content = """
        ---
        lang: en
        ---
        # Report

        ## Overview

        Text with **bold**, *italic*, ~~strike~~, `code`, ==mark==, a [site](https://example.com/a) and a [jump](#details).
        A footnote here.[^1] And a line\
        break.

        1. One
        2. Two
           - Nested
             1. Deeper
                - Deepest

        Between the lists.

        3. Restarted list
        4. Four

        - [ ] Open task
        - [x] Done task

        Term
        :   The description.

        > A quote
        >
        > > nested

        ```text
        code	line
        second
        ```

        ---

        **Sales by region**

        | Region | Revenue |
        | :----- | ------: |
        | North | 1,200 |

        Table: Sales in 2026

        ![Bar chart](diagram.png "Revenue by region")

        ![Photo](photo.png "caption")

        ![Plain](photo.png "scale=2")

        Table: misplaced

        ### Details {.unnumbered}

        # Second chapter

        Closing text.

        [^1]: The footnote text with a [link](https://example.com/note).
        """;

    [Fact]
    public void AFullDocument_IsValid_AndWrittenForReview()
    {
        foreach (var template in new[] { "neutral", "sample", "custom" })
        {
            var docx = Generate(Content, new DocxOptions { Template = Samples.Template(template), TableOfContents = true });
            Samples.WriteForReview($"content-{template}.docx", docx);
            Samples.AssertValid(docx);
        }
    }

    [Fact]
    public void Headings_AreShifted_SoTheHighestLevelAfterTheCoverIsHeading1_AndALaterHashStartsAPage()
    {
        using var package = Open(Content);
        var headings = Paragraphs(package).Where(p => Style(p).StartsWith("Heading", StringComparison.Ordinal)).ToList();

        Assert.Equal(
            [("Heading2", "Overview"), ("Heading3", "Details"), ("Heading1", "Second chapter")],
            headings.Select(h => (Style(h), Texts(h))));
        Assert.Null(headings[0].ParagraphProperties!.PageBreakBefore);
        Assert.NotNull(headings[2].ParagraphProperties!.PageBreakBefore);

        using var shifted = Open("# Cover\n\n## A\n\n### B\n");
        Assert.Equal(["Heading1", "Heading2"], Paragraphs(shifted).Select(Style).Where(s => s.StartsWith("Heading", StringComparison.Ordinal)));
    }

    [Fact]
    public void HeadingNumbers_AreOnByDefault_OffWithTheFrontMatter_AndNoneOnUnnumbered()
    {
        using var numbered = Open("# Title\n\n## A\n\n### B\n\n### C {.unnumbered}\n");
        var styles = Styles(numbered);

        Assert.Equal(DocxNumbering.HeadingNumId, styles["Heading1"].StyleParagraphProperties!.NumberingProperties!.NumberingId!.Val!.Value);
        Assert.Equal(1, styles["Heading2"].StyleParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value);
        Assert.Null(styles["Heading4"].StyleParagraphProperties!.NumberingProperties);
        var levels = numbered.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!.Elements<AbstractNum>().First().Elements<Level>().ToList();
        Assert.Equal(["%1", "%1.%2", "%1.%2.%3"], levels.Select(l => l.LevelText!.Val!.Value));
        var c = Paragraphs(numbered).Single(p => Texts(p) == "C");
        Assert.Equal(0, c.ParagraphProperties!.NumberingProperties!.NumberingId!.Val!.Value);

        using var toc = Open("---\ntoc: true\ntocDepth: 3\n---\n# Title\n\n## A\n\n### B\n\n### C {.unnumbered}\n\n### D\n");
        Assert.Equal(["1 A", "1.1 B", "C", "1.2 D"], Paragraphs(toc).Where(p => Style(p) is "TOC1" or "TOC2").Select(Texts));

        using var off = Open("---\nheadingNumbers: false\n---\n# Title\n\n## A\n");
        Assert.Null(Styles(off)["Heading1"].StyleParagraphProperties!.NumberingProperties);
        using var fromOptions = Open("# Title\n\n## A\n", new DocxOptions { HeadingNumbers = false, TableOfContents = true });
        Assert.Null(Styles(fromOptions)["Heading1"].StyleParagraphProperties!.NumberingProperties);
        Assert.Contains(Paragraphs(fromOptions), p => Style(p) == "TOC1" && Texts(p) == "A");
    }

    [Fact]
    public void Headings_HaveBookmarks_WhichLinksAndTheTableOfContentsJumpTo()
    {
        using var package = Open("---\ntoc: true\n---\n# Title\n\n## Costs & risks\n\nSee [costs](#costs--risks) and [nowhere](#missing).\n\n## Costs & risks\n");
        var bookmarks = Body(package).Descendants<BookmarkStart>().Select(b => b.Name!.Value).ToList();

        Assert.Equal(["costs__risks", "costs__risks_1"], bookmarks);
        var tocLinks = Paragraphs(package).Where(p => Style(p) == "TOC1").Select(p => p.GetFirstChild<Hyperlink>()!.Anchor!.Value).ToList();
        Assert.Equal(bookmarks, tocLinks);
        var link = Assert.Single(Paragraphs(package).Single(p => Texts(p).StartsWith("See", StringComparison.Ordinal)).Elements<Hyperlink>());
        Assert.Equal(("costs__risks", "costs"), (link.Anchor!.Value, Texts(link)));
    }

    [Fact]
    public void ExternalLinks_AreHyperlinksWithARelationship_InTheLinkStyle()
    {
        using var package = Open(Content);
        var main = package.MainDocumentPart!;
        var link = Body(package).Descendants<Hyperlink>().First(h => h.Id is not null);

        Assert.Equal("https://example.com/a", main.HyperlinkRelationships.Single(r => r.Id == link.Id!.Value).Uri.ToString());
        Assert.Equal("Hyperlink", link.Descendants<RunStyle>().Single().Val!.Value);
    }

    [Fact]
    public void Runs_CarryBoldItalicStrike_AndCodeInItsCharacterStyle()
    {
        using var package = Open(Content);
        var paragraph = Paragraphs(package).Single(p => Texts(p).StartsWith("Text with", StringComparison.Ordinal));
        Run RunOf(string text) => paragraph.Descendants<Run>().Single(r => Texts(r) == text);

        Assert.NotNull(RunOf("bold").RunProperties!.Bold);
        Assert.NotNull(RunOf("italic").RunProperties!.Italic);
        Assert.NotNull(RunOf("strike").RunProperties!.Strike);
        Assert.Equal("Mark", RunOf("mark").RunProperties!.RunStyle!.Val!.Value);
        Assert.Equal("CodeCharacter", RunOf("code").RunProperties!.RunStyle!.Val!.Value);
        Assert.Single(paragraph.Descendants<Break>());
    }

    [Fact]
    public void OrderedLists_Restart_AndListsNestToThreeLevels()
    {
        var (docx, result) = GenerateWithResult(Content);
        using var package = Samples.Open(docx);
        var numbering = package.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
        (int NumId, int Level) Of(string text)
        {
            var properties = Paragraphs(package).Single(p => Texts(p) == text).ParagraphProperties!.NumberingProperties!;
            return (properties.NumberingId!.Val!.Value, properties.NumberingLevelReference!.Val!.Value);
        }

        int? Start(int numId) => numbering.Elements<NumberingInstance>().Single(n => n.NumberID!.Value == numId)
            .GetFirstChild<LevelOverride>()?.StartOverrideNumberingValue?.Val?.Value;

        Assert.Equal(0, Of("One").Level);
        Assert.Equal(Of("One").NumId, Of("Two").NumId);
        Assert.Equal(1, Of("Nested").Level);
        Assert.Equal(2, Of("Deeper").Level);
        Assert.Equal(2, Of("Deepest").Level);
        Assert.NotEqual(Of("One").NumId, Of("Restarted list").NumId);
        Assert.Equal((1, 3), (Start(Of("One").NumId), Start(Of("Restarted list").NumId)));
        Assert.Equal("ListNumber", Style(Paragraphs(package).Single(p => Texts(p) == "One")));
        Assert.Equal("ListBullet", Style(Paragraphs(package).Single(p => Texts(p) == "Nested")));
        Assert.Equal(NoteCodes.ListTooDeep, Assert.Single(result.Notes, n => n.Code == NoteCodes.ListTooDeep).Code);

        using var two = Open("1. A\n2. B\n\nText\n\n1. C\n");
        var first = Paragraphs(two).Single(p => Texts(p) == "A").ParagraphProperties!.NumberingProperties!.NumberingId!.Val!.Value;
        var second = Paragraphs(two).Single(p => Texts(p) == "C").ParagraphProperties!.NumberingProperties!.NumberingId!.Val!.Value;
        Assert.NotEqual(first, second);
        Assert.Equal(1, two.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!.Elements<NumberingInstance>().Single(n => n.NumberID!.Value == second).GetFirstChild<LevelOverride>()!.StartOverrideNumberingValue!.Val!.Value);
    }

    [Fact]
    public void TaskItems_HaveCheckBoxes_AndDefinitionListsABoldTermAndAnIndentedDescription()
    {
        using var package = Open(Content);
        var open = Paragraphs(package).Single(p => Texts(p).EndsWith("Open task", StringComparison.Ordinal));
        var done = Paragraphs(package).Single(p => Texts(p).EndsWith("Done task", StringComparison.Ordinal));

        Assert.Equal("ListTask", Style(open));
        Assert.Equal(W14.OnOffValues.Zero, open.Descendants<W14.SdtContentCheckBox>().Single().Checked!.Val!.Value);
        Assert.Equal(W14.OnOffValues.One, done.Descendants<W14.SdtContentCheckBox>().Single().Checked!.Val!.Value);
        Assert.StartsWith("☒", Texts(done), StringComparison.Ordinal);
        Assert.Equal("DefinitionTerm", Style(Paragraphs(package).Single(p => Texts(p) == "Term")));
        Assert.Equal("Definition", Style(Paragraphs(package).Single(p => Texts(p) == "The description.")));
        Assert.NotNull(Styles(package)["DefinitionTerm"].StyleRunProperties!.Bold);
    }

    [Fact]
    public void CodeQuotesAndRules_UseTheirStyles()
    {
        using var package = Open(Content);
        var paragraphs = Paragraphs(package);

        Assert.Equal(["code    line", "second"], paragraphs.Where(p => Style(p) == "Code").Select(Texts));
        Assert.Equal(["A quote", "nested"], paragraphs.Where(p => Style(p) == "Quote").Select(Texts));
        Assert.Single(paragraphs, p => Style(p) == "Rule");
        Assert.Equal("Consolas", Styles(package)["Code"].StyleRunProperties!.RunFonts!.Ascii!.Value);
    }

    [Fact]
    public void Tables_SpanTheTextWidth_RepeatTheHeader_AndGetTheirTitleAndCaptionAbove()
    {
        using var package = Open(Content);
        var body = Body(package);
        var table = body.Elements<Table>().Single();
        var before = table.ElementsBefore().OfType<Paragraph>().Reverse().Take(2).Reverse().ToList();

        Assert.Equal([("TableTitle", "Sales by region"), ("Caption", "Table 1: Sales in 2026")], before.Select(p => (Style(p), Texts(p))));
        Assert.NotNull(before[1].ParagraphProperties!.KeepNext);
        Assert.Equal(" SEQ Table \\* ARABIC ", before[1].Descendants<FieldCode>().Single().Text);
        Assert.Equal("DocTable", table.GetFirstChild<TableProperties>()!.TableStyle!.Val!.Value);
        Assert.Equal(("5000", TableWidthUnitValues.Pct), (table.GetFirstChild<TableProperties>()!.TableWidth!.Width!.Value, table.GetFirstChild<TableProperties>()!.TableWidth!.Type!.Value));
        var rows = table.Elements<TableRow>().ToList();
        Assert.NotNull(rows[0].TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.Null(rows[1].TableRowProperties!.GetFirstChild<TableHeader>());
        Assert.Equal(JustificationValues.Right, rows[1].Elements<TableCell>().Last().Descendants<Justification>().Single().Val!.Value);
        var page = Section(package).GetFirstChild<PageSize>()!.Width!.Value - 2 * 1134;
        Assert.Equal((int)page, table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Sum(c => int.Parse(c.Width!.Value!, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.DoesNotContain(Paragraphs(package), p => Texts(p).StartsWith("Table: Sales", StringComparison.Ordinal));
    }

    [Fact]
    public void ColumnWidths_GiveEachColumnItsLongestWord_AndAddUpToTheWidth()
    {
        List<DocxRun> Cell(string text) => [new DocxRun(text, RunFlags.None, null)];
        var rows = new List<IReadOnlyList<IReadOnlyList<DocxRun>>>
        {
            new[] { Cell("Id"), Cell("Description") },
            new[] { Cell("1"), Cell(string.Join(" ", Enumerable.Repeat("words", 80))) },
            new[] { Cell("Incomprehensibilities"), Cell("x") },
        };

        var widths = DocxBody.ColumnWidths(rows, header: true, available: 9000);
        var longest = TextMetrics.LongestWord([("Incomprehensibilities", false)], 950) / DocxPage.EmuPerTwip;

        Assert.Equal(9000, widths.Sum());
        Assert.True(widths[0] >= longest + 160 - 1, $"{widths[0]} < {longest}");
        Assert.True(widths[1] > widths[0]);
    }

    [Fact]
    public void Images_AreFitted_CarryTheirAltText_AndTheirCaptionBelow()
    {
        var (docx, result) = GenerateWithResult(Content);
        using var package = Samples.Open(docx);
        var paragraphs = Paragraphs(package);
        var drawings = Body(package).Descendants<WordDrawing>().ToList();
        var extents = drawings.Select(d => d.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single()).ToList();
        var properties = drawings.Select(d => d.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Single()).ToList();

        Assert.Equal(["Bar chart", "Photo", "Plain"], properties.Select(p => p.Description!.Value));
        Assert.Equal((480 * 9525L, 270 * 9525L), (extents[0].Cx!.Value, extents[0].Cy!.Value));
        Assert.Equal(40 * 9525L, extents[2].Cx!.Value);
        Assert.Equal(properties.Count, properties.Select(p => p.Id!.Value).Distinct().Count());
        var chart = paragraphs.Single(p => p.Descendants<WordDrawing>().Any(d => d == drawings[0]));
        Assert.NotNull(chart.ParagraphProperties!.KeepNext);
        Assert.Equal(JustificationValues.Center, chart.ParagraphProperties!.Justification!.Val!.Value);
        var captions = paragraphs.Where(p => Style(p) == "Caption").Select(Texts).ToList();
        Assert.Equal(["Table 1: Sales in 2026", "Figure 1: Revenue by region", "Figure 2: Photo"], captions);
        Assert.Equal("Caption", Style((Paragraph)chart.NextSibling()!));

        // An image larger than the page is fitted to the text width, never enlarged.
        var wide = new DocumentResource("wide.png", "image/png", Png(4000, 100));
        using var fitted = Samples.Open(Samples.Generate(Samples.Markdown("![Wide](wide.png)\n", wide)));
        var extent = Body(fitted).Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single();
        Assert.InRange(extent.Cx!.Value, ((11906 - 2 * 1134) * 635L) - 1, (11906 - 2 * 1134) * 635L);
        Assert.Equal(NoteCodes.CaptionIgnored, Assert.Single(result.Notes, n => n.Code == NoteCodes.CaptionIgnored).Code);
        Assert.Contains(paragraphs, p => Texts(p) == "Table: misplaced");
    }

    [Fact]
    public void ImageTitles_GiveCaptions_ButScaleAloneGivesNone()
    {
        Assert.Equal("Sales by region", Captions.OfImage("Sales by region scale=0.5", false, "Alt"));
        Assert.Equal("Alt", Captions.OfImage("caption", false, "Alt"));
        Assert.Equal("Alt", Captions.OfImage("scale=0.5 caption", false, "Alt"));
        Assert.Equal("Alt", Captions.OfImage(null, true, "Alt"));
        Assert.Null(Captions.OfImage("scale=2", false, "Alt"));
        Assert.Null(Captions.OfImage(null, false, "Alt"));
        Assert.Equal("A captioned view", Captions.OfImage("A captioned view", false, "Alt"));
    }

    [Fact]
    public void AMissingImage_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("![Missing](missing.png)\n")));

        Assert.Equal(Format.Docx, error.Format);
        Assert.Contains("missing.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Footnotes_AreWordFootnotes_InTheFootnotesPart()
    {
        var (docx, result) = GenerateWithResult("Text.[^a] More.[^b] Missing.[^c]\n\n[^a]: First.\n[^b]: Second.\n");
        using var package = Samples.Open(docx);
        var references = Body(package).Descendants<FootnoteReference>().ToList();
        var footnotes = package.MainDocumentPart!.FootnotesPart!.Footnotes!.Elements<Footnote>().ToList();

        Assert.Equal([1L, 2L], references.Select(r => r.Id!.Value));
        Assert.Equal([-1L, 0L, 1L, 2L], footnotes.Select(f => f.Id!.Value));
        Assert.Equal("FootnoteText", Style(footnotes[2].Elements<Paragraph>().Single()));
        Assert.Equal(" First.", Texts(footnotes[2]));
        Assert.NotNull(footnotes[2].Descendants<FootnoteReferenceMark>().SingleOrDefault());
        Assert.Equal("FootnoteReference", references[0].Parent!.GetFirstChild<RunProperties>()!.RunStyle!.Val!.Value);
        Assert.Equal(NoteCodes.FootnoteMissing, Assert.Single(result.Notes).Code);
        Samples.AssertValid(docx);

        using var full = Open(Content);
        var note = full.MainDocumentPart!.FootnotesPart!.Footnotes!.Elements<Footnote>().Last();
        Assert.Equal("https://example.com/note", full.MainDocumentPart.FootnotesPart.HyperlinkRelationships.Single().Uri.ToString());
        Assert.NotNull(note.Descendants<Hyperlink>().SingleOrDefault());
    }

    [Fact]
    public void TheLanguage_IsInTheDefaults_AndTheCaptionsAreInIt()
    {
        using var package = Open("---\nlang: de\n---\n| A |\n| - |\n| 1 |\n\nTable: Umsatz\n\n![Bild](diagram.png \"Umsatz nach Region\")\n");
        var defaults = package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.DocDefaults!.RunPropertiesDefault!.RunPropertiesBaseStyle!;

        Assert.Equal("de", defaults.GetFirstChild<Languages>()!.Val!.Value);
        Assert.Equal("de", package.MainDocumentPart.DocumentSettingsPart!.Settings!.GetFirstChild<ThemeFontLanguages>()!.Val!.Value);
        Assert.Equal(["Tabelle 1: Umsatz", "Abbildung 1: Umsatz nach Region"], Paragraphs(package).Where(p => Style(p) == "Caption").Select(Texts));

        using var custom = Open("---\nlang: de-CH\n---\nText\n", new DocxOptions { Template = CustomTemplate.Options });
        Assert.Equal("de-CH", custom.MainDocumentPart!.StyleDefinitionsPart!.Styles!.DocDefaults!.RunPropertiesDefault!.RunPropertiesBaseStyle!.GetFirstChild<Languages>()!.Val!.Value);
    }

    [Fact]
    public void ContentRoleStyles_MissingFromACallersTemplate_AreAdded()
    {
        using var buffer = new MemoryStream();
        buffer.Write(CustomTemplate.Content);
        using (var package = WordprocessingDocument.Open(buffer, true))
        {
            var styles = package.MainDocumentPart!.StyleDefinitionsPart!.Styles!;
            foreach (var id in new[] { "ListBullet", "Quote", "DocTable", "Hyperlink", "CodeCharacter" })
            {
                styles.Elements<Style>().Single(s => s.StyleId == id).Remove();
            }

            styles.Save();
        }

        var docx = Samples.Generate(
            Samples.Markdown("- [link](https://example.com) and `code`\n\n> quoted\n\n| A |\n| - |\n| 1 |\n"),
            new DocxOptions { Template = new CustomDocxTemplateOptions(new MemoryStream(buffer.ToArray())) { Styles = CustomTemplate.Styles } });
        using var result = Samples.Open(docx);
        var added = Styles(result);

        Assert.Equal(StyleValues.Paragraph, added["ListBullet"].Type!.Value);
        Assert.Equal("Standard", added["Quote"].BasedOn!.Val!.Value);
        Assert.Equal(StyleValues.Table, added["DocTable"].Type!.Value);
        Assert.Equal(StyleValues.Character, added["Hyperlink"].Type!.Value);
        Assert.Equal("Doc Table", added["DocTable"].StyleName!.Val!.Value);
        Assert.Equal("ListBullet", Style(Paragraphs(result).First()));
        Samples.AssertValid(docx);
    }

    [Fact]
    public void AMappedCharacterStyle_IsFoundAmongTheCharacterStyles()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown("---\nstyles.hyperlink: Link Style\n---\nText\n"),
            new DocxOptions { Template = CustomTemplate.Options }));

        Assert.Contains("Its character styles: ", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Hyperlink'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeckSyntax_TakesItsStyles_InTheLayoutsSample()
    {
        var (docx, _) = GenerateWithResult(File.ReadAllText(Samples.Path("layouts.md")));
        using var package = Samples.Open(docx);
        var paragraphs = Paragraphs(package);

        var note = paragraphs.Single(p => Texts(p).StartsWith("Note To be fair", StringComparison.Ordinal));
        Assert.Equal("Note", Style(note));
        Assert.Equal("Important", Style(paragraphs.Single(p => Texts(p).StartsWith("Important Then a shadow run", StringComparison.Ordinal))));
        Assert.Equal("ContainerHeading", Style(paragraphs.Single(p => Texts(p) == "Configuration is too complicated")));
        Assert.Equal("PanelDark", Style(paragraphs.Single(p => Texts(p) == "Every result has a manifest")));
        Assert.DoesNotContain(paragraphs, p => Texts(p).Contains(":::", StringComparison.Ordinal));
        Assert.Contains(paragraphs, p => Texts(p) == "Rows in the highlight colour need a human.");
        Assert.Equal("LabelSuccess", paragraphs.SelectMany(p => p.Descendants<Run>()).First(r => Texts(r) == "Done").RunProperties!.RunStyle!.Val!.Value);
    }

    [Fact]
    public void ACodeBlockARendererWouldDraw_StaysCode_WithANote()
    {
        var (docx, result) = GenerateWithResult("```dot\ndigraph {}\n```\n", new DocxOptions { CodeBlockRenderers = [new Drawer()] });
        using var package = Samples.Open(docx);

        Assert.Equal(NoteCodes.CodeNotDrawn, Assert.Single(result.Notes).Code);
        Assert.Equal("Code", Style(Paragraphs(package).Single()));
    }

    [Fact]
    public void ATableTitle_ComesBackFromTheConverter_AsText_NotAsAHeading()
    {
        var markdown = Samples.RoundTrip(Generate("**Sales by region**\n\n| Region | Sales |\n| --- | --- |\n| North | 8 |\n"));

        Assert.DoesNotContain("# Sales by region", markdown, StringComparison.Ordinal);
        Assert.Contains("Sales by region", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RawHtml_StaysText_WithANote()
    {
        var (_, result) = GenerateWithResult("Text <b>bold</b>\n\n<div>block</div>\n");

        Assert.All(result.Notes, n => Assert.Equal(NoteCodes.HtmlAsText, n.Code));
        Assert.NotEmpty(result.Notes);
    }

    private static byte[] Generate(string markdown, DocxOptions? options = null) => GenerateWithResult(markdown, options).Docx;

    private static (byte[] Docx, GenerationResult Result) GenerateWithResult(string markdown, DocxOptions? options = null) =>
        Samples.GenerateWithResult(Samples.Markdown(markdown, Samples.Image("diagram.png"), Samples.Image("photo.png")), options);

    private static WordprocessingDocument Open(string markdown, DocxOptions? options = null) => Samples.Open(Generate(markdown, options));

    private static List<Paragraph> Paragraphs(WordprocessingDocument package) => [.. Body(package).Descendants<Paragraph>()];

    private static Dictionary<string, Style> Styles(WordprocessingDocument package) =>
        package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);

    // A PNG header of the given size, enough for ImageInfo; the pixels do not matter to the generator.
    private static byte[] Png(int width, int height)
    {
        var bytes = File.ReadAllBytes(Samples.Path("photo.png"));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private sealed class Drawer : ICodeBlockRenderer
    {
        public CodeBlockGraphic? Render(CodeBlockRequest request) =>
            request.Language == "dot" ? new DrawingGraphic(new Drawing(10, 10, [])) : null;
    }
}
