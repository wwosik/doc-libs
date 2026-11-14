using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>What the writers of a document share: its part, the styles of the roles, notes, texts and fonts.</summary>
internal sealed record DocxContext(
    MainDocumentPart Main,
    DocxStyles Styles,
    GenerationNotes Notes,
    GeneratedTexts Texts,
    string Language,
    string CodeFont,
    DocxOptions Options)
{
    /// <summary>The typography pass, or <see langword="null"/> when typography is off.</summary>
    public Typography? Typography { get; init; }

    /// <summary>The page of the template's last section.</summary>
    public DocxPage Page { get; init; } = DocxPage.For(DocxPageSize.A4, DocxOrientation.Portrait);

    /// <summary>The Markdown document, for its images.</summary>
    public DocumentConversions.MarkdownDocument? Document { get; init; }

    /// <summary>The lines of front matter cut off the Markdown, so notes give lines of the original.</summary>
    public int LineOffset { get; init; }

    /// <summary>The brand code-block renderers would draw in.</summary>
    public Brand Brand { get; init; } = Brand.Neutral;

    /// <summary>Whether Heading 1–3 carry the built-in numbers, so the table of contents shows them too.</summary>
    public bool HeadingNumbers { get; init; }

    /// <summary>The status labels front matter <c>labels.*</c> names, by their text.</summary>
    public IReadOnlyDictionary<string, LabelCategory> Labels { get; init; } = new Dictionary<string, LabelCategory>();

    /// <summary>The blocks a <c>::: pagebreak</c> comes right before.</summary>
    public IReadOnlySet<Markdig.Syntax.Block> PageBreaks { get; init; } = new HashSet<Markdig.Syntax.Block>();

    /// <summary>The colours of the built-in styles, for the one direct formatting: the shading of highlighted table rows.</summary>
    public StyleDesign? Design { get; init; }

    /// <summary>The classification marking; <see cref="DocumentGeneration.Marking.None"/>: none.</summary>
    public Marking Marking { get; init; } = Marking.None;
}

/// <summary>
/// A heading the table of contents lists: its level (1 for Heading 1), text, its number if it has one ("1.2") and the
/// bookmark its entry links to.
/// </summary>
internal sealed record TocEntry(int Level, string Text)
{
    public string? Number { get; init; }

    public string? Bookmark { get; init; }
}

/// <summary>
/// The body's blocks as Word paragraphs, and the headings among them for the table of contents. The one way the body
/// is written (<see cref="DocxBody"/>); deck syntax in Word (E09-S02) and drawings (E09-S03) extend it.
/// </summary>
internal interface IBodyWriter
{
    (IReadOnlyList<OpenXmlElement> Elements, IReadOnlyList<TocEntry> Headings) Write(IReadOnlyList<Markdig.Syntax.Block> blocks, DocxContext context);
}

/// <summary>
/// Writes the document's body into the template: the cover, the table of contents, then the content from the
/// <see cref="IBodyWriter"/>, before the template's final section properties. The cover and the table of contents
/// each end with a page break.
/// </summary>
internal static class DocxWriter
{
    public static void Write(DocxContent content, DocxContext context, IBodyWriter bodyWriter, string? author, string? date, bool tableOfContents, int depth)
    {
        var document = context.Main.Document ??= new Document(new Body());
        var body = document.Body ??= new Body();

        // The template's body goes; its final section properties (page, headers, footers) stay.
        var section = body.Elements<SectionProperties>().LastOrDefault();
        body.RemoveAllChildren();

        var (elements, headings) = bodyWriter.Write(content.Body, context);
        var blocks = new List<List<OpenXmlElement>>();
        if (content.Cover is { } cover)
        {
            blocks.Add(Cover(cover, author, date, context));
        }
        else if (section?.GetFirstChild<TitlePage>() is { } titlePage)
        {
            // Without a cover the first page is a content page: it gets the headers and footers of the others.
            titlePage.Remove();
        }

        if (tableOfContents)
        {
            blocks.Add(TableOfContents([.. headings.Where(h => h.Level <= depth)], depth, context));
        }

        // Each part before the content ends its page.
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i < blocks.Count - 1 || elements.Count > 0)
            {
                ((Paragraph)blocks[i][^1]).Append(new Run(new Break { Type = BreakValues.Page }));
            }

            body.Append(blocks[i]);
        }

        body.Append(elements);
        if (!body.HasChildren)
        {
            body.Append(Paragraph(context.Styles.Id(DocxRoles.Normal)));
        }

        if (section is not null)
        {
            body.Append(section);
        }

        // Check boxes are Word 2010 markup: declared on the root, ignorable for older readers.
        if (body.Descendants<DocumentFormat.OpenXml.Office2010.Word.SdtContentCheckBox>().Any())
        {
            Declare(document, "w14", "http://schemas.microsoft.com/office/word/2010/wordml");
            Declare(document, "mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            var ignorable = document.MCAttributes?.Ignorable?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (!ignorable.Contains("w14"))
            {
                document.MCAttributes = new MarkupCompatibilityAttributes { Ignorable = string.Join(" ", [.. ignorable, "w14"]) };
            }
        }

        document.Save();
    }

    private static void Declare(OpenXmlElement root, string prefix, string uri)
    {
        if (root.LookupNamespace(prefix) is null)
        {
            root.AddNamespaceDeclaration(prefix, uri);
        }
    }

    /// <summary>A paragraph in the style <paramref name="style"/> with <paramref name="pieces"/>, hard breaks as <c>w:br</c>.</summary>
    public static Paragraph Paragraph(string style, IEnumerable<TextPiece>? pieces = null)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        foreach (var piece in pieces ?? [])
        {
            paragraph.Append(piece.IsBreak ? new Run(new Break()) : Run(piece.Text));
        }

        return paragraph;
    }

    /// <summary>A run of plain text, spaces kept.</summary>
    public static Run Run(string text) => new(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

    // The marking, if any, the title, each subtitle paragraph, then the author and the date. Under a marking the title
    // starts right away: the marking's style has taken the space above the title, so the title stays where it was.
    private static List<OpenXmlElement> Cover(DocxCover cover, string? author, string? date, DocxContext context)
    {
        var styles = context.Styles;
        var elements = new List<OpenXmlElement>();
        var typography = context.Typography;
        IReadOnlyList<TextPiece> Typeset(IReadOnlyList<TextPiece> pieces) => typography is null
            ? pieces
            : typography.Apply(pieces, p => p.Text, _ => false, (_, text) => new TextPiece(text), 0);
        var title = Paragraph(styles.Id(DocxRoles.Title), Typeset([new TextPiece(cover.Title)]));
        if (DocxMarking.Paragraph(context, DocxRoles.ClassificationCover) is { } marking)
        {
            elements.Add(marking);
            title.ParagraphProperties!.SpacingBetweenLines = new SpacingBetweenLines { Before = "0" };
        }

        elements.Add(title);
        elements.AddRange(cover.Subtitle.Select(s => Paragraph(styles.Id(DocxRoles.Subtitle), Typeset(s))));
        var details = new[] { author, date }.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => new TextPiece(d!)).ToList();
        if (details.Count > 0)
        {
            elements.Add(Paragraph(styles.Id(DocxRoles.CoverDetails), details.Count == 2 ? [details[0], TextPiece.Break, details[1]] : details));
        }

        return elements;
    }

    // The heading, then a TOC field whose result is one entry per heading, with its number but without page numbers, a
    // link to the heading's bookmark: a viewer that does not update fields still shows the headings, and Word fills in
    // the page numbers when it updates the field on opening.
    private static List<OpenXmlElement> TableOfContents(IReadOnlyList<TocEntry> entries, int depth, DocxContext context)
    {
        var styles = context.Styles;
        var elements = new List<OpenXmlElement> { Paragraph(styles.Id(DocxRoles.TocHeading), [new TextPiece(context.Texts.Contents)]) };
        var instruction = string.Create(CultureInfo.InvariantCulture, $" TOC \\o \"1-{depth}\" \\h \\z \\u ");
        var begin = new OpenXmlElement[]
        {
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(instruction) { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
        };
        var end = new Run(new FieldChar { FieldCharType = FieldCharValues.End });
        if (entries.Count == 0)
        {
            var paragraph = Paragraph(styles.Id(DocxRoles.TocLevels[0]));
            paragraph.Append(begin);
            paragraph.Append(end);
            elements.Add(paragraph);
            return elements;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var paragraph = Paragraph(styles.Id(DocxRoles.TocLevels[Math.Clamp(entry.Level, 1, 3) - 1]));
            if (i == 0)
            {
                paragraph.Append(begin);
            }

            var text = Run(entry.Number is null ? entry.Text : entry.Number + " " + entry.Text);
            paragraph.Append(entry.Bookmark is null ? text : new Hyperlink(text) { Anchor = entry.Bookmark, History = true });
            if (i == entries.Count - 1)
            {
                paragraph.Append(end);
            }

            elements.Add(paragraph);
        }

        return elements;
    }
}
