namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>The cover image as the layout places it: its PNG or JPEG, its format and its width over its height.</summary>
internal sealed record PdfCoverImage(CoverImage Spec, ReadOnlyMemory<byte> Content, ImageFormat Format, double Ratio);

// The cover image (E01-S08): under the subtitle, left-aligned with the title, or right of the title block, top-aligned
// with the title, which then wraps short of it. It never covers the logo, the artwork or the tagline: where they are in
// the way it is made smaller.
internal sealed partial class LayoutEngine
{
    private const double CoverImageGap = 14;

    // Right of the title: placed now, before the title is set; returns the width the title and subtitle keep.
    private double CoverImageRight(double width)
    {
        if (this.design.CoverImage is not { Spec.Position: CoverImagePosition.Right } image)
        {
            return width;
        }

        var g = this.geometry;
        var top = this.y + (CoverTitleSize * (1.05 - CapHeight)); // the top of the title's capitals
        var placed = CoverImage.Place(
            g.Right, top, fromRight: true, g.Height * image.Spec.HeightShare, image.Ratio, g.ContentWidth * 0.4, g.Bottom, this.CoverObstacles(), g.Height * 0.02);
        if (placed is not { } box)
        {
            image.Spec.Ignored(this.notes, "the cover has no room left for it");
            return width;
        }

        this.Page.Items.Add(new ImageItem(image, image.Content, image.Format, box.X, box.Y, box.Width, box.Height));
        return Math.Min(width, box.X - CoverImageGap - g.Left);
    }

    // Under the subtitle, where the cover's text ends.
    private void CoverImageBelow()
    {
        if (this.design.CoverImage is not { Spec.Position: CoverImagePosition.Below } image)
        {
            return;
        }

        var g = this.geometry;
        var placed = CoverImage.Place(
            g.Left, this.y + CoverImageGap, fromRight: false, g.Height * image.Spec.HeightShare, image.Ratio, g.ContentWidth, g.Bottom, this.CoverObstacles(), g.Height * 0.02);
        if (placed is not { } box)
        {
            image.Spec.Ignored(this.notes, "the cover has no room left for it");
            return;
        }

        this.Page.Items.Add(new ImageItem(image, image.Content, image.Format, box.X, box.Y, box.Width, box.Height));
        this.y = box.Y + box.Height;
    }

    // Where the cover's artwork, tagline and logo go, as Cover places them, with a gap around each.
    private List<(double X, double Y, double Width, double Height)> CoverObstacles()
    {
        var brand = this.design.Brand;
        var (width, height, margin) = (this.geometry.Width, this.geometry.Height, this.geometry.Left);
        var found = new List<(double X, double Y, double Width, double Height)>();
        if (brand.CoverArtwork is { } artwork && ImageInfo.Read(artwork) is { } art)
        {
            var ratio = (double)art.Height / art.Width;
            var (w, h) = (width, width * ratio);
            if (h > height * 0.42)
            {
                h = height * 0.42;
                w = h / ratio;
            }

            found.Add((width - w, height - h, w, h));
        }

        if (this.design.ShowTagline && brand.Tagline is { } tagline && ImageInfo.Read(tagline) is { } taglineInfo)
        {
            var w = width * 0.17;
            var h = w * taglineInfo.Height / taglineInfo.Width;
            found.Add((width - margin - w, height - (margin * 0.6) - h, w, h));
        }

        if (brand.Logo is { } logo && ImageInfo.Read(logo) is { } logoInfo)
        {
            var w = width * 0.16;
            found.Add((width - margin - w, margin * 0.7, w, w * logoInfo.Height / logoInfo.Width));
        }

        return [.. found.Select(o => (o.X - CoverImageGap, o.Y - CoverImageGap, o.Width + (2 * CoverImageGap), o.Height + (2 * CoverImageGap)))];
    }
}
