using System.Text;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>The cover of a document that starts with <c>#</c>: the heading's text and each paragraph after it.</summary>
internal sealed record DocxCover(string Title, IReadOnlyList<IReadOnlyList<TextPiece>> Subtitle);

/// <summary>A piece of a paragraph's text: text, or a hard line break (<see cref="IsBreak"/>).</summary>
internal readonly record struct TextPiece(string Text)
{
    public static TextPiece Break { get; } = new("\n");

    public bool IsBreak => this.Text == "\n";
}

/// <summary>
/// The Markdown of a Word document, parsed: the cover from a leading <c>#</c> and the paragraphs after it, as in the
/// PDF, and the blocks of the body. Uses the pipeline of the PDF generator, so that the same Markdown parses alike.
/// </summary>
internal sealed record DocxContent(DocxCover? Cover, IReadOnlyList<Block> Body)
{
    /// <summary>The whole parsed Markdown, for the checks that look at all of it (footnotes, build markers).</summary>
    public Markdig.Syntax.MarkdownDocument Source { get; init; } = new();

    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    public static DocxContent Parse(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);

        // Speaker notes are for decks: checked like there, then left out.
        foreach (var notes in document.OfType<CustomContainer>().Where(c => c.Info == LayoutSyntax.Notes))
        {
            LayoutSyntax.CheckContainer(LayoutSyntax.Notes, notes.Arguments?.Trim() ?? string.Empty, Format.Docx);
            LayoutSyntax.CheckNoNestedNotes(notes, Format.Docx);
        }

        var blocks = document.Where(b => !(b is ParagraphBlock paragraph && BuildMarker.IsMarker(paragraph))
            && b is not CustomContainer { Info: LayoutSyntax.Notes }
            && b is not LinkReferenceDefinitionGroup
            && b is not Markdig.Extensions.Footnotes.FootnoteGroup).ToList();
        if (blocks.FirstOrDefault() is not HeadingBlock { Level: 1 } first)
        {
            return new DocxContent(null, blocks) { Source = document };
        }

        var start = 1;
        var subtitle = new List<IReadOnlyList<TextPiece>>();
        while (start < blocks.Count && blocks[start] is ParagraphBlock paragraph && !paragraph.Inline!.Descendants<LinkInline>().Any(l => l.IsImage))
        {
            subtitle.Add(Pieces(paragraph.Inline));
            start++;
        }

        return new DocxContent(new DocxCover(HeadingText(first), subtitle), blocks[start..]) { Source = document };
    }

    /// <summary>A heading's text without its slide attributes (<c>{.dark}</c> and the others), which are checked as in the PDF.</summary>
    public static string HeadingText(HeadingBlock heading)
    {
        var text = string.Concat(Pieces(heading.Inline).Select(p => p.IsBreak ? " " : p.Text)).Trim();
        return HeadingAttributes.Split(text, Format.Docx).Text.TrimEnd();
    }

    /// <summary>The plain text of inlines: links and emphasis as their text, images as their alt text, hard breaks as breaks.</summary>
    public static IReadOnlyList<TextPiece> Pieces(ContainerInline? inlines)
    {
        var pieces = new List<TextPiece>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0)
            {
                pieces.Add(new TextPiece(text.ToString()));
                text.Clear();
            }
        }

        void Walk(Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case AutolinkInline link:
                    text.Append(link.Url);
                    break;
                case HtmlInline html:
                    text.Append(html.Tag);
                    break;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded.ToString());
                    break;
                case TaskList task:
                    text.Append(task.Checked ? "[x] " : "[ ] ");
                    break;
                case LineBreakInline { IsHard: true }:
                    Flush();
                    pieces.Add(TextPiece.Break);
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                case ContainerInline container:
                    foreach (var child in container)
                    {
                        Walk(child);
                    }

                    break;
            }
        }

        if (inlines is not null)
        {
            Walk(inlines);
        }

        Flush();
        return pieces;
    }

    // The PDF's: tables, strikethrough and marks, autolinks, heading ids, alerts, footnotes, task and definition lists,
    // and custom containers without their inline form (`::text::`), which would take `std::vector::size` apart.
    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough | Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Marked)
            .UseAutoLinks()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
            .UseAlertBlocks()
            .UseDocumentSyntax();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }
}
