namespace wwosik.DocLibs.DocumentConversions;

/// <summary>The result of a conversion: Markdown, the resources it references, and document metadata.</summary>
/// <param name="Markdown">The Markdown text, with <c>\n</c> line endings and one trailing newline, or empty when the document has no extractable content.</param>
/// <param name="Resources">Images and other files the Markdown references by their <see cref="DocumentResource.Path"/>.</param>
/// <param name="Metadata">Document properties such as title and author; the keys are listed in <see cref="MetadataKeys"/>.</param>
public sealed record MarkdownDocument(
    string Markdown,
    IReadOnlyList<DocumentResource> Resources,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>
    /// The resource an image reference in the Markdown points at, e.g. <c>./images/a%20b.png</c>, or
    /// <see langword="null"/>. Both sides are compared with <c>%</c> escapes decoded, <c>/</c> as separator and
    /// a leading <c>./</c> dropped.
    /// </summary>
    public DocumentResource? FindResource(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var wanted = NormalizeReference(reference);
        return this.Resources.FirstOrDefault(r => string.Equals(NormalizeReference(r.Path), wanted, StringComparison.Ordinal));
    }

    private static string NormalizeReference(string path)
    {
        var normalized = Uri.UnescapeDataString(path.Trim()).Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}

/// <summary>A file the Markdown references by its relative path, e.g. <c>![](images/image1.png)</c>.</summary>
/// <param name="Path">The relative path as it appears in the Markdown, using <c>/</c>.</param>
/// <param name="ContentType">The MIME type, e.g. <c>image/png</c>.</param>
/// <param name="Content">The file content.</param>
public sealed record DocumentResource(string Path, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>The keys converters use in <see cref="MarkdownDocument.Metadata"/>. A key is absent when its value is unknown.</summary>
public static class MetadataKeys
{
    /// <summary>The document title from its properties.</summary>
    public const string Title = "title";

    /// <summary>The author from the document properties.</summary>
    public const string Author = "author";

    /// <summary>Number of pages (PDF).</summary>
    public const string PageCount = "pageCount";

    /// <summary>Number of slides (presentations).</summary>
    public const string SlideCount = "slideCount";

    /// <summary>Number of worksheets (workbooks).</summary>
    public const string SheetCount = "sheetCount";

    /// <summary>Number of images that could not be decoded and were left out.</summary>
    public const string SkippedImages = "skippedImages";
}
