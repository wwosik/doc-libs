extern alias OpenXml;

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenXml::DocumentFormat.OpenXml;
using OpenXml::DocumentFormat.OpenXml.Packaging;
using OpenXml::DocumentFormat.OpenXml.Validation;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Docx;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The tool for AI agents: name, schema, combined guide, and the replies of <see cref="DocumentTool.Run"/>.</summary>
public class DocumentToolTests
{
    private const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    private static readonly TimeProvider FixedTime = new Fixed(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    private static readonly PptxOptions Pptx = new() { TimeProvider = FixedTime };
    private static readonly PdfOptions Pdf = new() { TimeProvider = FixedTime };
    private static readonly DocxOptions Docx = new() { TimeProvider = FixedTime };

    private static DocumentTool Tool(DocumentToolOptions? options = null) =>
        new([PptxGenerator.ForOptions(Pptx), PdfGenerator.ForOptions(Pdf)], options);

    // ---- The generators -------------------------------------------------------------------------------------------

    [Fact]
    public void ForOptions_DescribesEachFormat()
    {
        IMarkdownGenerator[] generators = [PptxGenerator.ForOptions(), PdfGenerator.ForOptions(), DocxGenerator.ForOptions()];

        Assert.Equal(["pptx", "pdf", "docx"], generators.Select(g => g.Format));
        Assert.Equal(["PowerPoint deck", "PDF document", "Word document"], generators.Select(g => g.Title));
        Assert.Equal([".pptx", ".pdf", ".docx"], generators.Select(g => g.FileExtension));
        Assert.Equal(
            [
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ],
            generators.Select(g => g.ContentType));
    }

    [Fact]
    public void ForOptions_UsesItsOptions()
    {
        var options = new PdfOptions { Checks = CheckOptions.Default with { MaxTitleLength = 5 } };
        var generator = PdfGenerator.ForOptions(options);

        Assert.Equal(PdfGenerator.AuthoringGuide(options), generator.AuthoringGuide());
        Assert.Equal(PdfGenerator.Check(Document("# A long title\n"), options), generator.Check(Document("# A long title\n")));
        Assert.Equal(PptxGenerator.AuthoringGuide(), PptxGenerator.ForOptions().AuthoringGuide());
        Assert.Equal(DocxGenerator.AuthoringGuide(Docx), DocxGenerator.ForOptions(Docx).AuthoringGuide());
    }

    // ---- Construction ---------------------------------------------------------------------------------------------

    [Fact]
    public void Defaults()
    {
        var tool = Tool();

        Assert.Equal("create_document", tool.Name);
        Assert.Equal(2, tool.Generators.Count);
        Assert.Contains("a PowerPoint deck (format \"pptx\") or a PDF document (format \"pdf\")", tool.Description, StringComparison.Ordinal);
        Assert.Contains("mode \"check\"", tool.Description, StringComparison.Ordinal);
        Assert.Contains("'Writing documents with create_document'", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void NameAndDescription_CanBeChanged()
    {
        var tool = Tool(new DocumentToolOptions { Name = "write-report_2", Description = "Writes the report." });

        Assert.Equal("write-report_2", tool.Name);
        Assert.Equal("Writes the report.", tool.Description);
        Assert.StartsWith("# Writing documents with write-report_2\n", tool.Guide, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("create document")]
    [InlineData("créer")]
    [InlineData("a.b")]
    public void AnInvalidName_Throws(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => Tool(new DocumentToolOptions { Name = name }));
        Assert.Contains("1 to 64 letters", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameOf64_IsAccepted_And65_IsNot()
    {
        Assert.Equal(64, Tool(new DocumentToolOptions { Name = new string('a', 64) }).Name.Length);
        Assert.Throws<ArgumentException>(() => Tool(new DocumentToolOptions { Name = new string('a', 65) }));
    }

    [Fact]
    public void BadGenerators_Throw()
    {
        Assert.Throws<ArgumentException>(() => new DocumentTool([]));
        Assert.Throws<ArgumentException>(() => new DocumentTool([PdfGenerator.ForOptions(), null!]));
        var twice = Assert.Throws<ArgumentException>(() => new DocumentTool([PdfGenerator.ForOptions(), PdfGenerator.ForOptions(Pdf)]));
        Assert.Contains("'pdf'", twice.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Tool(new DocumentToolOptions { Description = " " }));
    }

    // ---- Schema ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Schema_MatchesExpected()
    {
        AssertMatchesExpected("tool-schema.expected.json", Tool().ParametersSchema);
    }

    [Fact]
    public void Schema_IsValidJson_WithTheOfferedFormats()
    {
        var tool = new DocumentTool([PptxGenerator.ForOptions(), PdfGenerator.ForOptions(), DocxGenerator.ForOptions()]);
        using var schema = JsonDocument.Parse(tool.ParametersSchema);
        var root = schema.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        var properties = root.GetProperty("properties");
        Assert.Equal(["format", "markdown", "audience", "mode"], properties.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["pptx", "pdf", "docx"], properties.GetProperty("format").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["generate", "check"], properties.GetProperty("mode").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["format", "markdown"], root.GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        // Only the keywords every major tool API accepts.
        string[] allowed = ["type", "properties", "enum", "description", "required", "default", "additionalProperties"];
        Assert.All(Keywords(root), k => Assert.Contains(k, allowed));
        Assert.All(properties.EnumerateObject(), p => Assert.False(string.IsNullOrWhiteSpace(p.Value.GetProperty("description").GetString())));
    }

    [Fact]
    public void Schema_WithOneFormat_MakesFormatOptional()
    {
        using var schema = JsonDocument.Parse(new DocumentTool([PdfGenerator.ForOptions()]).ParametersSchema);
        var format = schema.RootElement.GetProperty("properties").GetProperty("format");

        Assert.Equal(["markdown"], schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["pdf"], format.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("pdf", format.GetProperty("default").GetString());
    }

    [Fact]
    public void Schema_WithoutCheck_HasNoMode()
    {
        var tool = Tool(new DocumentToolOptions { OfferCheck = false });
        using var schema = JsonDocument.Parse(tool.ParametersSchema);

        Assert.False(schema.RootElement.GetProperty("properties").TryGetProperty("mode", out _));
        Assert.DoesNotContain("check", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mode\"", tool.Guide, StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\": \"checked\"", tool.Guide, StringComparison.Ordinal);
    }

    // ---- Guide ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Guide_MatchesExpected()
    {
        AssertMatchesExpected("tool-guide.expected.md", Tool().Guide);
    }

    [Fact]
    public void Guide_HasOneTopHeading_AndEveryFormatsGuideMovedDown()
    {
        var tool = new DocumentTool([PptxGenerator.ForOptions(Pptx), PdfGenerator.ForOptions(Pdf), DocxGenerator.ForOptions(Docx)]);
        var guide = tool.Guide;

        Assert.Single(Headings(guide), h => h.Level == 1);
        Assert.Equal("# Writing documents with create_document", guide.Split('\n')[0]);
        Assert.Equal(
            ["Calling the tool", "What comes back", "PowerPoint deck (format: \"pptx\")", "PDF document (format: \"pdf\")", "Word document (format: \"docx\")"],
            Headings(guide).Where(h => h.Level == 2).Select(h => h.Text));
        foreach (var generator in tool.Generators)
        {
            var own = generator.AuthoringGuide();
            var moved = DocumentTool.HeadingsDown(own);
            Assert.Contains(moved.TrimEnd('\n'), guide, StringComparison.Ordinal);
            Assert.Equal(Headings(own).Select(h => (h.Level + 1, h.Text)), Headings(moved).Select(h => (h.Level, h.Text)));
        }
    }

    [Fact]
    public void Guide_WithOneFormat_SaysFormatMayBeLeftOut()
    {
        var guide = new DocumentTool([DocxGenerator.ForOptions(Docx)]).Guide;
        Assert.Contains("**`format`** may be left out: the only format is \"docx\".", guide, StringComparison.Ordinal);
        Assert.Contains("\n## Word document (format: \"docx\")\n\n### ", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadingsDown_LeavesCodeAlone()
    {
        const string markdown = "## One\n\n### Two\n\n````markdown\n# Inside\n```\n## Still inside\n```\n````\n\n~~~\n# Tilde\n~~~\n\n"
            + "    # Indented code\n\n#Not a heading\n\n   ###### Six\n\n#####\n";
        const string expected = "### One\n\n#### Two\n\n````markdown\n# Inside\n```\n## Still inside\n```\n````\n\n~~~\n# Tilde\n~~~\n\n"
            + "    # Indented code\n\n#Not a heading\n\n   ###### Six\n\n######\n";

        Assert.Equal(expected, DocumentTool.HeadingsDown(markdown));
    }

    // ---- Run: generate and check ----------------------------------------------------------------------------------

    [Fact]
    public void Run_GeneratesADeck_LikeGenerate()
    {
        var document = Deck();
        using var output = new MemoryStream();
        var run = Tool().Run(Arguments("pptx", document.Markdown), document.Resources, output);

        using var expected = new MemoryStream();
        var result = PptxGenerator.Generate(document, expected, Pptx);
        Assert.True(run.Succeeded);
        Assert.Equal("pptx", run.Generator!.Format);
        Assert.Equal(result, run.Result);
        Assert.Null(run.Check);
        Assert.Equal(Parts(expected.ToArray()), Parts(output.ToArray()));
        AssertValidPptx(output.ToArray());
        AssertMatchesExpected("tool-reply-deck.expected.json", run.Reply);
    }

    [Fact]
    public void Run_GeneratesAPdf_LikeGenerate()
    {
        var document = Report();
        using var output = new MemoryStream();
        var run = Tool().Run(Arguments("pdf", document.Markdown), document.Resources, output);

        using var expected = new MemoryStream();
        var result = PdfGenerator.Generate(document, expected, Pdf);
        Assert.True(run.Succeeded);
        Assert.Equal(result, run.Result);
        Assert.Equal(WithoutId(expected.ToArray()), WithoutId(output.ToArray()));
        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(output.ToArray()), StringComparison.Ordinal);
        AssertMatchesExpected("tool-reply-report.expected.json", run.Reply);
    }

    [Fact]
    public void Run_GeneratesAWordDocument_LikeGenerate()
    {
        var document = Report();
        var tool = new DocumentTool([DocxGenerator.ForOptions(Docx)]);
        using var output = new MemoryStream();
        var run = tool.Run(Arguments(null, document.Markdown), document.Resources, output);

        using var expected = new MemoryStream();
        var result = DocxGenerator.Generate(document, expected, Docx);
        Assert.True(run.Succeeded);
        Assert.Equal(result, run.Result);
        Assert.Equal(Parts(expected.ToArray()), Parts(output.ToArray()));
        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal("docx", reply.RootElement.GetProperty("format").GetString());
        Assert.Equal(0, reply.RootElement.GetProperty("pages").GetInt32());
    }

    [Fact]
    public void Run_Checks_AndWritesNothing()
    {
        var document = Deck();
        using var output = new MemoryStream();
        var run = Tool().Run(Arguments("pptx", document.Markdown, "check"), document.Resources, output);

        Assert.Equal(0, output.Length);
        Assert.False(run.Succeeded);
        Assert.Null(run.Result);
        Assert.Equal(PptxGenerator.Check(document, Pptx), run.Check);
        AssertMatchesExpected("tool-reply-deck-check.expected.json", run.Reply);

        using var reply = JsonDocument.Parse(run.Reply);
        var statistics = reply.RootElement.GetProperty("statistics");
        Assert.Equal(run.Check!.Statistics.SlidesByKind.Keys, statistics.GetProperty("slidesByKind").EnumerateObject().Select(p => p.Name));
        Assert.Equal((int)run.Check.Statistics.TalkTime!.Value.TotalMinutes, statistics.GetProperty("talkTimeMinutes").GetInt32());
    }

    [Fact]
    public void Run_ChecksAPdf_WithoutDeckStatistics()
    {
        var document = Report();
        using var output = new MemoryStream();
        var run = Tool().Run(Arguments("pdf", document.Markdown, "check"), document.Resources, output);

        Assert.Equal(0, output.Length);
        AssertMatchesExpected("tool-reply-report-check.expected.json", run.Reply);
        using var reply = JsonDocument.Parse(run.Reply);
        var statistics = reply.RootElement.GetProperty("statistics");
        Assert.False(statistics.TryGetProperty("slidesByKind", out _));
        Assert.False(statistics.TryGetProperty("talkTimeMinutes", out _));
        Assert.False(reply.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void Run_CheckWithADocumentError_StaysChecked_WithTheError()
    {
        var run = Tool().Run(Arguments("pdf", "---\ncolour: red\n---\n# Plan\n\nText.\n", "check"), [], Stream.Null);

        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal(["status", "format", "error", "pages", "notes", "statistics"], reply.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("checked", reply.RootElement.GetProperty("status").GetString());
        Assert.Equal(run.Check!.Error, reply.RootElement.GetProperty("error").GetString());
        Assert.Contains("colour", run.Check.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ADocumentError_IsAnErrorReply()
    {
        using var output = new MemoryStream();
        var run = Tool().Run(Arguments("pdf", "# Plan\n\n![Chart](chart.png)\n"), [], output);

        Assert.False(run.Succeeded);
        Assert.Equal("pdf", run.Generator!.Format);
        Assert.Null(run.Result);
        var error = Assert.Throws<DocumentConversionException>(() => PdfGenerator.Generate(Document("# Plan\n\n![Chart](chart.png)\n"), Stream.Null, Pdf));
        Assert.Equal(Error(error.Reason), run.Reply);
    }

    [Fact]
    public void Run_HostErrors_Throw()
    {
        var bad = new DocumentTool([PdfGenerator.ForOptions(new PdfOptions { Checks = CheckOptions.Default with { WordsPerMinute = 0 } })]);

        Assert.Throws<ArgumentException>(() => bad.Run(Arguments(null, "# Plan\n", "check"), [], Stream.Null));
        Assert.Throws<ArgumentNullException>(() => Tool().Run(null!, [], Stream.Null));
    }

    [Fact]
    public void Run_TakesCarriageReturns_AndNulls_AndModeGenerate()
    {
        var document = Report();
        using var expected = new MemoryStream();
        PdfGenerator.Generate(document, expected, Pdf);

        using var output = new MemoryStream();
        var arguments = "{\"format\":\"PDF\",\"markdown\":" + Json(document.Markdown.Replace("\n", "\r\n", StringComparison.Ordinal))
            + ",\"mode\":\"generate\"}";
        var run = Tool().Run(arguments, document.Resources, output);

        Assert.True(run.Succeeded, run.Reply);
        Assert.Equal(WithoutId(expected.ToArray()), WithoutId(output.ToArray()));
        Assert.True(new DocumentTool([PdfGenerator.ForOptions(Pdf)]).Run("{\"format\":null,\"markdown\":\"# Plan\\n\",\"mode\":null}", [], Stream.Null).Succeeded);
    }

    // ---- Run: contact sheet ----------------------------------------------------------------------------------------

    [Fact]
    public void Run_WithAPreview_AttachesAContactSheet_ToADeck()
    {
        var document = Deck();
        var preview = new FakePreview();
        using var output = new MemoryStream();
        var run = Tool(new DocumentToolOptions { Preview = preview }).Run(Arguments("pptx", document.Markdown), document.Resources, output);

        using var plain = new MemoryStream();
        var without = Tool().Run(Arguments("pptx", document.Markdown), document.Resources, plain);
        Assert.True(run.Succeeded);
        Assert.Equal(FakePreview.Png, run.ContactSheet);
        Assert.Equal(output.ToArray(), preview.Deck);
        Assert.Same(run.Result, preview.Result);
        Assert.Equal(Parts(plain.ToArray()), Parts(output.ToArray()));
        Assert.Null(without.ContactSheet);

        using var reply = JsonDocument.Parse(run.Reply);
        using var plainReply = JsonDocument.Parse(without.Reply);
        Assert.Equal([.. plainReply.RootElement.EnumerateObject().Select(p => p.Name), "contactSheet"], reply.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("attached", reply.RootElement.GetProperty("contactSheet").GetString());
    }

    [Fact]
    public void Run_WithAPreview_GivesNoSheet_ForPdfs_WordDocuments_OrChecks()
    {
        var document = Report();
        var preview = new FakePreview();
        var options = new DocumentToolOptions { Preview = preview };
        var pdf = Tool(options).Run(Arguments("pdf", document.Markdown), document.Resources, Stream.Null);
        var docx = new DocumentTool([DocxGenerator.ForOptions(Docx)], options).Run(Arguments(null, document.Markdown), document.Resources, Stream.Null);
        var check = Tool(options).Run(Arguments("pptx", Deck().Markdown, "check"), Deck().Resources, Stream.Null);

        Assert.Equal(Tool().Run(Arguments("pdf", document.Markdown), document.Resources, Stream.Null).Reply, pdf.Reply);
        Assert.Null(pdf.ContactSheet);
        Assert.Null(docx.ContactSheet);
        Assert.Null(check.ContactSheet);
        Assert.DoesNotContain("contactSheet", docx.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain("contactSheet", check.Reply, StringComparison.Ordinal);
        Assert.Equal(0, preview.Calls);
    }

    [Fact]
    public void Run_WithAPreview_NotesALongDeck()
    {
        var markdown = string.Concat(Enumerable.Range(1, IDeckPreview.MaxSlides + 2).Select(i => $"## Slide {i}\n\nText.\n\n"));
        var run = Tool(new DocumentToolOptions { Preview = new FakePreview() }).Run(Arguments("pptx", markdown), [], Stream.Null);

        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal(IDeckPreview.MaxSlides + 2, run.Result!.Pages);
        Assert.Equal("The contact sheet shows slides 1 to 60 of 62.", reply.RootElement.GetProperty("contactSheetNote").GetString());
    }

    [Fact]
    public void Run_WithAPreviewThatCannotPaint_RepliesWithoutASheet()
    {
        var document = Deck();
        using var output = new MemoryStream();
        var run = Tool(new DocumentToolOptions { Preview = new FakePreview { Fail = true } }).Run(Arguments("pptx", document.Markdown), document.Resources, output);

        Assert.True(run.Succeeded);
        Assert.Null(run.ContactSheet);
        Assert.True(output.Length > 0);
        Assert.Equal(Tool().Run(Arguments("pptx", document.Markdown), document.Resources, Stream.Null).Reply, run.Reply);
    }

    [Fact]
    public void Guide_WithAPreview_HasLookingAtTheSlides()
    {
        var guide = Tool(new DocumentToolOptions { Preview = new FakePreview() }).Guide;

        Assert.Contains("\n## Looking at the slides\n", guide, StringComparison.Ordinal);
        Assert.Contains("\"contactSheet\": \"attached\"", guide, StringComparison.Ordinal);
        Assert.Equal(Tool().Guide, guide.Remove(guide.IndexOf("\n## Looking at the slides", StringComparison.Ordinal), guide.IndexOf("\n## PowerPoint deck", StringComparison.Ordinal) - guide.IndexOf("\n## Looking at the slides", StringComparison.Ordinal)));
        Assert.DoesNotContain("Looking at the slides", Tool().Guide, StringComparison.Ordinal);
        Assert.DoesNotContain("Looking at the slides", new DocumentTool([PdfGenerator.ForOptions(Pdf)], new DocumentToolOptions { Preview = new FakePreview() }).Guide, StringComparison.Ordinal);
    }

    private sealed class FakePreview : IDeckPreview
    {
        public static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G'];

        public bool Fail { get; init; }

        public int Calls { get; private set; }

        public byte[]? Deck { get; private set; }

        public GenerationResult? Result { get; private set; }

        public byte[]? ContactSheet(ReadOnlyMemory<byte> pptx, GenerationResult result)
        {
            this.Calls++;
            this.Deck = pptx.ToArray();
            this.Result = result;
            return this.Fail ? throw new DocumentConversionException(DocumentFormat.Svg, "Slide 1 cannot be painted.") : Png;
        }
    }

    // ---- Run: argument errors -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "The arguments are not valid JSON")]
    [InlineData("{\"format\": \"pdf\",", "The arguments are not valid JSON")]
    [InlineData("[\"pdf\"]", "The arguments must be a JSON object with the properties 'format', 'markdown', 'audience' (optional) and 'mode' (optional), not an array.")]
    [InlineData("{\"format\":\"pdf\",\"markdown\":\"# A\",\"title\":\"A\"}", "Unknown property 'title'. The tool takes the properties 'format', 'markdown', 'audience' (optional) and 'mode' (optional).")]
    [InlineData("{\"format\":\"pdf\",\"markdown\":1}", "'markdown' must be a string, not a number.")]
    [InlineData("{\"format\":[\"pdf\"],\"markdown\":\"# A\"}", "'format' must be a string, not an array.")]
    [InlineData("{\"format\":\"pdf\",\"markdown\":\"# A\",\"mode\":true}", "'mode' must be a string, not a boolean.")]
    [InlineData("{\"markdown\":\"# A\"}", "'format' is missing: give one of \"pptx\" or \"pdf\".")]
    [InlineData("{\"format\":\"docx\",\"markdown\":\"# A\"}", "Unknown format 'docx': give one of \"pptx\" or \"pdf\".")]
    [InlineData("{\"format\":\"pdf\"}", "'markdown' is missing: put the whole document in it, front matter included.")]
    [InlineData("{\"format\":\"pdf\",\"markdown\":\" \\n\"}", "'markdown' is empty: put the whole document in it, front matter included.")]
    [InlineData("{\"format\":\"pdf\",\"markdown\":\"# A\",\"mode\":\"preview\"}", "Unknown mode 'preview': give \"generate\" or \"check\".")]
    [InlineData("{\"format\":\"pdf\",\"format\":\"pptx\",\"markdown\":\"# A\"}", "The property 'format' is given twice.")]
    public void Run_ArgumentErrors_AreErrorReplies(string arguments, string message)
    {
        using var output = new MemoryStream();
        var run = Tool().Run(arguments, [], output);

        Assert.False(run.Succeeded);
        Assert.Null(run.Generator);
        Assert.Equal(0, output.Length);
        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal(["status", "error"], reply.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("error", reply.RootElement.GetProperty("status").GetString());
        Assert.StartsWith(message, reply.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_WithoutCheck_RefusesMode()
    {
        var tool = Tool(new DocumentToolOptions { OfferCheck = false });
        var run = tool.Run("{\"format\":\"pdf\",\"markdown\":\"# A\",\"mode\":\"check\"}", [], Stream.Null);

        Assert.Equal(Error("Unknown property 'mode'. The tool takes the properties 'format', 'markdown' and 'audience' (optional)."), run.Reply);
    }

    [Fact]
    public void Run_WithOneFormat_NamesIt()
    {
        var tool = new DocumentTool([PdfGenerator.ForOptions(Pdf)]);

        Assert.Equal(Error("Unknown format 'pptx': the only format is \"pdf\"."), tool.Run("{\"format\":\"pptx\",\"markdown\":\"# A\"}", [], Stream.Null).Reply);
        Assert.Equal(
            Error("Unknown property 'x'. The tool takes the properties 'format' (optional), 'markdown', 'audience' (optional) and 'mode' (optional)."),
            tool.Run("{\"x\":1}", [], Stream.Null).Reply);
    }

    [Fact]
    public void Replies_KeepLettersReadable()
    {
        var run = Tool().Run("{\"format\":\"pdf\",\"markdown\":\"# Größe <&> 'x'\\n\\n![Bild](fehlt.png)\\n\"}", [], Stream.Null);

        Assert.Contains("fehlt.png", run.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", run.Reply, StringComparison.Ordinal);
    }

    // ---- Dependencies ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheSharedPackage_ReferencesNoGenerator_AndPdfNoOpenXml()
    {
        var shared = typeof(DocumentTool).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(shared, n => n.StartsWith("wwosik.DocLibs.DocumentGeneration.", StringComparison.Ordinal));
        Assert.DoesNotContain(shared, n => n.StartsWith("DocumentFormat.OpenXml", StringComparison.Ordinal));
        Assert.DoesNotContain(shared, n => n.Contains("Json", StringComparison.Ordinal) && n != "System.Text.Json" && n != "System.Text.Encodings.Web");

        var pdf = typeof(PdfGenerator).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(pdf, n => n.StartsWith("DocumentFormat.OpenXml", StringComparison.Ordinal));
        Assert.DoesNotContain(pdf, n => n.StartsWith("wwosik.DocLibs.DocumentGeneration.", StringComparison.Ordinal));
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private static string Arguments(string? format, string markdown, string? mode = null)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (format is not null)
            {
                writer.WriteString("format", format);
            }

            writer.WriteString("markdown", markdown);
            if (mode is not null)
            {
                writer.WriteString("mode", mode);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Error(string message) =>
        "{\n  \"status\": \"error\",\n  \"error\": " + Json(message) + "\n}";

    // A JSON string as the tool writes it.
    private static string Json(string value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStringValue(value);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static MarkdownDocument Document(string markdown, params DocumentResource[] resources) =>
        new(markdown, resources, new Dictionary<string, string>());

    private static DocumentResource Image(string name) => new(name, "image/png", File.ReadAllBytes(SamplePath(name)));

    private static MarkdownDocument Deck() =>
        Document(File.ReadAllText(SamplePath("deck.md")).ReplaceLineEndings("\n"), Image("diagram.png"), Image("photo.png"));

    private static MarkdownDocument Report() =>
        Document(File.ReadAllText(SamplePath("report.md")).ReplaceLineEndings("\n"), Image("diagram.png"), Image("photo.png"));

    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    private static IEnumerable<string> Keywords(JsonElement schema)
    {
        foreach (var property in schema.EnumerateObject())
        {
            yield return property.Name;
            if (property.Name == "properties")
            {
                foreach (var keyword in property.Value.EnumerateObject().SelectMany(p => Keywords(p.Value)))
                {
                    yield return keyword;
                }
            }
        }
    }

    private static List<(int Level, string Text)> Headings(string markdown)
    {
        Markdig.Syntax.MarkdownDocument parsed = Markdig.Markdown.Parse(markdown);
        return [.. Markdig.Syntax.MarkdownObjectExtensions.Descendants<Markdig.Syntax.HeadingBlock>(parsed).Select(h => (h.Level, markdown.Substring(h.Span.Start, h.Span.Length).TrimStart('#', ' ')))];
    }

    // Every part's content with the SDK's random relationship ids masked; embedded workbooks by name only.
    private static List<string> Parts(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package));
        return [.. archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).Select(e =>
        {
            var name = Regex.Replace(e.FullName, "R[0-9a-f]{16}", "R*");
            if (e.FullName.EndsWith(".xlsx", StringComparison.Ordinal))
            {
                return name;
            }

            using var content = new MemoryStream();
            using (var stream = e.Open())
            {
                stream.CopyTo(content);
            }

            return name + ": " + Regex.Replace(Encoding.Latin1.GetString(content.ToArray()), "R[0-9a-f]{16}", "R*");
        })];
    }

    private static string WithoutId(byte[] pdf) =>
        Regex.Replace(Encoding.Latin1.GetString(pdf), @"/ID \[ <[0-9A-F]+><[0-9A-F]+>\]", "/ID");

    private static void AssertValidPptx(byte[] pptx)
    {
        using var package = PresentationDocument.Open(new MemoryStream(pptx), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.Empty(errors);
    }

    private static void AssertMatchesExpected(string name, string actual, [CallerFilePath] string caller = "")
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(caller)!, "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(SamplePath(name)).ReplaceLineEndings("\n"), actual);
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
