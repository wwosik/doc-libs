using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// Generates Word documents (<c>.docx</c>) from Markdown: a cover from a leading <c>#</c> heading and the paragraphs
/// after it, an optional table of contents, and the body, on the built-in template in a brand's design or on a
/// caller's template, whose styles every paragraph references by name. Front matter at the top of the Markdown
/// overrides the options.
/// </summary>
public static class DocxGenerator
{
    private const string TocKey = FrontMatter.TocKey;
    private const string TocDepthKey = FrontMatter.TocDepthKey;
    private const string HeadingNumbersKey = FrontMatter.HeadingNumbersKey;
    private const string JustifyKey = FrontMatter.JustifyKey;

    // Keys of decks, accepted so that one Markdown serves every format: their values are checked, then ignored.
    private const string TransitionKey = "transition";
    private const string BuildKey = "build";
    private const string NumberChaptersKey = "numberChapters";

    /// <summary>
    /// Writes <paramref name="document"/> as a <c>.docx</c> to <paramref name="output"/>. Returns the notes for the
    /// writer; <see cref="GenerationResult.Pages"/> is 0, because Word lays out the pages itself.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// The front matter is invalid or names an unknown brand, a style the template options name is not in the template,
    /// the language is not a language tag, or the template is damaged.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The brand is invalid, or two of <see cref="DocxOptions.Brands"/> share a name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DocxOptions.TableOfContentsDepth"/> is not 1, 2 or 3.</exception>
    public static GenerationResult Generate(MarkdownDocument document, Stream output, DocxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        var notes = new GenerationNotes();
        Write(document, output, options ?? DocxOptions.Default, notes);
        return notes.Result(0);
    }

    /// <summary>
    /// Checks <paramref name="document"/> without writing a document: generates it into nothing and returns the notes
    /// <see cref="Generate"/> would, the notes of the style rules of <see cref="DocxOptions.Checks"/> (paragraph length,
    /// heading length, alt text), every image that is missing or a web address, and statistics;
    /// <see cref="CheckStatistics.Pages"/> is 0, because Word lays out the pages. An error in the document is returned as
    /// <see cref="CheckResult.Error"/>, not thrown.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The brand or <see cref="DocxOptions.Checks"/> is invalid, or two of <see cref="DocxOptions.Brands"/> share a name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DocxOptions.TableOfContentsDepth"/> is not 1, 2 or 3.</exception>
    public static CheckResult Check(MarkdownDocument document, DocxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= DocxOptions.Default;
        return DocumentCheck.Run(document, options.Checks ?? CheckOptions.Default, Format.Docx, options.CodeBlockRenderers, (checkedDocument, renderers, notes) =>
        {
            Write(checkedDocument, Stream.Null, options with { CodeBlockRenderers = renderers }, notes);
            return new CheckRun(0);
        });
    }

    // Generate's work: the document written to output, the notes added to notes.
    private static void Write(MarkdownDocument document, Stream output, DocxOptions options, GenerationNotes notes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TableOfContentsDepth, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TableOfContentsDepth, 3, nameof(options));

        var frontMatter = FrontMatter.Read(document.Markdown, Format.Docx);
        var labels = frontMatter.Labels();
        options = Apply(frontMatter, options);
        var template = options.Template ?? DocxOptions.Default.Template;
        var brand = template.BrandValue is { } value ? ResolvedBrand.Resolve(value) : null;
        var marking = Marking.Resolve(frontMatter, options.Classification, options.Draft, notes, Format.Docx);
        var language = DocumentLanguage.Resolve(frontMatter.Language, options.Language, options.Texts, notes, Format.Docx);
        var content = DocxContent.Parse(frontMatter.Body);
        int LineOf(Markdig.Syntax.MarkdownObject block) => block.Line + 1 + frontMatter.BodyLine;
        MarkdownSyntax.ReportMissingFootnotes(content.Source, LineOf, notes);

        // Build markers are for decks: checked like there, then left out wherever they are.
        BuildMarker.Check(content.Source, Format.Docx, LineOf);
        SceneSyntax.Check(content.Source, Format.Docx, LineOf);
        var now = options.TimeProvider.GetUtcNow();
        var date = template.ShowsDate ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null;
        var typography = Typography.For(
            Typography.IsOn(frontMatter, options.Typography),
            language.Tag,
            options.TypographyRules,
            brand?.Fonts is { } faces ? c => faces.HasEveryTextFace(c) : _ => true,
            notes);
        var title = Typeset(typography, frontMatter.Title, frontMatter.LineOf(FrontMatter.TitleKey)) ?? document.Metadata.GetValueOrDefault(MetadataKeys.Title)
            ?? Typeset(typography, content.Cover?.Title, content.Cover is null ? 0 : LineOf(content.Source.OfType<Markdig.Syntax.HeadingBlock>().First()));
        var author = Typeset(typography, frontMatter.Author, frontMatter.LineOf(FrontMatter.AuthorKey)) ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author);
        var codeFont = options.CodeFont ?? brand?.CodeFontFamily ?? DocxOptions.DefaultCodeFont;

        using var buffer = new MemoryStream();
        template.Write(buffer, date, options.HeadingNumbers, codeFont);
        DocxPackage.ChangeMainType(buffer, DocxPackage.TemplateType, DocxPackage.DocumentType);
        try
        {
            using (var package = WordprocessingDocument.Open(buffer, isEditable: true))
            {
                if (package.DocumentType is WordprocessingDocumentType.Template or WordprocessingDocumentType.MacroEnabledTemplate)
                {
                    // A template the content type alone did not turn into a document, such as a .dotm; its macros go.
                    package.ChangeDocumentType(WordprocessingDocumentType.Document);
                }

                var main = package.MainDocumentPart ?? throw new InvalidDataException("The package has no word/document.xml.");
                var section = main.Document?.Body?.Elements<SectionProperties>().LastOrDefault();
                var page = DocxPage.Of(section);
                // Styles a template lacks are added unnumbered: a caller's headings keep their own numbering.
                // A caller's template has no brand: the deck-syntax styles added to it take its theme colours.
                var design = new StyleDesign(brand ?? ResolvedBrand.Neutral, page.CoverTitleSpace, page.TextWidth) { CodeFont = codeFont, Theme = brand is null };
                var styles = DocxStyles.Resolve(main, template.StyleNames, design, template.Description);
                DocxLanguage.Set(main, language.Tag);
                var context = new DocxContext(main, styles, notes, language.Texts, language.Tag, codeFont, options)
                {
                    Page = page,
                    Document = document,
                    LineOffset = frontMatter.BodyLine,
                    Markdown = frontMatter.Body,
                    Brand = template.BrandValue ?? Brand.Neutral,
                    HeadingNumbers = template.IsBuiltIn && options.HeadingNumbers,
                    Labels = labels,
                    PageBreaks = content.PageBreaks,
                    Design = design,
                    Marking = marking,
                    Typography = typography,
                    Highlighting = CodeHighlighting.For(frontMatter, options.HighlightCode, notes),
                };
                DocxWriter.Write(content, context, DocxBody.Instance, author, date, options.TableOfContents, options.TableOfContentsDepth);
                DocxMarking.Apply(context, template.IsBuiltIn ? LogoIndent(brand) : null, (brand ?? ResolvedBrand.Neutral).FontFamily);

                var fields = DocxPlaceholders.Fill(main, title, author);
                if (options.TableOfContents || fields)
                {
                    DocxSettings.Set(main, new UpdateFieldsOnOpen());
                }

                if (template.EmbedsFonts && brand?.Fonts is { } fonts)
                {
                    DocxFontEmbedding.Embed(main, fonts, codeFont);
                }

                var core = package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart();
                CoreProperties.Write(core, title, author, language.Tag, now);
                CoreProperties.ContentStatus(core, DocxMarking.Status(context));
            }
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or XmlException or FormatException)
        {
            throw new DocumentConversionException(Format.Docx, $"The template is damaged ({template.Description}): " + ex.Message, ex);
        }

        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    /// <summary>
    /// This generator bound to <paramref name="options"/>, as an <see cref="IMarkdownGenerator"/> (format <c>docx</c>) for
    /// <see cref="DocumentTool"/>: its <c>Generate</c>, <c>Check</c> and <c>AuthoringGuide</c> are this class's methods with
    /// these options. The options are not checked until one of them is called.
    /// </summary>
    public static IMarkdownGenerator ForOptions(DocxOptions? options = null) => new Bound(options ?? DocxOptions.Default);

    /// <summary>
    /// The authoring guide for whoever writes the Markdown (often an AI agent): how headings become the cover and the
    /// table of contents, the front matter keys of the options' template and brands, and the characters the brand's fonts
    /// lack. Markdown starting at <c>##</c>, in English, so a host can put it under a heading of its own and add what
    /// only it knows. Pass the same options as to <see cref="Generate"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The brand is invalid, or two of <see cref="DocxOptions.Brands"/> share a name.</exception>
    public static string AuthoringGuide(DocxOptions? options = null)
    {
        options ??= DocxOptions.Default;
        var template = options.Template ?? DocxOptions.Default.Template;
        var registry = BrandRegistry.Of(template.BrandValue, options.Brands);
        var brand = template.BrandValue is { } value ? ResolvedBrand.Resolve(value) : null;
        var codeFont = options.CodeFont ?? brand?.CodeFontFamily ?? DocxOptions.DefaultCodeFont;
        var meanings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["showDate"] = "`true` or `false`: today's date on the cover and in the footer. Default `true`.",
            ["showTagline"] = "`true` or `false`: the design's tagline on the cover, if it has one. Default `true`.",
            ["embedFonts"] = "`true` or `false`. Leave it out: by default the document carries its fonts.",
            [TocKey] = $"`true` or `false`: a table of contents after the cover. Default `{(options.TableOfContents ? "true" : "false")}`.",
            [TocDepthKey] = $"`1`, `2` or `3`: the heading levels the table of contents lists. Default `{options.TableOfContentsDepth.ToString(CultureInfo.InvariantCulture)}`.",
            [HeadingNumbersKey] = template.IsBuiltIn
                ? $"`true` or `false`: numbers on the first three heading levels (1, 1.1, 1.1.1). Default `{(options.HeadingNumbers ? "true" : "false")}`."
                : "`true` or `false`. This template's heading styles decide the numbers, so it has no effect.",
            [JustifyKey] = $"`true` or `false`: body text justified, without hyphenation. Default `{(options.Justify ? "true" : "false")}`.",
        };
        foreach (var role in DocxRoles.All.Where(r => r.Key is not null))
        {
            meanings[DocxRoles.KeyPrefix + role.Key] = $"The name of the template's style for {role.Description}, as Word shows it.";
        }

        (string, string) deckKeys = (
            "transition`, `build`, `numberChapters",
            "Settings for decks. A Word document accepts them and ignores them.");
        (string, string) classification = (
            FrontMatter.ClassificationKey,
            $"`public`, `internal` or `confidential`: the marking in the header of every page and above the cover title. Default `{options.Classification.ToString().ToLowerInvariant()}`"
                + (options.Classification == Classification.Public ? ", which shows nothing." : "; a lower value has no effect."));
        (string, string) draft = (
            FrontMatter.DraftKey,
            options.Draft
                ? "`true` or `false`. The document is a draft whatever this says: \"Draft\" joins the marking, and every page has a watermark."
                : "`true` or `false`: `true` adds \"Draft\" to the marking and a watermark to every page. Default `false`.");
        (string, string) labels = (
            "labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral",
            "Status labels, separated by `;`: a table cell that is only one of them, as code (`` `Done` ``), is shown as a coloured label.");
        return AuthoringGuides.Fill(
            AuthoringGuides.Template(typeof(DocxGenerator).Assembly, "Docx"),
            new Dictionary<string, string>
            {
                ["frontMatter"] = AuthoringGuides.FrontMatter(
                    registry.Expected,
                    options.Language,
                    [.. template.Keys.Concat([TocKey, TocDepthKey, HeadingNumbersKey, JustifyKey]).Select(k => (k, meanings[k])), classification, draft, deckKeys, labels],
                    options.Typography, options.HighlightCode),
                ["characters"] = Characters(brand?.Fonts, codeFont),
                ["imageFormats"] = options.ImageConverters.Count > 0
                    ? "Images are PNG, JPEG or GIF, or SVG: an `.svg` image keeps its SVG, which Word draws sharp\n  at any size."
                    : "Images are PNG, JPEG or GIF; an `.svg` image is an error here.",
                ["drawings"] = Drawings(options),
                ["icons"] = options.Icons is null ? string.Empty
                    : options.ImageConverters.Count > 0
                        ? AuthoringGuides.Icons(options.Icons, "A Word document draws it in the line of text, one line high.")
                        : "## Icons\n\n`:icon-name:` (such as `:icon-circle-check:`) is not drawn in this setup: the icon is left\n"
                            + "out. Leave icons out of a document meant for Word. An unknown name is an error.\n",
                ["diagrams"] = AuthoringGuides.Renderers(options.CodeBlockRenderers),
                ["headingNumbers"] = !template.IsBuiltIn
                    ? "The template's heading styles decide whether headings are numbered; `headingNumbers` has no effect."
                    : options.HeadingNumbers
                        ? "Headings of the first three levels are numbered `1`, `1.1`, `1.1.1`; deeper ones are not. `headingNumbers: false` in the front matter removes the numbers."
                        : "Headings are not numbered. `headingNumbers: true` in the front matter numbers the first three levels `1`, `1.1`, `1.1.1`.",
                ["limits"] = AuthoringGuides.Limits(options.Checks ?? CheckOptions.Default, Format.Docx),
                ["notes"] = AuthoringGuides.Notes(Format.Docx),
            });
    }

    /// <summary>The caller's options with the front matter applied on top.</summary>
    internal static DocxOptions Apply(FrontMatter frontMatter, DocxOptions options)
    {
        var template = options.Template ?? DocxOptions.Default.Template;
        var brands = BrandRegistry.Of(template.BrandValue, options.Brands);
        return frontMatter.Apply(
            options with { Template = template },
            (current, name) => brands.Find(name) is not { } brand ? null
                : current.Template.BrandValue is not null && current.Template.Name == brand.Name.ToLowerInvariant() ? current
                : current with { Template = new BrandDocxTemplateOptions { Brand = brand } },
            brands.Expected,
            current => current.Template.Name,
            current => [.. current.Template.Keys, TocKey, TocDepthKey, HeadingNumbersKey, JustifyKey, TransitionKey, BuildKey, NumberChaptersKey],
            (current, key, value) => key switch
            {
                TocKey => current with { TableOfContents = frontMatter.Bool(key, value) },
                TocDepthKey => current with { TableOfContentsDepth = frontMatter.OneOf(key, value, ("1", 1), ("2", 2), ("3", 3)) },
                HeadingNumbersKey => current with { HeadingNumbers = frontMatter.Bool(key, value) },
                JustifyKey => current with { Justify = frontMatter.Bool(key, value) },
                TransitionKey => frontMatter.OneOf(key, value, [.. LayoutSyntax.Transitions.Select(t => (t, current))]),
                BuildKey => frontMatter.OneOf(key, value, ("none", current), ("lists", current)),
                NumberChaptersKey => frontMatter.Bool(key, value) ? current : current,
                _ => current with { Template = current.Template.With(frontMatter, key, value) },
            });
    }

    // The built-in header's logo sits at the right margin, 18 pt high: the marking right-aligned beside it ends 6 pt
    // before it.
    private static int? LogoIndent(ResolvedBrand? brand) =>
        brand?.Logo is { } logo && ImageInfo.Read(logo) is { Width: > 0, Height: > 0 } info ? (18 * 20 * info.Width / info.Height) + 120 : null;

    // What becomes of charts, diagrams, QR codes and drawn containers with the options' renderers and image converters.
    private static string Drawings(DocxOptions options)
    {
        var rendered = options.CodeBlockRenderers.Count > 0;
        var text = new StringBuilder(rendered
            ? "A code block of a kind described below becomes a chart or a picture; any other stays code.\n\n"
            : "No code block is drawn in this setup: every one stays code.\n\n");
        if (rendered)
        {
            text.Append("""
                - **Charts** (`chart` blocks, where described below) are native Word charts as wide as the
                  text, whose data can be edited in Word.

                """);
        }

        if (options.ImageConverters.Count == 0)
        {
            text.Append("""
                - **Diagrams and other drawings are not drawn in this setup:** a diagram block stays code, a
                  `qr:` image is its alt text, and `::: timeline`, `::: gantt`, `::: steps` and
                  `::: riskmatrix` show their content in sequence. Leave them out of a document meant for Word.

                """);
        }
        else
        {
            text.Append("""
                - **Diagrams** and other drawings are pictures, made smaller to fit the page, never larger;
                  their alt text is the block's first line.

                """);
        }

        if (rendered)
        {
            text.Append("""
                - **Captions:** a paragraph starting `Figure: ` right after a drawn block is its caption,
                  numbered with the images and set below it: `Figure: Order flow` gives "Figure 2: Order flow".
                  After anything else it is shown as text.

                """);
        }

        if (options.ImageConverters.Count > 0)
        {
            text.Append("""
                - **QR codes:** `![Alt text](qr:https://example.com)` is a QR code of the text after `qr:` (at
                  most 1 000 characters; shorter addresses scan better). `"scale=2"` in the title enlarges it,
                  `"caption"` puts the alt text under it.
                - **Timelines and Gantt charts:** `::: timeline` around a list of 2–8
                  `- **date** Title: description`; `::: gantt` around a table of 1–20 rows of task, start and
                  end (no end: a milestone). Dates: `2026`, `2026-Q2`, `2026-03`, `2026-03-15`; what ended
                  before today is grey. `today=2026-11-01` after the name pins today;
                  `grid=week|month|quarter` sets a Gantt's grid.
                - **Steps and risk matrices:** `::: steps` around a list of 2–7 `- **Title** – description`
                  draws chevrons in a row, `{.current}` at the end of one item marks it. `::: riskmatrix`
                  around a table of 1–15 rows of risk, likelihood and impact (each 1–5) puts numbered dots on
                  a 5 × 5 grid.

                ````markdown
                ## Plan

                ::: timeline
                - **2026-01** Start
                - **2026-06** Go-live: all regions
                :::

                ::: riskmatrix
                | Risk | Likelihood | Impact |
                | ---- | ---------- | ------ |
                | Supplier delay | 4 | 3 |
                :::
                ````

                """);
        }

        return text.ToString();
    }

    // The characters the brand's fonts lack, which Word shows in a substitute font.
    // A front-matter title or author, or the cover's title, typeset when typography is on.
    private static string? Typeset(Typography? typography, string? text, int line) =>
        text is null || typography is null ? text : typography.Apply(text, line);

    private static string Characters(ResolvedFonts? fonts, string codeFont)
    {
        if (fonts is null)
        {
            return "Each character is shown in the document's font, or in a substitute where that font lacks it. Prefer "
                + "plain characters (`•`, `–`, `→`, typographic quotes) to symbols, and leave out emoji.";
        }

        var mono = fonts.Mono is { } monoFace && fonts.MonoFamily == codeFont ? monoFace : null;
        return AuthoringGuides.Characters(
            c => TrueTypeInfo.HasCharacter(fonts.Regular, c),
            c => mono is null || TrueTypeInfo.HasCharacter(mono, c),
            $"The document is set in {fonts.Family}. A character this font lacks is shown in a substitute font and looks "
                + "out of place. Leave out emoji, and write these symbols as shown:");
    }

    private sealed class Bound(DocxOptions options) : IMarkdownGenerator
    {
        public string Format => "docx";

        public string Title => "Word document";

        public string FileExtension => ".docx";

        public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        public string AuthoringGuide() => DocxGenerator.AuthoringGuide(options);

        public GenerationResult Generate(MarkdownDocument document, Stream output) => DocxGenerator.Generate(document, output, options);

        public CheckResult Check(MarkdownDocument document) => DocxGenerator.Check(document, options);

        public override string ToString() => $"{this.Title} ({this.Format})";
    }
}
