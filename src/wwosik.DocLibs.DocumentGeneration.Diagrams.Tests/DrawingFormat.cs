using System.Globalization;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

/// <summary>A drawing as text, one item per line, for golden files that diff well.</summary>
internal static class DrawingFormat
{
    public static string Write(Drawing drawing)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"drawing {N(drawing.Width)} x {N(drawing.Height)}\n");
        foreach (var item in drawing.Items)
        {
            text.Append(item switch
            {
                DrawingShape shape => Shape(shape),
                DrawingText label => Text(label),
                _ => throw new InvalidOperationException(),
            }).Append('\n');
        }

        return text.ToString();
    }

    private static string Shape(DrawingShape shape)
    {
        var line = new StringBuilder(shape.Kind switch
        {
            DrawingShapeKind.Rectangle => "rect",
            DrawingShapeKind.RoundedRectangle => "roundrect",
            DrawingShapeKind.Ellipse => "ellipse",
            _ => "path",
        });
        line.Append(" fill=").Append(shape.Fill ?? "none").Append(" stroke=").Append(shape.Stroke ?? "none");
        if (shape.Stroke is not null)
        {
            line.Append(" w=").Append(N(shape.StrokeWidth));
        }

        if (shape.Dash is { } dash)
        {
            line.Append(" dash=").Append(string.Join(',', dash.Select(N)));
        }

        if (shape.RoundJoins)
        {
            line.Append(" round");
        }

        if (shape.Kind != DrawingShapeKind.Path)
        {
            var (x, y, w, h) = shape.Path.Bounds;
            line.Append(" at ").Append(N(x)).Append(' ').Append(N(y)).Append(" size ").Append(N(w)).Append(' ').Append(N(h));
            if (shape.Kind == DrawingShapeKind.RoundedRectangle)
            {
                line.Append(" r=").Append(N(shape.CornerRadius));
            }

            return line.ToString();
        }

        line.Append(" :");
        foreach (var segment in shape.Path.Segments)
        {
            line.Append(segment switch
            {
                MoveTo m => $" M {N(m.X)} {N(m.Y)}",
                LineTo l => $" L {N(l.X)} {N(l.Y)}",
                CubicTo c => $" C {N(c.X1)} {N(c.Y1)} {N(c.X2)} {N(c.Y2)} {N(c.X)} {N(c.Y)}",
                _ => " Z",
            });
        }

        return line.ToString();
    }

    private static string Text(DrawingText text)
    {
        var line = new StringBuilder("text ");
        line.Append(N(text.X)).Append(' ').Append(N(text.Baseline)).Append(' ').Append(text.Anchor.ToString().ToLowerInvariant())
            .Append(" size=").Append(N(text.Size)).Append(" color=").Append(text.Color);
        if (text.MaxWidth is { } max)
        {
            line.Append(" max=").Append(N(max));
        }

        if (text.Mono)
        {
            line.Append(" mono");
        }

        line.Append(" :");
        foreach (var span in text.Spans)
        {
            line.Append(' ').Append(span.Bold ? "**" : string.Empty).Append('"').Append(span.Text).Append('"').Append(span.Bold ? "**" : string.Empty);
        }

        return line.ToString();
    }

    private static string N(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
