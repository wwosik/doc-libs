using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Generates PowerPoint presentations (<c>.pptx</c>) from Markdown: the cover from the first <c>#</c>, a section
/// divider per later <c>#</c>, one slide per <c>##</c>, and a slide of its own for every table and image; a
/// <c>##</c> section that uses layouts (heading attributes, <c>:::</c> containers, alerts, tag lists) stacks
/// everything on one slide. Front matter at the top of the Markdown overrides the template options.
/// </summary>
public static class PptxGenerator
{
    private static readonly XNamespace CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>Writes <paramref name="document"/> as a <c>.pptx</c> to <paramref name="output"/>.</summary>
    /// <exception cref="DocumentConversionException">
    /// An image is not among the document's resources or not a PNG, JPEG or GIF, the front matter is invalid or names
    /// an unknown brand, a named layout does not exist, or the template is damaged.
    /// </exception>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="PptxOptions.Brands"/> share a name.</exception>
    public static void Generate(MarkdownDocument document, Stream output, PptxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PptxOptions.Default;

        var frontMatter = FrontMatter.Read(document.Markdown, Format.Pptx);
        var template = PptxTemplateOptions.Apply(frontMatter, options.Template ?? PptxOptions.Default.Template, options.Brands);
        var labels = frontMatter.Labels();
        var date = template.ShowsDate
            ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;

        using var buffer = new MemoryStream();
        template.Write(buffer);

        buffer.Position = 0;
        try
        {
            using var package = PresentationDocument.Open(buffer, isEditable: true);
            if (package.DocumentType == PresentationDocumentType.Template)
            {
                package.ChangeDocumentType(PresentationDocumentType.Presentation);
            }

            // A brand's own template may have dark slides; the built-in master and caller templates are taken as light.
            var brand = template.BrandValue;
            var tone = brand?.Pptx is not { } own ? SlideTone.Light
                : own.Tone == SlideTone.Auto ? TemplateTone.Detect(package.PresentationPart!)
                : own.Tone;
            var dark = tone == SlideTone.Dark;
            var slides = DeckParser.Parse(
                document with { Markdown = frontMatter.Body },
                options,
                brand is null ? null : dark ? ResolvedBrand.DarkSlideBrand(brand) : ResolvedBrand.SlideBrand(brand),
                frontMatter.BodyLine);
            var resolved = brand is null ? null : ResolvedBrand.ForSlides(brand);
            var palette = resolved is null ? SlidePalette.Scheme : dark ? SlidePalette.ForDark(resolved) : SlidePalette.From(resolved);
            var codeFont = options.CodeFont ?? resolved?.CodeFontFamily ?? PptxOptions.DefaultCodeFont;
            new DeckWriter(
                package,
                options,
                template.LayoutNames,
                date,
                resolved?.TableFontFamily,
                palette,
                labels,
                codeFont,
                template.FooterText,
                template.IsBuiltIn,
                (template.ListBulletsOverride ?? options.ListBullets) == ListBullets.Always).Write(slides);
            if (template.EmbedsFonts && resolved?.Fonts is { } fonts)
            {
                FontEmbedding.Embed(package.PresentationPart!, fonts, codeFont);
            }

            WriteCoreProperties(
                package,
                frontMatter.Title ?? Title(document, slides),
                frontMatter.Author ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author));
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or XmlException or FormatException)
        {
            throw new DocumentConversionException(Format.Pptx, "The template is damaged: " + ex.Message, ex);
        }

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    /// <summary>
    /// The authoring guide for whoever writes the Markdown (often an AI agent): how headings become slides, the
    /// layouts, the diagrams the options' renderers draw, the front matter keys of the options' template and brands,
    /// and the characters the brand's fonts lack. Markdown starting at <c>##</c>, in English, so a host can put it under
    /// a heading of its own and add what only it knows: how images are referenced, which settings it fixes, limits.
    /// Pass the same options as to <see cref="Generate"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="PptxOptions.Brands"/> share a name.</exception>
    public static string AuthoringGuide(PptxOptions? options = null)
    {
        options ??= PptxOptions.Default;
        var template = options.Template ?? PptxOptions.Default.Template;
        var registry = BrandRegistry.Of(template.BrandValue, options.Brands);
        var brand = template.BrandValue is { } value ? ResolvedBrand.Resolve(value) : null;
        var codeFont = options.CodeFont ?? brand?.CodeFontFamily ?? PptxOptions.DefaultCodeFont;
        return AuthoringGuides.Fill(
            AuthoringGuides.Template(typeof(PptxGenerator).Assembly, "Pptx"),
            new Dictionary<string, string>
            {
                ["diagrams"] = AuthoringGuides.Renderers(options.CodeBlockRenderers),
                ["frontMatter"] = AuthoringGuides.FrontMatter(registry.Expected, [.. template.Keys.Select(k => (k, KeyMeanings[k])), Labels]),
                ["characters"] = Characters(brand?.Fonts, codeFont),
                ["templateLayouts"] = TemplateLayouts(brand?.Pptx),
            });
    }

    // A brand's own template: its layouts by name, for `layout="…"`.
    private static string TemplateLayouts(PptxBrandTemplate? template)
    {
        if (template is null)
        {
            return string.Empty;
        }

        using var package = PresentationDocument.Open(new MemoryStream(template.Content.ToArray()), isEditable: false);
        var names = package.PresentationPart?.SlideMasterParts
            .SelectMany(m => m.SlideLayoutParts)
            .Select(l => l.SlideLayout?.CommonSlideData?.Name?.Value)
            .OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        return names.Count == 0 ? string.Empty
            : "**Layouts of this design.** `layout=\"Name\"` after a `#` or `##` heading puts that slide on one of these: "
                + string.Join(", ", names.Select(n => $"`{n}`")) + ". Use them where a slide needs it, such as a layout for long titles.";
    }

    private static readonly Dictionary<string, string> KeyMeanings = new(StringComparer.Ordinal)
    {
        ["showDate"] = "`true` or `false`: today's date in the footer. Default `true`.",
        ["showTagline"] = "`true` or `false`: the design's tagline on the cover, if it has one. Default `true`.",
        ["embedFonts"] = "`true` or `false`. Leave it out: by default the deck carries its fonts.",
        ["layouts.title"] = "The name of the template's layout for the cover.",
        ["layouts.content"] = "The name of the template's layout for text slides.",
        ["layouts.titleOnly"] = "The name of the template's layout for table, image and layout slides.",
        ["layouts.sectionHeader"] = "The name of the template's layout for section dividers.",
        ["layouts.closing"] = "The name of the template's layout for the closing slide.",
        ["showFooter"] = "`true` or `false`: the design's footer text on every slide but the cover. Default `true`.",
        ["listBullets"] = "`always` to show bullets on every list level, also where the design's first level has none; `template` (the default) leaves it to the design.",
    };

    private static readonly (string, string) Labels = (
        "labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral",
        "The [status labels](#layouts) in that category, separated by `;`: `labels.success: Done; Delivered`.");

    // The characters the brand's fonts lack, which PowerPoint shows in a substitute font.
    private static string Characters(ResolvedFonts? fonts, string codeFont)
    {
        if (fonts is null)
        {
            return "Each character is shown in the deck's font, or in a substitute where that font lacks it. Prefer "
                + "plain characters (`•`, `–`, `→`, typographic quotes) to symbols, and leave out emoji.";
        }

        var mono = fonts.Mono is { } monoFace && fonts.MonoFamily == codeFont ? monoFace : null;
        var families = fonts.HasTableFonts ? $"{fonts.Family}, with {fonts.TableFamily} in tables" : fonts.Family;
        return AuthoringGuides.Characters(
            c => TrueTypeInfo.HasCharacter(fonts.Regular, c) && TrueTypeInfo.HasCharacter(fonts.TableRegular, c),
            c => mono is null || TrueTypeInfo.HasCharacter(mono, c),
            $"The deck is set in {families}{(mono is null ? string.Empty : $" and {codeFont} for code")}. A character "
                + "these fonts lack is shown in a substitute font and looks out of place. Leave out emoji"
                + ", and write these symbols as shown:");
    }

    private static string? Title(MarkdownDocument document, IReadOnlyList<Slide> slides)
    {
        if (document.Metadata.GetValueOrDefault(MetadataKeys.Title) is { } title)
        {
            return title;
        }

        var slide = slides.OfType<TitleSlide>().FirstOrDefault() ?? slides.FirstOrDefault();
        return slide is null ? null : string.Concat(slide.Title.Select(r => r.IsBreak ? " " : r.Text));
    }

    // Written by hand rather than through PackageProperties so that the part keeps the name docProps/core.xml.
    private static void WriteCoreProperties(PresentationDocument package, string? title, string? author)
    {
        var part = package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart();
        XDocument xml;
        using (var stream = part.GetStream(FileMode.Open, FileAccess.Read))
        {
            xml = stream.Length > 0
                ? XDocument.Load(stream)
                : new XDocument(new XElement(
                    CoreProperties + "coreProperties",
                    new XAttribute(XNamespace.Xmlns + "cp", CoreProperties),
                    new XAttribute(XNamespace.Xmlns + "dc", DublinCore)));
        }

        Replace(xml.Root!, DublinCore + "title", title);
        Replace(xml.Root!, DublinCore + "creator", author);
        using var output = part.GetStream(FileMode.Create, FileAccess.Write);
        xml.Save(output);
    }

    private static void Replace(XElement root, XName name, string? value)
    {
        root.Elements(name).Remove();
        if (!string.IsNullOrWhiteSpace(value))
        {
            root.Add(new XElement(name, value));
        }
    }
}
