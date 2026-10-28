using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Turns a <see cref="Drawing"/> into a group of editable shapes: PowerPoint's own rectangles, rounded rectangles
/// and ellipses where the drawing says so, custom geometry for every other outline, and a text box per line.
/// </summary>
internal static class DrawingWriter
{
    // A deck has no font metrics: text is estimated at half its size per character, like table rows.
    private const double CharacterWidth = 0.5;
    private const double MinLabelScale = 0.7;
    private const double PointsPerPixel = 0.75;

    /// <summary>The group, placed at (<paramref name="x"/>, <paramref name="y"/>) with <paramref name="emuPerPixel"/>.</summary>
    /// <remarks>
    /// <paramref name="font"/> is the family of the text (the brand's table family), <see langword="null"/> for the
    /// theme's body font; <paramref name="monoFont"/> the family of mono text.
    /// </remarks>
    public static P.GroupShape Group(Drawing drawing, long x, long y, double emuPerPixel, Func<uint> nextId, string? font, string monoFont)
    {
        long X(double px) => x + (long)Math.Round(px * emuPerPixel);
        long Y(double px) => y + (long)Math.Round(px * emuPerPixel);
        var width = Math.Max(1, (long)(drawing.Width * emuPerPixel));
        var height = Math.Max(1, (long)(drawing.Height * emuPerPixel));

        var id = nextId();
        var group = new P.GroupShape(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Diagram " + id },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup(
                new A.Offset { X = x, Y = y },
                new A.Extents { Cx = width, Cy = height },
                new A.ChildOffset { X = x, Y = y },
                new A.ChildExtents { Cx = width, Cy = height })));

        // Hotspots only carry hover text, which a slide cannot show.
        foreach (var item in drawing.Items.Where(item => item is not DrawingHotspot))
        {
            group.Append(item switch
            {
                DrawingShape shape => Shape(shape, nextId(), X, Y, emuPerPixel),
                DrawingText text => Text(text, nextId(), X, Y, emuPerPixel, font, monoFont),
                _ => throw new InvalidOperationException("Unknown drawing item."),
            });
        }

        return group;
    }

    private static P.Shape Shape(DrawingShape shape, uint id, Func<double, long> toX, Func<double, long> toY, double emuPerPixel)
    {
        var (bx, by, bw, bh) = shape.Path.Bounds;
        var left = toX(bx);
        var top = toY(by);
        var width = Math.Max(1, toX(bx + bw) - left);
        var height = Math.Max(1, toY(by + bh) - top);
        OpenXmlElement geometry = shape.Kind switch
        {
            DrawingShapeKind.Rectangle => new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
            DrawingShapeKind.Ellipse => new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Ellipse },
            DrawingShapeKind.RoundedRectangle => new A.PresetGeometry(new A.AdjustValueList(new A.ShapeGuide
            {
                Name = "adj",
                Formula = "val " + (long)Math.Clamp(shape.CornerRadius / Math.Max(Math.Min(bw, bh), 0.001) * 100000, 0, 50000),
            }))
            { Preset = A.ShapeTypeValues.RoundRectangle },
            _ => CustomGeometry(shape, left, top, width, height, toX, toY),
        };

        var properties = new P.ShapeProperties(
            new A.Transform2D(new A.Offset { X = left, Y = top }, new A.Extents { Cx = width, Cy = height }),
            geometry,
            shape.Fill is { } fill ? new A.SolidFill(Rgb(fill)) : new A.NoFill(),
            Outline(shape, emuPerPixel));
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = shape.Kind + " " + id },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            properties);
    }

    // Path coordinates are EMU relative to the shape, so the geometry stretches with it when a user resizes it.
    private static A.CustomGeometry CustomGeometry(DrawingShape shape, long left, long top, long width, long height, Func<double, long> toX, Func<double, long> toY)
    {
        A.Point Point(double px, double py) => new() { X = (toX(px) - left).ToString(System.Globalization.CultureInfo.InvariantCulture), Y = (toY(py) - top).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var path = new A.Path { Width = width, Height = height };
        if (shape.Fill is null)
        {
            path.Fill = A.PathFillModeValues.None;
        }

        foreach (var segment in shape.Path.Segments)
        {
            path.Append(segment switch
            {
                MoveTo m => new A.MoveTo(Point(m.X, m.Y)),
                LineTo l => new A.LineTo(Point(l.X, l.Y)),
                CubicTo c => new A.CubicBezierCurveTo(Point(c.X1, c.Y1), Point(c.X2, c.Y2), Point(c.X, c.Y)),
                _ => new A.CloseShapePath(),
            });
        }

        return new A.CustomGeometry(
            new A.AdjustValueList(),
            new A.ShapeGuideList(),
            new A.AdjustHandleList(),
            new A.ConnectionSiteList(),
            new A.Rectangle { Left = "l", Top = "t", Right = "r", Bottom = "b" },
            new A.PathList(path));
    }

    private static A.Outline Outline(DrawingShape shape, double emuPerPixel)
    {
        if (shape.Stroke is not { } stroke || shape.StrokeWidth <= 0)
        {
            return new A.Outline(new A.NoFill());
        }

        var outline = new A.Outline(new A.SolidFill(Rgb(stroke))) { Width = (int)Math.Max(1, Math.Round(shape.StrokeWidth * emuPerPixel)) };
        if (shape.Dash is { Count: > 0 } dash)
        {
            // Custom dashes are in percent of the line width.
            var custom = new A.CustomDash();
            for (var i = 0; i + 1 < dash.Count || i == 0; i += 2)
            {
                var gap = i + 1 < dash.Count ? dash[i + 1] : dash[i];
                custom.Append(new A.DashStop { DashLength = Percent(dash[i], shape.StrokeWidth), SpaceLength = Percent(gap, shape.StrokeWidth) });
            }

            outline.Append(custom);
        }

        if (shape.RoundJoins)
        {
            outline.CapType = A.LineCapValues.Round;
            outline.Append(new A.Round());
        }
        else
        {
            outline.Append(new A.Miter { Limit = 800000 });
        }

        return outline;
    }

    private static int Percent(double length, double width) => (int)Math.Round(length / width * 100000);

    // A text box without insets or wrapping, as wide as the estimated text, aligned by the anchor; its top is the
    // baseline less the ascent.
    private static P.Shape Text(DrawingText text, uint id, Func<double, long> toX, Func<double, long> toY, double emuPerPixel, string? font, string monoFont)
    {
        var size = text.Size;
        var estimate = text.Text.Length * size * CharacterWidth;
        if (text.MaxWidth is { } room && estimate > room)
        {
            size *= Math.Max(MinLabelScale, room / estimate);
            estimate = text.Text.Length * size * CharacterWidth;
        }

        var width = Math.Max(estimate, text.MaxWidth ?? 0);
        var left = text.Anchor switch
        {
            TextAnchor.Middle => text.X - (width / 2),
            TextAnchor.End => text.X - width,
            _ => text.X,
        };
        var top = text.Baseline - (size * 0.95);
        var box = new A.Transform2D(
            new A.Offset { X = toX(left), Y = toY(top) },
            new A.Extents { Cx = Math.Max(1, (long)(width * emuPerPixel)), Cy = Math.Max(1, (long)(size * 1.25 * emuPerPixel)) });

        // Points scale with the group: a pixel is 0.75 pt at an EMU per pixel of 9525.
        var points = size * PointsPerPixel * emuPerPixel / 9525;
        var paragraph = new A.Paragraph(new A.ParagraphProperties
        {
            Alignment = text.Anchor switch
            {
                TextAnchor.Middle => A.TextAlignmentTypeValues.Center,
                TextAnchor.End => A.TextAlignmentTypeValues.Right,
                _ => A.TextAlignmentTypeValues.Left,
            },
        });
        foreach (var span in text.Spans)
        {
            var properties = new A.RunProperties(new A.SolidFill(Rgb(span.Color ?? text.Color))) { Language = "en-US", FontSize = (int)Math.Max(100, Math.Round(points * 100)) };
            if (span.Bold)
            {
                properties.Bold = true;
            }

            if ((text.Mono ? monoFont : font) is { } typeface)
            {
                properties.Append(new A.LatinFont { Typeface = typeface });
            }

            paragraph.Append(new A.Run(properties, new A.Text(DeckWriter.XmlSafe(span.Text))));
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Text " + id },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(box, new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }, new A.NoFill()),
            new P.TextBody(
                new A.BodyProperties(new A.NoAutoFit())
                {
                    Wrap = A.TextWrappingValues.None,
                    LeftInset = 0,
                    TopInset = 0,
                    RightInset = 0,
                    BottomInset = 0,
                    Anchor = A.TextAnchoringTypeValues.Top,
                },
                new A.ListStyle(),
                paragraph));
    }

    private static A.RgbColorModelHex Rgb(string color) => new() { Val = color };
}
