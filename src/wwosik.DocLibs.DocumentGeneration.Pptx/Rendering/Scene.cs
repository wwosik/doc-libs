using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

// The model a deck is read into (DeckReader) and drawn from (SceneSvg): positioned shapes, pictures and text already
// broken into lines, in EMU. Everything PowerPoint inherits is resolved; the backends know four item kinds only.

/// <summary>A deck: its slide size in EMU and its visible slides in order.</summary>
internal sealed record DeckScene(long Width, long Height, IReadOnlyList<SlideScene> Slides, string? Title, string? Language, IReadOnlyList<SceneFace> Faces);

/// <summary>
/// One visible slide. <paramref name="Number"/> is its position in the deck, hidden slides counted. The first
/// <see cref="Decorations"/> items come from the master and the layout, the rest from the slide's own shape tree.
/// </summary>
internal sealed record SlideScene(int Number, string Title, SceneFill? Background, IReadOnlyList<SceneItem> Items)
{
    /// <summary>How many of <see cref="Items"/>, from the start, are master and layout decorations.</summary>
    public int Decorations { get; init; }

    /// <summary>The speaker notes (the notes page's body placeholder); empty without notes.</summary>
    public IReadOnlyList<NoteParagraph> Notes { get; init; } = [];

    /// <summary>The build steps of the main click sequence, in order; empty without builds.</summary>
    public IReadOnlyList<SceneStep> Steps { get; init; } = [];

    /// <summary>The transition into this slide; <see langword="null"/> for none.</summary>
    public SceneTransition? Transition { get; init; }

    /// <summary>The Markdown line the slide was generated from (<see cref="SourceLines"/>); 0 if the slide does not say.</summary>
    public int Line { get; init; }

    /// <summary>Whether the slide is in the deck's backup: the slide named <c>Backup</c> and every one after it (E06-S07).</summary>
    public bool Backup { get; init; }
}

/// <summary>Whether a paragraph of the speaker notes is a list item with a bullet or a number.</summary>
internal enum NoteList
{
    None,
    Bullet,
    Number,
}

/// <summary>A paragraph of the speaker notes: its level (from 0), list kind and first number, and its runs.</summary>
internal sealed record NoteParagraph(int Level, NoteList List, int Start, IReadOnlyList<NoteRun> Runs)
{
    public bool Equals(NoteParagraph? other) =>
        other is not null && this.Level == other.Level && this.List == other.List && this.Start == other.Start && this.Runs.SequenceEqual(other.Runs);

    public override int GetHashCode() => HashCode.Combine(this.Level, this.List, this.Start, this.Runs.Count);
}

/// <summary>A piece of speaker-note text; a text of <c>"\n"</c> is a line break. <paramref name="Link"/> is a web or mail address.</summary>
internal sealed record NoteRun(string Text, bool Bold, bool Italic, string? Link);

/// <summary>What a build effect does to its target: brings it in, takes it out, or draws attention to it.</summary>
internal enum EffectClass
{
    Entrance,
    Exit,
    Emphasis,
}

/// <summary>How a build effect plays; an exit plays its entrance in reverse, and <see cref="Appear"/> is instant.</summary>
internal enum EffectKind
{
    Appear,
    Fade,
    Fly,
    Wipe,
    Pulse,
}

/// <summary>
/// One effect of a build step: on the item with that <c>cNvPr id</c>, or on a range of its paragraphs (indices, both
/// included); the side it comes from or goes to (<c>l</c>, <c>r</c>, <c>t</c>, <c>b</c>, or two of them) and its
/// duration in milliseconds.
/// </summary>
internal sealed record SceneEffect(int Id, (int Start, int End)? Paragraphs, EffectClass Class, EffectKind Kind, string? Direction, int Duration);

/// <summary>One click of the main sequence: the effects that start with it.</summary>
internal sealed record SceneStep(IReadOnlyList<SceneEffect> Effects)
{
    public bool Equals(SceneStep? other) => other is not null && this.Effects.SequenceEqual(other.Effects);

    public override int GetHashCode() => this.Effects.Count;
}

/// <summary>
/// The transition into a slide: <c>fade</c>, <c>push</c>, <c>wipe</c>, <c>cover</c>, <c>uncover</c>, <c>split</c>,
/// <c>zoom</c>, <c>cut</c> or <c>morph</c>; its direction as PowerPoint writes it (<c>l</c>, <c>u</c>, <c>r</c>,
/// <c>d</c> and their pairs; <c>horz-out</c>, <c>vert-in</c> … for split; <c>in</c> or <c>out</c> for zoom) and its
/// duration in milliseconds.
/// </summary>
internal sealed record SceneTransition(string Kind, string? Direction, int Duration);

/// <summary>A colour with its opacity (0–1).</summary>
internal readonly record struct Rgba(byte R, byte G, byte B, double A = 1)
{
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba White = new(255, 255, 255);

    public string Hex => $"{this.R:X2}{this.G:X2}{this.B:X2}";

    public static Rgba Parse(string hex) =>
        hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? new Rgba((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Black;
}

/// <summary>An affine transform: <c>x' = A·x + C·y + E</c>, <c>y' = B·x + D·y + F</c>.</summary>
internal readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
{
    public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => this == Identity;

    public static Matrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Matrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>A rotation by <paramref name="degrees"/> clockwise (y down) around (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static Matrix Rotate(double degrees, double x, double y)
    {
        var radians = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        return Translate(x, y) * new Matrix(cos, sin, -sin, cos, 0, 0) * Translate(-x, -y);
    }

    /// <summary><paramref name="outer"/> applied after <paramref name="inner"/>.</summary>
    public static Matrix operator *(Matrix outer, Matrix inner) => new(
        (outer.A * inner.A) + (outer.C * inner.B),
        (outer.B * inner.A) + (outer.D * inner.B),
        (outer.A * inner.C) + (outer.C * inner.D),
        (outer.B * inner.C) + (outer.D * inner.D),
        (outer.A * inner.E) + (outer.C * inner.F) + outer.E,
        (outer.B * inner.E) + (outer.D * inner.F) + outer.F);

    public (double X, double Y) Apply(double x, double y) => ((this.A * x) + (this.C * y) + this.E, (this.B * x) + (this.D * y) + this.F);
}

/// <summary>A picture's bytes; one instance per image part, so a picture used on several slides is the same object.</summary>
internal sealed class SceneImage(byte[] data, string contentType)
{
    public byte[] Data { get; } = data;

    public string ContentType { get; } = contentType;
}

internal abstract record SceneFill;

internal sealed record SolidFill(Rgba Color) : SceneFill;

/// <summary>A gradient: stops at 0–1, <paramref name="Angle"/> in degrees clockwise from left-to-right; radial for <c>path</c> gradients.</summary>
internal sealed record GradientFill(IReadOnlyList<GradientStop> Stops, double Angle, bool Radial) : SceneFill
{
    public bool Equals(GradientFill? other) =>
        other is not null && this.Angle == other.Angle && this.Radial == other.Radial && this.Stops.SequenceEqual(other.Stops);

    public override int GetHashCode() => HashCode.Combine(this.Angle, this.Radial, this.Stops.Count);
}

internal readonly record struct GradientStop(double Position, Rgba Color);

/// <summary>A picture stretched over the area (a <c>blipFill</c> background or shape fill).</summary>
internal sealed record ImageFill(SceneImage Image) : SceneFill;

/// <summary>An outline: width in EMU, dashes in multiples of the width, joins and caps by their SVG names.</summary>
internal sealed record SceneStroke(double Width, Rgba Color, IReadOnlyList<double>? Dashes, string Join, string Cap, LineEnd? Head, LineEnd? Tail)
{
    public bool Equals(SceneStroke? other) =>
        other is not null && this.Width == other.Width && this.Color == other.Color && this.Join == other.Join && this.Cap == other.Cap
        && this.Head == other.Head && this.Tail == other.Tail
        && (this.Dashes ?? []).SequenceEqual(other.Dashes ?? []);

    public override int GetHashCode() => HashCode.Combine(this.Width, this.Color, this.Join, this.Cap);
}

/// <summary>An arrow head: <c>triangle</c> or <c>arrow</c>, sizes <c>sm</c>, <c>med</c>, <c>lg</c>.</summary>
internal sealed record LineEnd(string Type, string Width, string Length);

internal abstract record PathSegment;

internal sealed record PathMove(double X, double Y) : PathSegment;

internal sealed record PathLine(double X, double Y) : PathSegment;

internal sealed record PathCubic(double X1, double Y1, double X2, double Y2, double X, double Y) : PathSegment;

internal sealed record PathClose : PathSegment
{
    public static readonly PathClose Instance = new();
}

/// <summary>One path of a shape's geometry, in the shape's own coordinates (EMU, 0 to its width and height).</summary>
internal sealed record ScenePath(IReadOnlyList<PathSegment> Segments, SceneFill? Fill, SceneStroke? Stroke);

/// <summary>Something drawn, with its <c>cNvPr id</c> and name, so builds and Morph can address it.</summary>
internal abstract record SceneItem(int Id, string Name);

internal sealed record SceneGroup(int Id, string Name, IReadOnlyList<SceneItem> Items) : SceneItem(Id, Name);

/// <summary>
/// A shape: its paths and text in its own coordinates, <paramref name="Transform"/> to the slide (flips included), and
/// <paramref name="TextTransform"/> for the text (rotation only: PowerPoint does not mirror text).
/// </summary>
internal sealed record SceneShape(int Id, string Name, Matrix Transform, IReadOnlyList<ScenePath> Paths, SceneText? Text, Matrix TextTransform, string? Link)
    : SceneItem(Id, Name);

/// <summary>
/// A picture over (0, 0, <paramref name="Width"/>, <paramref name="Height"/>) in its own coordinates; <paramref name="Crop"/>
/// is the <c>srcRect</c> in fractions (left, top, right, bottom), <paramref name="Clip"/> the geometry when it is not a rectangle.
/// </summary>
internal sealed record ScenePicture(
    int Id,
    string Name,
    Matrix Transform,
    double Width,
    double Height,
    SceneImage Image,
    SceneImage? Svg,
    (double Left, double Top, double Right, double Bottom) Crop,
    IReadOnlyList<PathSegment>? Clip,
    bool Grey,
    string? Link) : SceneItem(Id, Name);

/// <summary>Text broken into lines; each paragraph keeps its index in the shape for paragraph builds.</summary>
internal sealed record SceneText(IReadOnlyList<SceneParagraph> Paragraphs);

internal sealed record SceneParagraph(int Index, IReadOnlyList<SceneTextLine> Lines);

/// <summary>A line at its baseline (EMU from the shape's top).</summary>
internal sealed record SceneTextLine(double Baseline, IReadOnlyList<SceneRun> Runs);

/// <summary>
/// A piece of text on one line: x and width in EMU from the shape's left, size in EMU, and the baseline shift up in EMU.
/// A bullet is a run of its own.
/// </summary>
internal sealed record SceneRun(
    double X,
    double Width,
    string Text,
    SceneFont Font,
    double Size,
    Rgba Color,
    bool Underline,
    bool Strike,
    double Shift,
    Rgba? Highlight,
    string? Link);

/// <summary>A font as the deck names it, and the face its text was measured with (<see langword="null"/>: an estimate).</summary>
internal sealed record SceneFont(string Family, bool Bold, bool Italic, SceneFace? Face);

/// <summary>Where a face comes from: embedded in the deck, passed by the caller, or a bundled stand-in.</summary>
internal enum FaceSource
{
    Embedded,
    Passed,
    StandIn,
}

/// <summary>A TrueType face that measured text; one instance per file.</summary>
internal sealed class SceneFace(string family, bool bold, bool italic, byte[] file, FaceSource source, TrueTypeMetrics metrics)
{
    /// <summary>The face's own family name (name ID 1): the deck's family, or the stand-in's.</summary>
    public string Family { get; } = family;

    public bool Bold { get; } = bold;

    public bool Italic { get; } = italic;

    public byte[] File { get; } = file;

    public FaceSource Source { get; } = source;

    public TrueTypeMetrics Metrics { get; } = metrics;
}
