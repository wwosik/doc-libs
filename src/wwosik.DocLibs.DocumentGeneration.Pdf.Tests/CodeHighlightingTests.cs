using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Highlighted code blocks in PDFs (E06-S04); layouts with made-up widths.</summary>
public class CodeHighlightingTests
{
    private const string Code = "# T\n\n```csharp\n// note\npublic int N = 42;\n```\n";

    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static readonly PdfOptions On = new() { HighlightCode = true };

    [Fact]
    public void Off_ByDefault_EveryLineIsOnePlainSpan()
    {
        var code = Assert.Single(Parse(Code).Blocks.OfType<CodeBlock>());

        Assert.Equal("csharp", code.Language);
        Assert.All(code.Lines, line => Assert.Equal(CodeTokenKind.Plain, Assert.Single(line).Kind));
    }

    [Fact]
    public void On_SplitsLinesIntoSpans()
    {
        var code = Assert.Single(Parse(Code, On).Blocks.OfType<CodeBlock>());

        Assert.Equal([new CodeSpan("// note", CodeTokenKind.Comment)], code.Lines[0]);
        Assert.Equal(
            [new("public", CodeTokenKind.Keyword), new(" ", CodeTokenKind.Plain), new("int", CodeTokenKind.Keyword), new(" N = ", CodeTokenKind.Plain),
             new("42", CodeTokenKind.Number), new(";", CodeTokenKind.Plain)],
            code.Lines[1]);
    }

    [Theory]
    [InlineData("---\nhighlight: true\n---\n", false, true)]
    [InlineData("---\nhighlight: false\n---\n", true, false)]
    [InlineData("", true, true)]
    public void FrontMatter_WinsOverTheOption(string frontMatter, bool option, bool highlighted)
    {
        var markdown = frontMatter + Code;
        var model = MarkdownParser.Parse(
            Samples.Markdown(markdown),
            new PdfOptions { HighlightCode = option },
            highlighting: CodeHighlighting.For(FrontMatter.Read(markdown, DocumentFormat.Pdf), option, new GenerationNotes()));

        Assert.Equal(highlighted, model.Blocks.OfType<CodeBlock>().Single().Lines[1].Count > 1);
    }

    [Fact]
    public void Layout_DrawsEachSpanInItsColour_OneAfterAnother()
    {
        var layout = Lay(Parse(Code, On));

        var colors = ResolvedBrand.Neutral.Code.On("F3F4F6");
        var items = layout.Pages.SelectMany(p => p.Items).OfType<TextItem>().Where(t => t.Face == FontFace.Mono).ToList();
        Assert.Equal(["// note", "public", " ", "int", " N = ", "42", ";"], items.Select(t => t.Text));
        Assert.Equal(colors.Comment, items[0].Color);
        Assert.Equal(colors.Keyword, items[1].Color);
        Assert.Equal(ResolvedBrand.Neutral.ThemeColors[0], items[2].Color);
        Assert.Equal(colors.Number, items[5].Color);
        Assert.Equal(items[1].X + (6 * 9 * 0.5), items[2].X, 6);
        Assert.Equal(items[1].Baseline, items[6].Baseline);
    }

    [Fact]
    public void Layout_BreaksALongLine_AcrossSpans_KeepingTheirColours()
    {
        var line = "var s = \"" + new string('a', 200) + "\"; // done";
        var layout = Lay(Parse($"```csharp\n{line}\n```\n", On));

        var items = layout.Pages.SelectMany(p => p.Items).OfType<TextItem>().Where(t => t.Face == FontFace.Mono).ToList();
        var colors = ResolvedBrand.Neutral.Code.On("F3F4F6");
        Assert.Equal(line, string.Concat(items.Select(t => t.Text)));
        Assert.True(items.Select(t => t.Baseline).Distinct().Count() > 1);
        Assert.All(items.Where(t => t.Text.Trim('"', 'a').Length == 0), t => Assert.Equal(colors.String, t.Color));
        Assert.Equal(colors.Comment, items[^1].Color);
    }

    [Fact]
    public void UnknownLanguage_StaysPlain_WithANote()
    {
        var notes = new GenerationNotes();
        var model = Parse("```csharpp\nvar x = 1;\n```\n", On, notes);

        Assert.Single(Assert.Single(model.Blocks.OfType<CodeBlock>()).Lines[0]);
        Assert.Equal(NoteCodes.CodeLanguageUnknown, Assert.Single(notes.Result(0).Notes).Code);
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Generate_StaysPdfA_WithItsText(string template)
    {
        var pdf = Samples.Generate(
            Samples.Markdown("---\nhighlight: true\n---\n" + Code + "\n```json\n{ \"a\": [1, true] }\n```\n"),
            new PdfOptions { Template = Samples.BuiltIn(template) });
        Samples.WriteForReview($"code-highlighting-{template}.pdf", pdf);

        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(p => p.Text));
        Assert.Contains("publicintN=42;", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("{\"a\":[1,true]}", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static DocumentModel Parse(string markdown, PdfOptions? options = null, GenerationNotes? notes = null)
    {
        options ??= new PdfOptions();
        notes ??= new GenerationNotes();
        return MarkdownParser.Parse(
            Samples.Markdown(markdown), options, notes: notes, highlighting: options.HighlightCode ? new CodeHighlighting(notes) : null);
    }

    private static Layout Lay(DocumentModel model) =>
        new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null)).Run(model);

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
