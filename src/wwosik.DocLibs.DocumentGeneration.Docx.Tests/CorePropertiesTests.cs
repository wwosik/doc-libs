using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>E09-S06: the subtitle, <c>description</c>, <c>keywords</c> and the last editor in the core properties.</summary>
public class CorePropertiesTests
{
    private const string Cover = "# Quarterly review\n\nResults and outlook\\\nSecond half\n\nFor the board\n\n## Results\n\nText.\n";

    private static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Terms = "http://purl.org/dc/terms/";

    private static XElement Core(byte[] docx)
    {
        using var package = Samples.Open(docx);
        using var stream = package.CoreFilePropertiesPart!.GetStream();
        return XDocument.Load(stream).Root!;
    }

    // The custom template with core properties of its own, as a house template saved in Word has them.
    private static DocxTemplateOptions TemplateWithProperties()
    {
        var buffer = new MemoryStream();
        buffer.Write(CustomTemplate.Content);
        using (var package = WordprocessingDocument.Open(buffer, true))
        {
            var part = package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart();
            using var output = part.GetStream(FileMode.Create, FileAccess.Write);
            new XDocument(new XElement(
                Cp + "coreProperties",
                new XAttribute(XNamespace.Xmlns + "cp", Cp),
                new XAttribute(XNamespace.Xmlns + "dc", Dc),
                new XAttribute(XNamespace.Xmlns + "dcterms", Terms),
                new XElement(Dc + "title", "House template"),
                new XElement(Dc + "creator", "Template author"),
                new XElement(Dc + "subject", "Template subject"),
                new XElement(Cp + "keywords", "template, house"),
                new XElement(Cp + "lastModifiedBy", "Template editor"),
                new XElement(Cp + "revision", "17"),
                new XElement(Cp + "category", "Templates"))).Save(output);
        }

        return new CustomDocxTemplateOptions(new MemoryStream(buffer.ToArray())) { Styles = CustomTemplate.Styles };
    }

    [Fact]
    public void TheSubtitle_Description_AndKeywords_AreInTheCoreProperties()
    {
        var docx = Samples.Generate(Samples.Markdown("---\nauthor: Team Docs\ndescription: What the quarter brought.\nkeywords: review, results , ,2026\n---\n" + Cover));
        Samples.AssertValid(docx);

        var core = Core(docx);
        Assert.Equal("Results and outlook – Second half – For the board", core.Element(Dc + "subject")?.Value);
        Assert.Equal("What the quarter brought.", core.Element(Dc + "description")?.Value);
        Assert.Equal("review, results, 2026", core.Element(Cp + "keywords")?.Value);
        Assert.Equal("Team Docs", core.Element(Cp + "lastModifiedBy")?.Value);
        Assert.Equal("Team Docs", core.Element(Dc + "creator")?.Value);
    }

    [Fact]
    public void WithoutThem_NoneIsWritten()
    {
        var core = Core(Samples.Generate(Samples.Markdown("## Results\n\nText.\n")));

        Assert.Null(core.Element(Dc + "subject"));
        Assert.Null(core.Element(Dc + "description"));
        Assert.Null(core.Element(Cp + "keywords"));
        Assert.Null(core.Element(Cp + "lastModifiedBy"));
    }

    [Fact]
    public void TheSubject_IsTypeset_AsTheCoverIs()
    {
        var core = Core(Samples.Generate(Samples.Markdown("# Review\n\nThe \"big\" quarter\n"), new DocxOptions { Typography = true }));

        Assert.Equal("The “big” quarter", core.Element(Dc + "subject")?.Value);
    }

    [Fact]
    public void NothingOfACallersTemplatesProperties_Survives()
    {
        var docx = Samples.Generate(Samples.Markdown("## Results\n\nText.\n"), new DocxOptions { Template = TemplateWithProperties() });
        Samples.AssertValid(docx);

        var core = Core(docx);
        Assert.Equal(
            ["language", "created", "modified"],
            core.Elements().Select(e => e.Name.LocalName));
        Assert.DoesNotContain("Template", core.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACallersTemplate_TakesTheDocumentsOwnProperties()
    {
        var docx = Samples.Generate(Samples.Markdown("---\nauthor: Team Docs\nkeywords: review\n---\n" + Cover), new DocxOptions { Template = TemplateWithProperties() });

        var core = Core(docx);
        Assert.Equal("Quarterly review", core.Element(Dc + "title")?.Value);
        Assert.Equal("Team Docs", core.Element(Cp + "lastModifiedBy")?.Value);
        Assert.Equal("review", core.Element(Cp + "keywords")?.Value);
        Assert.Null(core.Element(Cp + "revision"));
        Assert.Null(core.Element(Cp + "category"));
    }
}
