using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Icons the generators draw for <c>:icon-name:</c> in the Markdown, by name. Each icon is a <see cref="Drawing"/> of
/// one colour: the generators draw every stroke and fill of it in the colour of its place (the accent colour as a
/// bullet or a card's figure, the text colour inline), so the colours in the drawing do not matter.
/// </summary>
public interface IIconSet
{
    /// <summary>The names, in kebab-case (<c>circle-check</c>), as written after <c>:icon-</c>.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>The icon of that name; <see langword="null"/> when the set has none.</summary>
    Drawing? Find(string name);
}

/// <summary>
/// The icons of Tabler Icons (MIT, <see href="https://tabler.io/icons"/>), embedded in this package: the whole outline set
/// of the version in <see cref="Version"/>.
/// </summary>
public static class TablerIcons
{
    /// <summary>The Tabler Icons release the set was imported from (<c>tools/icon-import</c>).</summary>
    public const string Version = "3.52.0";

    /// <summary>
    /// The outline icons: 24 × 24 drawings of lines 2 px wide with round ends and joins, unfilled, as Tabler draws them.
    /// The generators use them by default. The data is read on the first lookup, and each drawing built when first asked
    /// for, then kept.
    /// </summary>
    public static IIconSet Outline { get; } = new EmbeddedIconSet("Icons.tabler-outline.txt.gz");
}

/// <summary>A gzip-compressed resource of lines <c>name&lt;TAB&gt;path data</c>, path data absolute M/L/C/Z only.</summary>
internal sealed class EmbeddedIconSet(string resource) : IIconSet
{
    private const double Size = 24;
    private const double StrokeWidth = 2;

    private readonly Lazy<Dictionary<string, string>> paths = new(() => Load(resource));
    private readonly ConcurrentDictionary<string, Drawing> drawings = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Names => this.paths.Value.Keys;

    public Drawing? Find(string name)
    {
        if (this.drawings.TryGetValue(name, out var drawing))
        {
            return drawing;
        }

        if (!this.paths.Value.TryGetValue(name, out var data))
        {
            return null;
        }

        drawing = new Drawing(Size, Size, [new DrawingShape(IconPaths.Parse(data), null, "000000", StrokeWidth) { RoundJoins = true }]);
        return this.drawings.GetOrAdd(name, drawing);
    }

    private static Dictionary<string, string> Load(string resource)
    {
        using var stream = typeof(EmbeddedIconSet).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The icon resource '{resource}' is missing from the assembly.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 0)
            {
                paths[line[..tab]] = line[(tab + 1)..];
            }
        }

        return paths;
    }
}

/// <summary>Reads the path data the import tool writes: absolute <c>M</c>, <c>L</c>, <c>C</c> and <c>Z</c>, numbers in between.</summary>
internal static class IconPaths
{
    /// <exception cref="FormatException">The data is not of that form.</exception>
    public static DrawingPath Parse(string data)
    {
        var segments = new List<PathSegment>();
        var position = 0;
        double Number()
        {
            while (position < data.Length && data[position] == ' ')
            {
                position++;
            }

            var start = position;
            if (position < data.Length && data[position] == '-')
            {
                position++;
            }

            var dot = false;
            while (position < data.Length && (char.IsAsciiDigit(data[position]) || (data[position] == '.' && !dot)))
            {
                dot |= data[position] == '.';
                position++;
            }

            return position > start && double.TryParse(data.AsSpan(start, position - start), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException($"A number was expected at {start} in the icon path '{data}'.");
        }

        while (position < data.Length)
        {
            var command = data[position++];
            if (command == ' ')
            {
                continue;
            }

            segments.Add(command switch
            {
                'M' => new MoveTo(Number(), Number()),
                'L' => new LineTo(Number(), Number()),
                'C' => new CubicTo(Number(), Number(), Number(), Number(), Number(), Number()),
                'Z' => new ClosePath(),
                _ => throw new FormatException($"The command '{command}' in the icon path '{data}' is not M, L, C or Z."),
            });
        }

        return new DrawingPath(segments);
    }
}

/// <summary>An icon found in the text of a run: its name, its drawing, and the line of its paragraph.</summary>
internal sealed record InlineIcon(string Name, Drawing Drawing, int Line);

/// <summary>
/// <c>:icon-name:</c> in running text: the icon of that name from an <see cref="IIconSet"/>. Found in the runs' text after
/// Markdown has been parsed, never inside code. Without a set, the text stays as it is.
/// </summary>
internal static partial class IconSyntax
{
    /// <summary>
    /// <paramref name="runs"/> with every <c>:icon-name:</c> taken out of its run into a run of its own, made by
    /// <paramref name="icon"/> from the run it was in. Runs are left alone when <paramref name="set"/> is null.
    /// </summary>
    /// <exception cref="DocumentConversionException">An icon the set does not have; the message suggests close names.</exception>
    public static List<T> Apply<T>(
        IReadOnlyList<T> runs,
        Func<T, string> text,
        Func<T, bool> isCode,
        Func<T, string, T> withText,
        Func<T, InlineIcon, T> icon,
        IIconSet? set,
        int line,
        DocumentFormat format)
    {
        if (set is null)
        {
            return [.. runs];
        }

        var result = new List<T>();
        foreach (var run in runs)
        {
            var content = text(run);
            if (isCode(run) || !content.Contains(":icon-", StringComparison.Ordinal))
            {
                result.Add(run);
                continue;
            }

            var position = 0;
            foreach (Match match in Pattern().Matches(content))
            {
                var name = match.Groups["name"].Value;
                var drawing = set.Find(name) ?? throw Unknown(name, set, line, format);
                if (match.Index > position)
                {
                    result.Add(withText(run, content[position..match.Index]));
                }

                result.Add(icon(withText(run, string.Empty), new InlineIcon(name, drawing, line)));
                position = match.Index + match.Length;
            }

            if (position < content.Length)
            {
                result.Add(withText(run, content[position..]));
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the paragraph's text starts with <c>:icon-name:</c> (spaces before it aside), judged from its leading
    /// literals, before its runs are made. A task box first means no.
    /// </summary>
    public static bool StartsWithIcon(ParagraphBlock paragraph)
    {
        var text = new StringBuilder();
        foreach (var inline in paragraph.Inline ?? [])
        {
            if (inline is not LiteralInline literal)
            {
                break;
            }

            text.Append(literal.Content.ToString());
        }

        return StartPattern().IsMatch(text.ToString());
    }

    /// <summary>The icon with every stroke and fill in <paramref name="colour"/> (RRGGBB).</summary>
    public static Drawing Colour(Drawing icon, string colour) => icon with
    {
        Items = [.. icon.Items.Select(item => item is DrawingShape shape
            ? shape with { Fill = shape.Fill is null ? null : colour, Stroke = shape.Stroke is null ? null : colour }
            : item)],
    };

    /// <summary>The error for an unknown name, with up to three close names: by edit distance, then by common prefix.</summary>
    public static DocumentConversionException Unknown(string name, IIconSet set, int line, DocumentFormat format)
    {
        var close = set.Names
            .Select(n => (Name: n, Distance: Distance(name, n), Prefix: Prefix(name, n)))
            .OrderBy(c => c.Distance)
            .ThenByDescending(c => c.Prefix)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(c => c.Name)
            .ToList();
        var at = line > 0 ? string.Create(CultureInfo.InvariantCulture, $" at line {line}") : string.Empty;
        return new DocumentConversionException(
            format,
            $"Unknown icon '{name}'{at}." + (close.Count > 0 ? $" Did you mean {string.Join(", ", close)}?" : string.Empty));
    }

    private static int Prefix(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
        {
            i++;
        }

        return i;
    }

    // Levenshtein, two rows.
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    [GeneratedRegex(@":icon-(?<name>[a-z0-9]+(?:-[a-z0-9]+)*):", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^\s*:icon-[a-z0-9]+(?:-[a-z0-9]+)*:", RegexOptions.CultureInvariant)]
    private static partial Regex StartPattern();
}
