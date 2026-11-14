namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The styles of a Word template that each part of a document takes, by name as Word shows it (<c>Heading 1</c>,
/// <c>Title</c>), compared case-insensitively. Styles are always found by name, never by id, because localized
/// templates have other ids. A role left <see langword="null"/> takes the style of its default name; a template that
/// lacks it gets that style added, based on the template's <c>Normal</c>. A name given here that the template does not
/// have is an error.
/// </summary>
public sealed record DocxStyleNames
{
    /// <summary>The cover title, from the first <c>#</c> heading. Default <c>Title</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Each paragraph after the cover title. Default <c>Subtitle</c>.</summary>
    public string? Subtitle { get; init; }

    /// <summary>The author and date on the cover. Default <c>Cover Details</c>.</summary>
    public string? CoverDetails { get; init; }

    /// <summary>The heading above the table of contents. Default <c>TOC Heading</c>.</summary>
    public string? TocHeading { get; init; }

    /// <summary>Headings of the first level. Default <c>Heading 1</c>.</summary>
    public string? Heading1 { get; init; }

    /// <summary>Headings of the second level. Default <c>Heading 2</c>.</summary>
    public string? Heading2 { get; init; }

    /// <summary>Headings of the third level. Default <c>Heading 3</c>.</summary>
    public string? Heading3 { get; init; }

    /// <summary>Headings of the fourth level. Default <c>Heading 4</c>.</summary>
    public string? Heading4 { get; init; }

    /// <summary>Headings of the fifth level. Default <c>Heading 5</c>.</summary>
    public string? Heading5 { get; init; }

    /// <summary>Headings of the sixth level. Default <c>Heading 6</c>.</summary>
    public string? Heading6 { get; init; }

    /// <summary>Body text. Default <c>Normal</c>.</summary>
    public string? Normal { get; init; }

    /// <summary>Items of bulleted lists. Default <c>List Bullet</c>.</summary>
    public string? ListBullet { get; init; }

    /// <summary>Items of numbered lists. Default <c>List Number</c>.</summary>
    public string? ListNumber { get; init; }

    /// <summary>Items of task lists, with a check box. Default <c>List Task</c>.</summary>
    public string? ListTask { get; init; }

    /// <summary>The term of a definition list. Default <c>Definition Term</c>.</summary>
    public string? DefinitionTerm { get; init; }

    /// <summary>The description of a term in a definition list. Default <c>Definition</c>.</summary>
    public string? Definition { get; init; }

    /// <summary>Quotes and alerts. Default <c>Quote</c>.</summary>
    public string? Quote { get; init; }

    /// <summary>Each line of a code block. Default <c>Code</c>.</summary>
    public string? Code { get; init; }

    /// <summary>Inline code (a character style). Default <c>Code Character</c>.</summary>
    public string? CodeCharacter { get; init; }

    /// <summary>A horizontal rule (<c>---</c>), an empty paragraph with a bottom border. Default <c>Rule</c>.</summary>
    public string? Rule { get; init; }

    /// <summary>The captions of figures and tables. Default <c>caption</c>.</summary>
    public string? Caption { get; init; }

    /// <summary>A paragraph of bold text right before a table. Default <c>Table Title</c>.</summary>
    public string? TableTitle { get; init; }

    /// <summary>Tables (a table style). Default <c>Doc Table</c>.</summary>
    public string? Table { get; init; }

    /// <summary>The text of footnotes. Default <c>footnote text</c>.</summary>
    public string? FootnoteText { get; init; }

    /// <summary>Footnote references (a character style). Default <c>footnote reference</c>.</summary>
    public string? FootnoteReference { get; init; }

    /// <summary>Links (a character style). Default <c>Hyperlink</c>.</summary>
    public string? Hyperlink { get; init; }
}

/// <summary>
/// A brand's own Word template, used like a caller's <c>CustomDocxTemplateOptions</c>: its styles, numbering, theme,
/// settings, headers and footers are kept, its body is replaced. Compared by content.
/// </summary>
/// <param name="Content">The bytes of a <c>.dotx</c> or <c>.docx</c>.</param>
public sealed record DocxBrandTemplate(ReadOnlyMemory<byte> Content)
{
    /// <summary>The template's styles for each part of a document. Default: every style by its default name.</summary>
    public DocxStyleNames Styles { get; init; } = new();

    /// <inheritdoc/>
    public bool Equals(DocxBrandTemplate? other) =>
        other is not null && this.Content.Span.SequenceEqual(other.Content.Span) && Equals(this.Styles, other.Styles);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Content.Length, this.Styles);
}
