using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentGeneration.Diagrams;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

public partial class AuthoringGuideTests
{
    public static TheoryData<string> Cases => ["neutral", "sample", "custom"];

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryExample_Generates(string template)
    {
        var options = Options(template);
        var examples = Examples(PptxGenerator.AuthoringGuide(options));

        Assert.True(examples.Count >= 3, $"{examples.Count} examples");
        Assert.All(examples, example => Samples.AssertValid(Samples.Generate(Samples.Markdown(example), Options(template))));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Guide_StaysUnder2500Words(string template)
    {
        var words = AuthoringGuides.Words(PptxGenerator.AuthoringGuide(Options(template)));

        Assert.True(words < 2500, $"{words} words");
    }

    [Fact]
    public void Guide_NamesEveryContainerAlertAttributeAndKey_TheParserKnows()
    {
        var guide = PptxGenerator.AuthoringGuide(Options("sample"));
        var custom = PptxGenerator.AuthoringGuide(Options("custom"));

        Assert.All(LayoutSyntax.Containers.Split(", "), name => Assert.Contains("::: " + name, guide, StringComparison.Ordinal));
        Assert.All(LayoutSyntax.Alerts.Split(", "), kind => Assert.Contains($"[!{kind}]", guide, StringComparison.Ordinal));
        Assert.Contains("::: panel dark", guide, StringComparison.Ordinal);
        Assert.Contains("{.dark}", guide, StringComparison.Ordinal);
        Assert.Contains("kicker=\"", guide, StringComparison.Ordinal);
        Assert.All(
            new[] { "template", "title", "author" }.Concat(new BrandTemplateOptions().Keys).Concat(Enum.GetNames<LabelCategory>().Select(c => "labels." + c.ToLowerInvariant())),
            key => Assert.Contains($"`{key}`", guide, StringComparison.Ordinal));
        Assert.All(new CustomTemplateOptions(Stream.Null).Keys, key => Assert.Contains($"`{key}`", custom, StringComparison.Ordinal));
    }

    [Fact]
    public void FrontMatterKeys_FollowTheTemplate_AndTemplateNamesTheBrands()
    {
        var neutral = PptxGenerator.AuthoringGuide();
        var custom = PptxGenerator.AuthoringGuide(Options("custom"));
        var withBrands = PptxGenerator.AuthoringGuide(new PptxOptions { Brands = [Samples.Brand] });

        Assert.Contains("`embedFonts`", neutral, StringComparison.Ordinal);
        Assert.DoesNotContain("`layouts.title`", neutral, StringComparison.Ordinal);
        Assert.Contains("`layouts.title`", custom, StringComparison.Ordinal);
        Assert.DoesNotContain("`showDate`", custom, StringComparison.Ordinal);
        Assert.Contains("The design: `neutral`.", neutral, StringComparison.Ordinal);
        Assert.Contains("The design: `neutral` or `sample`.", withBrands, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagrams_AreInTheGuide_OnlyWithARendererThatDescribesThem()
    {
        var without = PptxGenerator.AuthoringGuide();
        var throwing = PptxGenerator.AuthoringGuide(Options("sample"));
        var keeping = PptxGenerator.AuthoringGuide(new PptxOptions { CodeBlockRenderers = [new MermaidRenderer { OnError = MermaidErrorMode.KeepCode }] });
        var silent = PptxGenerator.AuthoringGuide(new PptxOptions { CodeBlockRenderers = [Samples.Renderer(_ => null)] });

        Assert.DoesNotContain("## Diagrams", without, StringComparison.Ordinal);
        Assert.DoesNotContain("## Diagrams", silent, StringComparison.Ordinal);
        Assert.Contains("is an error naming the line", throwing, StringComparison.Ordinal);
        Assert.Contains("stays a code block; check the result", keeping, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_FollowTheBrandFonts()
    {
        var sample = PptxGenerator.AuthoringGuide(Options("sample"));
        var neutral = PptxGenerator.AuthoringGuide(Options("neutral"));

        Assert.Contains("set in Liberation Serif, with PT Sans Narrow in tables and Inconsolata for code", sample, StringComparison.Ordinal);
        // PT Sans Narrow has none of the symbols; Inconsolata has ● but not ⇒, and → is no safe replacement either.
        Assert.Contains("| ● (fine in code) | `•` |", sample, StringComparison.Ordinal);
        Assert.Contains("| ⇒ | `->` |", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("(in code only)", sample, StringComparison.Ordinal);
        Assert.DoesNotContain("| Avoid |", neutral, StringComparison.Ordinal);
        Assert.DoesNotContain("Inconsolata for code", PptxGenerator.AuthoringGuide(Options("sample") with { CodeFont = "Courier New" }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("custom")]
    public void Guide_MatchesTheExpectation(string template)
    {
        Samples.AssertMatchesExpected($"authoring-guide-{template}", PptxGenerator.AuthoringGuide(Options(template)));
    }

    [Fact]
    public void ABrandsOwnTemplate_ListsItsLayoutsForTheLayoutAttribute()
    {
        var guide = PptxGenerator.AuthoringGuide(DarkTemplate.Options);

        Assert.Contains($"`{DarkTemplate.TwoLineLayout}`", guide, StringComparison.Ordinal);
        Assert.Contains("layout=\"Name\"", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("Layouts of this design", PptxGenerator.AuthoringGuide(), StringComparison.Ordinal);
    }

    internal static List<string> Examples(string guide) =>
        [.. ExamplePattern().Matches(guide).Select(m => m.Groups["body"].Value + "\n")];

    private static PptxOptions Options(string template) => new()
    {
        Template = template == "custom"
            ? new CustomTemplateOptions(new MemoryStream(Samples.TemplateBytes(Samples.BuiltIn("neutral"))))
            : Samples.BuiltIn(template),
        CodeBlockRenderers = template == "custom" ? [] : [new MermaidRenderer()],
    };

    [GeneratedRegex(@"^(?<fence>`{3,})markdown\n(?<body>.*?)\n\k<fence>$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ExamplePattern();
}
