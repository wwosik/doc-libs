using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using wwosik.DocLibs.DocumentConversions.Svg;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>
/// E08-S02: a generated document converts back to Markdown that generates the same document again, part by part; without
/// the generator's namespace, as Word saves it, it still converts, with the fallbacks.
/// </summary>
public class RoundTripTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"60\" viewBox=\"0 0 120 60\"><rect x=\"0\" y=\"0\" width=\"120\" height=\"60\" fill=\"#3366CC\"/></svg>";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static TheoryData<string, string> SamplesAndTemplates
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var sample in new[] { "report", "report-diagrams", "long", "layouts", "pages", "drawings" })
            {
                foreach (var template in new[] { "neutral", "sample", "custom" })
                {
                    data.Add(sample, template);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SamplesAndTemplates))]
    public void EverySample_ConvertsBackToMarkdownThatGeneratesTheSameDocument(string sample, string template)
    {
        var (document, options) = Sample(sample, template);
        var first = Samples.Generate(document, options);

        var converted = Convert(first);
        var second = Samples.Generate(converted, Sample(sample, template).Options);

        AssertSameParts(first, second, converted.Markdown);

        // And once more: the Markdown is a fixed point.
        Assert.Equal(converted.Markdown, Convert(second).Markdown);
    }

    [Theory]
    [MemberData(nameof(SamplesAndTemplates))]
    public void WithoutTheNamespace_EverySampleStillConverts(string sample, string template)
    {
        var (document, options) = Sample(sample, template);
        var saved = WithoutNamespace(Samples.Generate(document, options));

        var markdown = Convert(saved).Markdown;

        Assert.NotEmpty(markdown);
        Assert.DoesNotContain(GeneratedDocxNamespace, Encoding.UTF8.GetString(saved), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheNamespace_CodeLosesItsLanguage_AChartComesFromItsValues_AndADiagramIsAPicture()
    {
        var options = new DocxOptions
        {
            CodeBlockRenderers = [new MermaidRenderer(), new ChartRenderer()],
            ImageConverters = [new SvgImageConverter()],
        };
        var docx = Samples.Generate(
            Samples.Markdown("## A\n\n```csharp\nvar x = 1;\n```\n\n```chart\ntype: line\ntitle: Orders\n---\nmonth, plan\nJan, 1\nFeb, 2\n```\n\n```mermaid\nflowchart LR\n    A --> B\n```\n"),
            options);

        var kept = Convert(docx).Markdown;
        var dropped = Convert(WithoutNamespace(docx)).Markdown;

        Assert.Contains("```csharp\nvar x = 1;\n```", kept, StringComparison.Ordinal);
        Assert.Contains("```chart\ntype: line\ntitle: Orders\n---\nmonth, plan\n", kept, StringComparison.Ordinal);
        Assert.Contains("```mermaid\nflowchart LR", kept, StringComparison.Ordinal);
        Assert.Contains("```\nvar x = 1;\n```", dropped, StringComparison.Ordinal);
        Assert.Contains("```chart\ntype: line\ntitle: Orders\n---\ncategory, plan\nJan, 1\nFeb, 2\n```", dropped, StringComparison.Ordinal);
        Assert.Contains("![flowchart LR](images/", dropped, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatter_ComesFromTheCorePropertiesTheCoverAndTheSettings()
    {
        var markdown = Samples.Markdown(
            "---\nauthor: Team Docs\nlang: de\nclassification: confidential\ndraft: true\ntoc: true\ntocDepth: 3\nheadingNumbers: false\njustify: true\n---\n"
            + "# Bericht\n\nDas Jahr im Überblick\n\n## Teil\n\nText.\n");

        var converted = Convert(Samples.Generate(markdown)).Markdown;

        Assert.StartsWith(
            "---\ntitle: Bericht\nsubtitle: Das Jahr im Überblick\nauthor: Team Docs\ndate: 09.03.2026\nlang: de\nclassification: confidential\ndraft: true\n"
            + "toc: true\ntocDepth: 3\nheadingNumbers: false\njustify: true\n---\n\n# Bericht\n\n## Teil\n\nText.\n",
            converted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleAndDate_InTheFrontMatter_GoOnTheCover()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\nsubtitle: First\ndate: Spring 2026\n---\n# Cover\n\nSecond\n\n## A\n")));
        var paragraphs = DocxGeneratorTests.Body(package).Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();

        Assert.Equal(["First", "Second"], paragraphs.Where(p => DocxGeneratorTests.Style(p) == "Subtitle").Select(p => DocxGeneratorTests.Texts(p)));
        Assert.Contains("Spring 2026", DocxGeneratorTests.Texts(paragraphs.Single(p => DocxGeneratorTests.Style(p) == "CoverDetails")), StringComparison.Ordinal);
    }

    [Fact]
    public void BlocksAndInlines_ComeBackByTheirRoles()
    {
        const string Markdown = """
            ## Text

            Plain **bold *both*** and `code`, ~~gone~~, ==marked==, [green]{.success} and a [link](https://example.com) to [Text](#text).

            > [!WARNING]
            > Careful.
            >
            > - One

            - [x] Done
            - [ ] Open

            Term
            :   Meaning

            ---

            ### Deep {.unnumbered .newpage}

            1. One
            2. Two
               - Nested

            Text with a note.[^1]

            [^1]: The note.
            """;

        var converted = Convert(Samples.Generate(Samples.Markdown(Markdown))).Markdown;

        Assert.Contains("Plain **bold *both*** and `code`, ~~gone~~, ==marked==, [green]{.success} and a [link](https://example.com) to [Text](#text).", converted, StringComparison.Ordinal);
        Assert.Contains("> [!WARNING]\n> Careful.\n>\n> - One", converted, StringComparison.Ordinal);
        Assert.Contains("- [x] Done\n- [ ] Open", converted, StringComparison.Ordinal);
        Assert.Contains("Term\n:   Meaning", converted, StringComparison.Ordinal);
        Assert.Contains("\n---\n", converted, StringComparison.Ordinal);
        Assert.Contains("## Deep {.unnumbered .newpage}", converted, StringComparison.Ordinal);
        Assert.Contains("1. One\n2. Two\n   - Nested", converted, StringComparison.Ordinal);
        Assert.Contains("Text with a note.[^1]", converted, StringComparison.Ordinal);
        Assert.EndsWith("[^1]: The note.\n", converted, StringComparison.Ordinal);
    }

    public static TheoryData<string> NewerFeatures => new()
    {
        // Appendices, lettered by the numbering or written as text.
        "# Report\n\n## One\n\nText.\n\n# Glossary {.appendix}\n\n## Terms\n\nWords.\n\n# Sources\n\nMore.\n",
        "---\nheadingNumbers: false\n---\n# Report\n\n# One\n\nText.\n\n# Glossary {.appendix}\n\nWords.\n\n# Sources\n\nMore.\n",

        // A cover image, file properties and an audience version.
        "---\ncoverImage: photo.png\ncoverImageSize: small\ncoverImagePosition: right\ndescription: The year at a glance\nkeywords: plan, review\naudience: internal\n---\n"
            + "# Report\n\nThe year\n\n## One\n\n::: internal\nOnly for us.\n:::\n\nFor all {.internal}\n\n::: exercise\nTry it.\n:::\n\n::: solution\nLike this.\n:::\n",
        "---\ncoverImage: diagram.png\n---\n# Report\n\n## One\n\nText.\n",

        // Image styles.
        "## Images\n\n![Round](photo.png){.round focus=top}\n\n![Grey](photo.png){.grey}\n\n![A](photo.png){fit=cover} ![B](diagram.png)\n",
    };

    [Theory]
    [MemberData(nameof(NewerFeatures))]
    public void NewerWordFeatures_RoundTrip(string markdown)
    {
        var options = new DocxOptions { Template = Samples.Template("sample") };
        var document = new MarkdownDocument(markdown, [Samples.Image("diagram.png"), Samples.Image("photo.png")], new Dictionary<string, string>());
        var first = Samples.Generate(document, options);

        var converted = Convert(first);
        AssertSameParts(first, Samples.Generate(converted, options), converted.Markdown);
        Assert.NotEmpty(Convert(WithoutNamespace(first)).Markdown);
    }

    [Fact]
    public void WithoutTheNamespace_AppendicesAndRoundImagesAreReadFromWhatWordShows()
    {
        var document = new MarkdownDocument(
            "---\nheadingNumbers: false\n---\n# Report\n\n# One\n\n![Round](photo.png){.round}\n\n# Glossary {.appendix}\n\nWords.\n\n# Sources\n\nMore.\n",
            [Samples.Image("photo.png")],
            new Dictionary<string, string>());

        var markdown = Convert(WithoutNamespace(Samples.Generate(document))).Markdown;

        Assert.Contains("# Glossary {.appendix}\n", markdown, StringComparison.Ordinal);
        Assert.Contains("# Sources\n", markdown, StringComparison.Ordinal);
        Assert.Contains("){.round}", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void FootnotesAreRead_FromAnyWordDocument()
    {
        var docx = OtherWordDocument();

        var markdown = Convert(docx).Markdown;

        Assert.Equal("Text with a note.[^1]\n\n[^1]: The note.\n", markdown);
    }

    // The generator's namespace, as the converter knows it.
    private static string GeneratedDocxNamespace => "urn:wwosik:doclibs:wordprocessingml";

    private static (MarkdownDocument Document, DocxOptions Options) Sample(string sample, string template)
    {
        var options = new DocxOptions { Template = Samples.Template(template), TableOfContents = true };
        switch (sample)
        {
            case "report-diagrams":
                return (Samples.Document("report.md"), options with
                {
                    CodeBlockRenderers = [new StandIn(), new ChartRenderer()],
                    ImageConverters = [new SvgImageConverter()],
                });
            case "drawings":
                var svg = new DocumentResource("shape.svg", "image/svg+xml", Encoding.UTF8.GetBytes(Svg));
                return (Samples.Markdown(File.ReadAllText(Samples.Path("drawings.md")).ReplaceLineEndings("\n"), svg), options with
                {
                    TableOfContents = false,
                    CodeBlockRenderers = [new MermaidRenderer(), new ChartRenderer()],
                    ImageConverters = [new SvgImageConverter()],
                });
            default:
                return (Samples.Document(sample + ".md"), options);
        }
    }

    private static MarkdownDocument Convert(byte[] docx) => new DocxConverter().Convert(new MemoryStream(docx), ConversionOptions.Default);

    private static void AssertSameParts(byte[] first, byte[] second, string markdown)
    {
        var one = Parts(first);
        var two = Parts(second);
        Assert.Equal(one.Select(p => p[..p.IndexOf(": ", StringComparison.Ordinal)]), two.Select(p => p[..p.IndexOf(": ", StringComparison.Ordinal)]));
        for (var i = 0; i < one.Count; i++)
        {
            if (one[i] != two[i])
            {
                var a = one[i];
                var b = two[i];
                var at = 0;
                while (at < a.Length && at < b.Length && a[at] == b[at])
                {
                    at++;
                }

                var from = Math.Max(0, at - 300);
                Assert.Fail($"{a[..a.IndexOf(": ", StringComparison.Ordinal)]} differs at {at}:\n{a[from..Math.Min(a.Length, at + 300)]}\n----\n{b[from..Math.Min(b.Length, at + 300)]}\n==== Markdown:\n{markdown}");
            }
        }
    }

    // Every part's content with the SDK's random relationship ids masked; an embedded workbook by its entries, whose
    // times differ.
    private static List<string> Parts(byte[] docx)
    {
        using var archive = new ZipArchive(new MemoryStream(docx));
        return [.. archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).Select(e =>
        {
            using var stream = e.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var text = e.FullName.EndsWith(".xlsx", StringComparison.Ordinal) ? string.Join("\n", Parts(buffer.ToArray()))
                : e.FullName.EndsWith(".odttf", StringComparison.Ordinal) || e.FullName.EndsWith(".png", StringComparison.Ordinal) ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray()))
                : System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(buffer.ToArray()), "R[0-9a-f]{16}", "R*");
            return System.Text.RegularExpressions.Regex.Replace(e.FullName, "R[0-9a-f]{16}", "R*") + ": " + text;
        })];
    }

    // As Word saves a file: everything in the generator's ignorable namespace is gone.
    internal static byte[] WithoutNamespace(byte[] docx)
    {
        XNamespace dl = GeneratedDocxNamespace;
        using var output = new MemoryStream();
        output.Write(docx);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)).ToList())
            {
                XDocument xml;
                using (var stream = entry.Open())
                {
                    xml = XDocument.Load(stream);
                }

                if (!xml.Descendants().Any(e => e.Name.Namespace == dl || e.Attributes().Any(a => a.Name.Namespace == dl || a.Value == GeneratedDocxNamespace)))
                {
                    continue;
                }

                xml.Descendants().Where(e => e.Name.Namespace == dl).Remove();
                foreach (var element in xml.Descendants())
                {
                    element.Attributes().Where(a => a.Name.Namespace == dl || a.Value == GeneratedDocxNamespace).Remove();
                }

                var name = entry.FullName;
                entry.Delete();
                using var write = archive.CreateEntry(name).Open();
                xml.Save(write, SaveOptions.DisableFormatting);
            }
        }

        return output.ToArray();
    }

    // A Word document not of the generator, with one footnote.
    private static byte[] OtherWordDocument()
    {
        using var output = new MemoryStream();
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(output, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new DocumentFormat.OpenXml.Wordprocessing.Body(
                new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Text with a note.")),
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.FootnoteReference { Id = 1 }))));
            var footnotes = main.AddNewPart<DocumentFormat.OpenXml.Packaging.FootnotesPart>();
            footnotes.Footnotes = new DocumentFormat.OpenXml.Wordprocessing.Footnotes(
                new DocumentFormat.OpenXml.Wordprocessing.Footnote(new DocumentFormat.OpenXml.Wordprocessing.Paragraph()) { Type = DocumentFormat.OpenXml.Wordprocessing.FootnoteEndnoteValues.Separator, Id = -1 },
                new DocumentFormat.OpenXml.Wordprocessing.Footnote(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.FootnoteReferenceMark()),
                    new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(" The note.") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve })))
                { Id = 1 });
        }

        return output.ToArray();
    }

    // As the report's diagrams test: the report's `graph TD; A-->B;` is no Mermaid the renderer takes, so a stand-in draws it.
    private sealed class StandIn : ICodeBlockRenderer
    {
        public CodeBlockGraphic? Render(CodeBlockRequest request) => request.Language == "mermaid"
            ? new DrawingGraphic(new Drawing(240, 120, [
                new DrawingShape(DrawingPath.Rectangle(10, 10, 220, 100), "DBEAFE", "1D4ED8", 2),
                new DrawingText(120, 66, "Flow", 16, "1D4ED8") { Anchor = TextAnchor.Middle }]))
            : null;
    }
}
