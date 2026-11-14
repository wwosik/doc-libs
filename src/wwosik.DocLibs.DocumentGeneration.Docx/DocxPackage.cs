using System.IO.Compression;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// Turns a Word template (<c>.dotx</c>) into a document by the content type of its main part alone, in
/// <c>[Content_Types].xml</c>. The SDK's <c>ChangeDocumentType</c> also renames the part (<c>word/document2.xml</c>),
/// which Word accepts but readers that expect <c>word/document.xml</c>, such as the DOCX converter, do not.
/// </summary>
internal static class DocxPackage
{
    public const string TemplateType = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
    public const string DocumentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    private const string ContentTypes = "[Content_Types].xml";

    /// <summary>
    /// Replaces the main part's content type <paramref name="from"/> by <paramref name="to"/> in the package in
    /// <paramref name="buffer"/>; returns whether it had <paramref name="from"/>. Anything that is no zip is left alone.
    /// </summary>
    public static bool ChangeMainType(MemoryStream buffer, string from, string to)
    {
        try
        {
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true);
            var entry = archive.GetEntry(ContentTypes);
            if (entry is null)
            {
                return false;
            }

            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                xml = reader.ReadToEnd();
            }

            var marked = $"ContentType=\"{from}\"";
            if (!xml.Contains(marked, StringComparison.Ordinal))
            {
                return false;
            }

            entry.Delete();
            var replaced = archive.CreateEntry(ContentTypes);
            using (var writer = new StreamWriter(replaced.Open(), new UTF8Encoding(false)))
            {
                writer.Write(xml.Replace(marked, $"ContentType=\"{to}\"", StringComparison.Ordinal));
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            buffer.Position = 0;
        }
    }
}
