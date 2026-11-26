namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>
/// The design of the PDF: <see cref="BrandPdfTemplateOptions"/>, the built-in design in a brand's colours, fonts and
/// images. Front matter in the Markdown can override these values.
/// </summary>
public abstract record PdfTemplateOptions
{
    private protected PdfTemplateOptions()
    {
    }

    /// <summary>The value of the front-matter key <c>template</c> that selects this template.</summary>
    internal abstract string Name { get; }

    /// <summary>The front-matter keys this template supports, besides the shared ones.</summary>
    internal abstract IReadOnlyList<string> Keys { get; }

    internal abstract Brand BrandValue { get; }

    internal virtual bool ShowsTagline => false;

    internal abstract bool ShowsDate { get; }

    internal virtual PdfTone ToneValue => PdfTone.Light;

    /// <summary>A copy with the front-matter <paramref name="key"/> (one of <see cref="Keys"/>) set to <paramref name="value"/>.</summary>
    internal abstract PdfTemplateOptions With(FrontMatter source, string key, string value);
}

/// <summary>The built-in design in the colours, fonts and images of <see cref="Brand"/>. The default, with <see cref="DocumentGeneration.Brand.Neutral"/>.</summary>
public sealed record BrandPdfTemplateOptions : PdfTemplateOptions
{
    // Static, so that records with equal values stay equal.
    private static readonly string[] FrontMatterKeys = ["showDate", "showTagline", ToneKey];

    internal const string ToneKey = "tone";

    /// <summary>The brand. Default <see cref="DocumentGeneration.Brand.Neutral"/>.</summary>
    public Brand Brand { get; init; } = Brand.Neutral;

    /// <summary>The date of generation in the footer of every page but the cover. Default <see langword="true"/>.</summary>
    public bool ShowDate { get; init; } = true;

    /// <summary>The brand's tagline bottom right on the cover, if it has one. Default <see langword="true"/>.</summary>
    public bool ShowTagline { get; init; } = true;

    /// <summary>
    /// Light or dark pages. Default <see cref="PdfTone.Light"/>, whatever the brand: a dark PDF is made only on request,
    /// here or with the front-matter key <c>tone: dark</c>.
    /// </summary>
    public PdfTone Tone { get; init; }

    internal override string Name => this.Brand.Name.ToLowerInvariant();

    internal override IReadOnlyList<string> Keys => FrontMatterKeys;

    internal override Brand BrandValue => this.Brand;

    internal override bool ShowsTagline => this.ShowTagline;

    internal override bool ShowsDate => this.ShowDate;

    internal override PdfTone ToneValue => this.Tone;

    internal override PdfTemplateOptions With(FrontMatter source, string key, string value) => key switch
    {
        "showTagline" => this with { ShowTagline = source.Bool(key, value) },
        ToneKey => this with { Tone = source.OneOf(key, value, ("light", PdfTone.Light), ("dark", PdfTone.Dark)) },
        _ => this with { ShowDate = source.Bool(key, value) },
    };
}

/// <summary>Whether the pages of a PDF are light or dark.</summary>
public enum PdfTone
{
    /// <summary>White pages with dark text in the brand's <see cref="Brand.Colors"/>: the default, and what prints well.</summary>
    Light,

    /// <summary>
    /// Every page, the cover included, in a dark background with light text, in the brand's <see cref="Brand.SlideColors"/>
    /// and with its <see cref="Brand.LogoOnDark"/>; diagrams and charts drawn for a dark background, as on dark slides.
    /// The PDF prints dark too.
    /// </summary>
    Dark,
}
