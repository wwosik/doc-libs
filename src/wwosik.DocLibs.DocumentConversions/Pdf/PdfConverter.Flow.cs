using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace wwosik.DocLibs.DocumentConversions.Pdf;

public sealed partial class PdfConverter
{
    // Marker positions and list levels within this distance are the same.
    private const double SameX = 2;

    private enum MarkerKind
    {
        Bullet,
        Open,
        Done,
        Ordered,
    }

    /// <summary>A list item's marker, the text after it, and where that text starts.</summary>
    private sealed record Marker(MarkerKind Kind, string Body, double TextLeft, string Number = "")
    {
        public string Prefix => this.Kind switch
        {
            MarkerKind.Open => "- [ ] ",
            MarkerKind.Done => "- [x] ",
            MarkerKind.Ordered => this.Number + " ",
            _ => "- ",
        };

        // Items of different families at one level are separate lists.
        public char Family => this.Kind == MarkerKind.Ordered ? this.Number[^1] : '-';

        // How far a nested item is indented under this one: past the marker, where its text starts.
        public int ContentIndent => this.Kind == MarkerKind.Ordered ? this.Number.Length + 1 : 2;
    }

    // The structured pass reads drawn shapes too (tables, drawn markers, code, footnotes); the plain pass is
    // what the converter did before it: paragraphs, glyph bullets and headings.
    private static PageText Analyse(Page page, bool structured)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.TextOrientation == TextOrientation.Horizontal)
            .ToList();

        // Rotated text (watermarks) counts nowhere.
        var body = page.Letters
            .Where(l => !string.IsNullOrWhiteSpace(l.Value) && l.PointSize > 0 && l.TextOrientation == TextOrientation.Horizontal)
            .GroupBy(l => Math.Round(l.PointSize * 2) / 2)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        var graphics = PageGraphics.Empty;
        var tables = new List<PageBlock>();
        if (structured)
        {
            graphics = PageGraphics.Read(page, body > 0 ? body : 10);
            (tables, words) = Tables(graphics, words, body > 0 ? body : 10);
        }

        var lines = BuildLines(words);
        if (lines.Count == 0)
        {
            return new PageText(tables, []);
        }

        if (body <= 0)
        {
            body = Median(lines.Select(l => l.Size));
        }

        var footnotes = structured ? Footnotes(lines, body, page.Height) : [];
        // Tables and images stand between lines: a paragraph never runs across them.
        var between = tables.Select(t => t.Top).Concat(page.GetImages().Select(i => i.BoundingBox.Top)).ToList();
        var flow = lines.Count == 0 ? [] : Paragraphs(lines, body, page.Height, graphics, structured, between);
        return new PageText(Merge(flow, tables), footnotes);
    }

    private static List<PageBlock> Paragraphs(List<Line> lines, double body, double pageHeight, PageGraphics graphics, bool structured, List<double> between)
    {
        var marginTop = pageHeight * 0.92;
        var marginBottom = pageHeight * 0.08;
        var rights = lines.Select(l => l.Right).OrderBy(x => x).ToList();
        var lefts = lines.Select(l => l.Left).OrderBy(x => x).ToList();
        var textRight = rights[(int)(rights.Count * 0.9)];
        var textLeft = lefts[(int)(lefts.Count * 0.1)];
        var width = Math.Max(textRight - textLeft, 1);
        var gaps = lines.Zip(lines.Skip(1), (a, b) => a.Bottom - b.Top).Where(g => g > 0).ToList();
        var typicalGap = gaps.Count == 0 ? body * 0.3 : Median(gaps);

        var markers = structured ? Markers(lines, body, graphics) : [.. lines.Select(l => StripBullet(l.Text) is { } rest ? new Marker(MarkerKind.Bullet, rest, l.Left) : null)];
        var code = lines.Select((l, i) => structured && markers[i] is null && l.Monospace()).ToArray();

        var output = new List<PageBlock>();
        var current = new StringBuilder();
        var currentLines = new List<Line>();
        Marker? item = null;
        var startGap = 0.0;

        // The open list levels: where each level's text starts, and how far its children are indented.
        var levels = new List<(double X, int Indent, char Family)>();
        var itemIndent = 0;

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length > 0)
            {
                var size = currentLines.Average(l => l.Size);
                var bold = currentLines.All(l => l.Bold);
                var heading = item is null && text.Length < 100
                    && !TableOfContentsEntry().IsMatch(text)
                    && (StartsNumbered(text) || !(text.EndsWith('.') || text.EndsWith(':')))
                    && (size >= body * 1.15 || (bold && StartsNumbered(text)));
                var top = currentLines[0].Top;
                if (item is not null)
                {
                    var indent = 0;
                    var separate = false;
                    if (structured)
                    {
                        while (levels.Count > 0 && item.TextLeft < levels[^1].X - SameX)
                        {
                            levels.RemoveAt(levels.Count - 1);
                        }

                        if (levels.Count > 0 && Math.Abs(item.TextLeft - levels[^1].X) <= SameX)
                        {
                            separate = levels[^1].Family != item.Family;
                            levels.RemoveAt(levels.Count - 1);
                        }

                        indent = levels.Sum(l => l.Indent);
                        levels.Add((item.TextLeft, item.ContentIndent, item.Family));
                    }

                    itemIndent = indent + item.ContentIndent;
                    output.Add(new PageBlock(top, BlockKind.Item, new string(' ', indent) + item.Prefix + Inline(text)) { Separate = separate });
                }
                else if (structured && !heading && levels.Count > 0 && output.Count > 0 && output[^1].Kind == BlockKind.Item
                    && Math.Abs(currentLines[0].Left - levels[^1].X) <= SameX && startGap <= typicalGap + (body * 0.35))
                {
                    // A line set under the item's text that is not part of its paragraph: a hard line break.
                    output[^1] = output[^1] with { Markdown = output[^1].Markdown + "\\\n" + new string(' ', itemIndent) + Inline(text) };
                }
                else
                {
                    var escaped = References(MarkdownText.Escape(text));
                    output.Add(new PageBlock(top, BlockKind.Paragraph, heading ? "### " + escaped : MarkdownText.EscapeLineStart(escaped)));
                    levels.Clear();
                }
            }

            current.Clear();
            currentLines.Clear();
            item = null;
        }

        Line? previous = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (code[i])
            {
                Flush();
                var end = i;
                while (end + 1 < lines.Count && code[end + 1] && SameCode(lines[end], lines[end + 1], graphics))
                {
                    end++;
                }

                output.Add(CodeBlock(lines.GetRange(i, end - i + 1)));
                levels.Clear();
                previous = lines[end];
                i = end;
                continue;
            }

            var text = line.Text;
            var marker = markers[i];
            if (previous is not null)
            {
                var gap = previous.Bottom - line.Top;
                var breakHere = marker is not null
                    || between.Any(y => y < previous.Bottom && y > line.Top)
                    || StartsNumbered(text)
                    || InMargin(previous, marginTop, marginBottom) || InMargin(line, marginTop, marginBottom)
                    || gap > typicalGap + (body * 0.35)
                    || previous.Right < textRight - (width * 0.15)                    // the previous line ended early
                    || line.Left < previous.Left - (body * 1.5)                       // an outdent ends a hanging item
                    || Math.Abs(line.Size - previous.Size) > Math.Max(previous.Size, 1) * 0.15;
                if (breakHere)
                {
                    Flush();
                }
            }

            if (currentLines.Count == 0)
            {
                item = marker;
                startGap = previous is null ? double.MaxValue : previous.Bottom - line.Top;
            }

            Join(current, marker is not null ? marker.Body : text);
            currentLines.Add(line);
            previous = line;
        }

        Flush();
        return output;
    }

    // Item text: escaped, also where it would start a block of its own.
    private static string Inline(string text) => References(MarkdownText.EscapeLineStart(MarkdownText.Escape(text)));

    private static string References(string markdown) => ReferenceMark().Replace(markdown, "[^$1]");

    private static bool InMargin(Line line, double top, double bottom) => line.Top > top || line.Bottom < bottom;

    // Glyph and drawn bullets and boxes; numbered lines with a hanging indent that are continued by the next number,
    // or nested in a list.
    private static Marker?[] Markers(List<Line> lines, double body, PageGraphics graphics)
    {
        var markers = lines.Select(l => Bullet(l, graphics)).ToArray();
        var numbered = lines.Select(l =>
        {
            var match = l.Tokens.Count >= 2 && l.Size < body * 1.15 && !l.Bold ? OrderedMarker().Match(l.Tokens[0].Text) : Match.Empty;
            return match.Success ? new Marker(MarkerKind.Ordered, l.TextFrom(1), l.Tokens[1].Left, match.Value) : null;
        }).ToArray();

        var continued = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (numbered[i] is not { } first || markers[i] is not null)
            {
                continue;
            }

            for (var j = i + 1; j < lines.Count; j++)
            {
                if (numbered[j] is { } next && Math.Abs(next.TextLeft - first.TextLeft) <= SameX)
                {
                    if (Number(next) == Number(first) + 1 && next.Number[^1] == first.Number[^1])
                    {
                        continued[i] = continued[j] = true;
                    }

                    break;
                }

                var start = numbered[j]?.TextLeft ?? markers[j]?.TextLeft ?? lines[j].Left;
                if (start < first.TextLeft - SameX)
                {
                    break;
                }
            }
        }

        double? listX = null;
        for (var i = 0; i < lines.Count; i++)
        {
            if (markers[i] is { } marker)
            {
                listX = marker.TextLeft;
                continue;
            }

            if (numbered[i] is { } number && (continued[i] || (listX is { } x && number.TextLeft > x + SameX)))
            {
                markers[i] = number;
                listX = number.TextLeft;
                continue;
            }

            if (listX is { } open && lines[i].Left < open - SameX)
            {
                listX = null;
            }
        }

        return markers;

        static int Number(Marker marker) => int.Parse(marker.Number.AsSpan(0, marker.Number.Length - 1), CultureInfo.InvariantCulture);
    }

    // A bullet or box glyph starting the line, or a drawn dot or box just left of its first word.
    private static Marker? Bullet(Line line, PageGraphics graphics)
    {
        var text = line.Text;
        if (text.Length >= 2 && text[0] is '☐' or '☑' or '☒')
        {
            return new Marker(text[0] == '☐' ? MarkerKind.Open : MarkerKind.Done, text[1..].TrimStart(), AfterFirstGlyph(line));
        }

        if (StripBullet(text) is { } rest)
        {
            return new Marker(MarkerKind.Bullet, rest, AfterFirstGlyph(line));
        }

        var size = line.Size;
        bool Beside(Area a) => a.Right <= line.Left + 1 && a.Right >= line.Left - (size * 2.5)
            && a.CentreY >= line.Bottom - (size * 0.3) && a.CentreY <= line.Top + (size * 0.2);
        var box = graphics.Boxes.Where(b => Beside(b.Area)).MaxBy(b => b.Area.Right);
        var dot = graphics.Dots.Where(Beside).Select(d => (Area?)d).MaxBy(d => d!.Value.Right);
        if (box is not null && (dot is null || box.Area.Right >= dot.Value.Right))
        {
            return new Marker(box.Ticked ? MarkerKind.Done : MarkerKind.Open, text, line.Left);
        }

        return dot is not null ? new Marker(MarkerKind.Bullet, text, line.Left) : null;
    }

    // Where the text after a one-character marker starts: the next word, or the next letter of a glued-on word.
    private static double AfterFirstGlyph(Line line)
    {
        var first = line.Tokens[0];
        if (first.Letters.Count(l => !string.IsNullOrWhiteSpace(l.Value)) <= 1 && line.Tokens.Count > 1)
        {
            return line.Tokens[1].Left;
        }

        return first.Letters.Skip(1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Value))?.BoundingBox.Left ?? first.Right;
    }

    // Consecutive code lines: close together, or on the same background.
    private static bool SameCode(Line above, Line below, PageGraphics graphics) =>
        above.Baseline - below.Baseline <= Math.Max(above.Size, below.Size) * 2.6
        || graphics.Fills.Any(f => f.Contains(above.CentreX, above.CentreY) && f.Contains(below.CentreX, below.CentreY));

    // A fenced block: leading spaces rebuilt from x at one character width, blank lines from the vertical gaps.
    private static PageBlock CodeBlock(List<Line> lines)
    {
        var letters = lines.SelectMany(l => l.Tokens).SelectMany(t => t.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        var character = Median(letters.Select(l => l.Width));
        if (character <= 0)
        {
            character = lines[0].Size * 0.6;
        }

        var left = lines.Min(l => l.Left);
        var steps = lines.Zip(lines.Skip(1), (a, b) => a.Baseline - b.Baseline).Where(s => s > 0).ToList();
        var pitch = Math.Min(steps.Count == 0 ? double.MaxValue : steps.Min(), lines.Max(l => l.Size) * 1.5);
        var content = new StringBuilder();
        for (var k = 0; k < lines.Count; k++)
        {
            if (k > 0)
            {
                var blank = (int)Math.Round((lines[k - 1].Baseline - lines[k].Baseline) / pitch) - 1;
                content.Append('\n', 1 + Math.Clamp(blank, 0, 3));
            }

            var line = new StringBuilder();
            foreach (var token in lines[k].Tokens)
            {
                var column = (int)Math.Round((token.Left - left) / character);
                if (column > line.Length)
                {
                    line.Append(' ', column - line.Length);
                }
                else if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(token.Text);
            }

            content.Append(line);
        }

        var text = content.ToString();
        var longest = 0;
        var run = 0;
        foreach (var c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return new PageBlock(lines[0].Top, BlockKind.Code, fence + "\n" + text + "\n" + fence);
    }

    // Lines in a smaller size below the last body line (and above the footer margin) that start with a number are
    // footnotes. Those a superscript number in the page's text refers to become definitions and leave the flow;
    // their references are marked in the tokens.
    private static List<string> Footnotes(List<Line> lines, double body, double pageHeight)
    {
        var (marginTop, marginBottom) = (pageHeight * 0.92, pageHeight * 0.08);
        var last = lines.FindLastIndex(l => l.Size >= body * 0.95 && !InMargin(l, marginTop, marginBottom));
        if (last < 0)
        {
            return [];
        }

        var notes = new SortedDictionary<int, (List<Line> Lines, StringBuilder Text)>();
        int? open = null;
        for (var i = last + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (InMargin(line, marginTop, marginBottom) || line.Size >= body * 0.95)
            {
                open = null;
                continue;
            }

            var match = line.Tokens.Count >= 2 ? FootnoteNumber().Match(line.Tokens[0].Text) : Match.Empty;
            if (match.Success && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) is var number && !notes.ContainsKey(number))
            {
                notes[number] = ([line], new StringBuilder(line.TextFrom(1)));
                open = number;
            }
            else if (open is { } note)
            {
                notes[note].Lines.Add(line);
                Join(notes[note].Text, line.Text);
            }
        }

        var referenced = new HashSet<int>();
        foreach (var token in lines.Take(last + 1).SelectMany(l => l.Tokens).Where(t => t.Superscript))
        {
            if (FootnoteReference().IsMatch(token.Text) && int.Parse(token.Text, CultureInfo.InvariantCulture) is var number && notes.ContainsKey(number))
            {
                token.Reference = number;
                referenced.Add(number);
            }
        }

        var definitions = new List<string>();
        var used = new HashSet<Line>();
        foreach (var (number, note) in notes.Where(n => referenced.Contains(n.Key)))
        {
            used.UnionWith(note.Lines);
            definitions.Add($"[^{number.ToString(CultureInfo.InvariantCulture)}]: " + MarkdownText.Escape(note.Text.ToString().Trim()));
        }

        lines.RemoveAll(used.Contains);
        return definitions;
    }

    // The text after a leading bullet glyph, or null if the line is no bullet item.
    private static string? StripBullet(string text)
    {
        if (text.Length < 2)
        {
            return null;
        }

        var c = text[0];
        var strong = c is '•' or '▪' or '■' or '◦' or '·' or '●' or '‣' or '⁃' or '➢' or '→' or (>= '' and <= '');
        var weak = c is '-' or '–' or '*' or 'o';
        if (strong)
        {
            return text[1..].TrimStart();
        }

        return weak && char.IsWhiteSpace(text[1]) ? text[2..].TrimStart() : null;
    }

    // "1.", "2.3", "4.1.2 ": numbered headings and items start a new paragraph.
    private static bool StartsNumbered(string text) => NumberedStart().IsMatch(text);

    [GeneratedRegex(@"\.{4,}\s*\d+$")]
    private static partial Regex TableOfContentsEntry();

    [GeneratedRegex(@"^\d{1,2}(\.\d{1,2})*\.?\s+\S")]
    private static partial Regex NumberedStart();

    [GeneratedRegex(@"^\d{1,3}[.)]$")]
    private static partial Regex OrderedMarker();

    [GeneratedRegex(@"^(\d{1,3})[.)]?$")]
    private static partial Regex FootnoteNumber();

    [GeneratedRegex(@"^\d{1,3}$")]
    private static partial Regex FootnoteReference();

    [GeneratedRegex("(\\d+)")]
    private static partial Regex ReferenceMark();
}
