namespace wwosik.DocLibs.DocumentConversions.Tests;

public class MarkdownDocumentTests
{
    [Theory]
    [InlineData("images/a b.png")]
    [InlineData("./images/a%20b.png")]
    [InlineData("././images/a b.png")]
    [InlineData("images\\a b.png")]
    public void FindResource_NormalizesTheReference(string reference)
    {
        var resource = new DocumentResource("images/a b.png", "image/png", new byte[] { 1 });
        var document = new MarkdownDocument(string.Empty, [resource], new Dictionary<string, string>());

        Assert.Same(resource, document.FindResource(reference));
    }

    [Fact]
    public void FindResource_ReturnsNullForUnknownPaths()
    {
        var document = new MarkdownDocument(string.Empty, [], new Dictionary<string, string>());

        Assert.Null(document.FindResource("images/a.png"));
    }
}
