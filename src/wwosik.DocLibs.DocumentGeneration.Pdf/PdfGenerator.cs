using System.Globalization;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>
/// Generates PDF/A-2b documents from Markdown: a cover page from a leading <c>#</c> heading, a new page per later
/// <c>#</c>, headings as bookmarks, lists, tables with repeated header rows, code, quotes, images and links, in the
/// built-in design in a brand's colours, fonts and images. Front matter at the top of the Markdown overrides the
/// options.
/// </summary>
public static class PdfGenerator
{
    private const string PageSizeKey = "pageSize";
    private const string OrientationKey = "orientation";

    /// <summary>Writes <paramref name="document"/> as a PDF to <paramref name="output"/>.</summary>
    /// <exception cref="DocumentConversionException">
    /// An image is not among the document's resources or not a PNG or JPEG, the front matter is invalid or names an
    /// unknown brand, or a character is not in the font and <see cref="PdfOptions.ReplaceUnsupported"/> is not set or
    /// returns one that is not either.
    /// </exception>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="PdfOptions.Brands"/> share a name.</exception>
    public static void Generate(MarkdownDocument document, Stream output, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PdfOptions.Default;

        var frontMatter = FrontMatter.Read(document.Markdown, DocumentFormat.Pdf);
        options = Apply(frontMatter, options);
        var brand = ResolvedBrand.Resolve(options.Template.BrandValue);
        var fonts = FontSet.For(brand);
        var model = MarkdownParser.Parse(document with { Markdown = frontMatter.Body }, options, options.Template.BrandValue, frontMatter.BodyLine);
        var title = frontMatter.Title ?? document.Metadata.GetValueOrDefault(MetadataKeys.Title)
            ?? (model.Cover is { } cover ? string.Concat(cover.Title.Select(r => r.IsBreak ? " " : r.Text)) : null);
        var author = frontMatter.Author ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author);
        var date = options.Template.ShowsDate
            ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;

        var design = new Design(brand, options.Template.ShowsTagline, date, title);
        var layout = new LayoutEngine(fonts, PageGeometry.For(options.PageSize, options.Orientation), design, options.ReplaceUnsupported).Run(model);
        output.Write(PdfRenderer.Render(layout, fonts, layout.Title, author));
    }

    /// <summary>The caller's options with the front matter applied on top.</summary>
    internal static PdfOptions Apply(FrontMatter frontMatter, PdfOptions options)
    {
        var brands = BrandRegistry.Of(options.Template.BrandValue, options.Brands);
        return frontMatter.Apply(
            options,
            (current, name) => brands.Find(name) is not { } brand ? null
                : current.Template.Name == brand.Name.ToLowerInvariant() ? current
                : current with { Template = new BrandPdfTemplateOptions { Brand = brand } },
            brands.Expected,
            current => current.Template.Name,
            current => [.. current.Template.Keys, PageSizeKey, OrientationKey],
            (current, key, value) => key switch
            {
                PageSizeKey => current with
                {
                    PageSize = frontMatter.OneOf(key, value, ("A4", PdfPageSize.A4), ("A3", PdfPageSize.A3), ("Letter", PdfPageSize.Letter)),
                },
                OrientationKey => current with
                {
                    Orientation = frontMatter.OneOf(key, value, ("portrait", PdfOrientation.Portrait), ("landscape", PdfOrientation.Landscape)),
                },
                _ => current with { Template = current.Template.With(frontMatter, key, value) },
            });
    }
}
