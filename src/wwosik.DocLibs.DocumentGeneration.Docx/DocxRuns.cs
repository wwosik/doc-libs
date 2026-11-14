using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.Footnotes;
using Footnote = Markdig.Extensions.Footnotes.Footnote;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// What Word sets on a run: Ctrl+B, Ctrl+I and strike-through; inline code, marks, tags and the title of an alert through
/// their character styles.
/// </summary>
[Flags]
internal enum RunFlags
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strike = 4,
    Code = 8,

    /// <summary><c>==marked==</c>: the <c>Mark</c> style.</summary>
    Mark = 16,

    /// <summary>A code span shown as a chip: the <c>Tag</c> style.</summary>
    Tag = 32,

    /// <summary>The title of an alert: Word's <c>Strong</c>.</summary>
    Strong = 64,
}

/// <summary>
/// A run of a paragraph: text in <see cref="Flags"/>, linked to <see cref="Link"/> (an absolute address, or
/// <c>#bookmark</c> inside the document), a hard line break, or a footnote reference.
/// </summary>
internal sealed record DocxRun(string Text, RunFlags Flags, string? Link)
{
    public static DocxRun Break { get; } = new("\n", RunFlags.None, null) { IsBreak = true };

    public bool IsBreak { get; init; }

    /// <summary>The footnote this run refers to; its text is empty.</summary>
    public Footnote? Footnote { get; init; }

    /// <summary>The category of coloured words (<c>[words]{.success}</c>), or of a status label with <see cref="IsLabel"/>.</summary>
    public LabelCategory? Category { get; init; }

    /// <summary>Whether the run is a status label, a table cell's code span that front matter <c>labels.*</c> names.</summary>
    public bool IsLabel { get; init; }

    /// <summary>Whether the run is set in bold, by Ctrl+B or by its style, for measuring.</summary>
    public bool LooksBold => this.Flags.HasFlag(RunFlags.Bold) || this.Flags.HasFlag(RunFlags.Mark) || this.Flags.HasFlag(RunFlags.Strong) || this.Category is not null;

    /// <summary>An icon (<c>:icon-name:</c>) the run stands for; its text is empty.</summary>
    public InlineIcon? Icon { get; init; }

    public bool IsBlank => this.Footnote is null && this.Icon is null && (this.IsBreak || this.Text.Trim().Length == 0);
}

/// <summary>
/// Turns Markdown inlines into runs, and runs into Word: emphasis as <c>w:b</c>, <c>w:i</c>, <c>w:strike</c>; marks
/// (<c>==…==</c>), coloured words, status labels, tags and inline code in their character styles; links as <c>w:hyperlink</c> (an
/// external relationship, or an anchor to a heading's bookmark), footnote references as <c>w:footnoteReference</c>.
/// A paragraph is cut at its images, which the body places as paragraphs of their own.
/// </summary>
internal sealed class DocxRuns(DocxBody body)
{
    private readonly Dictionary<(OpenXmlPart, string), string> relationships = [];

    /// <summary>The runs of <paramref name="inline"/> and its images, in order: <see cref="List{DocxRun}"/> or <see cref="LinkInline"/>.</summary>
    public List<object> Pieces(ContainerInline? inline, bool imagesAsText = false, RunFlags flags = RunFlags.None)
    {
        var collector = new Collector(body, imagesAsText);
        collector.Add(inline, flags, null);
        var line = inline is null ? 0 : body.LineOf(inline);
        return [.. collector.Pieces.Select(p => p is List<DocxRun> runs ? this.Icons(Colour(runs), line) : p)];
    }

    /// <summary>The runs of <paramref name="inline"/>, images as their alt text, trimmed.</summary>
    public List<DocxRun> Runs(ContainerInline? inline, RunFlags flags = RunFlags.None) =>
        Trim([.. this.Pieces(inline, imagesAsText: true, flags).OfType<List<DocxRun>>().SelectMany(r => r)]);

    /// <summary>Runs without blank runs at either end, and without the spaces at the ends.</summary>
    public static List<DocxRun> Trim(List<DocxRun> runs)
    {
        var result = runs.SkipWhile(r => r.IsBlank).ToList();
        while (result.Count > 0 && result[^1].IsBlank)
        {
            result.RemoveAt(result.Count - 1);
        }

        if (result.Count > 0 && result[0] is { IsBreak: false, Footnote: null })
        {
            result[0] = result[0] with { Text = result[0].Text.TrimStart() };
        }

        if (result.Count > 0 && result[^1] is { IsBreak: false, Footnote: null })
        {
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        }

        return result;
    }

    /// <summary>The plain text of runs, breaks as spaces.</summary>
    public static string Text(IEnumerable<DocxRun> runs) => string.Concat(runs.Select(r => r.IsBreak ? " " : r.Text));

    /// <summary>The Word elements of <paramref name="runs"/> in <paramref name="part"/>: runs, and hyperlinks around linked runs.</summary>
    public List<OpenXmlElement> Elements(IReadOnlyList<DocxRun> runs, OpenXmlPart part)
    {
        var elements = new List<OpenXmlElement>();
        for (var i = 0; i < runs.Count;)
        {
            var link = runs[i].Link;
            var group = new List<OpenXmlElement>();
            for (; i < runs.Count && runs[i].Link == link; i++)
            {
                if (this.Run(runs[i], link is not null, part) is { } run)
                {
                    group.Add(run);
                }
            }

            if (link is null)
            {
                elements.AddRange(group);
            }
            else if (link.StartsWith('#'))
            {
                elements.Add(new Hyperlink(group) { Anchor = link[1..], History = true });
            }
            else
            {
                elements.Add(new Hyperlink(group) { Id = this.Relationship(part, link), History = true });
            }
        }

        return elements;
    }

    // A run of text, a break, a footnote reference or an icon's picture; null for an icon that is not drawn.
    private OpenXmlElement? Run(DocxRun run, bool linked, OpenXmlPart part)
    {
        if (run.IsBreak)
        {
            return new Run(new Break());
        }

        if (run.Icon is { } icon)
        {
            return body.IconRun(icon, part);
        }

        if (run.Footnote is { } footnote)
        {
            return new Run(
                new RunProperties(new RunStyle { Val = body.Context.Styles.Id(DocxRoles.FootnoteReference) }),
                new FootnoteReference { Id = body.FootnoteId(footnote) });
        }

        var properties = new RunProperties();
        if (Style(run, linked) is { } style)
        {
            properties.Append(new RunStyle { Val = body.Context.Styles.Id(style) });
        }

        if (run.Flags.HasFlag(RunFlags.Bold))
        {
            properties.Append(new Bold());
        }

        if (run.Flags.HasFlag(RunFlags.Italic))
        {
            properties.Append(new Italic());
        }

        if (run.Flags.HasFlag(RunFlags.Strike))
        {
            properties.Append(new Strike());
        }

        var result = new Run();
        if (properties.HasChildren)
        {
            result.Append(properties);
        }

        result.Append(new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
        return result;
    }

    // One character style per run: what the run is matters more than where it links.
    private static StyleRole? Style(DocxRun run, bool linked) =>
        run.IsLabel && run.Category is { } label ? DocxRoles.Labels[label]
        : run.Flags.HasFlag(RunFlags.Tag) ? DocxRoles.Tag
        : run.Flags.HasFlag(RunFlags.Code) ? DocxRoles.CodeCharacter
        : run.Flags.HasFlag(RunFlags.Strong) ? DocxRoles.Strong
        : run.Flags.HasFlag(RunFlags.Mark) ? DocxRoles.Mark
        : run.Category is { } colour ? DocxRoles.Colours[colour]
        : linked ? DocxRoles.Hyperlink
        : null;

    private string Relationship(OpenXmlPart part, string url)
    {
        if (!this.relationships.TryGetValue((part, url), out var id))
        {
            id = part.AddHyperlinkRelationship(new Uri(url, UriKind.Absolute), true).Id;
            this.relationships[(part, url)] = id;
        }

        return id;
    }

    // `[words]{.success}`: the words in the category's character style.
    private static List<DocxRun> Colour(List<DocxRun> runs) => ColouredWords.Apply(
        runs,
        r => r.Footnote is null ? r.Text : string.Empty,
        r => r.Flags.HasFlag(RunFlags.Code) || r.IsBreak,
        (r, text) => r with { Text = text },
        (r, category) => r with { Category = category },
        Format.Docx);

    // `:icon-name:`: a run of its own that stands for the icon, outside code; an unknown name throws. Then typography,
    // when it is on.
    private List<DocxRun> Icons(List<DocxRun> runs, int line) => this.Typeset(
        IconSyntax.Apply(
            runs,
            r => r.Footnote is null ? r.Text : string.Empty,
            r => r.Flags.HasFlag(RunFlags.Code) || r.IsBreak,
            (r, text) => r with { Text = text },
            (r, icon) => r with { Icon = icon },
            body.Context.Options.Icons,
            line,
            Format.Docx),
        line);

    // Typography, when it is on: code, tags, status labels and footnote references are left alone.
    private List<DocxRun> Typeset(List<DocxRun> runs, int line) => body.Context.Typography is not { } typography ? runs : typography.Apply(
        runs,
        r => r.Footnote is null ? r.Text : string.Empty,
        r => r.Flags.HasFlag(RunFlags.Code) || r.Flags.HasFlag(RunFlags.Tag) || r.IsLabel || r.Footnote is not null,
        (r, text) => r with { Text = text },
        line);

    /// <summary>Collects runs, cut into pieces at images unless images count as their alt text.</summary>
    private sealed class Collector(DocxBody body, bool imagesAsText)
    {
        private List<DocxRun> current = [];

        public List<object> Pieces { get; } = [];

        public void Add(ContainerInline? container, RunFlags flags, string? link)
        {
            if (this.Pieces.Count == 0)
            {
                this.Pieces.Add(this.current);
            }

            if (container is null)
            {
                return;
            }

            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        this.Text(literal.Content.ToString(), flags, link);
                        break;
                    case CodeInline code:
                        this.Text(code.Content, flags | RunFlags.Code, link);
                        break;
                    case EmphasisInline emphasis:
                        var added = emphasis.DelimiterChar == '~' ? RunFlags.Strike
                            : emphasis.DelimiterChar == '=' ? RunFlags.Mark
                            : emphasis.DelimiterCount >= 2 ? RunFlags.Bold
                            : RunFlags.Italic;
                        this.Add(emphasis, flags | added, link);
                        break;
                    case LinkInline { IsImage: true } image when imagesAsText:
                        body.ImageAsText(image);
                        this.Text(DocxBody.AltText(image), flags, link);
                        break;
                    case LinkInline { IsImage: true } image:
                        this.Pieces.Add(image);
                        this.current = [];
                        this.Pieces.Add(this.current);
                        break;
                    case LinkInline hyperlink:
                        this.Add(hyperlink, flags, body.LinkTarget(hyperlink.GetDynamicUrl?.Invoke() ?? hyperlink.Url) ?? link);
                        break;
                    case AutolinkInline autolink:
                        this.Text(autolink.Url, flags, body.LinkTarget(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url) ?? link);
                        break;
                    case LineBreakInline { IsHard: true }:
                        this.current.Add(DocxRun.Break);
                        break;
                    case LineBreakInline:
                        this.Text(" ", flags, link);
                        break;
                    case HtmlEntityInline entity:
                        this.Text(entity.Transcoded.ToString(), flags, link);
                        break;
                    case HtmlInline html:
                        body.HtmlAsText(html, html.Tag);
                        this.Text(html.Tag, flags, link);
                        break;
                    case FootnoteLink { IsBackLink: false } footnote:
                        this.current.Add(new DocxRun(string.Empty, flags, null) { Footnote = footnote.Footnote });
                        break;
                    case FootnoteLink:
                    case TaskList:
                        break;
                    case ContainerInline nested:
                        this.Add(nested, flags, link);
                        break;
                }
            }
        }

        private void Text(string text, RunFlags flags, string? link)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (this.current.Count > 0 && this.current[^1] is { IsBreak: false, Footnote: null } last && last.Flags == flags && last.Link == link)
            {
                this.current[^1] = last with { Text = last.Text + text };
            }
            else
            {
                this.current.Add(new DocxRun(text, flags, link));
            }
        }
    }
}
