using System.Globalization;
using System.Xml;
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
    /// <summary>
    /// Writes <paramref name="document"/> as a <c>.pptx</c> to <paramref name="output"/>. Returns the number of slides
    /// and the notes for the writer: slides split, set at the smallest size or still overfull, images enlarged, diagrams
    /// left as code, HTML shown as text.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// An image is not among the document's resources or not a PNG, JPEG or GIF, the front matter is invalid or names
    /// an unknown brand, a named layout does not exist, the language is not a language tag, or the template is damaged.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The brand is invalid, two of <see cref="PptxOptions.Brands"/> share a name, or the roles of a
    /// <see cref="CustomTemplateOptions"/> are invalid.
    /// </exception>
    public static GenerationResult Generate(MarkdownDocument document, Stream output, PptxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        options ??= PptxOptions.Default;

        var frontMatter = FrontMatter.Read(document.Markdown, Format.Pptx);
        var template = PptxTemplateOptions.Apply(frontMatter, options.Template ?? PptxOptions.Default.Template, options.Brands);
        var labels = frontMatter.Labels();

        // The marking is checked like in Word documents; decks show it with E03-S04.
        Marking.Resolve(frontMatter, Classification.Public, false, new GenerationNotes(), Format.Pptx);
        if (template is CustomTemplateOptions custom && CustomRolesProblem(custom.Roles) is { } problem)
        {
            throw new ArgumentException($"{nameof(CustomTemplateOptions)}.{problem}.", nameof(options));
        }

        var date = template.ShowsDate
            ? options.TimeProvider.GetLocalNow().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : null;

        using var buffer = new MemoryStream();
        template.Write(buffer);
        var notes = new GenerationNotes();
        var language = DocumentLanguage.Resolve(frontMatter.Language, options.Language, options.Texts, notes, Format.Pptx);
        int count;

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
            var resolved = brand is null ? null : ResolvedBrand.ForSlides(brand);
            var palette = resolved is null ? SlidePalette.Scheme : dark ? SlidePalette.ForDark(resolved) : SlidePalette.From(resolved);
            var codeFont = options.CodeFont ?? resolved?.CodeFontFamily ?? PptxOptions.DefaultCodeFont;
            var author = frontMatter.Author ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author);

            // The writer opens the template first: the parser asks it which placeholders a layout has.
            var writer = new DeckWriter(
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
                (template.ListBulletsOverride ?? options.ListBullets) == ListBullets.Always,
                notes,
                language.Tag,
                language.Texts,
                template.TemplateRoles,
                template.FooterPattern,
                author,
                template.Description);
            var slides = DeckParser.Parse(
                document with { Markdown = frontMatter.Body },
                options with { Transition = template.TransitionOverride ?? options.Transition, Build = template.BuildOverride ?? options.Build },
                brand is null ? null : dark ? ResolvedBrand.DarkSlideBrand(brand) : ResolvedBrand.SlideBrand(brand),
                frontMatter.BodyLine,
                notes,
                language.Texts,
                template.NumberChaptersOverride ?? writer.NumbersChapters,
                writer.Slots);
            var title = frontMatter.Title ?? Title(document, slides);
            count = writer.Write(slides, title);
            if (template.EmbedsFonts && resolved?.Fonts is { } fonts)
            {
                FontEmbedding.Embed(package.PresentationPart!, fonts, codeFont);
            }

            CoreProperties.Write(package.CoreFilePropertiesPart ?? package.AddCoreFilePropertiesPart(), title, author, language.Tag);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or XmlException or FormatException)
        {
            throw new DocumentConversionException(Format.Pptx, "The template is damaged: " + ex.Message, ex);
        }

        buffer.Position = 0;
        buffer.CopyTo(output);
        return notes.Result(count);
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
                ["frontMatter"] = AuthoringGuides.FrontMatter(registry.Expected, options.Language, [.. template.Keys.Select(k => (k, KeyMeanings[k])), Labels]),
                ["characters"] = Characters(brand?.Fonts, codeFont),
                ["templateLayouts"] = TemplateLayouts(brand?.Pptx, (template as CustomTemplateOptions)?.Roles),
                ["icons"] = AuthoringGuides.Icons(
                    options.Icons,
                    "Slides show it only alone after a card's `###` (as its figure) and at the start of every item of a list "
                    + "(as bullets); elsewhere it is left out."),
                ["notes"] = AuthoringGuides.Notes(Format.Pptx),
            });
    }

    // "Roles.<property> <reason>" for invalid roles of a caller's template, else null.
    private static string? CustomRolesProblem(PptxTemplateRoles? roles) =>
        roles is null ? "Roles is missing"
        : roles.Problem() is { } problem ? $"Roles.{problem.Property} {problem.Reason}"
        : null;

    // A brand's own template: its layouts by name, for `layout="…"`, and the layouts of its roles; a caller's template:
    // the layouts its roles name. With text levels, what `###` becomes.
    private static string TemplateLayouts(PptxBrandTemplate? template, PptxTemplateRoles? custom)
    {
        var parts = new List<string>();
        var roles = new List<(LayoutRole Role, string Name)>();
        if (template is not null)
        {
            using var package = PresentationDocument.Open(new MemoryStream(template.Content.ToArray()), isEditable: false);
            var layouts = package.PresentationPart?.SlideMasterParts.SelectMany(m => m.SlideLayoutParts).ToList() ?? [];
            var names = layouts
                .Select(l => l.SlideLayout?.CommonSlideData?.Name?.Value)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count > 0)
            {
                parts.Add("**Layouts of this design.** `layout=\"Name\"` after a `#` or `##` heading puts that slide on one of these: "
                    + string.Join(", ", names.Select(n => $"`{n}`")) + ". Use them where a slide needs it, such as a layout for long titles.");
            }

            try
            {
                var found = TemplateRoles.Resolve(layouts, template.Roles);
                roles.AddRange(Enum.GetValues<LayoutRole>()
                    .Select(r => (Role: r, Name: found.Layout(r)?.SlideLayout?.CommonSlideData?.Name?.Value))
                    .Where(r => r.Name is not null)
                    .Select(r => (r.Role, r.Name!)));
            }
            catch (DocumentConversionException)
            {
                // A role naming a layout the template lacks is reported when a deck is generated.
            }
        }
        else if (custom?.Layouts is { } declared)
        {
            roles.AddRange(new (LayoutRole, string?)[]
            {
                (LayoutRole.TwoContent, declared.TwoContent), (LayoutRole.TextAndPicture, declared.TextAndPicture),
                (LayoutRole.Picture, declared.Picture), (LayoutRole.Agenda, declared.Agenda), (LayoutRole.Quote, declared.Quote),
                (LayoutRole.Statement, declared.Statement), (LayoutRole.Contact, declared.Contact), (LayoutRole.Chart, declared.Chart),
                (LayoutRole.TwoCharts, declared.TwoCharts), (LayoutRole.Dark, declared.Dark),
            }.Where(r => r.Item2 is not null).Select(r => (r.Item1, r.Item2!)));
        }

        if (roles.Count > 0)
        {
            parts.Add("**Layouts for kinds of slide**, for `layout=\"Name\"`: "
                + string.Join(", ", roles.Select(r => $"{RoleText(r.Role)} `{r.Name}`")) + ".");
        }

        // Which kinds of slide have a layout of their own; on a caller's template the ones found by type are not known here.
        var kinds = new[] { LayoutRole.Agenda, LayoutRole.Quote, LayoutRole.Statement, LayoutRole.Contact, LayoutRole.Chart, LayoutRole.TwoCharts };
        if (template is null && custom is null)
        {
            parts.Add("This design has a layout for every kind of slide.");
        }
        else if (roles.Where(r => kinds.Contains(r.Role)).Select(r => RoleText(r.Role)).ToList() is { Count: > 0 } own)
        {
            parts.Add($"**Kinds of slide with a layout of their own here:** {string.Join(", ", own)}. "
                + (custom is not null ? "Others use a layout of the right type, if the template has one; else they are " : "The others are ")
                + "drawn as layout slides with the same content.");
        }
        else
        {
            parts.Add("**Kinds of slide** are drawn as layout slides with the same content"
                + (custom is not null ? ", unless the template has a layout of the right type." : ": this design has no layouts for them."));
        }

        if ((template?.Roles ?? custom)?.Layouts?.TwoContent is { } twoContent)
        {
            parts.Add($"A `##` slide whose only block is `::: columns` with two columns of text goes on `{twoContent}`, one column in each box.");
        }

        if (template is not null || custom is not null)
        {
            parts.Add("**Layouts with several boxes.** On a `layout=\"…\"` slide, lines `---` (each after a blank line) cut the text "
                + "into parts, which fill the layout's text boxes in order; images fill its picture boxes, cropped to their "
                + "shape, and charts its chart boxes. What has no box left continues on a further slide. Such a slide takes "
                + "text, images, charts, tables and diagrams, but no `:::` containers, alerts, `.dark` or `kicker`.\n\n"
                + "````text\n## Two offers {layout=\"Two Content\"}\n\n- Basic: 10 users\n\n---\n\n- Premium: unlimited\n````");
        }

        if ((template?.Roles ?? custom)?.TextLevels is not null)
        {
            parts.Add("On text slides a `###` heading is a sub-headline in the design's own style, not a bold line.");
        }

        return string.Join("\n\n", parts);
    }

    private static string RoleText(LayoutRole role) => role switch
    {
        LayoutRole.TwoContent => "two columns",
        LayoutRole.TextAndPicture => "text and picture",
        LayoutRole.TwoCharts => "two charts",
        LayoutRole.Dark => "dark slides",
        _ => role.ToString().ToLowerInvariant(),
    };

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
        ["layouts.twoContent"] = "The name of the template's layout with two columns.",
        ["layouts.textAndPicture"] = "The name of the template's layout for text beside a picture.",
        ["layouts.picture"] = "The name of the template's layout for one picture.",
        ["layouts.agenda"] = "The name of the template's layout for the agenda.",
        ["layouts.quote"] = "The name of the template's layout for a quote.",
        ["layouts.statement"] = "The name of the template's layout for one large statement.",
        ["layouts.contact"] = "The name of the template's layout for contact people.",
        ["layouts.chart"] = "The name of the template's layout for one chart.",
        ["layouts.twoCharts"] = "The name of the template's layout for two charts.",
        ["layouts.dark"] = "The name of the template's layout for `{.dark}` slides.",
        ["showFooter"] = "`true` or `false`: the design's footer text on every slide but the cover. Default `true`.",
        ["numberChapters"] = "`true` or `false`: \"01\", \"02\", … on the section dividers after the cover. Default: as the design says.",
        ["listBullets"] = "`always` to show bullets on every list level, also where the design's first level has none; `template` (the default) leaves it to the design.",
        ["transition"] = "How every slide is brought on when presented: " + string.Join(", ", LayoutSyntax.Transitions.Select(t => $"`{t}`")) + ". Default `none`. A heading's `transition=\"…\"` wins; see [Presenting](#presenting).",
        ["build"] = "`lists` builds every top-level list item by item; `none` (the default) only what has a marker. See [Presenting](#presenting).",
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
}
