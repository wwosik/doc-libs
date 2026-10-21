using System.Runtime.CompilerServices;

namespace wwosik.DocLibs.DocumentConversions.WindowsOnly.Tests;

/// <summary>A fact that runs only on Windows with Word and Excel installed, and shows as skipped elsewhere.</summary>
public sealed class OfficeFactAttribute : FactAttribute
{
    public OfficeFactAttribute()
    {
        if (!OfficeInstalled.Value)
        {
            this.Skip = "Needs Windows with Word and Excel installed.";
        }
    }

    private static readonly Lazy<bool> OfficeInstalled = new(() =>
        OperatingSystem.IsWindows()
        && Type.GetTypeFromProgID("Word.Application") is not null
        && Type.GetTypeFromProgID("Excel.Application") is not null);
}

/// <summary>A theory with the same condition as <see cref="OfficeFactAttribute"/>.</summary>
public sealed class OfficeTheoryAttribute : TheoryAttribute
{
    public OfficeTheoryAttribute()
    {
        this.Skip = new OfficeFactAttribute().Skip;
    }
}

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
}
