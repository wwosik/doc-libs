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
    /// <summary>The slides written to a deck, or the pages written to a PDF.</summary>
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

    /// <summary>A renderer declined a code block in its own language, which therefore stays code.</summary>
    public const string CodeNotDrawn = "code-not-drawn";

    /// <summary>Raw HTML was written as text.</summary>
    public const string HtmlAsText = "html-as-text";

    /// <summary>A character the fonts lack was replaced through <c>PdfOptions.ReplaceUnsupported</c>.</summary>
    public const string CharacterReplaced = "character-replaced";

    /// <summary>A footnote reference (<c>[^1]</c>) has no definition, so it stays text.</summary>
    public const string FootnoteMissing = "footnote-missing";

    /// <summary>The document language has no texts of its own; the generated texts are in English.</summary>
    public const string LanguageUnknown = "language-unknown";

    /// <summary>Every code, with the formats it occurs in and what a writer changes about it, for the authoring guides.</summary>
    internal static readonly IReadOnlyList<(string Code, DocumentFormat[] Formats, string Advice)> All =
    [
        (SlideSplit, [DocumentFormat.Pptx], "the slide had too much content and continues on further slides. Shorten it, or split it into slides of your own with their own titles."),
        (SlideShrunk, [DocumentFormat.Pptx], "the slide's text was set at the smallest size to fit. Shorten it."),
        (SlideOverfull, [DocumentFormat.Pptx], "one item, such as a long list entry or a table with very long cells, does not fit even on a slide of its own. Shorten it."),
        (ImageEnlarged, [DocumentFormat.Pptx], "the image is small and was enlarged; it may look blurred. Use a larger image, or `\"scale=1\"` in its title to keep it at its size."),
        (CodeNotDrawn, [DocumentFormat.Pptx, DocumentFormat.Pdf], "a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type."),
        (HtmlAsText, [DocumentFormat.Pptx, DocumentFormat.Pdf], "HTML is not interpreted: it is shown as text. Write Markdown instead."),
        (CharacterReplaced, [DocumentFormat.Pdf], "a character the fonts lack was replaced. Write it as one of the replacements under *Characters*."),
        (FootnoteMissing, [DocumentFormat.Pptx, DocumentFormat.Pdf], "a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference."),
        (LanguageUnknown, [DocumentFormat.Pptx, DocumentFormat.Pdf], "the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`."),
    ];
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
