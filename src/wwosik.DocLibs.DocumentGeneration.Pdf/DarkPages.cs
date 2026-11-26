using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>
/// The colours of a dark PDF (<see cref="PdfTone.Dark"/>), from the brand's slide colours: the dark variants decks use
/// on dark slides, made opaque over the page's background, so the PDF needs no transparency.
/// </summary>
internal static class DarkPages
{
    private const string White = "FFFFFF";
    private const string Black = "000000";

    // The page's background is at least this dark against white text; every other text colour reaches 4.5:1 on the
    // lightest surface it can stand on, the code box.
    private const double BackgroundContrast = 7;

    /// <summary>
    /// <paramref name="brand"/> as the layout draws it on dark pages, and the design's dark settings. The background is
    /// the slide colours' primary colour, darkened where white text on it would fall short of 7:1; text and headings are
    /// white; links, the footer and status words are lightened to 4.5:1; tables have a black veil on the header row and
    /// white veils on every other row, code a white veil, as on dark slides. The logo is <see cref="Brand.LogoOnDark"/>,
    /// else the logo.
    /// </summary>
    public static (ResolvedBrand Brand, DarkDesign Design) For(Brand brand)
    {
        var slides = ResolvedBrand.ForSlides(brand);
        var background = ResolvedBrand.Readable(slides.Primary, White, dark: false, BackgroundContrast);
        var code = Over(White, 0.10, background);
        string Light(string color) => ResolvedBrand.Readable(color, code, dark: true);
        string Word(string color) => Light(ResolvedBrand.Mix(color, 0.5));

        var labels = slides.Labels;
        var dark = slides with
        {
            ThemeColors = [White, .. slides.ThemeColors.Skip(1)],
            Primary = White,
            Link = Light(slides.Link),
            Footer = Light(slides.Footer),
            TableHeader = Over(Black, 0.40, background),
            TableBand = Over(White, 0.08, background),
            TableLine = Over(White, 0.35, background),
            Logo = slides.LogoOnDark ?? slides.Logo,
            Labels = new ResolvedLabelColors(Word(labels.Success), Word(labels.Info), Word(labels.Warning), Word(labels.Danger), Word(labels.Neutral)),
        };

        return (dark, new DarkDesign(background, Light(Over(White, 0.80, background)), code, Light(labels.Danger)));
    }

    /// <summary><paramref name="top"/> at <paramref name="share"/> opacity over <paramref name="bottom"/>, as one opaque colour.</summary>
    public static string Over(string top, double share, string bottom)
    {
        var (a, b) = (Rgb(top), Rgb(bottom));
        int Channel(int shift) => (int)Math.Round((((a >> shift) & 0xFF) * share) + (((b >> shift) & 0xFF) * (1 - share)));
        return string.Create(CultureInfo.InvariantCulture, $"{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}");
    }

    private static int Rgb(string color) => int.Parse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>What a dark PDF changes in the design besides the brand's colours: page, quote, code box, confidential marking.</summary>
internal sealed record DarkDesign(string Background, string Quote, string CodeBackground, string Danger);
