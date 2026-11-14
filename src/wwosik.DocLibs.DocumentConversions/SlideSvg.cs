namespace wwosik.DocLibs.DocumentConversions;

/// <summary>
/// One slide of a deck as a standalone SVG, written for painting: what <c>DeckSvg.Write</c> in
/// <c>wwosik.DocLibs.DocumentGeneration.Pptx</c> returns and <c>DeckPreview.Render</c> in
/// <c>wwosik.DocLibs.DocumentConversions.Svg</c> paints, so the two packages fit together without referencing each other.
/// </summary>
/// <param name="Number">The slide's position in the deck, from 1, hidden slides counted.</param>
/// <param name="Title">The slide's title; empty when it has none.</param>
/// <param name="Svg">The SVG document, UTF-8.</param>
/// <param name="Width">The slide's width in points.</param>
/// <param name="Height">The slide's height in points.</param>
public sealed record SlideSvg(int Number, string Title, ReadOnlyMemory<byte> Svg, double Width, double Height);
