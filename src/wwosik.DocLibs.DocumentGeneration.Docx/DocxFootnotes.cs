using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The footnotes part (<c>word/footnotes.xml</c>), made with the separator and the continuation separator when the
/// template has none and the document has a footnote. Footnotes are numbered by Word; their ids follow the template's.
/// </summary>
internal sealed class DocxFootnotes(MainDocumentPart main)
{
    private Footnotes? footnotes;
    private long next;

    /// <summary>The part footnote text is written into, for its hyperlinks and images.</summary>
    public FootnotesPart Part
    {
        get
        {
            this.Root();
            return main.FootnotesPart!;
        }
    }

    /// <summary>Adds a footnote of <paramref name="paragraphs"/> and returns its id for the <c>w:footnoteReference</c>.</summary>
    public long Add(IEnumerable<Paragraph> paragraphs)
    {
        var root = this.Root();
        var id = this.next++;
        root.Append(new Footnote(paragraphs) { Id = id });
        return id;
    }

    /// <summary>Saves the part if footnotes were added.</summary>
    public void Save() => this.footnotes?.Save();

    private Footnotes Root()
    {
        if (this.footnotes is not null)
        {
            return this.footnotes;
        }

        var part = main.FootnotesPart;
        if (part?.Footnotes is null)
        {
            part ??= main.AddNewPart<FootnotesPart>();
            part.Footnotes = new Footnotes(
                Separator(-1, FootnoteEndnoteValues.Separator, new SeparatorMark()),
                Separator(0, FootnoteEndnoteValues.ContinuationSeparator, new ContinuationSeparatorMark()));
            DocxSettings.Set(main, new FootnoteDocumentWideProperties(new FootnoteSpecialReference { Id = -1 }, new FootnoteSpecialReference { Id = 0 }));
        }

        this.footnotes = part.Footnotes;
        this.next = Math.Max(1, this.footnotes.Elements<Footnote>().Select(f => f.Id?.Value ?? 0).DefaultIfEmpty(0).Max() + 1);
        return this.footnotes;
    }

    private static Footnote Separator(long id, FootnoteEndnoteValues type, DocumentFormat.OpenXml.OpenXmlElement mark) =>
        new(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
            new Run(mark)))
        { Type = type, Id = id };
}
