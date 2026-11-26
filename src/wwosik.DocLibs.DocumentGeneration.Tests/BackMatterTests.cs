using System.IO.Compression;
using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using wwosik.DocLibs.DocumentGeneration.Docx;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>
/// Backup slides and appendices (E06-S07): a deck's backup behind the closing slide, out of agenda, numbering, progress,
/// words and talk time, read back by the converter; lettered appendices in PDFs and Word documents; the mistakes, the
/// ignored attribute, and the audience versions.
/// </summary>
public partial class BackMatterTests
{
    private const string Deck = """
        ---
        numberChapters: true
        ---

        # Product review

        ## Agenda {.agenda}

        # Alpha

        ## Alpha slide

        Alpha text.

        # Beta

        ## Beta slide

        Beta text.

        # Thank you {.closing}

        # Backup {.backup}

        ## Spare figures

        Many more words that nobody presents unless a question asks for them in the end.

        # Details

        ## Spare detail

        Detail text.
        """;

    private const string Report = """
        ---
        headingNumbers: true
        toc: true
        ---

        # Annual report

        ## Overview

        Text.

        # Results

        ## Figures

        Text.

        # Glossary {.appendix}

        ## Terms

        Text.

        # Sources

        Text.
        """;

    private static MarkdownDocument Markdown(string markdown) => new(markdown, [], new Dictionary<string, string>());

    private static byte[] Pptx(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(Markdown(markdown), output, options);
        return output.ToArray();
    }

    private static string Converted(byte[] pptx) => new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default).Markdown;

    // The slides' XML in deck order.
    private static List<string> SlideXml(byte[] pptx)
    {
        using var zip = new ZipArchive(new MemoryStream(pptx));
        string Read(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        var targets = Regex.Matches(Read("ppt/_rels/presentation.xml.rels"), "<Relationship [^>]*>")
            .ToDictionary(r => Regex.Match(r.Value, "Id=\"([^\"]+)\"").Groups[1].Value, r => Regex.Match(r.Value, "Target=\"/?(?:ppt/)?([^\"]+)\"").Groups[1].Value);
        return [.. SlideIds().Matches(Read("ppt/presentation.xml")).Select(m => Read("ppt/" + targets[m.Groups[1].Value]))];
    }

    [Fact]
    public void Deck_KeepsTheBackupBehindTheClosingSlide_OutOfTheAgendaAndNumbers_AndReadsItBack()
    {
        var pptx = Pptx(Deck);
        var markdown = Converted(pptx);

        Assert.True(markdown.IndexOf("## Thank you", StringComparison.Ordinal) < markdown.IndexOf("# Backup {.backup}", StringComparison.Ordinal), markdown);
        Assert.Contains("\n# Backup {.backup}\n", markdown, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(markdown, @"\{\.backup\}"));
        Assert.True(markdown.IndexOf("# Backup {.backup}", StringComparison.Ordinal) < markdown.IndexOf("## Spare detail", StringComparison.Ordinal));

        // The agenda lists the main chapters with their numbers, not the backup's.
        var agenda = markdown[markdown.IndexOf("## Agenda", StringComparison.Ordinal)..markdown.IndexOf("## Alpha", StringComparison.Ordinal)];
        Assert.Contains("01", agenda, StringComparison.Ordinal);
        Assert.Contains("02", agenda, StringComparison.Ordinal);
        Assert.DoesNotContain("Backup", agenda, StringComparison.Ordinal);
        Assert.DoesNotContain("Details", agenda, StringComparison.Ordinal);
        Assert.DoesNotContain("03", markdown, StringComparison.Ordinal);

        // The first backup slide is named; every backup slide's footer says so, and no other slide's does.
        var slides = SlideXml(pptx);
        var first = slides.FindIndex(s => s.Contains("<p:cSld name=\"Backup\"", StringComparison.Ordinal));
        Assert.True(first > 0);
        Assert.Single(slides, s => s.Contains("<p:cSld name=\"Backup\"", StringComparison.Ordinal));
        Assert.All(slides[first..], s => Assert.Contains(">Backup</a:t>", s, StringComparison.Ordinal));
        Assert.All(slides[..first], s => Assert.DoesNotContain(">Backup</a:t>", s, StringComparison.Ordinal));
        Assert.Equal(4, slides.Count - first);
    }

    [Fact]
    public void HtmlDeck_MarksTheBackupSlides_ForTheProgress()
    {
        using var html = new MemoryStream();
        HtmlDeck.Write(new MemoryStream(Pptx(Deck)), html);
        var page = System.Text.Encoding.UTF8.GetString(html.ToArray());

        Assert.Equal(4, Regex.Matches(page, "<section [^>]*data-backup").Count);
        Assert.Contains("data-backup", page[page.LastIndexOf("<section", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("progress(current)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_CountsBackupSlidesApart_AndLeavesThemOutOfWordsAndTalkTime()
    {
        var withBackup = PptxGenerator.Check(Markdown(Deck));
        var without = PptxGenerator.Check(Markdown(Deck[..Deck.IndexOf("# Backup", StringComparison.Ordinal)]));

        Assert.Null(withBackup.Error);
        Assert.Equal(4, withBackup.Statistics.SlidesByKind["backup"]);
        Assert.Equal("backup", withBackup.Statistics.SlidesByKind.Keys.Last());
        Assert.Equal(without.Statistics.Words, withBackup.Statistics.Words);
        Assert.Equal(without.Statistics.TalkTime, withBackup.Statistics.TalkTime);
        Assert.Equal(without.Statistics.Pages + 4, withBackup.Statistics.Pages);
    }

    [Theory]
    [InlineData(Audience.Customer, false)]
    [InlineData(Audience.Internal, true)]
    public void InternalBackup_IsHiddenFromTheCustomersVersion_Entirely(Audience audience, bool shown)
    {
        var markdown = Converted(Pptx(Deck.Replace("{.backup}", "{.backup .internal}", StringComparison.Ordinal), new PptxOptions { Audience = audience }));

        Assert.Equal(shown, markdown.Contains("Spare figures", StringComparison.Ordinal));
        Assert.Equal(shown, markdown.Contains("Spare detail", StringComparison.Ordinal));
        Assert.Equal(shown, markdown.Contains("{.backup}", StringComparison.Ordinal));
        Assert.Contains("Beta text", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Backup_StaysInTheCustomersVersion()
    {
        Assert.Contains("Spare detail", Converted(Pptx(Deck, new PptxOptions { Audience = Audience.Customer })), StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_LettersTheAppendices_InTextContentsAndBookmarks()
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(Markdown(Report), output);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.ToArray());
        var text = string.Join("\n", pdf.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));

        // The contents (page 2) and the text both read the appendix heading in full, the sub-heading lettered.
        Assert.Equal(2, Regex.Matches(text, "Appendix A Glossary").Count);
        Assert.Equal(2, Regex.Matches(text, "Appendix B Sources").Count);
        Assert.Equal(2, Regex.Matches(text, @"A\.1 Terms").Count);
        Assert.Equal(2, Regex.Matches(text, @"2\.1 Figures").Count);
        Assert.DoesNotContain("3 Glossary", text, StringComparison.Ordinal);

        Assert.True(pdf.TryGetBookmarks(out var bookmarks));
        var titles = Flatten(bookmarks.Roots).Select(b => b.Title).ToList();
        Assert.Contains("Appendix A Glossary", titles);
        Assert.Contains("A.1 Terms", titles);
        Assert.Contains("Appendix B Sources", titles);
    }

    [Fact]
    public void Pdf_WithoutNumbers_StillHeadsTheAppendices_InTheDocumentLanguage()
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(Markdown(Report.Replace("headingNumbers: true", "lang: de", StringComparison.Ordinal)), output);
        var text = new DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown;

        Assert.Contains("Anhang A Glossary", text, StringComparison.Ordinal);
        Assert.Contains("Anhang B Sources", text, StringComparison.Ordinal);
        Assert.DoesNotContain("A.1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Word_LettersTheAppendices_WithASecondNumbering_InTextAndContents()
    {
        using var output = new MemoryStream();
        DocxGenerator.Generate(Markdown(Report), output);
        using var zip = new ZipArchive(new MemoryStream(output.ToArray()));
        string Read(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        var numbering = Read("word/numbering.xml");
        var document = Read("word/document.xml");
        var appendix = Regex.Match(numbering, "<w:num w:numId=\"(\\d+)\"><w:abstractNumId w:val=\"0\" /><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\" /><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\" /><w:numFmt w:val=\"upperLetter\" />");
        Assert.True(appendix.Success, numbering);
        Assert.Contains("<w:lvlText w:val=\"Appendix %1\" />", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:ind w:left=\"0\" w:firstLine=\"0\" />", numbering, StringComparison.Ordinal);

        // Both appendix headings and the one below them take the second numbering; the chapters before keep the first.
        Assert.Equal(3, Regex.Matches(document, $"<w:numId w:val=\"{appendix.Groups[1].Value}\" />").Count);
        Assert.Contains(">Appendix A Glossary<", document, StringComparison.Ordinal);
        Assert.Contains(">A.1 Terms<", document, StringComparison.Ordinal);
        Assert.Contains(">Appendix B Sources<", document, StringComparison.Ordinal);
        Assert.Contains(">2.1 Figures<", document, StringComparison.Ordinal);
    }

    [Fact]
    public void Word_WithoutNumbers_HeadsTheAppendicesAsText()
    {
        using var output = new MemoryStream();
        DocxGenerator.Generate(Markdown(Report.Replace("headingNumbers: true", "headingNumbers: false", StringComparison.Ordinal)), output);
        var markdown = new DocxConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown;

        Assert.Contains("# Appendix A Glossary", markdown, StringComparison.Ordinal);
        Assert.Contains("# Appendix B Sources", markdown, StringComparison.Ordinal);
        Assert.Contains("## Terms", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EachFormat_IgnoresTheOthersAttribute_WithANote()
    {
        using var output = new MemoryStream();
        var deck = PptxGenerator.Generate(Markdown(Report), output);
        var pdf = PdfGenerator.Generate(Markdown(Deck), new MemoryStream());
        var word = DocxGenerator.Generate(Markdown(Deck), new MemoryStream());

        Assert.Contains(deck.Notes, n => n.Code == NoteCodes.AttributeIgnored && n.Line == 18 && n.Message.Contains("{.appendix}", StringComparison.Ordinal));
        Assert.Contains(pdf.Notes, n => n.Code == NoteCodes.AttributeIgnored && n.Line == 23 && n.Message.Contains("{.backup}", StringComparison.Ordinal));
        Assert.Contains(word.Notes, n => n.Code == NoteCodes.AttributeIgnored && n.Line == 23 && n.Message.Contains("{.backup}", StringComparison.Ordinal));
        Assert.Contains("## Terms", Converted(output.ToArray()), StringComparison.Ordinal);
        Assert.DoesNotContain("Appendix A", Converted(output.ToArray()), StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> Mistakes()
    {
        var data = new TheoryData<string, string, string>();
        var cases = new (string Markdown, string Message)[]
        {
            ("# Deck\n\n# A {.backup}\n\n# B {.backup}\n", "'{.backup}' is on line 5 and already on line 3"),
            ("# Deck\n\n# A {.appendix}\n\n# B {.appendix}\n", "'{.appendix}' is on line 5 and already on line 3"),
            ("# Deck\n\n## A {.backup}\n", "'{.backup}' on line 3 is on a '##' heading"),
            ("# Deck\n\n### A {.appendix}\n", "'{.appendix}' on line 3 is on a '###' heading"),
            ("# Deck\n\n> # A {.backup}\n", "The heading with '{.backup}' on line 3 is inside another block"),
            ("# Deck\n\n::: panel\n# A {.appendix}\n:::\n", "The heading with '{.appendix}' on line 4 is inside another block"),
            ("# Deck {.backup}\n\n## A\n", "'{.backup}' on line 1 is on the cover"),
            ("# Report {.appendix}\n\nText.\n", "'{.appendix}' on line 1 is on the cover"),
            ("# Deck\n\n# Thanks {.closing .backup}\n", "The heading on line 3 has both '{.backup}' and '{.closing}'"),
            ("# Deck\n\n# Backup {.backup}\n\n## A\n\n# Thanks {.closing}\n", "The closing slide on line 7 comes after the backup that starts on line 3"),
        };
        foreach (var format in new[] { "pptx", "pdf", "docx" })
        {
            foreach (var (markdown, message) in cases)
            {
                data.Add(format, markdown, message);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void Mistakes_Throw_WithTheLine(string format, string markdown, string message)
    {
        void Generate()
        {
            var document = Markdown(markdown);
            switch (format)
            {
                case "pptx":
                    PptxGenerator.Generate(document, new MemoryStream());
                    break;
                case "pdf":
                    PdfGenerator.Generate(document, new MemoryStream());
                    break;
                default:
                    DocxGenerator.Generate(document, new MemoryStream());
                    break;
            }
        }

        var error = Assert.Throws<DocumentConversionException>(Generate);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Letters_GoOnPastZ()
    {
        Assert.Equal(["A", "B", "Z", "AA", "AB", "AZ", "BA"], new[] { 1, 2, 26, 27, 28, 52, 53 }.Select(BackMatter.Letter));
    }

    private static IEnumerable<UglyToad.PdfPig.Outline.BookmarkNode> Flatten(IEnumerable<UglyToad.PdfPig.Outline.BookmarkNode> nodes) =>
        nodes.SelectMany(n => Flatten(n.Children).Prepend(n));

    [GeneratedRegex("<p:sldId [^>]*r:id=\"([^\"]+)\"")]
    private static partial Regex SlideIds();
}
