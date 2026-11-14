using Markdig;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class CaptionSyntaxTests
{
    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Pptx)]
    public void Unnumbered_IsKnownToEveryFormat(DocumentFormat format)
    {
        var (text, attributes) = HeadingAttributes.Split("Preface {.unnumbered}", format);

        Assert.Equal("Preface", text);
        Assert.True(attributes!.Unnumbered);
    }

    [Fact]
    public void DeepHeadings_LoseOnlyBracesWhoseEveryTokenApplies()
    {
        Assert.Equal(("Detail", true), Deep("Detail {.unnumbered}", DocumentFormat.Docx));
        Assert.Equal(("Detail {.dark}", false), Deep("Detail {.dark}", DocumentFormat.Docx));
        Assert.Equal(("Detail {.unnumbered .dark}", false), Deep("Detail {.unnumbered .dark}", DocumentFormat.Docx));
        Assert.Equal(("Detail", true), Deep("Detail {.unnumbered}", DocumentFormat.Pdf));
        Assert.Equal(("Detail", true), Deep("Detail {.unnumbered}", DocumentFormat.Pptx));

        // `.newpage` below `##` is Word's only, until E03-S03.
        Assert.Equal(("Detail", false), Deep("Detail {.newpage}", DocumentFormat.Docx));
        Assert.Equal(("Detail {.newpage}", false), Deep("Detail {.newpage}", DocumentFormat.Pdf));

        static (string, bool) Deep(string text, DocumentFormat format) =>
            HeadingAttributes.SplitDeep(text, format) is var (rest, attributes) ? (rest, attributes?.Unnumbered == true) : default;
    }

    [Fact]
    public void ACaptionParagraph_IsKnownByItsEnglishPrefix_WhichIsTakenOff()
    {
        var markdown = Markdown.Parse("Table: Sales\n\nFigure: Flow\n\nTabelle: nicht\n");
        var paragraphs = markdown.OfType<ParagraphBlock>().ToList();

        Assert.Equal([CaptionKind.Table, CaptionKind.Figure, null], paragraphs.Select(Captions.Kind));
        Captions.TakePrefix(paragraphs[0], CaptionKind.Table);
        Assert.Equal("Sales", paragraphs[0].Inline!.FirstChild!.ToString());
        Assert.Equal(("Table", "Abbildung"), (Captions.Label(CaptionKind.Table, GeneratedTexts.English), Captions.Label(CaptionKind.Figure, GeneratedTexts.German)));
    }
}
