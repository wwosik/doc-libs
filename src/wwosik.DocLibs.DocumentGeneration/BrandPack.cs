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
    BrandManifest.ColorsSection? SlideColors,
    BrandManifest.ImagesSection? Images,
    BrandManifest.FontsSection? Fonts,
    BrandManifest.PptxSection? Pptx,
    BrandManifest.DocxSection? Docx = null)
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
            Colors = ToColors(colors, colors.Primary ?? throw Missing("colors.primary")),
            SlideColors = this.SlideColors is not { } slides ? null : ToColors(slides, slides.Primary ?? colors.Primary!),
            Logo = Image("images.logo", this.Images?.Logo),
            LogoOnDark = Image("images.logoOnDark", this.Images?.LogoOnDark),
            CoverArtwork = Image("images.coverArtwork", this.Images?.CoverArtwork),
            Tagline = Image("images.tagline", this.Images?.Tagline),
            CoverImage = Image("images.coverImage", this.Images?.CoverImage),
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
                ClosingLayout = pptx.Layouts?.Closing,
                Tone = pptx.Tone switch
                {
                    null or "auto" => SlideTone.Auto,
                    "light" => SlideTone.Light,
                    "dark" => SlideTone.Dark,
                    var other => throw new InvalidDataException($"{BrandPack.ManifestFile}: 'pptx.tone' is '{other}'; expected 'auto', 'light' or 'dark'."),
                },
                Roles = ToRoles(pptx),
            },
            Docx = this.Docx is not { } docx ? null : new DocxBrandTemplate(File(openFile, "docx.template", docx.Template ?? throw Missing("docx.template")))
            {
                Styles = ToStyles(docx.Styles),
            },
        };
    }

    private static DocxStyleNames ToStyles(DocxStylesSection? styles) =>
        styles is null ? new() : new()
        {
            Title = styles.Title,
            Subtitle = styles.Subtitle,
            CoverDetails = styles.CoverDetails,
            TocHeading = styles.TocHeading,
            Heading1 = styles.Heading1,
            Heading2 = styles.Heading2,
            Heading3 = styles.Heading3,
            Heading4 = styles.Heading4,
            Heading5 = styles.Heading5,
            Heading6 = styles.Heading6,
            Normal = styles.Normal,
            ListBullet = styles.ListBullet,
            ListNumber = styles.ListNumber,
            ListTask = styles.ListTask,
            DefinitionTerm = styles.DefinitionTerm,
            Definition = styles.Definition,
            Quote = styles.Quote,
            Code = styles.Code,
            CodeCharacter = styles.CodeCharacter,
            Rule = styles.Rule,
            Caption = styles.Caption,
            TableTitle = styles.TableTitle,
            Table = styles.Table,
            FootnoteText = styles.FootnoteText,
            FootnoteReference = styles.FootnoteReference,
            Hyperlink = styles.Hyperlink,
            Note = styles.Note,
            Tip = styles.Tip,
            Important = styles.Important,
            Warning = styles.Warning,
            Caution = styles.Caution,
            Mark = styles.Mark,
            LabelSuccess = styles.LabelSuccess,
            LabelInfo = styles.LabelInfo,
            LabelWarning = styles.LabelWarning,
            LabelDanger = styles.LabelDanger,
            LabelNeutral = styles.LabelNeutral,
            ColourSuccess = styles.ColourSuccess,
            ColourInfo = styles.ColourInfo,
            ColourWarning = styles.ColourWarning,
            ColourDanger = styles.ColourDanger,
            ColourNeutral = styles.ColourNeutral,
            ContainerHeading = styles.ContainerHeading,
            Statement = styles.Statement,
            Panel = styles.Panel,
            PanelDark = styles.PanelDark,
            Tag = styles.Tag,
            ImageRow = styles.ImageRow,
            Classification = styles.Classification,
            ClassificationCover = styles.ClassificationCover,
        };

    private static PptxTemplateRoles ToRoles(PptxSection pptx) =>
        new()
        {
            Layouts = new PptxLayoutRoles
            {
                TwoContent = pptx.Layouts?.TwoContent,
                TextAndPicture = pptx.Layouts?.TextAndPicture,
                Picture = pptx.Layouts?.Picture,
                Agenda = pptx.Layouts?.Agenda,
                Quote = pptx.Layouts?.Quote,
                Statement = pptx.Layouts?.Statement,
                Contact = pptx.Layouts?.Contact,
                Chart = pptx.Layouts?.Chart,
                TwoCharts = pptx.Layouts?.TwoCharts,
                Dark = pptx.Layouts?.Dark,
            },
            Placeholders = pptx.Placeholders is not { } layouts ? [] : [.. layouts.SelectMany(layout => ToPlaceholderRoles(layout.Key, layout.Value))],
            TextLevels = pptx.TextLevels is not { } levels ? null : new PptxTextLevels(
                levels.Heading ?? throw Missing("pptx.textLevels.heading"),
                levels.Paragraph ?? throw Missing("pptx.textLevels.paragraph"),
                levels.Bullet ?? throw Missing("pptx.textLevels.bullet")),
            FooterPattern = pptx.Footer,
            ContentArea = pptx.ContentArea switch
            {
                null or "placeholders" => PptxContentArea.Placeholders,
                "guides" => PptxContentArea.Guides,
                var other => throw new InvalidDataException($"{BrandPack.ManifestFile}: 'pptx.contentArea' is '{other}'; expected 'placeholders' or 'guides'."),
            },
            NumberChapters = pptx.NumberChapters ?? false,
        };

    // One layout's placeholders: `body1`, `body2`, … give the order of its bodies; plain `body`s come first, as listed.
    private static IEnumerable<PptxPlaceholderRole> ToPlaceholderRoles(string layout, Dictionary<string, string>? placeholders)
    {
        var roles = new List<(PptxPlaceholderRole Role, int Order)>();
        foreach (var (name, value) in placeholders ?? [])
        {
            var key = $"pptx.placeholders.{layout}.{name}";
            var (role, order) = value switch
            {
                "subtitle" => (PlaceholderRole.Subtitle, 0),
                "byline" => (PlaceholderRole.Byline, 0),
                "chapterNumber" => (PlaceholderRole.ChapterNumber, 0),
                "body" => (PlaceholderRole.Body, 0),
                "picture" => (PlaceholderRole.Picture, 0),
                "chartTitle" => (PlaceholderRole.ChartTitle, 0),
                "chartUnit" => (PlaceholderRole.ChartUnit, 0),
                "chartSource" => (PlaceholderRole.ChartSource, 0),
                "decorative" => (PlaceholderRole.Decorative, 0),
                _ when value is not null && value.StartsWith("body", StringComparison.Ordinal)
                    && int.TryParse(value.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    && number > 0 => (PlaceholderRole.Body, number),
                _ => throw new InvalidDataException(
                    $"{BrandPack.ManifestFile}: '{key}' is '{value}'; expected 'subtitle', 'byline', 'chapterNumber', 'body', 'body1', 'body2', …, "
                    + "'picture', 'chartTitle', 'chartUnit', 'chartSource' or 'decorative'."),
            };
            roles.Add((new PptxPlaceholderRole(layout, name, role), order));
        }

        return roles.OrderBy(r => r.Role.Role == PlaceholderRole.Body ? r.Order : 0).Select(r => r.Role);
    }

    private static BrandColors ToColors(ColorsSection colors, string primary) =>
        new()
        {
            Primary = primary,
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
            Highlight = colors.Highlight,
            Labels = colors.Labels is not { } labels ? null : new BrandLabelColors
            {
                Success = labels.Success,
                Info = labels.Info,
                Warning = labels.Warning,
                Danger = labels.Danger,
                Neutral = labels.Neutral,
            },
            Chart = colors.Chart is not { } chart ? null : new BrandChartColors
            {
                Series = chart.Series,
                Categories = chart.Categories,
                Reference = chart.Reference,
                ReferenceFill = chart.ReferenceFill,
                Axis = chart.Axis,
                Gridline = chart.Gridline,
                Band = chart.Band,
                Emphasis = chart.Emphasis,
                Ahead = chart.Ahead,
                Level = chart.Level,
                Behind = chart.Behind,
                Surface = chart.Surface,
                TableHeader = chart.TableHeader,
                TotalHeader = chart.TotalHeader,
                TotalFill = chart.TotalFill,
                TooltipFill = chart.TooltipFill,
                TooltipText = chart.TooltipText,
            },
            Code = colors.Code is not { } code ? null : new BrandCodeColors
            {
                Keyword = code.Keyword,
                String = code.String,
                Comment = code.Comment,
                Number = code.Number,
                Type = code.Type,
                Markup = code.Markup,
            },
        };

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
        DiagramSection? Diagram,
        string? Highlight,
        LabelsSection? Labels,
        ChartSection? Chart = null,
        CodeSection? Code = null);

    internal sealed record CodeSection(string? Keyword, string? String, string? Comment, string? Number, string? Type, string? Markup);

    internal sealed record LabelsSection(string? Success, string? Info, string? Warning, string? Danger, string? Neutral);

    internal sealed record DiagramSection(string? Text, string? Line, string? Accent, string? Surface, string? Border, IReadOnlyList<string>? Palette);

    internal sealed record ChartSection(
        IReadOnlyList<string>? Series,
        IReadOnlyList<string>? Categories,
        string? Reference,
        string? ReferenceFill,
        string? Axis,
        string? Gridline,
        string? Band,
        string? Emphasis,
        string? Ahead,
        string? Level,
        string? Behind,
        string? Surface,
        string? TableHeader,
        string? TotalHeader,
        string? TotalFill,
        string? TooltipFill,
        string? TooltipText);

    internal sealed record TableSection(string? Header, string? Band, string? Line);

    internal sealed record ImagesSection(string? Logo, string? LogoOnDark, string? CoverArtwork, string? Tagline, string? CoverImage = null);

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

    internal sealed record PptxSection(
        string? Template,
        LayoutsSection? Layouts,
        string? Tone,
        Dictionary<string, Dictionary<string, string>?>? Placeholders = null,
        TextLevelsSection? TextLevels = null,
        string? Footer = null,
        string? ContentArea = null,
        bool? NumberChapters = null);

    internal sealed record LayoutsSection(
        string? Title,
        string? Content,
        string? TitleOnly,
        string? SectionHeader,
        string? Closing,
        string? TwoContent = null,
        string? TextAndPicture = null,
        string? Picture = null,
        string? Agenda = null,
        string? Quote = null,
        string? Statement = null,
        string? Contact = null,
        string? Chart = null,
        string? TwoCharts = null,
        string? Dark = null);

    internal sealed record TextLevelsSection(int? Heading, int? Paragraph, int? Bullet);

    internal sealed record DocxSection(string? Template, DocxStylesSection? Styles = null);

    // One property per role, so that a misspelt role is an unknown key.
    internal sealed record DocxStylesSection(
        string? Title = null,
        string? Subtitle = null,
        string? CoverDetails = null,
        string? TocHeading = null,
        string? Heading1 = null,
        string? Heading2 = null,
        string? Heading3 = null,
        string? Heading4 = null,
        string? Heading5 = null,
        string? Heading6 = null,
        string? Normal = null,
        string? ListBullet = null,
        string? ListNumber = null,
        string? ListTask = null,
        string? DefinitionTerm = null,
        string? Definition = null,
        string? Quote = null,
        string? Code = null,
        string? CodeCharacter = null,
        string? Rule = null,
        string? Caption = null,
        string? TableTitle = null,
        string? Table = null,
        string? FootnoteText = null,
        string? FootnoteReference = null,
        string? Hyperlink = null,
        string? Note = null,
        string? Tip = null,
        string? Important = null,
        string? Warning = null,
        string? Caution = null,
        string? Mark = null,
        string? LabelSuccess = null,
        string? LabelInfo = null,
        string? LabelWarning = null,
        string? LabelDanger = null,
        string? LabelNeutral = null,
        string? ColourSuccess = null,
        string? ColourInfo = null,
        string? ColourWarning = null,
        string? ColourDanger = null,
        string? ColourNeutral = null,
        string? ContainerHeading = null,
        string? Statement = null,
        string? Panel = null,
        string? PanelDark = null,
        string? Tag = null,
        string? ImageRow = null,
        string? Classification = null,
        string? ClassificationCover = null);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(BrandManifest))]
internal sealed partial class BrandManifestContext : JsonSerializerContext;
