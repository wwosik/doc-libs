namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>Something drawn on a page. Coordinates are points from the top left corner of the page.</summary>
internal abstract record PageItem;

/// <summary>Text on a baseline.</summary>
internal sealed record TextItem(double X, double Baseline, string Text, FontFace Face, double Size, string Color) : PageItem;

internal sealed record LineItem(double X1, double Y1, double X2, double Y2, double Width, string Color) : PageItem;

/// <summary>A filled rectangle.</summary>
internal sealed record RectItem(double X, double Y, double Width, double Height, string Color) : PageItem;

/// <summary>A PNG or JPEG; <see cref="Key"/> identifies the image so it is embedded once however often it is used.</summary>
internal sealed record ImageItem(object Key, ReadOnlyMemory<byte> Content, ImageFormat Format, double X, double Y, double Width, double Height) : PageItem;

/// <summary>
/// An outline in page coordinates, filled and stroked; <see cref="Dash"/> alternates dash and gap lengths in points.
/// </summary>
internal sealed record ShapeItem(IReadOnlyList<PathSegment> Path, string? Fill, string? Stroke, double Width, IReadOnlyList<double>? Dash, bool RoundJoins) : PageItem;

/// <summary>A clickable area: an absolute URL, or <c>#anchor</c> for a heading in the document.</summary>
internal sealed record LinkItem(double X, double Y, double Width, double Height, string Target) : PageItem;

internal sealed class LaidOutPage(bool isCover)
{
    public bool IsCover { get; } = isCover;

    public List<PageItem> Items { get; } = [];
}

/// <summary>An outline entry; <see cref="Level"/> 0 is the top.</summary>
internal sealed record Bookmark(string Title, int Level, int Page, double Y);

/// <summary>Page size and the area text flows in, in points from the top left corner.</summary>
internal sealed record PageGeometry(double Width, double Height, double Left, double Top, double Right, double Bottom)
{
    public double ContentWidth => this.Right - this.Left;

    public static PageGeometry For(PdfPageSize size, PdfOrientation orientation)
    {
        var (width, height) = size switch
        {
            PdfPageSize.A3 => (841.89, 1190.55),
            PdfPageSize.Letter => (612.0, 792.0),
            _ => (595.28, 841.89),
        };

        if (orientation == PdfOrientation.Landscape)
        {
            (width, height) = (height, width);
        }

        const double side = 56.69; // 20 mm
        return new PageGeometry(width, height, side, 72, width - side, height - 60);
    }
}

/// <summary>
/// The laid-out document: pages, outline, where each heading anchor is, and the title as the header shows it.
/// </summary>
internal sealed record Layout(
    IReadOnlyList<LaidOutPage> Pages,
    IReadOnlyList<Bookmark> Bookmarks,
    IReadOnlyDictionary<string, (int Page, double Y)> Anchors,
    PageGeometry Geometry,
    string? Title);
