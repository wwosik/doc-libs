using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary><see cref="DocxGenerator.Check"/>: the same rules as PDFs, on the Word generator.</summary>
public class CheckTests
{
    [Theory]
    [InlineData("report.md")]
    [InlineData("long.md")]
    [InlineData("layouts.md")]
    [InlineData("pages.md")]
    public void Check_ReturnsEveryNoteOfGenerate(string name)
    {
        var document = Samples.Document(name);
        using var output = new MemoryStream();
        var generated = DocxGenerator.Generate(document, output);

        var check = DocxGenerator.Check(document);

        Assert.Null(check.Error);
        Assert.Equal(0, check.Statistics.Pages);
        Assert.All(generated.Notes, note => Assert.Contains(note, check.Notes));
        Assert.All(check.Notes.Except(generated.Notes), note => Assert.Contains(note.Code, NoteCodes.CheckOnly));
    }

    [Fact]
    public void Rules_AreThoseOfDocuments_AndErrorsAreReturned()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 151));
        var markdown = $"# Report\n\n{words}\n\n## {new string('x', 61)}\n\n![](missing.png)\n";

        var check = DocxGenerator.Check(Samples.Markdown(markdown));

        Assert.Null(check.Error);
        Assert.Equal(
            [(NoteCodes.LongParagraph, 3), (NoteCodes.TitleTooLong, 5), (NoteCodes.AltTextMissing, 7), (NoteCodes.ImageSource, 7)],
            check.Notes.Select(n => (n.Code, n.Line)));
        Assert.Equal(1, check.Statistics.Images);
        Assert.NotNull(DocxGenerator.Check(Samples.Markdown("---\nnoSuchKey: 1\n---\n# Report\n")).Error);
    }

    [Fact]
    public void TheGuide_ListsTheLimits()
    {
        var guide = DocxGenerator.AuthoringGuide();

        Assert.Contains("at most 150 words in a paragraph", guide, StringComparison.Ordinal);
        Assert.Contains("`image-source`", guide, StringComparison.Ordinal);
    }
}
