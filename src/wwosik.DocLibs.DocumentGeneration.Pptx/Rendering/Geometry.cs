using System.Globalization;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>A path of a geometry before its fill and line are applied: whether it is filled and stroked.</summary>
internal sealed record GeometryPath(IReadOnlyList<PathSegment> Segments, bool Filled, bool Stroked);

/// <summary>
/// DrawingML shape geometry (ECMA-376 §20.1.9): adjust values and guides with the formula language, and paths with
/// <c>moveTo</c>, <c>lnTo</c>, <c>cubicBezTo</c>, <c>quadBezTo</c>, <c>arcTo</c> and <c>close</c>, evaluated for a
/// shape's width and height in EMU. Presets are custom geometry from <see cref="PresetShapes"/>.
/// </summary>
internal static class Geometry
{
    /// <summary>The paths of a <c>custGeom</c> or preset definition (elements matched by local name), or <see langword="null"/> if it is damaged.</summary>
    public static IReadOnlyList<GeometryPath>? Evaluate(XElement definition, double width, double height, XElement? adjustments = null)
    {
        try
        {
            var guides = Builtins(width, height);
            foreach (var guide in Children(definition, "avLst"))
            {
                Define(guides, guide);
            }

            // The shape's own adjust values override the definition's defaults.
            foreach (var guide in adjustments?.Elements().Where(e => e.Name.LocalName == "gd") ?? [])
            {
                Define(guides, guide);
            }

            foreach (var guide in Children(definition, "gdLst"))
            {
                Define(guides, guide);
            }

            var paths = new List<GeometryPath>();
            foreach (var path in Children(definition, "pathLst").Where(e => e.Name.LocalName == "path"))
            {
                paths.Add(Path(path, guides, width, height));
            }

            return paths;
        }
        catch (Exception e) when (e is FormatException or KeyNotFoundException or OverflowException)
        {
            return null;
        }
    }

    private static IEnumerable<XElement> Children(XElement definition, string list) =>
        definition.Elements().FirstOrDefault(e => e.Name.LocalName == list)?.Elements() ?? [];

    private static void Define(Dictionary<string, double> guides, XElement guide)
    {
        if (guide.Name.LocalName == "gd" && guide.Attribute("name")?.Value is { } name && guide.Attribute("fmla")?.Value is { } formula)
        {
            guides[name] = Formula(formula, guides);
        }
    }

    private static Dictionary<string, double> Builtins(double w, double h)
    {
        var ss = Math.Min(w, h);
        var guides = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["w"] = w, ["h"] = h, ["l"] = 0, ["t"] = 0, ["r"] = w, ["b"] = h, ["hc"] = w / 2, ["vc"] = h / 2,
            ["ss"] = ss, ["ls"] = Math.Max(w, h),
            ["cd2"] = 10800000, ["cd4"] = 5400000, ["cd8"] = 2700000, ["3cd4"] = 16200000, ["3cd8"] = 8100000,
            ["5cd8"] = 13500000, ["7cd8"] = 18900000,
        };
        foreach (var n in new[] { 2, 3, 4, 5, 6, 8, 10, 12, 32 })
        {
            guides["wd" + n] = w / n;
            guides["hd" + n] = h / n;
        }

        foreach (var n in new[] { 2, 4, 6, 8, 16, 32 })
        {
            guides["ssd" + n] = ss / n;
        }

        return guides;
    }

    /// <summary>One guide formula; angles in 60,000ths of a degree, as DrawingML has them.</summary>
    public static double Formula(string formula, IReadOnlyDictionary<string, double> guides)
    {
        var parts = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double Arg(int i) => Value(parts[i], guides);
        const double ToRadians = Math.PI / 180 / 60000;
        return parts[0] switch
        {
            "*/" => Arg(1) * Arg(2) / Arg(3),
            "+-" => Arg(1) + Arg(2) - Arg(3),
            "+/" => (Arg(1) + Arg(2)) / Arg(3),
            "?:" => Arg(1) > 0 ? Arg(2) : Arg(3),
            "abs" => Math.Abs(Arg(1)),
            "at2" => Math.Atan2(Arg(2), Arg(1)) / ToRadians,
            "cat2" => Arg(1) * Math.Cos(Math.Atan2(Arg(3), Arg(2))),
            "cos" => Arg(1) * Math.Cos(Arg(2) * ToRadians),
            "max" => Math.Max(Arg(1), Arg(2)),
            "min" => Math.Min(Arg(1), Arg(2)),
            "mod" => Math.Sqrt((Arg(1) * Arg(1)) + (Arg(2) * Arg(2)) + (Arg(3) * Arg(3))),
            "pin" => Arg(2) < Arg(1) ? Arg(1) : Arg(2) > Arg(3) ? Arg(3) : Arg(2),
            "sat2" => Arg(1) * Math.Sin(Math.Atan2(Arg(3), Arg(2))),
            "sin" => Arg(1) * Math.Sin(Arg(2) * ToRadians),
            "sqrt" => Math.Sqrt(Arg(1)),
            "tan" => Arg(1) * Math.Tan(Arg(2) * ToRadians),
            "val" => Arg(1),
            _ => throw new FormatException($"Unknown guide formula '{parts[0]}'."),
        };
    }

    private static double Value(string token, IReadOnlyDictionary<string, double> guides) =>
        guides.TryGetValue(token, out var value) ? value : double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static GeometryPath Path(XElement path, Dictionary<string, double> guides, double width, double height)
    {
        // A path with its own w and h has its coordinates in that space, stretched to the shape.
        var pathWidth = Attribute(path, "w") is { } pw && pw > 0 ? pw : width;
        var pathHeight = Attribute(path, "h") is { } ph && ph > 0 ? ph : height;
        var (sx, sy) = (width / pathWidth, height / pathHeight);
        var segments = new List<PathSegment>();
        var (x, y) = (0.0, 0.0);
        (double X, double Y) Point(XElement pt) => (Value(pt.Attribute("x")!.Value, guides) * sx, Value(pt.Attribute("y")!.Value, guides) * sy);
        foreach (var command in path.Elements())
        {
            var points = command.Elements().Where(e => e.Name.LocalName == "pt").Select(Point).ToList();
            switch (command.Name.LocalName)
            {
                case "moveTo":
                    (x, y) = points[0];
                    segments.Add(new PathMove(x, y));
                    break;
                case "lnTo":
                    (x, y) = points[0];
                    segments.Add(new PathLine(x, y));
                    break;
                case "cubicBezTo":
                    segments.Add(new PathCubic(points[0].X, points[0].Y, points[1].X, points[1].Y, points[2].X, points[2].Y));
                    (x, y) = points[2];
                    break;
                case "quadBezTo":
                    var (qx, qy) = points[0];
                    var (ex, ey) = points[1];
                    segments.Add(new PathCubic(x + (2.0 / 3 * (qx - x)), y + (2.0 / 3 * (qy - y)), ex + (2.0 / 3 * (qx - ex)), ey + (2.0 / 3 * (qy - ey)), ex, ey));
                    (x, y) = (ex, ey);
                    break;
                case "arcTo":
                    var wR = Value(command.Attribute("wR")!.Value, guides) * sx;
                    var hR = Value(command.Attribute("hR")!.Value, guides) * sy;
                    var start = Value(command.Attribute("stAng")!.Value, guides) / 60000;
                    var sweep = Value(command.Attribute("swAng")!.Value, guides) / 60000;
                    (x, y) = Arc(segments, x, y, wR, hR, start, sweep);
                    break;
                case "close":
                    segments.Add(PathClose.Instance);
                    break;
            }
        }

        return new GeometryPath(segments, path.Attribute("fill")?.Value != "none", path.Attribute("stroke")?.Value is not ("0" or "false"));
    }

    private static double? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value is { } value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    // An arc of the ellipse with radii wR and hR whose point at the visual angle `start` is the current point, swept by
    // `sweep` degrees (clockwise, y down), as cubic Béziers of at most 90° each. Returns the end point.
    private static (double X, double Y) Arc(List<PathSegment> segments, double x, double y, double wR, double hR, double start, double sweep)
    {
        if (wR <= 0 || hR <= 0 || sweep == 0)
        {
            return (x, y);
        }

        double Parametric(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return Math.Atan2(wR * Math.Sin(radians), hR * Math.Cos(radians));
        }

        var t0 = Parametric(start);
        var t1 = Parametric(start + sweep);

        // Keep the parametric sweep on the same side and of a similar size as the visual one.
        var delta = t1 - t0;
        if (sweep > 0)
        {
            while (delta <= 0)
            {
                delta += 2 * Math.PI;
            }

            while (delta > (sweep * Math.PI / 180) + Math.PI)
            {
                delta -= 2 * Math.PI;
            }
        }
        else
        {
            while (delta >= 0)
            {
                delta -= 2 * Math.PI;
            }

            while (delta < (sweep * Math.PI / 180) - Math.PI)
            {
                delta += 2 * Math.PI;
            }
        }

        if (Math.Abs(sweep) >= 360)
        {
            delta = Math.Sign(sweep) * 2 * Math.PI;
        }

        var cx = x - (wR * Math.Cos(t0));
        var cy = y - (hR * Math.Sin(t0));
        var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        var step = delta / pieces;
        var k = 4.0 / 3 * Math.Tan(step / 4);
        var t = t0;
        for (var i = 0; i < pieces; i++)
        {
            var (c0, s0, c1, s1) = (Math.Cos(t), Math.Sin(t), Math.Cos(t + step), Math.Sin(t + step));
            segments.Add(new PathCubic(
                cx + (wR * (c0 - (k * s0))), cy + (hR * (s0 + (k * c0))),
                cx + (wR * (c1 + (k * s1))), cy + (hR * (s1 - (k * c1))),
                cx + (wR * c1), cy + (hR * s1)));
            t += step;
        }

        return (cx + (wR * Math.Cos(t)), cy + (hR * Math.Sin(t)));
    }
}

/// <summary>
/// The preset shapes the reader draws, as their ECMA-376 definitions (<c>presetShapeDefinitions.xml</c>): first exactly
/// those the generator writes (<c>rect</c>, <c>roundRect</c>, <c>ellipse</c>, <c>wedgeRoundRectCallout</c>), then a few
/// that templates commonly use. A story that writes a new preset adds it here; any other is drawn as its bounding
/// rectangle and reported.
/// </summary>
internal static class PresetShapes
{
    private static readonly Dictionary<string, XElement> Definitions = new(StringComparer.Ordinal)
    {
        ["rect"] = Parse("<pathLst><path><moveTo><pt x='l' y='t'/></moveTo><lnTo><pt x='r' y='t'/></lnTo><lnTo><pt x='r' y='b'/></lnTo><lnTo><pt x='l' y='b'/></lnTo><close/></path></pathLst>"),
        ["roundRect"] = Parse("<avLst><gd name='adj' fmla='val 16667'/></avLst>"
            + "<gdLst><gd name='a' fmla='pin 0 adj 50000'/><gd name='x1' fmla='*/ ss a 100000'/><gd name='x2' fmla='+- r 0 x1'/><gd name='y2' fmla='+- b 0 x1'/></gdLst>"
            + "<pathLst><path><moveTo><pt x='l' y='x1'/></moveTo><arcTo wR='x1' hR='x1' stAng='cd2' swAng='cd4'/><lnTo><pt x='x2' y='t'/></lnTo>"
            + "<arcTo wR='x1' hR='x1' stAng='3cd4' swAng='cd4'/><lnTo><pt x='r' y='y2'/></lnTo><arcTo wR='x1' hR='x1' stAng='0' swAng='cd4'/>"
            + "<lnTo><pt x='x1' y='b'/></lnTo><arcTo wR='x1' hR='x1' stAng='cd4' swAng='cd4'/><close/></path></pathLst>"),
        ["ellipse"] = Parse("<pathLst><path><moveTo><pt x='l' y='vc'/></moveTo><arcTo wR='wd2' hR='hd2' stAng='cd2' swAng='cd4'/>"
            + "<arcTo wR='wd2' hR='hd2' stAng='3cd4' swAng='cd4'/><arcTo wR='wd2' hR='hd2' stAng='0' swAng='cd4'/>"
            + "<arcTo wR='wd2' hR='hd2' stAng='cd4' swAng='cd4'/><close/></path></pathLst>"),
        ["line"] = Parse("<pathLst><path fill='none'><moveTo><pt x='l' y='t'/></moveTo><lnTo><pt x='r' y='b'/></lnTo></path></pathLst>"),
        ["straightConnector1"] = Parse("<pathLst><path fill='none'><moveTo><pt x='l' y='t'/></moveTo><lnTo><pt x='r' y='b'/></lnTo></path></pathLst>"),
        ["wedgeRoundRectCallout"] = Parse("<avLst><gd name='adj1' fmla='val -20833'/><gd name='adj2' fmla='val 62500'/><gd name='adj3' fmla='val 16667'/></avLst>"
            + "<gdLst><gd name='dxPos' fmla='*/ w adj1 100000'/><gd name='dyPos' fmla='*/ h adj2 100000'/><gd name='xPos' fmla='+- hc dxPos 0'/>"
            + "<gd name='yPos' fmla='+- vc dyPos 0'/><gd name='dq' fmla='*/ dxPos h w'/><gd name='ady' fmla='abs dyPos'/><gd name='adq' fmla='abs dq'/>"
            + "<gd name='dz' fmla='+- ady 0 adq'/><gd name='xg1' fmla='?: dxPos 7 2'/><gd name='xg2' fmla='?: dxPos 10 5'/><gd name='x1' fmla='*/ w xg1 12'/>"
            + "<gd name='x2' fmla='*/ w xg2 12'/><gd name='yg1' fmla='?: dyPos 7 2'/><gd name='yg2' fmla='?: dyPos 10 5'/><gd name='y1' fmla='*/ h yg1 12'/>"
            + "<gd name='y2' fmla='*/ h yg2 12'/><gd name='t1' fmla='?: dxPos l xPos'/><gd name='xl' fmla='?: dz l t1'/><gd name='t2' fmla='?: dyPos x1 xPos'/>"
            + "<gd name='xt' fmla='?: dz t2 x1'/><gd name='t3' fmla='?: dxPos xPos r'/><gd name='xr' fmla='?: dz r t3'/><gd name='t4' fmla='?: dyPos xPos x1'/>"
            + "<gd name='xb' fmla='?: dz t4 x1'/><gd name='t5' fmla='?: dxPos y1 yPos'/><gd name='yl' fmla='?: dz y1 t5'/><gd name='t6' fmla='?: dyPos t yPos'/>"
            + "<gd name='yt' fmla='?: dz t6 t'/><gd name='t7' fmla='?: dxPos yPos y1'/><gd name='yr' fmla='?: dz y1 t7'/><gd name='t8' fmla='?: dyPos yPos b'/>"
            + "<gd name='yb' fmla='?: dz t8 b'/><gd name='u1' fmla='*/ ss adj3 100000'/><gd name='u2' fmla='+- r 0 u1'/><gd name='v2' fmla='+- b 0 u1'/></gdLst>"
            + "<pathLst><path><moveTo><pt x='l' y='u1'/></moveTo><arcTo wR='u1' hR='u1' stAng='cd2' swAng='cd4'/><lnTo><pt x='x1' y='t'/></lnTo>"
            + "<lnTo><pt x='xt' y='yt'/></lnTo><lnTo><pt x='x2' y='t'/></lnTo><lnTo><pt x='u2' y='t'/></lnTo><arcTo wR='u1' hR='u1' stAng='3cd4' swAng='cd4'/>"
            + "<lnTo><pt x='r' y='y1'/></lnTo><lnTo><pt x='xr' y='yr'/></lnTo><lnTo><pt x='r' y='y2'/></lnTo><lnTo><pt x='r' y='v2'/></lnTo>"
            + "<arcTo wR='u1' hR='u1' stAng='0' swAng='cd4'/><lnTo><pt x='x2' y='b'/></lnTo><lnTo><pt x='xb' y='yb'/></lnTo><lnTo><pt x='x1' y='b'/></lnTo>"
            + "<lnTo><pt x='u1' y='b'/></lnTo><arcTo wR='u1' hR='u1' stAng='cd4' swAng='cd4'/><lnTo><pt x='l' y='y2'/></lnTo><lnTo><pt x='xl' y='yl'/></lnTo>"
            + "<lnTo><pt x='l' y='y1'/></lnTo><close/></path></pathLst>"),
        ["triangle"] = Parse("<avLst><gd name='adj' fmla='val 50000'/></avLst><gdLst><gd name='a' fmla='pin 0 adj 100000'/><gd name='x1' fmla='*/ w a 100000'/></gdLst>"
            + "<pathLst><path><moveTo><pt x='l' y='b'/></moveTo><lnTo><pt x='x1' y='t'/></lnTo><lnTo><pt x='r' y='b'/></lnTo><close/></path></pathLst>"),
        ["rtTriangle"] = Parse("<pathLst><path><moveTo><pt x='l' y='b'/></moveTo><lnTo><pt x='l' y='t'/></lnTo><lnTo><pt x='r' y='b'/></lnTo><close/></path></pathLst>"),
        ["diamond"] = Parse("<pathLst><path><moveTo><pt x='l' y='vc'/></moveTo><lnTo><pt x='hc' y='t'/></lnTo><lnTo><pt x='r' y='vc'/></lnTo><lnTo><pt x='hc' y='b'/></lnTo><close/></path></pathLst>"),
        ["parallelogram"] = Parse("<avLst><gd name='adj' fmla='val 25000'/></avLst><gdLst><gd name='maxAdj' fmla='*/ 100000 w ss'/><gd name='a' fmla='pin 0 adj maxAdj'/>"
            + "<gd name='x2' fmla='*/ ss a 100000'/><gd name='x5' fmla='+- r 0 x2'/></gdLst>"
            + "<pathLst><path><moveTo><pt x='l' y='b'/></moveTo><lnTo><pt x='x2' y='t'/></lnTo><lnTo><pt x='r' y='t'/></lnTo><lnTo><pt x='x5' y='b'/></lnTo><close/></path></pathLst>"),
        ["homePlate"] = Parse("<avLst><gd name='adj' fmla='val 50000'/></avLst><gdLst><gd name='maxAdj' fmla='*/ 100000 w ss'/><gd name='a' fmla='pin 0 adj maxAdj'/>"
            + "<gd name='dx1' fmla='*/ ss a 100000'/><gd name='x1' fmla='+- r 0 dx1'/></gdLst>"
            + "<pathLst><path><moveTo><pt x='l' y='t'/></moveTo><lnTo><pt x='x1' y='t'/></lnTo><lnTo><pt x='r' y='vc'/></lnTo><lnTo><pt x='x1' y='b'/></lnTo><lnTo><pt x='l' y='b'/></lnTo><close/></path></pathLst>"),
        ["chevron"] = Parse("<avLst><gd name='adj' fmla='val 50000'/></avLst><gdLst><gd name='maxAdj' fmla='*/ 100000 w ss'/><gd name='a' fmla='pin 0 adj maxAdj'/>"
            + "<gd name='x1' fmla='*/ ss a 100000'/><gd name='x2' fmla='+- r 0 x1'/></gdLst>"
            + "<pathLst><path><moveTo><pt x='l' y='t'/></moveTo><lnTo><pt x='x2' y='t'/></lnTo><lnTo><pt x='r' y='vc'/></lnTo><lnTo><pt x='x2' y='b'/></lnTo><lnTo><pt x='l' y='b'/></lnTo><lnTo><pt x='x1' y='vc'/></lnTo><close/></path></pathLst>"),
    };

    /// <summary>The names of the presets the reader knows.</summary>
    public static IReadOnlyCollection<string> Names => Definitions.Keys;

    public static XElement? Definition(string name) => Definitions.GetValueOrDefault(name);

    private static XElement Parse(string xml) => XElement.Parse("<geometry>" + xml + "</geometry>");
}
