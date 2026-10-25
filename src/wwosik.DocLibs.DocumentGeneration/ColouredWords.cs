using System.Text;
using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>[words]{.success}</c> in running text: the words in the colour of a status label category, bold. Found in the
/// text of a paragraph's runs after Markdown has been parsed (Markdig leaves an unresolved <c>[…]</c> as text), never
/// inside code. Only that exact shape counts; any other brackets and braces stay text.
/// </summary>
internal static partial class ColouredWords
{
    /// <summary>
    /// <paramref name="runs"/> with every coloured span taken apart: the brackets and the braces dropped, the words
    /// in runs of their own made by <paramref name="colour"/>. Runs are left alone when there is no span.
    /// </summary>
    /// <exception cref="DocumentConversionException">A span names a category that does not exist.</exception>
    public static List<T> Apply<T>(
        IReadOnlyList<T> runs,
        Func<T, string> text,
        Func<T, bool> isCode,
        Func<T, string, T> withText,
        Func<T, LabelCategory, T> colour,
        DocumentFormat format)
    {
        // The paragraph as one string, with code (and line breaks) masked so that no span starts, ends or crosses there.
        var whole = new StringBuilder();
        foreach (var run in runs)
        {
            foreach (var c in text(run))
            {
                whole.Append(isCode(run) || c == '\n' ? '\u0001' : c);
            }
        }

        var spans = SpanPattern().Matches(whole.ToString()).ToList();
        if (spans.Count == 0)
        {
            return [.. runs];
        }

        // Per character: dropped (brackets, braces), or the category it takes.
        var categories = new LabelCategory?[whole.Length];
        var dropped = new bool[whole.Length];
        foreach (Match span in spans)
        {
            var name = span.Groups["category"].Value;
            var category = Enum.GetValues<LabelCategory>().FirstOrDefault(c => c.ToString().Equals(name, StringComparison.OrdinalIgnoreCase), (LabelCategory)(-1));
            if (category < 0)
            {
                throw new DocumentConversionException(
                    format,
                    $"'{span.Value}' names no colour. Valid: {string.Join(", ", Enum.GetValues<LabelCategory>().Select(c => "{." + c.ToString().ToLowerInvariant() + "}"))}.");
            }

            var words = span.Groups["words"];
            for (var i = span.Index; i < span.Index + span.Length; i++)
            {
                if (i >= words.Index && i < words.Index + words.Length)
                {
                    categories[i] = category;
                }
                else
                {
                    dropped[i] = true;
                }
            }
        }

        // Each run cut where the category changes, dropped characters left out.
        var result = new List<T>();
        var position = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var content = text(runs[i]);
            var start = position;
            position += content.Length;
            if (content.Length == 0 || isCode(runs[i]) || content == "\n")
            {
                result.Add(runs[i]);
                continue;
            }

            var piece = new StringBuilder();
            LabelCategory? current = null;
            void Flush()
            {
                if (piece.Length > 0)
                {
                    var run = withText(runs[i], piece.ToString());
                    result.Add(current is { } category ? colour(run, category) : run);
                    piece.Clear();
                }
            }

            for (var k = start; k < position; k++)
            {
                if (dropped[k])
                {
                    continue;
                }

                if (categories[k] != current)
                {
                    Flush();
                    current = categories[k];
                }

                piece.Append(content[k - start]);
            }

            Flush();
        }

        return result;
    }

    [GeneratedRegex(@"\[(?<words>[^\[\]\u0001]+)\]\{\.(?<category>[A-Za-z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex SpanPattern();
}
