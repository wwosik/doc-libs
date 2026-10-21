using System.Text;

namespace wwosik.DocLibs.DocumentConversions.Jira;

/// <summary>
/// Markdown in, JIRA wiki markup out. The description field of the API v2 speaks nothing else,
/// and the caller writes Markdown. What this does not know stays as it is.
/// </summary>
internal static class MarkdownToWiki
{
    private enum Kind
    {
        Blank,
        Heading,
        Rule,
        Row,
        Code,
        Item,
        Quote,
        Text,
    }

    private sealed record Block(
        Kind Kind,
        string Text,
        int Level = 0,
        int Indent = 0,
        char Marker = '\0',
        string Info = "");

    /// <summary>Characters JIRA reads as markup, and the ones a backslash may therefore guard.</summary>
    private const string Markup = "*_-+^~{}[]|!?#";

    public static string ToWiki(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        return Render(Blocks(lines));
    }

    /// <summary>
    /// The first of two phases: group the lines into blocks and fold every soft line break inside
    /// a block into a single space. A wrapped paragraph has to convert like an unwrapped one.
    /// </summary>
    private static List<Block> Blocks(string[] lines)
    {
        var blocks = new List<Block>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                blocks.Add(new Block(Kind.Blank, string.Empty));
                index++;
                continue;
            }

            if (Fence(trimmed) is { } fence)
            {
                var language = trimmed[3..].Trim();
                var fenced = new List<string>();
                index++;

                while (index < lines.Length && !lines[index].Trim().StartsWith(fence, StringComparison.Ordinal))
                {
                    fenced.Add(lines[index]);
                    index++;
                }

                // Past the closing fence, or past the end when the author forgot it.
                index++;
                blocks.Add(new Block(Kind.Code, string.Join('\n', fenced), Info: language));
                continue;
            }

            if (Quoted(line))
            {
                var quoted = new List<string>();

                while (index < lines.Length && Quoted(lines[index]))
                {
                    quoted.Add(Unquote(lines[index]));
                    index++;
                }

                blocks.Add(new Block(Kind.Quote, Fold(quoted)));
                continue;
            }

            if (Heading(trimmed, out var level, out var title))
            {
                blocks.Add(new Block(Kind.Heading, title, level));
                index++;
                continue;
            }

            if (IsRule(trimmed))
            {
                blocks.Add(new Block(Kind.Rule, string.Empty));
                index++;
                continue;
            }

            if (Cells(trimmed) is not null)
            {
                blocks.Add(new Block(Kind.Row, trimmed));
                index++;
                continue;
            }

            if (ListItem(line, out var indent, out var marker, out var item))
            {
                var parts = new List<string> { item };
                index++;

                while (index < lines.Length && Plain(lines[index]))
                {
                    parts.Add(lines[index].Trim());
                    index++;
                }

                blocks.Add(new Block(Kind.Item, Fold(parts), Indent: indent, Marker: marker));
                continue;
            }

            if (Indented(line))
            {
                blocks.Add(new Block(Kind.Code, Collect(lines, ref index)));
                continue;
            }

            var paragraph = new List<string>();

            while (index < lines.Length && Plain(lines[index]))
            {
                paragraph.Add(lines[index].Trim());
                index++;
            }

            blocks.Add(new Block(Kind.Text, Fold(paragraph)));
        }

        return blocks;
    }

    /// <summary>The second phase: one block, one piece of wiki markup.</summary>
    private static string Render(IReadOnlyList<Block> blocks)
    {
        var output = new List<string>();
        var list = new List<(int Indent, char Marker)>();

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (block.Kind == Kind.Item)
            {
                output.Add($"{Prefix(list, block.Indent, block.Marker)} {Body(block.Text)}");
                continue;
            }

            if (block.Kind == Kind.Blank)
            {
                // A blank line between two items would end the list in JIRA, so it is dropped.
                if (Enclosed(blocks, index))
                {
                    continue;
                }

                list.Clear();
                output.Add(string.Empty);
                continue;
            }

            list.Clear();

            switch (block.Kind)
            {
                case Kind.Heading:
                    output.Add($"h{block.Level}. {Inline(block.Text)}");
                    break;

                case Kind.Rule:
                    output.Add("----");
                    break;

                case Kind.Quote:
                    output.Add($"{{quote}}{Body(block.Text)}{{quote}}");
                    break;

                case Kind.Code:
                    var open = block.Info.Length == 0 ? "{code}" : $"{{code:{block.Info}}}";
                    output.Add($"{open}\n{block.Text}\n{{code}}");
                    break;

                case Kind.Row:
                    var cells = Cells(block.Text);

                    if (cells is null || IsSeparator(cells))
                    {
                        break;
                    }

                    var header = index + 1 < blocks.Count
                        && blocks[index + 1].Kind == Kind.Row
                        && Cells(blocks[index + 1].Text) is { } following
                        && IsSeparator(following);

                    var bar = header ? "||" : "|";
                    var row = new StringBuilder(bar);

                    foreach (var cell in cells)
                    {
                        row.Append(Inline(cell)).Append(bar);
                    }

                    output.Add(row.ToString());
                    break;

                default:
                    output.Add(Body(block.Text));
                    break;
            }
        }

        return string.Join('\n', output).TrimEnd('\n');
    }

    private static bool Enclosed(IReadOnlyList<Block> blocks, int index)
    {
        var before = index - 1;

        while (before >= 0 && blocks[before].Kind == Kind.Blank)
        {
            before--;
        }

        var after = index + 1;

        while (after < blocks.Count && blocks[after].Kind == Kind.Blank)
        {
            after++;
        }

        return before >= 0
            && blocks[before].Kind == Kind.Item
            && after < blocks.Count
            && blocks[after].Kind == Kind.Item;
    }

    private static string Fold(IReadOnlyList<string> lines)
    {
        var result = new StringBuilder();
        var gap = false;

        foreach (var line in lines)
        {
            var text = line.Trim();

            if (text.Length == 0)
            {
                gap = result.Length > 0;
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(gap ? "\n\n" : " ");
            }

            gap = false;
            result.Append(text);
        }

        return result.ToString();
    }

    /// <summary>True for a line that carries on the block it follows instead of starting a new one.</summary>
    private static bool Plain(string line)
    {
        var trimmed = line.Trim();

        return trimmed.Length != 0
            && Fence(trimmed) is null
            && !Quoted(line)
            && !Heading(trimmed, out _, out _)
            && !IsRule(trimmed)
            && Cells(trimmed) is null
            && !ListItem(line, out _, out _, out _);
    }

    private static bool Quoted(string line) => line.TrimStart().StartsWith('>');

    private static string Unquote(string line)
    {
        var text = line.TrimStart()[1..];

        return text.StartsWith(' ') ? text[1..] : text;
    }

    private static bool Indented(string line) =>
        line.Trim().Length != 0 && (line.StartsWith('\t') || line.StartsWith("    ", StringComparison.Ordinal));

    /// <summary>Collects an indented code block, keeping the blank lines between two indented runs.</summary>
    private static string Collect(string[] lines, ref int index)
    {
        var body = new List<string>();

        while (index < lines.Length)
        {
            if (Indented(lines[index]))
            {
                body.Add(Dedent(lines[index]));
                index++;
                continue;
            }

            if (lines[index].Trim().Length != 0)
            {
                break;
            }

            var next = index;

            while (next < lines.Length && lines[next].Trim().Length == 0)
            {
                next++;
            }

            if (next >= lines.Length || !Indented(lines[next]))
            {
                break;
            }

            for (; index < next; index++)
            {
                body.Add(string.Empty);
            }
        }

        return string.Join('\n', body);
    }

    private static string Dedent(string line)
    {
        if (line.StartsWith('\t'))
        {
            return line[1..];
        }

        var spaces = 0;

        while (spaces < 4 && spaces < line.Length && line[spaces] == ' ')
        {
            spaces++;
        }

        return line[spaces..];
    }

    private static string? Fence(string trimmed)
    {
        var marker = trimmed.StartsWith("```", StringComparison.Ordinal) ? "```"
            : trimmed.StartsWith("~~~", StringComparison.Ordinal) ? "~~~"
            : null;

        // `~~~~weg~~~~` is struck-through text, not a fence with a strange language.
        return marker is not null && !trimmed[3..].Contains(marker[0]) ? marker : null;
    }

    private static bool Heading(string trimmed, out int level, out string title)
    {
        level = 0;

        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level is 0 or > 6 || level >= trimmed.Length || trimmed[level] != ' ')
        {
            title = string.Empty;
            return false;
        }

        title = trimmed[(level + 1)..].Trim();
        return true;
    }

    private static bool IsRule(string trimmed)
    {
        if (trimmed.Length < 3)
        {
            return false;
        }

        var first = trimmed[0];
        return first is '-' or '*' or '_' && trimmed.All(c => c == first);
    }

    private static bool ListItem(string line, out int indent, out char marker, out string text)
    {
        indent = 0;
        marker = '\0';
        text = string.Empty;

        var position = 0;

        while (position < line.Length && line[position] is ' ' or '\t')
        {
            indent += line[position] == '\t' ? 4 : 1;
            position++;
        }

        if (position >= line.Length)
        {
            return false;
        }

        if (line[position] is '-' or '*' or '+')
        {
            if (position + 1 >= line.Length || line[position + 1] != ' ')
            {
                return false;
            }

            marker = '*';
            text = line[(position + 2)..].Trim();
            return true;
        }

        var digits = position;

        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits == position
            || digits + 1 >= line.Length
            || line[digits] is not ('.' or ')')
            || line[digits + 1] != ' ')
        {
            return false;
        }

        marker = '#';
        text = line[(digits + 2)..].Trim();
        return true;
    }

    /// <summary>Nesting follows the indentation the caller used, be it two spaces or four.</summary>
    private static string Prefix(List<(int Indent, char Marker)> list, int indent, char marker)
    {
        while (list.Count > 0 && indent < list[^1].Indent)
        {
            list.RemoveAt(list.Count - 1);
        }

        if (list.Count == 0 || indent > list[^1].Indent)
        {
            list.Add((indent, marker));
        }
        else
        {
            list[^1] = (list[^1].Indent, marker);
        }

        var prefix = new StringBuilder();

        foreach (var level in list)
        {
            prefix.Append(level.Marker);
        }

        return prefix.ToString();
    }

    private static IReadOnlyList<string>? Cells(string trimmed)
    {
        if (trimmed.Length < 2 || !trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
        {
            return null;
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        var last = trimmed.Length - 1;

        for (var index = 1; index < last; index++)
        {
            var current = trimmed[index];

            if (current == '\\' && index + 1 < last)
            {
                cell.Append(current).Append(trimmed[index + 1]);
                index++;
                continue;
            }

            if (current == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            cell.Append(current);
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    private static bool IsSeparator(IReadOnlyList<string> cells) =>
        cells.Count > 0
        && cells.All(cell => cell.Length > 0 && cell.Trim(':').Length > 0 && cell.Trim(':').All(c => c == '-'));

    /// <summary>Prose that happens to look like a JIRA block must not become one.</summary>
    private static string Body(string text) =>
        string.Join("\n\n", text.Split("\n\n").Select(part => (Leads(part) ? "\\" : string.Empty) + Inline(part)));

    private static bool Leads(string text) =>
        text.StartsWith('#')
        || text.StartsWith("bq.", StringComparison.Ordinal)
        || (text.Length >= 3 && text[0] == 'h' && text[1] is >= '1' and <= '6' && text[2] == '.');

    private static string Inline(string text)
    {
        var result = new StringBuilder();
        var position = 0;

        while (position < text.Length)
        {
            var current = text[position];

            if (current == '\\' && position + 1 < text.Length)
            {
                var guarded = text[position + 1];

                if (Markup.Contains(guarded))
                {
                    result.Append('\\');
                }

                result.Append(guarded);
                position += 2;
                continue;
            }

            if (current == '`' && text.IndexOf('`', position + 1) is var close and > 0)
            {
                result.Append(Monospace(text[(position + 1)..close]));
                position = close + 1;
                continue;
            }

            if (current == '!'
                && position + 1 < text.Length
                && text[position + 1] == '['
                && Link(text, position + 1, out _, out var source, out var past))
            {
                result.Append('!').Append(source).Append('!');
                position = past;
                continue;
            }

            if (current == '[' && Link(text, position, out var label, out var url, out var after))
            {
                result.Append('[');

                if (label.Length > 0 && !string.Equals(label, url, StringComparison.Ordinal))
                {
                    result.Append(Inline(label)).Append('|');
                }

                result.Append(url).Append(']');
                position = after;
                continue;
            }

            if (current == '~' && Run(text, position, "~~", out var struck, out var strike))
            {
                result.Append('-').Append(Inline(struck)).Append('-');
                position += strike;
                continue;
            }

            if (current is '*' or '_' && Emphasis(text, position, out var inner, out var width, out var length))
            {
                var opening = width switch { 1 => "_", 2 => "*", _ => "*_" };
                var closing = width switch { 1 => "_", 2 => "*", _ => "_*" };

                result.Append(opening).Append(Inline(inner)).Append(closing);
                position += length;
                continue;
            }

            if (Guards(text, position))
            {
                result.Append('\\');
            }

            result.Append(current);
            position++;
        }

        return result.ToString();
    }

    /// <summary>A backslash is no help inside <c>{{…}}</c>; JIRA wants a brace pair there.</summary>
    private static string Monospace(string content) =>
        content.IndexOfAny(['*', '_', '{', '}']) < 0 ? "{{" + content + "}}" : "{{{}" + content + "{}}}";

    /// <summary>
    /// Escapes only what JIRA would really read as markup: at a word boundary for the paired
    /// markers, always for the ones that have no such rule. <c>Ende-zu-Ende</c> stays as it is.
    /// </summary>
    private static bool Guards(string text, int position)
    {
        var current = text[position];

        if (current is '{' or '[')
        {
            return true;
        }

        if (current == '?')
        {
            return position + 1 < text.Length && text[position + 1] == '?';
        }

        if (current is not ('*' or '_' or '-' or '+' or '^' or '~' or '!'))
        {
            return false;
        }

        var before = position == 0 ? ' ' : text[position - 1];
        var after = position + 1 < text.Length ? text[position + 1] : ' ';
        var opens = before == ' ' && after != ' ';

        // An image needs `!url!`; a single `!` ending a sentence is harmless.
        return current == '!' ? opens : opens || (before != ' ' && after == ' ');
    }

    private static bool Link(string text, int position, out string label, out string url, out int after)
    {
        label = string.Empty;
        url = string.Empty;
        after = position;

        var close = text.IndexOf(']', position + 1);

        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
        {
            return false;
        }

        var end = text.IndexOf(')', close + 2);

        if (end < 0)
        {
            return false;
        }

        label = text[(position + 1)..close];
        url = text[(close + 2)..end].Trim();
        after = end + 1;
        return true;
    }

    private static bool Run(string text, int position, string token, out string inner, out int length)
    {
        inner = string.Empty;
        length = 0;

        if (!text.AsSpan(position).StartsWith(token, StringComparison.Ordinal))
        {
            return false;
        }

        var start = position + token.Length;

        if (start >= text.Length || text[start] == ' ')
        {
            return false;
        }

        var end = text.IndexOf(token, start, StringComparison.Ordinal);

        if (end < 0)
        {
            return false;
        }

        inner = text[start..end];
        length = end + token.Length - position;
        return true;
    }

    /// <summary>
    /// Underscores only mark emphasis at a word boundary — otherwise every
    /// <c>customfield_10682</c> would turn into italics.
    /// </summary>
    private static bool Emphasis(string text, int position, out string inner, out int width, out int length)
    {
        inner = string.Empty;
        length = 0;

        var marker = text[position];
        width = 1;

        while (width < 3 && position + width < text.Length && text[position + width] == marker)
        {
            width++;
        }

        if (marker == '_' && position > 0 && (char.IsLetterOrDigit(text[position - 1]) || text[position - 1] == '_'))
        {
            return false;
        }

        if (!Run(text, position, new string(marker, width), out inner, out length) || inner.Length == 0)
        {
            return false;
        }

        return marker != '_'
            || position + length >= text.Length
            || !char.IsLetterOrDigit(text[position + length]);
    }
}
