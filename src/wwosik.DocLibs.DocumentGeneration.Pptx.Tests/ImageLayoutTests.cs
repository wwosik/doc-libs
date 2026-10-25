using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Images side by side, captions, and columns.</summary>
public class ImageLayoutTests
{
    private static readonly DocumentResource[] Images = [Samples.Image("diagram.png"), Samples.Image("photo.png")];

    [Theory]
    [InlineData("## Charts\n\nAbove.\n\n![A](diagram.png) ![B](photo.png)\n\nBelow.\n")]
    [InlineData("## Charts {kicker=\"K\"}\n\nAbove.\n\n![A](diagram.png)\n![B](photo.png)\n\nBelow.\n")]
    public void ImagesInOneParagraph_ShareOneSlide_SideBySide_InEqualCells(string markdown)
    {
        var pptx = Samples.Generate(Samples.Markdown(markdown, Images));
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Assert.Single(Samples.Slides(package));

        var pictures = Pictures(slide);
        Assert.Equal(2, pictures.Count);
        Assert.Equal(pictures[0].Offset!.Y!.Value, pictures[1].Offset!.Y!.Value);
        var centres = pictures.Select(p => p.Offset!.X!.Value + (p.Extents!.Cx!.Value / 2)).ToList();
        var below = slide.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "Below.").ShapeProperties!.Transform2D!.Offset!.Y!.Value;
        Assert.True(below > pictures.Max(p => p.Offset!.Y!.Value + p.Extents!.Cy!.Value), "The text after the row goes below it.");
        var cell = centres[1] - centres[0]; // the distance between the centres of two equal cells
        Assert.True(Math.Abs(pictures[0].Offset!.X!.Value + (pictures[0].Extents!.Cx!.Value / 2) - (centres[1] - cell)) < 2);
    }

    [Fact]
    public void Caption_ComesFromTheAltText_WhenTheTitleAsksForIt()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## C\n\n![Orders by month](diagram.png \"caption\") ![Sales](photo.png)\n", Images)));
        var slide = Samples.Slides(package).Single();

        var texts = slide.Slide!.Descendants<P.Shape>().Select(s => s.InnerText).ToList();
        Assert.Contains("Orders by month", texts);
        Assert.DoesNotContain("Sales", texts);
    }

    [Fact]
    public void MoreThanFourImages_WrapIntoASecondRow()
    {
        var row = string.Join(" ", Enumerable.Repeat("![x](photo.png)", 5));
        using var package = Samples.Open(Samples.Generate(Samples.Markdown($"## Five\n\n{row}\n", Images)));

        var tops = Pictures(Samples.Slides(package).Single()).Select(p => p.Offset!.Y!.Value).Distinct().ToList();
        Assert.Equal(2, tops.Count);
    }

    [Fact]
    public void Columns_PutTextBesideAnImage_InTheirRatio()
    {
        var pptx = Samples.Generate(Samples.Markdown("## Side by side\n\n::: columns 1:2\nThe text column.\n\n---\n\n![Chart](diagram.png)\n:::\n", Images));
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package).Single();

        var text = slide.Slide!.Descendants<P.Shape>().Single(s => s.InnerText == "The text column.").ShapeProperties!.Transform2D!;
        var picture = Assert.Single(Pictures(slide));
        Assert.True(picture.Offset!.X!.Value > text.Offset!.X!.Value + text.Extents!.Cx!.Value, "The image is right of the text.");
        Assert.True(text.Extents!.Cx!.Value * 2 < picture.Offset!.X!.Value + picture.Extents!.Cx!.Value, "The text column is the narrow one.");
        Assert.Equal(text.Offset!.Y!.Value, picture.Offset!.Y!.Value);
    }

    [Theory]
    [InlineData("## T\n\n::: columns\nOnly one.\n:::\n", "two or three columns")]
    [InlineData("## T\n\n::: columns 1:2:1\nA\n\n---\n\nB\n:::\n", "gives 3 widths for 2 columns")]
    [InlineData("## T\n\n::: columns wide\nA\n\n---\n\nB\n:::\n", "as a ratio of two or three whole numbers")]
    public void ColumnMistakes_Throw(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static List<A.Transform2D> Pictures(SlidePart slide) =>
        slide.Slide!.Descendants<P.Picture>().Select(p => p.ShapeProperties!.Transform2D!).ToList();
}
