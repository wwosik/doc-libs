using System.Text;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>What <see cref="DeckSvg.Write"/> reads.</summary>
public sealed record DeckSvgOptions
{
    /// <summary>
    /// TrueType files for families the deck names but does not embed, matched by family name (name ID 1), as in
    /// <see cref="HtmlDeckOptions.Fonts"/>: text in them is measured and painted with these faces.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fonts { get; init; } = [];

    /// <summary>
    /// The slides to write, by their number in the deck (from 1, hidden slides counted); <see langword="null"/> (the
    /// default): every visible slide. Only these slides are read, so a thumbnail of slide 1 costs one slide.
    /// </summary>
    public IReadOnlyCollection<int>? Slides { get; init; }
}

/// <summary>
/// What <see cref="DeckSvg.Write"/> wrote: one SVG per visible slide, the faces they are painted with, and the notes
/// about what could not be drawn.
/// </summary>
/// <param name="Notes">
/// What could not be drawn (<see cref="NoteCodes.DrawingUnsupported"/>) and which fonts were measured by stand-in or
/// estimate (<see cref="NoteCodes.FontMissing"/>), one note per kind with its slides; empty when there is nothing to report.
/// </param>
public sealed record DeckSvgResult(IReadOnlyList<GenerationNote> Notes)
{
    /// <summary>The slides written, as in <see cref="GenerationResult.Pages"/>.</summary>
    public int Pages => this.Slides.Count;

    /// <summary>Whether there is anything to report.</summary>
    public bool HasNotes => this.Notes.Count > 0;

    /// <summary>The visible slides (or those asked for), in the deck's order.</summary>
    public IReadOnlyList<SlideSvg> Slides { get; init; } = [];

    /// <summary>
    /// Every face the SVGs name, once each, as TrueType: the faces embedded in the deck (from their EOT), the fonts
    /// passed in <see cref="DeckSvgOptions.Fonts"/>, and the bundled Liberation stand-ins. Pass them to
    /// <c>DeckPreviewOptions.Fonts</c>, so the slides are painted in the faces their lines were broken with.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Fonts { get; init; } = [];

    /// <summary>Liberation Sans Regular, for the labels of a contact sheet (<c>ContactSheetOptions.LabelFont</c>).</summary>
    public ReadOnlyMemory<byte> LabelFont { get; init; }

    /// <summary>One line per note, <c>- …</c>; <c>No notes.</c> when there are none.</summary>
    public override string ToString() => new GenerationResult(this.Notes).ToString();
}

/// <summary>
/// The slides of any deck as standalone SVGs written for painting, for previews: the same drawing as the HTML deck's
/// (<see cref="HtmlDeck"/>), with every build step shown, pictures as their PNG, JPEG or GIF (never an SVG inside the
/// SVG), and each run in the face its line was measured with. <c>DeckPreview</c> in
/// <c>wwosik.DocLibs.DocumentConversions.Svg</c> paints them as PNGs and a contact sheet.
/// </summary>
public static class DeckSvg
{
    /// <summary>Reads <paramref name="pptx"/> (any presentation, read best effort) and writes its slides as SVG.</summary>
    /// <exception cref="DocumentConversionException">The file is not a presentation, or is damaged.</exception>
    public static DeckSvgResult Write(Stream pptx, DeckSvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pptx);
        options ??= new DeckSvgOptions();
        var (deck, notes) = DeckReader.Read(pptx, options.Fonts, options.Slides is { } only ? new HashSet<int>(only) : null);
        const double EmuPerPoint = 12700;
        var (width, height) = (deck.Width / EmuPerPoint, deck.Height / EmuPerPoint);
        var slides = deck.Slides
            .Select(slide => new SlideSvg(slide.Number, slide.Title, Encoding.UTF8.GetBytes(SceneSvg.Write(deck, slide, SvgTarget.Painting)), width, height))
            .ToList();

        var faces = new List<byte[]>();
        void Add(byte[] file)
        {
            if (!faces.Any(f => ReferenceEquals(f, file)))
            {
                faces.Add(file);
            }
        }

        foreach (var face in deck.Faces)
        {
            Add(face.File);
        }

        // Text measured by estimate is painted in Liberation Sans (SvgTarget.Painting), so its faces travel too.
        foreach (var font in deck.Slides.SelectMany(s => Runs(s.Items)).Select(r => r.Font).Where(f => f.Face is null))
        {
            Add((font.Bold, font.Italic) switch
            {
                (true, true) => StandInFonts.SansBoldItalic,
                (true, false) => StandInFonts.SansBold,
                (false, true) => StandInFonts.SansItalic,
                _ => StandInFonts.SansRegular,
            });
        }

        var result = notes.Result(slides.Count);
        return new DeckSvgResult(result.Notes)
        {
            Slides = slides,
            Fonts = [.. faces.Select(f => (ReadOnlyMemory<byte>)f)],
            LabelFont = StandInFonts.SansRegular,
        };
    }

    private static IEnumerable<SceneRun> Runs(IEnumerable<SceneItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case SceneGroup group:
                    foreach (var run in Runs(group.Items))
                    {
                        yield return run;
                    }

                    break;
                case SceneShape { Text: { } text }:
                    foreach (var run in text.Paragraphs.SelectMany(p => p.Lines).SelectMany(l => l.Runs))
                    {
                        yield return run;
                    }

                    break;
            }
        }
    }
}
