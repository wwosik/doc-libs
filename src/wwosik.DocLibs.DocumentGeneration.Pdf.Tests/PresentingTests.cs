using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Speaker notes, transitions and builds, which are for decks: PDFs check them and leave them out.</summary>
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

    [Fact]
    public void BuildMarkers_AreLeftOut_AndTheRestIsUnchanged()
    {
        var plain = Generate(
            "---\ntitle: Plan\n---\n# Plan\n\nIntro.\n\n## One\n\nText.\n\n- A\n- B\n\n::: panel\nInside.\n\nMore.\n:::\n\n"
            + "> [!NOTE]\n> Noted.\n\n| A |\n| - |\n| 1 |\n");
        var built = Generate(
            "---\ntitle: Plan\nbuild: lists\n---\n# Plan\n\n{.build=fly}\n\nIntro.\n\n## One\n\n{.frag}\n\nText.\n\n{.build .exit}\n\n- A\n- B\n\n"
            + "{.emphasize}\n\n::: panel\nInside.\n\n{.frag}\n\nMore.\n:::\n\n> [!NOTE]\n> {.build=wipe}\n>\n> Noted.\n\n{.exit}\n\n| A |\n| - |\n| 1 |\n");

        Assert.Equal(WithoutId(plain), WithoutId(built));
    }

    [Theory]
    [InlineData("## A\n\n{.build}\n\n## B\n\nText.\n", "followed by a heading")]
    [InlineData("## A\n\nText.\n\n{.build}\n\n::: notes\nSay it.\n:::\n", "followed by speaker notes")]
    [InlineData("## A\n\n{.build}\n\n{.frag}\n\nText.\n", "followed by another marker")]
    [InlineData("## A\n\nText.\n\n{.build}\n", "followed by nothing to build")]
    [InlineData("## A\n\n- Item\n\n  {.build}\n\n  More.\n", "inside a list")]
    [InlineData("## A\n\nText.\n\n::: notes\n{.build}\n\nSay it.\n:::\n", "inside speaker notes")]
    [InlineData("## A\n\n{.note}\n\nText.\n", "Unknown build marker '.note'")]
    [InlineData("## A\n\n{.build=zoom}\n\nText.\n", "Valid markers: .build, .build=appear, .build=fly, .build=wipe, .frag, .exit, .emphasize.")]
    public void AStrayOrUnknownMarker_Throws(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));
        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildKey_IsChecked()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate("---\nbuild: all\n---\n## Plan\n\nText.\n"));
        Assert.Contains("'build'", error.Message, StringComparison.Ordinal);
    }
}
