using System.Buffers.Binary;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>
/// The faces text is measured with, found in order: the deck's embedded fonts (the TrueType file inside each EOT), the
/// caller's fonts, then the bundled stand-ins (Liberation Sans for Arial and Helvetica, Liberation Mono for Courier New
/// and Consolas). A family with none is measured by <see cref="TextMetrics"/>; both are reported as
/// <c>font-missing</c>, except a stand-in that has the original's widths.
/// </summary>
internal sealed class FontLibrary
{
    private const double EmuPerPoint = 12700;

    // Stand-ins by family, and whether they have the original's letter widths.
    private static readonly Dictionary<string, (bool Mono, bool Compatible)> StandIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arial"] = (false, true),
        ["Helvetica"] = (false, true),
        ["Arimo"] = (false, true),
        ["Liberation Sans"] = (false, true),
        ["Courier New"] = (true, true),
        ["Courier"] = (true, true),
        ["Cousine"] = (true, true),
        ["Liberation Mono"] = (true, true),
        ["Consolas"] = (true, false),
    };

    private readonly List<SceneFace> faces = [];
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SceneFace?> found = [];
    private readonly RenderNotes notes;

    public FontLibrary(IEnumerable<(string Family, byte[] File, bool? Bold, bool? Italic)> embedded, IEnumerable<ReadOnlyMemory<byte>> passed, RenderNotes notes)
    {
        this.notes = notes;
        foreach (var (family, file, bold, italic) in embedded)
        {
            this.Add(family, file, bold, italic, FaceSource.Embedded);
        }

        foreach (var file in passed)
        {
            var bytes = file.ToArray();
            if (TrueTypeInfo.FamilyName(bytes) is { } family)
            {
                this.Add(family, bytes, null, null, FaceSource.Passed);
            }
        }
    }

    /// <summary>The faces that measured some text, each once, in the order they were first used.</summary>
    public List<SceneFace> Used { get; } = [];

    /// <summary>The stand-in's family name for a family the deck names, if it has one.</summary>
    public static string? StandInFamily(string family) =>
        StandIns.TryGetValue(family, out var standIn) ? standIn.Mono ? "Liberation Mono" : "Liberation Sans" : null;

    /// <summary>The face <paramref name="family"/> is measured with in that weight and style, or <see langword="null"/> for an estimate.</summary>
    public SceneFace? Face(string family, bool bold, bool italic)
    {
        if (this.found.TryGetValue((family, bold, italic), out var cached))
        {
            return cached;
        }

        var face = this.Find(family, bold, italic);
        this.found[(family, bold, italic)] = face;
        if (face is not null && !this.Used.Contains(face))
        {
            this.Used.Add(face);
        }

        return face;
    }

    /// <summary>The width of <paramref name="text"/> at <paramref name="size"/> EMU in <paramref name="font"/>, in EMU.</summary>
    public static double Width(SceneFont font, string text, double size) =>
        font.Face is { } face
            ? face.Metrics.Width(text) * size
            : TextMetrics.Width(text, (int)Math.Round(size / EmuPerPoint * 100), font.Bold);

    private SceneFace? Find(string family, bool bold, bool italic)
    {
        var own = this.faces.Where(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase) && f.Source != FaceSource.StandIn).ToList();
        if (own.Count > 0)
        {
            if (own.FirstOrDefault(f => f.Bold == bold && f.Italic == italic) is { } exact)
            {
                return exact;
            }

            var fallback = own.FirstOrDefault(f => !f.Bold && !f.Italic) ?? own[0];
            this.notes.FontMissing($"The font '{family}' has no {Style(bold, italic)} face, so that text is measured in its {Style(fallback.Bold, fallback.Italic)} face");
            return fallback;
        }

        if (!StandIns.TryGetValue(family, out var standIn))
        {
            this.notes.FontMissing($"The font '{family}' is neither embedded nor passed and has no stand-in, so its text is measured by estimate");
            return null;
        }

        var file = (standIn.Mono, bold, italic) switch
        {
            (true, true, _) => StandInFonts.MonoBold,
            (true, false, _) => StandInFonts.MonoRegular,
            (false, true, true) => StandInFonts.SansBoldItalic,
            (false, true, false) => StandInFonts.SansBold,
            (false, false, true) => StandInFonts.SansItalic,
            _ => StandInFonts.SansRegular,
        };
        var face = this.faces.FirstOrDefault(f => ReferenceEquals(f.File, file)) ?? this.Add(StandInFamily(family)!, file, null, null, FaceSource.StandIn);
        if (!standIn.Compatible)
        {
            this.notes.FontMissing($"The font '{family}' is measured with its stand-in {face!.Family}, which has other letter widths");
        }
        else if (standIn.Mono && italic)
        {
            this.notes.FontMissing($"The font '{family}' has no italic stand-in, so its italic text is measured upright");
        }

        return face;
    }

    private SceneFace? Add(string family, byte[] file, bool? bold, bool? italic, FaceSource source)
    {
        if (TrueTypeMetrics.For(file) is not { } metrics)
        {
            return null;
        }

        var face = new SceneFace(family, bold ?? metrics.Bold, italic ?? metrics.Italic, file, source, metrics);
        this.faces.Add(face);
        return face;
    }

    private static string Style(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => "bold italic",
        (true, false) => "bold",
        (false, true) => "italic",
        _ => "regular",
    };

    /// <summary>
    /// The TrueType file inside an Embedded OpenType font part (<c>EOTSize</c>, <c>FontDataSize</c>, the data last); <see langword="null"/>
    /// when it is compressed (MicroType Express) or damaged. XOR-obfuscated data is decoded.
    /// </summary>
    public static byte[]? FromEmbeddedOpenType(byte[] eot)
    {
        if (eot.Length < 82)
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(eot);
        var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(eot.AsSpan(4));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(eot.AsSpan(12));
        if (size > eot.Length || dataSize > size || (flags & 0x4) != 0)
        {
            return null;
        }

        var data = eot.AsSpan((int)(size - dataSize), (int)dataSize).ToArray();
        if ((flags & 0x10000000) != 0)
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] ^= 0x50;
            }
        }

        return TrueTypeInfo.Outlines(data) == FontOutlines.TrueType ? data : null;
    }
}
