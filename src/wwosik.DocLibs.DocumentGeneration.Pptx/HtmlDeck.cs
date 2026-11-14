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
}

/// <summary>
/// A deck as one self-contained HTML page, drawn from the <c>.pptx</c> itself: one SVG per visible slide, keys to move
/// through them, an overview, a black screen and one slide per printed sheet. Nothing is loaded from the network.
/// Markdown to an HTML deck is <see cref="PptxGenerator.Generate"/> into a stream, then this.
/// </summary>
public static class HtmlDeck
{
    /// <summary>
    /// Writes <paramref name="pptx"/> (any presentation, read best effort) as an HTML page to <paramref name="html"/>.
    /// <see cref="GenerationResult.Pages"/> is the number of slides written; the notes say what could not be drawn
    /// (<see cref="NoteCodes.DrawingUnsupported"/>) and which fonts were measured by stand-in or estimate
    /// (<see cref="NoteCodes.FontMissing"/>).
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
        return notes.Result(deck.Slides.Count);
    }
}

/// <summary>The HTML page around the slide SVGs: head with CSP and fonts, sections, the key list and the script.</summary>
internal static class HtmlDeckWriter
{
    private const double EmuPerPoint = 12700;

    public static readonly string Script = Resource("HtmlDeck.deck.js");
    public static readonly string Styles = Resource("HtmlDeck.deck.css");

    /// <summary>The script's hash for the Content Security Policy.</summary>
    public static string ScriptHash => "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Script)));

    private static readonly (string Keys, string What)[] Keys =
    [
        ("→ ↓ Space Page Down Enter, click, swipe left", "next slide"),
        ("← ↑ Page Up Backspace, swipe right", "previous slide"),
        ("Home / End", "first / last slide"),
        ("a number, then Enter", "that slide"),
        ("F", "full screen"),
        ("O", "overview of all slides"),
        ("B or .", "black screen"),
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

        page.Append(Styles).Append("</style>\n</head>\n<body>\n<main class=\"deck\" id=\"deck\">\n");

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
            sections.Append(FormattableString.Invariant($"<section class=\"slide\" id=\"slide-{i + 1}\" data-number=\"{slide.Number}\" aria-label=\""))
                .Append(SceneSvg.Escape(slide.Title)).Append("\">")
                .Append(SceneSvg.Write(deck, slide, SvgTarget.Html, Symbol, $"s{i + 1}-"))
                .Append("</section>\n");
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

    private static string Number(double value) => Math.Round(value, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Resource(string name)
    {
        using var stream = typeof(HtmlDeckWriter).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource '{name}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
