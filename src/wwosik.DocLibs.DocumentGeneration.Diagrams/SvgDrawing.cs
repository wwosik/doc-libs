using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams;

/// <summary>
/// Converts the SVG Mermaider writes into a <see cref="Drawing"/>: the basic shapes, paths, markers (arrowheads),
/// text with <c>tspan</c> lines and bold spans, groups with transforms, and presentation attributes with their CSS
/// resolved by <see cref="SvgStyle"/>. Anything else — an element, attribute value or CSS function it does not know —
/// throws <see cref="InvalidDataException"/> rather than being dropped.
/// </summary>
internal sealed partial class SvgDrawing
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    // Elements that hold no drawing of their own; markers and gradients are read where they are used.
    private static readonly HashSet<string> Skipped = ["style", "defs", "marker", "linearGradient", "radialGradient", "title", "desc", "metadata"];

    private readonly SvgStyle style;
    private readonly Dictionary<string, XElement> markers;
    private readonly List<DrawingItem> items = [];

    private SvgDrawing(SvgStyle style, Dictionary<string, XElement> markers)
    {
        this.style = style;
        this.markers = markers;
    }

    /// <exception cref="InvalidDataException">The SVG has something that cannot be drawn, or is not well-formed.</exception>
    public static Drawing Convert(string svg)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The diagram's SVG is not well-formed: " + ex.Message, ex);
        }

        var root = document.Root;
        if (root is null || root.Name != Svg + "svg")
        {
            throw new InvalidDataException("The diagram's SVG has no <svg> root.");
        }

        var style = SvgStyle.Read(root.Attribute("style")?.Value, string.Concat(root.Descendants(Svg + "style").Select(e => e.Value)));
        foreach (var gradient in root.Descendants().Where(e => e.Name == Svg + "linearGradient" || e.Name == Svg + "radialGradient"))
        {
            if (gradient.Attribute("id")?.Value is { } id && gradient.Elements(Svg + "stop").FirstOrDefault() is { } stop)
            {
                style.Gradients[id] = Declared(stop, "stop-color") ?? "black";
            }
        }

        var markers = root.Descendants(Svg + "marker")
            .Where(m => m.Attribute("id") is not null)
            .GroupBy(m => m.Attribute("id")!.Value)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var converter = new SvgDrawing(style, markers);

        var viewBox = root.Attribute("viewBox")?.Value is { } box ? Numbers(box) : null;
        var width = root.Attribute("width")?.Value is { } w ? style.Length(w) : viewBox?[2] ?? throw new InvalidDataException("The diagram's SVG has no width.");
        var height = root.Attribute("height")?.Value is { } h ? style.Length(h) : viewBox?[3] ?? throw new InvalidDataException("The diagram's SVG has no height.");
        var view = viewBox is [var minX, var minY, var vbWidth, var vbHeight] && vbWidth > 0 && vbHeight > 0
            ? Affine.ScaleBy(width / vbWidth, height / vbHeight).Then(Affine.Translate(-minX, -minY))
            : Affine.Identity;

        converter.Children(root, Paint.Initial, view, null);
        return new Drawing(width, height, converter.items);
    }

    private void Children(XElement parent, Paint inherited, Affine transform, double? room)
    {
        foreach (var element in parent.Elements())
        {
            var name = element.Name.LocalName;
            if (element.Name.Namespace != Svg)
            {
                throw Unsupported($"the element <{element.Name}>");
            }

            if (Skipped.Contains(name) || Declared(element, "display") == "none" || Declared(element, "visibility") is "hidden" or "collapse")
            {
                continue;
            }

            var paint = inherited.With(element, this.style);
            var local = transform.Then(SvgGeometry.Transform(element.Attribute("transform")?.Value));
            switch (name)
            {
                case "g":
                    this.Children(element, paint, local, IsLabelGroup(element) ? Room(element, local) : room);
                    break;
                case "rect":
                {
                    var (x, y, w, h) = (this.Number(element, "x"), this.Number(element, "y"), this.Number(element, "width"), this.Number(element, "height"));
                    var rx = element.Attribute("rx") is not null ? this.Number(element, "rx") : element.Attribute("ry") is not null ? this.Number(element, "ry") : 0;
                    var ry = element.Attribute("ry") is not null ? this.Number(element, "ry") : rx;
                    if (w > 0 && h > 0)
                    {
                        var kind = rx <= 0 || ry <= 0 ? DrawingShapeKind.Rectangle : rx == ry ? DrawingShapeKind.RoundedRectangle : DrawingShapeKind.Path;
                        this.Shape(paint, local, SvgGeometry.Rectangle(x, y, w, h, rx, ry), kind, Math.Min(rx, Math.Min(w, h) / 2));
                    }

                    break;
                }

                case "circle":
                {
                    var r = this.Number(element, "r");
                    this.Shape(paint, local, SvgGeometry.Ellipse(this.Number(element, "cx"), this.Number(element, "cy"), r, r), DrawingShapeKind.Ellipse);
                    break;
                }

                case "ellipse":
                    this.Shape(paint, local, SvgGeometry.Ellipse(this.Number(element, "cx"), this.Number(element, "cy"), this.Number(element, "rx"), this.Number(element, "ry")), DrawingShapeKind.Ellipse);
                    break;
                case "line":
                    this.Shape(element, paint with { Fill = null }, local, [new MoveTo(this.Number(element, "x1"), this.Number(element, "y1")), new LineTo(this.Number(element, "x2"), this.Number(element, "y2"))]);
                    break;
                case "polyline":
                    this.Shape(element, paint, local, SvgGeometry.Points(element.Attribute("points")?.Value ?? string.Empty, close: false));
                    break;
                case "polygon":
                    this.Shape(element, paint, local, SvgGeometry.Points(element.Attribute("points")?.Value ?? string.Empty, close: true));
                    break;
                case "path":
                    this.Shape(element, paint, local, SvgGeometry.Path(element.Attribute("d")?.Value ?? string.Empty));
                    break;
                case "text":
                    this.Text(element, paint, local, room);
                    break;
                default:
                    throw Unsupported($"the element <{name}>");
            }
        }
    }

    // A shape that may carry markers.
    private void Shape(XElement element, Paint paint, Affine transform, List<PathSegment> path)
    {
        if (Declared(element, "marker-mid") is { } mid && mid != "none")
        {
            throw Unsupported("marker-mid");
        }

        this.Shape(paint, transform, path, DrawingShapeKind.Path);
        this.Marker(element, "marker-start", path, paint, transform);
        this.Marker(element, "marker-end", path, paint, transform);
    }

    private void Shape(Paint paint, Affine transform, List<PathSegment> path, DrawingShapeKind kind, double radius = 0)
    {
        paint = paint.Resolved();
        var stroke = paint.StrokeWidth > 0 ? paint.Stroke : null;
        if (path.Count == 0 || (paint.Fill is null && stroke is null))
        {
            return;
        }

        this.items.Add(new DrawingShape(new DrawingPath([.. path.Select(transform.Apply)]), paint.Fill, stroke, stroke is null ? 0 : paint.StrokeWidth * transform.Scale)
        {
            Dash = stroke is null ? null : paint.Dash?.Select(d => d * transform.Scale).ToList(),
            Kind = transform.IsAxisAligned ? kind : DrawingShapeKind.Path,
            CornerRadius = kind == DrawingShapeKind.RoundedRectangle && transform.IsAxisAligned ? radius * transform.Scale : 0,
            RoundJoins = paint.RoundJoins,
        });
    }

    // The marker's content placed at the path's start or end, turned along the path for orient="auto".
    private void Marker(XElement element, string attribute, List<PathSegment> path, Paint paint, Affine transform)
    {
        if (Declared(element, attribute) is not { } value || value == "none")
        {
            return;
        }

        var match = UrlPattern().Match(value);
        if (!match.Success || !this.markers.TryGetValue(match.Groups[1].Value, out var marker))
        {
            throw Unsupported($"the {attribute} '{value}'");
        }

        if (marker.Attribute("viewBox") is not null)
        {
            throw Unsupported("a marker with a viewBox");
        }

        var atStart = attribute == "marker-start";
        if ((atStart ? Start(path) : End(path)) is not var ((x, y), (dx, dy)))
        {
            return;
        }

        var direction = Math.Atan2(dy, dx) * 180 / Math.PI;
        var orient = marker.Attribute("orient")?.Value ?? "0";
        var angle = orient switch
        {
            "auto" => direction,
            "auto-start-reverse" => atStart ? direction + 180 : direction,
            _ => double.TryParse(orient.Replace("deg", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees)
                ? degrees
                : throw Unsupported($"the marker orient '{orient}'"),
        };
        var units = marker.Attribute("markerUnits")?.Value == "userSpaceOnUse" ? 1 : paint.StrokeWidth;
        var placed = transform
            .Then(Affine.Translate(x, y))
            .Then(Affine.Rotate(angle))
            .Then(Affine.ScaleBy(units, units))
            .Then(Affine.Translate(-this.Number(marker, "refX"), -this.Number(marker, "refY")));
        this.Children(marker, Paint.Initial.With(marker, this.style), placed, null);
    }

    // Each line — the text, and every tspan positioned with x, y or dy — becomes a DrawingText; nested tspans with
    // a weight become bold spans. Whitespace collapses as SVG collapses it.
    private void Text(XElement element, Paint paint, Affine transform, double? room)
    {
        if (!transform.IsAxisAligned)
        {
            throw Unsupported("rotated text");
        }

        if (paint.Resolved().Fill is not { } color)
        {
            return;
        }

        var mono = (element.Attribute("class")?.Value ?? string.Empty).Split(' ').Contains("mono");
        var lines = new List<(double X, double Y, List<DrawingSpan> Spans)>();
        var x = this.Number(element, "x");
        var y = this.Number(element, "y") + this.Number(element, "dy");
        var spans = new List<DrawingSpan>();

        void Flush()
        {
            if (spans.Count > 0)
            {
                lines.Add((x, y, spans));
                spans = [];
            }
        }

        void Walk(XElement container, bool bold)
        {
            foreach (var node in container.Nodes())
            {
                if (node is XText text)
                {
                    spans.Add(new DrawingSpan(text.Value, bold));
                }
                else if (node is XElement { Name.LocalName: "tspan" } tspan && tspan.Name.Namespace == Svg)
                {
                    var positioned = tspan.Attribute("x") is not null || tspan.Attribute("y") is not null || tspan.Attribute("dy") is not null;
                    if (positioned)
                    {
                        Flush();
                        x = tspan.Attribute("x") is not null ? this.Number(tspan, "x") : x;
                        y = (tspan.Attribute("y") is not null ? this.Number(tspan, "y") : y) + this.Number(tspan, "dy");
                    }

                    Walk(tspan, Declared(tspan, "font-weight") is { } weight ? IsBold(weight) : bold);
                    if (positioned)
                    {
                        Flush();
                    }
                }
                else if (node is XElement other)
                {
                    throw Unsupported($"the element <{other.Name.LocalName}> in text");
                }
            }
        }

        Walk(element, paint.Bold);
        Flush();
        foreach (var (lineX, lineY, lineSpans) in lines)
        {
            if (Collapse(lineSpans) is not { Count: > 0 } collapsed)
            {
                continue;
            }

            var (px, py) = transform.Apply(lineX, lineY);
            this.items.Add(new DrawingText(px, py, collapsed, paint.FontSize * transform.Scale, color)
            {
                Anchor = paint.Anchor,
                MaxWidth = room,
                Mono = mono,
            });
        }
    }

    // Whitespace runs become one space, the line is trimmed, and neighbouring spans of one weight are merged.
    private static List<DrawingSpan> Collapse(List<DrawingSpan> spans)
    {
        var result = new List<DrawingSpan>();
        var previousSpace = true;
        foreach (var span in spans)
        {
            var text = new StringBuilder();
            foreach (var character in span.Text)
            {
                var space = char.IsWhiteSpace(character);
                if (!(space && previousSpace))
                {
                    text.Append(space ? ' ' : character);
                }

                previousSpace = space;
            }

            if (text.Length == 0)
            {
                continue;
            }

            if (result.Count > 0 && result[^1].Bold == span.Bold)
            {
                result[^1] = result[^1] with { Text = result[^1].Text + text };
            }
            else
            {
                result.Add(new DrawingSpan(text.ToString(), span.Bold));
            }
        }

        if (result.Count > 0)
        {
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
            if (result[^1].Text.Length == 0)
            {
                result.RemoveAt(result.Count - 1);
            }
        }

        return result;
    }

    // The point and direction where a path starts.
    private static ((double X, double Y) Point, (double X, double Y) Direction)? Start(List<PathSegment> path)
    {
        if (path.FirstOrDefault() is not MoveTo start)
        {
            return null;
        }

        foreach (var segment in path.Skip(1))
        {
            foreach (var (px, py) in segment.Points)
            {
                if (px != start.X || py != start.Y)
                {
                    return ((start.X, start.Y), (px - start.X, py - start.Y));
                }
            }

            if (segment is MoveTo)
            {
                break;
            }
        }

        return null;
    }

    // The point and direction where a path ends.
    private static ((double X, double Y) Point, (double X, double Y) Direction)? End(List<PathSegment> path)
    {
        (double X, double Y) current = (0, 0), subpathStart = (0, 0);
        ((double X, double Y) Point, (double X, double Y) Direction)? result = null;
        foreach (var segment in path)
        {
            var points = segment is ClosePath ? [subpathStart] : segment.Points.ToList();
            if (segment is MoveTo)
            {
                current = subpathStart = points[0];
                continue;
            }

            // Along the last control point (or the segment's start) that differs from the end.
            var end = points[^1];
            var from = points.Take(points.Count - 1).Prepend(current).LastOrDefault(p => p != end, end);
            if (from != end)
            {
                result = (end, (end.X - from.X, end.Y - from.Y));
            }

            current = end;
        }

        return result;
    }

    private static bool IsLabelGroup(XElement group) =>
        (group.Attribute("class")?.Value ?? string.Empty).Split(' ').Any(c => c is "node" or "edge-label");

    // The width a node's or edge label's text has inside its shape: the first shape of the group, less padding.
    private double? Room(XElement group, Affine transform)
    {
        var shape = group.Elements().FirstOrDefault(e => e.Name.LocalName is "rect" or "circle" or "ellipse" or "polygon" or "path");
        double? width = shape?.Name.LocalName switch
        {
            "rect" => this.Number(shape, "width") - 16,
            "circle" => (2 * this.Number(shape, "r")) - 16,
            "ellipse" => (2 * this.Number(shape, "rx")) - 16,
            "polygon" => SvgGeometry.Points(shape.Attribute("points")?.Value ?? string.Empty, close: false) is var points && points.Count == 4
                ? new DrawingPath(points).Bounds.Width * 0.7 // a diamond: its width at the height of one line of text
                : new DrawingPath(points).Bounds.Width - 24,
            "path" => new DrawingPath(SvgGeometry.Path(shape.Attribute("d")?.Value ?? string.Empty)).Bounds.Width - 16,
            _ => null,
        };
        return width > 0 ? width * transform.Scale : null;
    }

    private double Number(XElement element, string name) =>
        element.Attribute(name)?.Value is { } value ? this.style.Length(value) : 0;

    // A presentation property: from the element's style attribute, else its attribute.
    private static string? Declared(XElement element, string name)
    {
        if (element.Attribute("style")?.Value is { } inline)
        {
            foreach (var declaration in inline.Split(';'))
            {
                var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && declaration[..colon].Trim() == name)
                {
                    return declaration[(colon + 1)..].Trim();
                }
            }
        }

        return element.Attribute(name)?.Value.Trim();
    }

    private static bool IsBold(string weight) =>
        weight is "bold" or "bolder" || (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 600);

    private static double[] Numbers(string value) =>
        [.. value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(n => double.Parse(n, NumberStyles.Float, CultureInfo.InvariantCulture))];

    private static InvalidDataException Unsupported(string what) => new($"The diagram's SVG has {what}, which cannot be drawn.");

    [GeneratedRegex(@"^url\(\s*#([^)\s]+)\s*\)$", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>The inherited presentation properties, resolved; colours are <see langword="null"/> for <c>none</c>.</summary>
    private sealed record Paint(
        string? Fill,
        string? Stroke,
        double StrokeWidth,
        IReadOnlyList<double>? Dash,
        bool RoundJoins,
        double FontSize,
        bool Bold,
        TextAnchor Anchor,
        double FillOpacity,
        double StrokeOpacity)
    {
        /// <summary>SVG's initial values: black fill, no stroke, 16 px text.</summary>
        public static Paint Initial { get; } = new("000000", null, 1, null, false, 16, false, TextAnchor.Start, 1, 1);

        public Paint With(XElement element, SvgStyle style)
        {
            var paint = this;
            var opacity = Declared(element, "opacity") is { } o ? Opacity(o) : 1;
            if (Declared(element, "fill-opacity") is { } fillOpacity)
            {
                paint = paint with { FillOpacity = Opacity(fillOpacity) };
            }

            if (Declared(element, "stroke-opacity") is { } strokeOpacity)
            {
                paint = paint with { StrokeOpacity = Opacity(strokeOpacity) };
            }

            if (Declared(element, "fill") is { } fill)
            {
                paint = paint with { Fill = style.Color(fill) };
            }

            if (Declared(element, "stroke") is { } stroke)
            {
                paint = paint with { Stroke = style.Color(stroke) };
            }

            if (Declared(element, "stroke-width") is { } width)
            {
                paint = paint with { StrokeWidth = style.Length(width) };
            }

            if (Declared(element, "stroke-dasharray") is { } dash)
            {
                var lengths = dash == "none" ? [] : dash.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(style.Length).ToList();
                paint = paint with { Dash = lengths.Count == 0 || lengths.All(l => l == 0) ? null : lengths.Count % 2 == 1 ? [.. lengths, .. lengths] : lengths };
            }

            if (Declared(element, "stroke-linejoin") is { } join)
            {
                paint = paint with { RoundJoins = join == "round" };
            }

            if (Declared(element, "font-size") is { } size)
            {
                paint = paint with { FontSize = style.Length(size) };
            }

            if (Declared(element, "font-weight") is { } weight)
            {
                paint = paint with { Bold = IsBold(weight) };
            }

            if (Declared(element, "text-anchor") is { } anchor)
            {
                paint = paint with
                {
                    Anchor = anchor switch
                    {
                        "middle" => TextAnchor.Middle,
                        "end" => TextAnchor.End,
                        _ => TextAnchor.Start,
                    },
                };
            }

            // Drawings have no transparency: a translucent colour is mixed with the white background instead.
            return opacity == 1 ? paint : paint with { FillOpacity = paint.FillOpacity * opacity, StrokeOpacity = paint.StrokeOpacity * opacity };
        }

        public Paint Resolved() => this with
        {
            Fill = OnWhite(this.Fill, this.FillOpacity),
            Stroke = OnWhite(this.Stroke, this.StrokeOpacity),
            FillOpacity = 1,
            StrokeOpacity = 1,
        };

        private static double Opacity(string value) =>
            Math.Clamp(
                value.EndsWith('%')
                    ? double.Parse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture) / 100
                    : double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
                0,
                1);

        private static string? OnWhite(string? color, double opacity)
        {
            if (color is null || opacity >= 1)
            {
                return opacity <= 0 ? null : color;
            }

            if (opacity <= 0)
            {
                return null;
            }

            int Channel(int index) => (int)Math.Round(255 - ((255 - int.Parse(color.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)) * opacity));
            return string.Concat(Enumerable.Range(0, 3).Select(i => Channel(i).ToString("X2", CultureInfo.InvariantCulture)));
        }
    }
}
