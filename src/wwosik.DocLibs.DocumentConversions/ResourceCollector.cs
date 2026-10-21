using System.Security.Cryptography;

namespace wwosik.DocLibs.DocumentConversions;

/// <summary>Collects the resources of one conversion; identical content is stored once.</summary>
internal sealed class ResourceCollector(string prefix)
{
    private readonly List<DocumentResource> resources = [];
    private readonly Dictionary<string, string> pathsByHash = new(StringComparer.Ordinal);
    private readonly HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DocumentResource> Resources => this.resources;

    /// <summary>Adds the content under <paramref name="fileName"/> and returns the path to reference it by.</summary>
    public string Add(string fileName, ReadOnlyMemory<byte> content)
    {
        var hash = Convert.ToHexString(SHA256.HashData(content.Span));
        if (this.pathsByHash.TryGetValue(hash, out var existing))
        {
            return existing;
        }

        var path = prefix + fileName;
        for (var n = 2; !this.paths.Add(path); n++)
        {
            path = prefix + Path.GetFileNameWithoutExtension(fileName) + "-" + n + Path.GetExtension(fileName);
        }

        this.pathsByHash[hash] = path;
        this.resources.Add(new DocumentResource(path, ContentTypes.FromFileName(fileName), content));
        return path;
    }
}

internal static class ContentTypes
{
    public static string FromFileName(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" or ".jpe" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".emf" => "image/x-emf",
        ".wmf" => "image/x-wmf",
        _ => "application/octet-stream",
    };
}
