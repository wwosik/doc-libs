using System.Text;

namespace wwosik.DocLibs.DocumentConversions;

/// <summary>Building blocks for writing Markdown: escaping, code spans, link targets, tables, normalization.</summary>
public static class MarkdownText
{
    /// <summary>
    /// Makes plain text safe to stand inside a Markdown line. Only what would really turn into
    /// formatting is escaped, so <c>customfield_10682</c> stays readable.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var before = index == 0 ? ' ' : text[index - 1];
            var after = index + 1 < text.Length ? text[index + 1] : ' ';

            var guard = current switch
            {
                '\\' => index + 1 >= text.Length || IsAsciiPunctuation(after),
                '`' or '*' or '[' or '~' => true,
                '_' => !char.IsLetterOrDigit(before) || !char.IsLetterOrDigit(after),
                '<' => char.IsLetter(after) || after is '/' or '!' or '?',
                _ => false,
            };

            if (guard)
            {
                result.Append('\\');
            }

            result.Append(current);
        }

        return result.ToString();
    }

    /// <summary>Escapes what only counts at the start of a line: headings, quotes, lists, rules, tables.</summary>
    public static string EscapeLineStart(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length == 0)
        {
            return line;
        }

        var first = line[0];

        if (first is '#' or '>' or '|')
        {
            return "\\" + line;
        }

        if (first is '-' or '+' or '=')
        {
            var marker = line.Length == 1 || line[1] == ' ';
            var underline = first != '+' && line.All(c => c == first);

            return marker || underline ? "\\" + line : line;
        }

        var digits = 0;

        while (digits < line.Length && digits < 9 && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits > 0
            && digits < line.Length
            && line[digits] is '.' or ')'
            && (digits + 1 == line.Length || line[digits + 1] == ' '))
        {
            return line[..digits] + "\\" + line[digits..];
        }

        return line;
    }

    /// <summary>A code span that survives backticks in its content.</summary>
    public static string Code(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return string.Empty;
        }

        var longest = 0;
        var run = 0;

        foreach (var c in content)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', longest + 1);
        var pad = content.StartsWith('`') || content.EndsWith('`') ? " " : string.Empty;

        return fence + pad + content + pad + fence;
    }

    /// <summary>A link target; angle brackets keep spaces and parentheses inside it.</summary>
    public static string Destination(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.IndexOfAny([' ', '(', ')', '<', '>']) < 0
            ? url
            : "<" + url.Replace("<", "%3C", StringComparison.Ordinal).Replace(">", "%3E", StringComparison.Ordinal) + ">";
    }

    /// <summary>Markdown that has to stand inside a table cell.</summary>
    public static string Cell(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return markdown.Replace("|", "\\|", StringComparison.Ordinal);
    }

    /// <summary>
    /// A pipe table whose first row is the header, followed by a blank line. Cells are Markdown; line
    /// breaks inside a cell, hard breaks included, become <c>&lt;br&gt;</c>. Returns an empty string for an empty grid.
    /// </summary>
    public static string Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (columns == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var r = 0; r < rows.Count; r++)
        {
            builder.Append('|');
            for (var c = 0; c < columns; c++)
            {
                builder.Append(' ').Append(c < rows[r].Count ? TableCell(rows[r][c]) : string.Empty).Append(" |");
            }

            builder.Append('\n');
            if (r == 0)
            {
                builder.Append('|');
                for (var c = 0; c < columns; c++)
                {
                    builder.Append(" --- |");
                }

                builder.Append('\n');
            }
        }

        return builder.Append('\n').ToString();
    }

    /// <summary>Drops rows and columns that are entirely empty.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Compact(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var kept = rows.Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
        if (kept.Count == 0)
        {
            return [];
        }

        var columns = kept.Max(r => r.Count);
        var keep = Enumerable.Range(0, columns).Where(c => kept.Any(r => c < r.Count && r[c].Trim().Length > 0)).ToList();
        return [.. kept.Select(r => (IReadOnlyList<string>)[.. keep.Select(c => c < r.Count ? r[c] : string.Empty)])];
    }

    /// <summary>
    /// Unifies line endings to <c>\n</c>, trims trailing whitespace, collapses runs of blank lines into
    /// one, and ends the text with exactly one newline. Text without content becomes empty.
    /// </summary>
    public static string Normalize(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var builder = new StringBuilder(markdown.Length);
        var blankRun = 0;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (++blankRun > 1)
                {
                    continue;
                }
            }
            else
            {
                blankRun = 0;
            }

            builder.Append(line).Append('\n');
        }

        var result = builder.ToString().Trim('\n');
        return result.Length == 0 ? string.Empty : result + "\n";
    }

    private static string TableCell(string cell) =>
        Cell(cell)
            .Replace("\\\n", "<br>", StringComparison.Ordinal)
            .Replace("  \n", "<br>", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace('\t', ' ')
            .Trim();

    private static bool IsAsciiPunctuation(char c) =>
        c is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');
}
