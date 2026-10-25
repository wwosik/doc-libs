using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

public partial class AuthoringGuideTests
{
    public static TheoryData<string> Brands => ["neutral", "sample"];

    [Theory]
    [MemberData(nameof(Brands))]
    public void EveryExample_Generates(string brand)
    {
        var examples = ExamplePattern().Matches(PdfGenerator.AuthoringGuide(Options(brand))).Select(m => m.Groups["body"].Value + "\n").ToList();

        Assert.True(examples.Count >= 3, $"{examples.Count} examples");
        Assert.All(examples, example => Assert.NotEmpty(Samples.Generate(Samples.Markdown(example), Options(brand))));
    }

    [Theory]
    [MemberData(nameof(Brands))]
    public void Guide_StaysUnder2500Words(string brand)
    {
        var words = AuthoringGuides.Words(PdfGenerator.AuthoringGuide(Options(brand)));

        Assert.True(words < 2500, $"{words} words");
    }

    [Fact]
    public void Guide_NamesEveryContainerAlertAndKey_TheParserKnows()
    {
        var guide = PdfGenerator.AuthoringGuide();

        Assert.All(LayoutSyntax.Containers.Split(", "), name => Assert.True(
            guide.Contains("::: " + name, StringComparison.Ordinal) || guide.Contains($"`{name}`", StringComparison.Ordinal), name));
        Assert.All(LayoutSyntax.Alerts.Split(", "), kind => Assert.Contains($"[!{kind}]", guide, StringComparison.Ordinal));
        Assert.All(
            new[] { "template", "title", "author", "pageSize", "orientation" }
                .Concat(new BrandPdfTemplateOptions().Keys)
                .Concat(Enum.GetNames<LabelCategory>().Select(c => "labels." + c.ToLowerInvariant())),
            key => Assert.Contains($"`{key}`", guide, StringComparison.Ordinal));
    }

    [Fact]
    public void Defaults_AreTheOptions()
    {
        var guide = PdfGenerator.AuthoringGuide(new PdfOptions { PageSize = PdfPageSize.Letter, Orientation = PdfOrientation.Landscape });

        Assert.Contains("`A4`, `A3` or `Letter`. Default `Letter`.", guide, StringComparison.Ordinal);
        Assert.Contains("`portrait` or `landscape`. Default `landscape`.", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_FollowTheBrandFonts_AndTheReplacementHook()
    {
        var sample = PdfGenerator.AuthoringGuide(Options("sample"));
        var neutral = PdfGenerator.AuthoringGuide(Options("neutral"));
        var replaced = PdfGenerator.AuthoringGuide(new PdfOptions { ReplaceUnsupported = _ => "?" });

        // The sample's table font has none of the symbols; its Inconsolata has these two.
        Assert.Contains("| ● (fine in code) | `•` |", sample, StringComparison.Ordinal);
        Assert.Contains("| ✓ (fine in code) | `[x]` |", sample, StringComparison.Ordinal);
        Assert.Contains("| ✓ | `[x]` |", neutral, StringComparison.Ordinal);
        Assert.DoesNotContain("| ● |", neutral, StringComparison.Ordinal);
        Assert.Contains("| ‖ (in code only) | `\\|` |", neutral, StringComparison.Ordinal); // Liberation Mono lacks it
        Assert.Contains("stops the generation with an error naming it", sample, StringComparison.Ordinal);
        Assert.Contains("is replaced, usually by a placeholder", replaced, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Brands))]
    public void EveryReplacement_IsInEveryFace(string brand)
    {
        var fonts = FontSet.For(ResolvedBrand.Resolve(((BrandPdfTemplateOptions)Samples.BuiltIn(brand)).Brand));
        var replacements = ReplacementPattern().Matches(PdfGenerator.AuthoringGuide(Options(brand))).Select(m => m.Groups[1].Value.Replace("\\|", "|", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(replacements);
        Assert.All(
            replacements.SelectMany(r => r.EnumerateRunes()).Distinct(),
            c => Assert.All(Enum.GetValues<FontFace>(), face => Assert.True(fonts.HasGlyph(c, face), $"{c} in {face}")));
        Assert.All(AuthoringGuides.Symbols, s => Assert.True(s.Instead[^1].All(char.IsAscii), s.Symbol));
    }

    [Theory]
    [InlineData("neutral")]
    [InlineData("sample")]
    public void Guide_MatchesTheExpectation(string brand)
    {
        Samples.AssertMatchesExpected($"authoring-guide-{brand}", PdfGenerator.AuthoringGuide(Options(brand)));
    }

    private static PdfOptions Options(string brand) => new() { Template = Samples.BuiltIn(brand), CodeBlockRenderers = [new MermaidRenderer()] };

    [GeneratedRegex(@"^\| [^|]+ \| `(.+)` \|$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReplacementPattern();

    [GeneratedRegex(@"^(?<fence>`{3,})markdown\n(?<body>.*?)\n\k<fence>$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ExamplePattern();
}
