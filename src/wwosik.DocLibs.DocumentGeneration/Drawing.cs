namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A vector drawing, such as a diagram, that both generators draw natively: vector graphics in PDFs, editable shapes
/// in decks. Coordinates are CSS pixels (a pixel is 0.75 pt) from the top left corner; colours are RGB hex
/// <c>RRGGBB</c>. Compared by content.
/// </summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Items">Shapes and texts, drawn in order: later items cover earlier ones.</param>
public sealed record Drawing(double Width, double Height, IReadOnlyList<DrawingItem> Items)
{
    /// <inheritdoc/>
    public bool Equals(Drawing? other) =>
        other is not null && this.Width == other.Width && this.Height == other.Height && this.Items.SequenceEqual(other.Items);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Width, this.Height, this.Items.Count);
}

/// <summary>A <see cref="DrawingShape"/>, a <see cref="DrawingText"/> or a <see cref="DrawingHotspot"/>.</summary>
public abstract record DrawingItem
{
    private protected DrawingItem()
    {
    }

    /// <summary>
    /// A tooltip shown while the pointer is over the item, in SVG only (<see cref="DrawingSvg"/>); decks and PDFs
    /// ignore it. <see langword="null"/>: none.
    /// </summary>
    public DrawingHover? Hover { get; init; }
}

/// <summary>A tooltip: its text, and the point it is shown above, centred and kept on the canvas.</summary>
/// <param name="Text">The text, one line.</param>
/// <param name="X">The point the tooltip is centred over.</param>
/// <param name="Y">The point the tooltip's bottom sits above.</param>
public sealed record DrawingHover(string Text, double X, double Y);

/// <summary>
/// An invisible circle that only carries a <see cref="DrawingItem.Hover"/>, such as a point on a line, which has no
/// area of its own to point at. SVG draws it as a transparent hit area, with a dot of <see cref="Dot"/> shown while
/// hovered; decks and PDFs skip it.
/// </summary>
/// <param name="X">The centre.</param>
/// <param name="Y">The centre.</param>
/// <param name="Radius">The radius of the hit area in pixels.</param>
public sealed record DrawingHotspot(double X, double Y, double Radius) : DrawingItem
{
    /// <summary>The colour of the dot shown while hovered; <see langword="null"/>: no dot.</summary>
    public string? Dot { get; init; }

    /// <summary>The radius of the dot in pixels. Default 4.</summary>
    public double DotRadius { get; init; } = 4;
}

/// <summary>What a shape is, so decks can use PowerPoint's own shapes for the common ones.</summary>
public enum DrawingShapeKind
{
    /// <summary>Any outline.</summary>
    Path,

    /// <summary>An axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>An axis-aligned rectangle with corners of <see cref="DrawingShape.CornerRadius"/>.</summary>
    RoundedRectangle,

    /// <summary>An axis-aligned ellipse or circle.</summary>
    Ellipse,
}

/// <summary>
/// An outline, filled and stroked. <see cref="Path"/> always holds the outline, whatever <see cref="Kind"/> says, so a
/// generator that does not know the kind can draw it.
/// </summary>
/// <param name="Path">The outline.</param>
/// <param name="Fill">The fill colour; <see langword="null"/>: not filled.</param>
/// <param name="Stroke">The line colour; <see langword="null"/>: no line.</param>
/// <param name="StrokeWidth">The line width in pixels.</param>
public sealed record DrawingShape(DrawingPath Path, string? Fill, string? Stroke, double StrokeWidth) : DrawingItem
{
    /// <summary>Dash and gap lengths in pixels, alternating; <see langword="null"/>: a solid line.</summary>
    public IReadOnlyList<double>? Dash { get; init; }

    /// <summary>What the outline is. Default <see cref="DrawingShapeKind.Path"/>.</summary>
    public DrawingShapeKind Kind { get; init; }

    /// <summary>The corner radius of a <see cref="DrawingShapeKind.RoundedRectangle"/>, in pixels.</summary>
    public double CornerRadius { get; init; }

    /// <summary>Whether line ends and corners are round rather than butt and mitred.</summary>
    public bool RoundJoins { get; init; }

    /// <inheritdoc/>
    public bool Equals(DrawingShape? other) =>
        other is not null && this.Path.Equals(other.Path) && this.Fill == other.Fill && this.Stroke == other.Stroke
        && this.StrokeWidth == other.StrokeWidth && this.Kind == other.Kind && this.CornerRadius == other.CornerRadius
        && this.RoundJoins == other.RoundJoins
        && (this.Dash is null ? other.Dash is null : other.Dash is not null && this.Dash.SequenceEqual(other.Dash));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Path, this.Fill, this.Stroke, this.StrokeWidth, this.Kind);
}

/// <summary>An outline of straight and cubic Bézier segments; each subpath starts with a <see cref="MoveTo"/>.</summary>
/// <param name="Segments">The segments in order.</param>
public sealed record DrawingPath(IReadOnlyList<PathSegment> Segments)
{
    /// <summary>The smallest rectangle holding every point, control points included.</summary>
    public (double X, double Y, double Width, double Height) Bounds
    {
        get
        {
            var points = this.Segments.SelectMany(s => s.Points).ToList();
            if (points.Count == 0)
            {
                return (0, 0, 0, 0);
            }

            var (left, top) = (points.Min(p => p.X), points.Min(p => p.Y));
            return (left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
        }
    }

    /// <summary>A rectangle as a closed path.</summary>
    public static DrawingPath Rectangle(double x, double y, double width, double height) => new(
    [
        new MoveTo(x, y), new LineTo(x + width, y), new LineTo(x + width, y + height), new LineTo(x, y + height), new ClosePath(),
    ]);

    /// <inheritdoc/>
    public bool Equals(DrawingPath? other) => other is not null && this.Segments.SequenceEqual(other.Segments);

    /// <inheritdoc/>
    public override int GetHashCode() => this.Segments.Count;
}

/// <summary>A segment of a <see cref="DrawingPath"/>.</summary>
public abstract record PathSegment
{
    private protected PathSegment()
    {
    }

    /// <summary>The segment's points, control points included.</summary>
    public abstract IEnumerable<(double X, double Y)> Points { get; }
}

/// <summary>Starts a subpath at a point.</summary>
public sealed record MoveTo(double X, double Y) : PathSegment
{
    /// <inheritdoc/>
    public override IEnumerable<(double X, double Y)> Points => [(this.X, this.Y)];
}

/// <summary>A straight line to a point.</summary>
public sealed record LineTo(double X, double Y) : PathSegment
{
    /// <inheritdoc/>
    public override IEnumerable<(double X, double Y)> Points => [(this.X, this.Y)];
}

/// <summary>A cubic Bézier curve through two control points to a point.</summary>
public sealed record CubicTo(double X1, double Y1, double X2, double Y2, double X, double Y) : PathSegment
{
    /// <inheritdoc/>
    public override IEnumerable<(double X, double Y)> Points => [(this.X1, this.Y1), (this.X2, this.Y2), (this.X, this.Y)];
}

/// <summary>Closes the subpath with a straight line to its start.</summary>
public sealed record ClosePath : PathSegment
{
    /// <inheritdoc/>
    public override IEnumerable<(double X, double Y)> Points => [];
}

/// <summary>Where <see cref="DrawingText.X"/> is on the line.</summary>
public enum TextAnchor
{
    /// <summary>At the start: the text runs right from it.</summary>
    Start,

    /// <summary>In the middle.</summary>
    Middle,

    /// <summary>At the end.</summary>
    End,
}

/// <summary>
/// One line of text in the brand's diagram font (its table font, else its text font), placed by the generator in
/// the real font. Widths are measured when the drawing is placed, so text is never laid out in pixels here.
/// </summary>
/// <param name="X">The anchor point, see <see cref="Anchor"/>.</param>
/// <param name="Baseline">The baseline.</param>
/// <param name="Spans">The text, in spans of one weight.</param>
/// <param name="Size">The font size in pixels.</param>
/// <param name="Color">The text colour.</param>
public sealed record DrawingText(double X, double Baseline, IReadOnlyList<DrawingSpan> Spans, double Size, string Color) : DrawingItem
{
    /// <summary>A line in one weight.</summary>
    public DrawingText(double x, double baseline, string text, double size, string color, bool bold = false)
        : this(x, baseline, [new DrawingSpan(text, bold)], size, color)
    {
    }

    /// <summary>The whole line.</summary>
    public string Text => string.Concat(this.Spans.Select(s => s.Text));

    /// <summary>Where <see cref="X"/> is on the line. Default <see cref="TextAnchor.Start"/>.</summary>
    public TextAnchor Anchor { get; init; }

    /// <summary>
    /// The room the line has, in pixels: wider text is drawn smaller, down to 70 % of <see cref="Size"/>, and
    /// overflows beyond that. <see langword="null"/>: never shrunk.
    /// </summary>
    public double? MaxWidth { get; init; }

    /// <summary>Whether the line is in the mono font, like code.</summary>
    public bool Mono { get; init; }

    /// <inheritdoc/>
    public bool Equals(DrawingText? other) =>
        other is not null && this.X == other.X && this.Baseline == other.Baseline && this.Spans.SequenceEqual(other.Spans)
        && this.Size == other.Size && this.Color == other.Color && this.Anchor == other.Anchor
        && this.MaxWidth == other.MaxWidth && this.Mono == other.Mono;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.X, this.Baseline, this.Text, this.Size, this.Color);
}

/// <summary>A piece of a <see cref="DrawingText"/> in one weight.</summary>
/// <param name="Text">The text.</param>
/// <param name="Bold">Whether it is bold.</param>
public sealed record DrawingSpan(string Text, bool Bold = false)
{
    /// <summary>The span's colour; <see langword="null"/>: the <see cref="DrawingText.Color"/> of its line.</summary>
    public string? Color { get; init; }
}
