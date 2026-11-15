using System.Globalization;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>
/// Generates PDF/A-2b documents from Markdown: a cover page from a leading <c>#</c> heading, a new page per later
/// <c>#</c> and wherever the Markdown asks for a page break, headings as bookmarks, an optional table of contents and heading numbers, numbered captions, footnotes at
/// the foot of their page, optionally justified text, lists, tables with repeated header rows, code, quotes, images and links, in the
/// built-in design in a brand's colours, fonts and images. Front matter at the top of the Markdown overrides the
/// options.
/// </summary>
public static class PdfGenerator
{
    private const string PageSizeKey = "pageSize";
    private const string OrientationKey = "orientation";
    private const string TocKey = FrontMatter.TocKey;
    private const string TocDepthKey = FrontMatter.TocDepthKey;
    private const string HeadingNumbersKey = FrontMatter.HeadingNumbersKey;
    private const string JustifyKey = FrontMatter.JustifyKey;
    private const string LayoutKey = "layout";

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
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PdfOptions.TableOfContentsDepth"/> is not 1, 2 or 3.</exception>
    public static GenerationResult Generate(MarkdownDocument document, Stream output, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PdfOptions.Default;
        CheckDepth(options);
        var notes = new GenerationNotes();
        var laidOut = Lay(document, options, notes);
        Renders++;
        output.Write(PdfRenderer.Render(laidOut.Layout, laidOut.Fonts, laidOut.Layout.Title, laidOut.Author, laidOut.LanguageTag));
        return notes.Result(laidOut.Layout.Pages.Count);
    }

    private static void CheckDepth(PdfOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TableOfContentsDepth, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TableOfContentsDepth, 3, nameof(options));
    }

    /// <summary>
    /// Checks <paramref name="document"/> without writing a PDF: lays it out, without rendering, and returns the notes
    /// <see cref="Generate"/> would, the notes of the style rules of <see cref="PdfOptions.Checks"/> (paragraph length,
    /// heading length, alt text), every image that is missing or a web address, and statistics with the pages. An error
    /// in the document is returned as <see cref="CheckResult.Error"/>, not thrown.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The brand or <see cref="PdfOptions.Checks"/> is invalid, or two of <see cref="PdfOptions.Brands"/> share a name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PdfOptions.TableOfContentsDepth"/> is not 1, 2 or 3.</exception>
    public static CheckResult Check(MarkdownDocument document, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= PdfOptions.Default;
        CheckDepth(options);
        return DocumentCheck.Run(document, options.Checks ?? CheckOptions.Default, DocumentFormat.Pdf, options.CodeBlockRenderers, (checkedDocument, renderers, notes) =>
            new CheckRun(Lay(checkedDocument, options with { CodeBlockRenderers = renderers }, notes).Layout.Pages.Count));
    }

    /// <summary>How often this thread rendered a PDF: a test seam, so a test can see that a check renders nothing.</summary>
    [ThreadStatic]
    internal static int Renders;

    // Generate's work up to the layout: front matter, parsing and layout, with the notes added to notes.
    private static LaidOut Lay(MarkdownDocument document, PdfOptions options, GenerationNotes notes)
    {
        var frontMatter = FrontMatter.Read(document.Markdown, DocumentFormat.Pdf);
        frontMatter.Labels(); // checked like in decks; PDFs show status labels as code

        var marking = Marking.Resolve(frontMatter, options.Classification, options.Draft, notes, DocumentFormat.Pdf);
        options = Apply(frontMatter, options);
        var brand = ResolvedBrand.Resolve(options.Template.BrandValue);
        var fonts = FontSet.For(brand);
        var language = DocumentLanguage.Resolve(frontMatter.Language, options.Language, options.Texts, notes, DocumentFormat.Pdf);
        var typography = Typography.For(
            Typography.IsOn(frontMatter, options.Typography),
            language.Tag,
            options.TypographyRules,
            c => TextFaces.All(face => fonts.HasGlyph(new Rune(c), face)),
            notes);
        var model = MarkdownParser.Parse(document with { Markdown = frontMatter.Body }, options, options.Template.BrandValue, frontMatter.BodyLine, notes, language.Texts, typography,
            CodeHighlighting.For(frontMatter, options.HighlightCode, notes));
        if (options.TableOfContents && model.Contents is null)
        {
            notes.Add(
                NoteCodes.TocEmpty,
                frontMatter.LineOf(TocKey),
                $"The table of contents lists the headings of {options.TableOfContentsDepth} level{(options.TableOfContentsDepth == 1 ? string.Empty : "s")} from the highest one after the cover, and the document has none, so it has no table of contents. Add headings, or leave 'toc' out.");
        }

        var title = Typeset(typography, frontMatter.Title, frontMatter.LineOf(FrontMatter.TitleKey)) ?? document.Metadata.GetValueOrDefault(MetadataKeys.Title)
            ?? (model.Cover is { } cover ? string.Concat(cover.Title.Select(r => r.IsBreak ? " " : r.Text)) : null);
        var author = Typeset(typography, frontMatter.Author, frontMatter.LineOf(FrontMatter.AuthorKey)) ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author);
        var date = options.Template.ShowsDate
            ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;

        var design = new Design(brand, options.Template.ShowsTagline, date, title)
        {
            Texts = language.Texts,
            Justify = options.Justify,
            Plain = options.Layout == PdfLayout.Plain,
            Marking = marking.IsEmpty ? null : marking.Text(language.Texts),
            MarkingColor = marking.Classification == Classification.Confidential ? brand.Labels.Danger : brand.Footer,
            Watermark = marking.Draft ? Marking.Watermark(language.Texts, language.Tag) : null,
        };
        var layout = new LayoutEngine(fonts, PageGeometry.For(options.PageSize, options.Orientation), design, options.ReplaceUnsupported, notes).Run(model);
        return new LaidOut(layout, fonts, author, language.Tag);
    }

    /// <summary>
    /// This generator bound to <paramref name="options"/>, as an <see cref="IMarkdownGenerator"/> (format <c>pdf</c>) for
    /// <see cref="DocumentTool"/>: its <c>Generate</c>, <c>Check</c> and <c>AuthoringGuide</c> are this class's methods with
    /// these options. The options are not checked until one of them is called.
    /// </summary>
    public static IMarkdownGenerator ForOptions(PdfOptions? options = null) => new Bound(options ?? PdfOptions.Default);

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
            [TocKey] = $"`true` or `false`: a table of contents after the cover, with page numbers. Default `{(options.TableOfContents ? "true" : "false")}`.",
            [TocDepthKey] = $"`1`, `2` or `3`: the heading levels the table of contents lists. Default `{options.TableOfContentsDepth.ToString(CultureInfo.InvariantCulture)}`.",
            [HeadingNumbersKey] = $"`true` or `false`: numbers on three heading levels (1, 1.1, 1.1.1). Default `{(options.HeadingNumbers ? "true" : "false")}`.",
            [JustifyKey] = $"`true` or `false`: body text justified, without hyphenation. Default `{(options.Justify ? "true" : "false")}`.",
            [LayoutKey] = $"`report` or `plain`: `plain` has no cover and no header, for a short business document. Default `{options.Layout.ToString().ToLowerInvariant()}`.",
        };
        (string, string) labels = (
            "labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral",
            "Status labels for decks, separated by `;`. A PDF accepts them and shows the labels as code.");
        (string, string) deckKeys = (
            $"{TransitionKey}`, `{BuildKey}`, `{NumberChaptersKey}",
            "Settings for decks. A PDF accepts them and ignores them.");
        var (classification, draft) = AuthoringGuides.MarkingKeys(options.Classification, options.Draft, "in the header and on the cover", "a diagonal watermark");
        return AuthoringGuides.Fill(
            AuthoringGuides.Template(typeof(PdfGenerator).Assembly, "Pdf"),
            new Dictionary<string, string>
            {
                ["diagrams"] = AuthoringGuides.Renderers(options.CodeBlockRenderers),
                ["frontMatter"] = AuthoringGuides.FrontMatter(
                    registry.Expected,
                    options.Language,
                    [.. options.Template.Keys.Concat([PageSizeKey, OrientationKey, LayoutKey, TocKey, TocDepthKey, HeadingNumbersKey, JustifyKey]).Select(k => (k, meanings[k])), classification, draft, deckKeys, labels],
                    options.Typography, options.HighlightCode),
                ["characters"] = Characters(FontSet.For(ResolvedBrand.Resolve(options.Template.BrandValue)), options.ReplaceUnsupported is not null),
                ["icons"] = AuthoringGuides.Icons(
                    options.Icons,
                    "A PDF draws it inside text, one letter wide in the text's colour; at the start of a list item, in place "
                    + "of its bullet; and alone in a paragraph, large. In headings, table cells and on the cover it is left out."),
                ["limits"] = AuthoringGuides.Limits(options.Checks ?? CheckOptions.Default, DocumentFormat.Pdf),
                // classification-kept is left to the front matter table, which says when lower values have no effect.
                ["notes"] = options.ReplaceUnsupported is null
                    ? AuthoringGuides.Notes(DocumentFormat.Pdf, NoteCodes.CharacterReplaced, NoteCodes.ClassificationKept)
                    : AuthoringGuides.Notes(DocumentFormat.Pdf, NoteCodes.ClassificationKept),
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

    // The faces text can be set in: typography introduces a character only when all of them have it.
    private static readonly FontFace[] TextFaces =
    [
        FontFace.Regular, FontFace.Bold, FontFace.Italic, FontFace.BoldItalic,
        FontFace.TableRegular, FontFace.TableBold, FontFace.TableItalic, FontFace.TableBoldItalic,
    ];

    // A front-matter title or author, typeset when typography is on.
    private static string? Typeset(Typography? typography, string? text, int line) =>
        text is null || typography is null ? text : typography.Apply(text, line);

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
            current => [.. current.Template.Keys, PageSizeKey, OrientationKey, LayoutKey, TocKey, TocDepthKey, HeadingNumbersKey, JustifyKey, TransitionKey, BuildKey, NumberChaptersKey],
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
                LayoutKey => current with
                {
                    Layout = frontMatter.OneOf(key, value, ("report", PdfLayout.Report), ("plain", PdfLayout.Plain)),
                },
                TocKey => current with { TableOfContents = frontMatter.Bool(key, value) },
                TocDepthKey => current with { TableOfContentsDepth = frontMatter.DocumentValue(key, value) },
                HeadingNumbersKey => current with { HeadingNumbers = frontMatter.Bool(key, value) },
                JustifyKey => current with { Justify = frontMatter.Bool(key, value) },
                TransitionKey => frontMatter.OneOf(key, value, [.. LayoutSyntax.Transitions.Select(t => (t, current))]),
                BuildKey => frontMatter.OneOf(key, value, ("none", current), ("lists", current)),
                NumberChaptersKey => frontMatter.Bool(key, value) ? current : current,
                _ => current with { Template = current.Template.With(frontMatter, key, value) },
            });
    }

    // A document laid out, with what rendering it needs besides.
    private sealed record LaidOut(Layout Layout, FontSet Fonts, string? Author, string LanguageTag);

    private sealed class Bound(PdfOptions options) : IMarkdownGenerator
    {
        public string Format => "pdf";

        public string Title => "PDF document";

        public string FileExtension => ".pdf";

        public string ContentType => "application/pdf";

        public string AuthoringGuide() => PdfGenerator.AuthoringGuide(options);

        public GenerationResult Generate(MarkdownDocument document, Stream output) => PdfGenerator.Generate(document, output, options);

        public CheckResult Check(MarkdownDocument document) => PdfGenerator.Check(document, options);

        public override string ToString() => $"{this.Title} ({this.Format})";
    }
}
