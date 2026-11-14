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

    // Keys of decks, accepted so that one Markdown serves both formats: their values are checked, then ignored.
    private const string TransitionKey = "transition";
    private const string BuildKey = "build";
    private const string NumberChaptersKey = "numberChapters";

    /// <summary>
    /// Writes <paramref name="document"/> as a PDF to <paramref name="output"/>. Returns the number of pages and the
    /// notes for the writer: diagrams left as code, HTML shown as text, characters replaced.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// An image is not among the document's resources or not a PNG or JPEG, the front matter is invalid or names an
    /// unknown brand, or a character is not in the font and <see cref="PdfOptions.ReplaceUnsupported"/> is not set or
    /// returns one that is not either, or the language is not a language tag.
    /// </exception>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="PdfOptions.Brands"/> share a name.</exception>
    public static GenerationResult Generate(MarkdownDocument document, Stream output, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PdfOptions.Default;

        var frontMatter = FrontMatter.Read(document.Markdown, DocumentFormat.Pdf);
        frontMatter.Labels(); // checked like in decks; PDFs show status labels as code

        // The marking is checked like in Word documents; PDFs show it with E03-S04.
        Marking.Resolve(frontMatter, Classification.Public, false, new GenerationNotes(), DocumentFormat.Pdf);
        options = Apply(frontMatter, options);
        var brand = ResolvedBrand.Resolve(options.Template.BrandValue);
        var fonts = FontSet.For(brand);
        var notes = new GenerationNotes();
        var language = DocumentLanguage.Resolve(frontMatter.Language, options.Language, options.Texts, notes, DocumentFormat.Pdf);
        var model = MarkdownParser.Parse(document with { Markdown = frontMatter.Body }, options, options.Template.BrandValue, frontMatter.BodyLine, notes, language.Texts);
        var title = frontMatter.Title ?? document.Metadata.GetValueOrDefault(MetadataKeys.Title)
            ?? (model.Cover is { } cover ? string.Concat(cover.Title.Select(r => r.IsBreak ? " " : r.Text)) : null);
        var author = frontMatter.Author ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author);
        var date = options.Template.ShowsDate
            ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;

        var design = new Design(brand, options.Template.ShowsTagline, date, title);
        var layout = new LayoutEngine(fonts, PageGeometry.For(options.PageSize, options.Orientation), design, options.ReplaceUnsupported, notes).Run(model);
        output.Write(PdfRenderer.Render(layout, fonts, layout.Title, author, language.Tag));
        return notes.Result(layout.Pages.Count);
    }

    /// <summary>
    /// The authoring guide for whoever writes the Markdown (often an AI agent): how headings become the cover, pages
    /// and sections, the diagrams the options' renderers draw, the front matter keys of the options' design and brands,
    /// and the characters the brand's fonts lack. Markdown starting at <c>##</c>, in English, so a host can put it under
    /// a heading of its own and add what only it knows: how images are referenced, which settings it fixes, limits.
    /// Pass the same options as to <see cref="Generate"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="PdfOptions.Brands"/> share a name.</exception>
    public static string AuthoringGuide(PdfOptions? options = null)
    {
        options ??= PdfOptions.Default;
        var registry = BrandRegistry.Of(options.Template.BrandValue, options.Brands);
        var meanings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["showDate"] = "`true` or `false`: today's date in the footer. Default `true`.",
            ["showTagline"] = "`true` or `false`: the design's tagline on the cover, if it has one. Default `true`.",
            [PageSizeKey] = $"`A4`, `A3` or `Letter`. Default `{options.PageSize}`.",
            [OrientationKey] = $"`portrait` or `landscape`. Default `{options.Orientation.ToString().ToLowerInvariant()}`.",
        };
        (string, string) labels = (
            "labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral",
            "Status labels for decks, separated by `;`. A PDF accepts them and shows the labels as code.");
        (string, string) transition = (
            TransitionKey,
            "The transition between slides in a deck: " + string.Join(", ", LayoutSyntax.Transitions.Select(t => $"`{t}`")) + ". A PDF accepts it and ignores it.");
        (string, string) build = (
            BuildKey,
            "`lists` builds every list of a deck item by item, `none` does not. A PDF accepts it and ignores it.");
        (string, string) numberChapters = (
            NumberChaptersKey,
            "`true` or `false`: chapter numbers on a deck's section dividers. A PDF accepts it and ignores it.");
        return AuthoringGuides.Fill(
            AuthoringGuides.Template(typeof(PdfGenerator).Assembly, "Pdf"),
            new Dictionary<string, string>
            {
                ["diagrams"] = AuthoringGuides.Renderers(options.CodeBlockRenderers),
                ["frontMatter"] = AuthoringGuides.FrontMatter(
                    registry.Expected,
                    options.Language,
                    [.. options.Template.Keys.Concat([PageSizeKey, OrientationKey]).Select(k => (k, meanings[k])), transition, build, numberChapters, labels]),
                ["characters"] = Characters(FontSet.For(ResolvedBrand.Resolve(options.Template.BrandValue)), options.ReplaceUnsupported is not null),
                ["icons"] = AuthoringGuides.Icons(
                    options.Icons,
                    "A PDF draws it inside text, one letter wide in the text's colour; at the start of a list item, in place "
                    + "of its bullet; and alone in a paragraph, large. In headings, table cells and on the cover it is left out."),
                ["notes"] = options.ReplaceUnsupported is null
                    ? AuthoringGuides.Notes(DocumentFormat.Pdf, NoteCodes.CharacterReplaced)
                    : AuthoringGuides.Notes(DocumentFormat.Pdf),
            });
    }

    // The characters the document's fonts lack: an error, or replaced by the caller's hook.
    private static string Characters(FontSet fonts, bool replaced)
    {
        bool Has(FontFace face, int c) => TrueTypeInfo.HasCharacter(fonts.File(face), c);
        var effect = replaced
            ? "is replaced, usually by a placeholder such as `?`, so check the result"
            : "stops the generation with an error naming it";
        return AuthoringGuides.Characters(
            c => Has(FontFace.Regular, c) && Has(FontFace.TableRegular, c),
            c => Has(FontFace.Mono, c),
            "The fonts are embedded in the PDF and have no emoji and no Asian scripts; never use emoji. Any character "
                + $"the fonts lack {effect}. Write these symbols as shown:");
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
            current => [.. current.Template.Keys, PageSizeKey, OrientationKey, TransitionKey, BuildKey, NumberChaptersKey],
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
                TransitionKey => frontMatter.OneOf(key, value, [.. LayoutSyntax.Transitions.Select(t => (t, current))]),
                BuildKey => frontMatter.OneOf(key, value, ("none", current), ("lists", current)),
                NumberChaptersKey => frontMatter.Bool(key, value) ? current : current,
                _ => current with { Template = current.Template.With(frontMatter, key, value) },
            });
    }
}
