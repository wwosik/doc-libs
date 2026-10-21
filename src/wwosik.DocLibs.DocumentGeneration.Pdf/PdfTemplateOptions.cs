namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>
/// The design of the PDF: the built-in <see cref="NeutralPdfTemplateOptions"/>. Each design carries only the
/// features it has. Front matter in the Markdown can override these values.
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

    internal abstract Brand Brand { get; }

    internal virtual bool ShowsClaim => false;

    internal abstract bool ShowsDate { get; }

    /// <summary>The built-in template with that front-matter name, with its defaults, or <see langword="null"/>.</summary>
    internal static PdfTemplateOptions? Create(string name) => name.ToLowerInvariant() switch
    {
        "neutral" => new NeutralPdfTemplateOptions(),
        _ => null,
    };

    /// <summary>A copy with the front-matter <paramref name="key"/> (one of <see cref="Keys"/>) set to <paramref name="value"/>.</summary>
    internal abstract PdfTemplateOptions With(FrontMatter source, string key, string value);
}

/// <summary>The neutral design: dark headings, blue accents, no images and no footer text. The default.</summary>
public sealed record NeutralPdfTemplateOptions : PdfTemplateOptions
{
    private static readonly string[] FrontMatterKeys = ["showDate"];

    /// <summary>The date of generation in the footer of every page but the cover. Default <see langword="true"/>.</summary>
    public bool ShowDate { get; init; } = true;

    internal override string Name => "neutral";

    internal override IReadOnlyList<string> Keys => FrontMatterKeys;

    internal override Brand Brand => Brand.Neutral;

    internal override bool ShowsDate => this.ShowDate;

    internal override PdfTemplateOptions With(FrontMatter source, string key, string value) => this with { ShowDate = source.Bool(key, value) };
}
