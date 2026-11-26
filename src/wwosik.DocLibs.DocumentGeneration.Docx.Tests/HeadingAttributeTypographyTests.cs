using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>
/// E06-S05: heading attributes are taken off before typography, so a Word document has the same headings, page breaks
/// and numbering with and without <c>typography: true</c>, apart from the typeset text.
/// </summary>
public class HeadingAttributeTypographyTests
{
    private const string Markdown = """
        # "Report" -- 2027 {layout="Title Slide"}

        A subtitle.

        # One "part" {transition="fade"}

        ## Status {kicker="Plan -- 2027..."}

        Text.

        ## Dark {.dark kicker="A"}

        Text.

        ## Plain {.unnumbered .newpage}

        Text.

        ### Detail {.unnumbered .newpage}

        More.

        # Two
        """;

    [Theory]
    [InlineData("en", "“Report” – 2027", "One “part”")]
    [InlineData("de", "„Report“ – 2027", "One „part“")]
    public void QuotedAttributes_AreTakenOff_WithTypographyOn_AndTheDocumentStaysTheSame(string language, string cover, string chapter)
    {
        var markdown = $"---\nlang: {language}\ntoc: true\nheadingNumbers: true\n---\n\n" + Markdown;
        using var off = Samples.Open(Samples.Generate(Samples.Markdown(markdown)));
        using var on = Samples.Open(Samples.Generate(Samples.Markdown(markdown), new DocxOptions { Typography = true }));

        Assert.Equal(Structure(off), Structure(on));
        var body = on.MainDocumentPart!.Document!.Body!;
        var texts = body.Descendants<Paragraph>().Select(p => p.InnerText).ToList();
        Assert.Contains(cover, texts);
        Assert.Equal([chapter, "Status", "Dark", "Plain", "Detail", "Two"], Headings(on).Select(h => h.Text));
        Assert.DoesNotContain(texts, t => t.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void ImageAttributes_BuildMarkersAndContainerArguments_StillWork_WithTypographyOn()
    {
        const string Markdown = """
            ## Photos

            {.build}

            ![One](photo.png){scale=0.5 .round}

            ::: columns 1:2
            The "text" column.

            ---

            Right.
            :::
            """;
        MarkdownDocument Document() => Samples.Markdown(Markdown, Samples.Image("photo.png"));
        using var off = Samples.Open(Samples.Generate(Document()));
        using var on = Samples.Open(Samples.Generate(Document(), new DocxOptions { Typography = true }));

        Assert.Equal(Structure(off), Structure(on));
        Assert.Equal(
            off.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Select(e => $"{e.Cx} {e.Cy}"),
            on.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Select(e => $"{e.Cx} {e.Cy}"));
        var texts = on.MainDocumentPart.Document.Body.Descendants<Paragraph>().Select(p => p.InnerText).ToList();
        var offTexts = off.MainDocumentPart.Document.Body.Descendants<Paragraph>().Select(p => p.InnerText).ToList();
        Assert.Contains("The “text” column.", texts);
        Assert.Equal(offTexts.Select(t => t.Replace("\"text\"", "“text”", StringComparison.Ordinal)), texts);
        Assert.DoesNotContain(texts, t => t.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void ImageAttributes_AreTakenOff_NotPrinted_ScaleApplies_AndTheOthersApply()
    {
        static (WordprocessingDocument Package, GenerationResult Result) Generate(string markdown)
        {
            var (docx, result) = Samples.GenerateWithResult(Samples.Markdown(markdown, Samples.Image("photo.png")));
            return (Samples.Open(docx), result);
        }

        static List<string> Texts(WordprocessingDocument package) =>
            [.. package.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => p.InnerText).Where(t => t.Length > 0)];

        static long Width(WordprocessingDocument package) =>
            package.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().Single().Cx!.Value;

        var (plain, _) = Generate("![One](photo.png)\n");
        var (scaled, _) = Generate("![One](photo.png){scale=0.5}\n");
        var (round, result) = Generate("Before ![One](photo.png){.round .grey} after.\n");
        using (plain)
        using (scaled)
        using (round)
        {
            Assert.Empty(Texts(scaled));
            Assert.InRange(Width(scaled), (Width(plain) / 2) - 1, (Width(plain) / 2) + 1);
            Assert.Equal(["Before", "after."], Texts(round).Select(t => t.Trim()));
            Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.AttributeIgnored);
        }
    }

    // The headings: their style, whether they are numbered and their text.
    private static List<(string Style, bool Numbered, string Text)> Headings(WordprocessingDocument package) => [.. package.MainDocumentPart!.Document!.Body!
        .Descendants<Paragraph>()
        .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value?.StartsWith("Heading", StringComparison.Ordinal) == true)
        .Select(p => (p.ParagraphProperties!.ParagraphStyleId!.Val!.Value!, p.ParagraphProperties.NumberingProperties is not null, p.InnerText))];

    // Per paragraph of the body: its style, page break, numbering and whether it holds a drawing; then the sections.
    private static List<string> Structure(WordprocessingDocument package)
    {
        var body = package.MainDocumentPart!.Document!.Body!;
        return [.. body.Descendants<Paragraph>().Select(p =>
                $"{p.ParagraphProperties?.ParagraphStyleId?.Val?.Value} {p.ParagraphProperties?.PageBreakBefore is not null} "
                + $"{p.ParagraphProperties?.NumberingProperties?.OuterXml} {p.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page)} {p.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().Count()}"),
            .. body.Descendants<SectionProperties>().Select(s => s.OuterXml)];
    }
}
