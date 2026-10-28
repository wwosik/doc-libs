using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A <see cref="Brand"/> checked and with every default filled in, as the generators draw it. Colours are upper-case
/// RGB hex without <c>#</c>; <see cref="ThemeColors"/> is in the order of a PowerPoint theme.
/// </summary>
internal sealed partial record ResolvedBrand(
    string Name,
    IReadOnlyList<string> ThemeColors,
    string Primary,
    string Link,
    string Footer,
    IReadOnlyList<string> Gradient,
    string TableHeader,
    string TableBand,
    string TableLine,
    string? FooterText,
    byte[]? Logo,
    byte[]? LogoOnDark,
    byte[]? CoverArtwork,
    byte[]? Tagline,
    ResolvedFonts? Fonts,
    PptxBrandTemplate? Pptx,
    ResolvedDiagramColors Diagram,
    string Highlight,
    ResolvedLabelColors Labels,
    ResolvedChartColors Chart)
{
    private const string Black = "000000";
    private const string White = "FFFFFF";

    // Accents 2–6 and the followed hyperlink of a derived theme: those of the neutral brand.
    private static readonly string[] NeutralAccents = ["0F766E", "B45309", "7C3AED", "BE123C", "4B5563"];
    private const string FollowedLink = "6D28D9";

    private static readonly ConditionalWeakTable<Brand, ResolvedBrand> Cache = new();
    private static readonly ConditionalWeakTable<Brand, Brand> SlideBrands = new();
    private static readonly ConditionalWeakTable<Brand, Brand> DarkSlideBrands = new();

    // The brands DarkSlideBrand made, so that their charts are resolved for a dark background.
    private static readonly ConditionalWeakTable<Brand, object> DarkBrands = new();

    public static ResolvedBrand Neutral => Resolve(Brand.Neutral);

    /// <summary>The family name decks put in their theme: the brand's, else Arial.</summary>
    public string FontFamily => this.Fonts?.Family ?? "Arial";

    /// <summary>The family name decks put in table cells; <see langword="null"/> when the brand has no table fonts.</summary>
    public string? TableFontFamily => this.Fonts is { HasTableFonts: true } fonts ? fonts.TableFamily : null;

    /// <summary>The family name decks put on code; <see langword="null"/> when the brand has no mono font.</summary>
    public string? CodeFontFamily => this.Fonts?.MonoFamily;

    /// <summary>Checks <paramref name="brand"/> and fills in its defaults. Cached per instance.</summary>
    /// <exception cref="ArgumentException">Something in the brand is invalid; the message names the property.</exception>
    public static ResolvedBrand Resolve(Brand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return Cache.TryGetValue(brand, out var resolved) ? resolved : Cache.GetValue(brand, Create);
    }

    /// <summary>
    /// The brand as decks draw it: <see cref="Brand.Colors"/> with <see cref="Brand.SlideColors"/> laid over them. The
    /// brand itself when it has no slide colours; else one derived brand per instance, so that its resolution is cached.
    /// </summary>
    public static Brand SlideBrand(Brand brand) =>
        brand.SlideColors is null ? brand : SlideBrands.GetValue(brand, b => b with { Colors = Merge(b.SlideColors!, b.Colors), SlideColors = null });

    /// <summary>The resolved colours, fonts and images of <see cref="SlideBrand"/>.</summary>
    public static ResolvedBrand ForSlides(Brand brand) => Resolve(SlideBrand(brand));

    /// <summary>
    /// <see cref="SlideBrand"/> for slides that are dark: diagrams with white text and lines, nodes in a dark shade of
    /// the primary colour and the highlight as accent — unless the slide colours name diagram colours of their own.
    /// </summary>
    public static Brand DarkSlideBrand(Brand brand) => DarkSlideBrands.GetValue(brand, original =>
    {
        var slides = SlideBrand(original);
        var resolved = Resolve(slides);
        var own = original.SlideColors?.Diagram;
        var dark = slides with
        {
            Colors = slides.Colors with
            {
                Diagram = new BrandDiagramColors
                {
                    Text = own?.Text ?? "FFFFFF",
                    Line = own?.Line ?? "D1D5DB",
                    Accent = own?.Accent ?? resolved.Highlight,
                    Surface = own?.Surface ?? Shade(resolved.Primary, 0.45),
                    Border = own?.Border ?? resolved.Highlight,
                    Palette = own?.Palette ?? slides.Colors.Diagram?.Palette,
                },
            },
        };
        DarkBrands.AddOrUpdate(dark, dark);
        return dark;
    });

    /// <summary>Whether <paramref name="brand"/> is one <see cref="DarkSlideBrand"/> made, for dark slides.</summary>
    public static bool IsDark(Brand brand) => DarkBrands.TryGetValue(brand, out _);

    /// <summary>Whether <paramref name="name"/> can name a brand.</summary>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    private static ResolvedBrand Create(Brand brand)
    {
        if (!IsValidName(brand.Name))
        {
            throw Invalid(brand, nameof(Brand.Name), $"'{brand.Name}' is not a valid brand name; use letters, digits and '-'");
        }

        var colors = brand.Colors ?? throw Invalid(brand, nameof(Brand.Colors), "is missing");
        var primary = Color(brand, "Colors.Primary", colors.Primary);
        var link = colors.Link is null ? primary : Color(brand, "Colors.Link", colors.Link);
        var tableBand = colors.TableBand is null ? Mix(primary, 0.10) : Color(brand, "Colors.TableBand", colors.TableBand);
        var gradient = colors.Gradient is null
            ? [primary, link == primary ? Mix(primary, 0.60) : link]
            : colors.Gradient.Count < 2
                ? throw Invalid(brand, "Colors.Gradient", "needs at least two colours")
                : colors.Gradient.Select((c, i) => Color(brand, $"Colors.Gradient[{i}]", c)).ToArray();
        var theme = colors.Theme is null
            ? [Black, White, primary, tableBand, primary, .. NeutralAccents, link, FollowedLink]
            : colors.Theme.Count != 12
                ? throw Invalid(brand, "Colors.Theme", $"has {colors.Theme.Count} colours; a PowerPoint theme needs exactly 12")
                : colors.Theme.Select((c, i) => Color(brand, $"Colors.Theme[{i}]", c)).ToArray();

        var footer = colors.Footer is null ? "6B7280" : Color(brand, "Colors.Footer", colors.Footer);
        var diagram = colors.Diagram;
        string DiagramColor(string name, string? value, string fallback) => value is null ? fallback : Color(brand, "Colors.Diagram." + name, value);
        var resolvedDiagram = new ResolvedDiagramColors(
            DiagramColor(nameof(BrandDiagramColors.Text), diagram?.Text, theme[0]),
            DiagramColor(nameof(BrandDiagramColors.Line), diagram?.Line, footer),
            DiagramColor(nameof(BrandDiagramColors.Accent), diagram?.Accent, primary),
            DiagramColor(nameof(BrandDiagramColors.Surface), diagram?.Surface, tableBand),
            DiagramColor(nameof(BrandDiagramColors.Border), diagram?.Border, primary),
            diagram?.Palette is not { } palette ? theme[4..10]
                : palette.Count == 0 ? throw Invalid(brand, "Colors.Diagram.Palette", "needs at least one colour")
                : palette.Select((c, i) => Color(brand, $"Colors.Diagram.Palette[{i}]", c)).ToArray());

        var labels = colors.Labels;
        string LabelColor(string name, string? value, string fallback) => value is null ? fallback : Color(brand, "Colors.Labels." + name, value);
        var resolvedLabels = new ResolvedLabelColors(
            LabelColor(nameof(BrandLabelColors.Success), labels?.Success, "16A34A"),
            LabelColor(nameof(BrandLabelColors.Info), labels?.Info, link),
            LabelColor(nameof(BrandLabelColors.Warning), labels?.Warning, "D97706"),
            LabelColor(nameof(BrandLabelColors.Danger), labels?.Danger, "DC2626"),
            LabelColor(nameof(BrandLabelColors.Neutral), labels?.Neutral, "9CA3AF"));

        var highlight = colors.Highlight is null ? theme[5] : Color(brand, "Colors.Highlight", colors.Highlight);
        var tableHeader = colors.TableHeader is null ? primary : Color(brand, "Colors.TableHeader", colors.TableHeader);
        var chart = ResolveChart(brand, colors.Chart, IsDark(brand), primary, footer, tableHeader, tableBand, resolvedDiagram, resolvedLabels);

        return new ResolvedBrand(
            brand.Name.ToLowerInvariant(),
            theme,
            primary,
            link,
            footer,
            gradient,
            tableHeader,
            tableBand,
            colors.TableLine is null ? "D1D5DB" : Color(brand, "Colors.TableLine", colors.TableLine),
            string.IsNullOrWhiteSpace(brand.FooterText) ? null : brand.FooterText,
            Image(brand, nameof(Brand.Logo), brand.Logo),
            Image(brand, nameof(Brand.LogoOnDark), brand.LogoOnDark),
            Image(brand, nameof(Brand.CoverArtwork), brand.CoverArtwork),
            Image(brand, nameof(Brand.Tagline), brand.Tagline),
            brand.Fonts is null ? null : ResolveFonts(brand, brand.Fonts),
            brand.Pptx is { Content.IsEmpty: true } ? throw Invalid(brand, nameof(Brand.Pptx), "is empty") : brand.Pptx,
            resolvedDiagram,
            highlight,
            resolvedLabels,
            chart);
    }

    // Chart colours: each set one checked, each other derived; for a dark background, darker fills and lighter lines.
    private static ResolvedChartColors ResolveChart(
        Brand brand,
        BrandChartColors? chart,
        bool dark,
        string primary,
        string footer,
        string tableHeader,
        string tableBand,
        ResolvedDiagramColors diagram,
        ResolvedLabelColors labels)
    {
        string Role(string name, string? value, string fallback) => value is null ? fallback : Color(brand, "Colors.Chart." + name, value);
        IReadOnlyList<string> List(string name, IReadOnlyList<string>? value, IReadOnlyList<string> fallback) =>
            value is null ? fallback
            : value.Count == 0 ? throw Invalid(brand, "Colors.Chart." + name, "needs at least one colour")
            : value.Select((c, i) => Color(brand, $"Colors.Chart.{name}[{i}]", c)).ToArray();

        var palette = diagram.Palette;
        var series = List(nameof(BrandChartColors.Series), chart?.Series, [.. Enumerable.Range(0, 3).Select(i => palette[i % palette.Count])])
            .Select(c => dark
                ? new ResolvedSeriesColors(Shade(c, 0.55), Shade(c, 0.35), c, Mix(c, 0.6))
                : new ResolvedSeriesColors(Mix(c, 0.18), Mix(c, 0.08), Mix(c, 0.65), c))
            .ToArray();
        var header = Role(nameof(BrandChartColors.TableHeader), chart?.TableHeader, tableHeader);
        return new ResolvedChartColors(
            series,
            List(nameof(BrandChartColors.Categories), chart?.Categories, palette),
            Role(nameof(BrandChartColors.Reference), chart?.Reference, dark ? "9CA3AF" : "6B7280"),
            Role(nameof(BrandChartColors.ReferenceFill), chart?.ReferenceFill, dark ? "4B5563" : "E5E7EB"),
            Role(nameof(BrandChartColors.Axis), chart?.Axis, "9CA3AF"),
            Role(nameof(BrandChartColors.Gridline), chart?.Gridline, dark ? "4B5563" : "E5E7EB"),
            Role(nameof(BrandChartColors.Band), chart?.Band, dark ? Shade(primary, 0.3) : "F9FAFB"),
            Role(nameof(BrandChartColors.Emphasis), chart?.Emphasis, labels.Success),
            Role(nameof(BrandChartColors.Ahead), chart?.Ahead, labels.Success),
            Role(nameof(BrandChartColors.Level), chart?.Level, dark ? "D1D5DB" : footer),
            Role(nameof(BrandChartColors.Behind), chart?.Behind, labels.Danger),
            chart?.Surface is { } surface ? Color(brand, "Colors.Chart.Surface", surface) : dark ? null : White,
            diagram.Text,
            dark ? "D1D5DB" : footer,
            dark ? diagram.Surface : White,
            header,
            Role(nameof(BrandChartColors.TotalHeader), chart?.TotalHeader, Shade(header, 0.7)),
            Role(nameof(BrandChartColors.TotalFill), chart?.TotalFill, dark ? Shade(primary, 0.35) : tableBand),
            Role(nameof(BrandChartColors.TooltipFill), chart?.TooltipFill, "111827"),
            Role(nameof(BrandChartColors.TooltipText), chart?.TooltipText, White));
    }

    private static string Color(Brand brand, string property, string? value)
    {
        var hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return ColorPattern().IsMatch(hex)
            ? hex.ToUpperInvariant()
            : throw Invalid(brand, property, $"'{value}' is not a colour; expected '#RRGGBB' or 'RRGGBB'");
    }

    // Each optional colour of `slides` that is not set, from `pdf`; diagram and label colours one by one.
    private static BrandColors Merge(BrandColors slides, BrandColors pdf) => new()
    {
        Primary = slides.Primary,
        Link = slides.Link ?? pdf.Link,
        Footer = slides.Footer ?? pdf.Footer,
        TableHeader = slides.TableHeader ?? pdf.TableHeader,
        TableBand = slides.TableBand ?? pdf.TableBand,
        TableLine = slides.TableLine ?? pdf.TableLine,
        Gradient = slides.Gradient ?? pdf.Gradient,
        Theme = slides.Theme ?? pdf.Theme,
        Diagram = slides.Diagram is null ? pdf.Diagram : pdf.Diagram is null ? slides.Diagram : new BrandDiagramColors
        {
            Text = slides.Diagram.Text ?? pdf.Diagram.Text,
            Line = slides.Diagram.Line ?? pdf.Diagram.Line,
            Accent = slides.Diagram.Accent ?? pdf.Diagram.Accent,
            Surface = slides.Diagram.Surface ?? pdf.Diagram.Surface,
            Border = slides.Diagram.Border ?? pdf.Diagram.Border,
            Palette = slides.Diagram.Palette ?? pdf.Diagram.Palette,
        },
        Highlight = slides.Highlight ?? pdf.Highlight,
        Chart = slides.Chart ?? pdf.Chart,
        Labels = slides.Labels is null ? pdf.Labels : pdf.Labels is null ? slides.Labels : new BrandLabelColors
        {
            Success = slides.Labels.Success ?? pdf.Labels.Success,
            Info = slides.Labels.Info ?? pdf.Labels.Info,
            Warning = slides.Labels.Warning ?? pdf.Labels.Warning,
            Danger = slides.Labels.Danger ?? pdf.Labels.Danger,
            Neutral = slides.Labels.Neutral ?? pdf.Labels.Neutral,
        },
    };

    // The colour at `share` of itself on black: a darker shade.
    internal static string Shade(string color, double share)
    {
        var rgb = int.Parse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int Channel(int shift) => (int)Math.Round(((rgb >> shift) & 0xFF) * share);
        return string.Create(CultureInfo.InvariantCulture, $"{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}");
    }

    // The colour at `share` on white.
    internal static string Mix(string color, double share)
    {
        var rgb = int.Parse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int Channel(int shift) => (int)Math.Round(255 - ((255 - ((rgb >> shift) & 0xFF)) * share));
        return string.Create(CultureInfo.InvariantCulture, $"{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}");
    }

    private static byte[]? Image(Brand brand, string property, BrandImage? image)
    {
        if (image is null)
        {
            return null;
        }

        return ImageInfo.Read(image.Content.Span) is { Format: ImageFormat.Png or ImageFormat.Jpeg }
            ? image.Content.ToArray()
            : throw Invalid(brand, property, "is not a PNG or JPEG image");
    }

    private static ResolvedFonts ResolveFonts(Brand brand, BrandFonts fonts)
    {
        byte[] Required(string face, BrandFont? font) =>
            font is null ? throw Invalid(brand, "Fonts." + face, "is missing; the four text faces are required") : Font(brand, "Fonts." + face, font)!;

        var regular = Required(nameof(BrandFonts.Regular), fonts.Regular);
        var bold = Required(nameof(BrandFonts.Bold), fonts.Bold);
        var italic = Required(nameof(BrandFonts.Italic), fonts.Italic);
        var boldItalic = Required(nameof(BrandFonts.BoldItalic), fonts.BoldItalic);
        var mono = Font(brand, "Fonts.Mono", fonts.Mono);
        var monoFamily = mono is null ? null : TrueTypeInfo.FamilyName(mono);
        var family = Family(brand, "Fonts.Family", fonts.Family, regular);
        if (fonts.Table is not { } table)
        {
            return new ResolvedFonts(
                regular, bold, italic, boldItalic, mono, Font(brand, "Fonts.MonoBold", fonts.MonoBold) ?? mono, family,
                regular, bold, italic, boldItalic, family)
            {
                MonoFamily = monoFamily,
            };
        }

        byte[] RequiredTable(string face, BrandFont? font) =>
            font is null ? throw Invalid(brand, "Fonts.Table." + face, "is missing; table fonts need a regular and a bold face") : Font(brand, "Fonts.Table." + face, font)!;

        var tableRegular = RequiredTable(nameof(BrandTableFonts.Regular), table.Regular);
        var tableBold = RequiredTable(nameof(BrandTableFonts.Bold), table.Bold);
        return new ResolvedFonts(
            regular, bold, italic, boldItalic, mono, Font(brand, "Fonts.MonoBold", fonts.MonoBold) ?? mono, family,
            tableRegular,
            tableBold,
            Font(brand, "Fonts.Table.Italic", table.Italic) ?? tableRegular,
            Font(brand, "Fonts.Table.BoldItalic", table.BoldItalic) ?? tableBold,
            Family(brand, "Fonts.Table.Family", table.Family, tableRegular))
        {
            MonoFamily = monoFamily,
        };
    }

    private static string Family(Brand brand, string property, string? family, byte[] regular) =>
        (string.IsNullOrWhiteSpace(family) ? TrueTypeInfo.FamilyName(regular) : family.Trim())
        ?? throw Invalid(brand, property, "is missing and cannot be read from the regular face");

    private static byte[]? Font(Brand brand, string property, BrandFont? font)
    {
        if (font is null)
        {
            return null;
        }

        return TrueTypeInfo.Outlines(font.Content.Span) switch
        {
            FontOutlines.TrueType => font.Content.ToArray(),
            FontOutlines.Cff => throw Invalid(brand, property, "has CFF (PostScript) outlines, which cannot be embedded; convert it to a TrueType font"),
            FontOutlines.Collection => throw Invalid(brand, property, "is a font collection; pass a single font"),
            _ => throw Invalid(brand, property, "is not a TrueType font"),
        };
    }

    private static ArgumentException Invalid(Brand brand, string property, string reason) =>
        new($"The brand '{brand.Name}': {property} {reason}.", nameof(brand));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}

/// <summary>The diagram colours, upper-case RGB hex without <c>#</c>.</summary>
/// <summary>Status label colours by category; see <see cref="BrandLabelColors"/>.</summary>
internal sealed record ResolvedLabelColors(string Success, string Info, string Warning, string Danger, string Neutral);

/// <summary>One chart series: its fill, pale fill (year to date, projections), strong fill (actuals) and outline.</summary>
internal sealed record ResolvedSeriesColors(string Fill, string Pale, string Strong, string Stroke);

/// <summary>
/// The colours of charts. <see cref="Surface"/> is <see langword="null"/> on dark slides, where the slide shows through;
/// <see cref="Patch"/> is what a label is written on where it crosses a line.
/// </summary>
internal sealed record ResolvedChartColors(
    IReadOnlyList<ResolvedSeriesColors> Series,
    IReadOnlyList<string> Categories,
    string Reference,
    string ReferenceFill,
    string Axis,
    string Gridline,
    string Band,
    string Emphasis,
    string Ahead,
    string Level,
    string Behind,
    string? Surface,
    string Text,
    string Muted,
    string Patch,
    string TableHeader,
    string TotalHeader,
    string TotalFill,
    string TooltipFill,
    string TooltipText);

internal sealed record ResolvedDiagramColors(string Text, string Line, string Accent, string Surface, string Border, IReadOnlyList<string> Palette);

/// <summary>
/// The brand's font files and family names; <see langword="null"/> mono faces fall back to Liberation Mono. Without
/// table fonts, the table faces are the very same arrays as the text faces, and the table family is the text family.
/// A face that falls back to another is the very same array.
/// </summary>
internal sealed record ResolvedFonts(
    byte[] Regular,
    byte[] Bold,
    byte[] Italic,
    byte[] BoldItalic,
    byte[]? Mono,
    byte[]? MonoBold,
    string Family,
    byte[] TableRegular,
    byte[] TableBold,
    byte[] TableItalic,
    byte[] TableBoldItalic,
    string TableFamily)
{
    /// <summary>The family name of <see cref="Mono"/>, read from the font; <see langword="null"/> without it.</summary>
    public string? MonoFamily { get; init; }

    /// <summary>Whether the brand has table fonts of its own.</summary>
    public bool HasTableFonts => !ReferenceEquals(this.TableRegular, this.Regular);
}
