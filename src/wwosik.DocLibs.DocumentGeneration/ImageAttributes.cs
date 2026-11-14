using System.Globalization;
using System.Text.RegularExpressions;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>How an image fills the box it is placed in.</summary>
internal enum ImageFit
{
    /// <summary>The whole image, as large as fits, keeping its aspect ratio: the default.</summary>
    Contain,

    /// <summary>The whole box, keeping the aspect ratio; what sticks out is cropped, by <see cref="ImageAttributes.Focus"/>.</summary>
    Cover,
}

/// <summary>Which part of an image stays when it is cropped.</summary>
internal enum ImageFocus
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>What is cut off each side of an image, as shares of its width and height, from 0 to 1.</summary>
internal readonly record struct ImageCrop(double Left, double Top, double Right, double Bottom)
{
    public bool IsNone => this.Left <= 0 && this.Top <= 0 && this.Right <= 0 && this.Bottom <= 0;

    /// <summary>The share of the width and of the height that stays.</summary>
    public (double Width, double Height) Visible => (1 - this.Left - this.Right, 1 - this.Top - this.Bottom);
}

/// <summary>
/// Settings in braces right after an image, Pandoc style: <c>![Team](team.jpg){focus=top fit=cover scale=0.8 .round .grey}</c>.
/// Only a <c>{…}</c> that directly follows the image and is made of classes and <c>key=value</c> pairs (the value
/// quoted or not) counts; any other braces stay text. Of those, only the names in <see cref="Valid"/> are known, anything
/// else throws <see cref="DocumentConversionException"/>.
/// </summary>
internal sealed partial record ImageAttributes
{
    /// <summary>The attributes, as they are written, for error messages and the guides.</summary>
    public const string Valid = "focus=top|bottom|left|right|center, fit=contain|cover, scale=<number>, .round, .grey, .caption";

    // Where the attributes of an image are kept once taken off the text after it, so a second look finds them.
    private static readonly object DataKey = new();

    /// <summary>No attributes.</summary>
    public static ImageAttributes None { get; } = new();

    /// <summary>Contain (the default) or cover.</summary>
    public ImageFit Fit { get; init; }

    /// <summary>Which part stays when the image is cropped; <see langword="null"/>: not given, the centre.</summary>
    public ImageFocus? Focus { get; init; }

    /// <summary><c>scale=</c>, with the meaning <c>scale=</c> in the title has in each format; <see langword="null"/>: not given.</summary>
    public double? Scale { get; init; }

    /// <summary><c>.round</c>: cropped to a square, by <see cref="Focus"/>, then to a circle.</summary>
    public bool Round { get; init; }

    /// <summary><c>.grey</c>: shown in grey.</summary>
    public bool Grey { get; init; }

    /// <summary><c>.caption</c>: the alt text as a caption, as <c>caption</c> in the title asks.</summary>
    public bool Caption { get; init; }

    /// <summary>The block as written, <c>{…}</c>, for messages; empty for <see cref="None"/>.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether the block named anything at all.</summary>
    public bool IsEmpty => this.Text.Length == 0;

    /// <summary>
    /// The attributes of <paramref name="image"/>: a <c>{…}</c> at the very start of the literal text after it, which is
    /// taken off that text so the braces never reach the output. Asking again for the same image gives the same
    /// attributes. <paramref name="line"/> is the image's line, for messages.
    /// </summary>
    /// <exception cref="DocumentConversionException">The block names an unknown attribute or value, or one twice.</exception>
    public static ImageAttributes Take(LinkInline image, int line, DocumentFormat format)
    {
        if (image.GetData(DataKey) is ImageAttributes known)
        {
            return known;
        }

        // Markdig may split the text after the image into several literals (a lone `=` is one of its own).
        var literals = new List<LiteralInline>();
        for (var next = image.NextSibling; next is LiteralInline literal; next = literal.NextSibling)
        {
            literals.Add(literal);
        }

        var text = string.Concat(literals.Select(l => l.Content.ToString()));
        var match = BlockPattern().Match(text);
        var attributes = match.Success ? Parse(match.Value, match.Groups[1].Value, line, format) : None;
        if (match.Success)
        {
            var consumed = match.Length;
            foreach (var literal in literals)
            {
                var content = literal.Content.ToString();
                var cut = Math.Min(consumed, content.Length);
                literal.Content = new Markdig.Helpers.StringSlice(content[cut..]);
                consumed -= cut;
                if (consumed == 0)
                {
                    break;
                }
            }
        }

        image.SetData(DataKey, attributes);
        return attributes;
    }

    /// <summary>
    /// What to cut off an image of <paramref name="width"/> × <paramref name="height"/> so that it has the aspect ratio
    /// <paramref name="aspect"/> (width over height), keeping the part <see cref="Focus"/> names.
    /// </summary>
    public ImageCrop Crop(double width, double height, double aspect)
    {
        if (width <= 0 || height <= 0 || aspect <= 0)
        {
            return default;
        }

        var focus = this.Focus ?? ImageFocus.Center;
        var own = width / height;
        if (own > aspect)
        {
            var excess = 1 - (aspect / own);
            return focus switch
            {
                ImageFocus.Left => new ImageCrop(0, 0, excess, 0),
                ImageFocus.Right => new ImageCrop(excess, 0, 0, 0),
                _ => new ImageCrop(excess / 2, 0, excess / 2, 0),
            };
        }

        if (own < aspect)
        {
            var excess = 1 - (own / aspect);
            return focus switch
            {
                ImageFocus.Top => new ImageCrop(0, 0, 0, excess),
                ImageFocus.Bottom => new ImageCrop(0, excess, 0, 0),
                _ => new ImageCrop(0, excess / 2, 0, excess / 2),
            };
        }

        return default;
    }

    private static ImageAttributes Parse(string block, string body, int line, DocumentFormat format)
    {
        DocumentConversionException Error(string reason) => new(
            format,
            line > 0 ? $"The image attributes '{block}' at line {line} cannot be used: {reason}" : $"The image attributes '{block}' cannot be used: {reason}");

        var attributes = new ImageAttributes { Text = block };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match token in TokenPattern().Matches(body))
        {
            var name = token.Groups["class"].Success ? "." + token.Groups["class"].Value : token.Groups["key"].Value;
            var value = token.Groups["quoted"].Success ? token.Groups["quoted"].Value : token.Groups["value"].Value;
            if (!seen.Add(name))
            {
                throw Error($"{name} is given twice.");
            }

            attributes = name switch
            {
                "focus" => attributes with
                {
                    Focus = value.ToLowerInvariant() switch
                    {
                        "center" => ImageFocus.Center,
                        "top" => ImageFocus.Top,
                        "bottom" => ImageFocus.Bottom,
                        "left" => ImageFocus.Left,
                        "right" => ImageFocus.Right,
                        _ => throw Error($"focus is top, bottom, left, right or center, not '{value}'."),
                    },
                },
                "fit" => attributes with
                {
                    Fit = value.ToLowerInvariant() switch
                    {
                        "contain" => ImageFit.Contain,
                        "cover" => ImageFit.Cover,
                        _ => throw Error($"fit is contain or cover, not '{value}'."),
                    },
                },
                "scale" => attributes with
                {
                    Scale = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0 && double.IsFinite(scale)
                        ? scale
                        : throw Error($"scale is a number above 0 with a decimal point, such as 0.8, not '{value}'."),
                },
                ".round" => attributes with { Round = true },
                ".grey" => attributes with { Grey = true },
                ".caption" => attributes with { Caption = true },
                _ => throw Error($"Unknown attribute '{name}'. Valid attributes: {Valid}."),
            };
        }

        return attributes;
    }

    [GeneratedRegex(@"^\{((?:\s*(?:\.[A-Za-z][\w-]*|[A-Za-z][\w-]*=(?:""[^""]*""|[^\s""{}]+)))+)\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"\.(?<class>[A-Za-z][\w-]*)|(?<key>[A-Za-z][\w-]*)=(?:""(?<quoted>[^""]*)""|(?<value>[^\s""{}]+))", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
