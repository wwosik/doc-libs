namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>
/// What the reader could not draw, collected per kind and family with the slide numbers, so a deck that has a shadow on
/// every slide gives one note, not twenty.
/// </summary>
internal sealed class RenderNotes
{
    private readonly Dictionary<(string Code, string What), SortedSet<int>> seen = [];
    private readonly List<(string Code, string What)> order = [];

    /// <summary>The slide being read; 0 for parts read before the first slide.</summary>
    public int Slide { get; set; }

    /// <summary>Something left out: an element, preset, chart kind, image format or effect, named in a few words.</summary>
    public void Unsupported(string what) => this.Add(NoteCodes.DrawingUnsupported, what);

    /// <summary>A family or face drawn with an estimate or a stand-in, explained in a few words.</summary>
    public void FontMissing(string what) => this.Add(NoteCodes.FontMissing, what);

    public IEnumerable<(string Code, string What)> Kinds => this.order;

    public GenerationResult Result(int pages) => new([.. this.order.Select(key =>
    {
        var slides = this.seen[key].Where(s => s > 0).ToList();
        var where = slides.Count switch
        {
            0 => string.Empty,
            1 => $" (slide {slides[0]})",
            _ => $" (slides {string.Join(", ", slides)})",
        };
        var message = key.Code == NoteCodes.DrawingUnsupported ? $"Not drawn: {key.What}{where}." : $"{key.What}{where}.";
        return new GenerationNote(key.Code, 0, message);
    })]) { Pages = pages };

    private void Add(string code, string what)
    {
        if (!this.seen.TryGetValue((code, what), out var slides))
        {
            this.seen[(code, what)] = slides = [];
            this.order.Add((code, what));
        }

        slides.Add(this.Slide);
    }
}
