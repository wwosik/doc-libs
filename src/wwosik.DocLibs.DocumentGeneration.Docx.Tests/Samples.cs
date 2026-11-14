using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>Sample files, their committed <c>*.expected.md</c>, and helpers to generate and inspect Word documents.</summary>
internal static class Samples
{
    /// <summary>Set to <c>1</c> to write the actual output over the committed expectations instead of comparing.</summary>
    public const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    public static readonly TimeProvider FixedTime = new Fixed(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The made-up brand of <c>Samples/brand-sample</c>: colours, footer text, all four images, Liberation Serif with PT Sans Narrow in tables and Inconsolata for code.</summary>
    public static readonly Brand Brand = BrandPack.Load(Path("brand-sample"));

    /// <summary>The neutral and the sample brand and the custom template, by name, for theories.</summary>
    public static TheoryData<string> Templates => ["neutral", "sample", "custom"];

    /// <summary>The template options of <paramref name="name"/>: <c>neutral</c>, <c>sample</c> or <c>custom</c> (<see cref="CustomTemplate"/>).</summary>
    public static DocxTemplateOptions Template(string name) => name switch
    {
        "custom" => CustomTemplate.Options,
        "sample" => new BrandDocxTemplateOptions { Brand = Brand },
        _ => new BrandDocxTemplateOptions(),
    };

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", name);

    public static DocumentResource Image(string name) => new(name, "image/png", File.ReadAllBytes(Path(name)));

    /// <summary>A sample Markdown file with the two sample images and an author.</summary>
    public static MarkdownDocument Document(string name) => new(
        File.ReadAllText(Path(name)).ReplaceLineEndings("\n"),
        [Image("diagram.png"), Image("photo.png")],
        new Dictionary<string, string> { [MetadataKeys.Author] = "DocLibs" });

    public static MarkdownDocument Markdown(string markdown, params DocumentResource[] resources) =>
        new(markdown, resources, new Dictionary<string, string>());

    public static byte[] Generate(MarkdownDocument document, DocxOptions? options = null) => GenerateWithResult(document, options).Docx;

    public static (byte[] Docx, GenerationResult Result) GenerateWithResult(MarkdownDocument document, DocxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = DocxGenerator.Generate(document, output, (options ?? new DocxOptions()) with { TimeProvider = FixedTime });
        return (output.ToArray(), result);
    }

    public static byte[] TemplateBytes(DocxTemplateOptions template)
    {
        using var output = new MemoryStream();
        template.Write(output, "09.03.2026");
        return output.ToArray();
    }

    public static WordprocessingDocument Open(byte[] docx) => WordprocessingDocument.Open(new MemoryStream(docx), false);

    public static void AssertValid(byte[] docx)
    {
        using var package = Open(docx);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.Empty(errors);
    }

    public static string RoundTrip(byte[] docx) =>
        new wwosik.DocLibs.DocumentConversions.OpenXml.DocxConverter().Convert(new MemoryStream(docx), ConversionOptions.Default).Markdown;

    public static void AssertMatchesExpected(string sample, string actual, [CallerFilePath] string caller = "")
    {
        var name = sample + ".expected.md";
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(caller)!, "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(Path(name)).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>Writes a generated file to <c>.tmp/review/</c> at the repo root, for a human to open.</summary>
    public static void WriteForReview(string name, byte[] content, [CallerFilePath] string caller = "")
    {
        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(caller)!, "..", "..", ".tmp", "review"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(System.IO.Path.Combine(directory, name), content);
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
