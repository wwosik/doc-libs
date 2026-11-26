using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Backup slides (E06-S07): valid decks, the footer word on templates with and without a footer placeholder.</summary>
public class BackupSlideTests
{
    private const string Deck = """
        # Review

        ## Main

        Text.

        # Thank you {.closing}

        # Backup {.backup}

        ## Spare

        Text.
        """;

    private static List<SlidePart> Slides(byte[] pptx, out PresentationDocument document)
    {
        document = PresentationDocument.Open(new MemoryStream(pptx), false);
        var part = document.PresentationPart!;
        return [.. part.Presentation!.SlideIdList!.Elements<P.SlideId>().Select(id => (SlidePart)part.GetPartById(id.RelationshipId!.Value!))];
    }

    private static string Text(SlidePart slide, string name) =>
        string.Concat(slide.Slide!.Descendants<P.Shape>().Where(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value is { } shape && (shape == name || shape.StartsWith(name + " ", StringComparison.Ordinal))).SelectMany(s => s.Descendants<A.Text>()).Select(t => t.Text));

    [Fact]
    public void BuiltIn_MarksTheBackupSlides_WithATextBoxOnTheFooterLine()
    {
        var pptx = Samples.Generate(Samples.Markdown(Deck), new PptxOptions { Template = new BrandTemplateOptions { ShowDate = true } });
        Samples.AssertValid(pptx);
        var slides = Slides(pptx, out var document);
        using (document)
        {
            Assert.Equal(5, slides.Count);
            Assert.Equal("Backup", slides[3].Slide!.CommonSlideData!.Name?.Value);
            Assert.Null(slides[4].Slide!.CommonSlideData!.Name?.Value);
            Assert.Equal(["", "", "", "Backup", "Backup"], slides.Select(s => Text(s, "Backup")));

            var date = slides[4].Slide!.Descendants<P.Shape>().Single(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value?.StartsWith("Date ", StringComparison.Ordinal) == true);
            var mark = slides[4].Slide!.Descendants<P.Shape>().Single(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value == "Backup");
            Assert.True(mark.ShapeProperties!.Transform2D!.Offset!.X!.Value > 0);
            Assert.Null(date.ShapeProperties?.Transform2D);
        }
    }

    [Fact]
    public void FooterPlaceholder_GetsTheWordAfterTheFooterText()
    {
        var pptx = Samples.Generate(Samples.Markdown(Deck), DarkTemplate.Options);
        Samples.AssertValid(pptx);
        var slides = Slides(pptx, out var document);
        using (document)
        {
            Assert.Equal("Made-up Corporation", Text(slides[1], "Footer"));
            Assert.Equal("Made-up Corporation · Backup", Text(slides[3], "Footer"));
            Assert.Equal("Made-up Corporation · Backup", Text(slides[4], "Footer"));
            Assert.All(slides, s => Assert.Equal(string.Empty, Text(s, "Backup")));
        }
    }

    [Fact]
    public void GermanDeck_SaysBackup_InTheFooter_AndReadsBack()
    {
        var pptx = Samples.Generate(Samples.Markdown("---\nlang: de\n---\n\n" + Deck), DarkTemplate.Options);
        var slides = Slides(pptx, out var document);
        using (document)
        {
            Assert.Equal("Made-up Corporation · Backup", Text(slides[4], "Footer"));
        }

        var markdown = new DocumentConversions.OpenXml.PptxConverter().Convert(new MemoryStream(pptx), DocumentConversions.ConversionOptions.Default).Markdown;
        Assert.Contains("# Backup {.backup}", markdown, StringComparison.Ordinal);
    }
}
