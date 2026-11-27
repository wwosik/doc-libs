using System.Text.RegularExpressions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public partial class AuthoringGuideTests
{
    [Fact]
    public void EveryExample_Generates_WithoutNotes()
    {
        var examples = ExamplePattern().Matches(XlsxGenerator.AuthoringGuide(Samples.Options)).Select(m => m.Groups["body"].Value + "\n").ToList();

        Assert.True(examples.Count >= 4, $"{examples.Count} examples");
        Assert.All(examples, example =>
        {
            var (xlsx, result) = MarkdownTests.Generate(MarkdownTests.Doc(example));
            Samples.AssertValid(xlsx);
            Assert.Empty(result.Notes);
        });
    }

    [Fact]
    public void Guide_MatchesTheExpectation()
    {
        MarkdownTests.AssertMatchesExpected("authoring-guide", XlsxGenerator.AuthoringGuide());
    }

    [Fact]
    public void Guide_StaysUnder2500Words()
    {
        var words = AuthoringGuides.Words(XlsxGenerator.AuthoringGuide());

        Assert.True(words < 2500, $"{words} words");
    }

    // The lists of the guide against the code: header attributes, totals, chart settings and types, front matter keys, note codes.
    [Fact]
    public void Guide_NamesEveryAttributeKeyAndNote()
    {
        var guide = XlsxGenerator.AuthoringGuide();

        Assert.All(XlsxMarkdown.Types.Keys, type => Assert.Contains($"`.{type}", guide, StringComparison.Ordinal));
        Assert.All(XlsxMarkdown.Settings, setting => Assert.Contains($"`{setting}=", guide, StringComparison.Ordinal));
        Assert.All(XlsxMarkdown.Totals.Keys, total => Assert.Contains($"`{total}`", guide, StringComparison.Ordinal));
        Assert.All(XlsxMarkdown.ChartSettings, setting => Assert.Contains($"`{setting}`", guide, StringComparison.Ordinal));
        Assert.All(XlsxMarkdown.ChartTypes.Keys, type => Assert.Contains($"`{type}`", guide, StringComparison.Ordinal));
        Assert.All(
            XlsxMarkdown.Keys.Concat(XlsxMarkdown.OtherFormatKeys).Concat(["title", "author", "lang", "template", "classification", "draft", "audience", "description", "keywords"]),
            key => Assert.Contains($"`{key}`", guide, StringComparison.Ordinal));
        Assert.All(NoteCodes.All.Where(n => n.Formats.Contains(Format.Xlsx) && n.Code != NoteCodes.JsonInvalid), note => Assert.Contains($"`{note.Code}`", guide, StringComparison.Ordinal));

        // The code of the JSON form is in its own guide, not in the Markdown's.
        Assert.DoesNotContain("`json-invalid`", guide, StringComparison.Ordinal);
        Assert.Contains("`json-invalid`", XlsxWorkbookJson.Guide(), StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_FollowsTheOptions()
    {
        Assert.Contains("the `internal` version", XlsxGenerator.AuthoringGuide(new XlsxOptions { Audience = Audience.Internal }), StringComparison.Ordinal);
        Assert.Contains("`sample`", XlsxGenerator.AuthoringGuide(new XlsxOptions { Brands = [Samples.Brand] }), StringComparison.Ordinal);
    }

    [Fact]
    public void DocsPage_ShowsTheFeatureSample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "CHANGELOG.md")))
        {
            directory = directory.Parent;
        }

        var docs = File.ReadAllText(Path.Combine(directory.FullName, "docs", "wwosik.DocLibs.DocumentGeneration.Xlsx.md")).ReplaceLineEndings("\n");
        Assert.Contains(MarkdownTests.Sample().Markdown, docs, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^(?<fence>`{3,})markdown\n(?<body>.*?)\n\k<fence>$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ExamplePattern();
}
