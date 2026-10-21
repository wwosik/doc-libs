using System.Globalization;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams;

/// <summary>
/// The CSS of Mermaider's SVG, resolved to plain values: custom properties from the root's <c>style</c> attribute and
/// the <c>svg { … }</c> rule, <c>var()</c> with fallbacks, <c>color-mix(in srgb, …)</c>, hex colours and
/// <c>px</c> / <c>rem</c> / <c>em</c> lengths. Only this grammar is understood; anything else throws
/// <see cref="InvalidDataException"/>, so a change in Mermaider's output fails loudly instead of drawing wrong colours.
/// </summary>
internal sealed partial class SvgStyle
{
    private const double RootFontSize = 16;

    private readonly Dictionary<string, string> properties = new(StringComparer.Ordinal);

    /// <summary>Colours of gradients by id, for <c>url(#id)</c>: drawings have no gradients, so the first stop stands in.</summary>
    public Dictionary<string, string> Gradients { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads the custom properties of <paramref name="rootStyle"/> and of the <c>svg</c> rule in <paramref name="styleSheet"/>.</summary>
    public static SvgStyle Read(string? rootStyle, string? styleSheet)
    {
        var style = new SvgStyle();
        foreach (Match rule in SvgRule().Matches(styleSheet ?? string.Empty))
        {
            style.Declarations(rule.Groups[1].Value);
        }

        style.Declarations(rootStyle ?? string.Empty);
        return style;
    }

    /// <summary>A colour as <c>RRGGBB</c>, or <see langword="null"/> for <c>none</c> and <c>transparent</c>.</summary>
    public string? Color(string value) => this.Evaluate(value.Trim(), 0) switch
    {
        null => null,
        var color when HexColor().IsMatch(color) => color,
        var other => throw Unsupported("colour", value + (other == value ? string.Empty : $" (resolved to '{other}')")),
    };

    /// <summary>A length in pixels.</summary>
    public double Length(string value)
    {
        var resolved = this.Evaluate(value.Trim(), 0) ?? throw Unsupported("length", value);
        var match = LengthPattern().Match(resolved);
        if (!match.Success)
        {
            throw Unsupported("length", value);
        }

        var number = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "rem" or "em" => number * RootFontSize,
            _ => number,
        };
    }

    private void Declarations(string block)
    {
        foreach (var declaration in SplitTopLevel(block, ';'))
        {
            var colon = declaration.IndexOf(':', StringComparison.Ordinal);
            var name = colon < 0 ? string.Empty : declaration[..colon].Trim();
            if (name.StartsWith("--", StringComparison.Ordinal))
            {
                this.properties[name] = declaration[(colon + 1)..].Trim();
            }
        }
    }

    // A value with var(), color-mix() and url() replaced; null for none / transparent.
    private string? Evaluate(string value, int depth)
    {
        if (depth > 32)
        {
            throw Unsupported("value", value + " (the custom properties refer to each other in a cycle)");
        }

        if (value is "none" or "transparent")
        {
            return null;
        }

        if (Function(value, "var") is { } variable)
        {
            var arguments = SplitTopLevel(variable, ',', limit: 2);
            var name = arguments[0].Trim();
            return this.properties.TryGetValue(name, out var defined) ? this.Evaluate(defined, depth + 1)
                : arguments.Count > 1 ? this.Evaluate(arguments[1].Trim(), depth + 1)
                : throw Unsupported("value", $"{value} ('{name}' is not defined)");
        }

        if (Function(value, "color-mix") is { } mix)
        {
            return this.Mix(value, mix, depth);
        }

        if (Function(value, "url") is { } url)
        {
            var id = url.Trim().Trim('\'', '"');
            return id.StartsWith('#') && this.Gradients.TryGetValue(id[1..], out var color)
                ? this.Evaluate(color, depth + 1)
                : throw Unsupported("paint", value);
        }

        return value.StartsWith('#') ? Hex(value) ?? throw Unsupported("colour", value)
            : value is "white" ? "FFFFFF"
            : value is "black" ? "000000"
            : value;
    }

    // color-mix(in srgb, A p%, B q%): a missing percentage is the rest of 100, both missing are 50 % each.
    private string Mix(string value, string arguments, int depth)
    {
        var parts = SplitTopLevel(arguments, ',');
        if (parts.Count != 3 || parts[0].Trim() != "in srgb")
        {
            throw Unsupported("colour", value);
        }

        var (first, p1) = this.MixPart(parts[1], value, depth);
        var (second, p2) = this.MixPart(parts[2], value, depth);
        var share = (p1, p2) switch
        {
            ({ } a, { } b) when a + b > 0 => a / (a + b),
            ({ } a, null) => a,
            (null, { } b) => 1 - b,
            _ => 0.5,
        };
        int Channel(string hex, int index) => int.Parse(hex.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return string.Concat(Enumerable.Range(0, 3).Select(i =>
            ((int)Math.Round((Channel(first, i) * share) + (Channel(second, i) * (1 - share)), MidpointRounding.AwayFromZero)).ToString("X2", CultureInfo.InvariantCulture)));
    }

    private (string Color, double? Share) MixPart(string part, string value, int depth)
    {
        var match = MixArgument().Match(part.Trim());
        var color = this.Evaluate(match.Success ? match.Groups[1].Value : part.Trim(), depth + 1);
        if (color is null || !HexColor().IsMatch(color))
        {
            throw Unsupported("colour", value);
        }

        return (color, match.Success ? double.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture) / 100 : null);
    }

    // The arguments of name( … ) when the value is exactly that call.
    private static string? Function(string value, string name) =>
        value.StartsWith(name + "(", StringComparison.Ordinal) && value.EndsWith(')') && Closes(value, name.Length)
            ? value[(name.Length + 1)..^1]
            : null;

    // Whether the parenthesis at `open` closes at the very end.
    private static bool Closes(string value, int open)
    {
        var depth = 0;
        for (var i = open; i < value.Length; i++)
        {
            depth += value[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i == value.Length - 1;
            }
        }

        return false;
    }

    private static List<string> SplitTopLevel(string text, char separator, int limit = int.MaxValue)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (text[i] == separator && depth == 0 && parts.Count < limit - 1)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts.Where(p => p.Trim().Length > 0 || separator == ',').ToList();
    }

    private static string? Hex(string value)
    {
        var hex = value[1..];
        return hex.Length switch
        {
            3 when HexDigits().IsMatch(hex) => string.Concat(hex.Select(c => new string(char.ToUpperInvariant(c), 2))),
            6 or 8 when HexDigits().IsMatch(hex) => hex[..6].ToUpperInvariant(),
            _ => null,
        };
    }

    private static InvalidDataException Unsupported(string kind, string value) =>
        new($"The diagram's SVG has the {kind} '{value}', which cannot be drawn.");

    [GeneratedRegex(@"(?:^|\})\s*svg\s*\{([^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex SvgRule();

    [GeneratedRegex("^[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    [GeneratedRegex("^[0-9A-Fa-f]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HexDigits();

    [GeneratedRegex(@"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*(px|rem|em)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LengthPattern();

    [GeneratedRegex(@"^(.*\S)\s+([-+]?(?:\d+\.?\d*|\.\d+))%$", RegexOptions.CultureInvariant)]
    private static partial Regex MixArgument();
}
