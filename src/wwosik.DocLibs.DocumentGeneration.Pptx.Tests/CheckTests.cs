using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary><see cref="PptxGenerator.Check"/>: the style rules, image sources, errors as a result, and the statistics.</summary>
public class CheckTests
{
    private static readonly PptxOptions Drawing = new() { CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }, new ChartRenderer()] };

    public static TheoryData<string> SampleFiles => ["deck.md", "layouts.md", "presenting.md", "presenting-lists.md", "schedules.md", "steps-and-risks.md"];

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Check_ReturnsEveryNoteOfGenerate_AndItsSlides(string name)
    {
        var document = Sample(name);
        using var output = new MemoryStream();
        var generated = PptxGenerator.Generate(document, output, Drawing);

        var check = PptxGenerator.Check(document, Drawing);

        Assert.Null(check.Error);
        Assert.Equal(generated.Pages, check.Statistics.Pages);
        Assert.Equal(generated.Pages, check.Statistics.SlidesByKind.Values.Sum());
        Assert.All(generated.Notes, note => Assert.Contains(note, check.Notes));
        Assert.All(check.Notes.Except(generated.Notes), note => Assert.Contains(note.Code, NoteCodes.CheckOnly));
    }

    [Fact]
    public void Layouts_GoldenStatistics()
    {
        var statistics = PptxGenerator.Check(Sample("layouts.md"), Drawing).Statistics;

        Assert.Equal(
            "15 slides, 480 words, 0 images, 3 tables, 1 diagrams; cover 1, layout 9, agenda 1, quote 1, statement 1, contact 1, chart 1; talk time 7 min.",
            statistics.ToString());
        Assert.Equal(TimeSpan.FromMinutes(7), statistics.TalkTime);
    }

    [Fact]
    public void Deck_GoldenStatistics()
    {
        var check = PptxGenerator.Check(Samples.Deck(), Drawing);

        Assert.Equal(GoldenDeck, check.ToString());
    }

    [Fact]
    public void TooManyBullets_FiresAboveTheLimit_AtTheHeadingsLine_AfterFrontMatter()
    {
        var markdown = "---\ntitle: T\n---\n# Deck\n\n## Six\n\n- a\n- b\n- c\n  - d\n- e\n- f\n\n## Seven\n\n- a\n- b\n- c\n  - d\n- e\n- f\n- g\n";

        var note = Assert.Single(PptxGenerator.Check(Samples.Markdown(markdown)).Notes);

        Assert.Equal((NoteCodes.TooManyBullets, 15), (note.Code, note.Line));
        Assert.Equal("The slide 'Seven' has 7 bullets; keep it to 6.", note.Message);
        Assert.Empty(PptxGenerator.Check(Samples.Markdown(markdown), new PptxOptions { Checks = new CheckOptions { MaxBulletsPerSlide = null } }).Notes);
    }

    [Fact]
    public void TooManyWords_CountsTheText_NotTitleNotesTablesOrCode()
    {
        var words = string.Join(' ', Enumerable.Repeat("word", 10));
        var markdown = $"# Deck\n\n## A title of many words that are not counted at all\n\n{words}\n\n- {words}\n\n```\n{words} {words}\n```\n\n"
            + $"| A |\n| - |\n| {words} {words} |\n\n::: notes\n{words} {words} {words}\n:::\n";
        var options = new PptxOptions { Checks = new CheckOptions { MaxWordsPerSlide = 20 } };

        Assert.Empty(PptxGenerator.Check(Samples.Markdown(markdown), options).Notes);

        var note = Assert.Single(PptxGenerator.Check(Samples.Markdown(markdown), options with { Checks = new CheckOptions { MaxWordsPerSlide = 19 } }).Notes);
        Assert.Equal((NoteCodes.TooManyWords, 3), (note.Code, note.Line));
        Assert.Contains("has 20 words; keep it to 19.", note.Message, StringComparison.Ordinal);
        Assert.Empty(PptxGenerator.Check(Samples.Markdown(markdown), options with { Checks = new CheckOptions { MaxWordsPerSlide = null } }).Notes);
    }

    [Fact]
    public void TitleTooLong_CountsTextElements_WithoutTheHeadingsSettings()
    {
        var sixty = new string('x', 59) + "é";
        var markdown = $"# {sixty} {{.dark}}\n\n## {sixty}y\n\nText.\n";

        var note = Assert.Single(PptxGenerator.Check(Samples.Markdown(markdown)).Notes);

        Assert.Equal((NoteCodes.TitleTooLong, 3), (note.Code, note.Line));
        Assert.Contains("is 61 characters long; keep titles to 60.", note.Message, StringComparison.Ordinal);
        Assert.Empty(PptxGenerator.Check(Samples.Markdown(markdown), new PptxOptions { Checks = new CheckOptions { MaxTitleLength = null } }).Notes);
    }

    [Fact]
    public void AltTextMissing_FiresForAnImageWithEmptyBrackets()
    {
        var markdown = "# Deck\n\n## Picture\n\n![](photo.png)\n\n## Described\n\n![A photo](photo.png)\n";
        var document = Samples.Markdown(markdown, Samples.Image("photo.png"));

        var note = Assert.Single(PptxGenerator.Check(document).Notes, n => n.Code == NoteCodes.AltTextMissing);

        Assert.Equal(5, note.Line);
        Assert.DoesNotContain(PptxGenerator.Check(document, new PptxOptions { Checks = new CheckOptions { RequireAltText = false } }).Notes, n => n.Code == NoteCodes.AltTextMissing);
    }

    [Fact]
    public void MissingAndRemoteImages_AreAllReported_AndTheCheckGoesOn()
    {
        var markdown = "# Deck\n\n## One\n\n![First](missing-1.png)\n\n## Two\n\n![Second](missing-2.png){.round}\n\n![Third](https://example.org/a.png)\n\n"
            + "## Three\n\n![Fourth](data:image/png;base64,AAAA)\n\n![Fine](photo.png)\n";

        var check = PptxGenerator.Check(Samples.Markdown(markdown, Samples.Image("photo.png")));

        Assert.Null(check.Error);
        var sources = check.Notes.Where(n => n.Code == NoteCodes.ImageSource).ToList();
        Assert.Equal([5, 9, 11, 15], sources.Select(n => n.Line));
        Assert.Contains("is a web address", sources[2].Message, StringComparison.Ordinal);
        Assert.Equal(4, check.Statistics.Pages);
        Assert.Equal(5, check.Statistics.Images);
    }

    [Fact]
    public void ADocumentError_IsReturned_NotThrown_WithTheNotesFoundBefore()
    {
        var markdown = "# Deck\n\n## Too long a title for a slide, which keeps going on and on and on\n\n::: unknown\nText.\n:::\n";

        var check = PptxGenerator.Check(Samples.Markdown(markdown));

        Assert.NotNull(check.Error);
        Assert.Contains("unknown", check.Error, StringComparison.Ordinal);
        Assert.False(check.IsClean);
        Assert.Equal(NoteCodes.TitleTooLong, Assert.Single(check.Notes).Code);
        Assert.Equal(0, check.Statistics.Pages);
        Assert.Null(check.Statistics.TalkTime);
        Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));
    }

    [Fact]
    public void BadFrontMatter_IsAnError()
    {
        var check = PptxGenerator.Check(Samples.Markdown("---\nnoSuchKey: 1\n---\n# Deck\n"));

        Assert.Contains("noSuchKey", check.Error, StringComparison.Ordinal);
        Assert.Empty(check.Notes);
    }

    [Fact]
    public void TalkTime_TakesSpeakerNotes_OverTheSlidesWords_AndAtLeastTheMinimum()
    {
        var notes = string.Join(' ', Enumerable.Repeat("spoken", 260));
        var markdown = $"# Deck\n\n## Short\n\nOne line.\n\n## Explained\n\nOne line.\n\n::: notes\n{notes}\n:::\n\n# Closing {{.closing}}\n";

        var check = PptxGenerator.Check(Samples.Markdown(markdown));

        // Cover and closing 10 s each, "Short" 30 s at least, "Explained" two minutes of notes: 2 min 50 s.
        Assert.Equal(TimeSpan.FromMinutes(3), check.Statistics.TalkTime);
        Assert.Equal(new Dictionary<string, int> { ["cover"] = 1, ["text"] = 2, ["closing"] = 1 }, check.Statistics.SlidesByKind);
        // At 260 words a minute and no minimum: 20 s, one minute of notes, and a second for "Short".
        Assert.Equal(TimeSpan.FromMinutes(1), PptxGenerator.Check(Samples.Markdown(markdown), new PptxOptions { Checks = new CheckOptions { WordsPerMinute = 260, MinTimePerSlide = TimeSpan.Zero } }).Statistics.TalkTime);
    }

    [Fact]
    public void Diagrams_AreCounted_WhenARendererDrawsThem()
    {
        var markdown = "# Deck\n\n## Flow\n\n```mermaid\ngraph TD\n  A --> B\n```\n\n## Plan\n\n::: steps\n- **One** – first\n- **Two** – second\n:::\n\n## Code\n\n```csharp\nvar x = 1;\n```\n";

        Assert.Equal(2, PptxGenerator.Check(Samples.Markdown(markdown), Drawing).Statistics.Diagrams);
        Assert.Equal(1, PptxGenerator.Check(Samples.Markdown(markdown)).Statistics.Diagrams);
    }

    [Fact]
    public void InvalidCheckOptions_Throw()
    {
        Assert.Throws<ArgumentException>(() => PptxGenerator.Check(Samples.Markdown("# Deck\n"), new PptxOptions { Checks = new CheckOptions { WordsPerMinute = 0 } }));
        Assert.Throws<ArgumentException>(() => PptxGenerator.Check(Samples.Markdown("# Deck\n"), new PptxOptions { Checks = new CheckOptions { MaxBulletsPerSlide = -1 } }));
    }

    [Fact]
    public void TheGuide_ListsTheLimits_AndTheCheckCodes()
    {
        var guide = PptxGenerator.AuthoringGuide();
        var none = PptxGenerator.AuthoringGuide(new PptxOptions
        {
            Checks = new CheckOptions { MaxBulletsPerSlide = null, MaxWordsPerSlide = null, MaxTitleLength = null, RequireAltText = false },
        });

        Assert.Contains("## Limits", guide, StringComparison.Ordinal);
        Assert.Contains("at most 6 bullets on a slide", guide, StringComparison.Ordinal);
        Assert.Contains("at most 80 words on a slide", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("words in a paragraph", guide, StringComparison.Ordinal);
        Assert.Contains("at most 9 bullets", PptxGenerator.AuthoringGuide(new PptxOptions { Checks = new CheckOptions { MaxBulletsPerSlide = 9 } }), StringComparison.Ordinal);
        Assert.DoesNotContain("## Limits", none, StringComparison.Ordinal);
        Assert.All(NoteCodes.CheckOnly.Where(c => NoteCodes.All.Single(n => n.Code == c).Formats.Contains(Format.Pptx)), code => Assert.Contains($"`{code}`", guide, StringComparison.Ordinal));
    }

    private const string GoldenDeck = """
        - line 7: The slide 'Agenda' has 7 bullets; keep it to 6.
        - line 58: The mermaid diagram cannot be drawn, so it stays code: Invalid mermaid header: "graph TD; A-->B;". Expected "graph TD", "flowchart LR", etc.
        9 slides, 85 words, 3 images, 1 tables, 0 diagrams; cover 1, section 1, text 3, visual 4; talk time 4 min.
        """;

    private static MarkdownDocument Sample(string name) => name == "deck.md"
        ? Samples.Deck()
        : new(File.ReadAllText(Samples.Path(name)).ReplaceLineEndings("\n"), [Samples.Image("diagram.png"), Samples.Image("photo.png")], new Dictionary<string, string>());
}
