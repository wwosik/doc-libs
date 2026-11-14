using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>Options for <see cref="PptxGenerator.Generate"/>.</summary>
public sealed record PptxOptions
{
    /// <summary>The default options: built-in master in the neutral brand, code blocks stay code.</summary>
    public static PptxOptions Default { get; } = new();

    /// <summary>The template and its features. Default: the built-in master in the neutral brand. Front matter in the Markdown overrides it.</summary>
    public PptxTemplateOptions Template { get; init; } = new BrandTemplateOptions();

    /// <summary>
    /// The brands front matter may select by name (<c>template: acme</c>), besides <c>neutral</c> and the brand of
    /// <see cref="Template"/>. Names must be unique. Default: none.
    /// </summary>
    public IReadOnlyList<Brand> Brands { get; init; } = [];

    /// <summary>
    /// Asked in order for every fenced code block; the first graphic wins. A drawing becomes a group of editable
    /// shapes, an image is placed like a Markdown image, and a block no renderer takes stays code. Either gets a
    /// slide of its own, like a table. This is how diagrams such as Mermaid get in. Default: none.
    /// </summary>
    public IReadOnlyList<ICodeBlockRenderer> CodeBlockRenderers { get; init; } = [];

    /// <summary>
    /// Asked in order for every image that is not a PNG, JPEG or GIF, such as an <c>.svg</c> image; the first conversion
    /// wins, and an image no converter takes is an error. Register <c>SvgImageConverter</c> from
    /// wwosik.DocLibs.DocumentConversions.Svg to place SVG images. Default: none.
    /// </summary>
    public IReadOnlyList<IImageConverter> ImageConverters { get; init; } = [];

    /// <summary>
    /// The font of inline code and code blocks. <see langword="null"/> (the default): the brand's mono family, else
    /// <c>Consolas</c>.
    /// </summary>
    public string? CodeFont { get; init; }

    /// <summary>The code font without a brand mono family or <see cref="CodeFont"/>.</summary>
    internal const string DefaultCodeFont = "Consolas";

    /// <summary>
    /// How far an image is enlarged at most to fill the room it has, as a multiple of its natural size (a pixel is
    /// 1/96 inch). Default <c>4</c>, so that small images do not turn into blurs; <c>1</c> never enlarges. An image's own
    /// <c>scale=</c> in its title wins.
    /// </summary>
    public double MaxImageUpscale { get; init; } = 4.0;

    /// <summary>
    /// Text added to the title of a slide that continues the one before because its content did not fit, such as
    /// <c>" (cont.)"</c>. Default <see langword="null"/>: the same title.
    /// </summary>
    public string? ContinuationSuffix { get; init; }

    /// <summary>
    /// Whether text slides write Markdown's bullets on every list level (<see cref="ListBullets.Always"/>), or leave
    /// level 1 to the template's style (<see cref="ListBullets.Template"/>, the default), which some templates draw
    /// without a bullet. Front matter <c>listBullets: always</c> overrides it.
    /// </summary>
    public ListBullets ListBullets { get; init; }

    /// <summary>
    /// How every slide is brought on when the deck is presented, unless the front matter sets <c>transition</c> or a
    /// heading <c>transition="…"</c>. Default <see cref="SlideTransition.None"/>: no transition.
    /// </summary>
    public SlideTransition Transition { get; init; }

    /// <summary>
    /// Which blocks build click by click without a marker of their own: <see cref="DeckBuild.Lists"/> builds every
    /// top-level list item by item, as <c>{.build}</c> before it would. Default <see cref="DeckBuild.None"/>: only
    /// blocks with a marker. Front matter <c>build</c> overrides it.
    /// </summary>
    public DeckBuild Build { get; init; }

    /// <summary>
    /// The document language, a BCP 47 tag such as <c>en</c>, <c>de</c> or <c>de-CH</c>, unless the front matter sets
    /// <c>lang</c>. It picks the texts the generator writes itself (see <see cref="Texts"/>) and tags the text, for
    /// spell checking in PowerPoint. Default <c>en</c>.
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Texts for languages besides English and German, or in place of them, by language tag (<c>pl</c>, <c>de-CH</c>):
    /// a tag is matched in full, then by its language alone. Start each from <see cref="GeneratedTexts.English"/> with
    /// <c>with { … }</c>. Default: none.
    /// </summary>
    public IReadOnlyDictionary<string, GeneratedTexts> Texts { get; init; } = new Dictionary<string, GeneratedTexts>();

    /// <summary>
    /// The icons <c>:icon-name:</c> draws (as a card's figure, as list bullets in an icon list, never inside a slide's text). Default
    /// <see cref="TablerIcons.Outline"/>, the whole Tabler outline set; a set of your own replaces it, and an unknown
    /// name is an error. <see langword="null"/> switches icons off: <c>:icon-name:</c> then stays text and is not checked.
    /// </summary>
    public IIconSet? Icons { get; init; } = TablerIcons.Outline;

    /// <summary>
    /// The clock for the date of generation in the footer, and for today in timelines and Gantt charts that do not pin it
    /// with <c>today=</c>. Default <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// The limits <see cref="PptxGenerator.Check"/> holds the document to, which <see cref="PptxGenerator.AuthoringGuide"/> lists
    /// for the writer; <see cref="PptxGenerator.Generate"/> never applies them. Default <see cref="CheckOptions.Default"/>.
    /// </summary>
    public CheckOptions Checks { get; init; } = CheckOptions.Default;
}

/// <summary>Who decides the bullets of lists on text slides.</summary>
public enum ListBullets
{
    /// <summary>The template's styles: Markdown's bullets only where the template has bullets.</summary>
    Template,

    /// <summary>Markdown's bullets on every level, whatever the template's styles say.</summary>
    Always,
}

/// <summary>Which blocks of a deck build click by click without a build marker of their own.</summary>
public enum DeckBuild
{
    /// <summary>None: only blocks with a marker such as <c>{.build}</c> build.</summary>
    None,

    /// <summary>Every top-level list of a slide builds item by item, unless it has a marker of its own.</summary>
    Lists,
}

/// <summary>
/// How a slide is brought on when the deck is presented, each with PowerPoint's default direction and speed. Front
/// matter <c>transition</c> and a heading's <c>transition="…"</c> name them in lower case.
/// </summary>
public enum SlideTransition
{
    /// <summary>No transition: the slide simply replaces the one before.</summary>
    None,

    /// <summary>The slide fades in.</summary>
    Fade,

    /// <summary>The slide pushes the one before off the screen.</summary>
    Push,

    /// <summary>The slide is wiped in over the one before.</summary>
    Wipe,

    /// <summary>The slide opens from the middle.</summary>
    Split,

    /// <summary>The slide slides in over the one before.</summary>
    Cover,

    /// <summary>The slide before slides away and uncovers it.</summary>
    Uncover,

    /// <summary>The slide zooms in.</summary>
    Zoom,

    /// <summary>A cut: the slide replaces the one before at once, as a transition of its own.</summary>
    Cut,

    /// <summary>
    /// PowerPoint's Morph: shapes that are on both slides move and change from one to the other. Viewers without Morph
    /// show a fade instead.
    /// </summary>
    Morph,
}
