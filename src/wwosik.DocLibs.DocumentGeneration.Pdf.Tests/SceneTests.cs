using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary><c>::: scene</c> in PDFs: one still vector drawing of its final state.</summary>
public class SceneTests
{
    private static DocumentModel Parse(string markdown) => MarkdownParser.Parse(Samples.Markdown(markdown), new PdfOptions());

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsPdfA_AndRoundTrips(string template)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Document("scenes.md"), output, new PdfOptions { Template = Samples.BuiltIn(template), TimeProvider = Samples.FixedTime });
        var pdf = output.ToArray();
        Samples.WriteForReview($"scenes-{template}.pdf", pdf);

        Assert.False(result.HasNotes, result.ToString());
        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Samples.AssertMatchesExpected($"scenes-{template}.roundtrip", Samples.RoundTrip(pdf));
    }

    [Fact]
    public void AScene_IsOneDrawing_OfItsFinalState()
    {
        var model = Parse("## Flow\n\n::: scene\nbox a 10 30 20 10 \"Stays\"\nbox b 60 30 20 10 \"Comes\" step=1\nbox c 110 30 20 10 \"Goes\" exit=2\narrow a b\narrow b c\n:::\n");

        var drawing = Assert.IsType<DrawingBlock>(model.Blocks[1]).Drawing;
        Assert.Equal((960.0, 540.0), (drawing.Width, drawing.Height));
        Assert.Equal(["Stays", "Comes"], drawing.Items.OfType<DrawingText>().Select(t => t.Text));
        Assert.Equal(2 + 2, drawing.Items.OfType<DrawingShape>().Count());
    }

    [Theory]
    [InlineData("## S\n\n::: scene\nbox a 1 1 2 2 .wide\n:::\n", "'box a 1 1 2 2 .wide' at line 4 has '.wide'")]
    [InlineData("## S\n\n::: scene\nbox a 1 1 2 2\narrow a b\n:::\n", "The connector 'a-b' at line 5 joins 'b'")]
    [InlineData("## S\n\nText.\n\n::: scene\nbox a 1 1 2 2\n:::\n", "has more than the scene (line 3)")]
    [InlineData("## S\n\n{.build}\n\n::: scene\nbox a 1 1 2 2\n:::\n", "has a build marker")]
    [InlineData("::: scene\nbox a 1 1 2 2\n:::\n", "is before the first heading")]
    public void Mistakes_ThrowInThePdfsFormat(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Parse(markdown));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASceneWithNotes_LeavesTheNotesOut()
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(Samples.Document("scenes.md"), output, new PdfOptions());

        using var document = PdfDocument.Open(output.ToArray());
        var text = string.Concat(document.GetPages().Select(p => p.Text));
        Assert.DoesNotContain("click twice", text, StringComparison.Ordinal);
        Assert.Contains("Database", text, StringComparison.Ordinal);
    }
}
