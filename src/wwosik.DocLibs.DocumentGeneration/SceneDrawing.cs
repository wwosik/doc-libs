namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A scene as one still <see cref="Drawing"/> of 960 × 540 px (6 px a grid unit, so a pixel is a point of a 16:9
/// slide): what is shown after its last click, in the brand's colours. Documents draw scenes with it; their steps,
/// exits and effects have no effect there beyond leaving out what has gone by the end.
/// </summary>
internal static class SceneDrawing
{
    /// <summary>Pixels per grid unit.</summary>
    public const double Unit = 6;

    /// <summary>The padding of a label inside its shape, and its distance from a connector, in pixels.</summary>
    public const double Padding = 4;

    private const string White = "FFFFFF";
    private const double LineWidth = 1;
    private const double ConnectorWidth = 1.5;
    private const double HeadLength = 9;
    private const double HeadWidth = 7;
    private const double LineStep = 1.2;

    /// <summary>The scene's final state.</summary>
    public static Drawing Final(Scene scene, ResolvedBrand brand)
    {
        var items = new List<DrawingItem>();
        foreach (var shape in scene.Shapes.Where(s => s.Style.VisibleAtEnd))
        {
            var (x, y, w, h) = (shape.X * Unit, shape.Y * Unit, shape.W * Unit, shape.H * Unit);
            var (fill, stroke, ink) = Colours(shape, brand);
            if (shape.Kind != SceneKind.Text && (fill is not null || stroke is not null))
            {
                // PowerPoint's rounded rectangle: corners of a sixth of the shorter side.
                var radius = Math.Round(Math.Min(w, h) * 0.16667, 2);
                var (kind, path) = shape.Kind switch
                {
                    SceneKind.Ellipse => (DrawingShapeKind.Ellipse, Ellipse(x, y, w, h)),
                    SceneKind.Round => (DrawingShapeKind.RoundedRectangle, Rounded(x, y, w, h, radius)),
                    _ => (DrawingShapeKind.Rectangle, DrawingPath.Rectangle(x, y, w, h)),
                };
                items.Add(new DrawingShape(path, fill, stroke, stroke is null ? 0 : LineWidth)
                {
                    Kind = kind,
                    CornerRadius = shape.Kind == SceneKind.Round ? radius : 0,
                });
            }

            if (shape.Label is { } label)
            {
                items.AddRange(Label(label, shape.Style, x + (w / 2), y + (h / 2), w - (2 * Padding), ink, TextAnchor.Middle));
            }
        }

        foreach (var connector in scene.Connectors.Where(c => c.Style.VisibleAtEnd && scene.Shape(c.From).Style.VisibleAtEnd && scene.Shape(c.To).Style.VisibleAtEnd))
        {
            var (fromSide, toSide) = scene.Sides(connector);
            var (sx, sy) = Scale(Scene.Point(scene.Shape(connector.From), fromSide));
            var (ex, ey) = Scale(Scene.Point(scene.Shape(connector.To), toSide));
            var colour = LineColour(connector.Style.Role, brand);
            var (length, dx, dy) = Direction(sx, sy, ex, ey);
            var (lx, ly) = connector.Arrow && length > HeadLength ? (ex - (dx * HeadLength), ey - (dy * HeadLength)) : (ex, ey);
            items.Add(new DrawingShape(new DrawingPath([new MoveTo(R(sx), R(sy)), new LineTo(R(lx), R(ly))]), null, colour, ConnectorWidth));
            if (connector.Arrow && length > 0)
            {
                var (bx, by) = (ex - (dx * HeadLength), ey - (dy * HeadLength));
                var (nx, ny) = (-dy * HeadWidth / 2, dx * HeadWidth / 2);
                items.Add(new DrawingShape(
                    new DrawingPath([new MoveTo(R(ex), R(ey)), new LineTo(R(bx + nx), R(by + ny)), new LineTo(R(bx - nx), R(by - ny)), new ClosePath()]),
                    colour,
                    null,
                    0));
            }

            if (connector.Label is { } label)
            {
                var (mx, my) = ((sx + ex) / 2, (sy + ey) / 2);
                var lines = label.Split('\n');
                var step = connector.Style.Points * LineStep;
                var across = Math.Abs(ex - sx) >= Math.Abs(ey - sy);

                // Above the middle of a line across, to the right of one up or down.
                var (cx, cy, anchor) = across
                    ? (mx, my - Padding - (lines.Length * step / 2), TextAnchor.Middle)
                    : (mx + Padding, my, TextAnchor.Start);
                items.AddRange(Label(label, connector.Style, cx, cy, null, brand.Diagram.Text, anchor));
            }
        }

        return new Drawing(Scene.Columns * Unit, Scene.Rows * Unit, items);
    }

    /// <summary>The fill, line and text colour of a shape: a role fills it, with white or dark text by contrast.</summary>
    public static (string? Fill, string? Stroke, string Ink) Colours(SceneNode shape, ResolvedBrand brand)
    {
        var text = brand.Diagram.Text;
        if (shape.Kind == SceneKind.Text)
        {
            return (null, null, shape.Style.Role is SceneRole.Default or SceneRole.Outline ? text : RoleColour(shape.Style.Role, brand));
        }

        return shape.Style.Role switch
        {
            SceneRole.Default => (brand.TableBand, brand.Primary, text),
            SceneRole.Outline => (null, brand.Primary, text),
            SceneRole.Muted => (ResolvedBrand.Mix(brand.Labels.Neutral, 0.22), null, text),
            var role => (RoleColour(role, brand), null, Ink(RoleColour(role, brand), text)),
        };
    }

    /// <summary>White text on <paramref name="fill"/> if it stands out more than <paramref name="dark"/>.</summary>
    public static string Ink(string fill, string dark) =>
        QrDrawing.Contrast(fill, White) >= QrDrawing.Contrast(fill, dark) ? White : dark;

    // The strong colour of a role.
    private static string RoleColour(SceneRole role, ResolvedBrand brand) => role switch
    {
        SceneRole.Accent => brand.Link,
        SceneRole.Highlight => brand.Highlight,
        SceneRole.Muted => brand.Labels.Neutral,
        SceneRole.Success => brand.Labels.Success,
        SceneRole.Info => brand.Labels.Info,
        SceneRole.Warning => brand.Labels.Warning,
        SceneRole.Danger => brand.Labels.Danger,
        SceneRole.Neutral => brand.Labels.Neutral,
        _ => brand.Primary,
    };

    private static string LineColour(SceneRole role, ResolvedBrand brand) =>
        role is SceneRole.Default or SceneRole.Outline ? brand.Primary : RoleColour(role, brand);

    // One text per line of the label, the lines centred on (x, y).
    private static IEnumerable<DrawingText> Label(string label, SceneStyle style, double x, double y, double? maxWidth, string colour, TextAnchor anchor)
    {
        var lines = label.Split('\n');
        var size = style.Points;
        var step = size * LineStep;
        var baseline = y + (size * 0.35) - ((lines.Length - 1) * step / 2);
        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                yield return new DrawingText(R(x), R(baseline), line, size, colour, style.Bold)
                {
                    Anchor = anchor,
                    MaxWidth = maxWidth is { } width ? Math.Max(R(width), 1) : null,
                };
            }

            baseline += step;
        }
    }

    private static (double X, double Y) Scale((double X, double Y) point) => (point.X * Unit, point.Y * Unit);

    private static (double Length, double Dx, double Dy) Direction(double sx, double sy, double ex, double ey)
    {
        var length = Math.Sqrt(((ex - sx) * (ex - sx)) + ((ey - sy) * (ey - sy)));
        return length == 0 ? (0, 0, 0) : (length, (ex - sx) / length, (ey - sy) / length);
    }

    // An ellipse in the box, from four cubic Béziers.
    private static DrawingPath Ellipse(double x, double y, double w, double h)
    {
        var (rx, ry) = (w / 2, h / 2);
        var (cx, cy) = (x + rx, y + ry);
        var (kx, ky) = (rx * 0.5523, ry * 0.5523);
        return new DrawingPath(
        [
            new MoveTo(R(cx + rx), R(cy)),
            new CubicTo(R(cx + rx), R(cy + ky), R(cx + kx), R(cy + ry), R(cx), R(cy + ry)),
            new CubicTo(R(cx - kx), R(cy + ry), R(cx - rx), R(cy + ky), R(cx - rx), R(cy)),
            new CubicTo(R(cx - rx), R(cy - ky), R(cx - kx), R(cy - ry), R(cx), R(cy - ry)),
            new CubicTo(R(cx + kx), R(cy - ry), R(cx + rx), R(cy - ky), R(cx + rx), R(cy)),
            new ClosePath(),
        ]);
    }

    // A rectangle with rounded corners, each a quarter ellipse.
    private static DrawingPath Rounded(double x, double y, double w, double h, double r)
    {
        var k = r * 0.5523;
        var (right, bottom) = (x + w, y + h);
        return new DrawingPath(
        [
            new MoveTo(R(x + r), R(y)),
            new LineTo(R(right - r), R(y)),
            new CubicTo(R(right - r + k), R(y), R(right), R(y + r - k), R(right), R(y + r)),
            new LineTo(R(right), R(bottom - r)),
            new CubicTo(R(right), R(bottom - r + k), R(right - r + k), R(bottom), R(right - r), R(bottom)),
            new LineTo(R(x + r), R(bottom)),
            new CubicTo(R(x + r - k), R(bottom), R(x), R(bottom - r + k), R(x), R(bottom - r)),
            new LineTo(R(x), R(y + r)),
            new CubicTo(R(x), R(y + r - k), R(x + r - k), R(y), R(x + r), R(y)),
            new ClosePath(),
        ]);
    }

    private static double R(double value) => Math.Round(value, 2);
}
