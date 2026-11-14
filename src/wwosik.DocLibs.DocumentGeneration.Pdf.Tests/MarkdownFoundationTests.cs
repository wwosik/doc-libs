using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Footnotes, task lists, definition lists, the document language and drawing fill rules in PDFs.</summary>
public partial class MarkdownFoundationTests
{
    private static (GenerationResult Result, byte[] Pdf) Generate(string markdown, PdfOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(Samples.Markdown(markdown), output, (options ?? new PdfOptions()) with { TimeProvider = Samples.FixedTime });
        return (result, output.ToArray());
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    [Fact]
    public void Footnotes_AreRaisedNumbers_ListedAtTheEnd_InTheOrderOfTheirReferences()
    {
        var (result, pdf) = Generate("# Report\n\nIntro.\n\n# Costs\n\nRaised[^b] and lowered[^a].\n\n[^a]: Second note.\n[^b]: First note.\n");

        Assert.False(result.HasNotes, result.ToString());
        using var document = PdfDocument.Open(pdf);
        var page = document.GetPages().Last();
        var words = page.GetWords().ToList();
        var raised = words.Single(w => w.Text.StartsWith("Raised", StringComparison.Ordinal));
        var one = page.Letters.First(l => l.Value == "1" && Math.Abs(l.StartBaseLine.Y - raised.Letters[0].StartBaseLine.Y) < 6);
        Assert.True(one.StartBaseLine.Y > raised.Letters[0].StartBaseLine.Y + 2, "the reference is raised");
        Assert.True(one.PointSize < raised.Letters[0].PointSize, "the reference is smaller");

        var text = page.Text;
        Assert.Contains("Notes", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("First note.", StringComparison.Ordinal) < text.IndexOf("Second note.", StringComparison.Ordinal));
        Assert.True(text.IndexOf("lowered", StringComparison.Ordinal) < text.IndexOf("Notes", StringComparison.Ordinal));
    }

    [Fact]
    public void AReferenceWithoutDefinition_StaysText_AndIsReported()
    {
        var (result, pdf) = Generate("---\ntitle: T\n---\nText[^gone] here.\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.FootnoteMissing, note.Code);
        Assert.Equal(4, note.Line);
        Assert.Contains("'[^gone]'", note.Message, StringComparison.Ordinal);
        Assert.Contains("[^gone]", Text(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void TaskLists_GetBoxes_DrawnRatherThanWritten()
    {
        // Liberation Sans has no ☐ or ☒: written as text, they would throw without a replacement hook.
        var (result, pdf) = Generate("- [ ] open item\n- [x] done item\n- plain item\n");

        Assert.False(result.HasNotes, result.ToString());
        var text = Text(pdf);
        Assert.Contains("open item", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[ ]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[x]", text, StringComparison.Ordinal);
        Assert.Contains("•", text, StringComparison.Ordinal); // the plain item keeps its bullet
        var (_, plain) = Generate("- open item\n- done item\n- plain item\n");
        Assert.Equal(3, Strokes(pdf) - Strokes(plain)); // two boxes and one tick
    }

    [Fact]
    public void DefinitionLists_SetTheTermBold_AndIndentTheDescription()
    {
        var (_, pdf) = Generate("Intro.\n\nLatency\n:   The time a request takes.\n:   Measured at the edge.\n\nThroughput\n:   Requests per second.\n");

        using var document = PdfDocument.Open(pdf);
        var words = document.GetPage(1).GetWords().ToList();
        var term = words.Single(w => w.Text == "Latency");
        var description = words.Single(w => w.Text == "The");
        Assert.Contains("Bold", term.Letters[0].FontName, StringComparison.Ordinal);
        Assert.DoesNotContain("Bold", description.Letters[0].FontName, StringComparison.Ordinal);
        Assert.True(description.BoundingBox.Left > term.BoundingBox.Left + 10, "the description is indented");
        Assert.Contains(words, w => w.Text == "Measured");
        Assert.DoesNotContain(words, w => w.Text.StartsWith(':'));
    }

    [Theory]
    [InlineData("de", "Hinweis", "Anmerkungen")]
    [InlineData("de-CH", "Hinweis", "Anmerkungen")]
    [InlineData("en-GB", "Note", "Notes")]
    public void TheLanguage_PicksTheGeneratedTexts(string tag, string alert, string footnotes)
    {
        var (result, pdf) = Generate($"---\nlang: {tag}\n---\n> [!NOTE]\n> Watch out[^1].\n\n[^1]: A note.\n");

        Assert.False(result.HasNotes, result.ToString());
        var text = Text(pdf);
        Assert.Contains(alert, text, StringComparison.Ordinal);
        Assert.Contains(footnotes, text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownLanguage_FallsBackToEnglish_WithANote_AndKeepsItsTag()
    {
        var (result, pdf) = Generate("---\nlang: pl\n---\n> [!WARNING]\n> Careful.\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.LanguageUnknown, note.Code);
        Assert.Contains("Warning", Text(pdf), StringComparison.Ordinal);
        Assert.Contains("/Lang (pl)", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void HostTexts_AddALanguage_AndTheOptionsSetTheDefault()
    {
        var polish = GeneratedTexts.English with { Warning = "Ostrzeżenie" };
        var (result, pdf) = Generate("> [!WARNING]\n> Careful.\n", new PdfOptions { Language = "pl", Texts = new Dictionary<string, GeneratedTexts> { ["pl"] = polish } });

        Assert.False(result.HasNotes, result.ToString());
        Assert.Contains("Ostrzeżenie", Text(pdf), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("---\nlang: not a tag\n---\nText.\n")]
    [InlineData("---\nlang: e\n---\nText.\n")]
    public void AnInvalidLanguage_Throws(string markdown)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));
        Assert.Contains("'lang'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de", "Short")]
    [InlineData("en", "A very long title that does not fit into the packet PdfPig leaves for its metadata, with ümlauts and more ümlauts, so that the XMP stream has to grow by quite a few bytes beyond what it had")]
    public void TheLanguage_IsInTheCatalogAndTheXmp_AndEveryOffsetStillPointsAtItsObject(string tag, string title)
    {
        var (_, pdf) = Generate($"---\nlang: {tag}\ntitle: {title}\n---\n# Cover\n\nText [with a link](#cover).\n");

        var raw = Encoding.Latin1.GetString(pdf);
        Assert.Contains($"/Type /Catalog /Lang ({tag})", raw, StringComparison.Ordinal);
        Assert.Contains($"<dc:language><rdf:Bag><rdf:li>{tag}</rdf:li></rdf:Bag></dc:language>", raw, StringComparison.Ordinal);
        AssertOffsets(raw);
        using var document = PdfDocument.Open(pdf);
        Assert.Equal(title, document.Information.Title);
    }

    [Fact]
    public void Structure_RoundTripsThroughThePdfConverter()
    {
        const string markdown = """
            # Report

            Intro with a reference[^a] in it.

            | Name | Size |
            | ---- | ---: |
            | Alpha | 12 |
            | Beta | 7 |

            - Fruit
              - Apples
            - Vegetables

            Tasks:

            - [ ] open item
            - [x] done item

            1. First step
            2. Second step

            ```
            if (ready)
                Start();
            ```

            [^a]: The note.
            """;
        var (_, pdf) = Generate(markdown);

        var back = new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(pdf), ConversionOptions.Default).Markdown;

        Assert.Contains("| Name | Size |\n| --- | --- |\n| Alpha | 12 |\n| Beta | 7 |", back, StringComparison.Ordinal);
        Assert.Contains("- Fruit\n  - Apples\n- Vegetables", back, StringComparison.Ordinal);
        Assert.Contains("- [ ] open item\n- [x] done item", back, StringComparison.Ordinal);
        Assert.Contains("1. First step\n2. Second step", back, StringComparison.Ordinal);
        Assert.Contains("```\nif (ready)\n    Start();\n```", back, StringComparison.Ordinal);

        // The generator lists notes at the end in the body size, so the reference stays a raised number on its line.
        Assert.Contains("Intro with a reference1 in it.", back, StringComparison.Ordinal);
    }

    [Fact]
    public void EvenOdd_FillsWithTheEvenOddOperator_AndAnInvalidDrawingThrows()
    {
        Drawing Ring(DrawingFillRule rule, string fill) => new(20, 20, [new DrawingShape(
            new DrawingPath([.. DrawingPath.Rectangle(0, 0, 20, 20).Segments, .. DrawingPath.Rectangle(5, 5, 10, 10).Segments]), fill, null, 0) { FillRule = rule }]);
        PdfOptions With(Drawing drawing) => new() { CodeBlockRenderers = [Samples.Renderer(_ => new DrawingGraphic(drawing))] };

        var (_, evenOdd) = Generate("```x\ny\n```\n", With(Ring(DrawingFillRule.EvenOdd, "1F4E79")));
        var (_, nonZero) = Generate("```x\ny\n```\n", With(Ring(DrawingFillRule.NonZero, "1F4E79")));

        using (var document = PdfDocument.Open(evenOdd))
        {
            Assert.Contains(document.GetPage(1).Operations, o => o.Operator == "f*");
        }

        using (var document = PdfDocument.Open(nonZero))
        {
            Assert.DoesNotContain(document.GetPage(1).Operations, o => o.Operator == "f*");
        }

        var error = Assert.Throws<ArgumentException>(() => Generate("```x\ny\n```\n", With(Ring(DrawingFillRule.NonZero, "red"))));
        Assert.Contains("'red'", error.Message, StringComparison.Ordinal);
    }

    private static int Strokes(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPage(1).Operations.Count(o => o.Operator == "S");
    }

    // Each in-use entry of the cross-reference table points at "n g obj" of its own number.
    private static void AssertOffsets(string raw)
    {
        var table = raw.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;
        var lines = raw[table..].Split('\n');
        var first = int.Parse(lines[1].Split(' ')[0], CultureInfo.InvariantCulture);
        var checkedObjects = 0;
        for (var i = 2; i < lines.Length && EntryPattern().Match(lines[i]) is { Success: true } entry; i++)
        {
            if (entry.Groups[2].Value == "n")
            {
                var offset = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.StartsWith($"{first + i - 2} 0 obj", raw[offset..], StringComparison.Ordinal);
                checkedObjects++;
            }
        }

        Assert.True(checkedObjects > 5);
        var startxref = raw.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.Equal(table, int.Parse(raw[(startxref + 10)..].Split('\n')[0].Trim(), CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"^(\d{10}) \d{5} ([nf])\s*$")]
    private static partial Regex EntryPattern();
}
