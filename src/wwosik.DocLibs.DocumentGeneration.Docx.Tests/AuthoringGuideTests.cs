using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions.Svg;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

public partial class AuthoringGuideTests
{
    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void EveryExample_Generates_WithoutNotes(string template)
    {
        var examples = Examples(DocxGenerator.AuthoringGuide(Options(template)));

        Assert.True(examples.Count >= (template == "sample" ? 6 : 3), $"{examples.Count} examples");
        Assert.All(examples, example =>
        {
            var (docx, result) = Samples.GenerateWithResult(Samples.Markdown(example), Options(template));
            Samples.AssertValid(docx);
            Assert.Empty(result.Notes);
        });
    }

    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void Guide_MatchesTheExpectation(string template)
    {
        Samples.AssertMatchesExpected($"authoring-guide-{template}", DocxGenerator.AuthoringGuide(Options(template)));
    }

    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void Guide_StaysUnder3500Words(string template)
    {
        var words = AuthoringGuides.Words(DocxGenerator.AuthoringGuide(Options(template)));

        Assert.True(words < 3500, $"{words} words");
    }

    // Every front-matter key of the template, every container, alert and note code of Word documents.
    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void Guide_NamesEveryKeyContainerAlertAndNote_OfWordDocuments(string template)
    {
        var options = Options(template);
        var guide = DocxGenerator.AuthoringGuide(options);
        var keys = options.Template.Keys
            .Concat(["template", "title", "author", "lang", "toc", "tocDepth", "headingNumbers", "classification", "draft", "transition", "build", "numberChapters"])
            .Concat(Enum.GetNames<LabelCategory>().Select(c => "labels." + c.ToLowerInvariant()));

        Assert.All(keys, key => Assert.Contains($"`{key}`", guide, StringComparison.Ordinal));
        Assert.All(LayoutSyntax.Containers.Split(", "), name => Assert.Contains("::: " + name, guide, StringComparison.Ordinal));
        Assert.All(LayoutSyntax.Alerts.Split(", "), kind => Assert.Contains($"[!{kind}]", guide, StringComparison.Ordinal));
        Assert.All(NoteCodes.All.Where(n => n.Formats.Contains(Format.Docx)), note => Assert.Contains($"`{note.Code}`", guide, StringComparison.Ordinal));
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
    public void Drawings_FollowTheRenderersAndTheImageConverter()
    {
        var plain = DocxGenerator.AuthoringGuide();
        var drawn = DocxGenerator.AuthoringGuide(Options("sample"));

        Assert.Contains("No code block is drawn in this setup", plain, StringComparison.Ordinal);
        Assert.Contains("an `.svg` image is an error here", plain, StringComparison.Ordinal);
        Assert.Contains("`:icon-name:` (such as `:icon-circle-check:`) is not drawn in this setup", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("## Charts", plain, StringComparison.Ordinal);
        Assert.Contains("## Charts", drawn, StringComparison.Ordinal);
        Assert.Contains("## Diagrams", drawn, StringComparison.Ordinal);
        Assert.Contains("native Word charts", drawn, StringComparison.Ordinal);
        Assert.Contains("A Word document draws it in the line of text", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("## Icons", DocxGenerator.AuthoringGuide(new DocxOptions { Icons = null }), StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_FollowTheBrandFonts()
    {
        Assert.Contains("set in Liberation Serif", DocxGenerator.AuthoringGuide(Options("sample")), StringComparison.Ordinal);
        Assert.DoesNotContain("| Avoid |", DocxGenerator.AuthoringGuide(), StringComparison.Ordinal);
    }

    // The sample brand with the Mermaid and chart renderers and the SVG converter; neutral and custom without them.
    private static DocxOptions Options(string template) => template == "sample"
        ? new DocxOptions
        {
            Template = Samples.Template(template),
            CodeBlockRenderers = [new MermaidRenderer(), new ChartRenderer()],
            ImageConverters = [new SvgImageConverter()],
        }
        : new DocxOptions { Template = Samples.Template(template) };

    private static List<string> Examples(string guide) =>
        [.. ExamplePattern().Matches(guide).Select(m => m.Groups["body"].Value + "\n")];

    [GeneratedRegex(@"^(?<fence>`{3,})markdown\n(?<body>.*?)\n\k<fence>$", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ExamplePattern();
}
