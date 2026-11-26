using System.Text;
using UglyToad.PdfPig;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>E09-S06: the subtitle, <c>description</c> and <c>keywords</c> in the PDF's document information and XMP.</summary>
public class DocumentPropertiesTests
{
    private const string Cover = "# Quarterly review\n\nResults and outlook\\\nSecond half\n\nFor the board\n\n## Results\n\nText.\n";

    // The XMP packet, as written.
    private static string Xmp(byte[] pdf)
    {
        var text = Encoding.UTF8.GetString(pdf);
        var begin = text.IndexOf("<?xpacket begin", StringComparison.Ordinal);
        return text[begin..text.IndexOf("<?xpacket end", begin, StringComparison.Ordinal)];
    }

    [Fact]
    public void TheSubtitle_IsTheSubject_AndKeywordsGoToTheInformationAndTheXmp()
    {
        var pdf = Samples.Generate(Samples.Markdown("---\ndescription: What the quarter brought.\nkeywords: review, results , ,2026\n---\n" + Cover));

        using var document = PdfDocument.Open(pdf);
        Assert.Equal("Results and outlook – Second half – For the board", document.Information.Subject);
        Assert.Equal("review, results, 2026", document.Information.Keywords);
        var xmp = Xmp(pdf);

        // PDF/A: /Subject and /Keywords have the same values in the XMP, dc:description a language alternative.
        Assert.Contains("<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">Results and outlook – Second half – For the board</rdf:li></rdf:Alt></dc:description>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdf:Keywords>review, results, 2026</pdf:Keywords>", xmp, StringComparison.Ordinal);
        Assert.Contains("<dc:subject><rdf:Bag><rdf:li>review</rdf:li><rdf:li>results</rdf:li><rdf:li>2026</rdf:li></rdf:Bag></dc:subject>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", xmp, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutASubtitle_TheDescriptionIsTheSubject()
    {
        var pdf = Samples.Generate(Samples.Markdown("---\ndescription: What the quarter brought.\n---\n## Results\n\nText.\n"));

        using var document = PdfDocument.Open(pdf);
        Assert.Equal("What the quarter brought.", document.Information.Subject);
        Assert.Null(document.Information.Keywords);
        Assert.Contains("<rdf:li xml:lang=\"x-default\">What the quarter brought.</rdf:li>", Xmp(pdf), StringComparison.Ordinal);
        Assert.DoesNotContain("pdf:Keywords", Xmp(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnyOfThem_ThereIsNoSubjectOrKeywords()
    {
        var pdf = Samples.Generate(Samples.Markdown("## Results\n\nText.\n"));

        using var document = PdfDocument.Open(pdf);
        Assert.Null(document.Information.Subject);
        Assert.Null(document.Information.Keywords);
        Assert.DoesNotContain("dc:description", Xmp(pdf), StringComparison.Ordinal);
        Assert.DoesNotContain("dc:subject", Xmp(pdf), StringComparison.Ordinal);
    }
}
