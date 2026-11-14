using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// A colour as RGB hex, or as a theme colour lightened by <see cref="LumMod"/> and <see cref="LumOff"/>; with
/// <see cref="Alpha"/> below 100000, translucent.
/// </summary>
internal readonly record struct SlideColor(string? Hex, A.SchemeColorValues? Scheme = null, int LumMod = 100000, int LumOff = 0, int Alpha = 100000)
{
    public static SlideColor White { get; } = new("FFFFFF");

    /// <summary>The colour at <paramref name="percent"/> % opacity.</summary>
    public SlideColor Translucent(int percent) => this with { Alpha = percent * 1000 };

    public static SlideColor Theme(A.SchemeColorValues scheme, int lumMod = 100000, int lumOff = 0) => new(null, scheme, lumMod, lumOff);

    public A.SolidFill Fill() => new(this.Element());

    public OpenXmlElement Element()
    {
        if (this.Hex is { } hex)
        {
            var rgb = new A.RgbColorModelHex { Val = hex };
            if (this.Alpha != 100000)
            {
                rgb.Append(new A.Alpha { Val = this.Alpha });
            }

            return rgb;
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

        if (this.Alpha != 100000)
        {
            scheme.Append(new A.Alpha { Val = this.Alpha });
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
    private static readonly SlideColor Black = new("000000");

    // Highlighted code on a caller's template: keywords in accent 1, types in accent 2, strings in accent 6, numbers in
    // accent 4, comments in the text colour at 60 %, markup in the hyperlink colour.
    private static readonly Dictionary<CodeTokenKind, SlideColor> SchemeCodeTokens = new()
    {
        [CodeTokenKind.Keyword] = SlideColor.Theme(A.SchemeColorValues.Accent1),
        [CodeTokenKind.Type] = SlideColor.Theme(A.SchemeColorValues.Accent2),
        [CodeTokenKind.String] = SlideColor.Theme(A.SchemeColorValues.Accent6),
        [CodeTokenKind.Number] = SlideColor.Theme(A.SchemeColorValues.Accent4),
        [CodeTokenKind.Comment] = SlideColor.Theme(A.SchemeColorValues.Text1, 60000, 40000),
        [CodeTokenKind.Markup] = SlideColor.Theme(A.SchemeColorValues.Hyperlink),
    };


    /// <summary>
    /// Whether every slide is dark (a brand template of <see cref="SlideTone.Dark"/>): the blocks then take the dark
    /// styles throughout, and everything the generator writes is in <see cref="Text"/>.
    /// </summary>
    public bool DarkSlides { get; init; }

    /// <summary>The colour of all text the generator writes outside placeholders; <see langword="null"/>: the template's.</summary>
    public SlideColor? Text { get; init; }

    /// <summary>The colour of the footer line's text, which the classification label in the corner takes.</summary>
    public SlideColor Footer { get; init; } = SlideColor.Theme(A.SchemeColorValues.Text1, 65000, 35000);

    /// <summary>The bar of notes and tips.</summary>
    public SlideColor NoteBar { get; init; } = SlideColor.Theme(A.SchemeColorValues.Text2);

    /// <summary>The bar of important callouts, warnings and cautions.</summary>
    public SlideColor ImportantBar { get; init; } = SlideColor.Theme(A.SchemeColorValues.Accent2);

    /// <summary>What <c>==marked==</c> text gets behind it (<c>a:highlight</c>); <see langword="null"/>: nothing.</summary>
    public SlideColor? MarkFill { get; init; } = SlideColor.Theme(A.SchemeColorValues.Accent2, 15000, 85000);

    /// <summary>The colour of <c>==marked==</c> text; <see langword="null"/>: the text's own.</summary>
    public SlideColor? MarkText { get; init; }

    /// <summary>Table cells on dark slides: the header row, every other row and the text; <see langword="null"/>: the table style's.</summary>
    public (SlideColor Header, SlideColor Band, SlideColor Text)? DarkTable { get; init; }

    /// <summary>Highlighted code on the code box and on light slides, by token kind; plain text has none.</summary>
    public IReadOnlyDictionary<CodeTokenKind, SlideColor> CodeTokens { get; init; } = new Dictionary<CodeTokenKind, SlideColor>();

    /// <summary>Highlighted code on the dark code box of a <c>.dark</c> slide, by token kind.</summary>
    public IReadOnlyDictionary<CodeTokenKind, SlideColor> DarkCodeTokens { get; init; } = new Dictionary<CodeTokenKind, SlideColor>();

    /// <summary>The colour of a highlighted run of <paramref name="kind"/>; <see langword="null"/>: the text's own.</summary>
    public SlideColor? CodeColour(CodeTokenKind kind, bool onDark) =>
        (onDark ? this.DarkCodeTokens : this.CodeTokens).TryGetValue(kind, out var color) ? color : null;

    /// <summary>The colour of <c>[words]{.success}</c>: the category's colour, on dark slides lightened halfway to white.</summary>
    public SlideColor WordColour(LabelCategory category) => this.DarkSlides
        ? new SlideColor(ResolvedBrand.Mix(this.Labels[category], 0.5))
        : new SlideColor(this.Labels[category]);

    /// <summary>The fill of a status label's cell: a light tint of the category's colour, or on dark slides a translucent one.</summary>
    public SlideColor LabelFill(LabelCategory category) => this.DarkSlides
        ? new SlideColor(this.Labels[category]).Translucent(45)
        : new SlideColor(ResolvedBrand.Mix(this.Labels[category], 0.22));

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
        LabelColors(brand.Labels))
    {
        Footer = new SlideColor(brand.Footer),
        NoteBar = new SlideColor(brand.Primary),
        ImportantBar = new SlideColor(brand.Highlight),
        MarkFill = new SlideColor(ResolvedBrand.Mix(brand.Highlight, 0.15)),
        CodeTokens = Tokens(brand.Code.On(ResolvedBrand.Mix(brand.TableLine, 0.30))),
        DarkCodeTokens = Tokens(brand.Code.On(ResolvedBrand.Mix(brand.Primary, 0.85), dark: true)),
    };

    /// <summary>
    /// For dark slides: white text; boxes, cards and code as white veils over the background and dark panels as black
    /// ones; kickers, figures and marked text in the highlight; the callout bars in the highlight and the warning
    /// colour; status labels and highlighted rows as translucent fills; tables with a black veil on the header row and
    /// white veils on every other row. Every text on these fills keeps a contrast ratio of at least 4.5 over any dark
    /// background.
    /// </summary>
    public static SlidePalette ForDark(ResolvedBrand brand) => new(
        SlideColor.White.Translucent(15),
        SlideColor.White,
        new SlideColor(brand.Highlight),
        SlideColor.White.Translucent(10),
        SlideColor.White.Translucent(35),
        SlideColor.White.Translucent(10),
        new SlideColor(brand.Highlight),
        new SlideColor(brand.Highlight).Translucent(25),
        Black.Translucent(35),
        SlideColor.White.Translucent(10),
        SlideColor.White.Translucent(35),
        LabelColors(brand.Labels))
    {
        DarkSlides = true,
        Text = SlideColor.White,
        Footer = SlideColor.White,
        NoteBar = new SlideColor(brand.Highlight),
        ImportantBar = new SlideColor(brand.Labels.Warning),
        MarkFill = null,
        MarkText = new SlideColor(brand.Highlight),
        DarkTable = (Black.Translucent(40), SlideColor.White.Translucent(8), SlideColor.White),
        CodeTokens = Tokens(brand.Code.On(DarkCodeBackground, dark: true)),
        DarkCodeTokens = Tokens(brand.Code.On(DarkCodeBackground, dark: true)),
    };

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
        LabelColors(ResolvedBrand.Neutral.Labels))
    {
        CodeTokens = SchemeCodeTokens,
        DarkCodeTokens = SchemeCodeTokens,
    };

    // What code on a dark template is checked against: the slide's background is the template's, unknown here, so a
    // dark grey that the white veil of the code box over most dark backgrounds is no lighter than.
    private const string DarkCodeBackground = "374151";

    private static Dictionary<CodeTokenKind, SlideColor> Tokens(ResolvedCodeColors colors) =>
        Enum.GetValues<CodeTokenKind>().Where(k => colors.Of(k) is not null).ToDictionary(k => k, k => new SlideColor(colors.Of(k)));

    private static Dictionary<LabelCategory, string> LabelColors(ResolvedLabelColors labels) => new()
    {
        [LabelCategory.Success] = labels.Success,
        [LabelCategory.Info] = labels.Info,
        [LabelCategory.Warning] = labels.Warning,
        [LabelCategory.Danger] = labels.Danger,
        [LabelCategory.Neutral] = labels.Neutral,
    };
}
