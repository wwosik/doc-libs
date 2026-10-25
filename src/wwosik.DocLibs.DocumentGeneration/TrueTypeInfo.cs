using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration;

internal enum FontOutlines
{
    Unknown,
    TrueType,
    Cff,
    Collection,
}

/// <summary>What the generators need to know about a font file before using it, read from its header.</summary>
internal static class TrueTypeInfo
{
    private const ushort TypographicFamily = 16;
    private const ushort Family = 1;

    public static FontOutlines Outlines(ReadOnlySpan<byte> font)
    {
        if (font.Length < 12)
        {
            return FontOutlines.Unknown;
        }

        return BinaryPrimitives.ReadUInt32BigEndian(font) switch
        {
            0x00010000 or 0x74727565 => Table(font, "glyf") is null ? FontOutlines.Unknown : FontOutlines.TrueType, // 1.0, 'true'
            0x4F54544F => FontOutlines.Cff,                                                                        // 'OTTO'
            0x74746366 => FontOutlines.Collection,                                                                 // 'ttcf'
            _ => FontOutlines.Unknown,
        };
    }

    /// <summary>
    /// The family name (name ID 1), else the typographic family name (16), preferring Windows Unicode records. Name ID 1
    /// is what Windows groups regular, bold, italic and bold italic under, and what PowerPoint finds an embedded font by.
    /// </summary>
    public static string? FamilyName(ReadOnlySpan<byte> font) => Name(font, Family) ?? Name(font, TypographicFamily);

    /// <summary>
    /// The embedding permissions (<c>OS/2 fsType</c>): 0 installable, bit 1 restricted, bit 2 preview and print, bit 3
    /// editable. 0 when the font has no <c>OS/2</c> table.
    /// </summary>
    public static ushort EmbeddingFlags(ReadOnlySpan<byte> font) =>
        Table(font, "OS/2") is var (offset, length) && length >= 10 && offset + 10 <= font.Length
            ? BinaryPrimitives.ReadUInt16BigEndian(font[(offset + 8)..])
            : (ushort)0;

    /// <summary>The name with <paramref name="nameId"/>, preferring Windows Unicode records; <see langword="null"/> if there is none.</summary>
    public static string? Name(ReadOnlySpan<byte> font, ushort nameId)
    {
        if (Table(font, "name") is not var (offset, length) || offset + 6 > font.Length)
        {
            return null;
        }

        var table = font.Slice(offset, Math.Min(length, font.Length - offset));
        var count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        var strings = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
        string? fallback = null;
        for (var i = 0; i < count && 6 + ((i + 1) * 12) <= table.Length; i++)
        {
            var record = table.Slice(6 + (i * 12), 12);
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            if (BinaryPrimitives.ReadUInt16BigEndian(record[6..]) != nameId)
            {
                continue;
            }

            var size = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var start = strings + BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            if (start + size > table.Length)
            {
                continue;
            }

            var bytes = table.Slice(start, size);
            if (platform is 0 or 3)
            {
                return Encoding.BigEndianUnicode.GetString(bytes);
            }

            fallback ??= platform == 1 ? Encoding.Latin1.GetString(bytes) : null;
        }

        return fallback;
    }

    /// <summary>Whether the font maps <paramref name="codePoint"/> to a glyph, by its Unicode <c>cmap</c> (format 4 or 12).</summary>
    public static bool HasCharacter(byte[] font, int codePoint) => CharacterMaps.GetValue(font, ReadCharacters).Contains(codePoint);

    private static readonly ConditionalWeakTable<byte[], HashSet<int>> CharacterMaps = new();

    private static HashSet<int> ReadCharacters(byte[] font)
    {
        var characters = new HashSet<int>();
        if (Table(font, "cmap") is not var (offset, _) || offset + 4 > font.Length)
        {
            return characters;
        }

        // The Windows Unicode subtable: full repertoire (3, 10) wins over the BMP (3, 1).
        int? subtable = null;
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset + 2));
        for (var i = 0; i < count && offset + 12 + (i * 8) <= font.Length; i++)
        {
            var record = font.AsSpan(offset + 4 + (i * 8));
            var (platform, encoding) = (BinaryPrimitives.ReadUInt16BigEndian(record), BinaryPrimitives.ReadUInt16BigEndian(record[2..]));
            if (platform == 3 && encoding is 1 or 10 && (subtable is null || encoding == 10))
            {
                subtable = offset + (int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
            }
        }

        if (subtable is not { } start || start + 16 > font.Length)
        {
            return characters;
        }

        ushort Word(int at) => BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(at));
        switch (Word(start))
        {
            case 4:
                var segments = Word(start + 6) / 2;
                for (var i = 0; i < segments; i++)
                {
                    var (first, last) = (Word(start + 16 + (segments * 2) + (i * 2)), Word(start + 14 + (i * 2)));
                    for (var c = first; c <= last && c != 0xFFFF; c++)
                    {
                        characters.Add(c);
                    }
                }

                break;
            case 12:
                var groups = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(start + 12));
                for (var i = 0; i < groups; i++)
                {
                    var group = font.AsSpan(start + 16 + (i * 12));
                    for (var c = BinaryPrimitives.ReadUInt32BigEndian(group); c <= BinaryPrimitives.ReadUInt32BigEndian(group[4..]); c++)
                    {
                        characters.Add((int)c);
                    }
                }

                break;
        }

        return characters;
    }

    /// <summary>Where the table <paramref name="tag"/> is in the file; <see langword="null"/> if it has none.</summary>
    public static (int Offset, int Length)? Table(ReadOnlySpan<byte> font, string tag)
    {
        if (font.Length < 12)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);
        for (var i = 0; i < count && 12 + ((i + 1) * 16) <= font.Length; i++)
        {
            var entry = font.Slice(12 + (i * 16), 16);
            if (entry[0] == tag[0] && entry[1] == tag[1] && entry[2] == tag[2] && entry[3] == tag[3])
            {
                return ((int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]));
            }
        }

        return null;
    }
}
