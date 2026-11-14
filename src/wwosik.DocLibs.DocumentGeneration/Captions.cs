using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>What a caption belongs to, which decides its label and its numbering.</summary>
internal enum CaptionKind
{
    /// <summary>An image, a drawn diagram or a chart: "Figure n".</summary>
    Figure,

    /// <summary>A table: "Table n".</summary>
    Table,
}

/// <summary>
/// The caption syntax every generator reads alike:
/// <list type="bullet">
/// <item>an image's title is its caption, once a <c>scale=N</c> token and the word <c>caption</c> standing alone are
/// taken out; a title of settings only gives none, but <c>"caption"</c> (or the attribute <c>.caption</c>) means "the
/// alt text";</item>
/// <item>a paragraph starting <c>Table: </c> directly after a table (after a blank line, which Markdown needs to end
/// the table) is the table's caption;</item>
/// <item>a paragraph starting <c>Figure: </c> directly after a drawn diagram or chart is the figure's caption.</item>
/// </list>
/// <c>Table:</c> and <c>Figure:</c> are syntax and stay English; the label written is the document language's
/// (<see cref="GeneratedTexts.Table"/>, <see cref="GeneratedTexts.Figure"/>), and a caption reads
/// <c>&lt;label&gt; &lt;n&gt;: &lt;text&gt;</c>, figures and tables numbered apart through the whole document. Word
/// documents and PDFs number captions; decks show a <c>Table:</c> or <c>Figure:</c> paragraph under its visual without
/// the prefix and without a number, and keep their own image titles.
/// </summary>
internal static partial class Captions
{
    /// <summary>The caption of an image, or <see langword="null"/> for none.</summary>
    /// <param name="title">The image's title, as written.</param>
    /// <param name="captionAttribute">Whether the image has the attribute <c>.caption</c>.</param>
    /// <param name="altText">The image's alt text, the caption that <c>caption</c> asks for.</param>
    public static string? OfImage(string? title, bool captionAttribute, string altText)
    {
        var rest = ScalePattern().Replace(title ?? string.Empty, " ");
        var asked = captionAttribute;
        if (CaptionWordPattern().IsMatch(rest))
        {
            asked = true;
            rest = CaptionWordPattern().Replace(rest, " ");
        }

        rest = WhitespacePattern().Replace(rest, " ").Trim();
        if (rest.Length > 0)
        {
            return rest;
        }

        return asked && altText.Trim().Length > 0 ? altText.Trim() : null;
    }

    /// <summary>The kind of caption <paramref name="paragraph"/> starts as, <c>Table: </c> or <c>Figure: </c>; <see langword="null"/>: neither.</summary>
    public static CaptionKind? Kind(ParagraphBlock paragraph) =>
        paragraph.Inline?.FirstChild is LiteralInline literal && literal.Content.ToString() is var text
            ? text.StartsWith(Prefix(CaptionKind.Table), StringComparison.Ordinal) ? CaptionKind.Table
            : text.StartsWith(Prefix(CaptionKind.Figure), StringComparison.Ordinal) ? CaptionKind.Figure
            : null
            : null;

    /// <summary>
    /// Takes the prefix (<c>Table: </c> or <c>Figure: </c>) off the start of <paramref name="paragraph"/>, which
    /// <see cref="Kind"/> found, so its inlines are the caption's text.
    /// </summary>
    public static void TakePrefix(ParagraphBlock paragraph, CaptionKind kind)
    {
        var literal = (LiteralInline)paragraph.Inline!.FirstChild!;
        literal.Content = new Markdig.Helpers.StringSlice(literal.Content.ToString()[Prefix(kind).Length..].TrimStart());
    }

    /// <summary>The message of <see cref="NoteCodes.CaptionIgnored"/> for a <paramref name="kind"/> paragraph that follows nothing it captions.</summary>
    public static string Ignored(CaptionKind kind) =>
        $"The paragraph '{GenerationNotes.Quote(Prefix(kind))}…' follows no {(kind == CaptionKind.Table ? "table" : "drawn diagram or chart")}, so it is shown as text. Put it right after its {(kind == CaptionKind.Table ? "table" : "diagram")}.";

    /// <summary>The syntax before a caption: <c>Table: </c> or <c>Figure: </c>.</summary>
    public static string Prefix(CaptionKind kind) => kind == CaptionKind.Table ? "Table: " : "Figure: ";

    /// <summary>The label of a caption in the document language: "Table", "Abbildung".</summary>
    public static string Label(CaptionKind kind, GeneratedTexts texts) => kind == CaptionKind.Table ? texts.Table : texts.Figure;

    [GeneratedRegex(@"\bscale\s*=\s*[0-9]+(?:\.[0-9]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"(?<!\S)caption(?!\S)", RegexOptions.CultureInvariant)]
    private static partial Regex CaptionWordPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
