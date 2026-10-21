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
    ResolvedDiagramColors Diagram)
{
    private const string Black = "000000";
    private const string White = "FFFFFF";

    // Accents 2–6 and the followed hyperlink of a derived theme: those of the neutral brand.
    private static readonly string[] NeutralAccents = ["0F766E", "B45309", "7C3AED", "BE123C", "4B5563"];
    private const string FollowedLink = "6D28D9";

    private static readonly ConditionalWeakTable<Brand, ResolvedBrand> Cache = new();

    public static ResolvedBrand Neutral => Resolve(Brand.Neutral);

    /// <summary>The family name decks put in their theme: the brand's, else Arial.</summary>
    public string FontFamily => this.Fonts?.Family ?? "Arial";

    /// <summary>The family name decks put in table cells; <see langword="null"/> when the brand has no table fonts.</summary>
    public string? TableFontFamily => this.Fonts is { HasTableFonts: true } fonts ? fonts.TableFamily : null;

    /// <summary>Checks <paramref name="brand"/> and fills in its defaults. Cached per instance.</summary>
    /// <exception cref="ArgumentException">Something in the brand is invalid; the message names the property.</exception>
    public static ResolvedBrand Resolve(Brand brand)
    {
        ArgumentNullException.ThrowIfNull(brand);
        return Cache.TryGetValue(brand, out var resolved) ? resolved : Cache.GetValue(brand, Create);
    }

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

        return new ResolvedBrand(
            brand.Name.ToLowerInvariant(),
            theme,
            primary,
            link,
            footer,
            gradient,
            colors.TableHeader is null ? primary : Color(brand, "Colors.TableHeader", colors.TableHeader),
            tableBand,
            colors.TableLine is null ? "D1D5DB" : Color(brand, "Colors.TableLine", colors.TableLine),
            string.IsNullOrWhiteSpace(brand.FooterText) ? null : brand.FooterText,
            Image(brand, nameof(Brand.Logo), brand.Logo),
            Image(brand, nameof(Brand.LogoOnDark), brand.LogoOnDark),
            Image(brand, nameof(Brand.CoverArtwork), brand.CoverArtwork),
            Image(brand, nameof(Brand.Tagline), brand.Tagline),
            brand.Fonts is null ? null : ResolveFonts(brand, brand.Fonts),
            brand.Pptx is { Content.IsEmpty: true } ? throw Invalid(brand, nameof(Brand.Pptx), "is empty") : brand.Pptx,
            resolvedDiagram);
    }

    private static string Color(Brand brand, string property, string? value)
    {
        var hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return ColorPattern().IsMatch(hex)
            ? hex.ToUpperInvariant()
            : throw Invalid(brand, property, $"'{value}' is not a colour; expected '#RRGGBB' or 'RRGGBB'");
    }

    // The colour at `share` on white.
    private static string Mix(string color, double share)
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
        var family = Family(brand, "Fonts.Family", fonts.Family, regular);
        if (fonts.Table is not { } table)
        {
            return new ResolvedFonts(
                regular, bold, italic, boldItalic, mono, Font(brand, "Fonts.MonoBold", fonts.MonoBold) ?? mono, family,
                regular, bold, italic, boldItalic, family);
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
            Family(brand, "Fonts.Table.Family", table.Family, tableRegular));
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
internal sealed record ResolvedDiagramColors(string Text, string Line, string Accent, string Surface, string Border, IReadOnlyList<string> Palette);

/// <summary>
/// The brand's font files and family names; <see langword="null"/> mono faces fall back to Liberation Mono. Without
/// table fonts, the table faces are the very same arrays as the text faces, and the table family is the text family.
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
    /// <summary>Whether the brand has table fonts of its own.</summary>
    public bool HasTableFonts => !ReferenceEquals(this.TableRegular, this.Regular);
}
