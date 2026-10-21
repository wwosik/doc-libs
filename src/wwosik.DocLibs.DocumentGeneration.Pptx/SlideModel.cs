using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

[Flags]
internal enum RunStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Strike = 8,
}

/// <summary>Text in one style; the text <c>"\n"</c> alone is a line break.</summary>
internal sealed record TextRun(string Text, RunStyle Style, string? Link = null)
{
    public static TextRun Break { get; } = new("\n", RunStyle.None);

    public bool IsBreak => this.Text == "\n";
}

internal enum ParagraphKind
{
    Plain,
    Bullet,
    Numbered,
}

internal enum CellAlignment
{
    Default,
    Left,
    Center,
    Right,
}

/// <summary>A paragraph; <see cref="NumberStart"/> is the start value of the numbered list it belongs to.</summary>
internal sealed record TextParagraph(IReadOnlyList<TextRun> Runs, ParagraphKind Kind = ParagraphKind.Plain, int Level = 0, int NumberStart = 1);

internal sealed record SlideTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<TextRun>>> Rows, IReadOnlyList<CellAlignment> Alignments, bool HasHeader);

internal sealed record SlideImage(DocumentResource Resource, string AltText, double Scale);

internal abstract record Slide(IReadOnlyList<TextRun> Title);

/// <summary>
/// From a <c>#</c> heading: title plus the text that follows it as subtitle. The first one is the cover, every
/// later one a section divider.
/// </summary>
internal sealed record TitleSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Subtitle, bool IsSection) : Slide(Title);

/// <summary>Text only, in the body placeholder of a title-and-content layout.</summary>
internal sealed record ContentSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<TextParagraph> Body) : Slide(Title);

/// <summary>One table or one image, with text above it and a caption below it.</summary>
internal sealed record VisualSlide(
    IReadOnlyList<TextRun> Title,
    IReadOnlyList<TextParagraph> Above,
    SlideTable? Table,
    SlideImage? Image,
    IReadOnlyList<TextParagraph> Below) : Slide(Title);
