namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Estimates how wide text is without the font: a width in ems per class of character, a little more for bold. On
/// the generous side, so that what is measured to fit does fit in common sans faces. Text comes as pieces, each set
/// bold or not; a piece whose text is exactly <see cref="LineBreak"/> breaks the line. Widths are in EMU (12,700 a
/// point).
/// </summary>
internal static class TextMetrics
{
    /// <summary>The text of a piece that breaks the line.</summary>
    public const string LineBreak = "\n";

    private const double EmuPerPoint = 12700;
    private const double BoldFactor = 1.08;

    /// <summary>The width of <paramref name="text"/> at <paramref name="size"/> (hundredths of a point), in EMU.</summary>
    public static double Width(string text, int size, bool bold) =>
        text.Sum(Em) * size / 100.0 * EmuPerPoint * (bold ? BoldFactor : 1.0);

    /// <summary>The width of the longest word of <paramref name="pieces"/>, which no line break can split.</summary>
    public static double LongestWord(IEnumerable<(string Text, bool Bold)> pieces, int size) =>
        Words(pieces, size).SelectMany(line => line).DefaultIfEmpty(0).Max();

    /// <summary>How many lines <paramref name="pieces"/> take at <paramref name="width"/> (EMU), wrapping between words.</summary>
    public static int Lines(IEnumerable<(string Text, bool Bold)> pieces, int size, double width)
    {
        var space = Em(' ') * size / 100.0 * EmuPerPoint;
        var lines = 0;
        foreach (var line in Words(pieces, size))
        {
            lines++;
            var x = 0.0;
            foreach (var word in line)
            {
                var needed = x == 0 ? word : x + space + word;
                if (x > 0 && needed > width)
                {
                    lines++;
                    x = word;
                }
                else
                {
                    x = needed;
                }

                // A word wider than the line breaks inside, one more line per width it takes.
                while (x > width && width > 0)
                {
                    lines++;
                    x -= width;
                }
            }
        }

        return Math.Max(lines, 1);
    }

    // The widths of the words of each line; a word runs across pieces until a space or a line break.
    private static List<List<double>> Words(IEnumerable<(string Text, bool Bold)> pieces, int size)
    {
        var lines = new List<List<double>> { new() };
        var word = 0.0;
        void EndWord()
        {
            if (word > 0)
            {
                lines[^1].Add(word);
                word = 0;
            }
        }

        foreach (var (text, bold) in pieces)
        {
            if (text == LineBreak)
            {
                EndWord();
                lines.Add([]);
                continue;
            }

            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    EndWord();
                }
                else
                {
                    word += Em(c) * size / 100.0 * EmuPerPoint * (bold ? BoldFactor : 1.0);
                }
            }
        }

        EndWord();
        return lines;
    }

    private static double Em(char c) => c switch
    {
        ' ' => 0.28,
        '.' or ',' or ':' or ';' or '\'' or '|' or '!' or 'i' or 'l' or 'j' or 'I' or '(' or ')' or '[' or ']' => 0.3,
        'f' or 't' or 'r' => 0.4,
        'm' or 'w' => 0.85,
        'M' or 'W' or '%' or '@' => 0.9,
        >= '0' and <= '9' => 0.6,
        >= 'A' and <= 'Z' => 0.68,
        >= 'a' and <= 'z' => 0.53,
        _ when char.IsUpper(c) => 0.68,
        _ when char.IsLetter(c) => 0.55,
        _ => 0.6,
    };
}
