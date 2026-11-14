using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The horizontal metrics of a TrueType face, read from its <c>head</c>, <c>hhea</c>, <c>hmtx</c>, <c>OS/2</c> and
/// <c>cmap</c> tables: what text layout needs to break lines in that face without a font library. No kerning.
/// </summary>
internal sealed class TrueTypeMetrics
{
    private static readonly ConditionalWeakTable<byte[], TrueTypeMetrics?> Cache = new();

    private readonly ushort[] advances;
    private readonly Dictionary<int, ushort> glyphs;

    private TrueTypeMetrics(ushort unitsPerEm, ushort[] advances, Dictionary<int, ushort> glyphs, double ascent, double descent, bool bold, bool italic)
    {
        this.UnitsPerEm = unitsPerEm;
        this.advances = advances;
        this.glyphs = glyphs;
        this.Ascent = ascent;
        this.Descent = descent;
        this.Bold = bold;
        this.Italic = italic;
    }

    public ushort UnitsPerEm { get; }

    /// <summary>The Windows ascent (<c>OS/2 usWinAscent</c>, else <c>hhea</c>) in ems.</summary>
    public double Ascent { get; }

    /// <summary>The Windows descent (<c>OS/2 usWinDescent</c>, else <c>hhea</c>), positive, in ems.</summary>
    public double Descent { get; }

    /// <summary>Whether <c>head macStyle</c> marks the face bold.</summary>
    public bool Bold { get; }

    /// <summary>Whether <c>head macStyle</c> marks the face italic.</summary>
    public bool Italic { get; }

    /// <summary>The metrics of a TrueType file, cached per array; <see langword="null"/> if a table it needs is missing or damaged.</summary>
    public static TrueTypeMetrics? For(byte[] font) => Cache.GetValue(font, Read);

    public bool Has(int codePoint) => this.glyphs.ContainsKey(codePoint);

    /// <summary>The advance of <paramref name="codePoint"/> in ems; <see langword="null"/> if the face has no glyph for it.</summary>
    public double? Advance(int codePoint) =>
        this.glyphs.TryGetValue(codePoint, out var glyph) ? this.advances[Math.Min(glyph, this.advances.Length - 1)] / (double)this.UnitsPerEm : null;

    /// <summary>The width of <paramref name="text"/> in ems, characters the face lacks measured as its missing glyph (glyph 0).</summary>
    public double Width(string text)
    {
        var width = 0.0;
        for (var i = 0; i < text.Length; i++)
        {
            var codePoint = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text[i], text[++i]) : text[i];
            width += this.Advance(codePoint) ?? (this.advances[0] / (double)this.UnitsPerEm);
        }

        return width;
    }

    private static TrueTypeMetrics? Read(byte[] font)
    {
        try
        {
            if (TrueTypeInfo.Table(font, "head") is not var (head, _)
                || TrueTypeInfo.Table(font, "hhea") is not var (hhea, _)
                || TrueTypeInfo.Table(font, "hmtx") is not var (hmtx, hmtxLength))
            {
                return null;
            }

            var span = font.AsSpan();
            var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(span[(head + 18)..]);
            var macStyle = BinaryPrimitives.ReadUInt16BigEndian(span[(head + 44)..]);
            var count = BinaryPrimitives.ReadUInt16BigEndian(span[(hhea + 34)..]);
            if (unitsPerEm == 0 || count == 0 || count * 4 > hmtxLength)
            {
                return null;
            }

            var advances = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                advances[i] = BinaryPrimitives.ReadUInt16BigEndian(span[(hmtx + (i * 4))..]);
            }

            double ascent = BinaryPrimitives.ReadInt16BigEndian(span[(hhea + 4)..]);
            double descent = -BinaryPrimitives.ReadInt16BigEndian(span[(hhea + 6)..]);
            if (TrueTypeInfo.Table(font, "OS/2") is var (os2, os2Length) && os2Length >= 78)
            {
                ascent = BinaryPrimitives.ReadUInt16BigEndian(span[(os2 + 74)..]);
                descent = BinaryPrimitives.ReadUInt16BigEndian(span[(os2 + 76)..]);
            }

            return new TrueTypeMetrics(unitsPerEm, advances, Glyphs(font), ascent / unitsPerEm, descent / unitsPerEm, (macStyle & 1) != 0, (macStyle & 2) != 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // The Windows Unicode cmap (3, 10) or (3, 1), format 4 or 12, as code point → glyph index.
    private static Dictionary<int, ushort> Glyphs(byte[] font)
    {
        var glyphs = new Dictionary<int, ushort>();
        if (TrueTypeInfo.Table(font, "cmap") is not var (offset, _))
        {
            return glyphs;
        }

        int? subtable = null;
        var count = Word(font, offset + 2);
        for (var i = 0; i < count; i++)
        {
            var record = offset + 4 + (i * 8);
            var (platform, encoding) = (Word(font, record), Word(font, record + 2));
            if ((platform == 3 && encoding is 1 or 10 && (subtable is null || encoding == 10)) || (platform == 0 && subtable is null))
            {
                subtable = offset + (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 4));
            }
        }

        if (subtable is not { } start)
        {
            return glyphs;
        }

        switch (Word(font, start))
        {
            case 4:
                var segments = Word(font, start + 6) / 2;
                var ends = start + 14;
                var starts = ends + (segments * 2) + 2;
                var deltas = starts + (segments * 2);
                var offsets = deltas + (segments * 2);
                for (var s = 0; s < segments; s++)
                {
                    var (first, last) = (Word(font, starts + (s * 2)), Word(font, ends + (s * 2)));
                    var delta = (short)Word(font, deltas + (s * 2));
                    var rangeOffset = Word(font, offsets + (s * 2));
                    for (var c = first; c <= last && c != 0xFFFF; c++)
                    {
                        ushort glyph;
                        if (rangeOffset == 0)
                        {
                            glyph = (ushort)(c + delta);
                        }
                        else
                        {
                            var at = offsets + (s * 2) + rangeOffset + ((c - first) * 2);
                            glyph = Word(font, at);
                            glyph = glyph == 0 ? (ushort)0 : (ushort)(glyph + delta);
                        }

                        if (glyph != 0)
                        {
                            glyphs[c] = glyph;
                        }
                    }
                }

                break;
            case 12:
                var groups = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(start + 12));
                for (var g = 0; g < groups; g++)
                {
                    var group = font.AsSpan(start + 16 + (g * 12));
                    var (first, last, glyph) = (BinaryPrimitives.ReadUInt32BigEndian(group), BinaryPrimitives.ReadUInt32BigEndian(group[4..]), BinaryPrimitives.ReadUInt32BigEndian(group[8..]));
                    for (var c = first; c <= last && c <= 0x10FFFF; c++)
                    {
                        glyphs[(int)c] = (ushort)(glyph + (c - first));
                    }
                }

                break;
        }

        return glyphs;
    }

    private static ushort Word(byte[] font, int at) => BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(at));
}
