using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The built-in highlighter (<see cref="CodeHighlighter"/>), its languages and the code colours of brands.</summary>
public class CodeHighlightingTests
{
    private static readonly Brand Sample = BrandPack.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample"));

    [Theory]
    [InlineData("csharp", "public class A { int n = 42; } // c", "<k:public> <k:class> A { <k:int> n = <n:42>; } <c:// c>")]
    [InlineData("cs", "var s = \"x\";", "<k:var> s = <s:\"x\">;")]
    [InlineData("C#", "return null;", "<k:return> <k:null>;")]
    [InlineData("python", "def f(x):  # c\n    return 'a'", "<k:def> f(x):  <c:# c>\n    <k:return> <s:'a'>")]
    [InlineData("js", "const a = 'x'; // c", "<k:const> a = <s:'x'>; <c:// c>")]
    [InlineData("typescript", "const s: string = 'x';", "<k:const> s: <k:string> = <s:'x'>;")]
    [InlineData("sql", "SELECT a FROM t -- c", "<k:SELECT> a <k:FROM> t <c:-- c>")]
    [InlineData("xml", "<a b=\"c\"/>", "<<m:a> <m:b>=<s:\"c\">/>")]
    [InlineData("html", "<p class=\"x\">t</p>", "<<m:p> <m:class>=<s:\"x\">>t</<m:p>>")]
    [InlineData("powershell", "$x = Get-Item # c", "<t:$x> = <k:Get-Item> <c:# c>")]
    [InlineData("java", "int x = 1;", "<k:int> x = <n:1>;")]
    [InlineData("cpp", "int main() { return 0; }", "<k:int> main() { <k:return> 0; }")]
    public void Languages_OfColorCode(string language, string code, string expected) => Assert.Equal(expected, Show(language, code));

    [Theory]
    [InlineData("{ \"a\": 1, \"b\": [true, null, \"x\"] }", "{ <m:\"a\">: <n:1>, <m:\"b\">: [<k:true>, <k:null>, <s:\"x\">] }")]
    [InlineData("{\"a\":-1.5e3} // c", "{<m:\"a\">:<n:-1.5e3>} <c:// c>")]
    [InlineData("\"unclosed \\\" string", "\"unclosed \\\" string")]
    public void Json(string code, string expected) => Assert.Equal(expected, Show("json", code));

    [Theory]
    [InlineData("# c\nname: x # d\ncount: 3", "<c:# c>\n<m:name>: x <c:# d>\n<m:count>: <n:3>")]
    [InlineData("- item: 'a'\n- \"b\"\n- true", "- <m:item>: <s:'a'>\n<p:-> <s:\"b\">\n<p:-> <k:true>")]
    [InlineData("---\nurl: http://x/a#b\nv: 1.2.3", "<p:--->\n<m:url>: http://x/a#b\n<m:v>: 1.2.3")]
    [InlineData("base: &b\n  x: 1\nuse: *b", "<m:base>: <t:&b>\n  <m:x>: <n:1>\n<m:use>: <t:*b>")]
    public void Yaml(string code, string expected) => Assert.Equal(expected, Show("yml", code));

    [Theory]
    [InlineData("#!/bin/sh\n# c\nexport NAME=\"x\"", "<c:#!/bin/sh>\n<c:# c>\n<k:export> NAME=<s:\"x\">")]
    [InlineData("for f in *.md; do echo $f ${HOME} $1; done", "<k:for> f <k:in> *.md; <k:do> <k:echo> <t:$f> <t:${HOME}> <t:$1>; <k:done>")]
    [InlineData("ls -la 'a b' 2>/dev/null # c", "ls -la <s:'a b'> <n:2>>/dev/null <c:# c>")]
    [InlineData("echo a#b", "<k:echo> a#b")]
    [InlineData("git checkout -b in-place", "git checkout -b in-place")]
    public void Bash(string code, string expected) => Assert.Equal(expected, Show("sh", code));

    [Theory]
    [InlineData("bash")]
    [InlineData("shell")]
    [InlineData("zsh")]
    [InlineData("YAML")]
    [InlineData(" json ")]
    [InlineData("vb.net")]
    [InlineData("fs")]
    [InlineData("ps1")]
    [InlineData("md")]
    public void Aliases_AreKnown(string name) => Assert.NotNull(CodeLanguages.Find(name));

    [Theory]
    [InlineData("csharpp")]
    [InlineData("mermaid")]
    [InlineData("go")]
    [InlineData("text")]
    public void UnknownLanguages_GiveNoTokens(string name) => Assert.Null(CodeHighlighter.Highlight(name, "x"));

    [Fact]
    public void Tokens_CoverTheCode_AndMergeNeighbours()
    {
        var code = "public static void M() { }\n";
        var tokens = CodeHighlighter.Highlight("csharp", code)!;
        Assert.Equal(0, tokens[0].Start);
        Assert.Equal(code.Length, tokens.Sum(t => t.Length));
        Assert.All(tokens.Zip(tokens.Skip(1)), pair => Assert.Equal(pair.First.Start + pair.First.Length, pair.Second.Start));
        Assert.All(tokens.Zip(tokens.Skip(1)), pair => Assert.NotEqual(pair.First.Kind, pair.Second.Kind));
    }

    [Fact]
    public void LongUnclosedStrings_TakeLinearTime()
    {
        var code = "{ \"" + new string('a', 200_000) + "\\\"" + new string('b', 200_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var language in new[] { "json", "yaml", "bash" })
        {
            Assert.NotNull(CodeHighlighter.Highlight(language, code));
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Fact]
    public void Lines_SplitSpansAtLineBreaks_AndDrawPunctuationAsPlain()
    {
        var notes = new GenerationNotes();
        var lines = new CodeHighlighting(notes).Lines("csharp", "/* a\nb */ x();\n", 3)!;

        Assert.Equal(3, lines.Count);
        Assert.Equal([new CodeSpan("/* a", CodeTokenKind.Comment)], lines[0]);
        Assert.Equal([new CodeSpan("b */", CodeTokenKind.Comment), new CodeSpan(" x();", CodeTokenKind.Plain)], lines[1]);
        Assert.Empty(lines[2]);
        Assert.False(notes.Result(0).HasNotes);
    }

    [Theory]
    [InlineData("csharpp", true)]
    [InlineData("mermaid", true)]
    [InlineData("text", false)]
    [InlineData("Plain", false)]
    [InlineData("console", false)]
    [InlineData("output", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UnknownLanguage_GivesANote(string? language, bool note)
    {
        var notes = new GenerationNotes();

        Assert.Null(new CodeHighlighting(notes).Lines(language, "x", 7));
        var result = notes.Result(0);
        Assert.Equal(note, result.Notes.Any(n => n is { Code: NoteCodes.CodeLanguageUnknown, Line: 7 }));
    }

    [Fact]
    public void UnknownLanguage_GivesNoNote_WhenARendererReportedOnTheBlock()
    {
        var notes = new GenerationNotes();
        notes.Add(NoteCodes.CodeNotDrawn, 7, "not drawn");

        Assert.Null(new CodeHighlighting(notes).Lines("mermaid", "x", 7));
        Assert.Single(notes.Result(0).Notes);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData("true", false, true)]
    [InlineData("false", true, false)]
    [InlineData("TRUE", false, true)]
    public void Switch_FrontMatterOverTheOption(string? value, bool option, bool on)
    {
        var markdown = value is null ? "# T\n" : $"---\nhighlight: {value}\n---\n# T\n";
        var frontMatter = FrontMatter.Read(markdown, DocumentFormat.Pdf);

        Assert.Equal(on, CodeHighlighting.IsOn(frontMatter, option));
        Assert.Equal(on, CodeHighlighting.For(frontMatter, option, new GenerationNotes()) is not null);
    }

    [Fact]
    public void Switch_RejectsOtherValues()
    {
        var frontMatter = FrontMatter.Read("---\nhighlight: yes\n---\n", DocumentFormat.Pdf);

        Assert.Throws<DocumentConversionException>(() => CodeHighlighting.IsOn(frontMatter, false));
    }

    [Fact]
    public void LanguageList_NamesEveryLanguage()
    {
        var list = CodeLanguages.List();

        Assert.StartsWith("`csharp`, `vb`, `fsharp`", list, StringComparison.Ordinal);
        Assert.EndsWith("`json`, `yaml`, `bash`", list, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("F3F4F6")]
    [InlineData("FFFFFF")]
    [InlineData("E5E7EB")]
    public void DefaultColours_HaveContrast_OnLightBackgrounds(string background)
    {
        foreach (var brand in new[] { Brand.Neutral, Sample, new Brand { Name = "pale", Colors = new BrandColors { Primary = "FFE066", Link = "A0E0FF" } } })
        {
            var colors = ResolvedBrand.Resolve(brand).Code.On(background);
            foreach (var kind in Enum.GetValues<CodeTokenKind>().Where(k => colors.Of(k) is not null))
            {
                Assert.True(ResolvedBrand.Contrast(colors.Of(kind)!, background) >= 4.5, $"{brand.Name} {kind} {colors.Of(kind)} on {background}");
            }
        }
    }

    [Theory]
    [InlineData("374151")]
    [InlineData("1F2937")]
    [InlineData("000000")]
    public void DefaultColours_AreLightened_OnDarkBackgrounds(string background)
    {
        var colors = ResolvedBrand.Resolve(Brand.Neutral).Code.On(background, dark: true);
        foreach (var kind in Enum.GetValues<CodeTokenKind>().Where(k => colors.Of(k) is not null))
        {
            Assert.True(ResolvedBrand.Contrast(colors.Of(kind)!, background) >= 4.5, $"{kind} {colors.Of(kind)} on {background}");
        }

        Assert.NotEqual(ResolvedBrand.Neutral.Primary, colors.Keyword);
    }

    [Fact]
    public void DefaultColours_ComeFromTheBrand()
    {
        var brand = ResolvedBrand.Resolve(Sample);

        Assert.Equal(brand.Primary, brand.Code.Keyword);
        Assert.Equal(brand.Labels.Success, brand.Code.String);
        Assert.Equal(brand.Footer, brand.Code.Comment);
        Assert.Equal(brand.Labels.Warning, brand.Code.Number);
        Assert.Equal(brand.Diagram.Accent, brand.Code.Type);
        Assert.Equal(brand.Link, brand.Code.Markup);
    }

    [Fact]
    public void BrandColours_AreUsedAsGiven()
    {
        var brand = new Brand { Name = "own", Colors = BrandColors.Neutral with { Code = new BrandCodeColors { Keyword = "#ffff00", Comment = "C0C0C0" } } };

        var colors = ResolvedBrand.Resolve(brand).Code.On("F3F4F6");

        Assert.Equal("FFFF00", colors.Keyword);
        Assert.Equal("C0C0C0", colors.Comment);
        Assert.True(ResolvedBrand.Contrast(colors.String, "F3F4F6") >= 4.5);
    }

    [Fact]
    public void BrandColours_AreChecked()
    {
        var brand = new Brand { Name = "bad", Colors = BrandColors.Neutral with { Code = new BrandCodeColors { Number = "red" } } };

        var error = Assert.Throws<ArgumentException>(() => ResolvedBrand.Resolve(brand));
        Assert.Contains("Colors.Code.Number", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandPack_ReadsCodeColours_AndRejectsUnknownKeys()
    {
        static Brand Load(string json) => BrandPack.Load(path => path == "brand.json" ? new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)) : null);

        var brand = Load("""{ "name": "p", "colors": { "primary": "#112233", "code": { "keyword": "#7C3AED", "markup": "#0000FF" } }, "slideColors": { "code": { "string": "#00FF00" } } }""");

        Assert.Equal(new BrandCodeColors { Keyword = "#7C3AED", Markup = "#0000FF" }, brand.Colors.Code);
        Assert.Equal("#00FF00", brand.SlideColors!.Code!.String);
        Assert.Equal("7C3AED", ResolvedBrand.Resolve(brand).Code.On("F3F4F6").Keyword);
        var error = Assert.Throws<InvalidDataException>(() => Load("""{ "name": "p", "colors": { "primary": "#112233", "code": { "keywords": "#7C3AED" } } }"""));
        Assert.Contains("keywords", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlideColours_MergeOverColours()
    {
        var brand = new Brand
        {
            Name = "slides",
            Colors = BrandColors.Neutral with { Code = new BrandCodeColors { Keyword = "111111", String = "222222" } },
            SlideColors = new BrandColors { Primary = "1F2937", Code = new BrandCodeColors { Keyword = "333333" } },
        };

        var slides = ResolvedBrand.ForSlides(brand).Code;

        Assert.Equal("333333", slides.Keyword);
        Assert.Equal("222222", slides.String);
        Assert.Equal("111111", ResolvedBrand.Resolve(brand).Code.Keyword);
    }

    private static readonly HashSet<string> Brackets = ["{", "}", "(", ")", "[", "]", ";", ",", ".", "=", ":", "<", ">", "/", "</", "/>"];

    // The code with every token but plain ones as <kind:text>: k keyword, s string, c comment, n number, t type,
    // p punctuation, m markup.
    private static string Show(string language, string code)
    {
        var tokens = CodeHighlighter.Highlight(language, code) ?? throw new InvalidOperationException($"'{language}' is unknown.");
        var text = new StringBuilder();
        foreach (var token in tokens)
        {
            var piece = code.Substring(token.Start, token.Length);
            text.Append(token.Kind switch
            {
                CodeTokenKind.Plain => piece,
                CodeTokenKind.Punctuation when piece.Trim().Length == 0 || Brackets.Contains(piece.Trim()) => piece,
                _ => $"<{char.ToLowerInvariant(token.Kind.ToString()[0])}:{piece}>",
            });
        }

        return text.ToString();
    }
}
