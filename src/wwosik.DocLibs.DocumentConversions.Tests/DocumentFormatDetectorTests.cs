namespace wwosik.DocLibs.DocumentConversions.Tests;

public class DocumentFormatDetectorTests
{
    [Theory]
    [InlineData("sample.pdf", DocumentFormat.Pdf)]
    [InlineData("sample.docx", DocumentFormat.Docx)]
    [InlineData("sample.pptx", DocumentFormat.Pptx)]
    [InlineData("sample.xlsx", DocumentFormat.Xlsx)]
    [InlineData("sample.zip", DocumentFormat.Zip)]
    [InlineData("sample.rtf", DocumentFormat.Rtf)]
    [InlineData("sample.txt", DocumentFormat.Text)]
    [InlineData("sample.doc", DocumentFormat.Doc)]
    [InlineData("sample.xls", DocumentFormat.Xls)]
    public void Samples_AreRecognized(string sample, DocumentFormat expected)
    {
        using var stream = Samples.Open(sample);

        Assert.Equal(expected, DocumentFormatDetector.Detect(stream, sample));
    }

    [Theory]
    [InlineData("sample.doc")]
    [InlineData("sample.xls")]
    public void OleFiles_WithoutAKnownExtension_AreJustOle(string sample)
    {
        using var stream = Samples.Open(sample);

        Assert.Equal(DocumentFormat.Ole, DocumentFormatDetector.Detect(stream, "backup.old"));
        Assert.Equal(DocumentFormat.Ole, DocumentFormatDetector.Detect(stream));
    }

    [Fact]
    public void ContentWins_OverAMisleadingExtension()
    {
        using var stream = Samples.Open("sample.pdf");

        Assert.Equal(DocumentFormat.Pdf, DocumentFormatDetector.Detect(stream, "report.docx"));
    }

    [Fact]
    public void BinaryAndEmptyInput_IsUnknown()
    {
        Assert.Equal(DocumentFormat.Unknown, DocumentFormatDetector.Detect(new MemoryStream([1, 2, 0, 3])));
        Assert.Equal(DocumentFormat.Unknown, DocumentFormatDetector.Detect(new MemoryStream()));
        Assert.Equal(DocumentFormat.Unknown, DocumentFormatDetector.Detect(new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0xFF, 0x00])));
    }

    [Fact]
    public void ThePosition_IsRestored()
    {
        using var stream = Samples.Open("sample.docx");
        stream.Position = 0;

        DocumentFormatDetector.Detect(stream);

        Assert.Equal(0, stream.Position);
    }
}
