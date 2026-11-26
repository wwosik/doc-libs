using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>E09-S06: the title slide's subtitle, <c>description</c>, <c>keywords</c> and the last editor in a deck's core properties.</summary>
public class CorePropertiesTests
{
    private const string Deck = "# Quarterly review\n\nResults and outlook\\\nSecond half\n\n## Results\n\n- One\n";

    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Terms = "http://purl.org/dc/terms/";

    private static XElement Core(byte[] pptx)
    {
        using var package = Samples.Open(pptx);
        using var stream = package.CoreFilePropertiesPart!.GetStream();
        return XDocument.Load(stream).Root!;
    }

    [Fact]
    public void TheSubtitle_Description_AndKeywords_AreInTheCoreProperties()
    {
        var pptx = Samples.Generate(Samples.Markdown("---\nauthor: Team Docs\ndescription: What the quarter brought.\nkeywords: review, results\n---\n" + Deck));
        Samples.AssertValid(pptx);

        var core = Core(pptx);
        Assert.Equal("Results and outlook – Second half", core.Element(Dc + "subject")?.Value);
        Assert.Equal("What the quarter brought.", core.Element(Dc + "description")?.Value);
        Assert.Equal("review, results", core.Element(Cp + "keywords")?.Value);
        Assert.Equal("Team Docs", core.Element(Cp + "lastModifiedBy")?.Value);
    }

    [Fact]
    public void ADeckWithoutATitleSlide_HasNoSubject()
    {
        var core = Core(Samples.Generate(Samples.Markdown("## Results\n\n- One\n")));

        Assert.Null(core.Element(Dc + "subject"));
        Assert.Null(core.Element(Dc + "description"));
        Assert.Null(core.Element(Cp + "keywords"));
    }

    [Fact]
    public void ACallersTemplate_KeepsOnlyItsDates()
    {
        var buffer = new MemoryStream();
        buffer.Write(Samples.TemplateBytes(Samples.BuiltIn("neutral")));
        using (var package = PresentationDocument.Open(buffer, true))
        {
            var part = package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart();
            using var output = part.GetStream(FileMode.Create, FileAccess.Write);
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            new XDocument(new XElement(
                Cp + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "cp", Cp),
                new XAttribute(XNamespace.Xmlns + "dc", Dc),
                new XAttribute(XNamespace.Xmlns + "dcterms", Terms),
                new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                new XElement(Dc + "title", "House template"),
                new XElement(Dc + "creator", "Template author"),
                new XElement(Cp + "keywords", "template, house"),
                new XElement(Cp + "lastModifiedBy", "Template editor"),
                new XElement(Cp + "revision", "17"),
                new XElement(Terms + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"), "2020-01-02T03:04:05Z"))).Save(output);
        }

        var pptx = Samples.Generate(
            Samples.Markdown("## Results\n\n- One\n"),
            new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(buffer.ToArray())) });
        Samples.AssertValid(pptx);

        var core = Core(pptx);
        Assert.DoesNotContain("Template", core.ToString(), StringComparison.Ordinal);
        Assert.Null(core.Element(Cp + "revision"));
        Assert.Equal("2020-01-02T03:04:05Z", core.Element(Terms + "created")?.Value);
    }
}
