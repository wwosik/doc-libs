using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The image attributes in Word, as in PDFs: <c>fit=cover</c> in a row of images fills the row's height, cropped by
/// <c>a:srcRect</c> around <c>focus</c>; <c>.round</c> crops to a square by <c>focus</c> and draws an ellipse (the pixels
/// stay whole, so resetting the picture shows them all); <c>.grey</c> embeds the pixels in grey (<see cref="GreyImages"/>,
/// SkiaSharp, loaded only then), converted once per image and embedded once per part. A grey SVG is only its grey PNG.
/// </summary>
internal sealed partial class DocxBody
{
    private readonly Dictionary<(OpenXmlPart, DocumentResource), (string Id, ImageInfo Info)> greyParts = [];
    private readonly Dictionary<DocumentResource, (DocumentResource Image, ImageInfo Info)> greyImages = [];

    // An image read from the Markdown, not yet placed.
    private sealed record MarkdownImage(int Line, DocumentResource Resource, ImageAttributes Attributes, double Scale, string Alt, string? Caption);

    // An image of its own: fitted as before, a round one as the square in it. It has no box to fill, so `fit=cover`
    // does nothing.
    private (Run Picture, string? Caption) SinglePicture(MarkdownImage image, double maxWidth, OpenXmlPart part)
    {
        if (image.Attributes.Fit == ImageFit.Cover)
        {
            this.Ignored(image, "fit=cover", "an image of its own in a Word document has no box to fill, so it is never cropped; put it in a row of images");
        }

        this.FocusNote(image);
        var (id, info, svg) = this.ImagePart(image, part);
        var (width, height) = Natural(image, info);
        var (cx, cy) = this.Fit(width, height, maxWidth);
        return (this.Placed(image, info, id, svg, cx, cy), image.Caption);
    }

    // Images in cells of `maxWidths` (EMU). Those with `fit=cover` fill their cell at the row's height: that of its
    // tallest other image, or 3/4 of the cell's width when all of them cover; round ones are the largest circle in that box.
    private List<(Run Picture, string? Caption)> RowPictures(List<Markdig.Syntax.Inlines.LinkInline> links, List<double> maxWidths, OpenXmlPart part)
    {
        var images = links.Select(this.ImageOf).ToList();
        var parts = images.Select(image => this.ImagePart(image, part)).ToList();
        var sizes = images.Select((image, i) => this.Fit(Natural(image, parts[i].Info).Width, Natural(image, parts[i].Info).Height, maxWidths[i])).ToList();
        bool Covers(int i) => images[i].Attributes.Fit == ImageFit.Cover;
        foreach (var image in images)
        {
            if (image.Attributes is { Fit: ImageFit.Cover, Scale: not null })
            {
                this.Ignored(image, "scale", "in a row, fit=cover gives the image the row's height");
            }

            this.FocusNote(image);
        }

        if (Enumerable.Range(0, images.Count).Any(Covers))
        {
            var others = Enumerable.Range(0, images.Count).Where(i => !Covers(i)).Select(i => (double)sizes[i].Cy).ToList();
            var height = Math.Min(others.Count > 0 ? others.Max() : maxWidths.Min() * 0.75, this.TextHeight * 0.8);
            for (var i = 0; i < images.Count; i++)
            {
                if (Covers(i))
                {
                    var side = (long)Math.Min(maxWidths[i], height);
                    sizes[i] = images[i].Attributes.Round ? (side, side) : ((long)maxWidths[i], (long)height);
                }
            }
        }

        return [.. images.Select((image, i) => (this.Placed(image, parts[i].Info, parts[i].Id, parts[i].Svg, sizes[i].Cx, sizes[i].Cy), image.Caption))];
    }

    // The picture at `cx` × `cy` (EMU): cropped to that shape when it covers or is round, an ellipse when round.
    private Run Placed(MarkdownImage image, ImageInfo info, string id, string? svg, long cx, long cy)
    {
        var attributes = image.Attributes;
        var crop = attributes.Fit == ImageFit.Cover || attributes.Round ? attributes.Crop(info.Width, info.Height, (double)cx / cy) : default;
        return new Run(this.Picture(id, cx, cy, image.Alt, svg, crop, attributes.Round));
    }

    // The natural size in pixels times the scale; a round image is the square of its shorter side.
    private static (double Width, double Height) Natural(MarkdownImage image, ImageInfo info)
    {
        var (width, height) = image.Attributes.Round
            ? (Math.Min(info.Width, info.Height), Math.Min(info.Width, info.Height))
            : (info.Width, info.Height);
        return (width * image.Scale, height * image.Scale);
    }

    // `focus` only matters when the image is cropped.
    private void FocusNote(MarkdownImage image)
    {
        if (image.Attributes is { Focus: not null, Fit: ImageFit.Contain, Round: false })
        {
            this.Ignored(image, "focus", "the image is not cropped; add .round, or fit=cover in a row of images");
        }
    }

    // An image attribute that does nothing where it is.
    private void Ignored(MarkdownImage image, string name, string reason) =>
        this.Context.Notes.Add(NoteCodes.AttributeIgnored, image.Line, $"The image attribute {name} in '{image.Attributes.Text}' has no effect: {reason}.");

    // The image's part: as it is, or with `.grey` in grey.
    private (string Id, ImageInfo Info, string? Svg) ImagePart(MarkdownImage image, OpenXmlPart part)
    {
        if (!image.Attributes.Grey)
        {
            return this.ImagePart(image.Resource, image.Scale, part);
        }

        if (SvgBlip.IsSvg(image.Resource))
        {
            this.Context.Notes.Add(
                NoteCodes.AttributeIgnored,
                image.Line,
                $"The image attribute .grey in '{image.Attributes.Text}' keeps only the grey PNG of the SVG '{image.Resource.Path}': Word gets no SVG of a grey image.");
        }

        if (!this.greyParts.TryGetValue((part, image.Resource), out var known))
        {
            if (!this.greyImages.TryGetValue(image.Resource, out var grey))
            {
                var (pixels, info) = this.Pixels(image.Resource, image.Scale);
                var converted = GreyImages.Convert(pixels, info.Format, Format.Docx);
                grey = (converted, info with { Format = info.Format == ImageFormat.Jpeg ? ImageFormat.Jpeg : ImageFormat.Png });
                this.greyImages[image.Resource] = grey;
            }

            known = (AddImage(part, PartType(grey.Info.Format), grey.Image.Content), grey.Info);
            this.greyParts[(part, image.Resource)] = known;
        }

        return (known.Id, known.Info, null);
    }

    // The crop as `a:srcRect`, in thousandths of a percent of each side; nothing when nothing is cut off.
    private static string SourceRectangle(ImageCrop crop)
    {
        if (crop.IsNone)
        {
            return string.Empty;
        }

        static string Share(double share) => ((int)Math.Round(share * 100000)).ToString(CultureInfo.InvariantCulture);
        return $"<a:srcRect l=\"{Share(crop.Left)}\" t=\"{Share(crop.Top)}\" r=\"{Share(crop.Right)}\" b=\"{Share(crop.Bottom)}\"/>";
    }
}
