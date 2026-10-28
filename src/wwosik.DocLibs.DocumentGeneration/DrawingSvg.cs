using System.Globalization;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Writes a <see cref="Drawing"/> as a standalone SVG for web pages, with its hover texts as tooltips that need no
/// script: each item with a <see cref="DrawingItem.Hover"/> becomes a <c>&lt;g class="chart-hoverable"&gt;</c> holding the
/// item and a <c>&lt;g class="chart-tooltip"&gt;</c> placed in advance, and an inline <c>&lt;style&gt;</c> shows the tooltip
/// on <c>:hover</c>. The class names stay the same, so a page can restyle them: <c>chart-hoverable</c>,
/// <c>chart-tooltip</c>, <c>chart-tooltip-bg</c>, <c>chart-tooltip-text</c> and <c>chart-hover-dot</c>.
/// </summary>
public static class DrawingSvg
{
    private const double TooltipHeight = 17;
    private const double TooltipPadding = 6;
    private const double TooltipSize = 10;

    // The share of the font size a character takes, on average, in a proportional font. The SVG is written, not
    // measured, so tooltips and lines that must shrink to their room are sized by this estimate.
    private const double CharacterWidth = 0.58;

    private const double MinTextScale = 0.7;

    /// <summary>The drawing as SVG markup, <c>width</c> and <c>height</c> in pixels with a matching <c>viewBox</c>.</summary>
    public static string Write(Drawing drawing, DrawingSvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        options ??= new DrawingSvgOptions();
        var svg = new StringBuilder();
        var (width, height) = (Number(drawing.Width), Number(drawing.Height));
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"");
        svg.Append(CultureInfo.InvariantCulture, $" font-family=\"{Escape(options.FontFamily)}\">");
        if (drawing.Items.Any(item => item.Hover is not null))
        {
            svg.Append("<style>")
                .Append(".chart-tooltip,.chart-hover-dot{opacity:0;pointer-events:none;transition:opacity .1s ease-in-out}")
                .Append(".chart-hoverable:hover .chart-tooltip,.chart-hoverable:hover .chart-hover-dot{opacity:1}")
                .Append(CultureInfo.InvariantCulture, $".chart-tooltip-bg{{fill:#{options.TooltipFill};fill-opacity:.94}}")
                .Append(CultureInfo.InvariantCulture, $".chart-tooltip-text{{fill:#{options.TooltipText};font-size:{Number(TooltipSize)}px}}")
                .Append("</style>");
        }

        if (options.Background is { } background)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#{background}\"/>");
        }

        foreach (var item in drawing.Items)
        {
            var markup = item switch
            {
                DrawingShape shape => Shape(shape),
                DrawingText text => Text(text, options),
                DrawingHotspot spot => Hotspot(spot),
                _ => throw new InvalidOperationException("Unknown drawing item."),
            };
            svg.Append(item.Hover is { } hover ? Hoverable(markup, hover, drawing.Width) : markup);
        }

        return svg.Append("</svg>").ToString();
    }

    /// <summary>Roughly how wide <paramref name="text"/> is at <paramref name="size"/> pixels, in a proportional font.</summary>
    public static double EstimateWidth(string text, double size) => (text?.Length ?? 0) * size * CharacterWidth;

    private static string Shape(DrawingShape shape)
    {
        var paint = new StringBuilder();
        paint.Append(CultureInfo.InvariantCulture, $" fill=\"{(shape.Fill is { } fill ? "#" + fill : "none")}\"");
        if (shape.Stroke is { } stroke && shape.StrokeWidth > 0)
        {
            paint.Append(CultureInfo.InvariantCulture, $" stroke=\"#{stroke}\" stroke-width=\"{Number(shape.StrokeWidth)}\"");
            if (shape.Dash is { Count: > 0 } dash)
            {
                paint.Append(CultureInfo.InvariantCulture, $" stroke-dasharray=\"{string.Join(",", dash.Select(Number))}\"");
            }

            if (shape.RoundJoins)
            {
                paint.Append(" stroke-linejoin=\"round\" stroke-linecap=\"round\"");
            }
        }

        var (x, y, w, h) = shape.Path.Bounds;
        return shape.Kind switch
        {
            DrawingShapeKind.Rectangle =>
                $"<rect x=\"{Number(x)}\" y=\"{Number(y)}\" width=\"{Number(w)}\" height=\"{Number(h)}\"{paint}/>",
            DrawingShapeKind.RoundedRectangle =>
                $"<rect x=\"{Number(x)}\" y=\"{Number(y)}\" width=\"{Number(w)}\" height=\"{Number(h)}\" rx=\"{Number(shape.CornerRadius)}\"{paint}/>",
            DrawingShapeKind.Ellipse =>
                $"<ellipse cx=\"{Number(x + (w / 2))}\" cy=\"{Number(y + (h / 2))}\" rx=\"{Number(w / 2)}\" ry=\"{Number(h / 2)}\"{paint}/>",
            _ => $"<path d=\"{PathData(shape.Path)}\"{paint}/>",
        };
    }

    private static string PathData(DrawingPath path) => string.Join(" ", path.Segments.Select(segment => segment switch
    {
        MoveTo m => $"M{Number(m.X)},{Number(m.Y)}",
        LineTo l => $"L{Number(l.X)},{Number(l.Y)}",
        CubicTo c => $"C{Number(c.X1)},{Number(c.Y1)} {Number(c.X2)},{Number(c.Y2)} {Number(c.X)},{Number(c.Y)}",
        ClosePath => "Z",
        _ => throw new InvalidOperationException("Unknown path segment."),
    }));

    private static string Text(DrawingText text, DrawingSvgOptions options)
    {
        var size = text.Size;
        if (text.MaxWidth is { } room && EstimateWidth(text.Text, size) > room)
        {
            size *= Math.Max(MinTextScale, room / EstimateWidth(text.Text, size));
        }

        var markup = new StringBuilder();
        markup.Append(CultureInfo.InvariantCulture, $"<text x=\"{Number(text.X)}\" y=\"{Number(text.Baseline)}\" font-size=\"{Number(size)}\" fill=\"#{text.Color}\"");
        if (text.Anchor != TextAnchor.Start)
        {
            markup.Append(text.Anchor == TextAnchor.Middle ? " text-anchor=\"middle\"" : " text-anchor=\"end\"");
        }

        if (text.Mono)
        {
            markup.Append(CultureInfo.InvariantCulture, $" font-family=\"{Escape(options.MonoFamily)}\"");
        }

        markup.Append(" xml:space=\"preserve\">");
        foreach (var span in text.Spans)
        {
            if (!span.Bold && span.Color is null)
            {
                markup.Append(Escape(span.Text));
                continue;
            }

            markup.Append("<tspan");
            if (span.Bold)
            {
                markup.Append(" font-weight=\"bold\"");
            }

            if (span.Color is { } color)
            {
                markup.Append(CultureInfo.InvariantCulture, $" fill=\"#{color}\"");
            }

            markup.Append('>').Append(Escape(span.Text)).Append("</tspan>");
        }

        return markup.Append("</text>").ToString();
    }

    private static string Hotspot(DrawingHotspot spot)
    {
        var markup = $"<circle cx=\"{Number(spot.X)}\" cy=\"{Number(spot.Y)}\" r=\"{Number(spot.Radius)}\" fill=\"transparent\"/>";
        return spot.Dot is { } dot
            ? markup + $"<circle class=\"chart-hover-dot\" cx=\"{Number(spot.X)}\" cy=\"{Number(spot.Y)}\" r=\"{Number(spot.DotRadius)}\" fill=\"#{dot}\" stroke=\"#FFFFFF\" stroke-width=\"1.25\"/>"
            : markup;
    }

    // The tooltip is centred over the hover point, clamped to the canvas, and sits just above the point.
    private static string Hoverable(string markup, DrawingHover hover, double canvasWidth)
    {
        var width = EstimateWidth(hover.Text, TooltipSize) + (2 * TooltipPadding);
        var left = Math.Clamp(hover.X - (width / 2), 2, Math.Max(2, canvasWidth - width - 2));
        var top = hover.Y - TooltipHeight - 6;
        return $"<g class=\"chart-hoverable\">{markup}<g class=\"chart-tooltip\">"
            + $"<rect class=\"chart-tooltip-bg\" x=\"{Number(left)}\" y=\"{Number(top)}\" width=\"{Number(width)}\" height=\"{Number(TooltipHeight)}\" rx=\"3\"/>"
            + $"<text class=\"chart-tooltip-text\" x=\"{Number(left + (width / 2))}\" y=\"{Number(top + TooltipHeight - 5)}\" text-anchor=\"middle\">{Escape(hover.Text)}</text>"
            + "</g></g>";
    }

    private static string Number(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => DrawingSvgOptions.XmlSafe(text)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}

/// <summary>How <see cref="DrawingSvg"/> writes a drawing.</summary>
public sealed record DrawingSvgOptions
{
    /// <summary>The CSS font family of the text. Default: <c>Segoe UI, Arial, sans-serif</c>.</summary>
    public string FontFamily { get; init; } = "Segoe UI, Arial, sans-serif";

    /// <summary>The CSS font family of mono text (<see cref="DrawingText.Mono"/>). Default: <c>Consolas, monospace</c>.</summary>
    public string MonoFamily { get; init; } = "Consolas, monospace";

    /// <summary>A colour filling the whole canvas first, as RGB hex; <see langword="null"/> (the default): transparent.</summary>
    public string? Background { get; init; }

    /// <summary>The tooltip's background, drawn at 94 % opacity. Default: near-black <c>111827</c>.</summary>
    public string TooltipFill { get; init; } = "111827";

    /// <summary>The tooltip's text. Default: white.</summary>
    public string TooltipText { get; init; } = "FFFFFF";

    // XML 1.0 has no place for most control characters.
    internal static string XmlSafe(string text) =>
        text.Any(c => c < ' ' && c is not '\t' and not '\n' and not '\r') ? new string([.. text.Where(c => c >= ' ' || c is '\t' or '\n' or '\r')]) : text;
}
