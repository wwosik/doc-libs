using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>The slide-layout Markdown of the PPTX generator, shown plainly in PDFs.</summary>
public class SlideLayoutSyntaxTests
{
    [Fact]
    public void LayoutsSample_RoundTripsThroughThePdfConverter()
    {
        var pdf = Samples.Generate(Samples.Document("layouts.md"));
        Samples.WriteForReview("layouts-neutral.pdf", pdf);

        Samples.AssertMatchesExpected("layouts-neutral.roundtrip", Samples.RoundTrip(pdf));
    }

    [Fact]
    public void Containers_ShowTheirContent_AndHeadingAttributesAreDropped()
    {
        using var document = PdfDocument.Open(Samples.Generate(Samples.Markdown(
            "## Title {.dark kicker=\"K\"}\n\n::: cards\n### Card\n**26**\n:::\n\n> [!WARNING]\n> Careful\n\n`a` `b` and ==marked==\n")));

        var letters = string.Concat(document.GetPages().SelectMany(p => p.Letters).Select(l => l.Value)).Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.StartsWith("TitleCard26WarningCarefulabandmarked", letters, StringComparison.Ordinal); // then the footer
    }

    [Fact]
    public void LabelCategories_InFrontMatter_AreAccepted() =>
        Assert.NotEmpty(Samples.Generate(Samples.Markdown("---\nlabels.success: Done; Delivered\n---\n## T\n\n| a |\n| - |\n| `Done` |\n")));

    [Theory]
    [InlineData("## T {.wide}\n", "Unknown heading attribute '.wide'")]
    [InlineData("## T\n\n::: card\nx\n:::\n", "Unknown container '::: card'")]
    [InlineData("## T\n\n> [!HINT]\n> x\n", "Unknown alert '[!HINT]'")]
    [InlineData("---\nlabels.ok: Done\n---\n## T\n", "names no label category")]
    public void Mistakes_Throw_LikeInDecks(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }
}
