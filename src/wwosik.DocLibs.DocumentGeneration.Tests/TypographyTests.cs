using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The typography pass the generators share (<see cref="Typography"/>, <see cref="TypographyRules"/>).</summary>
public class TypographyTests
{
    private const char Nbsp = ' ';

    private static readonly Dictionary<string, TypographyRules> None = [];

    [Theory]
    [InlineData("\"Hello\" -- it's 10 km...", "“Hello” – it’s 10 km…")]
    [InlineData("She said \"yes\".", "She said “yes”.")]
    [InlineData("'single' quotes", "‘single’ quotes")]
    [InlineData("'Peter's car'", "‘Peter’s car’")]
    [InlineData("Chris' book", "Chris’ book")]
    [InlineData("the '90s", "the ’90s")]
    [InlineData("5' tall, a 3\" screen", "5’ tall, a 3\" screen")]
    [InlineData("(\"quoted\")", "(“quoted”)")]
    [InlineData("a - b", "a – b")]
    [InlineData("a---b", "a—b")]
    [InlineData("a--b", "a–b")]
    [InlineData("2020-2026 and 2026-11-14", "2020-2026 and 2026-11-14")]
    [InlineData("2020--2026", "2020--2026")]
    [InlineData("5 - 3", "5 - 3")]
    [InlineData("well-known", "well-known")]
    [InlineData("a ---- b", "a ---- b")]
    [InlineData("A --> B <-- C", "A --> B <-- C")]
    [InlineData("wait.... no.. yes...", "wait.... no.. yes…")]
    [InlineData("10 % and 10%", "10 % and 10%")]
    [InlineData("5 min, 5 mice, 3 m²", "5 min, 5 mice, 3 m²")]
    [InlineData("on 13 November", "on 13 November")]
    [InlineData("on 13 Nov 2026", "on 13 Nov 2026")]
    [InlineData("See https://example.com/a--b...", "See https://example.com/a--b...")]
    [InlineData("Mail \"x\"@example.com", "Mail \"x\"@example.com")]
    [InlineData("<span title=\"a\">-- b", "<span title=\"a\">– b")]
    public void English(string typed, string set) => Assert.Equal(set, Typeset(typed, "en"));

    [Theory]
    [InlineData("\"Hallo\" - z. B. am 13. November", "„Hallo“ – z. B. am 13. November")]
    [InlineData("'einfach' und \"doppelt\"", "‚einfach‘ und „doppelt“")]
    [InlineData("Peter's Auto", "Peter’s Auto")]
    [InlineData("siehe S. 12, Nr. 5 und § 3 Abs. 2", "siehe S. 12, Nr. 5 und § 3 Abs. 2")]
    [InlineData("i. d. R. und d. h.", "i. d. R. und d. h.")]
    [InlineData("am 3. Okt. und am 1. Mai", "am 3. Okt. und am 1. Mai")]
    [InlineData("USA. Dann", "USA. Dann")]
    [InlineData("10 °C", "10 °C")]
    public void German(string typed, string set) => Assert.Equal(set, Typeset(typed, "de"));

    [Theory]
    [InlineData("de-CH", "„a“")]
    [InlineData("de-AT", "„a“")]
    [InlineData("en-US", "“a”")]
    [InlineData("pl", "“a”")]
    public void ATag_IsMatchedByItsLanguage_OtherLanguagesGetEnglish(string tag, string set) => Assert.Equal(set, Typeset("\"a\"", tag));

    [Fact]
    public void HostRules_WinOverTheBuiltInOnes()
    {
        var french = TypographyRules.English with { DoubleQuotes = ("« ", " »"), Apostrophe = "’" };
        var swiss = TypographyRules.German with { DoubleQuotes = ("«", "»") };
        var host = new Dictionary<string, TypographyRules> { ["fr"] = french, ["de-CH"] = swiss };

        Assert.Equal("« Bonjour » l’ami", Typography.For(true, "fr-FR", host, _ => true, new GenerationNotes())!.Apply("\"Bonjour\" l'ami", 1));
        Assert.Equal("«Grüezi»", Typography.For(true, "de-CH", host, _ => true, new GenerationNotes())!.Apply("\"Grüezi\"", 1));
        Assert.Equal("„Hallo“", Typography.For(true, "de", host, _ => true, new GenerationNotes())!.Apply("\"Hallo\"", 1));
    }

    [Fact]
    public void Off_GivesNoPass() => Assert.Null(Typography.For(false, "en", None, _ => true, new GenerationNotes()));

    [Fact]
    public void QuotesPairAcrossRuns_CodeRunsStay_AndTheRunsKeepTheirNumber()
    {
        var typography = Typography.For(true, "en", None, _ => true, new GenerationNotes())!;
        (string Text, bool Code)[] runs = [("He said \"look at ", false), ("this", false), ("\" and ", false), ("\"x\" -- y", true), (" ...", false)];

        var set = typography.Apply(runs, r => r.Text, r => r.Code, (r, text) => (text, r.Code), 3);

        Assert.Equal(["He said “look at ", "this", "” and ", "\"x\" -- y", " …"], set.Select(r => r.Text));
    }

    [Fact]
    public void ADashOrEllipsisAcrossRuns_StaysAsTyped()
    {
        var typography = Typography.For(true, "en", None, _ => true, new GenerationNotes())!;

        var set = typography.Apply(["a -", "- b ..", "."], r => r, _ => false, (_, text) => text, 1);

        Assert.Equal(["a -", "- b ..", "."], set);
    }

    [Fact]
    public void ACharacterTheFontsLack_StaysAsTyped_WithOneNotePerCharacter()
    {
        var notes = new GenerationNotes();
        var typography = Typography.For(true, "en", None, c => c != '“' && c != 0xA0, notes)!;

        Assert.Equal("\"Hi” – 10 km and \"again”", typography.Apply("\"Hi\" -- 10 km and \"again\"", 4));
        Assert.Equal("\"b”", typography.Apply("\"b\"", 9));

        var result = notes.Result(0).Notes;
        Assert.Equal(2, result.Count);
        Assert.All(result, n => Assert.Equal(NoteCodes.TypographySkipped, n.Code));
        Assert.All(result, n => Assert.Equal(4, n.Line));
        Assert.Contains(result, n => n.Message.Contains("U+201C", StringComparison.Ordinal));
        Assert.Contains(result, n => n.Message.Contains("U+00A0", StringComparison.Ordinal));
    }

    [Fact]
    public void SpacesAreNeverAddedOrRemoved()
    {
        const string typed = "\"A\" - B -- C --- D ... 10 km, z. B. 13. Mai, it's 'x'";

        Assert.Equal(typed.Count(c => c == ' '), Typeset(typed, "de").Count(c => c is ' ' or Nbsp));
    }

    [Fact]
    public void TheKey_SwitchesItOnOrOff_OverTheOption()
    {
        Assert.True(Typography.IsOn(Front("typography: true"), false));
        Assert.False(Typography.IsOn(Front("typography: false"), true));
        Assert.True(Typography.IsOn(Front("title: T"), true));
        var error = Assert.Throws<DocumentConversionException>(() => Typography.IsOn(Front("typography: yes"), false));
        Assert.Contains("'typography' has the value 'yes'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GermanRules_DifferFromEnglish_InQuotesAbbreviationsAndMonths()
    {
        Assert.NotEqual(TypographyRules.English.DoubleQuotes, TypographyRules.German.DoubleQuotes);
        Assert.NotEqual(TypographyRules.English.SingleQuotes, TypographyRules.German.SingleQuotes);
        Assert.Empty(TypographyRules.English.Abbreviations);
        Assert.Contains("z. B.", TypographyRules.German.Abbreviations);
        Assert.Contains("März", TypographyRules.German.MonthNames);
        Assert.All(GeneratedTexts.German.MonthNames, m => Assert.Contains(m, TypographyRules.German.MonthNames));
        Assert.Equal(TypographyRules.English.Dash, TypographyRules.German.Dash);
    }

    [Fact]
    public void TheGuides_ListTheKey_WithTheOptionsDefault()
    {
        Assert.Contains("| `typography` |", AuthoringGuides.FrontMatter("'neutral'", "en", []), StringComparison.Ordinal);
        Assert.Contains("Default `false`. |", AuthoringGuides.FrontMatter("'neutral'", "en", [], typography: false).Split('\n').Single(l => l.StartsWith("| `typography`", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("Default `true`. |", AuthoringGuides.FrontMatter("'neutral'", "en", [], typography: true).Split('\n').Single(l => l.StartsWith("| `typography`", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    private static string Typeset(string text, string tag) =>
        Typography.For(true, tag, None, _ => true, new GenerationNotes())!.Apply(text, 1);

    private static FrontMatter Front(string lines) => FrontMatter.Read($"---\n{lines}\n---\n\n# T\n", DocumentFormat.Pdf);
}
