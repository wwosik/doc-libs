namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The template a Word document is built on: the built-in template in a brand's design via
/// <see cref="BrandDocxTemplateOptions"/>, or a caller's own <c>.dotx</c> / <c>.docx</c> via
/// <see cref="CustomDocxTemplateOptions"/>. Each carries only the features its template has. Front matter in the
/// Markdown can override these values; see the package documentation for the keys.
/// </summary>
public abstract record DocxTemplateOptions
{
    private protected DocxTemplateOptions()
    {
    }

    /// <summary>The value of the front-matter key <c>template</c> that selects this template.</summary>
    internal abstract string Name { get; }

    /// <summary>The front-matter keys this template supports, besides the ones every template takes.</summary>
    internal abstract IReadOnlyList<string> Keys { get; }

    /// <summary>How error messages name the template.</summary>
    internal abstract string Description { get; }

    /// <summary>The brand front matter can name to keep these options, if any.</summary>
    internal virtual Brand? BrandValue => null;

    /// <summary>Whether this is the built-in template, written from the brand.</summary>
    internal virtual bool IsBuiltIn => false;

    /// <summary>Whether the cover and the footer show the date of generation.</summary>
    internal virtual bool ShowsDate => false;

    /// <summary>Whether the document embeds the brand's fonts.</summary>
    internal virtual bool EmbedsFonts => false;

    /// <summary>The template's styles for each role.</summary>
    internal abstract DocxStyleNames StyleNames { get; }

    /// <summary>A copy with the front-matter <paramref name="key"/> (one of <see cref="Keys"/>) set to <paramref name="value"/>.</summary>
    internal abstract DocxTemplateOptions With(FrontMatter source, string key, string value);

    /// <summary>Writes the template as a <c>.docx</c> or <c>.dotx</c>; <paramref name="date"/> is the date of generation, if shown.</summary>
    internal abstract void Write(Stream output, string? date);
}

/// <summary>
/// The built-in template in the design of <see cref="Brand"/>: its colours, font family, footer text and images. A
/// brand with its own <see cref="DocumentGeneration.Brand.Docx"/> template is built on that template instead, which
/// keeps its own page, headers and footers. The default, with <see cref="DocumentGeneration.Brand.Neutral"/>.
/// </summary>
public sealed record BrandDocxTemplateOptions : DocxTemplateOptions
{
    // Static, so that records with equal values stay equal.
    private static readonly string[] FrontMatterKeys = ["showDate", "showTagline", "embedFonts"];
    private static readonly string[] OwnTemplateKeys = ["embedFonts"];

    /// <summary>The brand. Default <see cref="DocumentGeneration.Brand.Neutral"/>.</summary>
    public Brand Brand { get; init; } = Brand.Neutral;

    /// <summary>The date of generation on the cover and in the footer of every page but the cover. Default <see langword="true"/>.</summary>
    public bool ShowDate { get; init; } = true;

    /// <summary>The brand's tagline bottom right on the cover, if it has one. Default <see langword="true"/>.</summary>
    public bool ShowTagline { get; init; } = true;

    /// <summary>
    /// The brand's fonts embedded in the document, so that it shows them where they are not installed: every family the
    /// document uses, whole faces, except faces whose licence restricts embedding. Default <see langword="true"/>;
    /// <see langword="false"/> only names the families. No effect for a brand without fonts.
    /// </summary>
    public bool EmbedFonts { get; init; } = true;

    /// <summary>The paper size of the built-in template. Default <see cref="DocxPageSize.A4"/>. A brand's own template keeps its own.</summary>
    public DocxPageSize PageSize { get; init; } = DocxPageSize.A4;

    /// <summary>The orientation of the built-in template. Default <see cref="DocxOrientation.Portrait"/>. A brand's own template keeps its own.</summary>
    public DocxOrientation Orientation { get; init; } = DocxOrientation.Portrait;

    internal override string Name => this.Brand.Name.ToLowerInvariant();

    internal override IReadOnlyList<string> Keys => this.Brand.Docx is null ? FrontMatterKeys : OwnTemplateKeys;

    internal override string Description => this.Brand.Docx is null ? "the built-in template" : $"the template of the brand '{this.Brand.Name}'";

    internal override Brand BrandValue => this.Brand;

    internal override bool IsBuiltIn => this.Brand.Docx is null;

    internal override bool ShowsDate => this.ShowDate && this.Brand.Docx is null;

    internal override bool EmbedsFonts => this.EmbedFonts;

    internal override DocxStyleNames StyleNames => this.Brand.Docx?.Styles ?? new DocxStyleNames();

    internal override DocxTemplateOptions With(FrontMatter source, string key, string value) => key switch
    {
        "showTagline" => this with { ShowTagline = source.Bool(key, value) },
        "embedFonts" => this with { EmbedFonts = source.Bool(key, value) },
        _ => this with { ShowDate = source.Bool(key, value) },
    };

    internal override void Write(Stream output, string? date)
    {
        var brand = ResolvedBrand.Resolve(this.Brand);
        if (brand.Docx is { } template)
        {
            output.Write(template.Content.Span);
        }
        else
        {
            DocxTemplateBuilder.Write(output, brand, DocxPage.For(this.PageSize, this.Orientation), this.ShowTagline, date);
        }
    }
}

/// <summary>
/// A caller's <c>.dotx</c> or <c>.docx</c> whose styles, numbering, theme, settings, page, headers and footers the
/// document uses; its own body is dropped.
/// </summary>
/// <param name="Template">Read from its current position and left open.</param>
public sealed record CustomDocxTemplateOptions(Stream Template) : DocxTemplateOptions
{
    /// <summary>
    /// The template's styles for each part of the document, by name as Word shows them. A role left
    /// <see langword="null"/> takes the style of its default name, which is added if the template lacks it; a name
    /// given here that the template lacks throws <see cref="DocumentConversions.DocumentConversionException"/> listing
    /// the template's paragraph styles. Default: every style by its default name.
    /// </summary>
    public DocxStyleNames Styles { get; init; } = new();

    internal override string Name => "custom";

    internal override IReadOnlyList<string> Keys => DocxRoles.Keys;

    internal override string Description => "the caller's template";

    internal override DocxStyleNames StyleNames => this.Styles ?? new DocxStyleNames();

    internal override DocxTemplateOptions With(FrontMatter source, string key, string value) =>
        this with { Styles = DocxRoles.OfKey(key).With!(this.StyleNames, value) };

    internal override void Write(Stream output, string? date)
    {
        ArgumentNullException.ThrowIfNull(this.Template);
        this.Template.CopyTo(output);
    }
}
