namespace wwosik.DocLibs.DocumentConversions;

/// <summary>Options for <see cref="IDocumentConverter.Convert"/>.</summary>
public sealed record ConversionOptions
{
    /// <summary>The default options.</summary>
    public static ConversionOptions Default { get; } = new();

    /// <summary>Prefix of every resource path, and so of every image reference in the Markdown. Default <c>images/</c>.</summary>
    public string ResourcePathPrefix { get; init; } = "images/";

    /// <summary>Whether images are extracted and referenced. Default <see langword="true"/>.</summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>
    /// Where converters that need files on disk (Office automation) create their per-call working
    /// directory. <see langword="null"/> means a subfolder of <see cref="Path.GetTempPath"/>.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// The original file name, if known. Converters that detect the format themselves need it for OLE
    /// files, whose content does not tell <c>.doc</c> from <c>.xls</c>. <see cref="DocumentConverterRegistry"/> fills it in.
    /// </summary>
    public string? FileName { get; init; }
}
