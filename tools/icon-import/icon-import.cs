#:property PublishAot=false

using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

// icon-import: turns the outline icons of Tabler Icons (MIT) into the icon resource of wwosik.DocLibs.DocumentGeneration.
//
//   ./run.sh icon-import                              downloads @tabler/icons at the pinned version from the npm registry
//   ./run.sh icon-import --source icons-3.52.0.tgz    the same package, from a file
//   ./run.sh icon-import --source <folder> --out <file>   every *.svg in a folder (for tests)
//
// Writes one line per icon, "name<TAB>path data", sorted by name, gzip-compressed. The path data is absolute
// M/L/C/Z only, rounded to two decimals: circles, ellipses, lines, polylines, polygons, rectangles, arcs and
// quadratic curves are turned into these here, so the library needs no SVG parser. Elements with stroke="none"
// (Tabler's 24 x 24 frame) are left out. Prints the count and the sizes, and fails above the budget.

const string Version = "3.52.0";
const string Registry = "https://registry.npmjs.org/@tabler/icons/-/icons-" + Version + ".tgz";
const long DefaultBudget = 1_000_000;

var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", ".."));
string? source = null;
var output = Path.Combine(root, "src", "wwosik.DocLibs.DocumentGeneration", "Icons", "tabler-outline.txt.gz");
var budget = DefaultBudget;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--source" when i + 1 < args.Length:
            source = args[++i];
            break;
        case "--out" when i + 1 < args.Length:
            output = Path.GetFullPath(args[++i]);
            break;
        case "--budget" when i + 1 < args.Length:
            budget = long.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Use --source <.tgz or folder>, --out <file>, --budget <bytes>.");
            return 2;
    }
}

SortedDictionary<string, string> svgs;
string version;
if (source is not null && Directory.Exists(source))
{
    svgs = new(StringComparer.Ordinal);
    foreach (var file in Directory.GetFiles(source, "*.svg"))
    {
        svgs[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
    }

    version = "(folder " + source + ")";
}
else
{
    byte[] package;
    if (source is null)
    {
        Console.WriteLine($"Downloading {Registry}");
        using var http = new HttpClient();
        package = await http.GetByteArrayAsync(Registry);
    }
    else
    {
        package = File.ReadAllBytes(source);
    }

    (svgs, version) = ReadPackage(package);
    if (source is null && version != Version)
    {
        Console.Error.WriteLine($"The registry served version {version}, not {Version}.");
        return 1;
    }
}

var text = new StringBuilder();
foreach (var (name, svg) in svgs)
{
    try
    {
        var data = Icons.Convert(svg);
        if (data.Length == 0)
        {
            throw new InvalidDataException("no outline");
        }

        text.Append(name).Append('\t').Append(data).Append('\n');
    }
    catch (Exception e) when (e is InvalidDataException or FormatException or System.Xml.XmlException)
    {
        Console.Error.WriteLine($"The icon '{name}' cannot be converted: {e.Message}");
        return 1;
    }
}

var raw = Encoding.UTF8.GetBytes(text.ToString());
using var compressed = new MemoryStream();
using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
{
    gzip.Write(raw);
}

Console.WriteLine($"Tabler Icons {version}, outline: {svgs.Count} icons");
Console.WriteLine($"Raw size: {raw.Length:N0} bytes");
Console.WriteLine($"Compressed size: {compressed.Length:N0} bytes (budget {budget:N0})");
if (compressed.Length > budget)
{
    Console.Error.WriteLine("Over the budget: cut the set to a curated list, and ask the human first.");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllBytes(output, compressed.ToArray());
Console.WriteLine($"Wrote {output}");
Console.WriteLine($"For THIRD-PARTY-NOTICES.txt: Tabler Icons {version}, outline style, from {Registry}");
return 0;

static (SortedDictionary<string, string> Svgs, string Version) ReadPackage(byte[] package)
{
    var svgs = new SortedDictionary<string, string>(StringComparer.Ordinal);
    var version = "unknown";
    using var gzip = new GZipStream(new MemoryStream(package), CompressionMode.Decompress);
    using var tar = new TarReader(gzip);
    while (tar.GetNextEntry() is { } entry)
    {
        if (entry.DataStream is null)
        {
            continue;
        }

        using var reader = new StreamReader(entry.DataStream, Encoding.UTF8);
        if (entry.Name == "package/package.json")
        {
            version = JsonDocument.Parse(reader.ReadToEnd()).RootElement.GetProperty("version").GetString() ?? version;
        }
        else if (entry.Name.StartsWith("package/icons/outline/", StringComparison.Ordinal) && entry.Name.EndsWith(".svg", StringComparison.Ordinal))
        {
            svgs[Path.GetFileNameWithoutExtension(entry.Name)] = reader.ReadToEnd();
        }
    }

    return (svgs, version);
}

static string SourcePath([CallerFilePath] string path = "") => path;

/// <summary>SVG shapes to absolute M/L/C/Z path data.</summary>
internal static class Icons
{
    private const double Kappa = 0.5522847498307936;

    public static string Convert(string svg)
    {
        var document = XDocument.Parse(svg);
        var path = new PathWriter();
        foreach (var element in document.Descendants())
        {
            if ((string?)element.Attribute("stroke") == "none")
            {
                continue;
            }

            double Number(string name) => double.Parse((string?)element.Attribute(name) ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
            switch (element.Name.LocalName)
            {
                case "path":
                    Path((string?)element.Attribute("d") ?? string.Empty, path);
                    break;
                case "circle":
                    Ellipse(Number("cx"), Number("cy"), Number("r"), Number("r"), path);
                    break;
                case "ellipse":
                    Ellipse(Number("cx"), Number("cy"), Number("rx"), Number("ry"), path);
                    break;
                case "line":
                    path.Move(Number("x1"), Number("y1"));
                    path.Line(Number("x2"), Number("y2"));
                    break;
                case "polyline":
                case "polygon":
                    var points = Numbers((string?)element.Attribute("points") ?? string.Empty);
                    for (var i = 0; i + 1 < points.Count; i += 2)
                    {
                        if (i == 0)
                        {
                            path.Move(points[i], points[i + 1]);
                        }
                        else
                        {
                            path.Line(points[i], points[i + 1]);
                        }
                    }

                    if (element.Name.LocalName == "polygon")
                    {
                        path.Close();
                    }

                    break;
                case "rect":
                    Rectangle(Number("x"), Number("y"), Number("width"), Number("height"), element.Attribute("rx") is null ? Number("ry") : Number("rx"), element.Attribute("ry") is null ? Number("rx") : Number("ry"), path);
                    break;
                case "svg":
                case "title":
                case "desc":
                case "defs":
                case "g":
                    break;
                default:
                    throw new InvalidDataException($"the element <{element.Name.LocalName}> is not supported");
            }
        }

        return path.ToString();
    }

    private static void Ellipse(double cx, double cy, double rx, double ry, PathWriter path)
    {
        var (kx, ky) = (rx * Kappa, ry * Kappa);
        path.Move(cx + rx, cy);
        path.Cubic(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry);
        path.Cubic(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy);
        path.Cubic(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry);
        path.Cubic(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy);
        path.Close();
    }

    private static void Rectangle(double x, double y, double width, double height, double rx, double ry, PathWriter path)
    {
        rx = Math.Min(rx, width / 2);
        ry = Math.Min(ry, height / 2);
        if (rx <= 0 || ry <= 0)
        {
            path.Move(x, y);
            path.Line(x + width, y);
            path.Line(x + width, y + height);
            path.Line(x, y + height);
            path.Close();
            return;
        }

        var (kx, ky) = (rx * Kappa, ry * Kappa);
        path.Move(x + rx, y);
        path.Line(x + width - rx, y);
        path.Cubic(x + width - rx + kx, y, x + width, y + ry - ky, x + width, y + ry);
        path.Line(x + width, y + height - ry);
        path.Cubic(x + width, y + height - ry + ky, x + width - rx + kx, y + height, x + width - rx, y + height);
        path.Line(x + rx, y + height);
        path.Cubic(x + rx - kx, y + height, x, y + height - ry + ky, x, y + height - ry);
        path.Line(x, y + ry);
        path.Cubic(x, y + ry - ky, x + rx - kx, y, x + rx, y);
        path.Close();
    }

    // The full SVG path grammar: relative and absolute commands, implicit repeats, H/V, S/T reflections, arcs.
    private static void Path(string d, PathWriter path)
    {
        var tokens = new Tokens(d);
        var (x, y, startX, startY) = (0.0, 0.0, 0.0, 0.0);
        var (lastControlX, lastControlY) = (0.0, 0.0);
        var previous = ' ';
        var command = ' ';
        while (tokens.More)
        {
            if (tokens.Command() is { } next)
            {
                command = next;
            }
            else if (command is ' ' or 'Z' or 'z')
            {
                throw new FormatException($"a number without a command in '{d}'");
            }

            var relative = char.IsLower(command);
            var (ox, oy) = relative ? (x, y) : (0.0, 0.0);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    (x, y) = (ox + tokens.Number(), oy + tokens.Number());
                    (startX, startY) = (x, y);
                    path.Move(x, y);
                    command = relative ? 'l' : 'L'; // further pairs are lines
                    previous = 'M';
                    continue;
                case 'L':
                    (x, y) = (ox + tokens.Number(), oy + tokens.Number());
                    path.Line(x, y);
                    break;
                case 'H':
                    x = ox + tokens.Number();
                    path.Line(x, y);
                    break;
                case 'V':
                    y = (relative ? y : 0) + tokens.Number();
                    path.Line(x, y);
                    break;
                case 'C':
                {
                    var (x1, y1, x2, y2) = (ox + tokens.Number(), oy + tokens.Number(), ox + tokens.Number(), oy + tokens.Number());
                    (x, y) = (ox + tokens.Number(), oy + tokens.Number());
                    path.Cubic(x1, y1, x2, y2, x, y);
                    (lastControlX, lastControlY) = (x2, y2);
                    previous = 'C';
                    continue;
                }

                case 'S':
                {
                    var (x1, y1) = previous == 'C' ? ((2 * x) - lastControlX, (2 * y) - lastControlY) : (x, y);
                    var (x2, y2) = (ox + tokens.Number(), oy + tokens.Number());
                    (x, y) = (ox + tokens.Number(), oy + tokens.Number());
                    path.Cubic(x1, y1, x2, y2, x, y);
                    (lastControlX, lastControlY) = (x2, y2);
                    previous = 'C';
                    continue;
                }

                case 'Q':
                {
                    var (qx, qy) = (ox + tokens.Number(), oy + tokens.Number());
                    var (ex, ey) = (ox + tokens.Number(), oy + tokens.Number());
                    Quadratic(x, y, qx, qy, ex, ey, path);
                    (x, y) = (ex, ey);
                    (lastControlX, lastControlY) = (qx, qy);
                    previous = 'Q';
                    continue;
                }

                case 'T':
                {
                    var (qx, qy) = previous == 'Q' ? ((2 * x) - lastControlX, (2 * y) - lastControlY) : (x, y);
                    var (ex, ey) = (ox + tokens.Number(), oy + tokens.Number());
                    Quadratic(x, y, qx, qy, ex, ey, path);
                    (x, y) = (ex, ey);
                    (lastControlX, lastControlY) = (qx, qy);
                    previous = 'Q';
                    continue;
                }

                case 'A':
                {
                    var (rx, ry, rotation) = (tokens.Number(), tokens.Number(), tokens.Number());
                    var (large, sweep) = (tokens.Flag(), tokens.Flag());
                    var (ex, ey) = (ox + tokens.Number(), oy + tokens.Number());
                    Arc(x, y, rx, ry, rotation, large, sweep, ex, ey, path);
                    (x, y) = (ex, ey);
                    break;
                }

                case 'Z':
                    path.Close();
                    (x, y) = (startX, startY);
                    break;
                default:
                    throw new FormatException($"the command '{command}' in '{d}'");
            }

            previous = char.ToUpperInvariant(command);
        }
    }

    private static void Quadratic(double x0, double y0, double qx, double qy, double x, double y, PathWriter path) =>
        path.Cubic(x0 + (2.0 / 3 * (qx - x0)), y0 + (2.0 / 3 * (qy - y0)), x + (2.0 / 3 * (qx - x)), y + (2.0 / 3 * (qy - y)), x, y);

    // SVG's arc implementation notes (F.6): endpoint to centre parameterisation, then cubics of at most 90 degrees.
    private static void Arc(double x1, double y1, double rx, double ry, double degrees, bool large, bool sweep, double x2, double y2, PathWriter path)
    {
        if (x1 == x2 && y1 == y2)
        {
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx == 0 || ry == 0)
        {
            path.Line(x2, y2);
            return;
        }

        var phi = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(phi), Math.Sin(phi));
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

        static double Angle(double ux, double uy, double vx, double vy)
        {
            var angle = Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
            return angle;
        }

        var theta = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        var segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
        segments = Math.Max(1, segments);
        var step = delta / segments;
        var k = 4.0 / 3 * Math.Tan(step / 4);
        (double X, double Y) Point(double t) => (cx + (rx * Math.Cos(t) * cos) - (ry * Math.Sin(t) * sin), cy + (rx * Math.Cos(t) * sin) + (ry * Math.Sin(t) * cos));
        (double X, double Y) Derivative(double t) => ((-rx * Math.Sin(t) * cos) - (ry * Math.Cos(t) * sin), (-rx * Math.Sin(t) * sin) + (ry * Math.Cos(t) * cos));
        for (var i = 0; i < segments; i++)
        {
            var (t1, t2) = (theta + (i * step), theta + ((i + 1) * step));
            var (p1, p2) = (Point(t1), i == segments - 1 ? (x2, y2) : Point(t2));
            var (d1, d2) = (Derivative(t1), Derivative(t2));
            path.Cubic(p1.X + (k * d1.Item1), p1.Y + (k * d1.Item2), p2.Item1 - (k * d2.Item1), p2.Item2 - (k * d2.Item2), p2.Item1, p2.Item2);
        }
    }

    private static List<double> Numbers(string text)
    {
        var tokens = new Tokens(text);
        var numbers = new List<double>();
        while (tokens.More)
        {
            numbers.Add(tokens.Number());
        }

        return numbers;
    }

    /// <summary>Reads SVG path data: command letters, numbers such as <c>-.5.5</c> (two numbers), flags.</summary>
    private sealed class Tokens(string text)
    {
        private int position;

        public bool More
        {
            get
            {
                this.Skip();
                return this.position < text.Length;
            }
        }

        public char? Command()
        {
            this.Skip();
            return this.position < text.Length && char.IsLetter(text[this.position]) && text[this.position] is not ('e' or 'E') ? text[this.position++] : null;
        }

        public bool Flag()
        {
            this.Skip();
            if (this.position >= text.Length || text[this.position] is not ('0' or '1'))
            {
                throw new FormatException($"an arc flag expected at {this.position} in '{text}'");
            }

            return text[this.position++] == '1';
        }

        public double Number()
        {
            this.Skip();
            var start = this.position;
            if (this.position < text.Length && text[this.position] is '-' or '+')
            {
                this.position++;
            }

            var dot = false;
            while (this.position < text.Length && (char.IsAsciiDigit(text[this.position]) || (text[this.position] == '.' && !dot)))
            {
                dot |= text[this.position] == '.';
                this.position++;
            }

            if (this.position < text.Length && text[this.position] is 'e' or 'E')
            {
                this.position++;
                if (this.position < text.Length && text[this.position] is '-' or '+')
                {
                    this.position++;
                }

                while (this.position < text.Length && char.IsAsciiDigit(text[this.position]))
                {
                    this.position++;
                }
            }

            if (this.position == start)
            {
                throw new FormatException($"a number expected at {start} in '{text}'");
            }

            return double.Parse(text.AsSpan(start, this.position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void Skip()
        {
            while (this.position < text.Length && (char.IsWhiteSpace(text[this.position]) || text[this.position] == ','))
            {
                this.position++;
            }
        }
    }

    /// <summary>Writes M/L/C/Z with absolute numbers of at most two decimals, as short as they go.</summary>
    private sealed class PathWriter
    {
        private readonly StringBuilder text = new();

        public void Move(double x, double y) => this.Command('M', x, y);

        public void Line(double x, double y) => this.Command('L', x, y);

        public void Cubic(double x1, double y1, double x2, double y2, double x, double y) => this.Command('C', x1, y1, x2, y2, x, y);

        public void Close() => this.text.Append('Z');

        public override string ToString() => this.text.ToString();

        private void Command(char command, params double[] numbers)
        {
            this.text.Append(command);
            for (var i = 0; i < numbers.Length; i++)
            {
                var number = Format(numbers[i]);
                if (i > 0 && number[0] != '-')
                {
                    this.text.Append(' ');
                }

                this.text.Append(number);
            }
        }

        private static string Format(double value)
        {
            var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            if (rounded == 0)
            {
                return "0";
            }

            var number = rounded.ToString("0.##", CultureInfo.InvariantCulture);
            return number.StartsWith("0.", StringComparison.Ordinal) ? number[1..] : number.StartsWith("-0.", StringComparison.Ordinal) ? "-" + number[2..] : number;
        }
    }
}
