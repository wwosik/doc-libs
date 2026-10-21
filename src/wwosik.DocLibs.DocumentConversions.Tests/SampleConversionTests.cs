using wwosik.DocLibs.DocumentConversions.OpenXml;
using wwosik.DocLibs.DocumentConversions.Pdf;

namespace wwosik.DocLibs.DocumentConversions.Tests;

public class SampleConversionTests
{
    [Theory]
    [InlineData("sample.pdf")]
    [InlineData("sample.docx")]
    [InlineData("sample.pptx")]
    public void Samples_ConvertToTheirExpectedMarkdown(string sample)
    {
        using var stream = Samples.Open(sample);

        var document = DocumentConverterRegistry.CreateDefault().Convert(stream, sample);

        Samples.AssertMatchesExpected(sample, document.Markdown);
        Samples.AssertResourcesMatchReferences(document);
        Assert.NotEmpty(document.Resources);
    }

    [Fact]
    public void Pdf_DropsTheLogoOnEveryPage_AndKeepsTheChart()
    {
        using var stream = Samples.Open("sample.pdf");

        var document = new PdfConverter().Convert(stream, ConversionOptions.Default);

        var image = Assert.Single(document.Resources);
        Assert.Equal("images/page2-image1.png", image.Path);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal("3", document.Metadata[MetadataKeys.PageCount]);
        Assert.Equal("Sample document", document.Metadata[MetadataKeys.Title]);
        Assert.Equal("DocLibs", document.Metadata[MetadataKeys.Author]);
        Assert.DoesNotContain(MetadataKeys.SkippedImages, document.Metadata.Keys);
        Assert.DoesNotContain("Sample document", document.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("of 3", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Docx_StoresIdenticalImagesOnce()
    {
        using var stream = Samples.Open("sample.docx");

        var document = new DocxConverter().Convert(stream, ConversionOptions.Default with { ResourcePathPrefix = "media/" });

        var image = Assert.Single(document.Resources);
        Assert.Equal("media/image1.png", image.Path);
        Assert.Contains("![Bar chart](media/image1.png)", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("![Bar chart again](media/image1.png)", document.Markdown, StringComparison.Ordinal);
        Assert.Equal("Sample document", document.Metadata[MetadataKeys.Title]);
    }

    [Fact]
    public void Pptx_UsesSlideTitlesAsHeadings()
    {
        using var stream = Samples.Open("sample.pptx");

        var document = new PptxConverter().Convert(stream, ConversionOptions.Default);

        Assert.StartsWith("## Quarterly review\n", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Highlights\n", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("## Slide 3\n", document.Markdown, StringComparison.Ordinal);
        Assert.Contains("![Team photo](images/image1.png)", document.Markdown, StringComparison.Ordinal);
        Assert.Equal("3", document.Metadata[MetadataKeys.SlideCount]);
    }

    [Theory]
    [InlineData("sample.pdf")]
    [InlineData("sample.docx")]
    [InlineData("sample.pptx")]
    public void WithoutImages_NothingIsExtractedOrReferenced(string sample)
    {
        using var stream = Samples.Open(sample);

        var document = DocumentConverterRegistry.CreateDefault().Convert(stream, sample, new ConversionOptions { IncludeImages = false });

        Assert.Empty(document.Resources);
        Assert.DoesNotContain("![", document.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void NonSeekableInput_IsAccepted()
    {
        var bytes = File.ReadAllBytes(Samples.Path("sample.pdf"));

        var document = new PdfConverter().Convert(new ForwardOnlyStream(bytes), ConversionOptions.Default);

        Assert.Contains("## Page 1", document.Markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("%PDF-1.7 broken", DocumentFormat.Pdf)]
    [InlineData("PK\x03\x04 broken", DocumentFormat.Docx)]
    [InlineData("PK\x03\x04 broken", DocumentFormat.Pptx)]
    public void DamagedInput_ThrowsADocumentConversionException(string content, DocumentFormat format)
    {
        IDocumentConverter converter = format switch
        {
            DocumentFormat.Pdf => new PdfConverter(),
            DocumentFormat.Docx => new DocxConverter(),
            _ => new PptxConverter(),
        };

        var error = Assert.Throws<DocumentConversionException>(
            () => converter.Convert(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(content)), ConversionOptions.Default));

        Assert.Equal(format, error.Format);
    }

    [Fact]
    public void UnsupportedFormats_AreRejectedByTheRegistry()
    {
        using var stream = Samples.Open("sample.zip");

        var error = Assert.Throws<DocumentConversionException>(() => DocumentConverterRegistry.CreateDefault().Convert(stream, "sample.zip"));

        Assert.Equal(DocumentFormat.Zip, error.Format);
    }

    [Fact]
    public void TheRegistry_PassesTheFileNameOn()
    {
        var spy = new SpyConverter();
        using var stream = Samples.Open("sample.doc");

        new DocumentConverterRegistry([spy]).Convert(stream, "letter.doc");

        Assert.Equal("letter.doc", spy.Options?.FileName);
    }

    private sealed class SpyConverter : IDocumentConverter
    {
        public ConversionOptions? Options { get; private set; }

        public bool CanConvert(DocumentFormat format) => format == DocumentFormat.Doc;

        public MarkdownDocument Convert(Stream input, ConversionOptions options)
        {
            this.Options = options;
            return new MarkdownDocument(string.Empty, [], new Dictionary<string, string>());
        }
    }

    private sealed class ForwardOnlyStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
