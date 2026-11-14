using System.Text;
using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A Harvey ball: a table cell whose whole content is <c>[0/4]</c> to <c>[4/4]</c>, or <c>[1/2]</c> for <c>[2/4]</c>,
/// is a circle filled by that many quarters. Decks write the glyph (○ ◔ ◑ ◕ ●) in <see cref="SymbolFont"/>, so it stays
/// in its row and editable; PDFs draw <see cref="Drawing"/>, since the bundled fonts lack ◔ ◑ ◕. A cell with anything
/// else beside it, or with the bracket escaped (<c>\[3/4]</c>), stays text.
/// </summary>
internal static partial class HarveyBall
{
    /// <summary>The font decks name for the glyphs, so PowerPoint does not depend on the theme font having them. Never embedded.</summary>
    public const string SymbolFont = "Segoe UI Symbol";

    // ○ ◔ ◑ ◕ ●, by quarters filled.
    private static readonly string[] Glyphs = ["○", "◔", "◑", "◕", "●"];

    /// <summary>The quarters of a cell's leaf blocks that are one Harvey ball; <see langword="null"/> for any other cell.</summary>
    public static int? Quarters(IEnumerable<LeafBlock> leaves)
    {
        var withText = leaves.Where(l => l.Inline is not null).ToList();
        if (withText is not [{ Inline: { } inline }])
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var child in inline)
        {
            if (child is not LiteralInline { IsFirstCharacterEscaped: false } literal)
            {
                return null;
            }

            text.Append(literal.Content.ToString());
        }

        return Quarters(text.ToString());
    }

    /// <summary>The quarters of <c>[n/4]</c> or <c>[1/2]</c>, white space around it aside; <see langword="null"/> for any other text.</summary>
    public static int? Quarters(string text)
    {
        var match = Pattern().Match(text.Trim());
        return !match.Success ? null
            : match.Groups[1].Success ? match.Groups[1].Value[0] - '0'
            : 2;
    }

    /// <summary>The glyph for <paramref name="quarters"/>: ○ ◔ ◑ ◕ ●.</summary>
    public static string Glyph(int quarters) => Glyphs[quarters];

    /// <summary>
    /// The ball as a 24 × 24 px drawing: a ring, stroked, and the quarters from twelve o'clock clockwise, filled. A writer
    /// draws it in the text colour.
    /// </summary>
    public static Drawing Drawing(int quarters)
    {
        const double centre = 12;
        const double radius = 10.5;
        var items = new List<DrawingItem>
        {
            new DrawingShape(TimelineDrawing.Ellipse(centre, centre, radius), null, "000000", 1.5) { Kind = DrawingShapeKind.Ellipse },
        };
        if (quarters == 4)
        {
            items.Add(new DrawingShape(TimelineDrawing.Ellipse(centre, centre, radius), "000000", null, 0) { Kind = DrawingShapeKind.Ellipse });
        }
        else if (quarters > 0)
        {
            items.Add(new DrawingShape(Sector(centre, centre, radius, quarters), "000000", null, 0));
        }

        return new Drawing(24, 24, items);
    }

    // From the centre up to twelve o'clock, then clockwise a quarter arc at a time.
    private static DrawingPath Sector(double x, double y, double radius, int quarters)
    {
        var k = radius * 0.5523;
        var arcs = new PathSegment[]
        {
            new CubicTo(x + k, y - radius, x + radius, y - k, x + radius, y),
            new CubicTo(x + radius, y + k, x + k, y + radius, x, y + radius),
            new CubicTo(x - k, y + radius, x - radius, y + k, x - radius, y),
            new CubicTo(x - radius, y - k, x - k, y - radius, x, y - radius),
        };
        return new DrawingPath([new MoveTo(x, y), new LineTo(x, y - radius), .. arcs.Take(quarters), new ClosePath()]);
    }

    [GeneratedRegex(@"^\[(?:([0-4])/4|1/2)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
