using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>
/// E06-S05: heading attributes are taken off before typography, so a PDF has the same headings, pages and bookmarks
/// with and without <c>typography: true</c>, apart from the typeset text.
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

        ## Agenda {.agenda}

        # Two
        """;

    [Theory]
    [InlineData("en", "“Report” – 2027", "One “part”")]
    [InlineData("de", "„Report“ – 2027", "One „part“")]
    public void QuotedAttributes_AreTakenOff_WithTypographyOn_AndThePagesStayTheSame(string language, string cover, string chapter)
    {
        var markdown = $"---\nlang: {language}\ntoc: true\nheadingNumbers: true\n---\n\n" + Markdown;
        var off = Samples.Generate(Samples.Markdown(markdown));
        var on = Samples.Generate(Samples.Markdown(markdown), new PdfOptions { Typography = true });

        using var offDocument = PdfDocument.Open(off);
        using var onDocument = PdfDocument.Open(on);
        Assert.Equal(offDocument.NumberOfPages, onDocument.NumberOfPages);
        Assert.Equal(Bookmarks(offDocument).Count, Bookmarks(onDocument).Count);
        Assert.Equal(["Status", "Dark", "Plain", "Detail", "Agenda", "Two"], Bookmarks(onDocument).Where(b => !b.Contains('“') && !b.Contains('„') && b is not ("Contents" or "Inhalt")).Select(Plain));
        Assert.Contains(chapter, Bookmarks(onDocument).Select(Plain));

        var text = Text(onDocument);
        Assert.Contains(cover, text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        Assert.DoesNotContain("kicker", text, StringComparison.Ordinal);
        Assert.Equal(Text(offDocument).Split('\n').Length, text.Split('\n').Length);
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
        using var off = PdfDocument.Open(Samples.Generate(Document()));
        using var on = PdfDocument.Open(Samples.Generate(Document(), new PdfOptions { Typography = true }));

        Assert.Equal(Images(off), Images(on));
        Assert.Equal(Positions(off, "Right."), Positions(on, "Right."));
        Assert.Contains("The “text” column.", Text(on), StringComparison.Ordinal);
        Assert.DoesNotContain("{", Text(on), StringComparison.Ordinal);
    }

    // Headings in the PDF outline, numbers taken off.
    private static List<string> Bookmarks(PdfDocument document)
    {
        Assert.True(document.TryGetBookmarks(out var bookmarks));
        return [.. bookmarks.GetNodes().Select(n => n.Title)];
    }

    private static string Plain(string bookmark) => bookmark.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' ');

    private static List<string> Images(PdfDocument document) =>
        [.. document.GetPages().SelectMany(p => p.GetImages().Select(i => $"{p.Number} {i.BoundingBox}"))];

    // Where the word starts on its page.
    private static List<string> Positions(PdfDocument document, string word) =>
        [.. document.GetPages().SelectMany(p => p.GetWords().Where(w => w.Text == word).Select(w => $"{p.Number} {w.BoundingBox.Left:F1} {w.BoundingBox.Bottom:F1}"))];

    private static string Text(PdfDocument document) =>
        string.Join("\n", document.GetPages().Select(p => string.Concat(p.Letters.Select(l => l.Value))));
}
