using System.Runtime.CompilerServices;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

/// <summary>Sample files, their committed <c>*.expected.txt</c>, and helpers to generate documents with diagrams.</summary>
internal static class Samples
{
    /// <summary>Set to <c>1</c> to write the actual output over the committed expectations instead of comparing.</summary>
    public const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    public static readonly TimeProvider FixedTime = new Fixed(new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The made-up brand of <c>Samples/brand-sample</c>, with PT Sans Narrow as its table and diagram font.</summary>
    public static readonly Brand Brand = BrandPack.Load(Path("brand-sample"));

    public static TheoryData<string> Brands => ["neutral", "sample"];

    public static Brand Named(string name) => name == "sample" ? Brand : Brand.Neutral;

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", name);

    public static MarkdownDocument Markdown(string markdown) => new(markdown, [], new Dictionary<string, string>());

    public static MarkdownDocument Document(string name) => Markdown(File.ReadAllText(Path(name)).ReplaceLineEndings("\n"));

    /// <summary>The <c>mermaid</c> blocks of a sample, by their first line.</summary>
    public static IEnumerable<string> MermaidBlocks(string name) =>
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path(name)).ReplaceLineEndings("\n"), "```mermaid\n(.*?)```", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value);

    public static byte[] Pdf(MarkdownDocument document, Brand brand, MermaidRenderer? renderer = null)
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(document, output, new PdfOptions
        {
            Template = new BrandPdfTemplateOptions { Brand = brand },
            CodeBlockRenderers = [renderer ?? new MermaidRenderer()],
            TimeProvider = FixedTime,
        });
        return output.ToArray();
    }

    public static byte[] Pptx(MarkdownDocument document, Brand brand, MermaidRenderer? renderer = null)
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(document, output, new PptxOptions
        {
            Template = new BrandTemplateOptions { Brand = brand },
            CodeBlockRenderers = [renderer ?? new MermaidRenderer()],
            TimeProvider = FixedTime,
        });
        return output.ToArray();
    }

    public static void AssertMatchesExpected(string sample, string actual, [CallerFilePath] string caller = "")
    {
        var name = sample + ".expected.txt";
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
