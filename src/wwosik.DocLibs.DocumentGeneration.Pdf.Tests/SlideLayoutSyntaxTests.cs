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

    [Fact]
    public void ImagesInOneParagraph_StandSideBySide()
    {
        var pdf = Samples.Generate(Samples.Markdown("## Charts\n\n![A](diagram.png) ![B](diagram.png)\n", Samples.Image("diagram.png")));

        using var document = PdfDocument.Open(pdf);
        var images = document.GetPages().SelectMany(p => p.GetImages()).Select(i => i.BoundingBox).ToList();
        Assert.Equal(2, images.Count);
        Assert.Equal(images[0].Top, images[1].Top, 1);
        Assert.True(images[1].Left >= images[0].Right, "The second image is right of the first.");
    }

    [Fact]
    public void Columns_FollowOneAnother_WithoutRules()
    {
        var pdf = Samples.Generate(Samples.Markdown("## C\n\n::: columns 1:2\nLeft text\n\n---\n\nRight text\n:::\n"));

        var text = Samples.RoundTrip(pdf);
        Assert.True(text.IndexOf("Left text", StringComparison.Ordinal) < text.IndexOf("Right text", StringComparison.Ordinal));
        Assert.DoesNotContain("---", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ColouredWords_TakeTheirCategorysColour()
    {
        var pdf = Samples.Generate(Samples.Markdown("## S\n\nStatus: [on schedule]{.success} today.\n"));

        using var document = PdfDocument.Open(pdf);
        var letters = document.GetPages().SelectMany(p => p.Letters).ToList();
        string Hex(UglyToad.PdfPig.Content.Letter letter)
        {
            var (r, g, b) = letter.Color.ToRGBValues();
            return $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
        }

        Assert.Equal(ResolvedBrand.Neutral.Labels.Success, Hex(letters.First(l => l.Value == "o" && letters.IndexOf(l) > letters.FindIndex(x => x.Value == ":"))));
        Assert.DoesNotContain(letters, l => l.Value is "[" or "{");
    }
}
