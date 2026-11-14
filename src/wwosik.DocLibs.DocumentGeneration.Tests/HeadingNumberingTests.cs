using Markdig;
using Markdig.Syntax;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class HeadingNumberingTests
{
    [Fact]
    public void ThreeLevels_AreNumbered_FromTheTop()
    {
        Assert.Equal(["1", "1.1", "1.1.1", null, "1.2", "2", "2.1"], Numbers(1, 1, 2, 3, 4, 2, 1, 2));
    }

    [Fact]
    public void ASkippedLevel_NeverCountsAsZero()
    {
        // `###` straight after `#`; `##` sections before the first `#`, as in report.md.
        Assert.Equal(["1", "1.1", "1.2", "2", "2.1"], Numbers(1, 1, 3, 3, 1, 3));
        Assert.Equal(["1", "1.1", "2", null, null, null, "3"], Numbers(1, 2, 3, 2, 4, 5, 6, 1));
    }

    [Fact]
    public void WithoutChapters_SectionsAreTheTopLevel()
    {
        Assert.Equal(["1", "1.1", "1.1.1", null, "2"], Numbers(2, 2, 3, 4, 5, 2));
    }

    [Fact]
    public void TheTop_IsTheHighestLevelOutsideLayoutContainers()
    {
        var markdown = Markdown.Parse("## Section\n\n::: cards\n# Not a chapter\n:::\n\n### Detail\n", new MarkdownPipelineBuilder().Use<Markdig.Extensions.CustomContainers.CustomContainerExtension>().Build());

        Assert.Equal(2, HeadingNumbering.Top(markdown));
        Assert.True(HeadingNumbering.InLayoutContainer(markdown.Descendants<HeadingBlock>().Single(h => h.Level == 1)));
        Assert.Equal(1, HeadingNumbering.Top([]));
    }

    private static List<string?> Numbers(int top, params int[] levels)
    {
        var numbering = new HeadingNumbering(top);
        return [.. levels.Select(level => numbering.Next(level)?.Number)];
    }
}
