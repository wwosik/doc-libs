using System.Runtime.Versioning;

namespace wwosik.DocLibs.DocumentConversions.WindowsOnly.Tests;

[SupportedOSPlatform("windows")]
public sealed class OfficeConverterTests : IDisposable
{
    private readonly OfficeConverter? office = OperatingSystem.IsWindows() ? new OfficeConverter() : null;

    public void Dispose() => this.office?.Dispose();

    [OfficeTheory]
    [InlineData("sample.doc")]
    [InlineData("sample.rtf")]
    [InlineData("sample.xls")]
    [InlineData("sample.xlsx")]
    public void Samples_ConvertToTheirExpectedMarkdown(string sample)
    {
        using var stream = Samples.Open(sample);

        var document = DocumentConverterRegistry.CreateDefault(this.office!).Convert(stream, sample);

        Samples.AssertMatchesExpected(sample, document.Markdown);
    }

    [OfficeFact]
    public void Doc_KeepsItsImages()
    {
        using var stream = Samples.Open("sample.doc");

        var document = DocumentConverterRegistry.CreateDefault(this.office!).Convert(stream, "sample.doc");

        var image = Assert.Single(document.Resources);
        Assert.Contains("](" + image.Path + ")", document.Markdown, StringComparison.Ordinal);
    }

    [OfficeFact]
    public void Workbooks_ReportTheirSheetCount()
    {
        using var stream = Samples.Open("sample.xlsx");

        var document = this.office!.Convert(stream, ConversionOptions.Default);

        Assert.Equal("2", document.Metadata[MetadataKeys.SheetCount]);
        Assert.Contains("## Empty\n\n*(empty)*", document.Markdown, StringComparison.Ordinal);
    }

    [OfficeFact]
    public void TheWorkingDirectory_IsLeftEmpty()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var stream = Samples.Open("sample.rtf");

            this.office!.Convert(stream, new ConversionOptions { WorkingDirectory = root });

            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [OfficeFact]
    public void OleFiles_NeedTheirFileName()
    {
        using var stream = Samples.Open("sample.xls");

        var error = Assert.Throws<DocumentConversionException>(() => this.office!.Convert(stream, ConversionOptions.Default));

        Assert.Equal(DocumentFormat.Ole, error.Format);
    }

    [OfficeFact]
    public void OtherFormats_AreRejected()
    {
        var error = Assert.Throws<DocumentConversionException>(
            () => this.office!.Convert(new MemoryStream("%PDF-1.7"u8.ToArray()), ConversionOptions.Default));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
    }
}

public class PlatformTests
{
    [Fact]
    public void OffWindows_TheConverterCannotBeCreated()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1416 // The point of the test is calling it off Windows.
        Assert.Throws<PlatformNotSupportedException>(() => new OfficeConverter());
#pragma warning restore CA1416
    }
}
