using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>What <see cref="HtmlDeck.Write"/> writes into the page.</summary>
public sealed record HtmlDeckOptions
{
    /// <summary>The page title; by default the deck's title (core properties), else <c>Presentation</c>.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// TrueType files for families the deck names but does not embed, matched by family name (name ID 1): text in
    /// them is measured with these faces, and with <see cref="EmbedFonts"/> they are written into the page.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fonts { get; init; } = [];

    /// <summary>Whether the faces the slides use (embedded in the deck or passed in <see cref="Fonts"/>) go into the page as <c>@font-face</c>; default on.</summary>
    public bool EmbedFonts { get; init; } = true;

    /// <summary>The page's <c>lang</c>; by default the first run's language in the deck, else <c>en</c>.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Whether the speaker notes go into the page, for the notes view and the presenter window; default on. Off leaves
    /// them out of the file, for a deck handed to the audience.
    /// </summary>
    public bool Notes { get; init; } = true;
}

/// <summary>
/// A deck as one self-contained HTML page, drawn from the <c>.pptx</c> itself: one SVG per visible slide, keys to step
/// through its builds and slides with their effects and transitions, a notes view, a presenter window, an overview, a
/// black screen and one slide per printed sheet. Nothing is loaded from the network.
/// Markdown to an HTML deck is <see cref="PptxGenerator.Generate"/> into a stream, then this.
/// </summary>
public static class HtmlDeck
{
    /// <summary>
    /// Writes <paramref name="pptx"/> (any presentation, read best effort) as an HTML page to <paramref name="html"/>.
    /// <see cref="GenerationResult.Pages"/> is the number of slides written; the notes say what could not be drawn
    /// (<see cref="NoteCodes.DrawingUnsupported"/>) and which fonts were measured by stand-in or estimate
    /// (<see cref="NoteCodes.FontMissing"/>). For a deck from <see cref="PptxGenerator.Generate"/>,
    /// <see cref="GenerationResult.SlideLines"/> is the Markdown line of each slide written, as the generator returned
    /// it (0 for a slide added by other means); <see langword="null"/> for a deck whose slides do not carry their lines.
    /// </summary>
    /// <exception cref="DocumentConversions.DocumentConversionException">The file is not a presentation, or is damaged.</exception>
    public static GenerationResult Write(Stream pptx, Stream html, HtmlDeckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pptx);
        ArgumentNullException.ThrowIfNull(html);
        options ??= new HtmlDeckOptions();
        var (deck, notes) = DeckReader.Read(pptx, options.Fonts);
        var page = HtmlDeckWriter.Page(deck, options);
        var bytes = new UTF8Encoding(false).GetBytes(page);
        html.Write(bytes);
        var lines = deck.Slides.Any(s => s.Line > 0) ? deck.Slides.Select(s => s.Line).ToList() : null;
        return notes.Result(deck.Slides.Count) with { SlideLines = lines };
    }
}

/// <summary>The HTML page around the slide SVGs: head with CSP and fonts, sections, the key list and the script.</summary>
internal static class HtmlDeckWriter
{
    private const double EmuPerPoint = 12700;

    // A printed sheet with notes: the slide's width, √2 times as tall (A-series proportions), slide on top, notes below.
    private const double NotesSheet = 1.4142;

    public static readonly string Script = Resource("HtmlDeck.deck.js");
    public static readonly string Styles = Resource("HtmlDeck.deck.css");

    /// <summary>The script's hash for the Content Security Policy.</summary>
    public static string ScriptHash => "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Script)));

    private static readonly (string Keys, string What)[] Keys =
    [
        ("→ ↓ Space Page Down Enter, click, swipe left", "next build step or slide"),
        ("← ↑ Page Up Backspace, swipe right", "previous build step or slide"),
        ("Home / End", "first / last slide"),
        ("a number, then Enter", "that slide"),
        ("F", "full screen"),
        ("O", "overview of all slides"),
        ("B or .", "black screen"),
        ("N", "notes view"),
        ("P", "presenter window"),
        ("?", "this list"),
        ("Esc", "leave full screen, overview or black screen"),
    ];

    public static string Page(DeckScene deck, HtmlDeckOptions options)
    {
        var title = options.Title ?? deck.Title ?? "Presentation";
        var language = options.Language ?? deck.Language ?? "en";
        var (width, height) = (deck.Width / EmuPerPoint, deck.Height / EmuPerPoint);
        var page = new StringBuilder();
        page.Append("<!DOCTYPE html>\n<html lang=\"").Append(SceneSvg.Escape(language)).Append("\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src '")
            .Append(ScriptHash).Append("'; img-src data:; font-src data:; base-uri 'none'; form-action 'none'\">\n")
            .Append("<title>").Append(SceneSvg.Escape(title)).Append("</title>\n<style>\n")
            .Append(FormattableString.Invariant($"@page {{ size: {Number(width)}pt {Number(height)}pt; margin: 0; }}\n"))
            .Append(FormattableString.Invariant($"@page withnotes {{ size: {Number(width)}pt {Number(width * NotesSheet)}pt; margin: 0; }}\n"))
            .Append(FormattableString.Invariant($":root {{ --slide-ratio: {Number(width)} / {Number(height)}; --slide-width: {Number(width)}pt; --slide-height: {Number(height)}pt; }}\n"));
        if (options.EmbedFonts)
        {
            foreach (var face in deck.Faces.Where(f => f.Source != FaceSource.StandIn))
            {
                page.Append("@font-face { font-family: '").Append(face.Family.Replace("'", string.Empty, StringComparison.Ordinal).Replace("\\", string.Empty, StringComparison.Ordinal))
                    .Append("'; font-weight: ").Append(face.Bold ? "bold" : "normal")
                    .Append("; font-style: ").Append(face.Italic ? "italic" : "normal")
                    .Append("; src: url(data:font/ttf;base64,").Append(Convert.ToBase64String(face.File)).Append(") format('truetype'); }\n");
            }
        }

        page.Append(Styles).Append("</style>\n</head>\n<body>\n<main class=\"deck\" id=\"deck\"")
            .Append(options.Notes ? string.Empty : " data-notes=\"off\"").Append(">\n");

        // Pictures once, as symbols every slide refers to.
        var symbols = new Dictionary<SceneImage, string>(ReferenceEqualityComparer.Instance);
        string Symbol(SceneImage image)
        {
            if (!symbols.TryGetValue(image, out var id))
            {
                symbols[image] = id = $"img{symbols.Count + 1}";
            }

            return id;
        }

        var sections = new StringBuilder();
        for (var i = 0; i < deck.Slides.Count; i++)
        {
            var slide = deck.Slides[i];
            sections.Append(FormattableString.Invariant($"<section class=\"slide\" id=\"slide-{i + 1}\" data-number=\"{slide.Number}\""));
            if (slide.Transition is { } transition)
            {
                sections.Append(" data-transition=\"").Append(transition.Kind).Append('"');
                if (transition.Direction is { } direction)
                {
                    sections.Append(" data-direction=\"").Append(SceneSvg.Escape(direction)).Append('"');
                }

                sections.Append(FormattableString.Invariant($" data-duration=\"{transition.Duration}\""));
            }

            if (slide.Steps.Count > 0)
            {
                sections.Append(" data-steps=\"").Append(SceneSvg.Escape(Steps(slide.Steps))).Append('"');
            }

            sections.Append(" aria-label=\"").Append(SceneSvg.Escape(slide.Title)).Append('"');
            if (slide.Line > 0)
            {
                sections.Append(FormattableString.Invariant($" data-line=\"{slide.Line}\""));
            }

            sections.Append('>')
                .Append(SceneSvg.Write(deck, slide, SvgTarget.Html, Symbol, $"s{i + 1}-"));
            if (options.Notes && slide.Notes.Count > 0)
            {
                Notes(sections, slide.Notes);
            }

            sections.Append("</section>\n");
        }

        if (symbols.Count > 0)
        {
            page.Append(SceneSvg.Symbols(symbols.Select(s => (s.Key, s.Value)))).Append('\n');
        }

        page.Append(sections).Append("</main>\n")
            .Append("<div class=\"bar\"><span id=\"counter\"></span><button id=\"help-button\" type=\"button\" aria-label=\"Keys\">?</button></div>\n")
            .Append("<div id=\"help\" role=\"dialog\" aria-label=\"Keys\" hidden><table>");
        foreach (var (keys, what) in Keys)
        {
            page.Append("<tr><td><kbd>").Append(SceneSvg.Escape(keys)).Append("</kbd></td><td>").Append(SceneSvg.Escape(what)).Append("</td></tr>");
        }

        page.Append("</table></div>\n<script>").Append(Script).Append("</script>\n</body>\n</html>\n");
        return page.ToString();
    }

    /// <summary>
    /// The build steps as JSON for the script: per step its effects, each with the item's id, the paragraph range,
    /// the class (<c>in</c>, <c>out</c>, <c>em</c>), the effect, the side and the duration.
    /// </summary>
    internal static string Steps(IReadOnlyList<SceneStep> steps)
    {
        var json = new StringBuilder("[");
        foreach (var step in steps)
        {
            json.Append(json.Length > 1 ? ",[" : "[");
            var first = true;
            foreach (var effect in step.Effects)
            {
                json.Append(first ? "{" : ",{").Append(CultureInfo.InvariantCulture, $"\"id\":{effect.Id}");
                if (effect.Paragraphs is var (start, end))
                {
                    json.Append(CultureInfo.InvariantCulture, $",\"p\":[{start},{end}]");
                }

                var effectClass = effect.Class switch
                {
                    EffectClass.Exit => "out",
                    EffectClass.Emphasis => "em",
                    _ => "in",
                };
                json.Append(",\"c\":\"").Append(effectClass).Append("\",\"e\":\"").Append(effect.Kind.ToString().ToLowerInvariant()).Append('"');
                if (effect.Direction is { } direction)
                {
                    json.Append(",\"d\":\"").Append(direction).Append('"');
                }

                json.Append(CultureInfo.InvariantCulture, $",\"t\":{effect.Duration}}}");
                first = false;
            }

            json.Append(']');
        }

        return json.Append(']').ToString();
    }

    /// <summary>The speaker notes as HTML: paragraphs, nested lists by level, bold, italic, line breaks and web or mail links.</summary>
    internal static void Notes(StringBuilder html, IReadOnlyList<NoteParagraph> paragraphs)
    {
        html.Append("<aside class=\"notes\" aria-label=\"Speaker notes\">");
        var open = new Stack<(int Level, string Tag)>();
        void Close()
        {
            var (_, tag) = open.Pop();
            html.Append("</li></").Append(tag).Append('>');
        }

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.List == NoteList.None)
            {
                while (open.Count > 0)
                {
                    Close();
                }

                html.Append("<p>");
                Runs(html, paragraph.Runs);
                html.Append("</p>");
                continue;
            }

            var tag = paragraph.List == NoteList.Number ? "ol" : "ul";
            while (open.Count > 0 && (open.Peek().Level > paragraph.Level || (open.Peek().Level == paragraph.Level && open.Peek().Tag != tag)))
            {
                Close();
            }

            if (open.Count > 0 && open.Peek().Level == paragraph.Level)
            {
                html.Append("</li><li>");
            }
            else
            {
                html.Append('<').Append(tag);
                if (tag == "ol" && paragraph.Start != 1)
                {
                    html.Append(CultureInfo.InvariantCulture, $" start=\"{paragraph.Start}\"");
                }

                html.Append("><li>");
                open.Push((paragraph.Level, tag));
            }

            Runs(html, paragraph.Runs);
        }

        while (open.Count > 0)
        {
            Close();
        }

        html.Append("</aside>");
    }

    private static void Runs(StringBuilder html, IReadOnlyList<NoteRun> runs)
    {
        foreach (var run in runs)
        {
            if (run.Text == "\n")
            {
                html.Append("<br>");
                continue;
            }

            var text = new StringBuilder(SceneSvg.Escape(run.Text));
            if (run.Italic)
            {
                text.Insert(0, "<em>").Append("</em>");
            }

            if (run.Bold)
            {
                text.Insert(0, "<strong>").Append("</strong>");
            }

            if (run.Link is { } link)
            {
                text.Insert(0, $"<a href=\"{SceneSvg.Escape(link)}\" target=\"_blank\" rel=\"noopener noreferrer\">").Append("</a>");
            }

            html.Append(text);
        }
    }

    private static string Number(double value) => Math.Round(value, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Resource(string name)
    {
        using var stream = typeof(HtmlDeckWriter).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource '{name}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
