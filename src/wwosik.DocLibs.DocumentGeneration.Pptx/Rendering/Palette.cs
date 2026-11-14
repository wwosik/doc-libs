using System.Globalization;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>A theme: its colour scheme, major and minor Latin fonts, and format scheme (fill, line and background styles).</summary>
internal sealed class DeckTheme
{
    private DeckTheme(IReadOnlyDictionary<string, XElement> colors, string major, string minor, XElement? formats)
    {
        this.Colors = colors;
        this.Major = major;
        this.Minor = minor;
        this.Formats = formats;
    }

    /// <summary>The scheme's colour elements by name (<c>dk1</c>, <c>lt1</c>, <c>accent1</c> …).</summary>
    public IReadOnlyDictionary<string, XElement> Colors { get; }

    public string Major { get; }

    public string Minor { get; }

    public XElement? Formats { get; }

    public static DeckTheme Read(XElement? theme)
    {
        var elements = theme?.Element(Ns.A + "themeElements");
        var colors = elements?.Element(Ns.A + "clrScheme")?.Elements()
            .Where(e => e.Elements().Any())
            .ToDictionary(e => e.Name.LocalName, e => e.Elements().First(), StringComparer.Ordinal)
            ?? [];
        var fonts = elements?.Element(Ns.A + "fontScheme");
        return new DeckTheme(
            colors,
            fonts?.Element(Ns.A + "majorFont")?.Element(Ns.A + "latin")?.Attribute("typeface")?.Value ?? "Calibri Light",
            fonts?.Element(Ns.A + "minorFont")?.Element(Ns.A + "latin")?.Attribute("typeface")?.Value ?? "Calibri",
            elements?.Element(Ns.A + "fmtScheme"));
    }

    /// <summary>The <paramref name="index"/>-th (from 1) style of a format scheme list: <c>fillStyleLst</c>, <c>lnStyleLst</c>, <c>bgFillStyleLst</c>.</summary>
    public XElement? Style(string list, int index) =>
        index < 1 ? null : this.Formats?.Element(Ns.A + list)?.Elements().ElementAtOrDefault(index - 1);

    /// <summary>The family a theme font reference stands for (<c>+mj-lt</c>, <c>+mn-lt</c>); other names as they are.</summary>
    public string Font(string typeface) => typeface switch
    {
        "+mj-lt" or "+mj-ea" or "+mj-cs" => this.Major,
        "+mn-lt" or "+mn-ea" or "+mn-cs" => this.Minor,
        _ => typeface,
    };
}

/// <summary>
/// Resolves colours, fills and lines for one slide: scheme colours through the slide's colour map (master
/// <c>clrMap</c>, layout and slide <c>clrMapOvr</c>) to the theme, <c>sysClr</c>, <c>prstClr</c>, and the modifiers
/// in DrawingML order. Unknown fills are reported.
/// </summary>
internal sealed class Palette(DeckTheme theme, IReadOnlyDictionary<string, string> map, RenderNotes notes)
{
    private static readonly Dictionary<string, string> PresetColors = new(StringComparer.Ordinal)
    {
        ["black"] = "000000", ["white"] = "FFFFFF", ["red"] = "FF0000", ["green"] = "008000", ["blue"] = "0000FF",
        ["yellow"] = "FFFF00", ["gray"] = "808080", ["grey"] = "808080", ["darkGray"] = "A9A9A9", ["lightGray"] = "D3D3D3",
        ["orange"] = "FFA500", ["purple"] = "800080", ["cyan"] = "00FFFF", ["magenta"] = "FF00FF", ["navy"] = "000080",
        ["silver"] = "C0C0C0", ["maroon"] = "800000", ["olive"] = "808000", ["teal"] = "008080", ["lime"] = "00FF00",
    };

    public DeckTheme Theme { get; } = theme;

    public RenderNotes Notes { get; } = notes;

    /// <summary>A colour map read from a <c>p:clrMap</c> or <c>a:overrideClrMapping</c>, over <paramref name="inherited"/>.</summary>
    public static IReadOnlyDictionary<string, string> Map(XElement? element, IReadOnlyDictionary<string, string>? inherited)
    {
        var map = new Dictionary<string, string>(inherited ?? new Dictionary<string, string>
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
        }, StringComparer.Ordinal);
        foreach (var attribute in element?.Attributes() ?? [])
        {
            map[attribute.Name.LocalName] = attribute.Value;
        }

        return map;
    }

    /// <summary>The colour of a colour element (<c>srgbClr</c>, <c>schemeClr</c> …) or of the first one inside <paramref name="element"/>.</summary>
    public Rgba? Color(XElement? element, Rgba? placeholder = null)
    {
        if (element is null)
        {
            return null;
        }

        if (!IsColor(element.Name.LocalName))
        {
            element = element.Elements().FirstOrDefault(e => IsColor(e.Name.LocalName));
            if (element is null)
            {
                return null;
            }
        }

        Rgba? baseColor = element.Name.LocalName switch
        {
            "srgbClr" => Rgba.Parse(element.Attribute("val")?.Value ?? "000000"),
            "schemeClr" => this.Scheme(element.Attribute("val")?.Value ?? "tx1", placeholder),
            "sysClr" => Rgba.Parse(element.Attribute("lastClr")?.Value ?? (element.Attribute("val")?.Value == "window" ? "FFFFFF" : "000000")),
            "prstClr" => Rgba.Parse(PresetColors.GetValueOrDefault(element.Attribute("val")?.Value ?? string.Empty, "000000")),
            "scrgbClr" => FromLinear(Percent(element, "r"), Percent(element, "g"), Percent(element, "b"), 1),
            "hslClr" => FromHsl(Number(element, "hue") / 60000 / 360, Percent(element, "sat"), Percent(element, "lum"), 1),
            _ => null,
        };
        return baseColor is { } color ? Modify(color, element) : null;
    }

    private static bool IsColor(string name) => name is "srgbClr" or "schemeClr" or "sysClr" or "prstClr" or "scrgbClr" or "hslClr";

    private Rgba? Scheme(string name, Rgba? placeholder)
    {
        if (name == "phClr")
        {
            return placeholder ?? Rgba.Black;
        }

        var mapped = map.GetValueOrDefault(name, name);
        // Theme colours are srgbClr or sysClr, never scheme colours, so this does not recurse.
        return this.Theme.Colors.TryGetValue(mapped, out var color) && color.Name.LocalName != "schemeClr" ? this.Color(color) : null;
    }

    // The modifiers in document order, as PowerPoint applies them.
    private static Rgba Modify(Rgba color, XElement element)
    {
        foreach (var modifier in element.Elements())
        {
            var value = Number(modifier, "val") / 100000;
            switch (modifier.Name.LocalName)
            {
                case "alpha":
                    color = color with { A = value };
                    break;
                case "alphaMod":
                    color = color with { A = Math.Clamp(color.A * value, 0, 1) };
                    break;
                case "alphaOff":
                    color = color with { A = Math.Clamp(color.A + value, 0, 1) };
                    break;
                case "lumMod" or "lumOff" or "satMod" or "satOff" or "hueMod" or "hueOff":
                    var (h, s, l) = ToHsl(color);
                    switch (modifier.Name.LocalName)
                    {
                        case "lumMod": l *= value; break;
                        case "lumOff": l += value; break;
                        case "satMod": s *= value; break;
                        case "satOff": s += value; break;
                        case "hueMod": h = (h * value) % 1; break;
                        default: h = (h + (Number(modifier, "val") / 60000 / 360)) % 1; break;
                    }

                    color = FromHsl(h, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1), color.A);
                    break;
                case "tint" or "shade":
                    var (r, g, b) = (Linear(color.R), Linear(color.G), Linear(color.B));
                    (r, g, b) = modifier.Name.LocalName == "tint"
                        ? (1 - ((1 - r) * value), 1 - ((1 - g) * value), 1 - ((1 - b) * value))
                        : (r * value, g * value, b * value);
                    color = FromLinear(r, g, b, color.A);
                    break;
                case "comp":
                    var (ch, cs, cl) = ToHsl(color);
                    color = FromHsl((ch + 0.5) % 1, cs, cl, color.A);
                    break;
                case "inv":
                    color = new Rgba((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B), color.A);
                    break;
                case "gray":
                    var grey = (byte)Math.Round((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B));
                    color = new Rgba(grey, grey, grey, color.A);
                    break;
            }
        }

        return color;
    }

    /// <summary>
    /// The fill of the first fill element in <paramref name="container"/> (<c>noFill</c> gives <see cref="NoFill"/>);
    /// <see langword="null"/> when there is none. <paramref name="images"/> resolves a <c>blipFill</c>.
    /// </summary>
    public SceneFill? Fill(XElement? container, Rgba? placeholder, Func<XElement, SceneImage?>? images)
    {
        foreach (var fill in container?.Elements() ?? [])
        {
            switch (fill.Name.LocalName)
            {
                case "noFill":
                    return NoFill;
                case "solidFill":
                    return this.Color(fill, placeholder) is { } color ? new SolidFill(color) : NoFill;
                case "gradFill":
                    return this.Gradient(fill, placeholder);
                case "blipFill":
                    return images?.Invoke(fill) is { } image ? new ImageFill(image) : NoFill;
                case "pattFill":
                    this.Notes.Unsupported("pattern fills (drawn in their foreground colour)");
                    return this.Color(fill.Element(Ns.A + "fgClr"), placeholder) is { } foreground ? new SolidFill(foreground) : NoFill;
                case "grpFill":
                    return GroupFill;
            }
        }

        return null;
    }

    /// <summary>A fill that draws nothing, distinct from "no fill given".</summary>
    public static readonly SceneFill NoFill = new SolidFill(new Rgba(0, 0, 0, 0));

    /// <summary>The group's fill, which the reader replaces with the enclosing group's.</summary>
    public static readonly SceneFill GroupFill = new SolidFill(new Rgba(1, 2, 3, 0));

    private GradientFill Gradient(XElement fill, Rgba? placeholder)
    {
        var stops = fill.Element(Ns.A + "gsLst")?.Elements(Ns.A + "gs")
            .Select(gs => new GradientStop(Number(gs, "pos") / 100000, this.Color(gs, placeholder) ?? Rgba.Black))
            .OrderBy(s => s.Position)
            .ToList() ?? [];
        if (stops.Count == 0)
        {
            stops.Add(new GradientStop(0, Rgba.Black));
        }

        var linear = fill.Element(Ns.A + "lin");
        return new GradientFill(stops, linear is null ? 0 : Number(linear, "ang") / 60000, fill.Element(Ns.A + "path") is not null);
    }

    /// <summary>
    /// The line of an <c>a:ln</c> (with <paramref name="reference"/>, the theme line style it is based on); <see langword="null"/>
    /// when there is no line to draw.
    /// </summary>
    public SceneStroke? Stroke(XElement? line, XElement? reference, Rgba? placeholder)
    {
        if (line is null && reference is null)
        {
            return null;
        }

        XElement? Pick(string name) => line?.Element(Ns.A + name) ?? reference?.Element(Ns.A + name);
        string? Attribute(string name) => line?.Attribute(name)?.Value ?? reference?.Attribute(name)?.Value;
        var fill = this.Fill(line, placeholder, null) ?? this.Fill(reference, placeholder, null);
        Rgba color;
        switch (fill)
        {
            case SolidFill { Color: var solid } when solid.A > 0:
                color = solid;
                break;
            case GradientFill gradient:
                color = gradient.Stops[0].Color;
                break;
            default:
                return null;
        }

        var width = double.TryParse(Attribute("w"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 9525;
        IReadOnlyList<double>? dashes = null;
        if (Pick("custDash") is { } custom)
        {
            dashes = [.. custom.Elements(Ns.A + "ds").SelectMany(ds => new[] { Number(ds, "d") / 100000, Number(ds, "sp") / 100000 })];
        }
        else if (Pick("prstDash")?.Attribute("val")?.Value is { } preset && preset != "solid")
        {
            dashes = preset switch
            {
                "dot" or "sysDot" => [1, 1],
                "dash" or "sysDash" => [3, 1],
                "lgDash" => [8, 3],
                "dashDot" or "sysDashDot" => [4, 3, 1, 3],
                "lgDashDot" => [8, 3, 1, 3],
                "lgDashDotDot" or "sysDashDotDot" => [8, 3, 1, 3, 1, 3],
                _ => null,
            };
        }

        var join = line?.Elements().FirstOrDefault(e => e.Name.LocalName is "round" or "bevel" or "miter")
            ?? reference?.Elements().FirstOrDefault(e => e.Name.LocalName is "round" or "bevel" or "miter");
        var cap = Attribute("cap") switch
        {
            "rnd" => "round",
            "sq" => "square",
            _ => "butt",
        };
        return new SceneStroke(Math.Max(width, 1), color, dashes, join?.Name.LocalName ?? "round", cap, End(Pick("headEnd")), End(Pick("tailEnd")));
    }

    private static LineEnd? End(XElement? end) =>
        end?.Attribute("type")?.Value is { } type and not "none"
            ? new LineEnd(type, end.Attribute("w")?.Value ?? "med", end.Attribute("len")?.Value ?? "med")
            : null;

    public static double Number(XElement element, string attribute) =>
        double.TryParse(element.Attribute(attribute)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static double Percent(XElement element, string attribute) => Number(element, attribute) / 100000;

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static Rgba FromLinear(double r, double g, double b, double alpha)
    {
        static byte Channel(double value)
        {
            value = Math.Clamp(value, 0, 1);
            var c = value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
            return (byte)Math.Round(c * 255);
        }

        return new Rgba(Channel(r), Channel(g), Channel(b), alpha);
    }

    private static (double H, double S, double L) ToHsl(Rgba color)
    {
        var (r, g, b) = (color.R / 255.0, color.G / 255.0, color.B / 255.0);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h / 6, s, l);
    }

    private static Rgba FromHsl(double h, double s, double l, double alpha)
    {
        static double Hue(double p, double q, double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        }

        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
            var p = (2 * l) - q;
            (r, g, b) = (Hue(p, q, h + (1.0 / 3)), Hue(p, q, h), Hue(p, q, h - (1.0 / 3)));
        }

        static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return new Rgba(Byte(r), Byte(g), Byte(b), alpha);
    }
}

/// <summary>The XML namespaces of DrawingML and PresentationML.</summary>
internal static class Ns
{
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public static readonly XNamespace Svg = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
}
