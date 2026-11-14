using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// Embeds a brand's fonts the way Word does: each face as an obfuscated font (<c>.odttf</c>) part of the font table,
/// referenced by <c>w:embedRegular</c>, <c>w:embedBold</c>, <c>w:embedItalic</c> and <c>w:embedBoldItalic</c> with the
/// key it is obfuscated with, and <c>w:embedTrueTypeFonts</c> in the settings. The text family always; the table and
/// code families only when the document's styles or text name them. Whole faces, each file once. A face whose licence
/// restricts embedding is left out, and its family with it if it is the regular face (<see cref="TrueTypeInfo.IsEmbeddingRestricted"/>).
/// </summary>
internal static class DocxFontEmbedding
{
    public static void Embed(MainDocumentPart main, ResolvedFonts fonts, string codeFont)
    {
        var used = UsedFamilies(main);
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

        var table = main.FontTablePart ?? main.AddNewPart<FontTablePart>();
        var list = table.Fonts ??= new Fonts();
        var parts = new Dictionary<byte[], (string Id, string Key)>(ReferenceEqualityComparer.Instance);
        (string Id, string Key)? Part(byte[]? face, params byte[]?[] earlier)
        {
            // A face that falls back to another of its family is not a face of its own.
            if (face is null || earlier.Any(e => ReferenceEquals(e, face)) || TrueTypeInfo.IsEmbeddingRestricted(face))
            {
                return null;
            }

            if (!parts.TryGetValue(face, out var found))
            {
                var key = Key(face);
                var part = table.AddFontPart(FontPartType.FontOdttf);
                using (var stream = new MemoryStream(Obfuscate(face, key)))
                {
                    part.FeedData(stream);
                }

                found = parts[face] = (table.GetIdOfPart(part), key);
            }

            return found;
        }

        var added = 0;
        foreach (var (family, regular, bold, italic, boldItalic) in families.DistinctBy(f => f.Family, StringComparer.Ordinal))
        {
            if (Part(regular) is not { } regularPart)
            {
                continue;
            }

            var font = list.Elements<Font>().FirstOrDefault(f => f.Name?.Value == family);
            if (font is null)
            {
                font = new Font(new FontCharSet { Val = "00" }, new FontFamily { Val = FontFamilyValues.Auto }, new Pitch { Val = FontPitchValues.Variable })
                {
                    Name = family,
                };
                list.Append(font);
            }

            foreach (var old in font.ChildElements.Where(e => e is EmbedRegularFont or EmbedBoldFont or EmbedItalicFont or EmbedBoldItalicFont).ToList())
            {
                old.Remove();
            }

            font.Append(new EmbedRegularFont { Id = regularPart.Id, FontKey = regularPart.Key });
            if (Part(bold, regular) is { } boldPart)
            {
                font.Append(new EmbedBoldFont { Id = boldPart.Id, FontKey = boldPart.Key });
            }

            if (Part(italic, regular, bold) is { } italicPart)
            {
                font.Append(new EmbedItalicFont { Id = italicPart.Id, FontKey = italicPart.Key });
            }

            if (Part(boldItalic, regular, bold, italic) is { } boldItalicPart)
            {
                font.Append(new EmbedBoldItalicFont { Id = boldItalicPart.Id, FontKey = boldItalicPart.Key });
            }

            added++;
        }

        list.Save();
        if (added > 0)
        {
            DocxSettings.Set(main, new EmbedTrueTypeFonts());
        }
    }

    /// <summary>
    /// The font obfuscated as ECMA-376 Part 1, 17.8.1 says: its first 32 bytes XORed with the bytes of
    /// <paramref name="key"/> (a GUID in braces) read from its last hex pair to its first. Obfuscating twice restores
    /// the font.
    /// </summary>
    internal static byte[] Obfuscate(byte[] font, string key)
    {
        var digits = key.Replace("{", string.Empty, StringComparison.Ordinal).Replace("}", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            bytes[i] = byte.Parse(digits.AsSpan(30 - (2 * i), 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        var result = (byte[])font.Clone();
        for (var i = 0; i < Math.Min(32, result.Length); i++)
        {
            result[i] ^= bytes[i % 16];
        }

        return result;
    }

    // A key from the face's content, so that the same face gives the same document.
    private static string Key(byte[] face) =>
        "{" + new Guid(SHA256.HashData(face).AsSpan(0, 16)).ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant() + "}";

    // The families the document's styles, text, headers and footers name in w:rFonts, and the theme's fonts.
    private static HashSet<string> UsedFamilies(MainDocumentPart main)
    {
        var roots = new OpenXmlPartRootElement?[] { main.Document, main.StyleDefinitionsPart?.Styles }
            .Concat(main.HeaderParts.Select(h => (OpenXmlPartRootElement?)h.Header))
            .Concat(main.FooterParts.Select(f => (OpenXmlPartRootElement?)f.Footer));
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fonts in roots.OfType<OpenXmlPartRootElement>().SelectMany(r => r.Descendants<RunFonts>()))
        {
            foreach (var name in new[] { fonts.Ascii?.Value, fonts.HighAnsi?.Value, fonts.ComplexScript?.Value, fonts.EastAsia?.Value })
            {
                if (name is not null)
                {
                    used.Add(name);
                }
            }
        }

        return used;
    }
}
