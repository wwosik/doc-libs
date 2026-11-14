using System.Text;
using UglyToad.PdfPig.Content;

namespace wwosik.DocLibs.DocumentConversions.Pdf;

public sealed partial class PdfConverter
{
    // Rule ends and crossings within this distance meet.
    private const double Touch = 1.5;

    /// <summary>A table found on the page: where it is, and its cells as Markdown, the first row being the header.</summary>
    private sealed record FoundTable(Area Area, List<string[]> Rows);

    // Full grids first, then tables ruled only horizontally. Returns the tables and the words outside them.
    private static (List<PageBlock> Found, List<Word> Outside) Tables(PageGraphics graphics, List<Word> words, double body)
    {
        var found = new List<FoundTable>();
        var horizontal = graphics.Horizontal.Where(r => r.Length >= 10).ToList();
        var vertical = graphics.Vertical.Where(r => r.Length >= 4).ToList();
        foreach (var (h, v) in Crossing(horizontal, vertical))
        {
            if (Grid(h, v, words) is { } table)
            {
                found.Add(table);
            }
        }

        var free = horizontal.Where(r => r.Length >= body * 5 && !found.Any(t => t.Area.Contains((r.From + r.To) / 2, r.At, Touch))).ToList();
        foreach (var group in SameEnds(free))
        {
            if (Ruled(group, vertical, words, body) is { } table && !found.Any(t => Overlaps(t.Area, table.Area)))
            {
                found.Add(table);
            }
        }

        var rest = words.Where(w => !found.Any(t => t.Area.Contains(Centre(w).X, Centre(w).Y))).ToList();
        var blocks = found.Select(t => new PageBlock(t.Area.Top, BlockKind.Table, MarkdownText.Table([.. t.Rows]).TrimEnd())).ToList();
        return (blocks, rest);
    }

    private static (double X, double Y) Centre(Word word) =>
        ((word.BoundingBox.Left + word.BoundingBox.Right) / 2, (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2);

    private static bool Overlaps(Area a, Area b) => a.Left < b.Right && b.Left < a.Right && a.Bottom < b.Top && b.Bottom < a.Top;

    // Groups of horizontal and vertical rules connected by crossing each other.
    private static List<(List<Rule> Horizontal, List<Rule> Vertical)> Crossing(List<Rule> horizontal, List<Rule> vertical)
    {
        var parent = Enumerable.Range(0, horizontal.Count + vertical.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        for (var h = 0; h < horizontal.Count; h++)
        {
            for (var v = 0; v < vertical.Count; v++)
            {
                var (row, column) = (horizontal[h], vertical[v]);
                if (column.At >= row.From - Touch && column.At <= row.To + Touch && row.At >= column.From - Touch && row.At <= column.To + Touch)
                {
                    parent[Find(h)] = Find(horizontal.Count + v);
                }
            }
        }

        return [.. Enumerable.Range(0, parent.Length)
            .GroupBy(Find)
            .Select(g => (
                g.Where(i => i < horizontal.Count).Select(i => horizontal[i]).ToList(),
                g.Where(i => i >= horizontal.Count).Select(i => vertical[i - horizontal.Count]).ToList()))
            .Where(g => g.Item1.Count >= 2 && g.Item2.Count >= 2)];
    }

    // Rules that cross into at least 2 × 2 cells, framed all round. Horizontal lines must span the grid; a vertical
    // one missing from a row merges the cells on both sides of it.
    private static FoundTable? Grid(List<Rule> horizontal, List<Rule> vertical, List<Word> words)
    {
        var (left, right) = (vertical.Min(r => r.At), vertical.Max(r => r.At));
        var (bottom, top) = (horizontal.Min(r => r.At), horizontal.Max(r => r.At));
        var ys = Cluster(horizontal.Where(r => r.From <= left + Touch && r.To >= right - Touch).Select(r => r.At)).OrderDescending().ToList();
        var xs = Cluster(vertical.Select(r => r.At)).Order().ToList();
        if (ys.Count < 3 || xs.Count < 3 || Math.Abs(ys[0] - top) > Touch || Math.Abs(ys[^1] - bottom) > Touch)
        {
            return null;
        }

        bool Covers(double x, double y) => vertical.Any(r => Math.Abs(r.At - x) <= Touch && r.From <= y + Touch && r.To >= y - Touch);
        var rows = ys.Count - 1;
        var columns = xs.Count - 1;
        var owner = new int[rows, columns];
        for (var r = 0; r < rows; r++)
        {
            var middle = (ys[r] + ys[r + 1]) / 2;
            if (!Covers(xs[0], middle) || !Covers(xs[^1], middle))
            {
                return null;
            }

            for (var c = 0; c < columns; c++)
            {
                owner[r, c] = c == 0 || Covers(xs[c], middle) ? c : owner[r, c - 1];
            }
        }

        var area = new Area(left, bottom, right, top);
        var inside = words.Where(w => area.Contains(Centre(w).X, Centre(w).Y)).ToList();
        if (inside.Count == 0)
        {
            return null;
        }

        return new FoundTable(area, Cells(BuildLines(inside), rows, columns, (_, token) =>
        {
            var (x, y) = ((token.Left + token.Right) / 2, (token.Bottom + token.Top) / 2);
            var r = Math.Clamp(ys.FindIndex(v => v < y) - 1, 0, rows - 1);
            var c = Math.Clamp(xs.FindIndex(v => v > x) - 1, 0, columns - 1);
            return (r, owner[r, c]);
        }));
    }

    // Three or more horizontal rules with the same ends and no vertical rule between them, around text that falls
    // into columns: x ranges that no word of the block crosses.
    private static FoundTable? Ruled(List<Rule> rules, List<Rule> vertical, List<Word> words, double body)
    {
        rules = [.. rules.OrderByDescending(r => r.At)];
        var (from, to) = (rules.Average(r => r.From), rules.Average(r => r.To));
        var (top, bottom) = (rules[0].At, rules[^1].At);
        if (vertical.Any(v => v.At > from + Touch && v.At < to - Touch && v.From < top - Touch && v.To > bottom + Touch))
        {
            return null;
        }

        var area = new Area(from - Touch, bottom, to + Touch, top);
        var inside = words.Where(w => area.Contains(Centre(w).X, Centre(w).Y) && !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (inside.Count == 0)
        {
            return null;
        }

        var spans = new List<(double Left, double Right)>();
        foreach (var word in inside.OrderBy(w => w.BoundingBox.Left))
        {
            if (spans.Count > 0 && word.BoundingBox.Left < spans[^1].Right + (body * 0.8))
            {
                spans[^1] = (spans[^1].Left, Math.Max(spans[^1].Right, word.BoundingBox.Right));
            }
            else
            {
                spans.Add((word.BoundingBox.Left, word.BoundingBox.Right));
            }
        }

        if (spans.Count < 2)
        {
            return null;
        }

        // The header is the first band. With one band below it, each of its lines is a row; else each band is one.
        var lines = BuildLines(inside);
        int Band(double y) => Math.Clamp(rules.FindIndex(r => r.At < y) - 1, 0, rules.Count - 2);
        var bodyLines = lines.Where(l => Band(l.CentreY) == 1).ToList();
        var perLine = rules.Count == 3;
        var rowCount = perLine ? 1 + bodyLines.Count : rules.Count - 1;
        if (rowCount < 3)
        {
            return null;
        }

        var rows = Cells(lines, rowCount, spans.Count, (line, token) =>
        {
            var x = (token.Left + token.Right) / 2;
            var column = spans.FindIndex(s => x <= s.Right + (body * 0.4));
            var band = Band(line.CentreY);
            var row = perLine && band == 1 ? 1 + bodyLines.IndexOf(line) : band;
            return (row, column < 0 ? spans.Count - 1 : column);
        });
        rows = [rows[0], .. rows.Skip(1).Where(r => r.Any(c => c.Length > 0))];
        var filled = rows.Skip(1).Count(r => r.Count(c => c.Length > 0) >= 2);
        return rows.Count >= 3 && filled * 2 >= rows.Count - 1 ? new FoundTable(area, rows) : null;
    }

    // Each cell's lines, top to bottom, joined with a space (words hyphenated across lines rejoined), escaped.
    private static List<string[]> Cells(List<Line> lines, int rows, int columns, Func<Line, Token, (int Row, int Column)> locate)
    {
        var cells = new StringBuilder[rows, columns];
        foreach (var line in lines)
        {
            foreach (var group in line.Tokens.GroupBy(t => locate(line, t)))
            {
                var (r, c) = group.Key;
                if (r < 0 || r >= rows || c < 0 || c >= columns)
                {
                    continue;
                }

                Join(cells[r, c] ??= new StringBuilder(), Line.Join(group));
            }
        }

        return [.. Enumerable.Range(0, rows).Select(r => Enumerable.Range(0, columns)
            .Select(c => MarkdownText.Escape(cells[r, c]?.ToString().Trim() ?? string.Empty))
            .ToArray())];
    }

    // Horizontal rules with the same left and right ends, at least three of them.
    private static IEnumerable<List<Rule>> SameEnds(List<Rule> rules)
    {
        var groups = new List<List<Rule>>();
        foreach (var rule in rules.OrderBy(r => r.From))
        {
            var group = groups.FirstOrDefault(g => Math.Abs(g[0].From - rule.From) <= 2 && Math.Abs(g[0].To - rule.To) <= 2);
            if (group is null)
            {
                groups.Add([rule]);
            }
            else
            {
                group.Add(rule);
            }
        }

        return groups.Where(g => g.Count >= 3);
    }

    private static List<double> Cluster(IEnumerable<double> values)
    {
        var result = new List<List<double>>();
        foreach (var value in values.Order())
        {
            if (result.Count > 0 && value - result[^1][0] <= Touch)
            {
                result[^1].Add(value);
            }
            else
            {
                result.Add([value]);
            }
        }

        return [.. result.Select(g => g.Average())];
    }
}
