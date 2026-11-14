using System.Globalization;
using System.Text;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// What a drawn container is drawn with: the brand (in decks the slide's, so dark slides get light lines and text),
/// today's date, the texts of the document language, the lines of Markdown objects in the original and the format
/// for errors.
/// </summary>
/// <param name="Brand">The brand to draw in.</param>
/// <param name="Today">Today, unless the container pins it with <c>today=</c>.</param>
/// <param name="Texts">The document language's texts, for dates.</param>
/// <param name="LineOf">The line of a Markdown object in the original, from 1.</param>
/// <param name="Format">The format of the document, for errors.</param>
internal sealed record VisualContext(ResolvedBrand Brand, DateOnly Today, GeneratedTexts Texts, Func<MarkdownObject, int> LineOf, DocumentFormat Format)
{
    /// <summary>Today as a generator sees it: the clock's date in UTC.</summary>
    public static DateOnly TodayOf(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>An error in the document, in its format.</summary>
    public DocumentConversionException Error(string message) => new(this.Format, message);
}

/// <summary>
/// The <c>:::</c> containers that both generators draw as a <see cref="Drawing"/> from the Markdown inside them:
/// <c>timeline</c> and <c>gantt</c>. Decks put the drawing on a flow slide as editable shapes, PDFs draw it as
/// vectors. Arguments are <c>key=value</c> pairs after the name, checked by <see cref="Check"/> through
/// <see cref="LayoutSyntax.CheckContainer"/>, so both formats reject the same mistakes. A new drawn container adds its
/// name to <see cref="Names"/>, its arguments to <see cref="Arguments"/> and its drawing to <see cref="Draw"/>.
/// </summary>
internal static class VisualContainers
{
    /// <summary>The drawn containers, in the order the guides list them.</summary>
    public static readonly IReadOnlyList<string> Names = ["timeline", "gantt"];

    /// <summary>The argument <c>today=YYYY-MM-DD</c>, which pins today's date.</summary>
    public const string Today = "today";

    /// <summary>The argument <c>grid=week|month|quarter</c> of a Gantt chart.</summary>
    public const string Grid = "grid";

    /// <summary>The grids of a Gantt chart.</summary>
    public static readonly IReadOnlyList<string> Grids = ["week", "month", "quarter"];

    /// <summary>Whether <paramref name="name"/> is a drawn container.</summary>
    public static bool IsVisual(string name) => Names.Contains(name, StringComparer.Ordinal);

    /// <summary>The arguments each drawn container takes, as the error messages show them.</summary>
    public static IReadOnlyList<string> Arguments(string name) => name switch
    {
        "gantt" => [Grid + "=" + string.Join("|", Grids), Today + "=YYYY-MM-DD"],
        _ => [Today + "=YYYY-MM-DD"],
    };

    /// <summary>Checks the arguments of a drawn container.</summary>
    /// <exception cref="DocumentConversionException">An argument is not <c>key=value</c>, unknown, repeated or invalid.</exception>
    public static void Check(string name, string arguments, DocumentFormat format) => Parse(name, arguments, format);

    /// <summary>The arguments of a drawn container by key.</summary>
    /// <exception cref="DocumentConversionException">An argument is not <c>key=value</c>, unknown, repeated or invalid.</exception>
    public static IReadOnlyDictionary<string, string> Parse(string name, string arguments, DocumentFormat format)
    {
        var allowed = Arguments(name);
        var keys = allowed.Select(a => a[..a.IndexOf('=', StringComparison.Ordinal)]).ToList();
        var takes = $"The container '::: {name}' takes {string.Join(" and ", allowed)}";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = token.IndexOf('=', StringComparison.Ordinal);
            var key = equals > 0 ? token[..equals] : token;
            var value = equals > 0 ? token[(equals + 1)..] : string.Empty;
            if (equals <= 0 || value.Length == 0 || !keys.Contains(key, StringComparer.Ordinal))
            {
                throw new DocumentConversionException(format, $"{takes}, not '{token}'.");
            }

            if (!result.TryAdd(key, value))
            {
                throw new DocumentConversionException(format, $"The container '::: {name} {arguments}' has {key} twice; keep one.");
            }

            var valid = key switch
            {
                Today => SchedulePeriod.Parse(value) is { Kind: PeriodKind.Day },
                Grid => Grids.Contains(value, StringComparer.Ordinal),
                _ => true,
            };
            if (!valid)
            {
                throw new DocumentConversionException(
                    format,
                    $"The container '::: {name}' has {token}; {allowed[keys.IndexOf(key)]} expected{(key == Today ? ", such as today=2026-11-01" : string.Empty)}.");
            }
        }

        return result;
    }

    /// <summary>Draws a drawn container.</summary>
    /// <exception cref="DocumentConversionException">Its content is not what the container takes; the message names the line.</exception>
    public static Drawing Draw(CustomContainer container, VisualContext context)
    {
        var name = container.Info ?? string.Empty;
        var arguments = Parse(name, container.Arguments?.Trim() ?? string.Empty, context.Format);
        if (arguments.TryGetValue(Today, out var today))
        {
            context = context with { Today = SchedulePeriod.Parse(today)!.Start };
        }

        return name switch
        {
            "timeline" => TimelineDrawing.Draw(container, context),
            "gantt" => GanttDrawing.Draw(container, context, arguments.GetValueOrDefault(Grid)),
            _ => throw new ArgumentException($"'{name}' is not a drawn container.", nameof(container)),
        };
    }

    /// <summary>" at line 12", or nothing for an object without a line.</summary>
    public static string At(MarkdownObject block, VisualContext context) =>
        context.LineOf(block) is var line and > 0 ? string.Create(CultureInfo.InvariantCulture, $" at line {line}") : string.Empty;
}

/// <summary>
/// Markdown text in a drawing: inlines as <see cref="DrawingSpan"/>s, and lines broken to a width. Bold stays bold,
/// other emphasis and links become plain text, code its text (a line that is only code is set in the mono font).
/// Widths are estimated, since the real font is only known when the drawing is placed; a <see cref="DrawingText.MaxWidth"/>
/// on each line makes up for the estimate.
/// </summary>
internal static class DrawingTexts
{
    // Average advance per character in ems, regular and bold, for breaking lines.
    private const double Average = 0.52;
    private const double AverageBold = 0.57;

    /// <summary>The spans of an inline and its siblings after it, merged where the weight is the same.</summary>
    public static List<DrawingSpan> Spans(Inline? first, bool bold = false)
    {
        var spans = new List<DrawingSpan>();
        for (var inline = first; inline is not null; inline = inline.NextSibling)
        {
            Add(inline, bold, spans);
        }

        return Merge(spans);
    }

    /// <summary>Whether the inlines are one code span, alone.</summary>
    public static bool IsCode(Inline? first) => first is CodeInline && first.NextSibling is null;

    /// <summary>The text of the spans.</summary>
    public static string Text(IEnumerable<DrawingSpan> spans) => string.Concat(spans.Select(s => s.Text));

    /// <summary>The spans without white space at their start and end, and without empty spans.</summary>
    public static List<DrawingSpan> Trim(IReadOnlyList<DrawingSpan> spans)
    {
        var result = spans.Where(s => s.Text.Length > 0).ToList();
        while (result.Count > 0 && result[0].Text.TrimStart().Length == 0)
        {
            result.RemoveAt(0);
        }

        while (result.Count > 0 && result[^1].Text.TrimEnd().Length == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        if (result.Count > 0)
        {
            result[0] = result[0] with { Text = result[0].Text.TrimStart() };
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        }

        return result;
    }

    /// <summary>
    /// The spans split at the first <paramref name="separator"/> outside bold text, both parts trimmed;
    /// <see langword="null"/> for the second part when there is none.
    /// </summary>
    public static (List<DrawingSpan> Before, List<DrawingSpan>? After) Split(IReadOnlyList<DrawingSpan> spans, char separator)
    {
        for (var i = 0; i < spans.Count; i++)
        {
            var at = spans[i].Bold ? -1 : spans[i].Text.IndexOf(separator, StringComparison.Ordinal);
            if (at >= 0)
            {
                var before = spans.Take(i).Append(spans[i] with { Text = spans[i].Text[..at] }).ToList();
                var after = new[] { spans[i] with { Text = spans[i].Text[(at + 1)..] } }.Concat(spans.Skip(i + 1)).ToList();
                return (Trim(before), Trim(after));
            }
        }

        return (Trim(spans), null);
    }

    /// <summary>
    /// The spans broken between words into lines of at most <paramref name="width"/> pixels at <paramref name="size"/>,
    /// at most <paramref name="maxLines"/>; what does not fit is cut off with "…".
    /// </summary>
    public static List<List<DrawingSpan>> Lines(IReadOnlyList<DrawingSpan> spans, double size, double width, int maxLines)
    {
        // Words, each in pieces of one weight: "**Q**2" is one word of two pieces.
        var words = new List<List<DrawingSpan>> { new() };
        foreach (var span in spans)
        {
            var parts = span.Text.Split(' ');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    words.Add([]);
                }

                if (parts[i].Length > 0)
                {
                    words[^1].Add(span with { Text = parts[i] });
                }
            }
        }

        var lines = new List<List<DrawingSpan>>();
        var line = new List<DrawingSpan>();
        double used = 0;
        foreach (var word in words.Where(w => w.Count > 0))
        {
            var advance = word.Sum(p => Width(p.Text, p.Bold, size));
            var space = Width(" ", false, size);
            if (line.Count > 0 && used + space + advance > width)
            {
                lines.Add(line);
                line = [];
                used = 0;
            }

            if (line.Count > 0)
            {
                line.Add(word[0] with { Text = " " });
                used += space;
            }

            line.AddRange(word);
            used += advance;
        }

        if (line.Count > 0)
        {
            lines.Add(line);
        }

        if (lines.Count > maxLines)
        {
            var last = lines[maxLines - 1];
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            last[^1] = last[^1] with { Text = last[^1].Text + "…" };
        }

        return [.. lines.Select(Merge)];
    }

    /// <summary>The estimated width of a text in pixels.</summary>
    public static double Width(string text, bool bold, double size) => text.Length * size * (bold ? AverageBold : Average);

    private static void Add(Inline inline, bool bold, List<DrawingSpan> spans)
    {
        switch (inline)
        {
            case LiteralInline literal:
                spans.Add(new DrawingSpan(literal.Content.ToString(), bold));
                break;
            case CodeInline code:
                spans.Add(new DrawingSpan(code.Content, bold));
                break;
            case HtmlEntityInline entity:
                spans.Add(new DrawingSpan(entity.Transcoded.ToString(), bold));
                break;
            case LineBreakInline:
                spans.Add(new DrawingSpan(" ", bold));
                break;
            case AutolinkInline link:
                spans.Add(new DrawingSpan(link.Url, bold));
                break;
            case EmphasisInline emphasis:
                var strong = bold || (emphasis.DelimiterCount == 2 && emphasis.DelimiterChar is '*' or '_');
                for (var child = emphasis.FirstChild; child is not null; child = child.NextSibling)
                {
                    Add(child, strong, spans);
                }

                break;
            case ContainerInline container:
                for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                {
                    Add(child, bold, spans);
                }

                break;
        }
    }

    private static List<DrawingSpan> Merge(List<DrawingSpan> spans)
    {
        var merged = new List<DrawingSpan>();
        foreach (var span in spans)
        {
            if (merged.Count > 0 && merged[^1].Bold == span.Bold && merged[^1].Color == span.Color)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }

    /// <summary>The plain text of inlines, for dates and messages.</summary>
    public static string Plain(Inline? first) => Text(Spans(first));

    /// <summary>The plain text of a block's inlines; empty for a block without.</summary>
    public static string Plain(LeafBlock? block) => block?.Inline is { } inline ? Plain(inline.FirstChild) : string.Empty;

    /// <summary>Quotes <paramref name="text"/> for an error message, shortened.</summary>
    public static string Quote(string text)
    {
        var builder = new StringBuilder(text.Trim());
        if (builder.Length > 40)
        {
            builder.Length = 40;
            builder.Append('…');
        }

        return builder.ToString();
    }
}
