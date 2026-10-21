using System.Collections.Concurrent;
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
}

/// <summary>Text widths, kept behind an interface so the layout can be tested with made-up widths.</summary>
internal interface IFontMetrics
{
    /// <summary>The width of <paramref name="text"/> in points.</summary>
    double Width(string text, FontFace face, double size);

    bool HasGlyph(Rune character, FontFace face);
}

/// <summary>
/// The TrueType files for each <see cref="FontFace"/>, embedded in every PDF. To change the family, replace the
/// files under <c>Fonts/</c> and the names in <see cref="Liberation"/>.
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
        this.fonts = files.ToDictionary(f => f.Key, f => TrueTypeFontParser.Parse(new TrueTypeDataBytes(f.Value)));
    }

    /// <summary>Liberation Sans and Mono 2.1.5 (SIL OFL 1.1): the letter widths of Arial and Courier New.</summary>
    public static FontSet Liberation { get; } = new("Liberation", new Dictionary<FontFace, byte[]>
    {
        [FontFace.Regular] = Resource("LiberationSans-Regular.ttf"),
        [FontFace.Bold] = Resource("LiberationSans-Bold.ttf"),
        [FontFace.Italic] = Resource("LiberationSans-Italic.ttf"),
        [FontFace.BoldItalic] = Resource("LiberationSans-BoldItalic.ttf"),
        [FontFace.Mono] = Resource("LiberationMono-Regular.ttf"),
        [FontFace.MonoBold] = Resource("LiberationMono-Bold.ttf"),
    });

    public string Name { get; }

    public byte[] File(FontFace face) => this.files[face];

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
