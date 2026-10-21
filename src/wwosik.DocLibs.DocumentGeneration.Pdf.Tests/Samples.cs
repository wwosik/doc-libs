using System.Runtime.CompilerServices;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Sample files, their committed <c>*.expected.md</c>, and helpers to generate PDFs.</summary>
internal static class Samples
{
    /// <summary>Set to <c>1</c> to write the actual output over the committed expectations instead of comparing.</summary>
    public const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    public static readonly TimeProvider FixedTime = new Fixed(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The built-in templates, by their front-matter name, for theories.</summary>
    public static TheoryData<string> BuiltInTemplates => ["neutral"];

    public static PdfTemplateOptions BuiltIn(string name) => PdfTemplateOptions.Create(name)!;

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", name);

    public static DocumentResource Image(string name) => new(name, "image/png", File.ReadAllBytes(Path(name)));

    /// <summary>A sample Markdown file with the two sample images and an author.</summary>
    public static MarkdownDocument Document(string name) => new(
        File.ReadAllText(Path(name)).ReplaceLineEndings("\n"),
        [Image("diagram.png"), Image("photo.png")],
        new Dictionary<string, string> { [MetadataKeys.Author] = "DocLibs" });

    public static MarkdownDocument Markdown(string markdown, params DocumentResource[] resources) =>
        new(markdown, resources, new Dictionary<string, string>());

    public static byte[] Generate(MarkdownDocument document, PdfOptions? options = null)
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(document, output, (options ?? new PdfOptions()) with { TimeProvider = FixedTime });
        return output.ToArray();
    }

    public static string RoundTrip(byte[] pdf) =>
        new wwosik.DocLibs.DocumentConversions.Pdf.PdfConverter().Convert(new MemoryStream(pdf), ConversionOptions.Default).Markdown;

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
