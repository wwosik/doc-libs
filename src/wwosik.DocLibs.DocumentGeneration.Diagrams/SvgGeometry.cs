using System.Globalization;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration.Diagrams;

/// <summary>An affine transform, <c>matrix(a b c d e f)</c> in SVG's terms.</summary>
internal readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static Affine Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Whether it only moves and scales, so axis-aligned shapes stay axis-aligned.</summary>
    public bool IsAxisAligned => this.B == 0 && this.C == 0;

    /// <summary>How much it scales lengths, such as line widths.</summary>
    public double Scale => Math.Sqrt(Math.Abs((this.A * this.D) - (this.B * this.C)));

    public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Affine Rotate(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var (sin, cos) = Math.SinCos(radians);
        return new(cos, sin, -sin, cos, 0, 0);
    }

    public static Affine ScaleBy(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary><paramref name="inner"/> first, then this.</summary>
    public Affine Then(Affine inner) => new(
        (this.A * inner.A) + (this.C * inner.B),
        (this.B * inner.A) + (this.D * inner.B),
        (this.A * inner.C) + (this.C * inner.D),
        (this.B * inner.C) + (this.D * inner.D),
        (this.A * inner.E) + (this.C * inner.F) + this.E,
        (this.B * inner.E) + (this.D * inner.F) + this.F);

    public (double X, double Y) Apply(double x, double y) => ((this.A * x) + (this.C * y) + this.E, (this.B * x) + (this.D * y) + this.F);

    public PathSegment Apply(PathSegment segment)
    {
        switch (segment)
        {
            case MoveTo m:
                var (mx, my) = this.Apply(m.X, m.Y);
                return new MoveTo(mx, my);
            case LineTo l:
                var (lx, ly) = this.Apply(l.X, l.Y);
                return new LineTo(lx, ly);
            case CubicTo c:
                var (x1, y1) = this.Apply(c.X1, c.Y1);
                var (x2, y2) = this.Apply(c.X2, c.Y2);
                var (x, y) = this.Apply(c.X, c.Y);
                return new CubicTo(x1, y1, x2, y2, x, y);
            default:
                return segment;
        }
    }
}

/// <summary>SVG geometry as <see cref="PathSegment"/>s: path data, basic shapes and transforms.</summary>
internal static partial class SvgGeometry
{
    // The control-point distance of a cubic quarter circle, as a fraction of the radius.
    private const double Kappa = 0.5522847498307936;

    /// <summary>Parses path data; every command of SVG 1.1, relative ones too. Arcs and quadratic curves become cubic.</summary>
    /// <exception cref="InvalidDataException">The data is malformed.</exception>
    public static List<PathSegment> Path(string data)
    {
        var tokens = PathToken().Matches(data).Select(m => m.Value).ToList();
        var result = new List<PathSegment>();
        var index = 0;
        var command = '\0';
        double x = 0, y = 0, startX = 0, startY = 0;
        double? lastCubicX = null, lastCubicY = null, lastQuadX = null, lastQuadY = null;

        double Number()
        {
            if (index >= tokens.Count || char.IsLetter(tokens[index][0]))
            {
                throw new InvalidDataException($"The diagram's SVG has the path data '{data}', which is malformed.");
            }

            return double.Parse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        bool Flag() => Number() != 0;

        while (index < tokens.Count)
        {
            if (char.IsLetter(tokens[index][0]))
            {
                command = tokens[index++][0];
            }
            else if (command == '\0')
            {
                throw new InvalidDataException($"The diagram's SVG has the path data '{data}', which does not start with a command.");
            }

            var relative = char.IsLower(command);
            double dx = relative ? x : 0, dy = relative ? y : 0;
            var isCubic = false;
            var isQuad = false;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    (x, y) = (Number() + dx, Number() + dy);
                    (startX, startY) = (x, y);
                    result.Add(new MoveTo(x, y));
                    command = relative ? 'l' : 'L'; // further pairs are lines
                    break;
                case 'L':
                    (x, y) = (Number() + dx, Number() + dy);
                    result.Add(new LineTo(x, y));
                    break;
                case 'H':
                    x = Number() + dx;
                    result.Add(new LineTo(x, y));
                    break;
                case 'V':
                    y = Number() + dy;
                    result.Add(new LineTo(x, y));
                    break;
                case 'C':
                {
                    var (x1, y1, x2, y2) = (Number() + dx, Number() + dy, Number() + dx, Number() + dy);
                    (x, y) = (Number() + dx, Number() + dy);
                    result.Add(new CubicTo(x1, y1, x2, y2, x, y));
                    (lastCubicX, lastCubicY, isCubic) = (x2, y2, true);
                    break;
                }

                case 'S':
                {
                    var (x1, y1) = lastCubicX is { } cx ? ((2 * x) - cx, (2 * y) - lastCubicY!.Value) : (x, y);
                    var (x2, y2) = (Number() + dx, Number() + dy);
                    (x, y) = (Number() + dx, Number() + dy);
                    result.Add(new CubicTo(x1, y1, x2, y2, x, y));
                    (lastCubicX, lastCubicY, isCubic) = (x2, y2, true);
                    break;
                }

                case 'Q':
                {
                    var (qx, qy) = (Number() + dx, Number() + dy);
                    result.Add(Quadratic(x, y, qx, qy, x = Number() + dx, y = Number() + dy));
                    (lastQuadX, lastQuadY, isQuad) = (qx, qy, true);
                    break;
                }

                case 'T':
                {
                    var (qx, qy) = lastQuadX is { } px ? ((2 * x) - px, (2 * y) - lastQuadY!.Value) : (x, y);
                    result.Add(Quadratic(x, y, qx, qy, x = Number() + dx, y = Number() + dy));
                    (lastQuadX, lastQuadY, isQuad) = (qx, qy, true);
                    break;
                }

                case 'A':
                {
                    var (rx, ry, rotation, large, sweep) = (Number(), Number(), Number(), Flag(), Flag());
                    var (ex, ey) = (Number() + dx, Number() + dy);
                    result.AddRange(Arc(x, y, rx, ry, rotation, large, sweep, ex, ey));
                    (x, y) = (ex, ey);
                    break;
                }

                case 'Z':
                    result.Add(new ClosePath());
                    (x, y) = (startX, startY);
                    break;
                default:
                    throw new InvalidDataException($"The diagram's SVG has the path command '{command}', which cannot be drawn.");
            }

            if (!isCubic)
            {
                (lastCubicX, lastCubicY) = (null, null);
            }

            if (!isQuad)
            {
                (lastQuadX, lastQuadY) = (null, null);
            }
        }

        return result;
    }

    /// <summary>The points of a <c>polygon</c> or <c>polyline</c>.</summary>
    public static List<PathSegment> Points(string points, bool close)
    {
        var numbers = PathToken().Matches(points).Select(m => double.Parse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();
        var result = new List<PathSegment>();
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            result.Add(i == 0 ? new MoveTo(numbers[i], numbers[i + 1]) : new LineTo(numbers[i], numbers[i + 1]));
        }

        if (close && result.Count > 0)
        {
            result.Add(new ClosePath());
        }

        return result;
    }

    /// <summary>A rectangle, with elliptical corners when <paramref name="rx"/> or <paramref name="ry"/> is positive.</summary>
    public static List<PathSegment> Rectangle(double x, double y, double width, double height, double rx, double ry)
    {
        rx = Math.Min(rx, width / 2);
        ry = Math.Min(ry, height / 2);
        if (rx <= 0 || ry <= 0)
        {
            return [.. DrawingPath.Rectangle(x, y, width, height).Segments];
        }

        var (kx, ky) = (rx * Kappa, ry * Kappa);
        var (right, bottom) = (x + width, y + height);
        return
        [
            new MoveTo(x + rx, y),
            new LineTo(right - rx, y),
            new CubicTo(right - rx + kx, y, right, y + ry - ky, right, y + ry),
            new LineTo(right, bottom - ry),
            new CubicTo(right, bottom - ry + ky, right - rx + kx, bottom, right - rx, bottom),
            new LineTo(x + rx, bottom),
            new CubicTo(x + rx - kx, bottom, x, bottom - ry + ky, x, bottom - ry),
            new LineTo(x, y + ry),
            new CubicTo(x, y + ry - ky, x + rx - kx, y, x + rx, y),
            new ClosePath(),
        ];
    }

    /// <summary>An ellipse as four cubic quarter arcs, clockwise from the top.</summary>
    public static List<PathSegment> Ellipse(double cx, double cy, double rx, double ry)
    {
        var (kx, ky) = (rx * Kappa, ry * Kappa);
        return
        [
            new MoveTo(cx, cy - ry),
            new CubicTo(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy),
            new CubicTo(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry),
            new CubicTo(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy),
            new CubicTo(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry),
            new ClosePath(),
        ];
    }

    /// <summary>Parses a <c>transform</c> attribute: <c>matrix</c>, <c>translate</c>, <c>scale</c>, <c>rotate</c>.</summary>
    public static Affine Transform(string? value)
    {
        var result = Affine.Identity;
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        var matches = TransformCall().Matches(value);
        if (TransformCall().Replace(value, string.Empty).Trim(' ', ',', '\t', '\r', '\n').Length > 0)
        {
            throw new InvalidDataException($"The diagram's SVG has the transform '{value}', which cannot be drawn.");
        }

        foreach (Match match in matches)
        {
            var n = PathToken().Matches(match.Groups[2].Value).Select(m => double.Parse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();
            var step = (match.Groups[1].Value, n.Count) switch
            {
                ("matrix", 6) => new Affine(n[0], n[1], n[2], n[3], n[4], n[5]),
                ("translate", 1) => Affine.Translate(n[0], 0),
                ("translate", 2) => Affine.Translate(n[0], n[1]),
                ("scale", 1) => Affine.ScaleBy(n[0], n[0]),
                ("scale", 2) => Affine.ScaleBy(n[0], n[1]),
                ("rotate", 1) => Affine.Rotate(n[0]),
                ("rotate", 3) => Affine.Translate(n[1], n[2]).Then(Affine.Rotate(n[0])).Then(Affine.Translate(-n[1], -n[2])),
                _ => throw new InvalidDataException($"The diagram's SVG has the transform '{value}', which cannot be drawn."),
            };
            result = result.Then(step);
        }

        return result;
    }

    private static CubicTo Quadratic(double x0, double y0, double qx, double qy, double x, double y) => new(
        x0 + (2.0 / 3 * (qx - x0)), y0 + (2.0 / 3 * (qy - y0)),
        x + (2.0 / 3 * (qx - x)), y + (2.0 / 3 * (qy - y)),
        x, y);

    // SVG 1.1, appendix F.6.5 and F.6.6: endpoint to centre parameterization, then cubic pieces of at most 90°.
    private static IEnumerable<PathSegment> Arc(double x1, double y1, double rx, double ry, double rotation, bool large, bool sweep, double x2, double y2)
    {
        if ((x1 == x2 && y1 == y2) || rx == 0 || ry == 0)
        {
            return x1 == x2 && y1 == y2 ? [] : [new LineTo(x2, y2)];
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        var (sin, cos) = Math.SinCos(rotation * Math.PI / 180);
        var dx = (x1 - x2) / 2;
        var dy = (y1 - y2) / 2;
        var x1p = (cos * dx) + (sin * dy);
        var y1p = (-sin * dx) + (cos * dy);
        var lambda = (x1p * x1p / (rx * rx)) + (y1p * y1p / (ry * ry));
        if (lambda > 1)
        {
            rx *= Math.Sqrt(lambda);
            ry *= Math.Sqrt(lambda);
        }

        var numerator = (rx * rx * ry * ry) - (rx * rx * y1p * y1p) - (ry * ry * x1p * x1p);
        var denominator = (rx * rx * y1p * y1p) + (ry * ry * x1p * x1p);
        var factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (large == sweep ? -1 : 1);
        var cxp = factor * rx * y1p / ry;
        var cyp = -factor * ry * x1p / rx;
        var cx = (cos * cxp) - (sin * cyp) + ((x1 + x2) / 2);
        var cy = (sin * cxp) + (cos * cyp) + ((y1 + y2) / 2);

        static double Angle(double ux, double uy, double vx, double vy) => Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
        var start = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        var pieces = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
        var step = delta / pieces;
        var k = 4.0 / 3 * Math.Tan(step / 4);
        (double X, double Y) Point(double t) => (cx + (rx * Math.Cos(t) * cos) - (ry * Math.Sin(t) * sin), cy + (rx * Math.Cos(t) * sin) + (ry * Math.Sin(t) * cos));
        (double X, double Y) Derivative(double t) => ((-rx * Math.Sin(t) * cos) - (ry * Math.Cos(t) * sin), (-rx * Math.Sin(t) * sin) + (ry * Math.Cos(t) * cos));

        var result = new List<PathSegment>();
        for (var i = 0; i < pieces; i++)
        {
            var (t1, t2) = (start + (i * step), start + ((i + 1) * step));
            var (p1, d1, p2, d2) = (Point(t1), Derivative(t1), Point(t2), Derivative(t2));
            var end = i == pieces - 1 ? (X: x2, Y: y2) : p2;
            result.Add(new CubicTo(p1.X + (k * d1.X), p1.Y + (k * d1.Y), p2.X - (k * d2.X), p2.Y - (k * d2.Y), end.X, end.Y));
        }

        return result;
    }

    [GeneratedRegex(@"[A-Za-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex PathToken();

    [GeneratedRegex(@"(matrix|translate|scale|rotate)\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex TransformCall();
}
