using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

// The cover image (E01-S08) on the deck's title slide: in a picture placeholder the title layout's roles declare, fitted
// into it; else under the subtitle, left-aligned with the title, or right of the title block, top-aligned with the
// title, which is then narrowed to leave it room. It never covers the layout's pictures (logo, artwork, tagline): where
// they are in the way it is made smaller.
internal sealed partial class DeckWriter
{
    private bool coverImageWritten;

    /// <summary>The image for the title slide (<c>coverImage</c>); <see langword="null"/>: none.</summary>
    public CoverImage? CoverImage { get; init; }

    // After the slides: a deck without a title slide reports the image it could not show.
    private void CoverImageLeftOver()
    {
        if (this.CoverImage is { } image && !this.coverImageWritten)
        {
            image.Ignored(this.notes, "the deck has no title slide, as it does not start with a '#' heading");
        }
    }

    private Canvas WithCoverImage(Canvas canvas, TitleSlide slide)
    {
        if (this.CoverImage is not { } image || slide.IsSection || slide.Closing || this.coverImageWritten)
        {
            return canvas;
        }

        this.coverImageWritten = true;
        var layout = canvas.Part.SlideLayoutPart!;
        var prepared = image.Placeable(
            this.options.ImageConverters, this.slideHeight * image.HeightShare / EmuPerPixel, Format.Pptx, ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif);
        var picture = new SlideImage(prepared.Image, Path.GetFileNameWithoutExtension(image.Resource.Path), double.MaxValue)
        {
            Natural = (prepared.Width, prepared.Height),
            Vector = prepared.Svg,
        };
        var ratio = prepared.Width / Math.Max(1, prepared.Height);

        var found = this.roles.Placeholders(layout);
        if (found.Pictures.FirstOrDefault(found.Excluded.Contains) is { } slot)
        {
            var area = this.PlaceholderBox(layout, slot);
            var height = Math.Min(area.H, area.W / ratio);
            this.CoverPicture(canvas, picture, new Rect(area.X, area.Y, (long)(height * ratio), (long)height), slot);
            return canvas;
        }

        var title = this.TitleArea(layout);
        var obstacles = this.CoverObstacles(layout);
        var bottom = this.slideHeight - (this.slideHeight / 20);
        var wanted = this.slideHeight * image.HeightShare;
        var least = this.slideHeight * 0.02;
        if (image.Position == CoverImagePosition.Right)
        {
            var top = this.CoverTitleTop(layout, slide, title);
            if (CoverImage.Place(title.Right, top, fromRight: true, wanted, ratio, title.W * 0.4, bottom, obstacles, least) is { } right)
            {
                var rect = new Rect((long)right.X, (long)right.Y, (long)right.Width, (long)right.Height);
                this.NarrowCoverText(canvas, layout, rect.X - Gap);
                this.Picture(canvas, picture, rect);
                return canvas;
            }
        }
        else if (CoverImage.Place(title.X, this.CoverTextBottom(layout, slide, title) + Gap, fromRight: false, wanted, ratio, title.W, bottom, obstacles, least) is { } below)
        {
            this.Picture(canvas, picture, new Rect((long)below.X, (long)below.Y, (long)below.Width, (long)below.Height));
            return canvas;
        }

        image.Ignored(this.notes, "the title slide has no room left for it");
        return canvas;
    }

    // Where the title slide's text ends: under the subtitle as it is set, else under the title's box.
    private long CoverTextBottom(SlideLayoutPart layout, TitleSlide slide, Rect title)
    {
        if (slide.Subtitle.Count == 0)
        {
            return title.Bottom;
        }

        if (this.CoverSubtitle(layout) is { } subtitle)
        {
            var box = this.PlaceholderBox(layout, subtitle);
            return box.Y + EstimateHeight(slide.Subtitle, this.LevelSize(layout, subtitle, 1) ?? TextSize, box.W);
        }

        return title.Bottom + Gap + EstimateHeight(slide.Subtitle, TextSize, title.W);
    }

    // The placeholder the subtitle goes into, as WriteTitleSlide picks it.
    private P.PlaceholderShape? CoverSubtitle(SlideLayoutPart layout)
    {
        var bodies = this.Bodies(layout).ToList();
        var titleInBody = FindPlaceholder(layout, IsTitle) is null && bodies.Count > 0;
        return FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.SubTitle && !this.roles.IsExcluded(layout, p))
            ?? bodies.Skip(titleInBody ? 1 : 0).FirstOrDefault();
    }

    // The top of the title's text: the top of its box, or for a title anchored at the bottom (or middle) of its box, an
    // estimate of where its lines start, narrowed as they will be.
    private long CoverTitleTop(SlideLayoutPart layout, TitleSlide slide, Rect title)
    {
        var shape = FindPlaceholder(layout, IsTitle)?.Parent?.Parent?.Parent as P.Shape;
        var anchor = shape?.TextBody?.BodyProperties?.Anchor?.Value;
        if (anchor is null || anchor == A.TextAnchoringTypeValues.Top)
        {
            return title.Y;
        }

        var size = shape?.TextBody?.ListStyle?.Level1ParagraphProperties?.GetFirstChild<A.DefaultRunProperties>()?.FontSize?.Value
            ?? layout.SlideMasterPart?.SlideMaster?.TextStyles?.TitleStyle?.Level1ParagraphProperties?.GetFirstChild<A.DefaultRunProperties>()?.FontSize?.Value
            ?? 4400;
        var height = EstimateLines(slide.Title, size, (long)(title.W * 0.6) - Gap);
        return Math.Max(title.Y, anchor == A.TextAnchoringTypeValues.Bottom ? title.Bottom - height : title.Y + ((title.H - height) / 2));
    }

    // The title and subtitle on the slide end at `right`, so that they wrap short of the image.
    private void NarrowCoverText(Canvas canvas, SlideLayoutPart layout, long right)
    {
        var subtitle = this.CoverSubtitle(layout);
        foreach (var shape in canvas.Part.Slide!.CommonSlideData!.ShapeTree!.Elements<P.Shape>())
        {
            var placeholder = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
            var layoutPlaceholder = placeholder is null ? null
                : FindPlaceholder(layout, p => IsTitle(p) ? placeholder.Type is { } t && (t.Value == P.PlaceholderValues.Title || t.Value == P.PlaceholderValues.CenteredTitle)
                    : p.Index?.Value is { } index ? placeholder.Index?.Value == index : p.Type?.Value == placeholder.Type?.Value);
            var isText = shape.NonVisualShapeProperties?.NonVisualShapeDrawingProperties?.TextBox?.Value == true;
            if (!isText && (layoutPlaceholder is null || !(IsTitle(layoutPlaceholder) || ReferenceEquals(layoutPlaceholder, subtitle))))
            {
                continue;
            }

            var own = shape.ShapeProperties?.Transform2D;
            var box = own?.Offset is { X: { } x, Y: { } y } && own.Extents is { Cx: { } cx, Cy: { } cy }
                ? new Rect(x.Value, y.Value, cx.Value, cy.Value)
                : layoutPlaceholder is null ? (Rect?)null : this.PlaceholderBox(layout, layoutPlaceholder);
            if (box is { } current && current.Right > right && right > current.X)
            {
                shape.ShapeProperties ??= new P.ShapeProperties();
                shape.ShapeProperties.Transform2D = (current with { W = right - current.X }).Transform();
            }
        }
    }

    // The pictures the layout draws (and its master's, when the layout shows them), with a gap around each.
    private List<(double X, double Y, double Width, double Height)> CoverObstacles(SlideLayoutPart layout)
    {
        var trees = new List<P.ShapeTree?> { layout.SlideLayout?.CommonSlideData?.ShapeTree };
        if (layout.SlideLayout?.ShowMasterShapes?.Value != false)
        {
            trees.Add(layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree);
        }

        return [.. trees.SelectMany(t => t?.Elements<P.Picture>() ?? [])
            .Select(p => p.ShapeProperties?.Transform2D)
            .Where(t => t?.Offset?.X is not null && t.Offset.Y is not null && t.Extents?.Cx is not null && t.Extents.Cy is not null)
            .Select(t => ((double)t!.Offset!.X!.Value - Gap, (double)t.Offset.Y!.Value - Gap, (double)t.Extents!.Cx!.Value + (2 * Gap), (double)t.Extents.Cy!.Value + (2 * Gap)))];
    }

    // In a picture placeholder: fitted into its box, not cropped.
    private void CoverPicture(Canvas canvas, SlideImage image, Rect box, P.PlaceholderShape placeholder)
    {
        var info = ImageInfo.Read(image.Resource.Content.Span)!.Value;
        var relationshipId = this.ImageRelationship(canvas, image, info);
        var id = canvas.NextId();
        canvas.Add(new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Picture " + id, Description = XmlSafe(image.AltText) },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoGrouping = true, NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties(Reference(placeholder))),
            BlipFill(this.Blip(canvas, image, relationshipId), default),
            new P.ShapeProperties(box.Transform())));
    }
}
