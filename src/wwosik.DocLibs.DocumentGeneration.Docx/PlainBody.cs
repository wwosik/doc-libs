using DocumentFormat.OpenXml;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The body written plainly: each heading as a paragraph in the style of its Markdown level, without numbers or
/// bookmarks, and every other block as its plain text in <c>Normal</c> paragraphs — a table row by row, cells separated
/// by <c>|</c>, code line by line. Containers, lists and quotes give their content in order; rules are left out.
/// </summary>
internal sealed class PlainBody : IBodyWriter
{
    public static PlainBody Instance { get; } = new();

    public (IReadOnlyList<OpenXmlElement> Elements, IReadOnlyList<TocEntry> Headings) Write(IReadOnlyList<Block> blocks, DocxContext context)
    {
        var elements = new List<OpenXmlElement>();
        var headings = new List<TocEntry>();
        var normal = context.Styles.Id(DocxRoles.Normal);

        void Add(Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var text = DocxContent.HeadingText(heading);
                    var level = Math.Clamp(heading.Level, 1, 6);
                    elements.Add(DocxWriter.Paragraph(context.Styles.Id(DocxRoles.Headings[level - 1]), [new TextPiece(text)]));
                    headings.Add(new TocEntry(level, text));
                    break;
                case ParagraphBlock paragraph when BuildMarker.IsMarker(paragraph):
                    break;
                case Table table:
                    foreach (var row in table.OfType<TableRow>())
                    {
                        var cells = row.OfType<TableCell>().Select(c => string.Join(" ", c.OfType<LeafBlock>().Select(Text)));
                        elements.Add(DocxWriter.Paragraph(normal, [new TextPiece(string.Join(" | ", cells))]));
                    }

                    break;
                case LeafBlock { Inline: { } inline }:
                    if (DocxContent.Pieces(inline) is { Count: > 0 } pieces)
                    {
                        elements.Add(DocxWriter.Paragraph(normal, pieces));
                    }

                    break;
                case ThematicBreakBlock or LinkReferenceDefinitionGroup:
                    break;
                case LeafBlock leaf:
                    var lines = leaf.Lines.Lines.Take(leaf.Lines.Count).Select(l => l.Slice.ToString()).ToList();
                    if (lines.Count > 0)
                    {
                        elements.Add(DocxWriter.Paragraph(normal, lines.SelectMany((l, i) => i == 0 ? [new TextPiece(l)] : new[] { TextPiece.Break, new TextPiece(l) })));
                    }

                    break;
                case ContainerBlock container:
                    foreach (var child in container)
                    {
                        Add(child);
                    }

                    break;
            }
        }

        foreach (var block in blocks)
        {
            Add(block);
        }

        return (elements, headings);
    }

    private static string Text(LeafBlock leaf) => string.Concat(DocxContent.Pieces(leaf.Inline).Select(p => p.IsBreak ? " " : p.Text));
}
