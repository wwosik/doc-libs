namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// The template a deck is built on: the built-in master in a brand's design via <see cref="BrandTemplateOptions"/>,
/// or a caller's own <c>.pptx</c> / <c>.potx</c> via <see cref="CustomTemplateOptions"/>. Each carries only the
/// features its template has. Front matter in the Markdown can override these values; see the package documentation
/// for the keys.
/// </summary>
public abstract record PptxTemplateOptions
{
    private protected PptxTemplateOptions()
    {
    }

    /// <summary>The value of the front-matter key <c>template</c> that selects this template.</summary>
    internal abstract string Name { get; }

    /// <summary>The front-matter keys this template supports, besides <c>template</c>, <c>title</c> and <c>author</c>.</summary>
    internal abstract IReadOnlyList<string> Keys { get; }

    /// <summary>Whether content slides show the date of generation.</summary>
    internal virtual bool ShowsDate => false;

    /// <summary>Layouts to use by name instead of by type.</summary>
    internal virtual PptxLayoutNames? LayoutNames => null;

    /// <summary>The brand front matter can name to keep these options, if any.</summary>
    internal virtual Brand? BrandValue => null;

    /// <summary>Whether the deck embeds the brand's fonts.</summary>
    internal virtual bool EmbedsFonts => false;

    /// <summary>The text for the template's footer placeholder; <see langword="null"/>: none written.</summary>
    internal virtual string? FooterText => null;

    /// <summary>Whether this is the built-in master, which picks its layouts itself.</summary>
    internal virtual bool IsBuiltIn => false;

    /// <summary>Front matter <c>listBullets</c>, which wins over <see cref="PptxOptions.ListBullets"/>.</summary>
    internal ListBullets? ListBulletsOverride { get; init; }

    // `listBullets` is taken by every template; the other keys by the template's own `With`.
    private protected PptxTemplateOptions WithListBullets(FrontMatter source, string key, string value) =>
        this with { ListBulletsOverride = source.OneOf(key, value, ("template", ListBullets.Template), ("always", ListBullets.Always)) };

    private protected const string ListBulletsKey = "listBullets";

    /// <summary>A copy with the front-matter <paramref name="key"/> (one of <see cref="Keys"/>) set to <paramref name="value"/>.</summary>
    internal abstract PptxTemplateOptions With(FrontMatter source, string key, string value);

    /// <summary>
    /// The caller's template with the front matter applied on top. <c>template</c> selects one of
    /// <paramref name="brands"/>, <see cref="Brand.Neutral"/> or the template's own brand.
    /// </summary>
    internal static PptxTemplateOptions Apply(FrontMatter frontMatter, PptxTemplateOptions template, IReadOnlyList<Brand>? brands = null)
    {
        var registry = BrandRegistry.Of(template.BrandValue, brands);
        return frontMatter.Apply(
            template,
            (current, name) => registry.Find(name) is not { } brand ? null
                : current.BrandValue is not null && current.Name == brand.Name.ToLowerInvariant() ? current
                : new BrandTemplateOptions { Brand = brand },
            registry.Expected,
            current => current.Name,
            current => current.Keys,
            (current, key, value) => current.With(frontMatter, key, value));
    }

    /// <summary>Writes the template as a <c>.pptx</c> or <c>.potx</c>.</summary>
    internal abstract void Write(Stream output);
}

/// <summary>
/// The built-in master in the design of <see cref="Brand"/>: its colours, font family, footer text and images. A
/// brand with its own <see cref="DocumentGeneration.Brand.Pptx"/> template is built on that template instead, which
/// never gets a date. The default, with <see cref="DocumentGeneration.Brand.Neutral"/>.
/// </summary>
public sealed record BrandTemplateOptions : PptxTemplateOptions
{
    // Static, so that records with equal values stay equal.
    private static readonly string[] FrontMatterKeys = ["showDate", "showTagline", "embedFonts", ListBulletsKey];
    private static readonly string[] OwnTemplateKeys = ["embedFonts", "showFooter", ListBulletsKey];

    /// <summary>The brand. Default <see cref="DocumentGeneration.Brand.Neutral"/>.</summary>
    public Brand Brand { get; init; } = Brand.Neutral;

    /// <summary>The date of generation in the footer of every slide but the cover. Default <see langword="true"/>.</summary>
    public bool ShowDate { get; init; } = true;

    /// <summary>The brand's tagline bottom right on the cover, if it has one. Default <see langword="true"/>.</summary>
    public bool ShowTagline { get; init; } = true;

    /// <summary>
    /// The brand's fonts embedded in the deck, so that it shows them where they are not installed: every family the
    /// deck uses, whole faces, except faces whose licence restricts embedding. Default <see langword="true"/>;
    /// <see langword="false"/> only names the families. No effect for a brand without fonts.
    /// </summary>
    public bool EmbedFonts { get; init; } = true;

    /// <summary>
    /// On a brand's own <c>Pptx</c> template: <see cref="Brand.FooterText"/> in the template's footer placeholder, on
    /// every slide but the cover and the closing slide. Default <see langword="true"/>. The built-in master always shows
    /// the footer text it has.
    /// </summary>
    public bool ShowFooter { get; init; } = true;

    internal override string Name => this.Brand.Name.ToLowerInvariant();

    internal override IReadOnlyList<string> Keys => this.Brand.Pptx is null ? FrontMatterKeys : OwnTemplateKeys;

    internal override bool EmbedsFonts => this.EmbedFonts;

    internal override string? FooterText => this.Brand.Pptx is not null && this.ShowFooter ? this.Brand.FooterText : null;

    internal override bool IsBuiltIn => this.Brand.Pptx is null;

    internal override bool ShowsDate => this.ShowDate && this.Brand.Pptx is null;

    internal override Brand BrandValue => this.Brand;

    internal override PptxLayoutNames? LayoutNames => this.Brand.Pptx is not { } pptx ? null : new PptxLayoutNames
    {
        Title = pptx.TitleLayout,
        Content = pptx.ContentLayout,
        TitleOnly = pptx.TitleOnlyLayout,
        SectionHeader = pptx.SectionHeaderLayout,
        Closing = pptx.ClosingLayout,
    };

    internal override PptxTemplateOptions With(FrontMatter source, string key, string value) => key switch
    {
        ListBulletsKey => this.WithListBullets(source, key, value),
        "showFooter" => this with { ShowFooter = source.Bool(key, value) },
        "showTagline" => this with { ShowTagline = source.Bool(key, value) },
        "embedFonts" => this with { EmbedFonts = source.Bool(key, value) },
        _ => this with { ShowDate = source.Bool(key, value) },
    };

    internal override void Write(Stream output)
    {
        var brand = ResolvedBrand.ForSlides(this.Brand);
        if (brand.Pptx is { } template)
        {
            output.Write(template.Content.Span);
        }
        else
        {
            TemplateBuilder.Write(output, brand, this.ShowTagline);
        }
    }
}

/// <summary>A caller's <c>.pptx</c> or <c>.potx</c> whose masters, layouts and theme the deck uses; its own slides are dropped.</summary>
/// <param name="Template">Read from its current position and left open.</param>
public sealed record CustomTemplateOptions(Stream Template) : PptxTemplateOptions
{
    /// <summary>
    /// Layouts to use by name, for templates whose layouts carry no type. A kind left <see langword="null"/> is
    /// looked up by type as usual.
    /// </summary>
    public PptxLayoutNames? Layouts { get; init; }

    internal override string Name => "custom";

    private static readonly string[] FrontMatterKeys = ["layouts.title", "layouts.content", "layouts.titleOnly", "layouts.sectionHeader", "layouts.closing", ListBulletsKey];

    internal override IReadOnlyList<string> Keys => FrontMatterKeys;

    internal override PptxLayoutNames? LayoutNames => this.Layouts;

    internal override PptxTemplateOptions With(FrontMatter source, string key, string value)
    {
        if (key == ListBulletsKey)
        {
            return this.WithListBullets(source, key, value);
        }

        var layouts = this.Layouts ?? new PptxLayoutNames();
        return this with
        {
            Layouts = key switch
            {
                "layouts.title" => layouts with { Title = value },
                "layouts.content" => layouts with { Content = value },
                "layouts.titleOnly" => layouts with { TitleOnly = value },
                "layouts.closing" => layouts with { Closing = value },
                _ => layouts with { SectionHeader = value },
            },
        };
    }

    internal override void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(this.Template);
        this.Template.CopyTo(output);
    }
}

/// <summary>Layout names (as shown in PowerPoint) to use for each kind of slide.</summary>
public sealed record PptxLayoutNames
{
    /// <summary>The cover, from the first <c>#</c> heading.</summary>
    public string? Title { get; init; }

    /// <summary>Text slides.</summary>
    public string? Content { get; init; }

    /// <summary>Table and image slides.</summary>
    public string? TitleOnly { get; init; }

    /// <summary>Section dividers, from every later <c>#</c> heading.</summary>
    public string? SectionHeader { get; init; }

    /// <summary>
    /// The closing slide, from a <c>#</c> heading with <c>{.closing}</c>. <see langword="null"/>: a layout named like a
    /// closing slide (<c>Closing</c>, <c>End</c>, <c>Thank you</c>), else the cover's.
    /// </summary>
    public string? Closing { get; init; }
}
