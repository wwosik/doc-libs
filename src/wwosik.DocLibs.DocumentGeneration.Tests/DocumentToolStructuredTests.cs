extern alias OpenXml;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenXml::DocumentFormat.OpenXml;
using OpenXml::DocumentFormat.OpenXml.Packaging;
using OpenXml::DocumentFormat.OpenXml.Validation;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Docx;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;
using wwosik.DocLibs.DocumentGeneration.Xlsx;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The tool with a structured generator: workbooks as Markdown or as data, inline or as an attached file.</summary>
public class DocumentToolStructuredTests
{
    private const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly TimeProvider FixedTime = new Fixed(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    private static readonly XlsxOptions Xlsx = new() { TimeProvider = FixedTime };

    private static DocumentTool AllFour() => new(
    [
        PptxGenerator.ForOptions(new PptxOptions { TimeProvider = FixedTime }),
        PdfGenerator.ForOptions(new PdfOptions { TimeProvider = FixedTime }),
        DocxGenerator.ForOptions(new DocxOptions { TimeProvider = FixedTime }),
        XlsxGenerator.ForOptions(Xlsx),
    ]);

    [Fact]
    public void TheWorkbookGenerator_IsBothKinds()
    {
        var generator = XlsxGenerator.ForOptions(Xlsx);

        Assert.Equal(XlsxType, generator.ContentType);
        var structured = Assert.IsAssignableFrom<IStructuredGenerator>(generator);
        Assert.Equal(("xlsx", "workbook"), (structured.Format, structured.ParameterName));
    }

    // ---- Schema and guide -----------------------------------------------------------------------------------------

    [Fact]
    public void Schema_WithAllFour_MatchesExpected()
    {
        AssertMatchesExpected("tool-schema-xlsx.expected.json", AllFour().ParametersSchema);
    }

    [Fact]
    public void Schema_OffersTheWorkbook_AndItsResource_AndRequiresNoMarkdown()
    {
        using var schema = JsonDocument.Parse(AllFour().ParametersSchema);
        var root = schema.RootElement;
        var properties = root.GetProperty("properties");

        Assert.Equal(["format", "markdown", "workbook", "workbookResource", "audience", "mode"], properties.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["format"], root.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["pptx", "pdf", "docx", "xlsx"], properties.GetProperty("format").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));

        // The workbook's own schema, with the tool's description in front of its own.
        var workbook = JsonNode.Parse(properties.GetProperty("workbook").GetRawText())!.AsObject();
        var own = JsonNode.Parse(XlsxWorkbookJson.Schema)!.AsObject();
        Assert.StartsWith("The Excel workbook as data, instead of 'markdown'; it implies the format \"xlsx\". A workbook for", workbook["description"]!.GetValue<string>(), StringComparison.Ordinal);
        workbook.Remove("description");
        own.Remove("description");
        Assert.True(JsonNode.DeepEquals(own, workbook));
        Assert.Equal("string", properties.GetProperty("workbookResource").GetProperty("type").GetString());
    }

    [Fact]
    public void Schema_WithoutAStructuredGenerator_StaysAsItWas()
    {
        var tool = new DocumentTool([PptxGenerator.ForOptions(), PdfGenerator.ForOptions()]);

        Assert.Equal(File.ReadAllText(SamplePath("tool-schema.expected.json")).ReplaceLineEndings("\n"), tool.ParametersSchema);
        Assert.DoesNotContain("workbook", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("workbook", tool.Guide, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_WithAPdfAndAWorkbook_MatchesExpected()
    {
        var tool = new DocumentTool([PdfGenerator.ForOptions(new PdfOptions { TimeProvider = FixedTime }), XlsxGenerator.ForOptions(Xlsx)]);

        AssertMatchesExpected("tool-guide-xlsx.expected.md", tool.Guide);
        Assert.Contains("### Workbooks as JSON\n", tool.Guide, StringComparison.Ordinal);
        Assert.Contains("`workbookResource`", tool.Guide, StringComparison.Ordinal);
        Assert.Contains("in 'workbook' as JSON instead, or in an attached .json file named by 'workbookResource'.", tool.Description, StringComparison.Ordinal);
    }

    // ---- Run ------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("pptx")]
    [InlineData("pdf")]
    [InlineData("docx")]
    [InlineData("xlsx")]
    public void Markdown_WorksForAllFour(string format)
    {
        var markdown = format == "xlsx" ? File.ReadAllText(SamplePath("workbook.md")).ReplaceLineEndings("\n") : "# Title\n\nA paragraph.\n";
        using var output = new MemoryStream();
        var run = AllFour().Run(Arguments(("format", format), ("markdown", markdown)), [], output);

        Assert.True(run.Succeeded, run.Reply);
        Assert.Equal(format, run.Generator!.Format);
        Assert.True(output.Length > 0);
    }

    [Fact]
    public void AWorkbookInline_GivesTheSameBytesAsItsMarkdown()
    {
        using var output = new MemoryStream();
        var run = AllFour().Run("{\"workbook\": " + WorkbookJson() + "}", [], output);

        Assert.True(run.Succeeded, run.Reply);
        Assert.Equal(XlsxType, run.Generator!.ContentType);
        Assert.Equal(5, run.Result!.Pages);
        Assert.Equal(Markdown(), output.ToArray());
        AssertValidXlsx(output.ToArray());
        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal(["status", "format", "pages", "notes"], reply.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("xlsx", reply.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public void AWorkbookAsAnAttachedFile_WorksLikeInline_WithItsFormat()
    {
        var file = new DocumentResource("data/figures.json", "application/json", Encoding.UTF8.GetBytes("﻿" + WorkbookJson()));
        using var output = new MemoryStream();
        var run = AllFour().Run("{\"format\": \"xlsx\", \"workbookResource\": \"data/figures.json\"}", [file], output);

        Assert.True(run.Succeeded, run.Reply);
        Assert.Equal(Markdown(), output.ToArray());
    }

    [Fact]
    public void Check_GivesTheProblemsAsNotes_WithTheirPaths()
    {
        const string workbook = "{\"sheets\": [{\"name\": \"A\", \"columns\": [{\"header\": \"N\", \"type\": \"number\", \"colour\": 1}], \"rows\": [[\"x\"]]}]}";
        using var output = new MemoryStream();
        var shape = AllFour().Run("{\"workbook\": " + workbook + ", \"mode\": \"check\"}", [], output);
        var model = AllFour().Run("{\"workbook\": " + workbook.Replace(", \"colour\": 1", string.Empty, StringComparison.Ordinal) + ", \"mode\": \"check\"}", [], output);

        Assert.Equal(0, output.Length);
        using var first = JsonDocument.Parse(shape.Reply);
        Assert.Equal("checked", first.RootElement.GetProperty("status").GetString());
        var note = Assert.Single(first.RootElement.GetProperty("notes").EnumerateArray());
        Assert.Equal("json-invalid", note.GetProperty("code").GetString());
        Assert.Equal(0, note.GetProperty("line").GetInt32());
        Assert.StartsWith("$.sheets[0].columns[0].colour: Unknown property 'colour'.", note.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("The workbook has a problem: $.sheets[0].columns[0].colour: ", first.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);

        using var second = JsonDocument.Parse(model.Reply);
        var cell = Assert.Single(second.RootElement.GetProperty("notes").EnumerateArray());
        Assert.Equal("cell-type", cell.GetProperty("code").GetString());
        Assert.StartsWith("$.sheets[0].rows[0][0]: ", cell.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, second.RootElement.GetProperty("statistics").GetProperty("tables").GetInt32());
    }

    [Fact]
    public void AWorkbookWithErrors_IsAnErrorReply_WithThePaths()
    {
        using var output = new MemoryStream();
        var run = AllFour().Run("{\"workbook\": {\"sheets\": [{\"name\": \"A\", \"columns\": []}]}}", [], output);

        Assert.False(run.Succeeded);
        Assert.Equal("xlsx", run.Generator!.Format);
        using var reply = JsonDocument.Parse(run.Reply);
        Assert.StartsWith("The workbook has a problem: $.sheets[0].columns: ", reply.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"format\": \"xlsx\", \"markdown\": \"# A\", \"workbook\": {\"sheets\": []}}", "Give either 'markdown' or 'workbook', not both.")]
    [InlineData("{\"workbook\": {\"sheets\": []}, \"workbookResource\": \"a.json\"}", "Give only one of 'workbook' and 'workbookResource'.")]
    [InlineData("{\"format\": \"pdf\", \"workbook\": {\"sheets\": []}}", "'workbook' is for the format \"xlsx\", but 'format' is \"pdf\". Leave 'format' out, or give \"xlsx\".")]
    [InlineData("{\"workbook\": \"{}\"}", "'workbook' must be an object, not a string.")]
    [InlineData("{\"workbookResource\": 1}", "'workbookResource' must be a string, not a number.")]
    [InlineData("{\"workbookResource\": \"a.json\"}", "'workbookResource' names the file 'a.json', which is not attached; the attached files are 'bad.json'.")]
    [InlineData("{\"workbookResource\": \"bad.json\"}", "The file 'bad.json' that 'workbookResource' names is not valid JSON")]
    [InlineData("{\"format\": \"xlsx\"}", "'markdown' is missing: put the whole document in it, front matter included, or for the format \"xlsx\" the data in 'workbook' or 'workbookResource'.")]
    [InlineData("{\"deck\": {}}", "Unknown property 'deck'. The tool takes the properties 'format', 'markdown', 'workbook' (optional), 'workbookResource' (optional), 'audience' (optional) and 'mode' (optional).")]
    public void CallErrors_AreErrorReplies(string arguments, string message)
    {
        var bad = new DocumentResource("bad.json", "application/json", Encoding.UTF8.GetBytes("{\"sheets\": ["));
        var run = AllFour().Run(arguments, [bad], Stream.Null);

        using var reply = JsonDocument.Parse(run.Reply);
        Assert.Equal("error", reply.RootElement.GetProperty("status").GetString());
        Assert.StartsWith(message, reply.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AWorkbook_WithItsOwnFormat_OrANull_IsFine()
    {
        var run = AllFour().Run("{\"format\": \"XLSX\", \"markdown\": null, \"workbook\": " + WorkbookJson() + ", \"workbookResource\": null}", [], new MemoryStream());

        Assert.True(run.Succeeded, run.Reply);
    }

    [Fact]
    public void ABadStructuredGenerator_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DocumentTool([new Fake("xlsx", "pdf", "workbook", "{}")]));
        Assert.Throws<ArgumentException>(() => new DocumentTool([new Fake("xlsx", "xlsx", "Workbook", "{}")]));
        Assert.Throws<ArgumentException>(() => new DocumentTool([new Fake("xlsx", "xlsx", "markdown", "{}")]));
        Assert.Throws<ArgumentException>(() => new DocumentTool([new Fake("xlsx", "xlsx", "workbook", "[]")]));
        Assert.Throws<ArgumentException>(() => new DocumentTool([new Fake("a", "a", "data", "{}"), new Fake("b", "b", "data", "{}")]));
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private static string WorkbookJson() => File.ReadAllText(SamplePath("workbook.json"));

    private static byte[] Markdown()
    {
        using var output = new MemoryStream();
        XlsxGenerator.Generate(new MarkdownDocument(File.ReadAllText(SamplePath("workbook.md")).ReplaceLineEndings("\n"), [], new Dictionary<string, string>()), output, Xlsx);
        return output.ToArray();
    }

    private static string Arguments(params (string Name, string Value)[] properties)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in properties)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void AssertValidXlsx(byte[] xlsx)
    {
        using var package = SpreadsheetDocument.Open(new MemoryStream(xlsx), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.Empty(errors);
    }

    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    private static void AssertMatchesExpected(string name, string actual, [CallerFilePath] string caller = "")
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(caller)!, "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(SamplePath(name)).ReplaceLineEndings("\n"), actual);
    }

    private sealed class Fake(string format, string structuredFormat, string parameter, string schema) : IMarkdownGenerator, IStructuredGenerator
    {
        public string Format => format;

        string IStructuredGenerator.Format => structuredFormat;

        public string Title => "Fake";

        public string FileExtension => ".fake";

        public string ContentType => "application/octet-stream";

        public string ParameterName => parameter;

        public string ParameterSchema => schema;

        public string AuthoringGuide() => "## Fake\n";

        public string StructuredGuide() => "## Fake data\n";

        public GenerationResult Generate(MarkdownDocument document, Stream output) => new([]);

        public CheckResult Check(MarkdownDocument document) => new([], new CheckStatistics());

        public GenerationResult Generate(JsonElement input, IReadOnlyList<DocumentResource> resources, Stream output) => new([]);

        public CheckResult Check(JsonElement input) => new([], new CheckStatistics());
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
