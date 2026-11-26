using System.Globalization;
using Markdig.Syntax;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The heading numbers every document format counts alike (<c>headingNumbers: true</c>), E03-S02:
/// <list type="bullet">
/// <item>numbering starts at the highest heading level used after the cover (<see cref="Top"/>): <c>#</c> chapters when
/// there are any, otherwise <c>##</c>; it numbers three levels from there (<c>1</c>, <c>1.2</c>, <c>1.2.3</c>), and
/// deeper headings stay unnumbered;</item>
/// <item>a level skipped in the Markdown never counts as 0: a number has one part per numbered heading above it, so a
/// <c>###</c> straight after a <c>#</c> is <c>1.1</c>, and <c>##</c> sections before the first <c>#</c> are <c>1</c>,
/// <c>2</c>, with the <c>#</c> after them <c>3</c>;</item>
/// <item>a <c>{.unnumbered}</c> heading, and a heading inside a slide-layout container, gets no number and does not
/// count: such headings are never passed to <see cref="Next"/>.</item>
/// </list>
/// From <see cref="StartAppendices"/> on, the first part is a letter: <c>A</c>, <c>A.1</c>.
/// One instance numbers one document, in document order.
/// </summary>
internal sealed class HeadingNumbering(int top)
{
    /// <summary>How many levels are numbered, from <see cref="Top"/> on.</summary>
    public const int Levels = 3;

    // The Markdown levels of the numbered headings the next one may be under, outermost first.
    private readonly List<int> open = [];
    private readonly int[] counters = new int[Levels];

    /// <summary>
    /// The highest (smallest) level of the headings after the cover, those inside slide-layout containers left out; 1
    /// when there are none.
    /// </summary>
    public static int Top(IEnumerable<Block> blocksAfterCover)
    {
        var levels = blocksAfterCover
            .SelectMany(b => b is HeadingBlock h ? [h] : b is ContainerBlock c ? c.Descendants<HeadingBlock>() : [])
            .Where(h => !InLayoutContainer(h))
            .Select(h => h.Level)
            .ToList();
        return levels.Count == 0 ? 1 : levels.Min();
    }

    /// <summary>Whether <paramref name="block"/> is inside a <c>:::</c> container, where a heading is no chapter.</summary>
    public static bool InLayoutContainer(Block block)
    {
        for (var parent = block.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is Markdig.Extensions.CustomContainers.CustomContainer)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts a heading of Markdown <paramref name="level"/>: its number (<c>"1.2"</c>) and depth (1 for <c>"1"</c>), or
    /// <see langword="null"/> for a heading too deep to be numbered, which leaves the counting as it was.
    /// </summary>
    public (string Number, int Depth)? Next(int level)
    {
        if (level >= top + Levels)
        {
            return null;
        }

        // A heading closes every numbered heading at its level or below; the depth is the number of those still open.
        while (this.open.Count > 0 && this.open[^1] >= level)
        {
            this.open.RemoveAt(this.open.Count - 1);
        }

        var depth = this.open.Count + 1;
        this.counters[depth - 1]++;
        for (var deeper = depth; deeper < Levels; deeper++)
        {
            this.counters[deeper] = 0;
        }

        this.open.Add(level);
        return (string.Join(".", this.counters.Take(depth).Select((n, i) => i == 0 && this.appendices ? BackMatter.Letter(n) : n.ToString(CultureInfo.InvariantCulture))), depth);
    }

    // From the first appendix on, the first part of a number is a letter (E06-S07).
    private bool appendices;

    /// <summary>
    /// The appendices start (<c>{.appendix}</c>, E06-S07): the next top-level number is <c>A</c>, then <c>B</c>, and the
    /// numbers below them <c>A.1</c>, <c>A.1.1</c>.
    /// </summary>
    public void StartAppendices()
    {
        this.appendices = true;
        this.open.Clear();
        Array.Clear(this.counters);
    }
}
