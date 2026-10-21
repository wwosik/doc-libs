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

/// <summary>What a <see cref="ICodeBlockRenderer"/> returns: a <see cref="DrawingGraphic"/> or an <see cref="ImageGraphic"/>.</summary>
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

/// <summary>Asks the renderers in order.</summary>
internal static class CodeBlockRenderers
{
    public static CodeBlockGraphic? Render(IReadOnlyList<ICodeBlockRenderer>? renderers, CodeBlockRequest request)
    {
        foreach (var renderer in renderers ?? [])
        {
            if (renderer.Render(request) is { } graphic)
            {
                return graphic;
            }
        }

        return null;
    }
}
