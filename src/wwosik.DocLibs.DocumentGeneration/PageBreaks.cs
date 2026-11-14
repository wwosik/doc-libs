using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>::: pagebreak</c>, the forced page break of documents (E03-S03's syntax). It takes no arguments and may hold
/// content, which follows the break; its closing <c>:::</c> is optional, so an unclosed one runs to the end of the
/// Markdown and gives the same pages. Every generator takes the containers apart right after parsing, before it looks
/// at sections: their content moves up into the container's place, so that a heading an unclosed break swallowed
/// starts its section as usual. Word documents and PDFs break before the block that then follows, never giving an empty
/// page; decks ignore the break.
/// </summary>
internal static class PageBreaks
{
    /// <summary>
    /// Replaces every <c>::: pagebreak</c> in <paramref name="document"/>, at any depth, by its content. Returns the
    /// blocks a page break comes right before, in document order; a break at the very end comes before none.
    /// </summary>
    /// <exception cref="DocumentConversionException">A <c>::: pagebreak</c> has arguments.</exception>
    public static IReadOnlySet<Block> Flatten(ContainerBlock document, DocumentFormat format)
    {
        var before = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        if (!document.Descendants<CustomContainer>().Any(c => c.Info == LayoutSyntax.PageBreak))
        {
            return before;
        }

        var breaks = new List<(ContainerBlock Parent, int Index)>();
        Flatten(document, format, breaks);

        // The block after each break: the next sibling, else the next sibling of the nearest parent that has one.
        foreach (var (parent, index) in breaks)
        {
            var (container, position) = (parent, index);
            while (position >= container.Count && container.Parent is { } outer)
            {
                position = outer.IndexOf(container) + 1;
                container = outer;
            }

            if (position < container.Count)
            {
                before.Add(container[position]);
            }
        }

        return before;
    }

    // Depth first, so that a break inside a break's content (two unclosed ones) is taken apart too. Each break is
    // recorded as the place its content now starts at.
    private static void Flatten(ContainerBlock container, DocumentFormat format, List<(ContainerBlock Parent, int Index)> breaks)
    {
        for (var i = 0; i < container.Count; i++)
        {
            if (container[i] is not ContainerBlock child)
            {
                continue;
            }

            if (child is CustomContainer { Info: LayoutSyntax.PageBreak } pageBreak)
            {
                LayoutSyntax.CheckContainer(LayoutSyntax.PageBreak, pageBreak.Arguments?.Trim() ?? string.Empty, format);
                var content = pageBreak.ToList();
                pageBreak.Clear();
                container.RemoveAt(i);
                for (var k = 0; k < content.Count; k++)
                {
                    container.Insert(i + k, content[k]);
                }

                breaks.Add((container, i));

                // Its content is looked at again where it now stands.
                i--;
                continue;
            }

            Flatten(child, format, breaks);
        }
    }
}
