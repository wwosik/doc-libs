using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;

namespace wwosik.DocLibs.DocumentConversions.Pdf;

/// <summary>A rectangle in PDF coordinates: y grows upwards.</summary>
internal readonly record struct Area(double Left, double Bottom, double Right, double Top)
{
    public double Width => this.Right - this.Left;

    public double Height => this.Top - this.Bottom;

    public double CentreX => (this.Left + this.Right) / 2;

    public double CentreY => (this.Bottom + this.Top) / 2;

    public bool Contains(double x, double y, double tolerance = 0) =>
        x >= this.Left - tolerance && x <= this.Right + tolerance && y >= this.Bottom - tolerance && y <= this.Top + tolerance;
}

/// <summary>
/// A horizontal or vertical line: <see cref="At"/> is its y (horizontal) or x (vertical), and it runs from
/// <see cref="From"/> to <see cref="To"/> along the other axis.
/// </summary>
internal sealed record Rule(double At, double From, double To)
{
    public double Length => this.To - this.From;
}

/// <summary>A small square drawn where a list marker goes; <see cref="Ticked"/> when filled or holding a tick.</summary>
internal sealed record MarkerBox(Area Area, bool Ticked);

/// <summary>
/// The drawn shapes of a page that carry structure: rules (table borders, separators), boxes and ticks (task
/// lists), dots (drawn bullets) and filled rectangles (cell and code backgrounds). Everything else is ignored.
/// </summary>
internal sealed class PageGraphics
{
    // A rule is thinner than this; positions within it are the same line.
    private const double Thin = 1.5;

    public static readonly PageGraphics Empty = new();

    public List<Rule> Horizontal { get; } = [];

    public List<Rule> Vertical { get; } = [];

    public List<MarkerBox> Boxes { get; } = [];

    public List<Area> Dots { get; } = [];

    public List<Area> Fills { get; } = [];

    /// <summary>Sorts the page's paths; <paramref name="body"/> is the body text size, which sets what counts as small.</summary>
    public static PageGraphics Read(Page page, double body)
    {
        var graphics = new PageGraphics();
        var boxes = new List<(Area Area, bool Filled)>();
        var ticks = new List<Area>();
        var fills = new List<Area>();
        var horizontal = new List<Rule>();
        var vertical = new List<Rule>();
        foreach (var path in page.Paths)
        {
            if (path.IsClipping || !(path.IsFilled || path.IsStroked))
            {
                continue;
            }

            var filled = path.IsFilled && !IsWhite(path);
            foreach (var subpath in path)
            {
                if (subpath.GetBoundingRectangle() is not { } bounds)
                {
                    continue;
                }

                var area = new Area(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top);
                var (width, height) = (area.Width, area.Height);
                var curved = subpath.Commands.Any(c => c is PdfSubpath.BezierCurve);
                var rectangle = !curved && (subpath.IsDrawnAsRectangle || IsAxisRectangle(subpath, area));
                var small = Math.Max(width, height);
                if (Math.Min(width, height) < Thin && small >= 2 && !curved && (path.IsStroked || rectangle))
                {
                    (width >= height ? horizontal : vertical).Add(width >= height
                        ? new Rule(area.CentreY, area.Left, area.Right)
                        : new Rule(area.CentreX, area.Bottom, area.Top));
                }
                else if (filled && small <= body * 0.6)
                {
                    graphics.Dots.Add(area);
                }
                else if (rectangle && small <= body * 1.2 && Math.Min(width, height) >= body * 0.3 && small / Math.Min(width, height) <= 1.35)
                {
                    boxes.Add((area, filled));
                }
                else if (path.IsStroked && !subpath.IsClosed() && small <= body * 1.2)
                {
                    ticks.Add(area);
                }
                else if (rectangle && path.IsFilled)
                {
                    fills.Add(area);
                }
                else if (rectangle && path.IsStroked)
                {
                    // A framed rectangle: its edges are rules, as cell borders drawn as rectangles are.
                    horizontal.Add(new Rule(area.Top, area.Left, area.Right));
                    horizontal.Add(new Rule(area.Bottom, area.Left, area.Right));
                    vertical.Add(new Rule(area.Left, area.Bottom, area.Top));
                    vertical.Add(new Rule(area.Right, area.Bottom, area.Top));
                }
            }
        }

        graphics.Horizontal.AddRange(Merge(horizontal));
        graphics.Vertical.AddRange(Merge(vertical));
        graphics.Boxes.AddRange(boxes.Select(b => new MarkerBox(b.Area, b.Filled || ticks.Any(t => b.Area.Contains(t.CentreX, t.CentreY)))));
        graphics.Fills.AddRange(MergeFills(fills));
        return graphics;
    }

    // Rules on the same line whose pieces touch become one: generators often draw a table's lines cell by cell.
    internal static List<Rule> Merge(IEnumerable<Rule> rules)
    {
        var result = new List<Rule>();
        var sorted = rules.OrderBy(r => r.At).ToList();
        var start = 0;
        while (start < sorted.Count)
        {
            var end = start;
            while (end + 1 < sorted.Count && sorted[end + 1].At - sorted[start].At <= 1)
            {
                end++;
            }

            var group = sorted.GetRange(start, end - start + 1);
            var at = group.Average(r => r.At);
            Rule? current = null;
            foreach (var rule in group.OrderBy(r => r.From))
            {
                if (current is not null && rule.From <= current.To + Thin)
                {
                    current = current with { To = Math.Max(current.To, rule.To) };
                    continue;
                }

                if (current is not null)
                {
                    result.Add(current);
                }

                current = new Rule(at, rule.From, rule.To);
            }

            result.Add(current!);
            start = end + 1;
        }

        return result;
    }

    // Fills stacked edge to edge with the same left and right (a code block drawn line by line) become one.
    private static List<Area> MergeFills(List<Area> fills)
    {
        var result = new List<Area>();
        foreach (var fill in fills.OrderByDescending(f => f.Top))
        {
            var index = result.FindIndex(r => Math.Abs(r.Left - fill.Left) <= 1 && Math.Abs(r.Right - fill.Right) <= 1 && fill.Top >= r.Bottom - 1 && fill.Bottom <= r.Top);
            if (index >= 0)
            {
                var merged = result[index];
                result[index] = merged with { Bottom = Math.Min(merged.Bottom, fill.Bottom), Top = Math.Max(merged.Top, fill.Top) };
            }
            else
            {
                result.Add(fill);
            }
        }

        return result;
    }

    private static bool IsWhite(PdfPath path)
    {
        if (path.FillColor is not { } color)
        {
            return false;
        }

        var (r, g, b) = color.ToRGBValues();
        return r > 0.95 && g > 0.95 && b > 0.95;
    }

    // Four corners and straight lines between them: a rectangle drawn with m/l rather than re.
    private static bool IsAxisRectangle(PdfSubpath subpath, Area area)
    {
        var points = new List<PdfPoint>();
        foreach (var command in subpath.Commands)
        {
            switch (command)
            {
                case PdfSubpath.Move move:
                    points.Add(move.Location);
                    break;
                case PdfSubpath.Line line:
                    points.Add(line.From);
                    points.Add(line.To);
                    break;
            }
        }

        const double t = 0.5;
        return points.Count >= 4
            && points.All(p => (Math.Abs(p.X - area.Left) <= t || Math.Abs(p.X - area.Right) <= t) && (Math.Abs(p.Y - area.Bottom) <= t || Math.Abs(p.Y - area.Top) <= t))
            && points.Select(p => (Math.Round(p.X), Math.Round(p.Y))).Distinct().Count() >= 4;
    }
}
