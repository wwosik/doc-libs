using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace wwosik.DocLibs.DocumentConversions.Pdf;

public sealed partial class PdfConverter
{
    // A footnote reference stands in the text as these around its number until the Markdown is escaped.
    private const char ReferenceStart = '';
    private const char ReferenceEnd = '';

    /// <summary>A word, or the raised tail of one, with its box in PDF coordinates.</summary>
    private sealed class Token
    {
        public Token(string text, IReadOnlyList<Letter> letters, PdfRectangle box)
        {
            this.Text = text;
            this.Letters = letters;
            this.Left = box.Left;
            this.Right = box.Right;
            this.Bottom = box.Bottom;
            this.Top = box.Top;
            this.Size = Median(letters.Select(l => l.PointSize));
            this.Baseline = letters.Count == 0 ? box.Bottom : letters[0].StartBaseLine.Y;
        }

        public string Text { get; }

        public IReadOnlyList<Letter> Letters { get; }

        public double Left { get; }

        public double Right { get; }

        public double Bottom { get; }

        public double Top { get; }

        public double Size { get; }

        public double Baseline { get; }

        /// <summary>Smaller and raised above the line it belongs to.</summary>
        public bool Superscript { get; set; }

        /// <summary>The footnote this superscript refers to, once its definition is found.</summary>
        public int? Reference { get; set; }
    }

    /// <summary>A visual line: tokens on one baseline, superscripts included, left to right.</summary>
    private sealed class Line
    {
        public Line(IEnumerable<Token> tokens)
        {
            this.Tokens = [.. tokens.OrderBy(t => t.Left)];
            var main = this.Tokens.Where(t => !t.Superscript).ToList();
            if (main.Count == 0)
            {
                main = this.Tokens;
            }

            var letters = main.SelectMany(t => t.Letters).ToList();
            this.Left = this.Tokens.Min(t => t.Left);
            this.Right = this.Tokens.Max(t => t.Right);
            this.Top = main.Max(t => t.Top);
            this.Bottom = main.Min(t => t.Bottom);
            this.Size = Median(letters.Select(l => l.PointSize));
            this.Baseline = Median(main.Select(t => t.Baseline));
            this.Bold = letters.Count > 0
                && letters.Count(l => (l.FontName ?? string.Empty).Contains("Bold", StringComparison.OrdinalIgnoreCase)) >= letters.Count * 0.8;
        }

        public List<Token> Tokens { get; }

        public double Left { get; }

        public double Right { get; }

        public double Top { get; }

        public double Bottom { get; }

        public double Size { get; }

        public double Baseline { get; }

        public bool Bold { get; }

        public double CentreX => (this.Left + this.Right) / 2;

        public double CentreY => (this.Top + this.Bottom) / 2;

        public string Text => this.TextFrom(0);

        /// <summary>The text from token <paramref name="start"/> on; a superscript follows the word before it without a space.</summary>
        public string TextFrom(int start) => Join(this.Tokens.Skip(start));

        public static string Join(IEnumerable<Token> tokens)
        {
            var builder = new StringBuilder();
            foreach (var token in tokens)
            {
                if (builder.Length > 0 && !token.Superscript)
                {
                    builder.Append(' ');
                }

                if (token.Reference is { } number)
                {
                    builder.Append(ReferenceStart).Append(number).Append(ReferenceEnd);
                }
                else
                {
                    builder.Append(token.Text);
                }
            }

            return builder.ToString().Trim();
        }

        /// <summary>Set in a monospace font: by name, or by all its letters advancing the same width.</summary>
        public bool Monospace() => PdfConverter.Monospace([.. this.Tokens.SelectMany(t => t.Letters)], this.Size);
    }

    /// <summary>
    /// Whether a line's letters are set in a monospace font: at least 90 % of them in a font named so, or all of them
    /// in the line's <paramref name="size"/> advancing within 2 % of one width (at least four letters, three distinct).
    /// </summary>
    internal static bool Monospace(IReadOnlyList<Letter> all, double size)
    {
        var letters = all.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        if (letters.Count == 0)
        {
            return false;
        }

        if (letters.Count(l => MonospaceName(l.FontName)) >= letters.Count * 0.9)
        {
            return true;
        }

        var sized = letters.Where(l => Math.Abs(l.PointSize - size) < 0.1 && l.Width > 0).ToList();
        if (sized.Count < 4 || sized.Count < letters.Count * 0.9 || sized.Select(l => l.Value).Distinct(StringComparer.Ordinal).Count() < 3)
        {
            return false;
        }

        var widths = sized.Select(l => l.Width).ToList();
        return widths.Max() <= widths.Min() * 1.02;
    }

    private static bool MonospaceName(string? font) =>
        font is not null && (font.Contains("Mono", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Code", StringComparison.OrdinalIgnoreCase)
            || font.Contains("Inconsolata", StringComparison.OrdinalIgnoreCase));

    // Groups words into visual lines by baseline; PDF y grows upwards. A smaller word raised within a line's
    // height joins that line as a superscript instead of making a line of its own.
    private static List<Line> BuildLines(IEnumerable<Word> words)
    {
        var tokens = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.TextOrientation == TextOrientation.Horizontal)
            .SelectMany(Tokens)
            .OrderByDescending(t => t.Bottom)
            .ToList();

        var rows = new List<List<Token>>();
        foreach (var token in tokens)
        {
            var row = rows.Count == 0 ? null : rows[^1];
            var tolerance = Math.Max(token.Top - token.Bottom, 2) * 0.5;
            if (row is not null && Math.Abs(row.Average(x => x.Bottom) - token.Bottom) <= tolerance)
            {
                row.Add(token);
            }
            else
            {
                rows.Add([token]);
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var k in (int[])[i + 1, i - 1])
            {
                if (k >= 0 && k < rows.Count && RaisedWithin(rows[i], rows[k]))
                {
                    foreach (var token in rows[i])
                    {
                        token.Superscript = true;
                    }

                    rows[k].AddRange(rows[i]);
                    rows.RemoveAt(i);
                    i--;
                    break;
                }
            }
        }

        // A raised, smaller word close enough to be grouped with its line in the first place.
        foreach (var row in rows)
        {
            var size = Median(row.SelectMany(t => t.Letters).Select(l => l.PointSize));
            var baseline = Median(row.Where(t => t.Size >= size * 0.8).Select(t => t.Baseline));
            foreach (var token in row.Where(t => t.Size < size * 0.8 && t.Baseline > baseline + (size * 0.05)))
            {
                token.Superscript = true;
            }
        }

        return [.. rows.Select(r => new Line(r))];
    }

    // Under 80 % of the line's size, baseline above the line's, box within its height and near it.
    private static bool RaisedWithin(List<Token> row, List<Token> line)
    {
        var main = line.Where(t => !t.Superscript).ToList();
        if (main.Count == 0)
        {
            return false;
        }

        var size = Median(main.SelectMany(t => t.Letters).Select(l => l.PointSize));
        var own = Median(row.SelectMany(t => t.Letters).Select(l => l.PointSize));
        if (size <= 0 || own <= 0 || own >= size * 0.8)
        {
            return false;
        }

        var baseline = Median(main.Select(t => t.Baseline));
        var (bottom, top) = (main.Min(t => t.Bottom), main.Max(t => t.Top));
        var (left, right) = (main.Min(t => t.Left), main.Max(t => t.Right));
        return row.All(t => t.Baseline > baseline + (size * 0.05)
            && t.Bottom >= bottom - 0.5
            && t.Top <= top + (size * 0.35)
            && t.Left >= left - (size * 2)
            && t.Right <= right + (size * 2));
    }

    // A word as one token, or two when its last letters are a raised, smaller footnote mark glued onto it.
    private static IEnumerable<Token> Tokens(Word word)
    {
        var letters = word.Letters;
        for (var split = 1; split < letters.Count; split++)
        {
            var head = letters.Take(split).ToList();
            var size = Median(head.Select(l => l.PointSize));
            var baseline = head[0].StartBaseLine.Y;
            if (letters.Skip(split).All(l => l.PointSize < size * 0.8 && l.StartBaseLine.Y > baseline + (size * 0.05)))
            {
                var tail = letters.Skip(split).ToList();
                return [FromLetters(head), FromLetters(tail)];
            }
        }

        return [new Token(word.Text, letters, word.BoundingBox)];
    }

    private static Token FromLetters(List<Letter> letters) =>
        new(
            string.Concat(letters.Select(l => l.Value)),
            letters,
            new PdfRectangle(
                letters.Min(l => l.BoundingBox.Left),
                letters.Min(l => l.BoundingBox.Bottom),
                letters.Max(l => l.BoundingBox.Right),
                letters.Max(l => l.BoundingBox.Top)));

    // Joins a line onto the paragraph and repairs words hyphenated across lines.
    private static void Join(StringBuilder current, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (current.Length == 0)
        {
            current.Append(text);
            return;
        }

        var n = current.Length;
        if (current[n - 1] == '-' && n >= 2 && char.IsLetter(current[n - 2]))
        {
            if (char.IsLower(current[n - 2]) && char.IsLower(text[0]))
            {
                current.Length--; // "Netz-|nutzung" joins, "GELI-|Anmeldung" keeps its hyphen.
            }

            current.Append(text);
            return;
        }

        current.Append(' ').Append(text);
    }
}
