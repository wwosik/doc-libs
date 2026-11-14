using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Scene slides (<c>::: scene</c>): every shape a top-level <c>p:sp</c> named <c>!!name</c>, so that Morph matches it
/// on the next slide, at its place on a 160 × 90 grid over the whole slide; every connector a <c>p:cxnSp</c> glued to
/// the connection sites of the facing sides, its label a text shape of its own; steps and exits as builds.
/// </summary>
internal sealed partial class DeckWriter
{
    private const long SceneInsetX = 45720;
    private const long SceneInsetY = 22860;
    private const long SceneLine = 12700;
    private const long SceneConnector = 19050;
    private const long SceneLabelGap = 50800;
    private const int SceneMinScale = 70;
    private const int SceneScaleStep = 5;

    private static readonly SlideColor Black = new("000000");

    private Canvas WriteSceneSlide(SceneSlide slide)
    {
        var layout = this.LayoutOf(slide, this.titleOnlyLayout);
        var canvas = this.NewSlide(layout);
        this.Title(canvas, slide.Title);
        var scene = slide.Scene;
        var (unitX, unitY) = (this.slideWidth / (double)Scene.Columns, this.slideHeight / (double)Scene.Rows);
        var ids = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var shape in scene.Shapes)
        {
            var box = new Rect((long)Math.Round(shape.X * unitX), (long)Math.Round(shape.Y * unitY), (long)Math.Round(shape.W * unitX), (long)Math.Round(shape.H * unitY));
            var element = this.SceneShape(canvas, shape, box, Plain(slide.Title));
            ids[shape.Name] = element.NonVisualShapeProperties!.NonVisualDrawingProperties!.Id!.Value;
            canvas.Add(element);
            SceneBuild(canvas, ids[shape.Name], TargetKind.Shape, shape.Style);
        }

        foreach (var connector in scene.Connectors)
        {
            var (fromSide, toSide) = scene.Sides(connector);
            var (from, to) = (scene.Shape(connector.From), scene.Shape(connector.To));
            var start = Emu(Scene.Point(from, fromSide), unitX, unitY);
            var end = Emu(Scene.Point(to, toSide), unitX, unitY);
            var id = canvas.NextId();
            var colour = this.SceneLineColour(connector.Style.Role);
            var line = new A.Outline(colour.Fill()) { Width = (int)SceneConnector };
            if (connector.Arrow)
            {
                line.Append(new A.TailEnd { Type = A.LineEndValues.Triangle });
            }

            var transform = new A.Transform2D(
                new A.Offset { X = Math.Min(start.X, end.X), Y = Math.Min(start.Y, end.Y) },
                new A.Extents { Cx = Math.Abs(end.X - start.X), Cy = Math.Abs(end.Y - start.Y) });
            if (end.X < start.X)
            {
                transform.HorizontalFlip = true;
            }

            if (end.Y < start.Y)
            {
                transform.VerticalFlip = true;
            }

            canvas.Add(new P.ConnectionShape(
                new P.NonVisualConnectionShapeProperties(
                    new P.NonVisualDrawingProperties { Id = id, Name = "!!" + connector.Name },
                    new P.NonVisualConnectorShapeDrawingProperties(
                        new A.StartConnection { Id = ids[connector.From], Index = Site(from.Kind, fromSide) },
                        new A.EndConnection { Id = ids[connector.To], Index = Site(to.Kind, toSide) }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(transform, new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.StraightConnector1 }, line)));
            SceneBuild(canvas, id, TargetKind.Other, connector.Style);

            if (connector.Label is { } label)
            {
                var size = connector.Style.Points * 100;
                var lines = label.Split('\n');
                var width = (long)(lines.Max(l => TextMetrics.Width(l, size, connector.Style.Bold)) * 1.1) + (2 * SceneInsetX);
                var height = (lines.Length * LineHeight(size)) + (2 * SceneInsetY);
                var (x, y) = Scene.LabelAt((start.X, start.Y), (end.X, end.Y), width, height, SceneLabelGap);
                var box = new Rect(
                    Math.Clamp((long)x, 0, Math.Max(this.slideWidth - width, 0)),
                    Math.Clamp((long)y, 0, Math.Max(this.slideHeight - height, 0)),
                    width,
                    height);
                var text = this.SceneText(canvas, label, connector.Style, size, this.palette.Text);
                var labelShape = SceneElement(canvas, "!!" + connector.Name + "-label", box, null, null, null, text, textBox: true);
                canvas.Add(labelShape);
                SceneBuild(canvas, labelShape.NonVisualShapeProperties!.NonVisualDrawingProperties!.Id!.Value, TargetKind.Shape, connector.Style);
            }
        }

        return canvas;
    }

    // A shape with its label, shrunk to fit down to 70 %; a label that does not fit even then is reported.
    private P.Shape SceneShape(Canvas canvas, SceneNode shape, Rect box, string title)
    {
        var (fill, line, ink) = this.SceneColours(shape);
        A.Paragraph? text = null;
        if (shape.Label is { } label)
        {
            var lines = label.Split('\n');
            var start = shape.Style.Points * 100;
            var (innerWidth, innerHeight) = (box.W - (2 * SceneInsetX), box.H - (2 * SceneInsetY));
            bool Fits(int size) =>
                lines.Max(l => TextMetrics.Width(l, size, shape.Style.Bold)) <= innerWidth && lines.Length * LineHeight(size) <= innerHeight;
            var size = start;
            for (var scale = 100; scale >= SceneMinScale; scale -= SceneScaleStep)
            {
                size = (int)(Math.Round(start * scale / 100.0 / 50.0) * 50);
                if (Fits(size))
                {
                    break;
                }
            }

            if (!Fits(size))
            {
                this.notes.Add(
                    NoteCodes.SlideOverfull,
                    shape.Line,
                    $"The label '{GenerationNotes.Quote(label)}' of the shape '{shape.Name}' on the slide '{title}' does not fit the shape even at 70 %. Make the shape larger or the label shorter.");
            }

            text = this.SceneText(canvas, label, shape.Style, size, ink);
        }

        var preset = shape.Kind switch
        {
            SceneKind.Round => A.ShapeTypeValues.RoundRectangle,
            SceneKind.Ellipse => A.ShapeTypeValues.Ellipse,
            _ => A.ShapeTypeValues.Rectangle,
        };
        return SceneElement(canvas, "!!" + shape.Name, box, preset, fill, line, text, textBox: shape.Kind == SceneKind.Text);
    }

    private static P.Shape SceneElement(Canvas canvas, string name, Rect box, A.ShapeTypeValues? preset, SlideColor? fill, SlideColor? line, A.Paragraph? text, bool textBox)
    {
        var id = canvas.NextId();
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                textBox ? new P.NonVisualShapeDrawingProperties { TextBox = true } : new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                box.Transform(),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = preset ?? A.ShapeTypeValues.Rectangle },
                fill is { } color ? color.Fill() : new A.NoFill(),
                line is { } stroke ? new A.Outline(stroke.Fill()) { Width = (int)SceneLine } : new A.Outline(new A.NoFill())),
            new P.TextBody(
                new A.BodyProperties(new A.NoAutoFit())
                {
                    Wrap = A.TextWrappingValues.None,
                    LeftInset = (int)SceneInsetX,
                    RightInset = (int)SceneInsetX,
                    TopInset = (int)SceneInsetY,
                    BottomInset = (int)SceneInsetY,
                    Anchor = A.TextAnchoringTypeValues.Center,
                },
                new A.ListStyle(),
                text ?? new A.Paragraph(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center }, new A.EndParagraphRunProperties())));
    }

    // One centred paragraph, its lines broken where the label has `\n`.
    private A.Paragraph SceneText(Canvas canvas, string label, SceneStyle style, int size, SlideColor? colour)
    {
        var paragraph = new A.Paragraph(new A.ParagraphProperties(new A.NoBullet()) { Alignment = A.TextAlignmentTypeValues.Center, LeftMargin = 0, Indent = 0 });
        foreach (var (index, line) in label.Split('\n').Index())
        {
            if (index > 0)
            {
                paragraph.Append(new A.Break(new A.RunProperties { FontSize = size }));
            }

            if (line.Length > 0)
            {
                paragraph.Append(this.Run(canvas, new TextRun(line, style.Bold ? RunStyle.Bold : RunStyle.None), size, color: colour));
            }
        }

        paragraph.Append(new A.EndParagraphRunProperties { FontSize = size });
        return paragraph;
    }

    // The fill, line and text colour of a shape: by default a light surface with a thin primary line; a role fills it
    // with white or dark text by contrast; a text is coloured by its role.
    private (SlideColor? Fill, SlideColor? Line, SlideColor? Ink) SceneColours(SceneNode shape)
    {
        var text = this.palette.Text;
        var role = shape.Style.Role;
        if (shape.Kind == SceneKind.Text)
        {
            return (null, null, role is SceneRole.Default or SceneRole.Outline ? text
                : shape.Style.Category is { } category ? this.palette.WordColour(category)
                : role == SceneRole.Muted ? this.palette.WordColour(LabelCategory.Neutral)
                : this.SceneFill(role));
        }

        return role switch
        {
            SceneRole.Default => (this.palette.Surface, this.palette.Primary, text),
            SceneRole.Outline => (null, this.palette.Primary, text),
            SceneRole.Muted => (this.palette.LabelFill(LabelCategory.Neutral), null, text),
            _ => (this.SceneFill(role), null, this.InkOn(this.SceneFill(role))),
        };
    }

    private SlideColor SceneFill(SceneRole role) => role switch
    {
        SceneRole.Accent => this.palette.Accent,
        SceneRole.Highlight => this.palette.Highlight,
        SceneRole.Dark => this.palette.Dark,
        SceneRole.Muted => new SlideColor(this.palette.Labels[LabelCategory.Neutral]),
        SceneRole.Success => new SlideColor(this.palette.Labels[LabelCategory.Success]),
        SceneRole.Info => new SlideColor(this.palette.Labels[LabelCategory.Info]),
        SceneRole.Warning => new SlideColor(this.palette.Labels[LabelCategory.Warning]),
        SceneRole.Danger => new SlideColor(this.palette.Labels[LabelCategory.Danger]),
        SceneRole.Neutral => new SlideColor(this.palette.Labels[LabelCategory.Neutral]),
        _ => this.palette.Primary,
    };

    private SlideColor SceneLineColour(SceneRole role) =>
        role is SceneRole.Default or SceneRole.Outline ? this.palette.Primary : this.SceneFill(role);

    // White on a dark or theme colour, dark on a light one; translucent fills are on dark slides, which take white.
    private SlideColor? InkOn(SlideColor fill) =>
        fill is { Hex: { } hex, Alpha: 100000 } && SceneDrawing.Ink(hex, "000000") != "FFFFFF"
            ? this.palette.DarkSlides ? Black : this.palette.Text
            : SlideColor.White;

    // The connection site of a side: rectangles have four (top, left, bottom, right), ellipses eight from the top.
    private static uint Site(SceneKind kind, SceneSide side) =>
        kind == SceneKind.Ellipse ? (uint)side * 2 : (uint)side;

    private static (long X, long Y) Emu((double X, double Y) point, double unitX, double unitY) =>
        ((long)Math.Round(point.X * unitX), (long)Math.Round(point.Y * unitY));

    /// <summary>
    /// The rows of the scene grid the title of the template's title-only layout takes (its bottom, rounded up), for the
    /// guide; <see langword="null"/> when the template cannot be read without consuming it, or has no such title.
    /// </summary>
    internal static int? SceneTitleRows(PptxTemplateOptions template)
    {
        byte[] bytes;
        using (var copy = new MemoryStream())
        {
            if (template is CustomTemplateOptions custom)
            {
                if (custom.Template is not { CanSeek: true } stream)
                {
                    return null;
                }

                var position = stream.Position;
                stream.CopyTo(copy);
                stream.Position = position;
            }
            else
            {
                template.Write(copy);
            }

            bytes = copy.ToArray();
        }

        try
        {
            using var package = PresentationDocument.Open(new MemoryStream(bytes), isEditable: false);
            var height = package.PresentationPart?.Presentation?.SlideSize?.Cy?.Value ?? 6858000;
            var layouts = package.PresentationPart?.SlideMasterParts.SelectMany(m => m.SlideLayoutParts).ToList() ?? [];
            var layout = (template.LayoutNames?.TitleOnly is { } name ? layouts.FirstOrDefault(l => LayoutName(l) == name) : null)
                ?? ByType(layouts, P.SlideLayoutValues.TitleOnly)
                ?? layouts.FirstOrDefault(l => Placeholders(l).Any(IsTitle) && !Placeholders(l).Any(IsBody));
            var title = layout is null ? null
                : Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, IsTitle) ?? Bounds(layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, IsTitle);
            return title is { } box && height > 0 ? (int)Math.Ceiling((box.Bottom * (double)Scene.Rows / height) - 1e-9) : null;
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or IOException)
        {
            return null;
        }
    }

    // A step brings the item in on its click with its effect, an exit takes it away on its click; equal numbers play together.
    private static void SceneBuild(Canvas canvas, uint id, TargetKind kind, SceneStyle style)
    {
        if (style.Step is { } step)
        {
            canvas.Target(new BuildTarget(new BuildMark([new Build(BuildKind.Entrance, style.Effect, ByItem: false)], step) { Joined = true }, 0, id, kind, null));
        }

        if (style.Exit is { } exit)
        {
            canvas.Target(new BuildTarget(new BuildMark([new Build(BuildKind.Exit, BuildEffect.Disappear, ByItem: false)], exit) { Joined = true }, 0, id, kind, null));
        }
    }
}
