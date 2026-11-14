using System.Text.RegularExpressions;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>Whether a build brings its content in, takes it away, or draws attention to it.</summary>
internal enum BuildKind
{
    Entrance,
    Exit,
    Emphasis,
}

/// <summary>The animation a build plays, each one of PowerPoint's own presets.</summary>
internal enum BuildEffect
{
    /// <summary>Entrance: Appear.</summary>
    Appear,

    /// <summary>Entrance: Fly In, from the bottom.</summary>
    Fly,

    /// <summary>Entrance: Wipe, from the left.</summary>
    Wipe,

    /// <summary>Exit: Disappear.</summary>
    Disappear,

    /// <summary>Emphasis: Pulse, which leaves the content as it was.</summary>
    Pulse,
}

/// <summary>One click of a build marker: what it does, and whether it goes item by item (<c>.build</c>) or plays on the whole block.</summary>
internal sealed record Build(BuildKind Kind, BuildEffect Effect, bool ByItem);

/// <summary>
/// Build markers: a paragraph that is nothing but <c>{.build}</c>, <c>{.build=fly}</c>, <c>{.frag}</c>, <c>{.exit}</c>
/// or <c>{.emphasize}</c>, or several of them (<c>{.frag .exit}</c>), builds the block after it on clicks. Only braces
/// made of classes and quoted <c>key="value"</c> pairs count, as for heading attributes; any other braces stay text.
/// Both generators check them alike (<see cref="Check"/>); decks build, PDFs leave the markers out.
/// </summary>
internal static partial class BuildMarker
{
    /// <summary>The markers, as they are written, for error messages.</summary>
    public const string Valid = ".build, .build=appear, .build=fly, .build=wipe, .frag, .exit, .emphasize";

    /// <summary>The effects <c>.build=…</c> takes.</summary>
    public static readonly IReadOnlyList<string> Effects = ["appear", "fly", "wipe"];

    /// <summary>The marker <c>build: lists</c> gives every top-level list without one: <c>{.build}</c>.</summary>
    public static readonly IReadOnlyList<Build> Lists = [new(BuildKind.Entrance, BuildEffect.Appear, ByItem: true)];

    /// <summary>Whether the paragraph is a marker: nothing but one <c>{…}</c> of classes and quoted pairs.</summary>
    public static bool IsMarker(ParagraphBlock paragraph) => Text(paragraph) is { } text && BlockPattern().IsMatch(text);

    /// <summary>The builds of a marker paragraph, in their written order; <see langword="null"/> if it is no marker.</summary>
    /// <exception cref="DocumentConversionException">The marker names a class or key that is not a build marker.</exception>
    public static IReadOnlyList<Build>? TryParse(ParagraphBlock paragraph, DocumentFormat format, int line = 0)
    {
        var text = Text(paragraph);
        var match = text is null ? null : BlockPattern().Match(text);
        if (match is not { Success: true })
        {
            return null;
        }

        var builds = new List<Build>();
        foreach (Match token in TokenPattern().Matches(match.Groups[1].Value))
        {
            builds.Add((token.Groups["class"].Value, token.Groups["value"].Success ? token.Groups["value"].Value : null) switch
            {
                ("build", null) => new Build(BuildKind.Entrance, BuildEffect.Appear, ByItem: true),
                ("build", var effect) when Effects.Contains(effect!) => new Build(BuildKind.Entrance, Enum.Parse<BuildEffect>(effect!, ignoreCase: true), ByItem: true),
                ("frag", null) => new Build(BuildKind.Entrance, BuildEffect.Appear, ByItem: false),
                ("exit", null) => new Build(BuildKind.Exit, BuildEffect.Disappear, ByItem: false),
                ("emphasize", null) => new Build(BuildKind.Emphasis, BuildEffect.Pulse, ByItem: false),
                _ => throw new DocumentConversionException(
                    format,
                    $"Unknown build marker '{token.Value}' in '{text!.Trim()}'{OnLine(line)}. Valid markers: {Valid}."),
            });
        }

        return builds;
    }

    /// <summary>
    /// Checks every marker of the document: its classes, and that it stands right before the block it builds, at the
    /// top level of a section or directly inside a <c>:::</c> container or an alert. A marker followed by a heading,
    /// a <c>::: notes</c>, another marker or nothing, and a marker inside a list, a quote or speaker notes, throw.
    /// </summary>
    /// <exception cref="DocumentConversionException">A marker is unknown or stray.</exception>
    public static void Check(ContainerBlock root, DocumentFormat format, Func<MarkdownObject, int> lineOf)
    {
        foreach (var container in root.Descendants<ContainerBlock>().Prepend(root))
        {
            if (container is TableCell or Table or TableRow)
            {
                continue;
            }

            for (var i = 0; i < container.Count; i++)
            {
                if (container[i] is not ParagraphBlock paragraph || TryParse(paragraph, format, lineOf(paragraph)) is null)
                {
                    continue;
                }

                var marker = Text(paragraph)!.Trim();
                var line = OnLine(lineOf(paragraph));
                var allowed = ReferenceEquals(container, root) || container is AlertBlock
                    || (container is CustomContainer custom && custom.Info != LayoutSyntax.Notes);
                if (!allowed)
                {
                    throw new DocumentConversionException(
                        format,
                        $"The build marker '{marker}'{line} is inside {Inside(container)}. A build marker goes right before the block it builds, at the top level of a section or directly inside a ':::' container or an alert.");
                }

                var next = i + 1 < container.Count ? container[i + 1] : null;
                if (Stray(next) is { } what)
                {
                    throw new DocumentConversionException(
                        format,
                        $"The build marker '{marker}'{line} is followed by {what}. A build marker goes right before the block it builds, with a blank line between them.");
                }
            }
        }
    }

    // What a marker cannot be followed by, as a phrase; null for a block it can build.
    private static string? Stray(Block? next) => next switch
    {
        null or FootnoteGroup => "nothing to build",
        HeadingBlock => "a heading",
        CustomContainer { Info: LayoutSyntax.Notes } => "speaker notes",
        ParagraphBlock paragraph when IsMarker(paragraph) => "another marker; write both in one, such as {.frag .exit}",
        ThematicBreakBlock => "a '---'",
        HtmlBlock { Type: HtmlBlockType.Comment } or LinkReferenceDefinitionGroup => "nothing shown",
        _ => null,
    };

    private static string Inside(ContainerBlock container) => container switch
    {
        ListItemBlock or ListBlock => "a list",
        QuoteBlock => "a quote",
        CustomContainer { Info: LayoutSyntax.Notes } => "speaker notes",
        Footnote => "a footnote",
        _ => "a block that is not built",
    };

    // The paragraph's text if it is plain text only, without escapes, which keep braces text: else null.
    private static string? Text(ParagraphBlock paragraph)
    {
        var text = new System.Text.StringBuilder();
        foreach (var inline in paragraph.Inline ?? [])
        {
            switch (inline)
            {
                case LiteralInline { IsFirstCharacterEscaped: false } literal:
                    text.Append(literal.Content.ToString());
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                default:
                    return null;
            }
        }

        return text.ToString();
    }

    private static string OnLine(int line) => line > 0 ? $" on line {line}" : string.Empty;

    [GeneratedRegex(@"^\s*\{((?:\s*(?:\.[A-Za-z][\w-]*(?:=[\w-]+)?|[A-Za-z][\w-]*=""[^""]*""))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"\.(?<class>[A-Za-z][\w-]*)(?:=(?<value>[\w-]+))?|(?<key>[A-Za-z][\w-]*)=""(?<keyValue>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
