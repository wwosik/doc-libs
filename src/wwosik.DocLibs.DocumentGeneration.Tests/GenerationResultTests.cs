using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class GenerationResultTests
{
    [Fact]
    public void Notes_AreOrderedByLine_ThoseWithoutALineLast_AndRepeatsKeptOnce()
    {
        var notes = new GenerationNotes();
        notes.Add("b", 7, "Second.");
        notes.Add("c", 0, "Anywhere.");
        notes.Add("a", 2, "First.");
        notes.Add("b", 7, "Second.");

        var result = notes.Result(3);

        Assert.Equal(["First.", "Second.", "Anywhere."], result.Notes.Select(n => n.Message));
        Assert.Equal(3, result.Pages);
        Assert.Equal("- line 2: First.\n- line 7: Second.\n- Anywhere.", result.ToString());
    }

    [Fact]
    public void Results_CompareByContent()
    {
        var one = new GenerationResult([new GenerationNote("x", 1, "M")]) { Pages = 2 };
        var two = new GenerationResult([new GenerationNote("x", 1, "M")]) { Pages = 2 };

        Assert.Equal(one, two);
        Assert.NotEqual(one, two with { Pages = 3 });
    }

    [Fact]
    public void EveryCode_HasAFormatAndAdvice_AndIsDocumented()
    {
        var codes = typeof(NoteCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var docs = File.ReadAllText(Path.Combine(Root(), "docs", "wwosik.DocLibs.DocumentGeneration.md"));

        Assert.Equal(codes.Order(), NoteCodes.All.Select(n => n.Code).Order());
        Assert.All(NoteCodes.All, n => Assert.True(n.Formats.Length > 0 && n.Advice.Length > 20, n.Code));
        Assert.All(codes, code => Assert.Contains($"| `{code}` |", docs, StringComparison.Ordinal));
    }

    [Fact]
    public void TheGuidesNotes_ListOnlyTheFormatsCodes()
    {
        var pdf = AuthoringGuides.Notes(DocumentFormat.Pdf, NoteCodes.CharacterReplaced);

        Assert.Contains("`html-as-text`", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("`slide-split`", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("`character-replaced`", pdf, StringComparison.Ordinal);
        Assert.Contains("`slide-split`", AuthoringGuides.Notes(DocumentFormat.Pptx), StringComparison.Ordinal);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CHANGELOG.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
