using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Highlighted code blocks in decks (E06-S04): the switch, the colours on light, dark and caller templates.</summary>
public class CodeHighlightingTests
{
    private const string Code = "## Code\n\n```csharp\n// note\npublic int N = 42;\n```\n";

    private const string FlowCode = "## Code {.dark}\n\n```csharp\npublic int N = 42;\n```\n";

    [Fact]
    public void Off_ByDefault_CodeIsOneRunPerLine()
    {
        var deck = Samples.Generate(Samples.Markdown(Code));

        Assert.Equal(["// note", "public int N = 42;"], CodeRuns(deck).Select(r => r.Text!.Text));
        Assert.All(CodeRuns(deck), r => Assert.Null(r.RunProperties!.GetFirstChild<A.SolidFill>()));
    }

    [Fact]
    public void Off_GivesTheSameDeck_AsBeforeTheOptionExisted()
    {
        Assert.Equal(
            PresentingTests.Parts(Samples.Generate(Samples.Markdown(Code))),
            PresentingTests.Parts(Samples.Generate(Samples.Markdown("---\nhighlight: false\n---\n" + Code), new PptxOptions { HighlightCode = true })));
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void FrontMatter_SwitchesItOn_InTheBrandsColours(string template)
    {
        var options = new PptxOptions { Template = Samples.BuiltIn(template) };
        var deck = Samples.Generate(Samples.Markdown("---\nhighlight: true\n---\n" + Code), options);
        Samples.AssertValid(deck);
        Samples.WriteForReview($"code-highlighting-{template}.pptx", deck);

        var colors = ResolvedBrand.ForSlides(template == "sample" ? Samples.Brand : Brand.Neutral).Code.On(ResolvedBrand.Mix(ResolvedBrand.ForSlides(template == "sample" ? Samples.Brand : Brand.Neutral).TableLine, 0.30));
        var runs = CodeRuns(deck);
        Assert.Equal(["// note", "public", " ", "int", " N = ", "42", ";"], runs.Select(r => r.Text!.Text));
        Assert.Equal(colors.Comment, Hex(runs[0]));
        Assert.Equal(colors.Keyword, Hex(runs[1]));
        Assert.Null(Hex(runs[2]));
        Assert.Equal(colors.Number, Hex(runs[5]));
        Assert.All(runs, r => Assert.Null(r.RunProperties!.Bold));
        Assert.All(runs, r => Assert.Null(r.RunProperties!.Italic));
    }

    [Fact]
    public void Option_SwitchesItOn_AndFrontMatterSwitchesItOff()
    {
        var on = Samples.Generate(Samples.Markdown(Code), new PptxOptions { HighlightCode = true });
        var off = Samples.Generate(Samples.Markdown("---\nhighlight: false\n---\n" + Code), new PptxOptions { HighlightCode = true });

        Assert.Equal(7, CodeRuns(on).Count);
        Assert.Equal(2, CodeRuns(off).Count);
    }

    [Fact]
    public void DarkFlowSlide_LightensTheColours_OnItsCodeBox()
    {
        var deck = Samples.Generate(Samples.Markdown(FlowCode), new PptxOptions { HighlightCode = true });
        Samples.AssertValid(deck);

        var box = ResolvedBrand.Mix(ResolvedBrand.Neutral.Primary, 0.85);
        var keyword = Hex(CodeRuns(deck).First(r => r.Text!.Text == "public"))!;
        Assert.True(ResolvedBrand.Contrast(keyword, box) >= 4.5, keyword);
        Assert.Equal(ResolvedBrand.Neutral.Code.On(box, dark: true).Keyword, keyword);
    }

    [Fact]
    public void DarkTemplate_LightensTheColours()
    {
        var deck = Samples.Generate(Samples.Markdown(Code), DarkTemplate.Options with { HighlightCode = true });
        Samples.AssertValid(deck);
        Samples.WriteForReview("code-highlighting-dark.pptx", deck);

        var keyword = Hex(CodeRuns(deck).First(r => r.Text!.Text == "public"))!;
        Assert.True(ResolvedBrand.Contrast(keyword, "374151") >= 4.5, keyword);
    }

    [Fact]
    public void CallerTemplate_TakesThemeColours()
    {
        var template = new CustomTemplateOptions(new MemoryStream(Samples.TemplateBytes(Samples.BuiltIn("neutral"))));
        var deck = Samples.Generate(Samples.Markdown(Code + "\n" + FlowCode.Replace("## Code {.dark}", "## Flow {.dark}", StringComparison.Ordinal)), new PptxOptions { Template = template, HighlightCode = true });
        Samples.AssertValid(deck);

        var runs = CodeRuns(deck);
        Assert.Equal(A.SchemeColorValues.Text1, Scheme(runs[0])!.Val!.Value);
        Assert.Equal(60000, Scheme(runs[0])!.GetFirstChild<A.LuminanceModulation>()!.Val!.Value);
        Assert.Equal(A.SchemeColorValues.Accent1, Scheme(runs[1])!.Val!.Value);
        Assert.Equal(A.SchemeColorValues.Accent4, Scheme(runs.First(r => r.Text!.Text == "42"))!.Val!.Value);
        Assert.All(runs.Where(r => r.Text!.Text == "public"), r => Assert.Equal(A.SchemeColorValues.Accent1, Scheme(r)!.Val!.Value));
    }

    [Fact]
    public void BrandCodeColours_AreUsedAsGiven()
    {
        var brand = Samples.Brand with { Colors = Samples.Brand.Colors with { Code = new BrandCodeColors { Keyword = "FFFF00" } } };
        var deck = Samples.Generate(Samples.Markdown(Code), new PptxOptions { Template = new BrandTemplateOptions { Brand = brand }, HighlightCode = true });

        Assert.Equal("FFFF00", Hex(CodeRuns(deck).First(r => r.Text!.Text == "public")));
    }

    [Fact]
    public void UnknownLanguage_StaysPlain_WithANote_OnlyWhileHighlightingIsOn()
    {
        const string markdown = "## Code\n\n```csharpp\nvar x = 1;\n```\n";
        using var on = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), on, new PptxOptions { HighlightCode = true });
        using var off = new MemoryStream();
        var plain = PptxGenerator.Generate(Samples.Markdown(markdown), off);

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.CodeLanguageUnknown, note.Code);
        Assert.Equal(3, note.Line);
        Assert.Equal(["var x = 1;"], CodeRuns(on.ToArray()).Select(r => r.Text!.Text));
        Assert.False(plain.HasNotes);
    }

    [Fact]
    public void Renderers_AreAskedFirst_AndADeclinedBlockGetsNoSecondNote()
    {
        var asked = new List<string>();
        var renderer = Samples.Renderer(request =>
        {
            asked.Add(request.Language);
            if (request.Language == "mermaid")
            {
                request.Report!(NoteCodes.CodeNotDrawn, "not drawn");
            }

            return null;
        });
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(
            Samples.Markdown(Code + "\n```mermaid\nflowchart LR\n```\n"), output, new PptxOptions { HighlightCode = true, CodeBlockRenderers = [renderer] });

        Assert.Equal(["csharp", "mermaid"], asked);
        Assert.Equal(7 + 1, CodeRuns(output.ToArray()).Count);
        Assert.Equal([NoteCodes.CodeNotDrawn], result.Notes.Select(n => n.Code));
    }

    [Fact]
    public void IndentedCode_AndInlineCode_StayPlain()
    {
        var deck = Samples.Generate(Samples.Markdown("## Code\n\nUse `public int` here.\n\n    public int N;\n"), new PptxOptions { HighlightCode = true });

        Assert.All(CodeRuns(deck), r => Assert.Null(r.RunProperties!.GetFirstChild<A.SolidFill>()));
    }

    // The runs in the code font, in order, across all slides.
    private static List<A.Run> CodeRuns(byte[] deck)
    {
        using var package = Samples.Open(deck);
        return [.. Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Run>())
            .Where(r => r.RunProperties?.GetFirstChild<A.LatinFont>()?.Typeface?.Value is "Consolas" or "Inconsolata")
            .Select(r => (A.Run)r.CloneNode(true))];
    }

    private static string? Hex(A.Run run) =>
        run.RunProperties!.GetFirstChild<A.SolidFill>()?.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value;

    private static A.SchemeColor? Scheme(A.Run run) =>
        run.RunProperties!.GetFirstChild<A.SolidFill>()?.GetFirstChild<A.SchemeColor>();
}
