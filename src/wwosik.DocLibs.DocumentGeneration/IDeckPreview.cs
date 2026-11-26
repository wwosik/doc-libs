namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Paints a contact sheet of a generated deck for <see cref="DocumentTool"/> (<see cref="DocumentToolOptions.Preview"/>),
/// so an agent that sees images can look at its slides. <c>DeckContactSheet</c> in
/// <c>wwosik.DocLibs.DocumentConversions.Svg</c> implements it without a browser; this package knows no painter.
/// </summary>
public interface IDeckPreview
{
    /// <summary>The most slides on one sheet; a longer deck shows its first ones, and the reply says so.</summary>
    const int MaxSlides = 60;

    /// <summary>
    /// The slides of <paramref name="pptx"/> (the deck just written, with <paramref name="result"/>) as one PNG, at most
    /// <see cref="MaxSlides"/> of them, from the first; <see langword="null"/> when there is nothing to paint. The same
    /// deck gives the same PNG.
    /// </summary>
    /// <exception cref="DocumentConversions.DocumentConversionException">
    /// The deck cannot be painted; <see cref="DocumentTool"/> then replies without a sheet.
    /// </exception>
    byte[]? ContactSheet(ReadOnlyMemory<byte> pptx, GenerationResult result);
}
