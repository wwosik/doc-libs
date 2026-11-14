using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>Highlighted code blocks in Word documents (E06-S04, applied to DOCX as well).</summary>
public class CodeHighlightingTests
{
    private const string Code = "# Report\n\n## Code\n\n```csharp\n// note\npublic int N = 42;\n```\n";

    [Fact]
    public void Off_ByDefault_EveryLineIsOneRun()
    {
        var runs = CodeRuns(Samples.Generate(Samples.Markdown(Code)));

        Assert.Equal(["// note", "public int N = 42;"], runs.Select(r => r.InnerText));
        Assert.All(runs, r => Assert.Null(r.RunProperties));
    }

    [Theory]
    [MemberData(nameof(Samples.Templates), MemberType = typeof(Samples))]
    public void FrontMatter_SwitchesItOn(string template)
    {
        var docx = Samples.Generate(Samples.Markdown("---\nhighlight: true\n---\n" + Code), new DocxOptions { Template = Samples.Template(template) });
        Samples.AssertValid(docx);
        Samples.WriteForReview($"code-highlighting-{template}.docx", docx);

        var runs = CodeRuns(docx);
        Assert.Equal(["// note", "public", " ", "int", " N = ", "42", ";"], runs.Select(r => r.InnerText));
        Assert.Null(runs[2].RunProperties);
        Assert.All(runs, r => Assert.Null(r.RunProperties?.Bold));
        Assert.All(runs, r => Assert.Null(r.RunProperties?.Italic));
        var keyword = runs[1].RunProperties!.Color!;
        if (template == "custom")
        {
            Assert.Equal(ThemeColorValues.Accent1, keyword.ThemeColor!.Value);
            Assert.Equal(ThemeColorValues.Text1, runs[0].RunProperties!.Color!.ThemeColor!.Value);
            Assert.Equal("99", runs[0].RunProperties!.Color!.ThemeTint!.Value);
            Assert.Equal(ThemeColorValues.Accent4, runs[5].RunProperties!.Color!.ThemeColor!.Value);
        }
        else
        {
            var colors = ResolvedBrand.Resolve(template == "sample" ? Samples.Brand : Brand.Neutral).Code.On("F3F4F6");
            Assert.Equal(colors.Keyword, keyword.Val!.Value);
            Assert.Null(keyword.ThemeColor);
            Assert.Equal(colors.Comment, runs[0].RunProperties!.Color!.Val!.Value);
            Assert.Equal(colors.Number, runs[5].RunProperties!.Color!.Val!.Value);
        }

        Assert.Contains("public int N = 42;", Samples.RoundTrip(docx), StringComparison.Ordinal);
    }

    [Fact]
    public void Option_SwitchesItOn_AndFrontMatterOff()
    {
        Assert.Equal(7, CodeRuns(Samples.Generate(Samples.Markdown(Code), new DocxOptions { HighlightCode = true })).Count);
        Assert.Equal(2, CodeRuns(Samples.Generate(Samples.Markdown("---\nhighlight: false\n---\n" + Code), new DocxOptions { HighlightCode = true })).Count);
    }

    [Fact]
    public void UnknownLanguage_StaysPlain_WithANote()
    {
        var (docx, result) = Samples.GenerateWithResult(Samples.Markdown("```csharpp\nvar x = 1;\n```\n"), new DocxOptions { HighlightCode = true });

        Assert.Equal(["var x = 1;"], CodeRuns(docx).Select(r => r.InnerText));
        Assert.Equal(NoteCodes.CodeLanguageUnknown, Assert.Single(result.Notes).Code);
    }

    // The runs of the paragraphs in the code style, in order.
    private static List<Run> CodeRuns(byte[] docx)
    {
        using var package = Samples.Open(docx);
        var main = package.MainDocumentPart!;
        var codeStyle = main.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(s => s.StyleName?.Val?.Value == "Code").StyleId!.Value;
        return [.. main.Document!.Body!.Elements<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == codeStyle)
            .SelectMany(p => p.Elements<Run>())
            .Select(r => (Run)r.CloneNode(true))];
    }
}
