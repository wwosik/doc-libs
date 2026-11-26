using System.Text.Json;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using wwosik.DocLibs.DocumentGeneration.Docx;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>
/// Audience versions (E06-S06): one Markdown, three versions, in every generator, the HTML deck included; their marking,
/// misplaced markers, the helper for hosts and the tool's parameter.
/// </summary>
public class AudienceTests
{
    private const string Course = """
        # Course

        For everyone

        ## Basics

        Everyone sees this.

        {.internal}

        Team note on pricing.

        ::: internal
        Internal block.
        :::

        ::: exercise
        Build a deck.
        :::

        ::: solution
        Call the generator.
        :::

        ::: exercise
        Build a report.
        :::

        ## Pricing {.internal}

        Internal slide.

        ## Wrap-up

        Done.
        """;

    private static readonly string[] Everyone = ["Everyone sees this", "Exercise 1", "Build a deck", "Exercise 2", "Build a report", "Wrap-up", "Done"];
    private static readonly string[] InternalOnly = ["Team note on pricing", "Internal block", "Pricing", "Internal slide"];
    private static readonly string[] TrainerOnly = ["Solution 1", "Call the generator"];

    public static TheoryData<string, Audience> Cases()
    {
        var data = new TheoryData<string, Audience>();
        foreach (var format in new[] { "pptx", "html", "pdf", "docx" })
        {
            foreach (var audience in Enum.GetValues<Audience>())
            {
                data.Add(format, audience);
            }
        }

        return data;
    }

    private static MarkdownDocument Markdown(string markdown) => new(markdown, [], new Dictionary<string, string>());

    // The text of the generated file, read back by the converters, and the notes.
    private static (string Text, GenerationResult Result) Generate(string format, string markdown, Audience? audience = null)
    {
        using var output = new MemoryStream();
        var document = Markdown(markdown);
        GenerationResult result;
        string text;
        switch (format)
        {
            case "pptx" or "html":
                result = PptxGenerator.Generate(document, output, new PptxOptions { Audience = audience });
                if (format == "html")
                {
                    using var html = new MemoryStream();
                    HtmlDeck.Write(new MemoryStream(output.ToArray()), html);
                    text = System.Net.WebUtility.HtmlDecode(System.Text.Encoding.UTF8.GetString(html.ToArray()));
                }
                else
                {
                    text = new PptxConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown;
                }

                break;
            case "pdf":
                result = PdfGenerator.Generate(document, output, new PdfOptions { Audience = audience });
                text = new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown;
                break;
            default:
                result = DocxGenerator.Generate(document, output, new DocxOptions { Audience = audience });
                text = new DocxConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown;
                break;
        }

        return (text, result);
    }

    private static void AssertVersion(string text, Audience audience)
    {
        Assert.All(Everyone, t => Assert.Contains(t, text, StringComparison.Ordinal));
        foreach (var t in InternalOnly)
        {
            if (audience == Audience.Customer)
            {
                Assert.DoesNotContain(t, text, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(t, text, StringComparison.Ordinal);
            }
        }

        foreach (var t in TrainerOnly)
        {
            if (audience == Audience.Trainer)
            {
                Assert.Contains(t, text, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(t, text, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain("{.internal}", text, StringComparison.Ordinal);
        Assert.DoesNotContain(":::", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EachVersion_ShowsExactlyWhatIsMarkedForIt(string format, Audience audience)
    {
        var (text, result) = Generate(format, Course, audience);

        AssertVersion(text, audience);
        var omitted = result.Notes.Where(n => n.Code == NoteCodes.AudienceOmitted).ToList();
        switch (audience)
        {
            case Audience.Customer:
                var note = Assert.Single(omitted);
                Assert.Equal(9, note.Line);
                Assert.Contains("leaves out 4 parts", note.Message, StringComparison.Ordinal);
                Assert.Contains("lines 9, 13, 21 and 29", note.Message, StringComparison.Ordinal);
                break;
            case Audience.Internal:
                Assert.Equal(21, Assert.Single(omitted).Line);
                break;
            default:
                Assert.Empty(omitted);
                break;
        }
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void FrontMatter_PicksTheVersion_AndTheOptionsWin(string format)
    {
        var trainer = "---\naudience: trainer\n---\n" + Course;

        AssertVersion(Generate(format, trainer).Text, Audience.Trainer);
        AssertVersion(Generate(format, trainer, Audience.Customer).Text, Audience.Customer);
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void AnUnknownVersion_IsAnError(string format)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(format, "---\naudience: partner\n---\n# A"));

        Assert.Contains("'audience' has the value 'partner'; expected 'customer' or 'internal' or 'trainer'", error.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void ADocumentWithoutTheSyntax_IsTheSameInEveryVersion(string format)
    {
        const string plain = "---\nclassification: internal\n---\n# Plain\n\n## One\n\nText {.frag}\n\n- a\n- b\n\n::: panel\nBoxed.\n:::\n";

        var customer = Generate(format, plain, Audience.Customer);
        var trainer = Generate(format, plain, Audience.Trainer);

        // DocxConverter writes the version a Word document was generated as back into its front matter (E08-S02).
        Assert.Equal(customer.Text, trainer.Text.Replace("audience: trainer\n", string.Empty, StringComparison.Ordinal));
        Assert.Equal(customer.Result.Notes, trainer.Result.Notes);
        Assert.DoesNotContain(customer.Result.Notes, n => n.Code == NoteCodes.AudienceOmitted);
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void Exercises_AreNumberedInTheDocumentLanguage(string format)
    {
        var text = Generate(format, "---\nlang: de\n---\n# Kurs\n\n## Teil\n\n::: exercise\nAufgabe.\n:::\n\n::: solution\nAntwort.\n:::\n", Audience.Trainer).Text;

        Assert.Contains("Übung 1", text, StringComparison.Ordinal);
        Assert.Contains("Lösung 1", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pptx", "# A\n\n## B\n\n### Deep {.internal}\n\nText.\n", "'{.internal}' on line 5 is on a '###' heading")]
    [InlineData("pdf", "# A\n\n## B\n\n### Deep {.internal}\n\nText.\n", "'{.internal}' on line 5 is on a '###' heading")]
    [InlineData("docx", "# A\n\n## B\n\n### Deep {.internal}\n\nText.\n", "'{.internal}' on line 5 is on a '###' heading")]
    [InlineData("pptx", "---\ntitle: T\n---\n# A\n\n::: solution\nNo exercise.\n:::\n", "The '::: solution' on line 6 does not follow a '::: exercise'")]
    [InlineData("pdf", "# A\n\nText.\n\n::: solution\nNo exercise.\n:::\n", "The '::: solution' on line 5 does not follow a '::: exercise'")]
    [InlineData("docx", "# A\n\n::: exercise\nE.\n:::\n\nBetween.\n\n::: solution\nS.\n:::\n", "The '::: solution' on line 9 does not follow a '::: exercise'")]
    [InlineData("pptx", "# A\n\n## B\n\n{.internal}\n\n## C\n", "The marker '{.internal}' on line 5 is followed by a heading")]
    [InlineData("pdf", "# A\n\nText.\n\n{.internal}\n", "The marker '{.internal}' on line 5 is followed by nothing to mark")]
    [InlineData("docx", "# A\n\n- item\n\n  {.internal}\n\n  more\n", "The marker '{.internal}' on line 5 is inside a list")]
    [InlineData("pptx", "# A\n\n::: exercise One\nE.\n:::\n", "'::: exercise One' on line 3 takes no arguments")]
    public void MisplacedMarkers_AreErrorsWithTheLine(string format, string markdown, string expected)
    {
        // In every version: a mistake does not depend on which one is generated.
        foreach (var audience in Enum.GetValues<Audience>())
        {
            var error = Assert.Throws<DocumentConversionException>(() => Generate(format, markdown, audience));

            Assert.Contains(expected, error.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMarkerWithABuild_KeepsTheBuild()
    {
        const string deck = "# A\n\n## B\n\n{.internal .frag}\n\nSecret.\n\n{.internal}\n\n{.build}\n\n- one\n- two\n\nOpen.\n";

        var customer = Generate("pptx", deck, Audience.Customer).Text;
        var team = Generate("pptx", deck, Audience.Internal).Text;

        Assert.DoesNotContain("Secret", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("one", customer, StringComparison.Ordinal);
        Assert.Contains("Open", customer, StringComparison.Ordinal);
        Assert.Contains("Secret", team, StringComparison.Ordinal);
        Assert.Contains("two", team, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInternalChapter_TakesItsSlidesWithIt_AndTheNextChapterFollows()
    {
        const string deck = "# Cover\n\n# Internal part {.internal}\n\n## Inside\n\nHidden.\n\n# Open part\n\n## Outside\n\nShown.\n";

        var customer = Generate("pptx", deck, Audience.Customer);
        var team = Generate("pptx", deck, Audience.Internal);

        Assert.Equal(3, customer.Result.Pages);
        Assert.Equal(5, team.Result.Pages);
        Assert.DoesNotContain("Inside", customer.Text, StringComparison.Ordinal);
        Assert.Contains("Internal part", team.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", team.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Marking_OfAVersionOtherThanTheCustomers_IsInternal_UnlessSet()
    {
        static Marking Resolve(string frontMatter, Classification host, Audience audience) =>
            Marking.Resolve(FrontMatter.Read(frontMatter + "# A", DocumentFormat.Pdf), host, false, new GenerationNotes(), DocumentFormat.Pdf, audience);

        Assert.Equal(Classification.Public, Resolve(string.Empty, Classification.Public, Audience.Customer).Classification);
        Assert.Equal(Classification.Internal, Resolve(string.Empty, Classification.Public, Audience.Internal).Classification);
        Assert.Equal(Classification.Internal, Resolve(string.Empty, Classification.Public, Audience.Trainer).Classification);
        Assert.Equal(Classification.Public, Resolve("---\nclassification: public\n---\n", Classification.Public, Audience.Trainer).Classification);
        Assert.Equal(Classification.Confidential, Resolve("---\nclassification: confidential\n---\n", Classification.Public, Audience.Internal).Classification);
        Assert.Equal(Classification.Confidential, Resolve(string.Empty, Classification.Confidential, Audience.Internal).Classification);
    }

    [Theory]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void TheInternalVersion_IsMarkedInternal(string format)
    {
        Assert.DoesNotContain("Internal\n", Generate(format, "# A\n\n## B\n\nText.\n", Audience.Customer).Text + "\n", StringComparison.Ordinal);

        using var output = new MemoryStream();
        var document = Markdown("# A\n\n## B\n\nText.\n");
        var marking = format switch
        {
            "pptx" => Check(PptxGenerator.Generate(document, output, new PptxOptions { Audience = Audience.Internal }), output, "ppt/slides/slide1.xml"),
            "pdf" => Check(PdfGenerator.Generate(document, output, new PdfOptions { Audience = Audience.Internal }), output, null),
            _ => Check(DocxGenerator.Generate(document, output, new DocxOptions { Audience = Audience.Internal }), output, "docProps/core.xml"),
        };

        Assert.Contains("Internal", marking, StringComparison.Ordinal);
    }

    // The part of the package that holds the marking, or for a PDF its text.
    private static string Check(GenerationResult result, MemoryStream output, string? part)
    {
        Assert.NotNull(result);
        if (part is null)
        {
            return new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown;
        }

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(output.ToArray()));
        using var reader = new StreamReader(zip.GetEntry(part)!.Open());
        return reader.ReadToEnd();
    }

    [Theory]
    [InlineData("# A\n\nText.\n", new[] { Audience.Customer })]
    [InlineData("# A\n\n::: exercise\nE.\n:::\n", new[] { Audience.Customer })]
    [InlineData("# A\n\n## B {.internal}\n\nText.\n", new[] { Audience.Customer, Audience.Internal })]
    [InlineData("# A\n\n{.internal}\n\nText.\n", new[] { Audience.Customer, Audience.Internal })]
    [InlineData("# A\n\n::: exercise\nE.\n:::\n\n::: solution\nS.\n:::\n", new[] { Audience.Customer, Audience.Trainer })]
    [InlineData("---\ntitle: T\n---\n::: internal\nI.\n:::\n\n::: exercise\nE.\n:::\n\n::: solution\nS.\n:::\n", new[] { Audience.Customer, Audience.Internal, Audience.Trainer })]
    [InlineData("```markdown\n::: internal\n:::\n```\n", new[] { Audience.Customer })]
    public void DistinctVersions_NameTheVersionsWhoseContentDiffers(string markdown, Audience[] expected)
    {
        Assert.Equal(expected, AudienceVersions.Distinct(Markdown(markdown)));
    }

    [Fact]
    public void DistinctVersions_RejectMisplacedMarkers()
    {
        var error = Assert.Throws<DocumentConversionException>(() => AudienceVersions.Distinct(Markdown("# A\n\n::: solution\nS.\n:::\n")));

        Assert.Contains("line 3", error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTool_TakesTheVersion()
    {
        var tool = new DocumentTool([PdfGenerator.ForOptions()]);
        var markdown = JsonSerializer.Serialize(Course);

        using var trainer = new MemoryStream();
        Assert.True(tool.Run($"{{\"markdown\":{markdown},\"audience\":\"trainer\"}}", [], trainer).Succeeded);
        using var customer = new MemoryStream();
        Assert.True(tool.Run($"{{\"markdown\":{markdown}}}", [], customer).Succeeded);
        var wrong = tool.Run($"{{\"markdown\":{markdown},\"audience\":\"partner\"}}", [], new MemoryStream());

        AssertVersion(new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(trainer.ToArray()), ConversionOptions.Default).Markdown, Audience.Trainer);
        AssertVersion(new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(customer.ToArray()), ConversionOptions.Default).Markdown, Audience.Customer);
        Assert.False(wrong.Succeeded);
        Assert.Contains("Unknown audience 'partner'", wrong.Reply, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTool_DoesNotOverrideTheHostsVersion()
    {
        var tool = new DocumentTool([PdfGenerator.ForOptions(new PdfOptions { Audience = Audience.Customer })]);
        using var output = new MemoryStream();

        Assert.True(tool.Run($"{{\"markdown\":{JsonSerializer.Serialize(Course)},\"audience\":\"trainer\"}}", [], output).Succeeded);

        AssertVersion(new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(output.ToArray()), ConversionOptions.Default).Markdown, Audience.Customer);
    }
}
