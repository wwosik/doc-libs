using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The shared syntax of documents that E09-S02 adds for E03-S03 and E03-S04: page breaks and the classification marking.</summary>
public class DocumentSyntaxTests
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    [Fact]
    public void PageBreaks_AreTakenApart_AndNameTheBlockTheyComeBefore()
    {
        var document = Markdown.Parse("A\n\n::: pagebreak\n:::\n\nB\n\n::: pagebreak\n## Held\n\nC\n:::\n\nD\n\n::: pagebreak\n\n## Unclosed\n\nE\n", Pipeline);

        var before = PageBreaks.Flatten(document, DocumentFormat.Docx);

        Assert.DoesNotContain(document.Descendants<CustomContainer>(), c => c.Info == LayoutSyntax.PageBreak);
        Assert.Equal(["A", "B", "Held", "C", "D", "Unclosed", "E"], document.Select(Text));
        Assert.Equal(["B", "Held", "Unclosed"], document.Where(before.Contains).Select(Text));
        Assert.All(document, b => Assert.Same(document, b.Parent));
    }

    [Fact]
    public void APageBreak_AtTheEnd_ComesBeforeNothing_AndOneInsideAContainer_BeforeTheNextBlockOutside()
    {
        var atEnd = Markdown.Parse("A\n\n::: pagebreak\n:::\n", Pipeline);
        var inside = Markdown.Parse(":::: panel\nA\n\n::: pagebreak\n:::\n::::\n\nB\n", Pipeline);

        Assert.Empty(PageBreaks.Flatten(atEnd, DocumentFormat.Pdf));
        Assert.Equal("B", Text(Assert.Single(PageBreaks.Flatten(inside, DocumentFormat.Pdf))));
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Pptx)]
    [InlineData(DocumentFormat.Docx)]
    public void APageBreak_TakesNoArguments(DocumentFormat format)
    {
        var document = Markdown.Parse("::: pagebreak soon\n:::\n", Pipeline);

        var error = Assert.Throws<DocumentConversionException>(() => PageBreaks.Flatten(document, format));

        Assert.Contains("'::: pagebreak soon' takes no arguments", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Pptx)]
    [InlineData(DocumentFormat.Docx)]
    public void NewPage_IsAHeadingAttribute_OfEveryFormat(DocumentFormat format)
    {
        var (text, attributes) = HeadingAttributes.Split("Appendix {.newpage kicker=\"End\"}", format);

        Assert.Equal("Appendix", text);
        Assert.True(attributes!.NewPage);
        Assert.Equal("End", attributes.Kicker);
    }

    [Theory]
    [InlineData(DocumentFormat.Docx)]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Pptx)]
    public void NewPage_OnDeepHeadings_IsKnownToEveryFormat(DocumentFormat format)
    {
        var (text, attributes) = HeadingAttributes.SplitDeep("Detail {.newpage .unnumbered}", format);

        Assert.Equal("Detail", text);
        Assert.True(attributes!.NewPage);
        Assert.True(attributes.Unnumbered);
        Assert.Equal(("Detail {.newpage .dark}", null), HeadingAttributes.SplitDeep("Detail {.newpage .dark}", format));
    }

    [Fact]
    public void Justify_IsADocumentKey()
    {
        Assert.Contains(FrontMatter.JustifyKey, FrontMatter.DocumentKeys);
    }

    [Theory]
    [InlineData(Classification.Public, false, "classification: confidential", "Confidential", false)]
    [InlineData(Classification.Confidential, false, "classification: internal", "Confidential", true)]
    [InlineData(Classification.Internal, false, "classification: internal\ndraft: true", "Internal · Draft", false)]
    [InlineData(Classification.Public, true, "draft: false", "Draft", false)]
    [InlineData(Classification.Public, false, "classification: public", "", false)]
    public void Marking_TakesTheHigherClassification_AndADraftFromEitherSide(Classification host, bool hostDraft, string keys, string text, bool kept)
    {
        var notes = new GenerationNotes();
        var frontMatter = FrontMatter.Read($"---\ntitle: x\n{keys}\n---\nBody\n", DocumentFormat.Docx);

        var marking = Marking.Resolve(frontMatter, host, hostDraft, notes, DocumentFormat.Docx);

        Assert.Equal(text, marking.Text(GeneratedTexts.English));
        Assert.Equal(text.Length == 0, marking.IsEmpty);
        var note = notes.Result(0).Notes.SingleOrDefault();
        Assert.Equal(kept, note is not null);
        if (note is not null)
        {
            Assert.Equal((NoteCodes.ClassificationKept, 3), (note.Code, note.Line));
        }
    }

    [Fact]
    public void Marking_InGerman_AndAnUnknownValue()
    {
        var marking = new Marking(Classification.Confidential, true);

        Assert.Equal("Vertraulich · Entwurf", marking.Text(GeneratedTexts.German));
        Assert.Equal("Intern", new Marking(Classification.Internal, false).Text(GeneratedTexts.German));
        var error = Assert.Throws<DocumentConversionException>(() => Marking.Resolve(
            FrontMatter.Read("---\nclassification: secret\n---\n", DocumentFormat.Pdf), Classification.Public, false, new GenerationNotes(), DocumentFormat.Pdf));
        Assert.Contains("expected 'public' or 'internal' or 'confidential'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkingKeys_AreShared_SoNoTemplateSeesThem()
    {
        var frontMatter = FrontMatter.Read("---\nclassification: internal\ndraft: true\n---\n", DocumentFormat.Pdf);

        var options = frontMatter.Apply("options", (o, _) => o, "'a'", _ => "a", _ => [], (_, key, _) => throw new InvalidOperationException(key));

        Assert.Equal("options", options);
    }

    private static string Text(Block block) => block switch
    {
        HeadingBlock heading => heading.Inline!.FirstChild!.ToString()!,
        ParagraphBlock paragraph => paragraph.Inline!.FirstChild!.ToString()!,
        _ => block.GetType().Name,
    };

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }
}
