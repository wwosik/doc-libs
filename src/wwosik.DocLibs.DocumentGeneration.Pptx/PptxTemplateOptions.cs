namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// The template a deck is built on: the built-in <see cref="NeutralTemplateOptions"/> or a caller's own via
/// <see cref="CustomTemplateOptions"/>. Each carries only the features its template has.
/// Front matter in the Markdown can override these values; see the package documentation for the keys.
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

    /// <summary>The built-in template with that front-matter name, with its defaults, or <see langword="null"/>.</summary>
    internal static PptxTemplateOptions? Create(string name) => name.ToLowerInvariant() switch
    {
        "neutral" => new NeutralTemplateOptions(),
        _ => null,
    };

    /// <summary>A copy with the front-matter <paramref name="key"/> (one of <see cref="Keys"/>) set to <paramref name="value"/>.</summary>
    internal abstract PptxTemplateOptions With(FrontMatter source, string key, string value);

    /// <summary>The caller's template with the front matter applied on top.</summary>
    internal static PptxTemplateOptions Apply(FrontMatter frontMatter, PptxTemplateOptions template) =>
        frontMatter.Apply(
            template,
            (current, name) => Create(name) is { } chosen ? (chosen.GetType() == current.GetType() ? current : chosen) : null,
            "'neutral'",
            current => current.Name,
            current => current.Keys,
            (current, key, value) => current.With(frontMatter, key, value));

    /// <summary>Writes the template as a <c>.pptx</c> or <c>.potx</c>.</summary>
    internal abstract void Write(Stream output);
}

/// <summary>The built-in neutral template: white, Arial, blue accents, no images and no footer text. The default.</summary>
public sealed record NeutralTemplateOptions : PptxTemplateOptions
{
    /// <summary>The date of generation in the footer of every slide but the cover. Default <see langword="true"/>.</summary>
    public bool ShowDate { get; init; } = true;

    internal override string Name => "neutral";

    private static readonly string[] FrontMatterKeys = ["showDate"];

    internal override IReadOnlyList<string> Keys => FrontMatterKeys;

    internal override bool ShowsDate => this.ShowDate;

    internal override PptxTemplateOptions With(FrontMatter source, string key, string value) => this with { ShowDate = source.Bool(key, value) };

    internal override void Write(Stream output) => TemplateBuilder.Write(output, Brand.Neutral, showClaim: false);
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

    private static readonly string[] FrontMatterKeys = ["layouts.title", "layouts.content", "layouts.titleOnly", "layouts.sectionHeader"];

    internal override IReadOnlyList<string> Keys => FrontMatterKeys;

    internal override PptxLayoutNames? LayoutNames => this.Layouts;

    internal override PptxTemplateOptions With(FrontMatter source, string key, string value)
    {
        var layouts = this.Layouts ?? new PptxLayoutNames();
        return this with
        {
            Layouts = key switch
            {
                "layouts.title" => layouts with { Title = value },
                "layouts.content" => layouts with { Content = value },
                "layouts.titleOnly" => layouts with { TitleOnly = value },
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
}
