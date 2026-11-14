using System.Collections.Concurrent;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The bundled Liberation fonts 2.1.5 (SIL OFL 1.1, <c>Fonts/OFL.txt</c>): Liberation Sans has the letter widths of
/// Arial and Helvetica, Liberation Mono those of Courier New. PDFs embed them by default; the HTML deck measures text
/// in them when a deck names a family it does not embed.
/// </summary>
internal static class StandInFonts
{
    private static readonly ConcurrentDictionary<string, byte[]> Loaded = new();

    public static byte[] SansRegular => Load("LiberationSans-Regular.ttf");

    public static byte[] SansBold => Load("LiberationSans-Bold.ttf");

    public static byte[] SansItalic => Load("LiberationSans-Italic.ttf");

    public static byte[] SansBoldItalic => Load("LiberationSans-BoldItalic.ttf");

    public static byte[] MonoRegular => Load("LiberationMono-Regular.ttf");

    public static byte[] MonoBold => Load("LiberationMono-Bold.ttf");

    /// <summary>Each file is read once, so every caller gets the same <c>byte[]</c> (PDFs key their font copies by reference).</summary>
    private static byte[] Load(string name) => Loaded.GetOrAdd(name, Resource);

    private static byte[] Resource(string name)
    {
        using var stream = typeof(StandInFonts).Assembly.GetManifestResourceStream("Fonts." + name)
            ?? throw new InvalidOperationException($"The embedded font '{name}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
