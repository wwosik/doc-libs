using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>A colour as RGB hex, or as a theme colour lightened by <see cref="LumMod"/> and <see cref="LumOff"/>.</summary>
internal readonly record struct SlideColor(string? Hex, A.SchemeColorValues? Scheme = null, int LumMod = 100000, int LumOff = 0)
{
    public static SlideColor White { get; } = new("FFFFFF");

    public static SlideColor Theme(A.SchemeColorValues scheme, int lumMod = 100000, int lumOff = 0) => new(null, scheme, lumMod, lumOff);

    public A.SolidFill Fill() => new(this.Element());

    public OpenXmlElement Element()
    {
        if (this.Hex is { } hex)
        {
            return new A.RgbColorModelHex { Val = hex };
        }

        var scheme = new A.SchemeColor { Val = this.Scheme!.Value };
        if (this.LumMod != 100000)
        {
            scheme.Append(new A.LuminanceModulation { Val = this.LumMod });
        }

        if (this.LumOff != 0)
        {
            scheme.Append(new A.LuminanceOffset { Val = this.LumOff });
        }

        return scheme;
    }
}

/// <summary>
/// The colours of flow slides. From a brand: its own colours. On a caller's template, which has no brand: the
/// template's theme colours, so the layouts follow its scheme.
/// </summary>
internal sealed record SlidePalette(
    SlideColor Paper,
    SlideColor Primary,
    SlideColor Accent,
    SlideColor Surface,
    SlideColor Line,
    SlideColor Code,
    SlideColor Highlight,
    SlideColor HighlightSurface,
    SlideColor Dark,
    SlideColor DarkSurface,
    SlideColor DarkLine,
    IReadOnlyDictionary<LabelCategory, string> Labels)
{
    /// <summary>
    /// Chips on white; card titles and statements in <see cref="ResolvedBrand.Primary"/>, which is also the dark
    /// background; kickers and figures in <see cref="ResolvedBrand.Link"/>; boxes in the table colours; code on the
    /// table line colour at 30 %; highlights and status labels in the brand's own colours for them.
    /// </summary>
    public static SlidePalette From(ResolvedBrand brand) => new(
        SlideColor.White,
        new SlideColor(brand.Primary),
        new SlideColor(brand.Link),
        new SlideColor(brand.TableBand),
        new SlideColor(brand.TableLine),
        new SlideColor(ResolvedBrand.Mix(brand.TableLine, 0.30)),
        new SlideColor(brand.Highlight),
        new SlideColor(ResolvedBrand.Mix(brand.Highlight, 0.15)),
        new SlideColor(brand.Primary),
        new SlideColor(ResolvedBrand.Mix(brand.Primary, 0.85)),
        new SlideColor(ResolvedBrand.Mix(brand.Primary, 0.65)),
        LabelColors(brand.Labels));

    /// <summary>
    /// Background 1 for chips, text 2 for titles and dark backgrounds, accent 1 for kickers, background 2 for boxes
    /// and code, accent 2 for highlights; status labels in the neutral brand's colours.
    /// </summary>
    public static SlidePalette Scheme { get; } = new(
        SlideColor.Theme(A.SchemeColorValues.Background1),
        SlideColor.Theme(A.SchemeColorValues.Text2),
        SlideColor.Theme(A.SchemeColorValues.Accent1),
        SlideColor.Theme(A.SchemeColorValues.Background2),
        SlideColor.Theme(A.SchemeColorValues.Text1, 25000, 75000),
        SlideColor.Theme(A.SchemeColorValues.Background2),
        SlideColor.Theme(A.SchemeColorValues.Accent2),
        SlideColor.Theme(A.SchemeColorValues.Accent2, 15000, 85000),
        SlideColor.Theme(A.SchemeColorValues.Text2),
        SlideColor.Theme(A.SchemeColorValues.Text2, 85000, 15000),
        SlideColor.Theme(A.SchemeColorValues.Text2, 65000, 35000),
        LabelColors(ResolvedBrand.Neutral.Labels));

    private static Dictionary<LabelCategory, string> LabelColors(ResolvedLabelColors labels) => new()
    {
        [LabelCategory.Success] = labels.Success,
        [LabelCategory.Info] = labels.Info,
        [LabelCategory.Warning] = labels.Warning,
        [LabelCategory.Danger] = labels.Danger,
        [LabelCategory.Neutral] = labels.Neutral,
    };
}
