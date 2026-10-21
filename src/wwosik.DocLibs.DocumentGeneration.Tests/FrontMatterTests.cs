using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class FrontMatterTests
{
    [Fact]
    public void Block_IsRemovedFromTheBody()
    {
        var front = FrontMatter.Read("---\nkey: value\n---\n# Title\n", DocumentFormat.Pdf);

        Assert.Equal("# Title\n", front.Body);
    }

    [Theory]
    [InlineData("---\n\n## Slide\n")]
    [InlineData("## Slide\n\n---\n\ntitle: x\n")]
    [InlineData("---")]
    [InlineData("")]
    public void WithoutFrontMatter_TheMarkdownIsUnchanged(string markdown) =>
        Assert.Equal(markdown, FrontMatter.Read(markdown, DocumentFormat.Pdf).Body);

    [Fact]
    public void Syntax_AllowsComments_BlankLines_Quotes_DotsAndAnyCase()
    {
        var front = FrontMatter.Read("---\r\n# comment\r\n\r\nTITLE: \"Plan: 2027\"\r\nauthor: 'Team VT'\r\n...\r\nbody\r\n", DocumentFormat.Pdf);

        Assert.Equal("Plan: 2027", front.Title);
        Assert.Equal("Team VT", front.Author);
        Assert.Equal("body\r\n", front.Body);
    }

    [Theory]
    [InlineData("---\ntitle: a\nTitle: b\n---\n", "'Title' appears twice")]
    [InlineData("---\ntitle: a\njust text\n---\n", "'just text' is not 'key: value'")]
    [InlineData("---\ntitle:\n---\n", "'title' has no value")]
    [InlineData("---\ntitle: a\n## Slide\n", "never closed")]
    public void MalformedBlocks_ThrowWithTheGeneratorsFormat(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => FrontMatter.Read(markdown, DocumentFormat.Docx));

        Assert.Equal(DocumentFormat.Docx, error.Format);
        Assert.Contains(expected, error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SwitchesTheTemplateFirst_ThenAppliesTheOtherKeysInAnyOrder()
    {
        var result = Apply("---\nsize: large\ntemplate: B\ntitle: ignored\n---\n", new Options("a", "small"));

        Assert.Equal(new Options("b", "large"), result);
    }

    [Theory]
    [InlineData("---\ncolour: red\n---\n", "'colour' is not supported by the template 'a'. Valid keys: template, title, author, size.")]
    [InlineData("---\ntemplate: c\n---\n", "'template' has the value 'c'; expected 'a' or 'b'.")]
    [InlineData("---\nsize: huge\n---\n", "'size' has the value 'huge'; expected 'small' or 'large'.")]
    public void Apply_Mistakes_ThrowNamingTheKey(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Apply(markdown, new Options("a", "small")));

        Assert.Contains(expected, error.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void Bool_IsCaseInsensitive(string value, bool expected) =>
        Assert.Equal(expected, FrontMatter.Read(string.Empty, DocumentFormat.Pdf).Bool("key", value));

    private static Options Apply(string markdown, Options options)
    {
        var front = FrontMatter.Read(markdown, DocumentFormat.Pdf);
        return front.Apply(
            options,
            (current, name) => name.ToLowerInvariant() is "a" or "b" ? current with { Template = name.ToLowerInvariant() } : null,
            "'a' or 'b'",
            current => current.Template,
            _ => ["size"],
            (current, key, value) => current with { Size = front.OneOf(key, value, ("small", "small"), ("large", "large")) });
    }

    private sealed record Options(string Template, string Size);
}
