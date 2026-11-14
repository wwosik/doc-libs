using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Speaker notes and transitions, which are for decks: PDFs check them and leave them out.</summary>
public class PresentingTests
{
    private static byte[] Generate(string markdown)
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(Samples.Markdown(markdown), output, new PdfOptions { TimeProvider = Samples.FixedTime });
        return output.ToArray();
    }

    [Fact]
    public void SpeakerNotesAndTransitions_AreLeftOut_AndTheRestIsUnchanged()
    {
        var plain = Generate("---\ntitle: Plan\n---\n# Plan\n\nIntro.\n\n## One\n\nText.\n\n## Two\n\n- Item\n");
        var presenting = Generate(
            "---\ntitle: Plan\ntransition: fade\n---\n# Plan {transition=\"zoom\"}\n\nIntro.\n\n::: notes\nWelcome.\n:::\n\n## One {transition=\"morph\"}\n\nText.\n\n"
            + "::: notes\nSay **this**.\n\n| A | B |\n| - | - |\n| 1 | 2 |\n:::\n\n## Two\n\n- Item\n");

        Assert.Equal(WithoutId(plain), WithoutId(presenting));
    }

    // The file without its random /ID, which differs on every run.
    private static string WithoutId(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.Latin1.GetString(pdf), @"/ID \[ <[0-9A-F]+><[0-9A-F]+>\]", "/ID");

    [Theory]
    [InlineData("## Plan\n\n::: panel\n::: notes\nHidden.\n:::\n:::\n")]
    [InlineData("## Plan\n\n- Item\n\n  ::: notes\n  Hidden.\n  :::\n")]
    [InlineData("## Plan\n\n:::: notes\n::: notes\nHidden.\n:::\n::::\n")]
    public void NestedNotes_Throw(string markdown)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));
        Assert.Contains("'::: notes' goes at the top level", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownTransition_Throws()
    {
        var heading = Assert.Throws<DocumentConversionException>(() => Generate("## Plan {transition=\"spin\"}\n\nText.\n"));
        Assert.Contains("Unknown transition 'spin'", heading.Message, StringComparison.Ordinal);
        var frontMatter = Assert.Throws<DocumentConversionException>(() => Generate("---\ntransition: spin\n---\n## Plan\n\nText.\n"));
        Assert.Contains("'transition'", frontMatter.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesTakeNoArguments()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("## Plan\n\n::: notes dark\nHidden.\n:::\n"));
        Assert.Contains("takes no arguments", error.Message, StringComparison.Ordinal);
    }
}
