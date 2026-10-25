using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Slide settings at the end of a <c>##</c> heading, Pandoc style: <c>## Title {.dark kicker="Label"}</c>. Only a
/// trailing <c>{…}</c> made of classes and quoted <c>key="value"</c> pairs counts; any other braces stay text. Of
/// those, only <c>.dark</c> and <c>kicker</c> are known, anything else throws <see cref="DocumentConversionException"/>.
/// </summary>
internal sealed partial record HeadingAttributes(bool Dark, string? Kicker)
{
    /// <summary>The attributes, as they are written, for error messages.</summary>
    public const string Valid = ".dark, kicker=\"…\"";

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
        string? kicker = null;
        foreach (Match token in TokenPattern().Matches(match.Groups[1].Value))
        {
            switch (token.Groups["class"].Success ? "." + token.Groups["class"].Value : token.Groups["key"].Value)
            {
                case ".dark":
                    dark = true;
                    break;
                case "kicker" when kicker is null:
                    kicker = token.Groups["value"].Value;
                    break;
                case "kicker":
                    throw new DocumentConversionException(format, $"The heading attributes '{match.Value.Trim()}' name kicker twice.");
                case var unknown:
                    throw new DocumentConversionException(
                        format,
                        $"Unknown heading attribute '{unknown}' in '{match.Value.Trim()}'. Valid attributes: {Valid}.");
            }
        }

        return (text[..match.Index].TrimEnd(), new HeadingAttributes(dark, kicker));
    }

    [GeneratedRegex(@"\s*\{((?:\s*(?:\.[A-Za-z][\w-]*|[A-Za-z][\w-]*=""[^""]*""))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"\.(?<class>[A-Za-z][\w-]*)|(?<key>[A-Za-z][\w-]*)=""(?<value>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
