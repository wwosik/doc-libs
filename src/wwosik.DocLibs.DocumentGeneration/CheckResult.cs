using System.Globalization;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The limits a check (<c>PptxGenerator.Check</c>, <c>PdfGenerator.Check</c>, <c>DocxGenerator.Check</c>) holds a
/// document to, and how it estimates the talk time of a deck. A <see langword="null"/> limit switches its rule off.
/// <c>Generate</c> never applies them; the authoring guides list them under <em>Limits</em>.
/// </summary>
public sealed record CheckOptions
{
    /// <summary>The default limits: 6 bullets and 80 words per slide, 60-character titles, 150 words per paragraph, alt text required.</summary>
    public static CheckOptions Default { get; } = new();

    /// <summary>Decks: list items, at every level, per <c>#</c> or <c>##</c> section (<c>too-many-bullets</c>). Default 6.</summary>
    public int? MaxBulletsPerSlide { get; init; } = 6;

    /// <summary>
    /// Decks: words per <c>#</c> or <c>##</c> section, its title, speaker notes, tables, code and drawn containers not
    /// counted (<c>too-many-words</c>). Default 80.
    /// </summary>
    public int? MaxWordsPerSlide { get; init; } = 80;

    /// <summary>
    /// Text elements (what a reader sees as one character) in a slide title (<c>#</c> and <c>##</c>) or a heading of a PDF or
    /// Word document (<c>title-too-long</c>). Default 60.
    /// </summary>
    public int? MaxTitleLength { get; init; } = 60;

    /// <summary>PDFs and Word documents: words per paragraph (<c>long-paragraph</c>). Default 150.</summary>
    public int? MaxWordsPerParagraph { get; init; } = 150;

    /// <summary>Whether every image needs alt text, <c>![What it shows](file.png)</c> (<c>alt-text-missing</c>). Default <see langword="true"/>.</summary>
    public bool RequireAltText { get; init; } = true;

    /// <summary>Decks: the speaking rate the talk time is estimated at. Default 130.</summary>
    public int WordsPerMinute { get; init; } = 130;

    /// <summary>Decks: the least time a content slide takes in the talk time. Default 30 seconds.</summary>
    public TimeSpan MinTimePerSlide { get; init; } = TimeSpan.FromSeconds(30);

    /// <exception cref="ArgumentException">A limit is negative, <see cref="WordsPerMinute"/> is not positive, or <see cref="MinTimePerSlide"/> is negative.</exception>
    internal void Validate()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(this.MaxBulletsPerSlide), this.MaxBulletsPerSlide), (nameof(this.MaxWordsPerSlide), this.MaxWordsPerSlide),
            (nameof(this.MaxTitleLength), this.MaxTitleLength), (nameof(this.MaxWordsPerParagraph), this.MaxWordsPerParagraph),
        })
        {
            if (value < 0)
            {
                throw new ArgumentException($"{nameof(CheckOptions)}.{name} must not be negative.", "options");
            }
        }

        if (this.WordsPerMinute <= 0)
        {
            throw new ArgumentException($"{nameof(CheckOptions)}.{nameof(this.WordsPerMinute)} must be positive.", "options");
        }

        if (this.MinTimePerSlide < TimeSpan.Zero)
        {
            throw new ArgumentException($"{nameof(CheckOptions)}.{nameof(this.MinTimePerSlide)} must not be negative.", "options");
        }
    }
}

/// <summary>
/// What a check found: the notes <c>Generate</c> would return, the notes of the check's own rules
/// (<see cref="NoteCodes.TooManyBullets"/> and the others), and <see cref="Statistics"/>. Nothing was written.
/// </summary>
/// <param name="Notes">All notes, in the order of the Markdown; empty when there is nothing to report.</param>
/// <param name="Statistics">What the document holds, as far as the check got.</param>
public sealed record CheckResult(IReadOnlyList<GenerationNote> Notes, CheckStatistics Statistics)
{
    /// <summary>
    /// The reason of the first <see cref="DocumentConversionException"/>, which <c>Generate</c> would have thrown (such as
    /// a front matter key that does not exist); <see langword="null"/> when the document can be generated. The notes and
    /// statistics are then those found before it.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>Whether the document can be generated and there is nothing to report.</summary>
    public bool IsClean => this.Error is null && this.Notes.Count == 0;

    /// <summary>One <c>- line 12: …</c> per note (or <c>No notes.</c>), then <c>Error: …</c> if any, then a line of statistics.</summary>
    public override string ToString()
    {
        var text = new StringBuilder(new GenerationResult(this.Notes).ToString()).Append('\n');
        if (this.Error is not null)
        {
            text.Append("Error: ").Append(this.Error).Append('\n');
        }

        return text.Append(this.Statistics).ToString();
    }

    /// <inheritdoc/>
    public bool Equals(CheckResult? other) =>
        other is not null && this.Error == other.Error && this.Statistics.Equals(other.Statistics) && this.Notes.SequenceEqual(other.Notes);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Error, this.Statistics, this.Notes.Count);
}

/// <summary>What a checked document holds.</summary>
public sealed record CheckStatistics
{
    /// <summary>The kinds of slide in <see cref="SlidesByKind"/>, in the order they are listed.</summary>
    public static IReadOnlyList<string> SlideKinds { get; } = ["cover", "section", "text", "visual", "layout", "agenda", "quote", "statement", "contact", "chart", "scene", "closing", "backup"];

    /// <summary>The slides a deck would have, or the pages of a PDF; 0 for a Word document, and when the check found an error.</summary>
    public int Pages { get; init; }

    /// <summary>Workbooks: the sheets, the info sheet included; 0 for other formats.</summary>
    public int Sheets { get; init; }

    /// <summary>
    /// The words of all text, tables, headings and footnotes included, speaker notes, code and alt text not: runs of letters
    /// and digits (with <c>'</c> or <c>-</c> inside), a code span one word, and in Chinese, Japanese and Thai each two
    /// characters one word. A deck's backup (<c>{.backup}</c>) is not counted.
    /// </summary>
    public int Words { get; init; }

    /// <summary>The images, each image of a row counted, <c>qr:</c> codes not.</summary>
    public int Images { get; init; }

    /// <summary>The tables.</summary>
    public int Tables { get; init; }

    /// <summary>The code blocks a renderer drew (diagrams, charts) and the drawn containers (<c>::: timeline</c> and the others).</summary>
    public int Diagrams { get; init; }

    /// <summary>
    /// Decks: the slides by kind, split parts included, in the order of <see cref="SlideKinds"/>, kinds without a slide left
    /// out: <c>cover</c>, <c>section</c>, <c>text</c>, <c>visual</c> (a table, image or diagram), <c>layout</c> (a flow
    /// slide or one that fills a layout's placeholders), the slide kinds <c>agenda</c>, <c>quote</c>, <c>statement</c>,
    /// <c>contact</c>, <c>chart</c>, <c>scene</c>, <c>closing</c>, and <c>backup</c> (every slide of the backup, whatever its
    /// kind). Empty for other formats.
    /// </summary>
    public IReadOnlyDictionary<string, int> SlidesByKind { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Decks: the estimated talk time, in whole minutes. A content slide takes its speaker notes' words (else its own,
    /// title excluded) at <see cref="CheckOptions.WordsPerMinute"/>, and at least <see cref="CheckOptions.MinTimePerSlide"/>;
    /// a cover, section or closing slide 10 seconds; backup slides none. <see langword="null"/> for other formats.
    /// </summary>
    public TimeSpan? TalkTime { get; init; }

    /// <summary><c>9 slides, 612 words, 3 images, 2 tables, 1 diagrams; cover 1, text 5; talk time 11 min.</c></summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (this.Sheets > 0)
        {
            parts.Add(Invariant($"{this.Sheets} sheets"));
        }

        if (this.Pages > 0)
        {
            parts.Add(Invariant($"{this.Pages} {(this.SlidesByKind.Count > 0 ? "slides" : "pages")}"));
        }

        parts.Add(Invariant($"{this.Words} words"));
        parts.Add(Invariant($"{this.Images} images"));
        parts.Add(Invariant($"{this.Tables} tables"));
        parts.Add(Invariant($"{this.Diagrams} diagrams"));
        var text = string.Join(", ", parts);
        if (this.SlidesByKind.Count > 0)
        {
            text += "; " + string.Join(", ", this.SlidesByKind.Select(k => Invariant($"{k.Key} {k.Value}")));
        }

        if (this.TalkTime is { } time)
        {
            text += Invariant($"; talk time {(int)time.TotalMinutes} min");
        }

        return text + ".";
    }

    /// <inheritdoc/>
    public bool Equals(CheckStatistics? other) =>
        other is not null && this.Pages == other.Pages && this.Sheets == other.Sheets && this.Words == other.Words && this.Images == other.Images && this.Tables == other.Tables
        && this.Diagrams == other.Diagrams && this.TalkTime == other.TalkTime && this.SlidesByKind.SequenceEqual(other.SlidesByKind);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Pages, this.Words, this.Images, this.Tables, this.Diagrams, this.TalkTime);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
