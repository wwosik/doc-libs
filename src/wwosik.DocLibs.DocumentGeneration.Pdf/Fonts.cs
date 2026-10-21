using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

internal enum FontFace
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
    Mono,
    MonoBold,
    TableRegular,
    TableBold,
    TableItalic,
    TableBoldItalic,
}

/// <summary>Text widths, kept behind an interface so the layout can be tested with made-up widths.</summary>
internal interface IFontMetrics
{
    /// <summary>The width of <paramref name="text"/> in points.</summary>
    double Width(string text, FontFace face, double size);

    bool HasGlyph(Rune character, FontFace face);
}

/// <summary>
/// The TrueType files for each <see cref="FontFace"/>, embedded in every PDF: a brand's own (<see cref="For"/>), or
/// <see cref="Liberation"/>. To change the default family, replace the files under <c>Fonts/</c> and the names in
/// <see cref="Liberation"/>.
/// </summary>
internal sealed class FontSet : IFontMetrics
{
    private readonly IReadOnlyDictionary<FontFace, byte[]> files;
    private readonly IReadOnlyDictionary<FontFace, TrueTypeFont> fonts;
    private readonly ConcurrentDictionary<(FontFace Face, char Character), double?> widths = new();

    private FontSet(string name, IReadOnlyDictionary<FontFace, byte[]> files)
    {
        this.Name = name;
        this.files = files;
        var parsed = new Dictionary<byte[], TrueTypeFont>(ReferenceEqualityComparer.Instance);
        this.fonts = files.ToDictionary(f => f.Key, f => parsed.TryGetValue(f.Value, out var font)
            ? font
            : parsed[f.Value] = TrueTypeFontParser.Parse(new TrueTypeDataBytes(f.Value)));
    }

    /// <summary>
    /// Liberation Sans and Mono 2.1.5 (SIL OFL 1.1): the letter widths of Arial and Courier New. Tables use the text
    /// faces.
    /// </summary>
    public static FontSet Liberation { get; } = Create(
        "Liberation",
        Resource("LiberationSans-Regular.ttf"),
        Resource("LiberationSans-Bold.ttf"),
        Resource("LiberationSans-Italic.ttf"),
        Resource("LiberationSans-BoldItalic.ttf"),
        Resource("LiberationMono-Regular.ttf"),
        Resource("LiberationMono-Bold.ttf"));

    private static readonly ConditionalWeakTable<ResolvedFonts, FontSet> BrandSets = new();

    public string Name { get; }

    /// <summary>The brand's fonts, with Liberation Mono for the mono faces it lacks; <see cref="Liberation"/> if it has none.</summary>
    /// <exception cref="ArgumentException">A font file cannot be read.</exception>
    public static FontSet For(ResolvedBrand brand) =>
        brand.Fonts is not { } fonts ? Liberation : BrandSets.GetValue(fonts, f =>
        {
            try
            {
                return Create(
                    f.Family,
                    f.Regular,
                    f.Bold,
                    f.Italic,
                    f.BoldItalic,
                    f.Mono ?? Liberation.File(FontFace.Mono),
                    f.MonoBold ?? Liberation.File(FontFace.MonoBold),
                    (f.TableRegular, f.TableBold, f.TableItalic, f.TableBoldItalic));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new ArgumentException($"The brand '{brand.Name}': a font of the family '{f.Family}' cannot be read: {ex.Message}", nameof(brand), ex);
            }
        });

    /// <summary>The face's file. Faces that share a file get the same array, so a PDF embeds it once.</summary>
    public byte[] File(FontFace face) => this.files[face];

    // Without table faces, tables use the text faces' arrays; each distinct file is parsed once.
    private static FontSet Create(
        string name, byte[] regular, byte[] bold, byte[] italic, byte[] boldItalic, byte[] mono, byte[] monoBold,
        (byte[] Regular, byte[] Bold, byte[] Italic, byte[] BoldItalic)? table = null)
    {
        var (tableRegular, tableBold, tableItalic, tableBoldItalic) = table ?? (regular, bold, italic, boldItalic);
        return new FontSet(name, new Dictionary<FontFace, byte[]>
        {
            [FontFace.Regular] = regular,
            [FontFace.Bold] = bold,
            [FontFace.Italic] = italic,
            [FontFace.BoldItalic] = boldItalic,
            [FontFace.Mono] = mono,
            [FontFace.MonoBold] = monoBold,
            [FontFace.TableRegular] = tableRegular,
            [FontFace.TableBold] = tableBold,
            [FontFace.TableItalic] = tableItalic,
            [FontFace.TableBoldItalic] = tableBoldItalic,
        });
    }

    public double Width(string text, FontFace face, double size)
    {
        double units = 0;
        foreach (var character in text)
        {
            units += this.Advance(face, character) ?? 0;
        }

        return units * size;
    }

    // Only the BMP: the layout measures and the renderer writes UTF-16 chars, one glyph each.
    public bool HasGlyph(Rune character, FontFace face) => character.IsBmp && this.Advance(face, (char)character.Value) is not null;

    // The advance width as a fraction of the font size, or null if the font has no glyph for the character.
    private double? Advance(FontFace face, char character) =>
        this.widths.GetOrAdd((face, character), key =>
        {
            var font = this.fonts[key.Face];
            return font.TryGetAdvanceWidth(key.Character, out var width) ? width / font.GetUnitsPerEm() : null;
        });

    private static byte[] Resource(string name)
    {
        using var stream = typeof(FontSet).Assembly.GetManifestResourceStream("Fonts." + name)
            ?? throw new InvalidOperationException($"The embedded font '{name}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
