using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>What every generator's check shares: the word count, the results, the guides' parts.</summary>
public class CheckTests
{
    [Theory]
    [InlineData("The quick brown fox jumps.", 5)]
    [InlineData("It's a well-known, end-to-end test -- isn't it?", 7)]
    [InlineData("Die Straßenbahn fährt um 8:30 Uhr über die Brücke.", 10)]
    [InlineData("Zażółć gęślą jaźń, proszę.", 4)]
    [InlineData("東京駅は日本の鉄道の中心です。", 7)]
    [InlineData("ภาษาไทย", 4)]
    [InlineData("Version 2.0 of 𠀀𠀁", 5)]
    [InlineData("", 0)]
    public void Words_AreCountedAlike_InEveryLanguage(string text, int words)
    {
        Assert.Equal(words, DocumentCheck.Words(text));
    }

    [Fact]
    public void CodeSpans_CountAsOneWord()
    {
        Assert.Equal(3, DocumentCheck.Words([("Call ", false), ("Console.WriteLine(x, y)", true), (" now", false)]));
    }

    [Fact]
    public void Result_ListsNotesErrorAndStatistics()
    {
        var result = new CheckResult(
            [new GenerationNote(NoteCodes.TooManyBullets, 7, "The slide 'Agenda' has 7 bullets; keep it to 6.")],
            new CheckStatistics
            {
                Pages = 9, Words = 612, Images = 3, Tables = 2, Diagrams = 1,
                SlidesByKind = new Dictionary<string, int> { ["cover"] = 1, ["text"] = 8 }, TalkTime = TimeSpan.FromMinutes(11),
            })
        { Error = "The image 'a.png' is not a PNG, JPEG or GIF." };

        Assert.Equal(
            "- line 7: The slide 'Agenda' has 7 bullets; keep it to 6.\nError: The image 'a.png' is not a PNG, JPEG or GIF.\n"
                + "9 slides, 612 words, 3 images, 2 tables, 1 diagrams; cover 1, text 8; talk time 11 min.",
            result.ToString());
        Assert.False(result.IsClean);
        Assert.Equal("No notes.\n4 pages, 10 words, 0 images, 0 tables, 0 diagrams.", new CheckResult([], new CheckStatistics { Pages = 4, Words = 10 }).ToString());
        Assert.True(new CheckResult([], new CheckStatistics()).IsClean);
        Assert.Equal(result, result with { Statistics = result.Statistics with { SlidesByKind = new Dictionary<string, int> { ["cover"] = 1, ["text"] = 8 } } });
    }

    [Fact]
    public void TalkTime_ParticipatesInTheEstimate()
    {
        var checks = CheckOptions.Default;

        Assert.Equal(TimeSpan.FromSeconds(30), DocumentCheck.ContentSlideTime(10, checks));
        Assert.Equal(TimeSpan.FromMinutes(2), DocumentCheck.ContentSlideTime(260, checks));
        Assert.Equal(TimeSpan.FromMinutes(3), DocumentCheck.RoundTalkTime(TimeSpan.FromSeconds(150)));
    }

    [Fact]
    public void CheckCodes_AreCodes_AndTheGuidesListThemApart()
    {
        var notes = AuthoringGuides.Notes(DocumentFormat.Pptx);
        var apart = notes.IndexOf("A check before generating", StringComparison.Ordinal);

        Assert.Subset(NoteCodes.All.Select(n => n.Code).ToHashSet(), NoteCodes.CheckOnly.ToHashSet());
        Assert.True(apart > notes.IndexOf("`slide-split`", StringComparison.Ordinal));
        Assert.True(notes.IndexOf("`too-many-bullets`", StringComparison.Ordinal) > apart);
        Assert.DoesNotContain("`long-paragraph`", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_ListOnlyTheFormatsLimits_AndNothingWithoutAny()
    {
        Assert.Equal(
            "## Limits\n\nA check before generating reports what goes beyond these: at most 150 words in a paragraph; at most 60 characters in a heading; alt text for every image. Keep to them from the start.",
            AuthoringGuides.Limits(CheckOptions.Default, DocumentFormat.Pdf));
        Assert.Empty(AuthoringGuides.Limits(new CheckOptions { MaxWordsPerParagraph = null, MaxTitleLength = null, RequireAltText = false }, DocumentFormat.Pdf));
    }
}
