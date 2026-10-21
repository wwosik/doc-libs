using System.Runtime.CompilerServices;

namespace wwosik.DocLibs.DocumentConversions.Tests;

/// <summary>Sample files and their committed <c>*.expected.md</c>.</summary>
internal static class Samples
{
    /// <summary>Set to <c>1</c> to write the actual output over the committed expectations instead of comparing.</summary>
    public const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", name);

    public static FileStream Open(string name) => File.OpenRead(Path(name));

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

    /// <summary>Every resource is referenced from the Markdown, and every image reference points at a resource.</summary>
    public static void AssertResourcesMatchReferences(MarkdownDocument document)
    {
        var referenced = System.Text.RegularExpressions.Regex.Matches(document.Markdown, @"!\[[^\]]*\]\(<?([^)>]+)>?\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(referenced.Order(StringComparer.Ordinal), document.Resources.Select(r => r.Path).Order(StringComparer.Ordinal));
    }
}
