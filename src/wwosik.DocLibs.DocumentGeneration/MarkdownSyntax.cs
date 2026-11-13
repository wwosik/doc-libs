using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>The Markdown both generators understand beyond CommonMark, and the checks on it they share.</summary>
internal static partial class MarkdownSyntax
{
    /// <summary>Footnotes (<c>[^1]</c>), task lists (<c>- [ ]</c>) and definition lists (<c>Term</c> / <c>:   text</c>).</summary>
    public static MarkdownPipelineBuilder UseDocumentSyntax(this MarkdownPipelineBuilder builder) =>
        builder.UseFootnotes().UseTaskLists().UseDefinitionLists();

    /// <summary>
    /// Reports each footnote reference without a definition, which Markdig leaves as text, with the line of its block
    /// from <paramref name="lineOf"/>. Code spans are not looked into.
    /// </summary>
    public static void ReportMissingFootnotes(Markdig.Syntax.MarkdownDocument markdown, Func<MarkdownObject, int> lineOf, GenerationNotes notes)
    {
        foreach (var leaf in markdown.Descendants<LeafBlock>())
        {
            if (leaf.Inline is null)
            {
                continue;
            }

            foreach (var container in new[] { leaf.Inline }.Concat(leaf.Inline.Descendants<ContainerInline>()))
            {
                var text = new StringBuilder();
                foreach (var inline in container)
                {
                    text.Append(inline is LiteralInline literal ? literal.Content.ToString() : "\u0000");
                }

                foreach (Match match in ReferencePattern().Matches(text.ToString()))
                {
                    notes.Add(
                        NoteCodes.FootnoteMissing,
                        lineOf(leaf),
                        $"The footnote '{match.Value}' has no definition '{match.Value}: text', so it is shown as written.");
                }
            }
        }
    }

    [GeneratedRegex(@"\[\^[^\]\s\u0000]+\]", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
