using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Slide settings at the end of a heading, Pandoc style: <c>## Title {.dark kicker="Label" layout="Name"}</c>, and
/// <c># Thank you {.closing}</c>. Only a trailing <c>{…}</c> made of classes and quoted <c>key="value"</c> pairs counts;
/// any other braces stay text. Of those, only <c>.dark</c>, <c>kicker</c>, <c>layout</c> and <c>.closing</c> are known,
/// anything else throws <see cref="DocumentConversionException"/>.
/// </summary>
internal sealed partial record HeadingAttributes(bool Dark, string? Kicker)
{
    /// <summary>The attributes, as they are written, for error messages.</summary>
    public const string Valid = ".dark, kicker=\"…\", layout=\"…\", .closing";

    /// <summary>The template's layout to put the slide on, by its name; <see langword="null"/>: chosen by the kind of slide.</summary>
    public string? Layout { get; init; }

    /// <summary>On a <c>#</c> heading: the closing slide, on the template's closing layout.</summary>
    public bool Closing { get; init; }

    /// <summary>
    /// Splits <paramref name="text"/>, the end of a heading, into the text before the attributes and the attributes.
    /// Without attributes, the text comes back unchanged with <see langword="null"/>.
    /// </summary>
    /// <exception cref="DocumentConversionException">The block names an unknown class or key, or a key twice.</exception>
    public static (string Text, HeadingAttributes? Attributes) Split(string text, DocumentFormat format)
    {
        var match = BlockPattern().Match(text);
        if (!match.Success)
        {
            return (text, null);
        }

        var dark = false;
        var closing = false;
        string? kicker = null;
        string? layout = null;
        foreach (Match token in TokenPattern().Matches(match.Groups[1].Value))
        {
            switch (token.Groups["class"].Success ? "." + token.Groups["class"].Value : token.Groups["key"].Value)
            {
                case ".dark":
                    dark = true;
                    break;
                case ".closing":
                    closing = true;
                    break;
                case "kicker" when kicker is null:
                    kicker = token.Groups["value"].Value;
                    break;
                case "layout" when layout is null:
                    layout = token.Groups["value"].Value;
                    break;
                case "kicker" or "layout":
                    throw new DocumentConversionException(format, $"The heading attributes '{match.Value.Trim()}' name {token.Groups["key"].Value} twice.");
                case var unknown:
                    throw new DocumentConversionException(
                        format,
                        $"Unknown heading attribute '{unknown}' in '{match.Value.Trim()}'. Valid attributes: {Valid}.");
            }
        }

        return (text[..match.Index].TrimEnd(), new HeadingAttributes(dark, kicker) { Layout = layout, Closing = closing });
    }

    [GeneratedRegex(@"\s*\{((?:\s*(?:\.[A-Za-z][\w-]*|[A-Za-z][\w-]*=""[^""]*""))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"\.(?<class>[A-Za-z][\w-]*)|(?<key>[A-Za-z][\w-]*)=""(?<value>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
