namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Estimates how wide text is without the font: a width in ems per class of character, a little more for bold. On
/// the generous side, so that what is measured to fit does fit in common sans faces.
/// </summary>
internal static class TextMetrics
{
    private const double EmuPerPoint = 12700;
    private const double BoldFactor = 1.08;

    /// <summary>The width of <paramref name="text"/> at <paramref name="size"/> (hundredths of a point), in EMU.</summary>
    public static double Width(string text, int size, bool bold) =>
        text.Sum(Em) * size / 100.0 * EmuPerPoint * (bold ? BoldFactor : 1.0);

    /// <summary>The width of the longest word of <paramref name="runs"/>, which no line break can split.</summary>
    public static double LongestWord(IReadOnlyList<TextRun> runs, int size, bool bold) =>
        Words(runs, size, bold).SelectMany(line => line).DefaultIfEmpty(0).Max();

    /// <summary>How many lines <paramref name="runs"/> take at <paramref name="width"/>, wrapping between words.</summary>
    public static int Lines(IReadOnlyList<TextRun> runs, int size, bool bold, double width)
    {
        var space = Em(' ') * size / 100.0 * EmuPerPoint;
        var lines = 0;
        foreach (var line in Words(runs, size, bold))
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

    /// <summary>Whether a run is set bold: bold itself, marked text, a status label, or everything in a header row.</summary>
    public static bool IsBold(TextRun run, bool header) =>
        header || run.Style.HasFlag(RunStyle.Bold) || run.Style.HasFlag(RunStyle.Mark) || run.Style.HasFlag(RunStyle.Label);

    // The widths of the words of each line; a word runs across runs until a space or a line break.
    private static List<List<double>> Words(IReadOnlyList<TextRun> runs, int size, bool bold)
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

        foreach (var run in runs)
        {
            if (run.IsBreak)
            {
                EndWord();
                lines.Add([]);
                continue;
            }

            var runBold = IsBold(run, bold);
            foreach (var c in run.Text)
            {
                if (char.IsWhiteSpace(c))
                {
                    EndWord();
                }
                else
                {
                    word += Em(c) * size / 100.0 * EmuPerPoint * (runBold ? BoldFactor : 1.0);
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
