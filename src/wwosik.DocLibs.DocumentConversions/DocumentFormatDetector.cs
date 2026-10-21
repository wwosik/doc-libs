using System.IO.Compression;

namespace wwosik.DocLibs.DocumentConversions;

/// <summary>Recognizes a document's format from its content; the file name only refines OLE files.</summary>
public static class DocumentFormatDetector
{
    private const int TextProbeLength = 8192;

    private static ReadOnlySpan<byte> OleSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>
    /// Detects the format of <paramref name="input"/>. The stream must be readable and seekable; its
    /// position is restored afterwards.
    /// </summary>
    /// <param name="input">The document.</param>
    /// <param name="fileName">The original file name, if known. Only its extension is used, and only for OLE files.</param>
    public static DocumentFormat Detect(Stream input, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(input));
        }

        var start = input.Position;
        try
        {
            return DetectFrom(input, start, Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant());
        }
        finally
        {
            input.Position = start;
        }
    }

    private static DocumentFormat DetectFrom(Stream input, long start, string extension)
    {
        var probe = new byte[TextProbeLength];
        var length = input.ReadAtLeast(probe, probe.Length, throwOnEndOfStream: false);
        var head = probe.AsSpan(0, length);

        if (head.StartsWith("PK\x03\x04"u8))
        {
            input.Position = start;
            return DetectZip(input);
        }

        if (head.StartsWith(OleSignature))
        {
            return extension switch
            {
                ".doc" or ".dot" => DocumentFormat.Doc,
                ".xls" or ".xlt" or ".xla" => DocumentFormat.Xls,
                ".ppt" or ".pps" or ".pot" => DocumentFormat.Ppt,
                ".msg" => DocumentFormat.Msg,
                _ => DocumentFormat.Ole,
            };
        }

        if (head.StartsWith("%PDF"u8))
        {
            return DocumentFormat.Pdf;
        }

        if (head.StartsWith("{\\rtf"u8))
        {
            return DocumentFormat.Rtf;
        }

        return length > 0 && head.IndexOf((byte)0) < 0 ? DocumentFormat.Text : DocumentFormat.Unknown;
    }

    private static DocumentFormat DetectZip(Stream input)
    {
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.GetEntry("word/document.xml") is not null)
            {
                return DocumentFormat.Docx;
            }

            if (zip.GetEntry("xl/workbook.xml") is not null)
            {
                return DocumentFormat.Xlsx;
            }

            return zip.GetEntry("ppt/presentation.xml") is not null ? DocumentFormat.Pptx : DocumentFormat.Zip;
        }
        catch (InvalidDataException)
        {
            return DocumentFormat.Unknown;
        }
    }
}
