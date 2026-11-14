using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Slide settings at the end of a heading, Pandoc style: <c>## Title {.dark kicker="Label" layout="Name"}</c>, and
/// <c># Thank you {.closing}</c>. Only a trailing <c>{…}</c> made of classes and quoted <c>key="value"</c> pairs counts;
/// any other braces stay text. Of those, only <c>.dark</c>, <c>kicker</c>, <c>layout</c>, <c>transition</c>,
/// <c>.closing</c> and the slide kinds (<see cref="SlideKinds.Names"/>) are known, anything else throws
/// <see cref="DocumentConversionException"/>.
/// </summary>
internal sealed partial record HeadingAttributes(bool Dark, string? Kicker)
{
    /// <summary>The attributes, as they are written, for error messages.</summary>
    public const string Valid = ".dark, kicker=\"…\", layout=\"…\", transition=\"…\", .closing, .agenda, .quote, .statement, .contact, .chart";

    /// <summary>The template's layout to put the slide on, by its name; <see langword="null"/>: chosen by the kind of slide.</summary>
    public string? Layout { get; init; }

    /// <summary>The transition to the slide, one of <see cref="LayoutSyntax.Transitions"/>; <see langword="null"/>: the deck's.</summary>
    public string? Transition { get; init; }

    /// <summary>On a <c>#</c> heading: the closing slide, on the template's closing layout.</summary>
    public bool Closing { get; init; }

    /// <summary>The kind of slide (<see cref="SlideKinds.Names"/>, without the dot); <see langword="null"/>: none.</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// <c>.unnumbered</c>: the heading gets no number and does not count. Known only to Word documents for now (the
    /// other formats read it with heading numbers, E03-S02), so it is unknown elsewhere, as before.
    /// </summary>
    public bool Unnumbered { get; init; }

    /// <summary>
    /// Splits the end of a heading of level 3 to 6, where slide settings do not apply: a trailing <c>{…}</c> is taken
    /// off only when every token in it is one that applies at that level (<c>.unnumbered</c>, in
    /// <paramref name="format"/>s that know it); any other braces stay text, without an error.
    /// </summary>
    public static (string Text, HeadingAttributes? Attributes) SplitDeep(string text, DocumentFormat format)
    {
        var match = BlockPattern().Match(text);
        if (!match.Success || !KnowsUnnumbered(format))
        {
            return (text, null);
        }

        var tokens = TokenPattern().Matches(match.Groups[1].Value);
        return tokens.All(t => t.Groups["class"].Success && t.Groups["class"].Value == "unnumbered")
            ? (text[..match.Index].TrimEnd(), new HeadingAttributes(false, null) { Unnumbered = true })
            : (text, null);
    }

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
        string? transition = null;
        string? kind = null;
        var unnumbered = false;
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
                case ".unnumbered" when KnowsUnnumbered(format):
                    unnumbered = true;
                    break;
                case var name when name.Length > 1 && name[0] == '.' && SlideKinds.Names.Contains(name[1..]):
                    if (kind is not null && kind != name[1..])
                    {
                        throw new DocumentConversionException(format, $"The heading attributes '{match.Value.Trim()}' name two kinds of slide, .{kind} and {name}; a slide is one kind.");
                    }

                    kind = name[1..];
                    break;
                case "kicker" when kicker is null:
                    kicker = token.Groups["value"].Value;
                    break;
                case "layout" when layout is null:
                    layout = token.Groups["value"].Value;
                    break;
                case "transition" when transition is null:
                    transition = LayoutSyntax.Transition(token.Groups["value"].Value, format);
                    break;
                case "kicker" or "layout" or "transition":
                    throw new DocumentConversionException(format, $"The heading attributes '{match.Value.Trim()}' name {token.Groups["key"].Value} twice.");
                case var unknown:
                    throw new DocumentConversionException(
                        format,
                        $"Unknown heading attribute '{unknown}' in '{match.Value.Trim()}'. Valid attributes: {Valid}{(KnowsUnnumbered(format) ? ", .unnumbered" : string.Empty)}.");
            }
        }

        if (kind is not null && dark)
        {
            throw new DocumentConversionException(format, $"The heading attributes '{match.Value.Trim()}' combine .{kind} with .dark; a kind of slide has its own layout, so leave out .dark.");
        }

        return (text[..match.Index].TrimEnd(), new HeadingAttributes(dark, kicker) { Layout = layout, Transition = transition, Closing = closing, Kind = kind, Unnumbered = unnumbered });
    }

    // `.unnumbered` comes with heading numbers: in Word documents now, in PDFs and decks with E03-S02.
    private static bool KnowsUnnumbered(DocumentFormat format) => format == DocumentFormat.Docx;

    [GeneratedRegex(@"\s*\{((?:\s*(?:\.[A-Za-z][\w-]*|[A-Za-z][\w-]*=""[^""]*""))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"\.(?<class>[A-Za-z][\w-]*)|(?<key>[A-Za-z][\w-]*)=""(?<value>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
