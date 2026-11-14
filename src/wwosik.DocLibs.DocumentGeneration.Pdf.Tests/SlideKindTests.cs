using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary>Slide kinds, <c>::: conclusion</c> and <c>::: comment</c> as PDFs show them (E02-S05).</summary>
public class SlideKindTests
{
    private static string Text(Block block) => block switch
    {
        HeadingBlock heading => string.Concat(heading.Runs.Select(r => r.Text)),
        ParagraphBlock paragraph => string.Concat(paragraph.Runs.Select(r => r.Text)),
        ListBlock list => string.Join("|", list.Items.Select(i => string.Concat(i.Select(Text)))),
        QuoteBlock quote => string.Concat(quote.Blocks.Select(Text)),
        _ => string.Empty,
    };

    [Fact]
    public void AnEmptyAgenda_ListsTheChapters_ButNotTheCoverOrTheClosingHeading()
    {
        var model = MarkdownParser.Parse(Samples.Markdown("# Deck\n\n## Agenda {.agenda}\n\n# Market\n\nText\n\n# Plan\n\n# Thank you {.closing}\n"), PdfOptions.Default);

        var index = model.Blocks.ToList().FindIndex(b => b is HeadingBlock { Level: 2 } h && Text(h) == "Agenda");
        var list = Assert.IsType<ListBlock>(model.Blocks[index + 1]);
        Assert.True(list.Ordered);
        Assert.Equal("Market|Plan", Text(list));
    }

    [Fact]
    public void AnAgendaWithoutChapters_GetsANote()
    {
        var result = PdfGenerator.Generate(Samples.Markdown("# Deck\n\n## Agenda {.agenda}\n"), new MemoryStream());

        Assert.Equal(NoteCodes.AgendaEmpty, Assert.Single(result.Notes).Code);
    }

    [Fact]
    public void KindsShowTheirContent_AConclusionIsAQuote_AndACommentAnItalicQuoteAfterItsBlock()
    {
        var model = MarkdownParser.Parse(
            Samples.Markdown("## {.quote}\n\n> Ship it.\n> — Ann\n\n## Status\n\nText\n\n::: comment\nMind this.\n:::\n\n::: conclusion\nSo we go.\n:::\n"),
            PdfOptions.Default);

        var blocks = model.Blocks.ToList();
        Assert.IsType<QuoteBlock>(blocks[0]);
        Assert.Equal(["Status", "Text", "Mind this.", "So we go."], blocks.Skip(1).Select(Text));
        var comment = Assert.IsType<QuoteBlock>(blocks[3]);
        Assert.All(((ParagraphBlock)comment.Blocks[0]).Runs, r => Assert.True(r.Style.HasFlag(RunStyle.Italic)));
        var conclusion = Assert.IsType<QuoteBlock>(blocks[4]);
        Assert.DoesNotContain(((ParagraphBlock)conclusion.Blocks[0]).Runs, r => r.Style.HasFlag(RunStyle.Italic));
    }

    [Theory]
    [InlineData("## Q {.quote}\n\nNo quote here.\n", "A '{.quote}' slide holds a quote")]
    [InlineData("## C {.contact}\n\nAnn, Ben\n", "A '{.contact}' slide holds one list")]
    [InlineData("## R {.chart}\n\n| a |\n| - |\n| 1 |\n", "A '{.chart}' slide holds one or two ```chart blocks")]
    [InlineData("## T\n\n::: comment\nNote\n:::\n", "it cannot be the first block")]
    [InlineData("## T\n\n::: conclusion\nSo.\n:::\n\nText\n", "as its last block")]
    [InlineData("# Agenda {.agenda}\n", "{.agenda} goes on a '##' heading")]
    [InlineData("## T {.quote .agenda}\n\n> A\n", "name two kinds of slide")]
    public void Mistakes_Throw_LikeInDecks(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(markdown)));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }
}
