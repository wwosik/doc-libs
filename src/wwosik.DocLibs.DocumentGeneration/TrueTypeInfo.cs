using System.Buffers.Binary;
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

    /// <summary>The typographic family name (name ID 16), else the family name (1), preferring Windows Unicode records.</summary>
    public static string? FamilyName(ReadOnlySpan<byte> font) => Name(font, TypographicFamily) ?? Name(font, Family);

    private static string? Name(ReadOnlySpan<byte> font, ushort nameId)
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

    private static (int Offset, int Length)? Table(ReadOnlySpan<byte> font, string tag)
    {
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
