using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using MarkdownDocument = wwosik.DocLibs.DocumentConversions.MarkdownDocument;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>What a generator's run for a check gives back besides the notes.</summary>
/// <param name="Pages">The slides or pages written.</param>
internal sealed record CheckRun(int Pages)
{
    public IReadOnlyDictionary<string, int> SlidesByKind { get; init; } = new Dictionary<string, int>();

    public TimeSpan? TalkTime { get; init; }
}

/// <summary>
/// The check every generator's <c>Check</c> runs: the image sources first, all at once, then the style rules on the
/// Markdown, then the generator itself through <c>generate</c>, with the renderers counted. A
/// <see cref="DocumentConversionException"/> becomes <see cref="CheckResult.Error"/>.
/// </summary>
internal static partial class DocumentCheck
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    /// <remarks>
    /// <c>generate</c> runs the generator on the document (bad images replaced by their alt text) with the given
    /// renderers, adds its notes to the given ones, and writes nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The check options are invalid; or whatever <c>generate</c> throws besides document errors.</exception>
    public static CheckResult Run(
        MarkdownDocument document,
        CheckOptions checks,
        DocumentFormat format,
        IReadOnlyList<ICodeBlockRenderer> renderers,
        Func<MarkdownDocument, IReadOnlyList<ICodeBlockRenderer>, GenerationNotes, CheckRun> generate)
    {
        ArgumentNullException.ThrowIfNull(checks);
        checks.Validate();
        var notes = new GenerationNotes();
        Source? source = null;
        try
        {
            var frontMatter = FrontMatter.Read(document.Markdown, format);
            source = new Source(document, frontMatter.BodyLine, checks, format, notes);
            source.Walk(frontMatter.Body);
            var checkedDocument = source.Replaced(frontMatter.Body) is { } body
                ? document with { Markdown = document.Markdown[..^frontMatter.Body.Length] + body }
                : document;

            var counted = new CountingRenderers(renderers);
            var run = generate(checkedDocument, counted.Renderers, notes);
            return new CheckResult(notes.Result(run.Pages).Notes, source.Statistics(run.Pages, counted.Drawn) with
            {
                SlidesByKind = run.SlidesByKind,
                TalkTime = run.TalkTime,
            });
        }
        catch (DocumentConversionException ex)
        {
            return new CheckResult(notes.Result(0).Notes, source?.Statistics(0, 0) ?? new CheckStatistics()) { Error = ex.Reason };
        }
    }

    /// <summary>
    /// The words in <paramref name="text"/>: runs of letters, digits and marks, with <c>'</c>, <c>’</c> or <c>-</c> inside
    /// a run; in Han, Hiragana, Katakana and Thai, which are written without spaces, every two characters count as one word.
    /// </summary>
    public static int Words(string text)
    {
        var words = 0;
        var inWord = false;
        var unspaced = 0;
        var runes = text.EnumerateRunes().ToList();
        for (var i = 0; i < runes.Count; i++)
        {
            var rune = runes[i];
            if (IsUnspaced(rune))
            {
                inWord = false;
                unspaced++;
                continue;
            }

            if (unspaced > 0)
            {
                words += (unspaced + 1) / 2;
                unspaced = 0;
            }

            if (IsWordRune(rune))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else if (!(inWord && rune.Value is '\'' or '’' or '-' && i + 1 < runes.Count && IsWordRune(runes[i + 1]) && !IsUnspaced(runes[i + 1])))
            {
                inWord = false;
            }
        }

        return words + ((unspaced + 1) / 2);
    }

    /// <summary>The words of runs of text: code runs count as one word each.</summary>
    public static int Words(IEnumerable<(string Text, bool Code)> runs) =>
        runs.Sum(r => r.Code ? (r.Text.Trim().Length > 0 ? 1 : 0) : 0) + Words(string.Join(' ', runs.Where(r => !r.Code).Select(r => r.Text)));

    /// <summary>The time one content slide of <paramref name="words"/> words takes in a talk.</summary>
    public static TimeSpan ContentSlideTime(int words, CheckOptions checks)
    {
        var spoken = TimeSpan.FromMinutes((double)words / checks.WordsPerMinute);
        return spoken > checks.MinTimePerSlide ? spoken : checks.MinTimePerSlide;
    }

    /// <summary>The time a cover, section or closing slide takes in a talk.</summary>
    public static TimeSpan TitleSlideTime { get; } = TimeSpan.FromSeconds(10);

    /// <summary>A talk time rounded to whole minutes.</summary>
    public static TimeSpan RoundTalkTime(TimeSpan time) => TimeSpan.FromMinutes(Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero));

    // Han, Hiragana, Katakana (with the half-width forms) and Thai.
    private static bool IsUnspaced(Rune rune) => rune.Value is (>= 0x3040 and <= 0x30FF) or (>= 0x31F0 and <= 0x31FF) or (>= 0x3400 and <= 0x4DBF)
        or (>= 0x4E00 and <= 0x9FFF) or (>= 0xF900 and <= 0xFAFF) or (>= 0xFF66 and <= 0xFF9F) or (>= 0x20000 and <= 0x3134F)
        or (>= 0x0E01 and <= 0x0E5B);

    private static bool IsWordRune(Rune rune) => Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    // The generators' Markdown: tables, strikethrough and marks, autolinks, alerts, footnotes, task and definition lists,
    // and custom containers without their inline form.
    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Marked)
            .UseAutoLinks()
            .UseAlertBlocks()
            .UseDocumentSyntax();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    [GeneratedRegex(@":icon-[a-z0-9]+(?:-[a-z0-9]+)*:", RegexOptions.CultureInvariant)]
    private static partial Regex IconPattern();

    [GeneratedRegex(@"(?<=\])\{\.[A-Za-z]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex ColourPattern();

    [GeneratedRegex(@"\s*\{[^{}]*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingAttributesPattern();

    /// <summary>The pass over the Markdown: image sources, the style rules and the counts.</summary>
    private sealed class Source(MarkdownDocument document, int lineOffset, CheckOptions checks, DocumentFormat format, GenerationNotes notes)
    {
        private readonly List<(LinkInline Image, string Alt)> broken = [];
        private int words;
        private int images;
        private int tables;
        private int drawn;

        // The `#` or `##` section of a deck being walked: its heading, list items and words.
        private Markdig.Syntax.HeadingBlock? sectionHeading;
        private int sectionBullets;
        private int sectionWords;

        // In a deck's backup (from the `#` heading with `{.backup}` on), whose words do not count.
        private bool backup;

        public void Walk(string body)
        {
            var markdown = Markdown.Parse(body, Pipeline);
            PageBreaks.Flatten(markdown, format);
            foreach (var block in markdown)
            {
                if (format == DocumentFormat.Pptx && block is Markdig.Syntax.HeadingBlock { Level: <= 2 } heading)
                {
                    this.backup |= heading.Level == 1 && AudienceFilter.HeadingTokens(heading) is { } tokens && tokens.Contains(BackMatter.Backup);
                    this.EndSection();
                    this.sectionHeading = heading;
                    this.Title(heading, "The slide title");
                }

                this.Walk(block, block is FootnoteGroup ? null : this.sectionHeading, inTable: false);
            }

            this.EndSection();
        }

        // The body with every image the generator cannot load replaced by its alt text (and attributes after it dropped),
        // or null when there is none.
        public string? Replaced(string body)
        {
            if (this.broken.Count == 0)
            {
                return null;
            }

            var text = new StringBuilder(body);
            foreach (var (image, alt) in this.broken.OrderByDescending(b => b.Image.Span.Start))
            {
                var end = image.Span.End + 1;
                if (end < body.Length && body[end] == '{' && body.IndexOf('}', end) is var close and > 0
                    && body.IndexOf('\n', end) is var newline && (newline < 0 || close < newline))
                {
                    end = close + 1;
                }

                text.Remove(image.Span.Start, end - image.Span.Start).Insert(image.Span.Start, alt);
            }

            return text.ToString();
        }

        public CheckStatistics Statistics(int pages, int renderedDiagrams) => new()
        {
            Pages = pages,
            Words = this.words,
            Images = this.images,
            Tables = this.tables,
            Diagrams = this.drawn + renderedDiagrams,
        };

        private int LineOf(MarkdownObject item) => item.Line + 1 + lineOffset;

        private void Walk(Block block, Markdig.Syntax.HeadingBlock? section, bool inTable)
        {
            switch (block)
            {
                case CustomContainer { Info: LayoutSyntax.Notes }:
                    // Speaker notes are not on the slide, and not in PDFs and Word documents.
                    return;
                case CustomContainer container when VisualContainers.IsVisual(container.Info ?? string.Empty) || SceneSyntax.IsScene(container):
                    this.drawn++;
                    return;
                case Table table:
                    this.tables++;
                    foreach (var child in table)
                    {
                        this.Walk(child, section, inTable: true);
                    }

                    return;
                case ListItemBlock item:
                    if (section is not null && !inTable)
                    {
                        this.sectionBullets++;
                    }

                    foreach (var child in item)
                    {
                        this.Walk(child, section, inTable);
                    }

                    return;
                case ContainerBlock container:
                    foreach (var child in container)
                    {
                        this.Walk(child, section, inTable);
                    }

                    return;
                case LeafBlock { Inline: { } inline } leaf and (Markdig.Syntax.ParagraphBlock or Markdig.Syntax.HeadingBlock):
                    this.Leaf(leaf, inline, section, inTable);
                    return;
            }
        }

        private void Leaf(LeafBlock leaf, ContainerInline inline, Markdig.Syntax.HeadingBlock? section, bool inTable)
        {
            var runs = new List<(string Text, bool Code)>();
            var found = new List<LinkInline>();
            Collect(inline, runs, found);
            var count = Words([.. runs.Select(r => r.Code ? r : (ColourPattern().Replace(IconPattern().Replace(r.Text, " "), string.Empty), false))]);
            this.words += this.backup ? 0 : count;
            if (section is not null && !inTable && leaf != section)
            {
                this.sectionWords += count;
            }

            if (format != DocumentFormat.Pptx && !inTable)
            {
                if (leaf is Markdig.Syntax.HeadingBlock heading)
                {
                    this.Title(heading, "The heading");
                }
                else if (checks.MaxWordsPerParagraph is { } most && count > most)
                {
                    notes.Add(
                        NoteCodes.LongParagraph,
                        this.LineOf(leaf),
                        Invariant($"The paragraph '{GenerationNotes.Quote(Plain(runs))}' has {count} words; keep paragraphs to {most}."));
                }
            }

            foreach (var image in found)
            {
                this.Image(image);
            }
        }

        private void Image(LinkInline image)
        {
            var url = image.GetDynamicUrl?.Invoke() ?? image.Url ?? string.Empty;
            var altRuns = new List<(string Text, bool Code)>();
            foreach (var child in image)
            {
                Collect(child, altRuns, null);
            }

            var alt = Plain(altRuns).Trim();
            if (checks.RequireAltText && alt.Length == 0)
            {
                notes.Add(
                    NoteCodes.AltTextMissing,
                    this.LineOf(image),
                    $"The image '{GenerationNotes.Quote(url)}' has no alt text. Say what it shows in the brackets: ![What it shows]({GenerationNotes.Quote(url)}).");
            }

            if (QrDrawing.IsQr(url))
            {
                return;
            }

            this.images++;
            string? problem = null;
            if (url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            {
                problem = $"The image '{GenerationNotes.Quote(url)}' is a web address, which is never loaded. Use one of the images given to you, by its path.";
            }
            else if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                problem = "An image is a 'data:' URL, which is never loaded. Use one of the images given to you, by its path.";
            }
            else if (document.FindResource(url) is null)
            {
                problem = $"The image '{GenerationNotes.Quote(url)}' is not among the document's resources. Use one of the images given to you, by its path.";
            }

            if (problem is not null)
            {
                notes.Add(NoteCodes.ImageSource, this.LineOf(image), problem);
                this.broken.Add((image, alt));
            }
        }

        // A slide title (`#`, `##` of a deck) or a heading of a document, without the settings in braces at its end.
        private void Title(Markdig.Syntax.HeadingBlock heading, string what)
        {
            if (checks.MaxTitleLength is not { } most || heading.Inline is null)
            {
                return;
            }

            var runs = new List<(string Text, bool Code)>();
            Collect(heading.Inline, runs, null);
            var title = HeadingAttributesPattern().Replace(ColourPattern().Replace(Plain(runs), string.Empty), string.Empty).Trim();
            var length = new StringInfo(title).LengthInTextElements;
            if (length > most)
            {
                notes.Add(
                    NoteCodes.TitleTooLong,
                    this.LineOf(heading),
                    Invariant($"{what} '{GenerationNotes.Quote(title)}' is {length} characters long; keep titles to {most}."));
            }
        }

        private void EndSection()
        {
            if (this.sectionHeading is { } heading)
            {
                var runs = new List<(string Text, bool Code)>();
                Collect(heading.Inline, runs, null);
                var title = HeadingAttributesPattern().Replace(Plain(runs), string.Empty).Trim();
                var slide = title.Length > 0 ? $"The slide '{GenerationNotes.Quote(title)}'" : "The slide without a title";
                if (checks.MaxBulletsPerSlide is { } bullets && this.sectionBullets > bullets)
                {
                    notes.Add(NoteCodes.TooManyBullets, this.LineOf(heading), Invariant($"{slide} has {this.sectionBullets} bullets; keep it to {bullets}."));
                }

                if (checks.MaxWordsPerSlide is { } most && this.sectionWords > most)
                {
                    notes.Add(NoteCodes.TooManyWords, this.LineOf(heading), Invariant($"{slide} has {this.sectionWords} words; keep it to {most}."));
                }
            }

            this.sectionHeading = null;
            this.sectionBullets = 0;
            this.sectionWords = 0;
        }

        // The text of inlines as runs, code spans and autolinks as code; images are collected, their alt text left out.
        private static void Collect(Inline? inline, List<(string Text, bool Code)> runs, List<LinkInline>? found)
        {
            switch (inline)
            {
                case null:
                    return;
                case LinkInline { IsImage: true } image when found is not null:
                    found.Add(image);
                    return;
                case LiteralInline literal:
                    runs.Add((literal.Content.ToString(), false));
                    return;
                case CodeInline code:
                    runs.Add((code.Content, true));
                    return;
                case AutolinkInline link:
                    runs.Add((link.Url, true));
                    return;
                case HtmlEntityInline entity:
                    runs.Add((entity.Transcoded.ToString(), false));
                    return;
                case LineBreakInline:
                    runs.Add((" ", false));
                    return;
                case HtmlInline or FootnoteLink or TaskList:
                    return;
                case ContainerInline container:
                    foreach (var child in container)
                    {
                        Collect(child, runs, found);
                    }

                    return;
            }
        }

        private static string Plain(List<(string Text, bool Code)> runs) => string.Concat(runs.Select(r => r.Text));

        private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The renderers, each wrapped to count the code blocks it draws (by line, as a block may be asked twice).</summary>
    private sealed class CountingRenderers
    {
        private readonly HashSet<(int, string)> drawn = [];

        public CountingRenderers(IReadOnlyList<ICodeBlockRenderer> renderers) =>
            this.Renderers = [.. renderers.Select(r => (ICodeBlockRenderer)new Counting(r, this.drawn))];

        public IReadOnlyList<ICodeBlockRenderer> Renderers { get; }

        public int Drawn => this.drawn.Count;

        private sealed class Counting(ICodeBlockRenderer inner, HashSet<(int, string)> drawn) : ICodeBlockRenderer
        {
            public string? AuthoringGuide => inner.AuthoringGuide;

            public CodeBlockGraphic? Render(CodeBlockRequest request)
            {
                var graphic = inner.Render(request);
                if (graphic is not null)
                {
                    lock (drawn)
                    {
                        drawn.Add((request.Line, request.Code));
                    }
                }

                return graphic;
            }
        }
    }
}
