using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

public partial class AuthoringGuideTests
{
    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void EveryExample_Generates(string template)
    {
        var examples = Examples(DocxGenerator.AuthoringGuide(Options(template)));

        Assert.NotEmpty(examples);
        Assert.All(examples, example => Samples.AssertValid(Samples.Generate(Samples.Markdown(example), Options(template))));
    }

    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void Guide_MatchesTheExpectation(string template)
    {
        Samples.AssertMatchesExpected($"authoring-guide-{template}", DocxGenerator.AuthoringGuide(Options(template)));
    }

    [Fact]
    public void Guide_NamesEveryKeyOfItsTemplate()
    {
        var neutral = DocxGenerator.AuthoringGuide();
        var custom = DocxGenerator.AuthoringGuide(Options("custom"));

        Assert.All(new BrandDocxTemplateOptions().Keys.Concat(["toc", "tocDepth", "template", "title", "author", "lang"]), key => Assert.Contains($"`{key}`", neutral, StringComparison.Ordinal));
        Assert.All(new CustomDocxTemplateOptions(Stream.Null).Keys, key => Assert.Contains($"`{key}`", custom, StringComparison.Ordinal));
        Assert.DoesNotContain("`showDate`", custom, StringComparison.Ordinal);
        Assert.Contains("`language-unknown`", neutral, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_FollowTheBrandFonts()
    {
        Assert.Contains("set in Liberation Serif", DocxGenerator.AuthoringGuide(Options("sample")), StringComparison.Ordinal);
        Assert.DoesNotContain("| Avoid |", DocxGenerator.AuthoringGuide(), StringComparison.Ordinal);
    }

    private static DocxOptions Options(string template) => new() { Template = Samples.Template(template) };

    private static List<string> Examples(string guide) =>
        [.. ExamplePattern().Matches(guide).Select(m => m.Groups["body"].Value + "\n")];

    [GeneratedRegex(@"^(?<fence>`{3,})markdown\n(?<body>.*?)\n\k<fence>$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ExamplePattern();
}
