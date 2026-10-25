using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Turns fenced code blocks of a language it knows, such as <c>mermaid</c>, into a drawing or an image. The
/// generators ask their renderers in order; the first graphic wins, and a block nobody renders stays code.
/// </summary>
public interface ICodeBlockRenderer
{
    /// <summary>A drawing or an image for the block, or <see langword="null"/> to leave it as code.</summary>
    CodeBlockGraphic? Render(CodeBlockRequest request);

    /// <summary>
    /// What a Markdown writer needs to know about the blocks this renderer takes, as Markdown under a <c>##</c>
    /// heading of its own, for the generators' authoring guides. <see langword="null"/> (the default): nothing.
    /// </summary>
    string? AuthoringGuide => null;
}

/// <summary>A fenced code block to render.</summary>
/// <param name="Language">The info string after the opening fence, e.g. <c>mermaid</c>; empty if there is none.</param>
/// <param name="Code">The block's content.</param>
/// <param name="Brand">The brand the document is generated in, after front matter.</param>
public sealed record CodeBlockRequest(string Language, string Code, Brand Brand)
{
    /// <summary>The line of the opening fence in the Markdown, from 1, front matter included; 0 if unknown.</summary>
    public int Line { get; init; }
}

/// <summary>
/// What a <see cref="ICodeBlockRenderer"/> returns: a <see cref="DrawingGraphic"/>, an <see cref="ImageGraphic"/> or a
/// <see cref="ChartGraphic"/>.
/// </summary>
public abstract record CodeBlockGraphic
{
    private protected CodeBlockGraphic()
    {
    }
}

/// <summary>A vector drawing, drawn natively by both generators.</summary>
/// <param name="Drawing">The drawing.</param>
public sealed record DrawingGraphic(Drawing Drawing) : CodeBlockGraphic;

/// <summary>An image, placed like a Markdown image: PNG or JPEG for PDFs; PNG, JPEG or GIF for decks.</summary>
/// <param name="Image">The image.</param>
public sealed record ImageGraphic(DocumentResource Image) : CodeBlockGraphic;

/// <summary>
/// A chart: decks write it as a native, editable PowerPoint chart from <paramref name="Chart"/>; PDFs draw
/// <paramref name="Drawing"/>, the same chart as vectors.
/// </summary>
/// <param name="Chart">The chart's kind, labels and data.</param>
/// <param name="Drawing">The chart drawn in the brand's colours and fonts.</param>
public sealed record ChartGraphic(ChartSpec Chart, Drawing Drawing) : CodeBlockGraphic;

/// <summary>A bar, line or area chart: categories along the horizontal axis, one series per value column.</summary>
/// <param name="Kind">Bars (vertical), lines or areas.</param>
/// <param name="Categories">The labels of the horizontal axis.</param>
/// <param name="Series">The series, each with one value per category.</param>
public sealed record ChartSpec(ChartKind Kind, IReadOnlyList<string> Categories, IReadOnlyList<ChartSeries> Series)
{
    /// <summary>Bars stacked on each other, or areas on top of each other, rather than side by side or overlapping.</summary>
    public bool Stacked { get; init; }

    /// <summary>The chart's title; <see langword="null"/>: none.</summary>
    public string? Title { get; init; }

    /// <summary>The unit of the values, shown with the value axis (<c>k</c>, <c>%</c>, <c>EUR</c>); <see langword="null"/>: none.</summary>
    public string? Unit { get; init; }

    /// <summary>The colours of the series, upper-case RGB hex without <c>#</c>, in order; they repeat when there are more series.</summary>
    public IReadOnlyList<string> Colors { get; init; } = [];

    /// <inheritdoc/>
    public bool Equals(ChartSpec? other) =>
        other is not null && this.Kind == other.Kind && this.Stacked == other.Stacked && this.Title == other.Title && this.Unit == other.Unit
        && this.Categories.SequenceEqual(other.Categories) && this.Series.SequenceEqual(other.Series) && this.Colors.SequenceEqual(other.Colors);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Kind, this.Title, this.Categories.Count, this.Series.Count);
}

/// <summary>One series of a chart: its name, from the column header, and a value per category (<see langword="null"/> where empty).</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double?> Values)
{
    /// <inheritdoc/>
    public bool Equals(ChartSeries? other) => other is not null && this.Name == other.Name && this.Values.SequenceEqual(other.Values);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Name, this.Values.Count);
}

/// <summary>The kinds of chart.</summary>
public enum ChartKind
{
    /// <summary>Vertical bars, clustered or stacked.</summary>
    Bar,

    /// <summary>Lines with markers.</summary>
    Line,

    /// <summary>Filled areas, overlapping or stacked.</summary>
    Area,
}

/// <summary>Asks the renderers in order.</summary>
internal static class CodeBlockRenderers
{
    /// <exception cref="DocumentConversionException">
    /// A renderer threw; its exception is the inner exception, so every document problem has the same type.
    /// </exception>
    public static CodeBlockGraphic? Render(IReadOnlyList<ICodeBlockRenderer>? renderers, CodeBlockRequest request, DocumentFormat format)
    {
        foreach (var renderer in renderers ?? [])
        {
            CodeBlockGraphic? graphic;
            try
            {
                graphic = renderer.Render(request);
            }
            catch (Exception ex) when (ex is not DocumentConversionException)
            {
                throw new DocumentConversionException(format, ex.Message, ex);
            }

            if (graphic is not null)
            {
                return graphic;
            }
        }

        return null;
    }
}
