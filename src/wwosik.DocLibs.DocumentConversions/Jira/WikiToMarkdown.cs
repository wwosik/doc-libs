using System.Text;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentConversions.Jira;

/// <summary>
/// JIRA wiki markup in, Markdown out — the way back from <see cref="MarkdownToWiki.ToWiki"/>. Like it,
/// this works in two phases: blocks first, then the text inside them. What this does not know
/// stays as text.
/// </summary>
internal static partial class WikiToMarkdown
{
    /// <summary>Macros whose content is code: it goes into a fence untouched.</summary>
    private static readonly HashSet<string> Literal = new(StringComparer.OrdinalIgnoreCase) { "code", "noformat" };

    [GeneratedRegex(@"(?<![\\{])\{(code|noformat|quote)(:[^}\n]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex BlockMacro();

    [GeneratedRegex(@"\G\{([a-zA-Z][a-zA-Z0-9-]*)(:[^}\n]*)?\}")]
    private static partial Regex InlineMacro();

    [GeneratedRegex(@"\{color(:[^}\n]*)?\}", RegexOptions.IgnoreCase)]
    private static partial Regex Colour();

    [GeneratedRegex(@"^\s*h([1-6])\.(?:\s+(.*))?$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^\s*([*#]+|-)\s+(.*)$")]
    private static partial Regex ListLine();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]+-\d+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:")]
    private static partial Regex Scheme();

    [GeneratedRegex(@"\.[A-Za-z0-9]{2,5}$")]
    private static partial Regex Extension();

    /// <param name="wiki">The wiki markup as JIRA stores it.</param>
    /// <param name="topLevel">The Markdown heading level <c>h1.</c> becomes, so that headings stay below their section.</param>
    /// <param name="attachment">Resolves an attachment's file name to its address, for images and <c>[^datei]</c>.</param>
    public static string ToMarkdown(string? wiki, int topLevel = 1, Func<string, string?>? attachment = null)
    {
        if (string.IsNullOrWhiteSpace(wiki))
        {
            return string.Empty;
        }

        var text = wiki.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ');

        return new Converter(Math.Clamp(topLevel, 1, 6), attachment).Document(text);
    }

    private sealed class Converter(int topLevel, Func<string, string?>? attachment)
    {
        /// <summary>Code, noformat and quote may span blank lines, so they are cut out before the lines are.</summary>
        public string Document(string text)
        {
            var blocks = new List<string>();
            var position = 0;

            while (position < text.Length)
            {
                var match = BlockMacro().Match(text, position);

                if (!match.Success)
                {
                    Lines(text[position..], blocks);
                    break;
                }

                Lines(text[position..match.Index], blocks);

                var name = match.Groups[1].Value.ToLowerInvariant();
                var start = match.Index + match.Length;
                var close = Close(text, name, start);
                var inner = close < 0 ? text[start..] : text[start..close];

                position = close < 0 ? text.Length : close + name.Length + 2;

                blocks.Add(Literal.Contains(name)
                    ? Fence(inner, name == "code" ? Language(match.Groups[2].Value) : string.Empty)
                    : Quote(Document(inner)));
            }

            return string.Join("\n\n", blocks.Where(block => block.Length > 0));
        }

        private static int Close(string text, string name, int start)
        {
            var token = "{" + name + "}";
            var index = start;

            while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (index == 0 || text[index - 1] != '\\')
                {
                    return index;
                }

                index += token.Length;
            }

            return -1;
        }

        private static string Language(string parameters)
        {
            foreach (var part in parameters.TrimStart(':').Split('|'))
            {
                var value = part.Trim();
                var equals = value.IndexOf('=');

                if (equals < 0)
                {
                    return Clean(value);
                }

                if (value[..equals].Trim().Equals("language", StringComparison.OrdinalIgnoreCase))
                {
                    return Clean(value[(equals + 1)..]);
                }
            }

            return string.Empty;

            static string Clean(string value) =>
                new(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is '#' or '+' or '-').ToArray());
        }

        private static string Fence(string content, string language)
        {
            var lines = content.Trim('\n').Split('\n').Select(line => line.TrimEnd());
            var body = string.Join('\n', lines);

            var longest = 0;
            var run = 0;

            foreach (var c in body)
            {
                run = c == '`' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }

            var fence = new string('`', Math.Max(3, longest + 1));

            return body.Length == 0
                ? $"{fence}{language}\n{fence}"
                : $"{fence}{language}\n{body}\n{fence}";
        }

        private static string Quote(string markdown) =>
            markdown.Length == 0
                ? string.Empty
                : string.Join('\n', markdown.Split('\n').Select(line => line.Length == 0 ? ">" : "> " + line));

        private void Lines(string text, List<string> blocks)
        {
            var lines = text.Split('\n');
            var paragraph = new List<string>();
            var index = 0;

            void Flush()
            {
                if (paragraph.Count > 0)
                {
                    blocks.Add(string.Join('\n', paragraph));
                    paragraph.Clear();
                }
            }

            while (index < lines.Length)
            {
                var line = lines[index];
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    Flush();
                    index++;
                    continue;
                }

                if (HeadingLine().Match(line) is { Success: true } heading)
                {
                    Flush();

                    var title = Inline(heading.Groups[2].Value.Trim());
                    var level = Math.Min(6, topLevel + int.Parse(heading.Groups[1].Value) - 1);

                    if (title.Length > 0)
                    {
                        blocks.Add(new string('#', level) + " " + title);
                    }

                    index++;
                    continue;
                }

                if (trimmed.StartsWith("bq. ", StringComparison.Ordinal))
                {
                    Flush();
                    blocks.Add(Quote(string.Join('\n', Paragraph(trimmed[4..]))));
                    index++;
                    continue;
                }

                if (IsRule(trimmed))
                {
                    Flush();
                    blocks.Add("---");
                    index++;
                    continue;
                }

                if (trimmed.StartsWith('|'))
                {
                    Flush();
                    blocks.Add(Table(lines, ref index));
                    continue;
                }

                if (ListLine().IsMatch(line))
                {
                    Flush();
                    blocks.Add(List(lines, ref index));
                    continue;
                }

                paragraph.AddRange(Paragraph(trimmed));
                index++;
            }

            Flush();
        }

        private static bool IsRule(string trimmed) => trimmed.Length >= 4 && trimmed.All(c => c == '-');

        private static bool StartsBlock(string line)
        {
            var trimmed = line.Trim();

            return HeadingLine().IsMatch(line)
                || trimmed.StartsWith("bq. ", StringComparison.Ordinal)
                || IsRule(trimmed)
                || trimmed.StartsWith('|')
                || ListLine().IsMatch(line);
        }

        /// <summary>A wiki line break <c>\\</c> becomes a line of its own.</summary>
        private IEnumerable<string> Paragraph(string line) =>
            line.Split(@"\\")
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .Select(part => MarkdownText.EscapeLineStart(Inline(part)));

        /// <summary>Nesting follows the markers: <c>*#</c> is a numbered list inside a bulleted one.</summary>
        private string List(string[] lines, ref int index)
        {
            var output = new List<string>();
            var continuation = string.Empty;

            while (index < lines.Length)
            {
                var line = lines[index];

                if (ListLine().Match(line) is { Success: true } item)
                {
                    var markers = item.Groups[1].Value;
                    var indent = new StringBuilder();

                    for (var level = 0; level < markers.Length - 1; level++)
                    {
                        indent.Append(markers[level] == '#' ? "   " : "  ");
                    }

                    var bullet = markers[^1] == '#' ? "1. " : "- ";
                    var parts = Paragraph(item.Groups[2].Value).ToList();

                    continuation = indent + new string(' ', bullet.Length);
                    output.Add(indent + bullet + (parts.Count > 0 ? parts[0] : string.Empty));

                    foreach (var part in parts.Skip(1))
                    {
                        output.Add(continuation + part);
                    }

                    index++;
                    continue;
                }

                if (line.Trim().Length == 0 || StartsBlock(line))
                {
                    break;
                }

                foreach (var part in Paragraph(line.Trim()))
                {
                    output.Add(continuation + part);
                }

                index++;
            }

            return string.Join('\n', output);
        }

        private string Table(string[] lines, ref int index)
        {
            var rows = new List<List<(bool Header, string Text)>>();

            while (index < lines.Length && lines[index].TrimStart().StartsWith('|'))
            {
                var row = lines[index].Trim();
                index++;

                // A cell may run over several lines until the row is closed again.
                while (!row.EndsWith('|')
                    && index < lines.Length
                    && lines[index].Trim().Length > 0
                    && !StartsBlock(lines[index]))
                {
                    row += "\n" + lines[index].Trim();
                    index++;
                }

                rows.Add(Cells(row));
            }

            var columns = rows.Count == 0 ? 0 : rows.Max(row => row.Count);

            if (columns == 0)
            {
                return string.Empty;
            }

            var header = rows[0].Count > 0 && rows[0].All(cell => cell.Header) ? rows[0] : null;
            var output = new List<string>
            {
                Row(header?.Select(cell => Cell(cell.Text)).ToList() ?? [], columns),
                Row(Enumerable.Repeat("---", columns).ToList(), columns),
            };

            foreach (var row in header is null ? rows : rows.Skip(1))
            {
                output.Add(Row(row.Select(cell => Cell(cell.Text)).ToList(), columns));
            }

            return string.Join('\n', output);
        }

        private static string Row(IReadOnlyList<string> cells, int columns)
        {
            var padded = cells.Concat(Enumerable.Repeat(string.Empty, Math.Max(0, columns - cells.Count)));
            return "| " + string.Join(" | ", padded) + " |";
        }

        private string Cell(string text) =>
            MarkdownText.Cell(string.Join(
                "<br>",
                text.Split('\n').SelectMany(line => line.Split(@"\\")).Select(part => Inline(part.Trim()))));

        /// <summary>
        /// Splits a row at its bars, but not at the bar inside <c>[text|url]</c> or <c>{{…}}</c>.
        /// <c>||</c> opens a header cell.
        /// </summary>
        private static List<(bool Header, string Text)> Cells(string row)
        {
            var cells = new List<(bool, string)>();
            var position = 0;

            while (position < row.Length && row[position] == '|')
            {
                var header = position + 1 < row.Length && row[position + 1] == '|';
                position += header ? 2 : 1;

                var start = position;

                while (position < row.Length && row[position] != '|')
                {
                    var current = row[position];

                    if (current == '\\' && position + 1 < row.Length)
                    {
                        position += 2;
                        continue;
                    }

                    if (current == '{' && position + 1 < row.Length && row[position + 1] == '{'
                        && row.IndexOf("}}", position + 2, StringComparison.Ordinal) is var end and > 0)
                    {
                        position = end + 2;
                        continue;
                    }

                    if (current == '[' && row.IndexOf(']', position + 1) is var close and > 0)
                    {
                        position = close + 1;
                        continue;
                    }

                    position++;
                }

                var content = row[start..Math.Min(position, row.Length)];

                if (position >= row.Length && content.Trim().Length == 0)
                {
                    break;
                }

                cells.Add((header, content.Trim()));
            }

            return cells;
        }

        private string Inline(string text)
        {
            var output = new StringBuilder();
            var literal = new StringBuilder();
            var position = 0;

            void Flush()
            {
                if (literal.Length > 0)
                {
                    output.Append(MarkdownText.Escape(literal.ToString()));
                    literal.Clear();
                }
            }

            while (position < text.Length)
            {
                var current = text[position];

                if (current == '\\' && position + 1 < text.Length
                    && !char.IsLetterOrDigit(text[position + 1]) && !char.IsWhiteSpace(text[position + 1]))
                {
                    literal.Append(text[position + 1]);
                    position += 2;
                    continue;
                }

                if (current == '{' && Forced(text, position) is { } marker)
                {
                    Flush();

                    var token = text.Substring(position, 3);
                    var partner = text.IndexOf(token, position + 3, StringComparison.Ordinal);

                    // The editor writes {*}…{*} where a marker touches a letter; a lone one is dropped.
                    if (partner > position + 3)
                    {
                        output.Append(Wrap(marker, Inline(text[(position + 3)..partner])));
                    }

                    position = partner < 0 ? position + 3 : partner + 3;
                    continue;
                }

                if (current == '{' && Monospace(text, position, out var code, out var past))
                {
                    Flush();
                    output.Append(MarkdownText.Code(code));
                    position = past;
                    continue;
                }

                if (current == '{' && InlineMacro().Match(text, position) is { Success: true } macro)
                {
                    position += macro.Length;
                    continue;
                }

                if (current == '[' && text.IndexOf(']', position + 1) is var close and > 0
                    && Link(text[(position + 1)..close]) is { } link)
                {
                    Flush();
                    output.Append(link);
                    position = close + 1;
                    continue;
                }

                if (current == '!' && text.IndexOf('!', position + 1) is var end and > 0
                    && Image(text[(position + 1)..end]) is { } image)
                {
                    Flush();
                    output.Append(image);
                    position = end + 1;
                    continue;
                }

                if (current is '*' or '_' or '-' or '+' && Emphasis(text, position, out var inner, out var last))
                {
                    Flush();
                    output.Append(Wrap(current, Inline(inner)));
                    position = last + 1;
                    continue;
                }

                literal.Append(current);
                position++;
            }

            Flush();
            return output.ToString();
        }

        private static string Wrap(char marker, string content) =>
            marker switch
            {
                '*' => "**" + content + "**",
                '_' => "*" + content + "*",
                '-' => "~~" + content + "~~",
                _ => content,
            };

        private static char? Forced(string text, int position) =>
            position + 2 < text.Length && text[position + 2] == '}' && text[position + 1] is '*' or '_' or '-' or '+' or '^' or '~'
                ? text[position + 1]
                : null;

        /// <summary>
        /// <c>{{…}}</c>, including the <c>{{{}…{}}}</c> form that guards braces inside it. A colour
        /// inside it goes; it is the one macro the editor puts there.
        /// </summary>
        private static bool Monospace(string text, int position, out string code, out int past)
        {
            code = string.Empty;
            past = position;

            if (position + 1 >= text.Length || text[position + 1] != '{')
            {
                return false;
            }

            var close = text.IndexOf("}}", position + 2, StringComparison.Ordinal);

            if (close < 0)
            {
                return false;
            }

            // In }}} the last two braces close; the first belongs to the content.
            while (close + 2 < text.Length && text[close + 2] == '}')
            {
                close++;
            }

            code = text[(position + 2)..close];

            if (code.StartsWith("{}", StringComparison.Ordinal) && code.EndsWith("{}", StringComparison.Ordinal) && code.Length >= 4)
            {
                code = code[2..^2];
            }

            code = Colour().Replace(code, string.Empty);
            past = close + 2;
            return true;
        }

        /// <summary>
        /// Wiki emphasis opens after a space or punctuation and closes before one, which keeps
        /// <c>Ende-zu-Ende</c> and <c>customfield_10682</c> as they are.
        /// </summary>
        private static bool Emphasis(string text, int position, out string inner, out int end)
        {
            inner = string.Empty;
            end = -1;

            var marker = text[position];
            var before = position == 0 ? ' ' : text[position - 1];

            if (char.IsLetterOrDigit(before) || before == marker)
            {
                return false;
            }

            if (marker == '-' && !char.IsWhiteSpace(before) && before is not ('(' or '"' or '\''))
            {
                return false;
            }

            if (position + 1 >= text.Length || char.IsWhiteSpace(text[position + 1]) || text[position + 1] == marker)
            {
                return false;
            }

            for (var index = position + 2; index < text.Length; index++)
            {
                if (text[index] != marker || char.IsWhiteSpace(text[index - 1]))
                {
                    continue;
                }

                var after = index + 1 < text.Length ? text[index + 1] : ' ';

                if (char.IsLetterOrDigit(after) || after == marker)
                {
                    continue;
                }

                inner = text[(position + 1)..index];
                end = index;
                return true;
            }

            return false;
        }

        private string? Link(string content)
        {
            if (content.Length == 0)
            {
                return null;
            }

            if (content[0] == '~')
            {
                return "@" + content[1..].Trim();
            }

            var bar = content.IndexOf('|');
            var label = bar < 0 ? null : content[..bar].Trim();
            var target = (bar < 0 ? content : content[(bar + 1)..]).Trim();

            if (target.IndexOf('|') is var tip and >= 0)
            {
                target = target[..tip].Trim();
            }

            var text = string.IsNullOrEmpty(label) ? null : Inline(label);

            if (target.StartsWith('~'))
            {
                return "@" + target[1..];
            }

            if (target.StartsWith('^'))
            {
                var name = target[1..];
                var address = attachment?.Invoke(name);
                var shown = text ?? MarkdownText.Escape(name);

                return address is null ? shown : $"[{shown}]({MarkdownText.Destination(address)})";
            }

            if (Scheme().IsMatch(target) && !target.Contains(' '))
            {
                return text is null ? $"<{target}>" : $"[{text}]({MarkdownText.Destination(target)})";
            }

            if (KeyPattern().IsMatch(target))
            {
                return text is null ? target : $"{text} ({target})";
            }

            if (target.StartsWith('#') && text is not null)
            {
                return text;
            }

            return null;
        }

        private string? Image(string content)
        {
            if (content.Length == 0 || char.IsWhiteSpace(content[0]) || char.IsWhiteSpace(content[^1]))
            {
                return null;
            }

            var bar = content.IndexOf('|');
            var source = (bar < 0 ? content : content[..bar]).Trim();

            if (attachment?.Invoke(source) is { } address)
            {
                return $"![{MarkdownText.Escape(source)}]({MarkdownText.Destination(address)})";
            }

            // Without a known attachment, a space means prose between two exclamation marks.
            if (source.Contains(' '))
            {
                return null;
            }

            if (Scheme().IsMatch(source))
            {
                var name = source[(source.LastIndexOf('/') + 1)..];
                return $"![{MarkdownText.Escape(name)}]({MarkdownText.Destination(source)})";
            }

            return Extension().IsMatch(source)
                ? $"![{MarkdownText.Escape(source)}]({MarkdownText.Destination(source)})"
                : null;
        }
    }
}
