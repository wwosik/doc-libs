using System.Globalization;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>How a slide's SVG is meant to be shown.</summary>
internal enum SvgTarget
{
    /// <summary>
    /// Inside the HTML deck: pictures as <c>&lt;use&gt;</c> of symbols written once per page (<see cref="SceneSvg.Symbols"/>),
    /// the SVG alternative of a picture preferred, font families as CSS lists with the original first.
    /// </summary>
    Html,

    /// <summary>
    /// For painting with Svg.Skia (E07-S03): standalone, raster pictures only, each run's family the face it was
    /// measured with (Liberation Sans for an estimate).
    /// </summary>
    Painting,
}

/// <summary>
/// Writes a <see cref="SlideScene"/> as SVG in points (EMU / 12,700): the background, then each item as a
/// <c>&lt;g data-id&gt;</c> (paragraphs as <c>&lt;g data-p&gt;</c>, for builds), paths with fills, gradients and
/// strokes, pictures clipped to their geometry, and text as <c>&lt;text&gt;</c> per line at its baseline with
/// <c>&lt;tspan&gt;</c>s per run, underline, strike-through and highlight drawn as rectangles. Only what Svg.Skia also
/// draws is used: no <c>foreignObject</c>, no CSS filters, no <c>textLength</c>.
/// </summary>
internal sealed class SceneSvg
{
    private const double EmuPerPoint = 12700;

    private readonly StringBuilder svg = new();
    private readonly StringBuilder defs = new();
    private readonly SvgTarget target;
    private readonly Func<SceneImage, string>? symbol;
    private readonly string prefix;
    private int next;
    private bool grey;

    private SceneSvg(SvgTarget target, Func<SceneImage, string>? symbol, string prefix)
    {
        this.target = target;
        this.symbol = symbol;
        this.prefix = prefix;
    }

    /// <summary>
    /// The slide's SVG. In <see cref="SvgTarget.Html"/>, <paramref name="symbol"/> gives the id of a picture's symbol;
    /// ids inside start with <paramref name="prefix"/>, so that several slides can share a page.
    /// </summary>
    public static string Write(DeckScene deck, SlideScene slide, SvgTarget target, Func<SceneImage, string>? symbol = null, string? prefix = null, string? attributes = null)
    {
        var writer = new SceneSvg(target, symbol, prefix ?? $"s{slide.Number}-");
        var (width, height) = (deck.Width / EmuPerPoint, deck.Height / EmuPerPoint);
        writer.Background(slide.Background, width, height);
        foreach (var item in slide.Items)
        {
            writer.Item(item);
        }

        var head = new StringBuilder();
        head.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(width)} {N(height)}\"");
        if (target == SvgTarget.Painting)
        {
            head.Append(CultureInfo.InvariantCulture, $" width=\"{N(width)}pt\" height=\"{N(height)}pt\"");
        }

        head.Append(attributes).Append('>');
        if (writer.grey)
        {
            writer.defs.Append(CultureInfo.InvariantCulture, $"<filter id=\"{writer.prefix}grey\"><feColorMatrix type=\"saturate\" values=\"0\"/></filter>");
        }

        if (writer.defs.Length > 0)
        {
            head.Append("<defs>").Append(writer.defs).Append("</defs>");
        }

        return head.Append(writer.svg).Append("</svg>").ToString();
    }

    /// <summary>
    /// The pictures of a page as <c>&lt;symbol&gt;</c>s in a hidden SVG: each once, stretched over a unit square, the
    /// SVG alternative where there is one.
    /// </summary>
    public static string Symbols(IEnumerable<(SceneImage Image, string Id)> images)
    {
        var text = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"0\" height=\"0\" style=\"position:absolute\" aria-hidden=\"true\"><defs>");
        foreach (var (image, id) in images)
        {
            text.Append(CultureInfo.InvariantCulture, $"<symbol id=\"{id}\" viewBox=\"0 0 1 1\" preserveAspectRatio=\"none\"><image width=\"1\" height=\"1\" preserveAspectRatio=\"none\" href=\"{DataUri(image)}\"/></symbol>");
        }

        return text.Append("</defs></svg>").ToString();
    }

    public static string DataUri(SceneImage image) => $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Data)}";

    private string Id(string kind) => $"{this.prefix}{kind}{++this.next}";

    private void Background(SceneFill? fill, double width, double height)
    {
        var path = new[] { new PathMove(0, 0), new PathLine(width * EmuPerPoint, 0), new PathLine(width * EmuPerPoint, height * EmuPerPoint), new PathLine(0, height * EmuPerPoint), (PathSegment)PathClose.Instance };
        this.Path(new ScenePath(path, fill ?? new SolidFill(Rgba.White), null), "bg");
    }

    private void Item(SceneItem item)
    {
        switch (item)
        {
            case SceneGroup group:
                this.svg.Append(CultureInfo.InvariantCulture, $"<g data-id=\"{group.Id}\">");
                foreach (var child in group.Items)
                {
                    this.Item(child);
                }

                this.svg.Append("</g>");
                break;
            case SceneShape shape:
                this.Shape(shape);
                break;
            case ScenePicture picture:
                this.Picture(picture);
                break;
        }
    }

    private void Shape(SceneShape shape)
    {
        if (shape.Link is { } link)
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<a href=\"{Escape(link)}\" target=\"_blank\" rel=\"noopener noreferrer\">");
        }

        this.svg.Append(CultureInfo.InvariantCulture, $"<g data-id=\"{shape.Id}\">");
        if (shape.Paths.Count > 0)
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<g{Transform(shape.Transform)}>");
            foreach (var path in shape.Paths)
            {
                this.Path(path, null);
            }

            this.svg.Append("</g>");
        }

        if (shape.Text is { } text)
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<g{Transform(shape.TextTransform)}>");
            foreach (var paragraph in text.Paragraphs.Where(p => p.Lines.Any(l => l.Runs.Count > 0)))
            {
                this.svg.Append(CultureInfo.InvariantCulture, $"<g data-p=\"{paragraph.Index}\">");
                foreach (var line in paragraph.Lines)
                {
                    this.Line(line);
                }

                this.svg.Append("</g>");
            }

            this.svg.Append("</g>");
        }

        this.svg.Append("</g>");
        if (shape.Link is not null)
        {
            this.svg.Append("</a>");
        }
    }

    private void Line(SceneTextLine line)
    {
        var runs = line.Runs.Where(r => r.Text.Length > 0).ToList();
        if (runs.Count == 0)
        {
            return;
        }

        // Highlights behind, then the text, then underline and strike-through.
        foreach (var run in runs.Where(r => r.Highlight is not null))
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{P(run.X)}\" y=\"{P(line.Baseline - run.Shift - (run.Size * 0.9))}\" width=\"{P(run.Width)}\" height=\"{P(run.Size * 1.15)}\"{Paint("fill", run.Highlight!.Value)}/>");
        }

        this.svg.Append("<text xml:space=\"preserve\">");
        foreach (var run in runs)
        {
            if (run.Link is not null && this.target == SvgTarget.Html)
            {
                this.svg.Append(CultureInfo.InvariantCulture, $"<a href=\"{Escape(run.Link)}\" target=\"_blank\" rel=\"noopener noreferrer\">");
            }

            this.svg.Append(CultureInfo.InvariantCulture, $"<tspan x=\"{P(run.X)}\" y=\"{P(line.Baseline - run.Shift)}\" font-family=\"{Escape(this.Family(run.Font))}\" font-size=\"{P(run.Size)}\"");
            if (run.Font.Bold)
            {
                this.svg.Append(" font-weight=\"bold\"");
            }

            if (run.Font.Italic)
            {
                this.svg.Append(" font-style=\"italic\"");
            }

            this.svg.Append(Paint("fill", run.Color)).Append('>').Append(Escape(run.Text)).Append("</tspan>");
            if (run.Link is not null && this.target == SvgTarget.Html)
            {
                this.svg.Append("</a>");
            }
        }

        this.svg.Append("</text>");
        foreach (var run in runs.Where(r => r.Underline || r.Strike))
        {
            var thickness = Math.Max(run.Size * 0.06, 6350);
            var trimmed = run.Text.TrimEnd();
            var width = trimmed.Length == run.Text.Length ? run.Width : FontLibrary.Width(run.Font, trimmed, run.Size);
            if (run.Underline)
            {
                this.svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{P(run.X)}\" y=\"{P(line.Baseline - run.Shift + (run.Size * 0.1))}\" width=\"{P(width)}\" height=\"{P(thickness)}\"{Paint("fill", run.Color)}/>");
            }

            if (run.Strike)
            {
                this.svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{P(run.X)}\" y=\"{P(line.Baseline - run.Shift - (run.Size * 0.3))}\" width=\"{P(width)}\" height=\"{P(thickness)}\"{Paint("fill", run.Color)}/>");
            }
        }
    }

    private string Family(SceneFont font)
    {
        if (this.target == SvgTarget.Painting)
        {
            return font.Face?.Family ?? "Liberation Sans";
        }

        var families = new List<string> { font.Family };
        if (font.Face is { Source: FaceSource.StandIn } standIn)
        {
            families.Add(standIn.Family);
        }
        else if (FontLibrary.StandInFamily(font.Family) is { } fallback)
        {
            families.Add(fallback);
        }

        var mono = font.Face?.Family == "Liberation Mono" || FontLibrary.StandInFamily(font.Family) == "Liberation Mono";
        return string.Join(", ", families.Distinct().Select(f => f.Contains(' ', StringComparison.Ordinal) || f.Any(c => !char.IsLetter(c)) ? $"'{f.Replace("'", string.Empty, StringComparison.Ordinal)}'" : f))
            + (mono ? ", monospace" : ", sans-serif");
    }

    private void Picture(ScenePicture picture)
    {
        if (picture.Link is { } link)
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<a href=\"{Escape(link)}\" target=\"_blank\" rel=\"noopener noreferrer\">");
        }

        this.svg.Append(CultureInfo.InvariantCulture, $"<g data-id=\"{picture.Id}\"{Transform(picture.Transform)}>");
        var cropped = picture.Crop != (0, 0, 0, 0);
        string? clip = null;
        if (picture.Clip is not null || cropped)
        {
            clip = this.Id("clip");
            var segments = picture.Clip ?? [new PathMove(0, 0), new PathLine(picture.Width, 0), new PathLine(picture.Width, picture.Height), new PathLine(0, picture.Height), PathClose.Instance];
            this.defs.Append(CultureInfo.InvariantCulture, $"<clipPath id=\"{clip}\"><path d=\"{Data(segments)}\"/></clipPath>");
            this.svg.Append(CultureInfo.InvariantCulture, $"<g clip-path=\"url(#{clip})\">");
        }

        // A crop shows a part of the picture over the whole box: the picture is drawn larger, offset and clipped.
        var (l, t, r, b) = picture.Crop;
        var w = picture.Width / Math.Max(1e-6, 1 - l - r);
        var h = picture.Height / Math.Max(1e-6, 1 - t - b);
        var (x, y) = (-l * w, -t * h);
        var filter = string.Empty;
        if (picture.Grey)
        {
            this.grey = true;
            filter = $" filter=\"url(#{this.prefix}grey)\"";
        }

        var image = this.target == SvgTarget.Html ? picture.Svg ?? picture.Image : picture.Image;
        if (this.symbol is not null && this.target == SvgTarget.Html)
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<use href=\"#{this.symbol(image)}\" x=\"{P(x)}\" y=\"{P(y)}\" width=\"{P(w)}\" height=\"{P(h)}\"{filter}/>");
        }
        else
        {
            this.svg.Append(CultureInfo.InvariantCulture, $"<image x=\"{P(x)}\" y=\"{P(y)}\" width=\"{P(w)}\" height=\"{P(h)}\" preserveAspectRatio=\"none\" href=\"{DataUri(image)}\"{filter}/>");
        }

        if (clip is not null)
        {
            this.svg.Append("</g>");
        }

        this.svg.Append("</g>");
        if (picture.Link is not null)
        {
            this.svg.Append("</a>");
        }
    }

    private void Path(ScenePath path, string? role)
    {
        var data = Data(path.Segments);
        var marker = role is null ? string.Empty : $" data-role=\"{role}\"";
        if (path.Fill is ImageFill imageFill)
        {
            // A picture fill: the picture stretched over the path's bounds, clipped to it.
            var clip = this.Id("clip");
            this.defs.Append(CultureInfo.InvariantCulture, $"<clipPath id=\"{clip}\"><path d=\"{data}\"/></clipPath>");
            var (minX, minY, maxX, maxY) = Bounds(path.Segments);
            var href = this.symbol is not null && this.target == SvgTarget.Html ? null : DataUri(imageFill.Image);
            this.svg.Append(CultureInfo.InvariantCulture, $"<g clip-path=\"url(#{clip})\"{marker}>");
            this.svg.Append(href is null
                ? $"<use href=\"#{this.symbol!(imageFill.Image)}\" x=\"{P(minX)}\" y=\"{P(minY)}\" width=\"{P(maxX - minX)}\" height=\"{P(maxY - minY)}\"/>"
                : $"<image x=\"{P(minX)}\" y=\"{P(minY)}\" width=\"{P(maxX - minX)}\" height=\"{P(maxY - minY)}\" preserveAspectRatio=\"none\" href=\"{href}\"/>");
            this.svg.Append("</g>");
            if (path.Stroke is not null)
            {
                this.Path(path with { Fill = null }, null);
            }

            return;
        }

        var fill = path.Fill switch
        {
            SolidFill { Color.A: 0 } or null => " fill=\"none\"",
            SolidFill solid => Paint("fill", solid.Color),
            GradientFill gradient => $" fill=\"url(#{this.Gradient(gradient)})\"",
            _ => " fill=\"none\"",
        };
        this.svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{data}\"{fill}{marker}");
        if (path.Stroke is { } stroke)
        {
            this.svg.Append(Paint("stroke", stroke.Color)).Append(CultureInfo.InvariantCulture, $" stroke-width=\"{P(stroke.Width)}\"");
            if (stroke.Dashes is { Count: > 0 } dashes)
            {
                this.svg.Append(" stroke-dasharray=\"").Append(string.Join(" ", dashes.Select(d => P(Math.Max(d, 0.01) * stroke.Width)))).Append('"');
            }

            if (stroke.Join != "miter")
            {
                this.svg.Append(" stroke-linejoin=\"").Append(stroke.Join).Append('"');
            }

            if (stroke.Cap != "butt")
            {
                this.svg.Append(" stroke-linecap=\"").Append(stroke.Cap).Append('"');
            }
        }

        this.svg.Append("/>");
        if (path.Stroke is { } line && (line.Head is not null || line.Tail is not null))
        {
            this.Arrows(path.Segments, line);
        }
    }

    // Arrow heads at the start (head) and end (tail) of an open path, as filled triangles or open arrows.
    private void Arrows(IReadOnlyList<PathSegment> segments, SceneStroke stroke)
    {
        var points = new List<(double X, double Y)>();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case PathMove m: points.Add((m.X, m.Y)); break;
                case PathLine l: points.Add((l.X, l.Y)); break;
                case PathCubic c: points.Add((c.X2, c.Y2)); points.Add((c.X, c.Y)); break;
            }
        }

        if (points.Count < 2)
        {
            return;
        }

        void Head(LineEnd end, (double X, double Y) tip, (double X, double Y) from)
        {
            static double Size(string size) => size switch { "sm" => 2, "lg" => 5, _ => 3 };
            var (dx, dy) = (tip.X - from.X, tip.Y - from.Y);
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length == 0)
            {
                return;
            }

            (dx, dy) = (dx / length, dy / length);
            var (along, across) = (Size(end.Length) * stroke.Width, Size(end.Width) * stroke.Width / 2);
            var (bx, by) = (tip.X - (dx * along), tip.Y - (dy * along));
            var left = (bx - (dy * across), by + (dx * across));
            var right = (bx + (dy * across), by - (dx * across));
            var data = $"M{P(left.Item1)} {P(left.Item2)}L{P(tip.X)} {P(tip.Y)}L{P(right.Item1)} {P(right.Item2)}";
            this.svg.Append(end.Type == "arrow"
                ? $"<path d=\"{data}\" fill=\"none\"{Paint("stroke", stroke.Color)} stroke-width=\"{P(stroke.Width)}\" stroke-linejoin=\"round\"/>"
                : $"<path d=\"{data}Z\"{Paint("fill", stroke.Color)}/>");
        }

        if (stroke.Head is { } head)
        {
            Head(head, points[0], points[1]);
        }

        if (stroke.Tail is { } tail)
        {
            Head(tail, points[^1], points[^2]);
        }
    }

    private string Gradient(GradientFill gradient)
    {
        var id = this.Id("grad");
        var stops = string.Concat(gradient.Stops.Select(s =>
            $"<stop offset=\"{N(s.Position)}\" stop-color=\"#{s.Color.Hex}\"{(s.Color.A < 1 ? $" stop-opacity=\"{N(s.Color.A)}\"" : string.Empty)}/>"));
        if (gradient.Radial)
        {
            this.defs.Append(CultureInfo.InvariantCulture, $"<radialGradient id=\"{id}\" cx=\"0.5\" cy=\"0.5\" r=\"0.71\">{stops}</radialGradient>");
        }
        else
        {
            var radians = gradient.Angle * Math.PI / 180;
            var (dx, dy) = (Math.Cos(radians) / 2, Math.Sin(radians) / 2);
            this.defs.Append(CultureInfo.InvariantCulture, $"<linearGradient id=\"{id}\" x1=\"{N(0.5 - dx)}\" y1=\"{N(0.5 - dy)}\" x2=\"{N(0.5 + dx)}\" y2=\"{N(0.5 + dy)}\">{stops}</linearGradient>");
        }

        return id;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<PathSegment> segments)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case PathMove m: xs.Add(m.X); ys.Add(m.Y); break;
                case PathLine l: xs.Add(l.X); ys.Add(l.Y); break;
                case PathCubic c: xs.AddRange([c.X1, c.X2, c.X]); ys.AddRange([c.Y1, c.Y2, c.Y]); break;
            }
        }

        return xs.Count == 0 ? (0, 0, 0, 0) : (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static string Data(IReadOnlyList<PathSegment> segments)
    {
        var data = new StringBuilder();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case PathMove m: data.Append('M').Append(P(m.X)).Append(' ').Append(P(m.Y)); break;
                case PathLine l: data.Append('L').Append(P(l.X)).Append(' ').Append(P(l.Y)); break;
                case PathCubic c: data.Append('C').Append(P(c.X1)).Append(' ').Append(P(c.Y1)).Append(' ').Append(P(c.X2)).Append(' ').Append(P(c.Y2)).Append(' ').Append(P(c.X)).Append(' ').Append(P(c.Y)); break;
                case PathClose: data.Append('Z'); break;
            }
        }

        return data.ToString();
    }

    private static string Transform(Matrix matrix) =>
        matrix.IsIdentity ? string.Empty
        : matrix is { B: 0, C: 0, A: 1, D: 1 } ? $" transform=\"translate({P(matrix.E)} {P(matrix.F)})\""
        : $" transform=\"matrix({N(matrix.A)} {N(matrix.B)} {N(matrix.C)} {N(matrix.D)} {P(matrix.E)} {P(matrix.F)})\"";

    private static string Paint(string property, Rgba color) =>
        color.A >= 1 ? $" {property}=\"#{color.Hex}\"" : $" {property}=\"#{color.Hex}\" {property}-opacity=\"{N(color.A)}\"";

    // EMU as points, two decimals.
    private static string P(double emu) => N(emu / EmuPerPoint);

    private static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Text for XML content and attributes: markup characters escaped, characters XML has no place for left out.</summary>
    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': escaped.Append("&amp;"); break;
                case '<': escaped.Append("&lt;"); break;
                case '>': escaped.Append("&gt;"); break;
                case '"': escaped.Append("&quot;"); break;
                case '\'': escaped.Append("&#39;"); break;
                case < ' ' and not ('\t' or '\n' or '\r'):
                case '￾' or '￿':
                    break;
                default: escaped.Append(c); break;
            }
        }

        return escaped.ToString();
    }
}
