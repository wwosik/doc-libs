using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Small PNGs of any size, made in the test, so a test can ask for a portrait or a landscape image.</summary>
internal static class TestImages
{
    private static readonly uint[] CrcTable = [.. Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c;
    })];

    /// <summary>An RGB PNG of <paramref name="width"/> × <paramref name="height"/> in one colour.</summary>
    public static DocumentResource Png(string name, int width, int height, byte r = 200, byte g = 80, byte b = 40)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // no filter
            for (var x = 0; x < width; x++)
            {
                raw.Write([r, g, b]);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return new DocumentResource(name, "image/png", png.ToArray());
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        var crc = 0xFFFFFFFFu;
        foreach (var value in typed)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        BinaryPrimitives.WriteUInt32BigEndian(length, crc ^ 0xFFFFFFFFu);
        png.Write(length);
    }
}
