using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Column widths and font sizes of tables on slides.</summary>
public class TableLayoutTests
{
    private const long CellInsets = 91440 * 2;

    private const string Wide = """
        ## Bookings

        | Area | Plan | Actual | Value this month against the plan | Forecast | Status at month end |
        | --- | --: | --: | --: | --: | --: |
        | Online shop and mail order | ==48,215== | 39,870 | -8,345 | 41,002 | 12,431 |
        | Public sector, schools and libraries (with support) | ==31,904== | 27,113 | -4,791 | 26,380 | 5,445 |
        """;

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void NoColumn_IsNarrowerThanItsLongestWord(string template)
    {
        var (table, size) = TableOf(Samples.Generate(Samples.Markdown(Wide), new PptxOptions { Template = Samples.BuiltIn(template) }));

        var widths = table.TableGrid!.Elements<A.GridColumn>().Select(c => c.Width!.Value).ToList();
        var rows = table.Elements<A.TableRow>().ToList();
        for (var c = 0; c < widths.Count; c++)
        {
            var longest = rows.Select((row, r) => Words(row, c).Max(w => TextMetrics.Width(w.Text, size, w.Bold || r == 0))).Max();
            Assert.True(widths[c] >= longest + CellInsets, $"Column {c} is {widths[c]} EMU, its longest word needs {longest + CellInsets}.");
        }
    }

    [Fact]
    public void AMarkedCell_IsHighlightedAlone_AndKeepsItsColumn()
    {
        var (table, size) = TableOf(Samples.Generate(Samples.Markdown(Wide)));

        var row = table.Elements<A.TableRow>().ElementAt(1);
        var cells = row.Elements<A.TableCell>().ToList();
        Assert.All(cells, cell => Assert.Null(cell.TableCellProperties!.GetFirstChild<A.SolidFill>()));
        var marked = cells[1].Descendants<A.RunProperties>().Single();
        Assert.NotNull(marked.GetFirstChild<A.Highlight>());
        var width = table.TableGrid!.Elements<A.GridColumn>().ElementAt(1).Width!.Value;
        Assert.True(width >= TextMetrics.Width("48,215", size, bold: true) + CellInsets);
    }

    [Fact]
    public void ATableTooWideForItsWords_StepsItsFontDown()
    {
        var header = string.Join(" | ", Enumerable.Range(1, 9).Select(i => $"Heading{i}"));
        var cells = string.Join(" | ", Enumerable.Range(1, 9).Select(i => $"Value{i:000000}"));
        var markdown = $"## T\n\n| {header} |\n|{string.Concat(Enumerable.Repeat(" --- |", 9))}\n| {cells} |\n";

        var (_, size) = TableOf(Samples.Generate(Samples.Markdown(markdown)));

        Assert.True(size < 1600, $"{size}");
        Assert.True(size >= 1000, $"{size}");
    }

    [Fact]
    public void Lines_WrapBetweenWords_AndBreakOnlyAWordWiderThanTheLine()
    {
        var size = 1600;
        var word = TextMetrics.Width("abcdef", size, bold: false);

        Assert.Equal(1, TextMetrics.Lines([new TextRun("abcdef abcdef", RunStyle.None)], size, false, word * 3));
        Assert.Equal(2, TextMetrics.Lines([new TextRun("abcdef abcdef", RunStyle.None)], size, false, word * 1.5));
        Assert.Equal(2, TextMetrics.Lines([new TextRun("abcdef", RunStyle.None), TextRun.Break, new TextRun("x", RunStyle.None)], size, false, word * 3));
        Assert.True(TextMetrics.Width("88", size, bold: true) > TextMetrics.Width("88", size, bold: false));
    }

    private static (A.Table Table, int Size) TableOf(byte[] pptx)
    {
        using var package = Samples.Open(pptx);
        var table = (A.Table)Samples.Slides(package).SelectMany(s => s.Slide!.Descendants<A.Table>()).Single().CloneNode(true);
        var size = table.Descendants<A.RunProperties>().First().FontSize!.Value;
        return (table, size);
    }

    private static List<(string Text, bool Bold)> Words(A.TableRow row, int column) =>
        row.Elements<A.TableCell>().ElementAt(column).Descendants<A.Run>()
            .SelectMany(run => run.Text!.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => (w, run.RunProperties?.Bold?.Value == true)))
            .DefaultIfEmpty((string.Empty, false))
            .ToList();
}
