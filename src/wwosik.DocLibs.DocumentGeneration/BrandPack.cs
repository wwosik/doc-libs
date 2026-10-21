using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Loads a <see cref="Brand"/> from a brand pack: a <c>brand.json</c> and the image, font and template files it
/// names, by paths relative to the pack. The pack can be a directory, a zip, embedded resources, or anything a
/// callback can open. Unknown keys, missing files, and paths leading out of the pack throw
/// <see cref="InvalidDataException"/> naming the key; the files themselves are checked when a generator uses the
/// brand, like any other brand.
/// </summary>
public static class BrandPack
{
    /// <summary>The file every brand pack has at its root.</summary>
    public const string ManifestFile = "brand.json";

    /// <summary>
    /// Loads a brand pack through <paramref name="openFile"/>, which is called with a path relative to the pack
    /// (<c>/</c>-separated, never with <c>..</c>) and returns the file's content, or <see langword="null"/> if
    /// there is no such file. Returned streams are read to the end and disposed.
    /// </summary>
    /// <exception cref="InvalidDataException">The pack is invalid; the message names the key.</exception>
    public static Brand Load(Func<string, Stream?> openFile)
    {
        ArgumentNullException.ThrowIfNull(openFile);
        var json = Read(openFile, ManifestFile, "the brand pack has no " + ManifestFile);
        BrandManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(json, BrandManifestContext.Default.BrandManifest)
                ?? throw new InvalidDataException($"{ManifestFile} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{ManifestFile}: {ex.Message}", ex);
        }

        return manifest.ToBrand(openFile);
    }

    /// <summary>Loads the brand pack in <paramref name="directory"/>. Files outside it, also through links, are refused.</summary>
    /// <exception cref="InvalidDataException">The pack is invalid; the message names the key.</exception>
    public static Brand Load(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Load(path =>
        {
            var file = Path.GetFullPath(Path.Combine(root, path));
            if (!File.Exists(file))
            {
                return null;
            }

            var target = new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file;
            if (!Inside(root, file) || !Inside(root, target))
            {
                throw new InvalidDataException($"'{path}' leads out of the brand pack.");
            }

            return File.OpenRead(target);
        });
    }

    /// <summary>Loads the brand pack in a zip archive, with <c>brand.json</c> at the archive's root. The stream is left open.</summary>
    /// <exception cref="InvalidDataException">The pack is invalid; the message names the key.</exception>
    public static Brand Load(Stream zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return Load(path => archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == path)?.Open());
    }

    /// <summary>
    /// Loads a brand pack embedded in <paramref name="assembly"/>: each file is the resource named
    /// <paramref name="resourcePrefix"/> followed by its path, with <c>/</c>, <c>\</c> or <c>.</c> between folders —
    /// e.g. <c>LogicalName="Brand.%(RecursiveDir)%(Filename)%(Extension)"</c> with the prefix <c>Brand.</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">The pack is invalid; the message names the key.</exception>
    public static Brand Load(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourcePrefix);
        return Load(path => new[] { path, path.Replace('/', '\\'), path.Replace('/', '.') }
            .Select(name => assembly.GetManifestResourceStream(resourcePrefix + name))
            .FirstOrDefault(stream => stream is not null));
    }

    internal static byte[] Read(Func<string, Stream?> openFile, string path, string missing)
    {
        using var stream = openFile(path) ?? throw new InvalidDataException(missing + ".");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool Inside(string root, string path) =>
        path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

/// <summary>The content of <c>brand.json</c>.</summary>
internal sealed record BrandManifest(
    string? Name,
    string? FooterText,
    BrandManifest.ColorsSection? Colors,
    BrandManifest.ImagesSection? Images,
    BrandManifest.FontsSection? Fonts,
    BrandManifest.PptxSection? Pptx)
{
    public Brand ToBrand(Func<string, Stream?> openFile)
    {
        BrandImage? Image(string key, string? path) => path is null ? null : new(File(openFile, key, path));
        BrandFont? Font(string key, string? path) => path is null ? null : new(File(openFile, key, path));
        BrandFont RequiredFont(string key, string? path) => Font(key, path) ?? throw Missing(key);

        var colors = this.Colors ?? throw Missing("colors");
        return new Brand
        {
            Name = this.Name ?? throw Missing("name"),
            FooterText = this.FooterText,
            Colors = new BrandColors
            {
                Primary = colors.Primary ?? throw Missing("colors.primary"),
                Link = colors.Link,
                Footer = colors.Footer,
                TableHeader = colors.Table?.Header,
                TableBand = colors.Table?.Band,
                TableLine = colors.Table?.Line,
                Gradient = colors.Gradient,
                Theme = colors.Theme,
                Diagram = colors.Diagram is not { } diagram ? null : new BrandDiagramColors
                {
                    Text = diagram.Text,
                    Line = diagram.Line,
                    Accent = diagram.Accent,
                    Surface = diagram.Surface,
                    Border = diagram.Border,
                    Palette = diagram.Palette,
                },
            },
            Logo = Image("images.logo", this.Images?.Logo),
            LogoOnDark = Image("images.logoOnDark", this.Images?.LogoOnDark),
            CoverArtwork = Image("images.coverArtwork", this.Images?.CoverArtwork),
            Tagline = Image("images.tagline", this.Images?.Tagline),
            Fonts = this.Fonts is not { } fonts ? null : new BrandFonts
            {
                Regular = RequiredFont("fonts.regular", fonts.Regular),
                Bold = RequiredFont("fonts.bold", fonts.Bold),
                Italic = RequiredFont("fonts.italic", fonts.Italic),
                BoldItalic = RequiredFont("fonts.boldItalic", fonts.BoldItalic),
                Mono = Font("fonts.mono", fonts.Mono),
                MonoBold = Font("fonts.monoBold", fonts.MonoBold),
                Family = fonts.Family,
                Table = fonts.Table is not { } table ? null : new BrandTableFonts
                {
                    Regular = RequiredFont("fonts.table.regular", table.Regular),
                    Bold = RequiredFont("fonts.table.bold", table.Bold),
                    Italic = Font("fonts.table.italic", table.Italic),
                    BoldItalic = Font("fonts.table.boldItalic", table.BoldItalic),
                    Family = table.Family,
                },
            },
            Pptx = this.Pptx is not { } pptx ? null : new PptxBrandTemplate(File(openFile, "pptx.template", pptx.Template ?? throw Missing("pptx.template")))
            {
                TitleLayout = pptx.Layouts?.Title,
                ContentLayout = pptx.Layouts?.Content,
                TitleOnlyLayout = pptx.Layouts?.TitleOnly,
                SectionHeaderLayout = pptx.Layouts?.SectionHeader,
            },
        };
    }

    private static byte[] File(Func<string, Stream?> openFile, string key, string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || Path.IsPathRooted(path)
            || normalized.Split('/').Any(segment => segment is ".." or "." or ""))
        {
            throw new InvalidDataException($"{BrandPack.ManifestFile}: '{key}' is '{path}'; expected a path inside the brand pack, without '..'.");
        }

        try
        {
            return BrandPack.Read(openFile, normalized, $"{BrandPack.ManifestFile}: '{key}' names '{path}', which is not in the brand pack");
        }
        catch (InvalidDataException ex) when (!ex.Message.StartsWith(BrandPack.ManifestFile, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{BrandPack.ManifestFile}: '{key}': {ex.Message}", ex);
        }
    }

    private static InvalidDataException Missing(string key) => new($"{BrandPack.ManifestFile}: '{key}' is missing.");

    internal sealed record ColorsSection(
        string? Primary,
        string? Link,
        string? Footer,
        TableSection? Table,
        IReadOnlyList<string>? Gradient,
        IReadOnlyList<string>? Theme,
        DiagramSection? Diagram);

    internal sealed record DiagramSection(string? Text, string? Line, string? Accent, string? Surface, string? Border, IReadOnlyList<string>? Palette);

    internal sealed record TableSection(string? Header, string? Band, string? Line);

    internal sealed record ImagesSection(string? Logo, string? LogoOnDark, string? CoverArtwork, string? Tagline);

    internal sealed record FontsSection(
        string? Regular,
        string? Bold,
        string? Italic,
        string? BoldItalic,
        string? Mono,
        string? MonoBold,
        string? Family,
        TableFontsSection? Table);

    internal sealed record TableFontsSection(string? Regular, string? Bold, string? Italic, string? BoldItalic, string? Family);

    internal sealed record PptxSection(string? Template, LayoutsSection? Layouts);

    internal sealed record LayoutsSection(string? Title, string? Content, string? TitleOnly, string? SectionHeader);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(BrandManifest))]
internal sealed partial class BrandManifestContext : JsonSerializerContext;
