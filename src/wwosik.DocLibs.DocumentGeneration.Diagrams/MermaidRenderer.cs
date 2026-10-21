using Mermaider;
using Mermaider.Models;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams;

/// <summary>
/// Draws <c>```mermaid</c> code blocks as diagrams in the brand's diagram colours and font (its table font, else its
/// text font): vector graphics in PDFs, editable shapes in decks. Add it to <c>PdfOptions.CodeBlockRenderers</c> or
/// <c>PptxOptions.CodeBlockRenderers</c>. Flowchart, sequence, class, state, ER, pie, gantt and mindmap diagrams are
/// drawn; other Mermaid types stay code. Built on Mermaider (MIT), without a browser or Node.js.
/// </summary>
public sealed class MermaidRenderer : ICodeBlockRenderer
{
    /// <summary>The diagram types drawn; the others stay code until they are tested.</summary>
    internal const DiagramTypes Supported = DiagramTypes.Flowchart | DiagramTypes.Sequence | DiagramTypes.Class | DiagramTypes.State
        | DiagramTypes.Er | DiagramTypes.Pie | DiagramTypes.Gantt | DiagramTypes.Mindmap;

    /// <summary>What a block that cannot be drawn does. Default <see cref="MermaidErrorMode.Throw"/>.</summary>
    public MermaidErrorMode OnError { get; init; } = MermaidErrorMode.Throw;

    /// <summary>
    /// A <see cref="DrawingGraphic"/> for a <c>mermaid</c> block (the language is case-insensitive) of a supported
    /// type; <see langword="null"/> for other languages and other diagram types.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The diagram cannot be parsed or drawn and <see cref="OnError"/> is <see cref="MermaidErrorMode.Throw"/>; the
    /// message names the block's line. Also when the brand is invalid.
    /// </exception>
    public CodeBlockGraphic? Render(CodeBlockRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Language.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var options = Options(ResolvedBrand.Resolve(request.Brand));
        try
        {
            return new DrawingGraphic(SvgDrawing.Convert(Mermaider.MermaidRenderer.RenderSvg(request.Code, options)));
        }
        catch (MermaidParseException ex) when (ex.Message.Contains("is not in the allowed set", StringComparison.Ordinal))
        {
            // A Mermaid type outside Supported.
            return null;
        }
        catch (Exception ex) when (ex is MermaidParseException or MermaidRenderException or MermaidResourceLimitException or MermaidSvgException or InvalidDataException)
        {
            if (this.OnError == MermaidErrorMode.KeepCode)
            {
                return null;
            }

            var where = request.Line > 0 ? $" at line {request.Line}" : string.Empty;
            throw new ArgumentException($"The mermaid code block{where} cannot be drawn: {ex.Message}", nameof(request), ex);
        }
    }

    /// <summary>The brand's diagram colours and font as Mermaider's options: white background, no gradients or shadows.</summary>
    internal static RenderOptions Options(ResolvedBrand brand)
    {
        var colors = brand.Diagram;
        return new RenderOptions
        {
            Bg = "#FFFFFF",
            Fg = "#" + colors.Text,
            Line = "#" + colors.Line,
            Accent = "#" + colors.Accent,
            Surface = "#" + colors.Surface,
            Border = "#" + colors.Border,
            DataPalette = [.. colors.Palette.Select(c => "#" + c)],
            Font = brand.Fonts?.TableFamily,
            Gradient = false,
            Elevation = 0,
            Transparent = false,
            AllowedDiagrams = Supported,
        };
    }
}

/// <summary>What <see cref="MermaidRenderer"/> does with a block it cannot draw.</summary>
public enum MermaidErrorMode
{
    /// <summary>Throw <see cref="ArgumentException"/> naming the block's line and the reason, so mistakes are noticed.</summary>
    Throw,

    /// <summary>Leave the block as code.</summary>
    KeepCode,
}
