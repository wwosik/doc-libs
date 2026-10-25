using System.Buffers.Binary;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Embeds a brand's fonts in a deck the way PowerPoint does: each face as an uncompressed Embedded OpenType in a font
/// part, listed in <c>p:embeddedFontLst</c> under its family name. The text family always, the table and code
/// families only when a slide uses them; whole faces, each file once. A face whose licence restricts embedding
/// (<c>fsType</c> 2) is left out, and its family with it if it is the regular face.
/// </summary>
internal static class FontEmbedding
{
    private const ushort Restricted = 0x0002;
    private const ushort Permissions = 0x000E;

    public static void Embed(PresentationPart presentationPart, ResolvedFonts fonts, string codeFont)
    {
        var presentation = presentationPart.Presentation ?? throw new InvalidDataException("ppt/presentation.xml is empty.");
        var used = presentationPart.SlideParts
            .SelectMany(s => s.Slide?.Descendants<A.LatinFont>() ?? [])
            .Select(f => f.Typeface?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var families = new List<(string Family, byte[] Regular, byte[]? Bold, byte[]? Italic, byte[]? BoldItalic)>
        {
            (fonts.Family, fonts.Regular, fonts.Bold, fonts.Italic, fonts.BoldItalic),
        };
        if (fonts.HasTableFonts && used.Contains(fonts.TableFamily))
        {
            families.Add((fonts.TableFamily, fonts.TableRegular, fonts.TableBold, fonts.TableItalic, fonts.TableBoldItalic));
        }

        if (fonts is { Mono: { } mono, MonoFamily: { } monoFamily } && monoFamily == codeFont && used.Contains(codeFont))
        {
            families.Add((monoFamily, mono, fonts.MonoBold, null, null));
        }

        var parts = new Dictionary<byte[], string>(ReferenceEqualityComparer.Instance);
        string? Part(byte[]? face, params byte[]?[] earlier)
        {
            // A face that falls back to another of its family is not a face of its own.
            if (face is null || earlier.Any(e => ReferenceEquals(e, face)) || IsRestricted(face))
            {
                return null;
            }

            if (!parts.TryGetValue(face, out var id))
            {
                var part = presentationPart.AddFontPart(FontPartType.FontData);
                using (var stream = new MemoryStream(EmbeddedOpenType(face)))
                {
                    part.FeedData(stream);
                }

                id = parts[face] = presentationPart.GetIdOfPart(part);
            }

            return id;
        }

        var list = presentation.EmbeddedFontList ??= new P.EmbeddedFontList();
        var added = 0;
        foreach (var (family, regular, bold, italic, boldItalic) in families.DistinctBy(f => f.Family, StringComparer.Ordinal))
        {
            if (Part(regular) is not { } regularId)
            {
                continue;
            }

            foreach (var existing in list.Elements<P.EmbeddedFont>().Where(e => e.Font?.Typeface?.Value == family).ToList())
            {
                existing.Remove();
            }

            var font = new P.EmbeddedFont(new P.Font { Typeface = family }, new P.RegularFont { Id = regularId });
            if (Part(bold, regular) is { } boldId)
            {
                font.Append(new P.BoldFont { Id = boldId });
            }

            if (Part(italic, regular, bold) is { } italicId)
            {
                font.Append(new P.ItalicFont { Id = italicId });
            }

            if (Part(boldItalic, regular, bold, italic) is { } boldItalicId)
            {
                font.Append(new P.BoldItalicFont { Id = boldItalicId });
            }

            list.Append(font);
            added++;
        }

        if (!list.HasChildren)
        {
            presentation.EmbeddedFontList = null;
        }

        if (added > 0)
        {
            presentation.EmbedTrueTypeFonts = true;
        }
    }

    // Restricted licence embedding: bit 1 alone; a less restrictive bit set with it wins.
    private static bool IsRestricted(byte[] face) => (TrueTypeInfo.EmbeddingFlags(face) & Permissions) == Restricted;

    /// <summary>
    /// The font as an Embedded OpenType, version <c>0x00020001</c>, without subsetting, compression or XOR: a
    /// little-endian header filled from the font's <c>OS/2</c>, <c>head</c> and <c>name</c> tables, then the font.
    /// </summary>
    internal static byte[] EmbeddedOpenType(byte[] font)
    {
        var os2 = TrueTypeInfo.Table(font, "OS/2") is { Length: >= 86 } o ? o.Offset : throw new InvalidDataException("The font has no OS/2 table of version 1 or later.");
        var head = TrueTypeInfo.Table(font, "head") is { Length: >= 12 } h ? h.Offset : throw new InvalidDataException("The font has no head table.");
        ushort Word(int offset) => BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset));
        uint Long(int offset) => BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offset));

        using var header = new MemoryStream();
        using (var writer = new BinaryWriter(header, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(0u); // EOTSize, set below
            writer.Write((uint)font.Length);
            writer.Write(0x00020001u);
            writer.Write(0u); // Flags: no subsetting, compression or XOR
            writer.Write(font, os2 + 32, 10); // PANOSE
            writer.Write((byte)1); // DEFAULT_CHARSET
            writer.Write((byte)(Word(os2 + 62) & 1)); // the italic bit of fsSelection
            writer.Write((uint)Word(os2 + 4)); // usWeightClass
            writer.Write(Word(os2 + 8)); // fsType
            writer.Write((ushort)0x504C); // MagicNumber
            for (var i = 0; i < 4; i++)
            {
                writer.Write(Long(os2 + 42 + (4 * i))); // ulUnicodeRange1–4
            }

            for (var i = 0; i < 2; i++)
            {
                writer.Write(Long(os2 + 78 + (4 * i))); // ulCodePageRange1–2
            }

            writer.Write(Long(head + 8)); // checkSumAdjustment
            for (var i = 0; i < 4; i++)
            {
                writer.Write(0u); // Reserved1–4
            }

            foreach (ushort nameId in new ushort[] { 1, 2, 5, 4 }) // family, style, version, full name
            {
                var name = Encoding.Unicode.GetBytes(TrueTypeInfo.Name(font, nameId) ?? string.Empty);
                writer.Write((ushort)0); // padding
                writer.Write((ushort)name.Length);
                writer.Write(name);
            }

            writer.Write((ushort)0); // padding
            writer.Write((ushort)0); // RootStringSize: no root string
            writer.Write(font);
        }

        var eot = header.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(eot, (uint)eot.Length);
        return eot;
    }
}
