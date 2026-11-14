using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// What a generator wrote: the number of slides or pages, and <see cref="Notes"/> about what it did to the Markdown
/// that the writer cannot see in the result: a slide split or set small, an image blown up, a diagram left as code.
/// Pass the notes back to whoever wrote the Markdown, so they can fix the source.
/// </summary>
/// <param name="Notes">The notes, in the order of the Markdown; empty when there is nothing to report.</param>
public sealed record GenerationResult(IReadOnlyList<GenerationNote> Notes)
{
    /// <summary>
    /// The slides written to a deck, or the pages written to a PDF; 0 for a Word document, whose pages Word lays out
    /// itself.
    /// </summary>
    public int Pages { get; init; }

    /// <summary>Whether there is anything to report.</summary>
    public bool HasNotes => this.Notes.Count > 0;

    /// <summary>One line per note, <c>- line 12: …</c>; <c>No notes.</c> when there are none.</summary>
    public override string ToString()
    {
        if (this.Notes.Count == 0)
        {
            return "No notes.";
        }

        var text = new StringBuilder();
        foreach (var note in this.Notes)
        {
            text.Append("- ").Append(note).Append('\n');
        }

        return text.ToString(0, text.Length - 1);
    }

    /// <inheritdoc/>
    public bool Equals(GenerationResult? other) =>
        other is not null && this.Pages == other.Pages && this.Notes.SequenceEqual(other.Notes);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Pages, this.Notes.Count);
}

/// <summary>Something a generator did to the Markdown that the writer should know about.</summary>
/// <param name="Code">What happened, one of <see cref="NoteCodes"/>; stable, for a host to filter on.</param>
/// <param name="Line">The line in the Markdown it concerns, from 1, front matter included; 0 if unknown.</param>
/// <param name="Message">What happened and what to change, in English, naming the slide or block.</param>
public sealed record GenerationNote(string Code, int Line, string Message)
{
    /// <summary><c>line 12: Message</c>, or the message alone when the line is unknown.</summary>
    public override string ToString() => this.Line > 0 ? $"line {this.Line}: {this.Message}" : this.Message;
}

/// <summary>The codes of <see cref="GenerationNote"/>.</summary>
public static class NoteCodes
{
    /// <summary>A slide's content did not fit, so it continues on further slides with the same title.</summary>
    public const string SlideSplit = "slide-split";

    /// <summary>A slide's text or table had to be set at the smallest size to fit.</summary>
    public const string SlideShrunk = "slide-shrunk";

    /// <summary>A slide's content runs past the content area even at the smallest size.</summary>
    public const string SlideOverfull = "slide-overfull";

    /// <summary>An image was drawn at more than twice its natural size, and may look blurred.</summary>
    public const string ImageEnlarged = "image-enlarged";

    /// <summary>An image was cropped by more than a tenth of its width or height to fill a picture placeholder of a slide layout.</summary>
    public const string ImageCropped = "image-cropped";

    /// <summary>
    /// A renderer declined a code block in its own language, which therefore stays code; in Word documents also a drawing
    /// (a diagram, a QR code, a drawn container) that no image converter turned into a picture.
    /// </summary>
    public const string CodeNotDrawn = "code-not-drawn";

    /// <summary>Raw HTML was written as text.</summary>
    public const string HtmlAsText = "html-as-text";

    /// <summary>A character the fonts lack was replaced through <c>PdfOptions.ReplaceUnsupported</c>.</summary>
    public const string CharacterReplaced = "character-replaced";

    /// <summary>A footnote reference (<c>[^1]</c>) has no definition, so it stays text.</summary>
    public const string FootnoteMissing = "footnote-missing";

    /// <summary>The document language has no texts of its own; the generated texts are in English.</summary>
    public const string LanguageUnknown = "language-unknown";

    /// <summary>A <c>::: notes</c> before the first heading belongs to no slide and was left out.</summary>
    public const string NotesDropped = "notes-dropped";

    /// <summary>Speaker notes held a table, code block, image, quote or other block, which was written as plain text.</summary>
    public const string NotesSimplified = "notes-simplified";
    /// <summary>An image attribute (<c>{fit=cover}</c>, <c>{.caption}</c> …) has no effect in this format or in this place.</summary>
    public const string AttributeIgnored = "attribute-ignored";

    /// <summary>An icon (<c>:icon-name:</c>) stands where the format cannot draw it, such as inside a slide's text, and was left out.</summary>
    public const string IconNotDrawn = "icon-not-drawn";

    /// <summary>A slide kind (<c>{.quote}</c> and the others) asked for a layout the template does not have, so it is drawn as a flow slide.</summary>
    public const string LayoutFallback = "layout-fallback";

    /// <summary>An empty <c>{.agenda}</c> found no chapters to list, so it shows its title only.</summary>
    public const string AgendaEmpty = "agenda-empty";

    /// <summary>A <c>Table: …</c> or <c>Figure: …</c> paragraph follows no table or drawn diagram, so it is shown as text.</summary>
    public const string CaptionIgnored = "caption-ignored";

    /// <summary>A table of contents was asked for (<c>toc: true</c>), but no heading is at the depth it lists, so there is none.</summary>
    public const string TocEmpty = "toc-empty";

    /// <summary>A list is nested deeper than three levels; the deeper items are set at the third level.</summary>
    public const string ListTooDeep = "list-too-deep";

    /// <summary>
    /// Front matter asked for a lower classification (<c>classification:</c>) than the host set; the host's applies.
    /// </summary>
    public const string ClassificationKept = "classification-kept";

    /// <summary>Check only: a slide has more list items, at every level, than <see cref="CheckOptions.MaxBulletsPerSlide"/>.</summary>
    public const string TooManyBullets = "too-many-bullets";

    /// <summary>Check only: a slide has more words, its title excluded, than <see cref="CheckOptions.MaxWordsPerSlide"/>.</summary>
    public const string TooManyWords = "too-many-words";

    /// <summary>Check only: a paragraph of a PDF or Word document has more words than <see cref="CheckOptions.MaxWordsPerParagraph"/>.</summary>
    public const string LongParagraph = "long-paragraph";

    /// <summary>Check only: a slide title or heading is longer than <see cref="CheckOptions.MaxTitleLength"/>.</summary>
    public const string TitleTooLong = "title-too-long";

    /// <summary>Check only: an image has no alt text, and <see cref="CheckOptions.RequireAltText"/> is on.</summary>
    public const string AltTextMissing = "alt-text-missing";

    /// <summary>
    /// Check only: an image is not among the document's resources, or is a web address or <c>data:</c> URL, which the
    /// generators never load. <c>Generate</c> throws for it instead.
    /// </summary>
    public const string ImageSource = "image-source";

    /// <summary>
    /// The HTML deck left out something of a slide it cannot draw: an element, preset shape, chart kind, image format or
    /// effect, named in the note with the slides it is on.
    /// </summary>
    public const string DrawingUnsupported = "drawing-unsupported";

    /// <summary>
    /// The HTML deck measured a family or face with a stand-in that is not metric-compatible, or by estimate, so its
    /// lines may break differently from PowerPoint's.
    /// </summary>
    public const string FontMissing = "font-missing";

    /// <summary>Every code, with the formats it occurs in and what a writer changes about it, for the authoring guides.</summary>
    internal static readonly IReadOnlyList<(string Code, DocumentFormat[] Formats, string Advice)> All =
    [
        (SlideSplit, [DocumentFormat.Pptx], "the slide had too much content and continues on further slides. Shorten it, or split it into slides of your own with their own titles."),
        (SlideShrunk, [DocumentFormat.Pptx], "the slide's text was set at the smallest size to fit. Shorten it."),
        (SlideOverfull, [DocumentFormat.Pptx], "one item, such as a long list entry or a table with very long cells, does not fit even on a slide of its own. Shorten it."),
        (ImageEnlarged, [DocumentFormat.Pptx], "the image is small and was enlarged; it may look blurred. Use a larger image, or `\"scale=1\"` in its title to keep it at its size."),
        (ImageCropped, [DocumentFormat.Pptx], "an image was cut to fill a picture placeholder of the layout, and much of it is not shown. Use an image of the placeholder's shape, or `focus=` to say which part stays."),
        (CodeNotDrawn, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type."),
        (HtmlAsText, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "HTML is not interpreted: it is shown as text. Write Markdown instead."),
        (CharacterReplaced, [DocumentFormat.Pdf], "a character the fonts lack was replaced. Write it as one of the replacements under *Characters*."),
        (FootnoteMissing, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference."),
        (LanguageUnknown, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`."),
        (NotesDropped, [DocumentFormat.Pptx], "a `::: notes` stands before the first heading, so it belongs to no slide and was left out. Move it under the heading of the slide it is for."),
        (NotesSimplified, [DocumentFormat.Pptx], "speaker notes hold only paragraphs and lists; a table, code block, image or quote in `::: notes` was written as plain text. Keep speaker notes to text and lists."),
        (AttributeIgnored, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "an image attribute has no effect here, for example `fit=cover` on an image of its own in a PDF, or `focus` on an image that is not cropped. Remove it, or use it where it works."),
        (IconNotDrawn, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx],"an icon was left out where it cannot be drawn. Move it as the *Icons* section says."),
        (LayoutFallback, [DocumentFormat.Pptx], "the design has no layout for that kind of slide (`{.quote}` and the others), so it is drawn as a layout slide with the same content. Nothing to fix, unless the design's own look matters."),
        (AgendaEmpty, [DocumentFormat.Pptx, DocumentFormat.Pdf], "an empty `{.agenda}` found no `#` chapters to list. Add the chapters, or write the agenda as a list."),
        (CaptionIgnored, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "a `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text. Move it right after its table, or write it as ordinary text."),
        (TocEmpty, [DocumentFormat.Pdf], "`toc: true` asked for a table of contents, but the document has no headings at the depth it lists, so there is none. Add headings, raise `tocDepth`, or leave `toc` out."),
        (ListTooDeep, [DocumentFormat.Docx], "a list is nested deeper than three levels; the deeper items are set at the third level. Flatten the list to three levels."),
        (ClassificationKept, [DocumentFormat.Docx], "the front matter asks for a lower `classification` than the host set, and the host's applies. Leave the key out, or raise it."),
        (TooManyBullets, [DocumentFormat.Pptx], "a slide has more list items, counting every level, than *Limits* allows. Cut them, or split the slide into slides of your own."),
        (TooManyWords, [DocumentFormat.Pptx], "a slide has more words than *Limits* allows, its title and speaker notes not counted. Shorten it, or move detail into `::: notes`."),
        (LongParagraph, [DocumentFormat.Pdf, DocumentFormat.Docx], "a paragraph has more words than *Limits* allows. Split it, or make a list of it."),
        (TitleTooLong, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "a title or heading is longer than *Limits* allows. Shorten it; the detail goes in the text."),
        (AltTextMissing, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "an image has no alt text. Say what it shows: `![Sales by region](sales.png)`."),
        (ImageSource, [DocumentFormat.Pptx, DocumentFormat.Pdf, DocumentFormat.Docx], "an image is not among the files given to you, or is a web address or `data:` URL, which is never loaded. Use only the paths you were given."),
        (DrawingUnsupported, [DocumentFormat.Pptx], "the HTML deck could not draw something on a slide, such as a shadow or an EMF picture, and left it out. Nothing to fix in the Markdown: it comes from the template, or from a deck made elsewhere."),
        (FontMissing, [DocumentFormat.Pptx], "the HTML deck measured a font it does not have with a stand-in or an estimate, so its lines may break differently from PowerPoint's. Pass the font in `HtmlDeckOptions.Fonts`."),
    ];

    /// <summary>The codes only a check returns, never <c>Generate</c>: the guides list them apart.</summary>
    internal static readonly IReadOnlySet<string> CheckOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        TooManyBullets, TooManyWords, LongParagraph, TitleTooLong, AltTextMissing, ImageSource,
    };

    /// <summary>What the guides of one format add to a code's advice in <see cref="All"/>.</summary>
    internal static readonly IReadOnlyDictionary<(string Code, DocumentFormat Format), string> FormatAdvice = new Dictionary<(string, DocumentFormat), string>
    {
        [(CodeNotDrawn, DocumentFormat.Docx)] = "In a Word document, diagrams, QR codes and drawn containers become pictures only where an image converter is set up; without one they stay code, alt text or their content. Ask whoever generates the document to set one up.",
        [(IconNotDrawn, DocumentFormat.Docx)] = "In a Word document, icons are drawn only where an image converter is set up, and are left out otherwise.",
    };
}

/// <summary>Collects the notes of one generation, in the order of the Markdown; a note repeated word for word is kept once.</summary>
internal sealed class GenerationNotes
{
    private readonly List<GenerationNote> notes = [];
    private readonly HashSet<(string, int, string)> seen = [];

    public void Add(string code, int line, string message)
    {
        if (this.seen.Add((code, line, message)))
        {
            this.notes.Add(new GenerationNote(code, line, message));
        }
    }

    /// <summary>
    /// The notes ordered by line, those without one last; stable, so notes of one line keep the order they were added in.
    /// </summary>
    public GenerationResult Result(int pages) => new([.. this.notes.OrderBy(n => n.Line > 0 ? n.Line : int.MaxValue)]) { Pages = pages };

    /// <summary>A short quote of <paramref name="text"/> for a message: its first line, cut at 40 characters.</summary>
    public static string Quote(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length <= 40 ? line : line[..39] + "…";
    }
}
