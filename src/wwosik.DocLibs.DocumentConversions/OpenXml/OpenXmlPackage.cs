using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentConversions.OpenXml;

/// <summary>Read access to the parts of an Office Open XML package, without the Open XML SDK.</summary>
internal sealed class OpenXmlPackage : IDisposable
{
    private static readonly XNamespace CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    private readonly ZipArchive zip;

    public OpenXmlPackage(Stream input)
    {
        this.zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
    }

    public XDocument? ReadXml(string partName)
    {
        var entry = this.zip.GetEntry(partName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    public byte[]? ReadBytes(string partName)
    {
        var entry = this.zip.GetEntry(partName);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>The relationships of <paramref name="partName"/>: id → target, internal targets resolved to part names.</summary>
    public Dictionary<string, string> Relationships(string partName)
    {
        var folder = partName.Contains('/') ? partName[..partName.LastIndexOf('/')] : string.Empty;
        var relsName = (folder.Length == 0 ? string.Empty : folder + "/") + "_rels/" + partName[(partName.LastIndexOf('/') + 1)..] + ".rels";
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rels = this.ReadXml(relsName);
        if (rels?.Root is null)
        {
            return map;
        }

        foreach (var rel in rels.Root.Elements())
        {
            var id = (string?)rel.Attribute("Id");
            var target = (string?)rel.Attribute("Target");
            if (id is null || target is null)
            {
                continue;
            }

            var external = string.Equals((string?)rel.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
            map[id] = external ? target : Resolve(folder, target);
        }

        return map;
    }

    /// <summary>Title and author from <c>docProps/core.xml</c>, when present.</summary>
    public void AddCoreProperties(IDictionary<string, string> metadata)
    {
        var core = this.ReadXml("docProps/core.xml")?.Root;
        if (core is null || core.Name != CoreProperties + "coreProperties")
        {
            return;
        }

        if (core.Element(DublinCore + "title")?.Value.Trim() is { Length: > 0 } title)
        {
            metadata[MetadataKeys.Title] = title;
        }

        if (core.Element(DublinCore + "creator")?.Value.Trim() is { Length: > 0 } author)
        {
            metadata[MetadataKeys.Author] = author;
        }
    }

    public void Dispose() => this.zip.Dispose();

    /// <summary>Wraps what a damaged package throws into a <see cref="DocumentConversionException"/>.</summary>
    public static T Guard<T>(DocumentFormat format, Func<T> convert)
    {
        try
        {
            return convert();
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            throw new DocumentConversionException(format, "The package is damaged: " + ex.Message, ex);
        }
    }

    private static string Resolve(string folder, string target)
    {
        if (target.StartsWith('/'))
        {
            return target[1..];
        }

        var parts = new List<string>(folder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in target.Split('/'))
        {
            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (segment is not ("." or ""))
            {
                parts.Add(segment);
            }
        }

        return string.Join('/', parts);
    }
}
