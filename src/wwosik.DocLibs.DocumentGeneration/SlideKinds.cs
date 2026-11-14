using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// What both generators check alike about slide kinds (<c>## Agenda {.agenda}</c> and the others) and the containers
/// <c>::: conclusion</c> and <c>::: comment</c>: decks put them on their layouts, PDFs show their content plainly, and both
/// reject the same mistakes with <see cref="DocumentConversionException"/>.
/// </summary>
internal static class SlideKinds
{
    public const string Agenda = "agenda";
    public const string Quote = "quote";
    public const string Statement = "statement";
    public const string Contact = "contact";
    public const string Chart = "chart";

    /// <summary>The container at the foot of a flow slide, <c>::: conclusion</c>.</summary>
    public const string Conclusion = "conclusion";

    /// <summary>The callout over the block before it, <c>::: comment</c>.</summary>
    public const string Comment = "comment";

    /// <summary>The people a contact slide holds; more continue on a further slide.</summary>
    public const int MaxPeople = 5;

    /// <summary>The charts a chart slide holds; more continue on a further slide.</summary>
    public const int MaxCharts = 2;

    /// <summary>The kinds, as their classes name them without the dot.</summary>
    public static readonly IReadOnlyList<string> Names = [Agenda, Quote, Statement, Contact, Chart];

    /// <summary>
    /// The blocks of a section that carry content: without HTML comments, link definitions, <c>---</c>, build markers
    /// and <c>::: notes</c>.
    /// </summary>
    public static List<Block> Content(IEnumerable<Block> blocks) =>
        [.. blocks.Where(b => b is not (HtmlBlock { Type: HtmlBlockType.Comment } or LinkReferenceDefinitionGroup or ThematicBreakBlock)
            && !(b is ParagraphBlock paragraph && BuildMarker.IsMarker(paragraph))
            && !(b is CustomContainer { Info: LayoutSyntax.Notes }))];

    /// <summary>
    /// Checks the blocks under a <c>##</c> heading of <paramref name="kind"/>, the slide titled <paramref name="title"/>:
    /// an agenda holds one list or nothing; a quote a <c>&gt;</c> quote and its byline; a statement text; a contact
    /// slide one list of people; a chart slide <c>chart</c> blocks and text.
    /// </summary>
    /// <exception cref="DocumentConversionException">The blocks do not fit the kind.</exception>
    public static void Check(string kind, IEnumerable<Block> blocks, string title, DocumentFormat format)
    {
        var content = Content(blocks);
        var slide = $"the slide '{title}'";
        switch (kind)
        {
            case Agenda when content is not ([] or [ListBlock]):
                throw new DocumentConversionException(
                    format, $"A '{{.agenda}}' slide holds one list, its points ({slide}); with nothing under its heading it lists the deck's chapters.");
            case Quote when !IsQuote(content):
                throw new DocumentConversionException(
                    format, $"A '{{.quote}}' slide holds a quote ('> …') and, if you like, its byline on a line starting with '—' ({slide}).");
            case Statement when !content.All(b => IsText(b)):
                throw new DocumentConversionException(
                    format, $"A '{{.statement}}' slide is its heading, with text below it if you like: paragraphs and lists ({slide}).");
            case Contact when content is not [ListBlock { Count: > 0 }]:
                throw new DocumentConversionException(
                    format, $"A '{{.contact}}' slide holds one list, an item per person: '- ![Name](photo.jpg) **Name** – role' ({slide}).");
            case Chart when !content.All(b => IsChart(b) || IsText(b) || b is HeadingBlock { Level: >= 3 }) || !content.Any(IsChart):
                throw new DocumentConversionException(
                    format, $"A '{{.chart}}' slide holds one or two ```chart blocks, and text if you like ({slide}).");
        }
    }

    /// <summary>
    /// Checks <c>::: conclusion</c> and <c>::: comment</c> in the blocks of one section: both at its top level and of text
    /// only; a conclusion once, as the last block; a comment right after another block.
    /// </summary>
    /// <exception cref="DocumentConversionException">One of them is misplaced or holds more than text.</exception>
    public static void CheckContainers(IEnumerable<Block> blocks, DocumentFormat format)
    {
        var content = Content(blocks);
        foreach (var block in content)
        {
            var nested = block is ContainerBlock container
                && container.Descendants<CustomContainer>().Any(c => c.Info is Conclusion or Comment);
            if (nested)
            {
                throw new DocumentConversionException(
                    format, "A '::: conclusion' or '::: comment' goes at the top level of a slide, not inside another container, a list or a quote.");
            }
        }

        for (var i = 0; i < content.Count; i++)
        {
            if (content[i] is not CustomContainer { Info: Conclusion or Comment } found)
            {
                continue;
            }

            if (found.Info == Conclusion && i != content.Count - 1)
            {
                throw new DocumentConversionException(
                    format, "A '::: conclusion' goes once on a slide, as its last block: it sits at the foot of the slide.");
            }

            if (found.Info == Comment && i == 0)
            {
                throw new DocumentConversionException(
                    format, "A '::: comment' goes right after the block it comments; it cannot be the first block of a slide.");
            }

            if (!found.All(IsText))
            {
                throw new DocumentConversionException(format, $"A '::: {found.Info}' holds text: paragraphs and lists.");
            }
        }
    }

    /// <summary>A paragraph that starts with <c>—</c>, <c>–</c> or <c>--</c>: the byline of a quote.</summary>
    public static bool IsByline(ParagraphBlock paragraph) =>
        paragraph.Inline?.FirstChild is LiteralInline literal && StartsWithDash(literal.Content.ToString());

    /// <summary>Whether <paramref name="text"/>, spaces aside, starts with <c>—</c>, <c>–</c> or <c>--</c>.</summary>
    public static bool StartsWithDash(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('—') || trimmed.StartsWith('–') || trimmed.StartsWith("--", StringComparison.Ordinal);
    }

    /// <summary><paramref name="text"/> without its leading dash and the spaces around it.</summary>
    public static string WithoutDash(string text)
    {
        var trimmed = text.TrimStart();
        return (trimmed.StartsWith("--", StringComparison.Ordinal) ? trimmed[2..] : trimmed[1..]).TrimStart();
    }

    /// <summary>A fenced <c>chart</c> block.</summary>
    public static bool IsChart(Block block) =>
        block is FencedCodeBlock fenced && (fenced.Info ?? string.Empty).Trim().Equals(Chart, StringComparison.OrdinalIgnoreCase);

    private static bool IsQuote(List<Block> content) =>
        content is [QuoteBlock and not AlertBlock] || (content is [QuoteBlock and not AlertBlock, ParagraphBlock byline] && IsByline(byline));

    // Paragraphs without images, and lists.
    private static bool IsText(Block block) =>
        block is ListBlock || (block is ParagraphBlock paragraph && !(paragraph.Inline?.Descendants<LinkInline>().Any(l => l.IsImage) ?? false));
}
