using System.Buffers.Binary;

namespace wwosik.DocLibs.DocumentGeneration;

internal enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
}

/// <summary>Format and pixel size of a PNG, JPEG or GIF, read from its header.</summary>
internal readonly record struct ImageInfo(ImageFormat Format, int Width, int Height)
{
    public static ImageInfo? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Sized(ImageFormat.Png, BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
        }

        if (data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
        {
            return Sized(ImageFormat.Gif, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
        }

        return data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8 ? Jpeg(data) : null;
    }

    private static ImageInfo? Jpeg(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = data[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return Sized(ImageFormat.Jpeg, BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
            }

            i += marker is 0xFF or 0x01 or (>= 0xD0 and <= 0xD8) ? (marker == 0xFF ? 1 : 2) : 2 + BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
        }

        return null;
    }

    private static ImageInfo? Sized(ImageFormat format, int width, int height) =>
        width > 0 && height > 0 ? new ImageInfo(format, width, height) : null;
}
