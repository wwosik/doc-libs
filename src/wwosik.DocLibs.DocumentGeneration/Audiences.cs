using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using MarkdownDocument = wwosik.DocLibs.DocumentConversions.MarkdownDocument;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The versions one Markdown source holds (E06-S06): front matter (<c>audience:</c>) and the host's options choose one.
/// Each version shows everything the one before it shows.
/// </summary>
public enum Audience
{
    /// <summary>The customer's version, the default: without what is marked <c>internal</c> and without solutions.</summary>
    Customer,

    /// <summary>The internal version: the customer's, with the team's notes and parts marked <c>internal</c>.</summary>
    Internal,

    /// <summary>The trainer's version: the internal one, with the solutions to the exercises.</summary>
    Trainer,
}

/// <summary>For hosts: which versions of a document are worth generating.</summary>
public static class AudienceVersions
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    /// <summary>
    /// The versions of <paramref name="document"/> whose content differs, in the order of <see cref="Audience"/>:
    /// always <see cref="Audience.Customer"/>; <see cref="Audience.Internal"/> when something is marked
    /// <c>internal</c>; <see cref="Audience.Trainer"/> when there is a <c>::: solution</c>. A host generates only these.
    /// </summary>
    /// <exception cref="DocumentConversionException">The front matter is malformed, or a marker is misplaced.</exception>
    public static IReadOnlyList<Audience> Distinct(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var frontMatter = FrontMatter.Read(document.Markdown, DocumentFormat.Unknown);
        var markdown = Markdown.Parse(frontMatter.Body, Pipeline);
        var found = AudienceFilter.Check(markdown, DocumentFormat.Unknown, block => block.Line + 1 + frontMatter.BodyLine);
        var versions = new List<Audience> { Audience.Customer };
        if (found.Internal)
        {
            versions.Add(Audience.Internal);
        }

        if (found.Solutions)
        {
            versions.Add(Audience.Trainer);
        }

        return versions;
    }

    // The block structure of the generators' pipelines; inline syntax does not matter here.
    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseAlertBlocks().UseDocumentSyntax();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }
}

/// <summary>
/// Takes out of a parsed Markdown what a version does not show, before a generator splits it into slides or pages:
/// <c>::: internal</c> containers, blocks after a line <c>{.internal}</c>, chapters and slides whose <c>#</c> or <c>##</c>
/// heading has <c>{.internal}</c>, and <c>::: solution</c> containers. It numbers each <c>::: exercise</c> ("Exercise 1")
/// and its solution alike, and takes the containers apart, so generators see plain blocks. The markers themselves are
/// taken out in every version.
/// </summary>
internal static partial class AudienceFilter
{
    /// <summary>The container of blocks for the internal and the trainer's versions.</summary>
    public const string Internal = "internal";

    /// <summary>The container of an exercise, shown in every version.</summary>
    public const string Exercise = "exercise";

    /// <summary>The container of a solution, right after its exercise, shown in the trainer's version only.</summary>
    public const string Solution = "solution";

    /// <summary>The containers, for <see cref="LayoutSyntax.Containers"/>.</summary>
    public static readonly IReadOnlyList<string> Containers = [Internal, Exercise, Solution];

    private const string Token = ".internal";

    /// <summary>
    /// The version to generate: <paramref name="host"/>, the options' version, if set; else the front matter's
    /// <c>audience</c>; else <see cref="Audience.Customer"/>. The front matter's value is checked either way.
    /// </summary>
    /// <exception cref="DocumentConversionException">The front matter's value is not a version.</exception>
    public static Audience Resolve(FrontMatter frontMatter, Audience? host)
    {
        var asked = frontMatter.Value(FrontMatter.AudienceKey) is { } value
            ? frontMatter.OneOf(
                FrontMatter.AudienceKey,
                value,
                ("customer", Audience.Customer),
                ("internal", Audience.Internal),
                ("trainer", Audience.Trainer))
            : Audience.Customer;
        return host ?? asked;
    }

    /// <summary>
    /// Changes <paramref name="document"/> to the blocks <paramref name="audience"/> shows, and adds one
    /// <see cref="NoteCodes.AudienceOmitted"/> note with the lines of what it left out. A document without the syntax
    /// stays as it is.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// A marker is misplaced: <c>{.internal}</c> on a <c>###</c> heading or followed by nothing to mark, a
    /// <c>::: solution</c> that does not follow an exercise, or arguments on one of the containers.
    /// </exception>
    public static void Apply(
        ContainerBlock document, Audience audience, DocumentFormat format, Func<MarkdownObject, int> lineOf, GeneratedTexts texts, GenerationNotes notes)
    {
        var found = Check(document, format, lineOf);
        if (!found.Any)
        {
            return;
        }

        var filter = new Filter(audience, lineOf, texts, found.Exercises);
        filter.Chapters(document);
        filter.Walk(document);
        if (filter.Omitted.Count > 0)
        {
            var lines = filter.Omitted.Where(l => l > 0).Distinct().Order().ToList();
            var count = filter.Omitted.Count;
            notes.Add(
                NoteCodes.AudienceOmitted,
                lines.FirstOrDefault(),
                $"The {Name(audience)} version leaves out {count} part{(count == 1 ? string.Empty : "s")} marked for {(audience == Audience.Internal ? "the trainer's version" : "other versions")}"
                + (lines.Count == 0 ? "." : $", on line{(lines.Count == 1 ? string.Empty : "s")} {Join(lines)}."));
        }
    }

    /// <summary>
    /// Checks every marker of <paramref name="document"/> without changing it, and says what it holds: something
    /// marked internal, solutions, and each solution's exercise.
    /// </summary>
    /// <exception cref="DocumentConversionException">A marker is misplaced.</exception>
    public static Found Check(ContainerBlock document, DocumentFormat format, Func<MarkdownObject, int> lineOf)
    {
        var found = new Found();
        foreach (var container in document.Descendants<ContainerBlock>().Prepend(document))
        {
            if (container is Table or TableRow or TableCell)
            {
                continue;
            }

            for (var i = 0; i < container.Count; i++)
            {
                var block = container[i];
                switch (block)
                {
                    case HeadingBlock heading when HeadingTokens(heading) is { } tokens && tokens.Contains(Token):
                        if (heading.Level > 2)
                        {
                            throw new DocumentConversionException(
                                format,
                                $"'{{.internal}}' on line {lineOf(heading)} is on a '{new string('#', heading.Level)}' heading. It marks a chapter ('#') or a slide ('##'); for a part of one, put a line '{{.internal}}' before the block, or use '::: internal'.");
                        }

                        if (!ReferenceEquals(container, document))
                        {
                            throw new DocumentConversionException(
                                format,
                                $"The heading with '{{.internal}}' on line {lineOf(heading)} is inside another block. A chapter or slide marked internal starts at a heading outside any container, list or quote.");
                        }

                        found.Internal = true;
                        break;
                    case ParagraphBlock paragraph when MarkerTokens(paragraph) is { } tokens && tokens.Contains(Token):
                        CheckMarker(container, i, document, format, lineOf);
                        found.Internal = true;
                        break;
                    case CustomContainer custom when Containers.Contains(custom.Info ?? string.Empty):
                        found.HasExercises |= custom.Info == Exercise;
                        if (custom.Arguments?.Trim() is { Length: > 0 } arguments)
                        {
                            throw new DocumentConversionException(format, $"The container '::: {custom.Info} {arguments}' on line {lineOf(custom)} takes no arguments.");
                        }

                        if (custom.Info == Internal)
                        {
                            found.Internal = true;
                        }
                        else if (custom.Info == Solution)
                        {
                            if (i == 0 || container[i - 1] is not CustomContainer { Info: Exercise } exercise)
                            {
                                throw new DocumentConversionException(
                                    format,
                                    $"The '::: solution' on line {lineOf(custom)} does not follow a '::: exercise'. A solution goes right after the exercise it solves.");
                            }

                            found.Solutions = true;
                            found.Exercises[custom] = exercise;
                        }

                        break;
                }
            }
        }

        return found;
    }

    /// <summary>What <see cref="Check"/> found.</summary>
    internal sealed class Found
    {
        public bool Internal { get; set; }

        public bool Solutions { get; set; }

        /// <summary>The exercise of each solution.</summary>
        public Dictionary<CustomContainer, CustomContainer> Exercises { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether there are exercises.</summary>
        public bool HasExercises { get; set; }

        /// <summary>Whether there is anything of the syntax.</summary>
        public bool Any => this.Internal || this.Solutions || this.HasExercises;
    }

    private static void CheckMarker(ContainerBlock container, int index, ContainerBlock document, DocumentFormat format, Func<MarkdownObject, int> lineOf)
    {
        var line = lineOf(container[index]);
        var allowed = ReferenceEquals(container, document) || container is AlertBlock or CustomContainer;
        if (!allowed)
        {
            throw new DocumentConversionException(
                format,
                $"The marker '{{.internal}}' on line {line} is inside {(container is ListItemBlock or ListBlock ? "a list" : container is QuoteBlock ? "a quote" : "a block it cannot mark in")}. It goes right before the block it marks, at the top level of a section or directly inside a ':::' container or an alert.");
        }

        var next = index + 1 < container.Count ? container[index + 1] : null;
        var stray = next switch
        {
            null or FootnoteGroup or LinkReferenceDefinitionGroup => "nothing to mark",
            HeadingBlock => "a heading; to mark a chapter or slide, put {.internal} on its heading",
            ParagraphBlock paragraph when MarkerTokens(paragraph) is { } tokens && tokens.Contains(Token) => "another '{.internal}'",
            ThematicBreakBlock => "a '---'",
            _ => null,
        };
        if (stray is not null)
        {
            throw new DocumentConversionException(
                format,
                $"The marker '{{.internal}}' on line {line} is followed by {stray}. It goes right before the block it marks, with a blank line between them.");
        }
    }

    // The walk that changes the document: one per generation.
    private sealed class Filter(Audience audience, Func<MarkdownObject, int> lineOf, GeneratedTexts texts, Dictionary<CustomContainer, CustomContainer> exercises)
    {
        private readonly Dictionary<CustomContainer, int> numbers = new(ReferenceEqualityComparer.Instance);
        private int exerciseCount;

        public List<int> Omitted { get; } = [];

        private bool ShowsInternal => audience != Audience.Customer;

        // `#` and `##` headings with `{.internal}`: the attribute goes; in the customer's version, the heading and
        // everything up to the next heading of its level or higher go too.
        public void Chapters(ContainerBlock document)
        {
            for (var i = 0; i < document.Count; i++)
            {
                if (document[i] is not HeadingBlock { Level: <= 2 } heading || HeadingTokens(heading) is not { } tokens || !tokens.Contains(Token))
                {
                    continue;
                }

                RemoveHeadingToken(heading);
                if (this.ShowsInternal)
                {
                    continue;
                }

                // A backup or the appendices (E06-S07) run to the end of the document, and so does the part an internal one hides.
                var toEnd = heading.Level == 1 && BackMatter.Starts(tokens);
                this.Omitted.Add(lineOf(heading));
                var start = i;
                document.RemoveAt(i);
                while (i < document.Count && (toEnd || !(document[i] is HeadingBlock next && next.Level <= heading.Level)))
                {
                    if (document[i] is FootnoteGroup or LinkReferenceDefinitionGroup)
                    {
                        i++;
                    }
                    else
                    {
                        document.RemoveAt(i);
                    }
                }

                i = start - 1;
            }
        }

        public void Walk(ContainerBlock container)
        {
            if (container is Table or TableRow or TableCell)
            {
                return;
            }

            for (var i = 0; i < container.Count; i++)
            {
                switch (container[i])
                {
                    case ParagraphBlock paragraph when MarkerTokens(paragraph) is { } tokens && tokens.Contains(Token):
                        this.Marked(container, i, paragraph, tokens);
                        i--;
                        break;
                    case CustomContainer { Info: Internal } custom:
                        if (this.ShowsInternal)
                        {
                            Unwrap(container, i, null);
                        }
                        else
                        {
                            this.Omitted.Add(lineOf(custom));
                            container.RemoveAt(i);
                        }

                        i--;
                        break;
                    case CustomContainer { Info: Exercise } custom:
                        this.numbers[custom] = ++this.exerciseCount;
                        Unwrap(container, i, this.Label(custom, texts.Exercise, this.exerciseCount));
                        break;
                    case CustomContainer { Info: Solution } custom:
                        if (audience == Audience.Trainer)
                        {
                            var number = exercises.TryGetValue(custom, out var exercise) ? this.numbers.GetValueOrDefault(exercise) : 0;
                            Unwrap(container, i, this.Label(custom, texts.Solution, number));
                        }
                        else
                        {
                            this.Omitted.Add(lineOf(custom));
                            container.RemoveAt(i);
                            i--;
                        }

                        break;
                    case ContainerBlock child:
                        this.Walk(child);
                        break;
                }
            }
        }

        // A marker paragraph and the block it marks, at container[index]: the marker's other tokens (build markers)
        // stay, the `.internal` goes; in the customer's version both go.
        private void Marked(ContainerBlock container, int index, ParagraphBlock paragraph, IReadOnlyList<string> tokens)
        {
            var rest = tokens.Where(t => t != Token).ToList();
            var markers = 1;
            if (rest.Count > 0)
            {
                var literal = new LiteralInline("{" + string.Join(' ', rest) + "}") { Line = paragraph.Line, Column = paragraph.Column };
                paragraph.Inline = new ContainerInline().AppendChild(literal);
            }
            else if (container.Count > index + 1 && container[index + 1] is ParagraphBlock next && BuildMarker.IsMarker(next))
            {
                // `{.internal}`, then a build marker, then the block: the marker builds that block, and goes with it.
                markers = 2;
            }

            if (this.ShowsInternal)
            {
                // A rewritten marker holds no `.internal` any more, and is walked on like any paragraph.
                if (rest.Count == 0)
                {
                    container.RemoveAt(index);
                }

                return;
            }

            this.Omitted.Add(lineOf(paragraph));
            var remove = rest.Count > 0 ? 2 : 1 + markers;
            for (var k = 0; k < remove && index < container.Count; k++)
            {
                container.RemoveAt(index);
            }
        }

        private ParagraphBlock Label(CustomContainer container, string text, int number)
        {
            var line = container.Line;
            var literal = new LiteralInline(number > 0 ? $"{text} {number}" : text) { Line = line, Column = container.Column };
            var strong = new EmphasisInline { DelimiterChar = '*', DelimiterCount = 2, Line = line, Column = container.Column };
            strong.AppendChild(literal);
            return new ParagraphBlock { Line = line, Column = container.Column, Span = container.Span, Inline = new ContainerInline().AppendChild(strong) };
        }

        // Replaces container[index] by its content, after the label if there is one. The content is walked where it
        // now stands, after the label.
        private static void Unwrap(ContainerBlock container, int index, ParagraphBlock? label)
        {
            var wrapper = (ContainerBlock)container[index];
            var content = wrapper.ToList();
            wrapper.Clear();
            container.RemoveAt(index);
            var at = index;
            if (label is not null)
            {
                container.Insert(at++, label);
            }

            foreach (var block in content)
            {
                container.Insert(at++, block);
            }
        }
    }

    // The tokens of a trailing `{…}` on a heading, made of classes and quoted pairs; null without one.
    internal static List<string>? HeadingTokens(HeadingBlock heading)
    {
        var (text, _) = TrailingText(heading);
        var match = text is null ? null : HeadingPattern().Match(text);
        return match is { Success: true } ? Tokens(match.Groups[1].Value) : null;
    }

    // Takes `.internal` off the heading's trailing `{…}`, and the braces when nothing else is in them.
    private static void RemoveHeadingToken(HeadingBlock heading)
    {
        var (text, literals) = TrailingText(heading);
        var match = HeadingPattern().Match(text!);
        var rest = Tokens(match.Groups[1].Value).Where(t => t != Token).ToList();
        var before = text![..match.Index];
        var changed = rest.Count == 0 ? before.TrimEnd() : $"{before} {{{string.Join(' ', rest)}}}";
        var line = literals[0].Line;
        var column = literals[0].Column;
        foreach (var literal in literals)
        {
            literal.Remove();
        }

        if (changed.Length > 0)
        {
            heading.Inline!.AppendChild(new LiteralInline(changed) { Line = line, Column = column });
        }
    }

    // The plain text at the end of a heading: its trailing literals, which the parser may have split.
    private static (string? Text, List<LiteralInline> Literals) TrailingText(HeadingBlock heading)
    {
        var literals = new List<LiteralInline>();
        for (var inline = heading.Inline?.LastChild; inline is LiteralInline { IsFirstCharacterEscaped: false } literal; inline = inline.PreviousSibling)
        {
            literals.Insert(0, literal);
        }

        return literals.Count == 0 ? (null, literals) : (string.Concat(literals.Select(l => l.Content.ToString())), literals);
    }

    // The tokens of a paragraph that is nothing but one `{…}` of classes and quoted pairs; null for any other.
    private static List<string>? MarkerTokens(ParagraphBlock paragraph)
    {
        var text = new StringBuilder();
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

        var match = MarkerPattern().Match(text.ToString());
        return match.Success ? Tokens(match.Groups[1].Value) : null;
    }

    private static List<string> Tokens(string text) => TokenPattern().Matches(text).Select(m => m.Value).ToList();

    private static string Name(Audience audience) => audience switch
    {
        Audience.Internal => "internal",
        Audience.Trainer => "trainer's",
        _ => "customer's",
    };

    private static string Join(List<int> lines) => lines.Count == 1
        ? lines[0].ToString(System.Globalization.CultureInfo.InvariantCulture)
        : string.Join(", ", lines[..^1].Select(l => l.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            + " and " + lines[^1].ToString(System.Globalization.CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s*\{((?:\s*(?:\.[A-Za-z][\w-]*(?:=[\w-]+)?|[A-Za-z][\w-]*=""[^""]*""))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*\{((?:\s*(?:\.[A-Za-z][\w-]*(?:=[\w-]+)?|[A-Za-z][\w-]*=""[^""]*""))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    [GeneratedRegex(@"\.[A-Za-z][\w-]*(?:=[\w-]+)?|[A-Za-z][\w-]*=""[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
