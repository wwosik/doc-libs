using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// A page in twentieths of a point (twips): its size and margins. The built-in template has margins of 20 mm left and
/// right and 25 mm top and bottom; a caller's template is read from its section properties.
/// </summary>
internal sealed record DocxPage(int Width, int Height, int Top, int Right, int Bottom, int Left)
{
    /// <summary>EMU per twip: drawings are measured in EMU, the page in twips.</summary>
    public const long EmuPerTwip = 635;

    private const int Millimetre20 = 1134;
    private const int Millimetre25 = 1417;

    /// <summary>The width between the margins.</summary>
    public int TextWidth => this.Width - this.Left - this.Right;

    /// <summary>Whether the page is wider than tall.</summary>
    public bool Landscape => this.Width > this.Height;

    /// <summary>The space above the cover title that puts it at 28 % of the page height, as in the PDF.</summary>
    public int CoverTitleSpace => Math.Max(0, (int)(this.Height * 0.28) - this.Top);

    public static DocxPage For(DocxPageSize size, DocxOrientation orientation)
    {
        var (width, height) = size switch
        {
            DocxPageSize.Letter => (12240, 15840),
            DocxPageSize.A3 => (16838, 23811),
            _ => (11906, 16838),
        };
        return orientation == DocxOrientation.Landscape
            ? new DocxPage(height, width, Millimetre25, Millimetre20, Millimetre25, Millimetre20)
            : new DocxPage(width, height, Millimetre25, Millimetre20, Millimetre25, Millimetre20);
    }

    /// <summary>The page of a template's last section; A4 with the built-in margins for what it does not say.</summary>
    public static DocxPage Of(SectionProperties? section)
    {
        var a4 = For(DocxPageSize.A4, DocxOrientation.Portrait);
        var size = section?.GetFirstChild<PageSize>();
        var margin = section?.GetFirstChild<PageMargin>();
        return new DocxPage(
            (int?)size?.Width?.Value ?? a4.Width,
            (int?)size?.Height?.Value ?? a4.Height,
            Math.Abs(margin?.Top?.Value ?? a4.Top),
            (int?)margin?.Right?.Value ?? a4.Right,
            Math.Abs(margin?.Bottom?.Value ?? a4.Bottom),
            (int?)margin?.Left?.Value ?? a4.Left);
    }
}
