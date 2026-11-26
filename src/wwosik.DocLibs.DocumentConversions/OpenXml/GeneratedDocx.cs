using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentConversions.OpenXml;

/// <summary>
/// Reads a Word document that <c>DocxGenerator</c> wrote back into the Markdown it came from (E08-S02). Such a document
/// carries a marker in an ignorable namespace of its own in <c>settings.xml</c> (<see cref="Namespace"/>), or, once
/// Word has saved it and dropped that, still has the generator's styles. Blocks are read by the role of their style
/// (headings, lists, alerts, code, captions, image rows, definitions, rules), runs by their character styles; what Word
/// cannot express comes from the namespace while it is there: the Markdown of containers and drawn blocks, the language
/// of code, the titles and attributes of images. Without it the converter falls back to what Word shows: a fence
/// without a language, a <c>chart</c> block from a chart's cached values, a picture, a container's content.
/// </summary>
internal sealed partial class GeneratedDocx
{
    /// <summary>The generator's namespace (<c>DocxRoundTrip.Namespace</c> there).</summary>
    public const string Namespace = "urn:wwosik:doclibs:wordprocessingml";

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace Asvg = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
    private static readonly XNamespace CoreNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DL = Namespace;

    private const string DocumentPart = "word/document.xml";
    private const string FootnotesPart = "word/footnotes.xml";
    private const string IconTitle = "Icon: ";
    private const string CoverImageName = "Cover Image ";

    // The generator's style ids of the roles this reader tells apart, as it names them by default.
    private const string Normal = "Normal";
    private const string Title = "Title";
    private const string Subtitle = "Subtitle";
    private const string CoverDetails = "CoverDetails";
    private const string TocHeading = "TOCHeading";
    private const string ListBullet = "ListBullet";
    private const string ListNumber = "ListNumber";
    private const string ListTask = "ListTask";
    private const string DefinitionTerm = "DefinitionTerm";
    private const string Definition = "Definition";
    private const string Quote = "Quote";
    private const string Code = "Code";
    private const string CodeCharacter = "CodeCharacter";
    private const string Rule = "Rule";
    private const string Caption = "Caption";
    private const string TableTitle = "TableTitle";
    private const string FootnoteReference = "FootnoteReference";
    private const string Strong = "Strong";
    private const string Mark = "Mark";
    private const string ContainerHeading = "ContainerHeading";
    private const string Tag = "Tag";
    private const string ImageRow = "ImageRow";
    private const string ClassificationCover = "ClassificationCover";

    private static readonly string[] Headings = ["Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6"];
    private static readonly string[] TocLevels = ["TOC1", "TOC2", "TOC3"];
    private static readonly string[] Alerts = ["Note", "Tip", "Important", "Warning", "Caution"];
    private static readonly string[] Categories = ["Success", "Info", "Warning", "Danger", "Neutral"];

    // Every role's default style id, which a document without the marker is read by.
    private static readonly HashSet<string> Known = new(
        [Normal, Title, Subtitle, CoverDetails, TocHeading, .. Headings, .. TocLevels, ListBullet, ListNumber, ListTask, DefinitionTerm,
         Definition, Quote, Code, CodeCharacter, Rule, Caption, TableTitle, "DocTable", "FootnoteText", FootnoteReference, "Hyperlink",
         .. Alerts, Strong, Mark, .. Categories.Select(c => "Label" + c), .. Categories.Select(c => "Colour" + c), ContainerHeading,
         "Statement", "Panel", "PanelDark", Tag, ImageRow, "Classification", ClassificationCover],
        StringComparer.OrdinalIgnoreCase);

    private readonly OpenXmlPackage package;
    private readonly ResourceCollector? images;
    private readonly Dictionary<string, string> roles;
    private readonly IReadOnlyDictionary<string, string> styleNames;
    private readonly Dictionary<(string NumId, int Level), int> starts = [];
    private readonly Dictionary<string, XElement> footnotes = new(StringComparer.Ordinal);
    private readonly List<string> footnoteOrder = [];
    private readonly Dictionary<string, List<string>> labels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> anchors = new(StringComparer.Ordinal);
    private readonly int shift;
    private readonly HashSet<string> letteredNumbers = new(StringComparer.Ordinal);
    private (string? Path, string Size, string Position)? coverImage;
    private bool appendixSeen;
    private bool justified;

    private GeneratedDocx(OpenXmlPackage package, XElement? marker, XDocument? styles, ResourceCollector? images)
    {
        this.package = package;
        this.images = images;
        this.Marker = marker;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in styles?.Root?.Elements(W + "style") ?? [])
        {
            if (style.Attribute(W + "styleId")?.Value is { } id)
            {
                names[id] = style.Element(W + "name")?.Attribute(W + "val")?.Value ?? id;
            }
        }

        this.styleNames = names;
        this.Styles = styles;

        // Actual style id → the role's default id: from the marker, else every style under a role's default id.
        this.roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (marker is not null)
        {
            foreach (var style in marker.Elements(DL + "style"))
            {
                if (style.Attribute(DL + "id")?.Value is { } id && style.Attribute(DL + "role")?.Value is { } role)
                {
                    this.roles.TryAdd(id, role);
                }
            }
        }
        else
        {
            foreach (var id in names.Keys.Where(Known.Contains))
            {
                this.roles[id] = id;
            }
        }

        this.shift = int.TryParse(marker?.Attribute(DL + "headingShift")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : -1;
    }

    private XElement? Marker { get; }

    private XDocument? Styles { get; }

    /// <summary>
    /// The reader of a generated document, or <see langword="null"/> for any other: one with the marker, or with the
    /// styles the generator adds to every document it writes (<c>Cover Details</c> and <c>Image Row</c>).
    /// </summary>
    public static GeneratedDocx? Of(OpenXmlPackage package, ConversionOptions options)
    {
        var marker = package.ReadXml("word/settings.xml")?.Root?.Element(DL + "generated");
        if (marker is not null && marker.Attribute(DL + "version")?.Value != "1")
        {
            marker = null;
        }

        var styles = package.ReadXml("word/styles.xml");
        if (marker is null)
        {
            var ids = styles?.Root?.Elements(W + "style").Select(s => s.Attribute(W + "styleId")?.Value).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (!ids.Contains(CoverDetails) || !ids.Contains(ImageRow))
            {
                return null;
            }
        }

        return new GeneratedDocx(package, marker, styles, options.IncludeImages ? new ResourceCollector(options.ResourcePathPrefix) : null);
    }

    /// <summary>The Markdown of the document and the images it references.</summary>
    public (string Markdown, IReadOnlyList<DocumentResource> Resources) Read()
    {
        var body = this.package.ReadXml(DocumentPart)?.Root?.Element(W + "body")
            ?? throw new DocumentConversionException(DocumentFormat.Docx, "word/document.xml has no body.");
        this.LoadNumbering();
        this.LoadFootnotes();
        var elements = body.Elements().Where(e => e.Name == W + "p" || e.Name == W + "tbl").ToList();
        var main = new Part(DocumentPart, this.package.Relationships(DocumentPart), Normal);

        foreach (var start in elements.SelectMany(e => e.Descendants(W + "bookmarkStart")))
        {
            if (start.Attribute(W + "name")?.Value is { } name)
            {
                this.anchors[name] = start.Attribute(DL + "id")?.Value ?? name;
            }
        }

        // The cover: the marking above the title, the title, the subtitle paragraphs and the author and date.
        var i = 0;
        string? coverTitle = null;
        var subtitles = new List<string>();
        var details = new List<string>();
        while (i < elements.Count && this.Role(elements[i]) == ClassificationCover)
        {
            i++;
        }

        if (i < elements.Count && this.Role(elements[i]) == Title)
        {
            this.CoverImageIn(elements[i], main);
            coverTitle = this.Inline(elements[i], main).Replace("\\\n", " ", StringComparison.Ordinal).Trim();
            i++;
            while (i < elements.Count && this.Role(elements[i]) == Subtitle)
            {
                // The cover image below the subtitle has a paragraph of the subtitle's style.
                if (!this.CoverImageIn(elements[i], main))
                {
                    subtitles.Add(this.Inline(elements[i], main));
                }

                i++;
            }

            if (i < elements.Count && this.Role(elements[i]) == CoverDetails)
            {
                details.AddRange(PlainLines(elements[i]));
                i++;
            }
        }

        // The table of contents: its heading and its entries, read back as `toc` and `tocDepth`.
        int? tocDepth = null;
        if (i < elements.Count && this.Role(elements[i]) == TocHeading)
        {
            tocDepth = 2;
            i++;
            while (i < elements.Count && TocLevels.Contains(this.Role(elements[i])))
            {
                if (string.Concat(elements[i].Descendants(W + "instrText").Select(t => t.Value)) is var instruction
                    && TocDepthPattern().Match(instruction) is { Success: true } depth)
                {
                    tocDepth = depth.Groups[1].Value[0] - '0';
                }

                i++;
            }
        }

        var shift = this.shift >= 0 ? this.shift : this.GuessShift(elements, i, coverTitle is not null);
        var blocks = this.Blocks(elements.GetRange(i, elements.Count - i), main, shift);

        var text = new StringBuilder();
        text.Append(this.FrontMatter(coverTitle, subtitles, details, tocDepth, elements));
        if (coverTitle is not null)
        {
            text.Append("# ").Append(EscapeHeading(coverTitle)).Append("\n\n");
            foreach (var subtitle in subtitles.Skip(this.SubtitleInFrontMatter(subtitles) ? 1 : 0))
            {
                text.Append(EscapeLines(subtitle)).Append("\n\n");
            }
        }

        foreach (var block in blocks)
        {
            text.Append(block).Append("\n\n");
        }

        // Footnotes: one definition per reference, in the order of the references, at the end.
        var footnotesPart = new Part(FootnotesPart, this.package.Relationships(FootnotesPart), "FootnoteText");
        for (var n = 0; n < this.footnoteOrder.Count; n++)
        {
            if (!this.footnotes.TryGetValue(this.footnoteOrder[n], out var footnote))
            {
                continue;
            }

            var content = this.Blocks([.. footnote.Elements().Where(e => e.Name == W + "p" || e.Name == W + "tbl")], footnotesPart, 0, footnote: true);
            var definition = string.Join("\n\n", content);
            text.Append(CultureInfo.InvariantCulture, $"[^{n + 1}]: ").Append(Indent(definition, "    ", first: false)).Append("\n\n");
        }

        var markdown = text.ToString().TrimEnd('\n');
        return (markdown.Length == 0 ? string.Empty : markdown + "\n", this.images?.Resources ?? []);
    }

    // ---- Front matter ----

    private bool SubtitleInFrontMatter(List<string> subtitles) =>
        subtitles.Count == 1 && !subtitles[0].Contains('\n', StringComparison.Ordinal) && subtitles[0].Trim().Length > 0;

    // The cover image of a cover paragraph, if it holds one, kept for the front matter.
    private bool CoverImageIn(XElement paragraph, Part part)
    {
        var drawing = paragraph.Descendants(W + "drawing").FirstOrDefault(IsCoverImage);
        if (drawing is null)
        {
            return false;
        }

        var properties = drawing.Descendants(WP + "docPr").First();
        // Named after its resource, whose file name the generator gives as its description.
        var picture = this.PictureOf(drawing, part, properties.Attribute("descr")?.Value);
        this.coverImage = (
            properties.Attribute(DL + "source")?.Value == "brand" ? null : picture?.Path,
            properties.Attribute(DL + "size")?.Value ?? "medium",
            properties.Attribute(DL + "position")?.Value ?? (drawing.Element(WP + "anchor") is null ? "below" : "right"));
        return true;
    }

    private static bool IsCoverImage(XElement drawing) =>
        drawing.Descendants(WP + "docPr").FirstOrDefault()?.Attribute("name")?.Value.StartsWith(CoverImageName, StringComparison.Ordinal) == true;

    private string FrontMatter(string? coverTitle, List<string> subtitles, List<string> details, int? tocDepth, List<XElement> elements)
    {
        var entries = new List<(string Key, string Value)>();
        var core = this.package.ReadXml("docProps/core.xml")?.Root;
        var title = core?.Element(DublinCore + "title")?.Value.Trim();
        var author = core?.Element(DublinCore + "creator")?.Value.Trim();
        var language = core?.Element(DublinCore + "language")?.Value.Trim();
        var status = core?.Element(CoreNs + "contentStatus")?.Value.Trim();
        if (!string.IsNullOrEmpty(title))
        {
            entries.Add(("title", title));
        }

        if (this.SubtitleInFrontMatter(subtitles))
        {
            // The subtitle's Markdown, unescaped: front matter is plain text.
            entries.Add(("subtitle", string.Concat(PlainLines(elements.First(e => this.Role(e) == Subtitle)))));
        }

        if (!string.IsNullOrEmpty(author))
        {
            entries.Add(("author", author));
        }

        // The subject is the subtitle's, which the generator writes itself.
        if (core?.Element(DublinCore + "description")?.Value.Trim() is { Length: > 0 } description)
        {
            entries.Add(("description", description));
        }

        if (core?.Element(CoreNs + "keywords")?.Value.Trim() is { Length: > 0 } keywords)
        {
            entries.Add(("keywords", keywords));
        }

        if (this.coverImage is var (path, size, position))
        {
            if (path is not null)
            {
                entries.Add(("coverImage", path));
            }

            if (size != "medium")
            {
                entries.Add(("coverImageSize", size));
            }

            if (position != "below")
            {
                entries.Add(("coverImagePosition", position));
            }
        }

        // The cover's details are the author, then the date; a single line other than the author is the date.
        var date = details.Count >= 2 ? details[1] : details.Count == 1 && details[0] != author ? details[0] : null;
        if (coverTitle is not null && !string.IsNullOrWhiteSpace(date))
        {
            entries.Add(("date", date.Trim()));
        }

        if (!string.IsNullOrEmpty(language))
        {
            entries.Add(("lang", language));
        }

        if (this.Marker?.Attribute(DL + "audience")?.Value is { } audience and not "customer")
        {
            entries.Add(("audience", audience));
        }

        var (classification, draft) = this.Marking(status);
        if (classification is not null)
        {
            entries.Add(("classification", classification));
        }

        if (draft)
        {
            entries.Add(("draft", "true"));
        }

        entries.Add(("toc", tocDepth is null ? "false" : "true"));
        if (tocDepth is { } depth)
        {
            entries.Add(("tocDepth", depth.ToString(CultureInfo.InvariantCulture)));
        }

        // Heading numbers are on by default: front matter turned them off when Heading 1 has no numbering.
        if (elements.Any(e => this.Role(e) is { } role && Headings.Contains(role)) && !this.HeadingsNumbered())
        {
            entries.Add(("headingNumbers", "false"));
        }

        if (this.justified)
        {
            entries.Add(("justify", "true"));
        }

        foreach (var category in Categories)
        {
            if (this.labels.TryGetValue(category, out var texts))
            {
                entries.Add(("labels." + category.ToLowerInvariant(), string.Join("; ", texts)));
            }
        }

        var text = new StringBuilder("---\n");
        foreach (var (key, value) in entries)
        {
            text.Append(key).Append(": ").Append(FrontMatterValue(value)).Append('\n');
        }

        return text.Append("---\n\n").ToString();
    }

    // A value Unquote would take apart, or one with a line break, is quoted so that it stays as it is.
    private static string FrontMatterValue(string value)
    {
        value = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0]
            ? (value[0] == '"' ? "'" + value + "'" : "\"" + value + "\"")
            : value;
    }

    // The classification and draft from the marker, else from the content status, in English or German.
    private (string? Classification, bool Draft) Marking(string? status)
    {
        if (this.Marker?.Attribute(DL + "classification")?.Value is { } kept)
        {
            return (kept == "public" ? null : kept, this.Marker.Attribute(DL + "draft")?.Value == "true");
        }

        string? classification = null;
        var draft = false;
        foreach (var part in (status ?? string.Empty).Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "internal" or "intern":
                    classification = "internal";
                    break;
                case "confidential" or "vertraulich":
                    classification = "confidential";
                    break;
                case "draft" or "entwurf":
                    draft = true;
                    break;
            }
        }

        return (classification, draft);
    }

    private bool HeadingsNumbered()
    {
        var id = this.roles.FirstOrDefault(r => r.Value == Headings[0]).Key ?? Headings[0];
        var style = this.Styles?.Root?.Elements(W + "style").FirstOrDefault(s => string.Equals(s.Attribute(W + "styleId")?.Value, id, StringComparison.OrdinalIgnoreCase));
        return style?.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value is { } numId && numId != "0";
    }

    // Without the marker: the generator makes the highest level Heading 1, and every later `#` starts a page. A Heading 1
    // that does not, or one that opens a document without a cover, was a `##`.
    private int GuessShift(List<XElement> elements, int start, bool cover)
    {
        var first = true;
        for (var i = start; i < elements.Count; i++)
        {
            var element = elements[i];
            if (this.Role(element) == Headings[0])
            {
                if (first ? !cover : PageBreakBefore(element) is false)
                {
                    return 1;
                }
            }

            first = false;
        }

        return 0;
    }

    // ---- Blocks ----

    private sealed record Part(string Name, IReadOnlyDictionary<string, string> Rels, string Plain);

    private List<string> Blocks(List<XElement> elements, Part part, int shift, bool footnote = false, bool inner = false)
    {
        var blocks = new List<string>();
        string? tableTitle = null;
        string? tableCaption = null;
        Picture? lastPicture = null;
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if (!(inner && i == 0) && element.Attribute(DL + "before")?.Value is { } before)
            {
                // A heading that Word does not show, such as `## {.quote}`.
                blocks.Add(before);
            }

            if (element.Attribute(DL + "skip") is not null)
            {
                continue;
            }

            var picture = lastPicture;
            lastPicture = null;
            var role = this.Role(element);
            var pageBreak = PageBreakBefore(element);

            // Inside an alert or a quote its own paragraphs are plain ones.
            var special = role != part.Plain;
            void Add(string block, bool breakBefore = true)
            {
                if (pageBreak && breakBefore)
                {
                    blocks.Add("::: pagebreak\n:::");
                }

                blocks.Add(block);
            }

            if (element.Attribute(DL + "md")?.Value is { } source)
            {
                Add(source);
                continue;
            }

            if (element.Name == W + "tbl")
            {
                if (this.TableStyle(element) == ImageRow)
                {
                    Add(this.ImageRowOf(element, part));
                }
                else
                {
                    var table = this.Table(element, part);
                    Add((tableTitle is null ? string.Empty : "**" + tableTitle + "**\n\n") + table + (tableCaption is null ? string.Empty : "\n\nTable: " + tableCaption));
                }

                tableTitle = null;
                tableCaption = null;
                continue;
            }

            if (tableTitle is not null || tableCaption is not null)
            {
                // A title or caption that no table followed after all.
                if (tableTitle is not null)
                {
                    blocks.Add("**" + tableTitle + "**");
                }

                if (tableCaption is not null)
                {
                    blocks.Add("Table: " + tableCaption);
                }

                tableTitle = null;
                tableCaption = null;
            }

            this.NoteJustified(element);
            if (role is not null && Headings.Contains(role) && !footnote)
            {
                var level = Math.Clamp(Array.IndexOf(Headings, role) + 1 + shift, 1, 6);
                var text = EscapeHeading(this.Inline(element, part).Replace("\\\n", " ", StringComparison.Ordinal).Trim());
                string? appendix = null;
                if (element.Attribute(DL + "label")?.Value is { } label && text.StartsWith(label.Trim(), StringComparison.Ordinal))
                {
                    // "Appendix A " the generator wrote before the heading's text.
                    text = text[label.Trim().Length..].TrimStart();
                }
                else if (this.Marker is null && level == 1 && AppendixLabel().Match(text) is { Success: true } written)
                {
                    text = text[written.Length..].TrimStart();
                    appendix = this.appendixSeen ? null : ".appendix";
                    this.appendixSeen = true;
                }
                else if (this.Marker is null && level == 1 && !this.appendixSeen
                         && element.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value is { } lettered
                         && this.letteredNumbers.Contains(lettered))
                {
                    appendix = ".appendix";
                    this.appendixSeen = true;
                }

                string? attributes;
                if (this.Marker is not null)
                {
                    // The attributes as written, whose text the heading's id is made of; a page break they do not
                    // explain came from a `::: pagebreak`.
                    attributes = element.Attribute(DL + "attrs")?.Value;
                    if (pageBreak && level > 1 && attributes?.Contains(".newpage", StringComparison.Ordinal) != true)
                    {
                        blocks.Add("::: pagebreak\n:::");
                    }
                }
                else
                {
                    var found = new List<string>();
                    var numId = element.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value;
                    if (numId == "0")
                    {
                        found.Add(".unnumbered");
                    }

                    if (pageBreak && level > 1)
                    {
                        found.Add(".newpage");
                    }

                    if (appendix is not null)
                    {
                        found.Add(appendix);
                    }

                    attributes = found.Count == 0 ? null : "{" + string.Join(" ", found) + "}";
                }

                blocks.Add(new string('#', level) + " " + text + (attributes is null ? string.Empty : " " + attributes));
                continue;
            }

            if (role == Code)
            {
                var lines = new List<string> { CodeText(element) };
                var language = element.Attribute(DL + "code")?.Value ?? string.Empty;
                while (i + 1 < elements.Count && this.Role(elements[i + 1]) == Code && elements[i + 1].Attribute(DL + "code") is null
                       && elements[i + 1].Attribute(DL + "skip") is null && elements[i + 1].Attribute(DL + "md") is null)
                {
                    lines.Add(CodeText(elements[++i]));
                }

                var content = string.Join("\n", lines);
                var fence = new string('`', Math.Max(3, LongestRun(content, '`') + 1));
                Add(fence + language + "\n" + content + "\n" + fence);
                continue;
            }

            if (special && role is not null && Alerts.Contains(role) && StartsWithStrong(element))
            {
                var group = new List<XElement> { element };
                while (i + 1 < elements.Count && (this.Role(elements[i + 1]) == role && !StartsWithStrong(elements[i + 1])
                       || (IsListRole(this.Role(elements[i + 1])) && elements[i + 1].Element(W + "pPr")?.Element(W + "pBdr") is not null)))
                {
                    group.Add(elements[++i]);
                }

                var content = this.Blocks(group, part with { Plain = role }, shift, inner: true);
                Add(Prefix("[!" + role.ToUpperInvariant() + "]" + (content.Count == 0 ? string.Empty : "\n" + string.Join("\n\n", content)), "> "));
                continue;
            }

            if (special && role == Quote)
            {
                var group = new List<XElement> { element };
                while (i + 1 < elements.Count && this.Role(elements[i + 1]) == Quote && elements[i + 1].Attribute(DL + "skip") is null && elements[i + 1].Attribute(DL + "md") is null)
                {
                    group.Add(elements[++i]);
                }

                Add(Prefix(string.Join("\n\n", this.Blocks(group, part with { Plain = Quote }, shift, inner: true)), "> "));
                continue;
            }

            if (IsListRole(role))
            {
                var items = new List<XElement> { element };
                while (i + 1 < elements.Count && IsListRole(this.Role(elements[i + 1])) && elements[i + 1].Attribute(DL + "md") is null
                       && !PageBreakBefore(elements[i + 1]))
                {
                    items.Add(elements[++i]);
                }

                Add(this.List(items, part));
                continue;
            }

            if (role == DefinitionTerm)
            {
                var text = new StringBuilder(EscapeLines(this.Inline(element, part)));
                while (i + 1 < elements.Count && this.Role(elements[i + 1]) == Definition && elements[i + 1].Attribute(DL + "md") is null)
                {
                    text.Append('\n').Append(":   ").Append(Indent(EscapeLines(this.Inline(elements[++i], part)), "    ", first: false));
                }

                Add(text.ToString());
                continue;
            }

            if (role == Rule)
            {
                Add("---");
                continue;
            }

            if (role == TableTitle && i + 1 < elements.Count
                && (elements[i + 1].Name == W + "tbl" || (i + 2 < elements.Count && this.Role(elements[i + 1]) == Caption && elements[i + 2].Name == W + "tbl")))
            {
                tableTitle = this.Inline(element, part);
                continue;
            }

            if (role == Caption && this.CaptionOf(element, part) is { } caption)
            {
                var (kind, captionText) = caption;
                if (kind == "Table" && i + 1 < elements.Count && elements[i + 1].Name == W + "tbl")
                {
                    tableCaption = captionText;
                    continue;
                }

                if (kind == "Figure" && picture is { Title: null } && blocks.Count > 0)
                {
                    // A picture's caption, after Word dropped the image's title: the caption becomes the title.
                    blocks[^1] = (picture with { Title = PlainCaption(element) }).Markdown;
                    continue;
                }

                Add((kind == "Table" ? "Table: " : "Figure: ") + captionText);
                continue;
            }

            if (element.Descendants(C + "chart").FirstOrDefault() is { } chart)
            {
                Add(this.ChartBlock(chart, part));
                continue;
            }

            if (this.LonePicture(element, part) is { } lone)
            {
                Add(lone.Markdown);
                lastPicture = lone;
                continue;
            }

            var paragraph = this.Inline(element, part, dropLeadingStrong: role is not null && Alerts.Contains(role));
            if (paragraph.Trim().Length == 0)
            {
                continue;
            }

            if (role == ContainerHeading)
            {
                paragraph = "**" + paragraph.Trim() + "**";
            }

            Add(EscapeLines(paragraph));
        }

        if (tableTitle is not null)
        {
            blocks.Add("**" + tableTitle + "**");
        }

        if (tableCaption is not null)
        {
            blocks.Add("Table: " + tableCaption);
        }

        return blocks;
    }

    private static bool IsListRole(string? role) => role is ListBullet or ListNumber or ListTask;

    private void NoteJustified(XElement element)
    {
        if (element.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val")?.Value == "both")
        {
            this.justified = true;
        }
    }

    // ---- Lists ----

    private sealed class ListItem
    {
        public int Level { get; init; }

        public bool Ordered { get; init; }

        public string NumId { get; init; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public List<string> More { get; } = [];
    }

    private string List(List<XElement> paragraphs, Part part)
    {
        var items = new List<ListItem>();
        foreach (var paragraph in paragraphs)
        {
            var role = this.Role(paragraph);
            var properties = paragraph.Element(W + "pPr");
            var numId = properties?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value;
            var level = int.TryParse(properties?.Element(W + "numPr")?.Element(W + "ilvl")?.Attribute(W + "val")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var l) ? l : 0;
            var box = paragraph.Descendants(W14 + "checkbox").FirstOrDefault();
            if (box is not null)
            {
                var done = box.Element(W14 + "checked")?.Attribute(W14 + "val")?.Value is "1" or "true";
                var left = int.TryParse(properties?.Element(W + "ind")?.Attribute(W + "left")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var indent) ? indent : 360;
                items.Add(new ListItem { Level = Math.Clamp((int)Math.Round(left / 360.0) - 1, 0, 2), Text = (done ? "[x] " : "[ ] ") + EscapeLines(this.Inline(paragraph, part).TrimStart('\t')).TrimStart() });
                continue;
            }

            if (numId is not null && numId != "0")
            {
                items.Add(new ListItem
                {
                    Level = Math.Clamp(level, 0, 2),
                    Ordered = role == ListNumber,
                    NumId = numId,
                    Text = EscapeLines(this.Inline(paragraph, part)),
                });
                continue;
            }

            // A paragraph that continues the item before it, or a picture in it.
            var more = this.LonePicture(paragraph, part)?.Markdown ?? EscapeLines(this.Inline(paragraph, part));
            if (items.Count == 0)
            {
                items.Add(new ListItem { Ordered = role == ListNumber, Text = more });
            }
            else if (more.Trim().Length > 0)
            {
                items[^1].More.Add(more);
            }
        }

        var text = new StringBuilder();
        var open = new List<(ListItem First, int Width, int Number, char Delimiter)>();
        var delimiters = new Dictionary<int, char>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var level = Math.Min(item.Level, open.Count);
            var continues = level < open.Count && open[level].First.Ordered == item.Ordered && (!item.Ordered || open[level].First.NumId == item.NumId);
            if (level < open.Count && !continues)
            {
                // Another list at the same level: an ordered one changes its delimiter, so that Markdown starts a new one.
                if (item.Ordered && open[level].First.Ordered)
                {
                    delimiters[level] = delimiters.GetValueOrDefault(level, '.') == '.' ? ')' : '.';
                }

                open.RemoveRange(level, open.Count - level);

                // A blank line before it, so that it never reads as the text of the item before.
                text.Append('\n');
            }
            else if (level < open.Count - 1)
            {
                open.RemoveRange(level + 1, open.Count - level - 1);
            }

            var indent = new string(' ', open.Take(level).Sum(o => o.Width));
            string marker;
            if (level < open.Count)
            {
                var entry = open[level];
                marker = item.Ordered ? (entry.Number + 1).ToString(CultureInfo.InvariantCulture) + entry.Delimiter + " " : "- ";
                open[level] = entry with { Number = entry.Number + 1, Width = marker.Length };
            }
            else
            {
                var delimiter = delimiters.GetValueOrDefault(level, '.');
                var number = item.Ordered ? this.starts.GetValueOrDefault((item.NumId, item.Level), 1) : 0;
                marker = item.Ordered ? number.ToString(CultureInfo.InvariantCulture) + delimiter + " " : "- ";
                open.Add((item, marker.Length, number, delimiter));
            }

            var width = new string(' ', indent.Length + marker.Length);
            text.Append(indent).Append(marker).Append(Indent(item.Text, width, first: false)).Append('\n');
            foreach (var more in item.More)
            {
                text.Append('\n').Append(Indent(more, width, first: true)).Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    // ---- Tables ----

    private string Table(XElement table, Part part)
    {
        var rows = table.Elements(W + "tr").Select(r => r.Elements(W + "tc").Select(c => c).ToList()).ToList();
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columns = rows.Max(r => r.Count);
        var text = new StringBuilder();
        for (var r = 0; r < rows.Count; r++)
        {
            text.Append('|');
            for (var c = 0; c < columns; c++)
            {
                var cell = c < rows[r].Count ? this.Cell(rows[r][c], part) : string.Empty;
                text.Append(' ').Append(cell).Append(" |");
            }

            text.Append('\n');
            if (r == 0)
            {
                text.Append('|');
                for (var c = 0; c < columns; c++)
                {
                    var alignment = c < rows[0].Count ? rows[0][c].Element(W + "p")?.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val")?.Value : null;
                    text.Append(alignment switch
                    {
                        "left" or "start" => " :--- |",
                        "center" => " :---: |",
                        "right" or "end" => " ---: |",
                        _ => " --- |",
                    });
                }

                text.Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    private string Cell(XElement cell, Part part) => string.Join(" ", cell.Elements(W + "p")
        .Select(p => this.Inline(p, part, inCell: true).Replace("\\\n", " ", StringComparison.Ordinal).Trim())
        .Where(t => t.Length > 0)).Replace("|", "\\|", StringComparison.Ordinal);

    // A borderless table of pictures, each with its caption under it: a paragraph of images.
    private string ImageRowOf(XElement table, Part part)
    {
        var pictures = new List<string>();
        foreach (var cell in table.Descendants(W + "tc"))
        {
            Picture? picture = null;
            foreach (var paragraph in cell.Elements(W + "p"))
            {
                if (paragraph.Attribute(DL + "skip") is not null)
                {
                    continue;
                }

                if (this.LonePicture(paragraph, part) is { } found)
                {
                    picture = found;
                }
                else if (this.Role(paragraph) == Caption && picture is { Title: null })
                {
                    picture = picture with { Title = PlainCaption(paragraph) };
                }
            }

            if (picture is not null)
            {
                pictures.Add(picture.Markdown);
            }
        }

        return string.Join(" ", pictures);
    }

    // ---- Captions ----

    // "Table 1: text": the kind from the SEQ field, the text after the colon that follows it.
    private (string Kind, string Text)? CaptionOf(XElement paragraph, Part part)
    {
        var instruction = string.Concat(paragraph.Descendants(W + "instrText").Select(t => t.Value));
        if (SeqPattern().Match(instruction) is not { Success: true } match)
        {
            return null;
        }

        var rest = this.CaptionRest(paragraph);
        return (match.Groups[1].Value, EscapeLines(this.InlineOf(rest, part)).Trim());
    }

    private string PlainCaption(XElement paragraph) => string.Concat(this.CaptionRest(paragraph).SelectMany(e => e.DescendantsAndSelf(W + "t")).Select(t => t.Value)).Trim();

    // The elements of a caption after the label, its field and the colon.
    private List<XElement> CaptionRest(XElement paragraph)
    {
        var children = paragraph.Elements().Where(e => e.Name != W + "pPr").ToList();
        var end = children.FindIndex(e => e.Descendants(W + "fldChar").Any(f => f.Attribute(W + "fldCharType")?.Value == "end"));
        var colon = children.FindIndex(Math.Max(0, end), e => e.Descendants(W + "t").Any(t => t.Value.StartsWith(':')));
        return colon < 0 ? children : children.Skip(colon + 1).ToList();
    }

    // ---- Pictures and charts ----

    private sealed record Picture(string Alt, string Path, string? Title, string? Attributes)
    {
        public string Markdown =>
            $"![{EscapeAlt(this.Alt)}]({MarkdownText.Destination(this.Path)}{(this.Title is null ? string.Empty : " \"" + this.Title.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")}){this.Attributes}";
    }

    // A paragraph of nothing but one picture.
    private Picture? LonePicture(XElement paragraph, Part part)
    {
        var drawings = paragraph.Descendants(W + "drawing").ToList();
        if (drawings.Count != 1 || paragraph.Descendants(W + "t").Any(t => t.Value.Trim().Length > 0))
        {
            return null;
        }

        return this.PictureOf(drawings[0], part);
    }

    private Picture? PictureOf(XElement drawing, Part part, string? name = null)
    {
        var properties = drawing.Descendants(WP + "docPr").FirstOrDefault();
        var alt = properties?.Attribute("descr")?.Value ?? string.Empty;
        var title = properties?.Attribute(DL + "title")?.Value;
        var attributes = properties?.Attribute(DL + "attrs")?.Value
            ?? (this.Marker is null && drawing.Descendants(A + "prstGeom").FirstOrDefault()?.Attribute("prst")?.Value == "ellipse" ? "{.round}" : null);
        if (properties?.Attribute(DL + "url")?.Value is { } url)
        {
            return new Picture(alt, url, title, attributes);
        }

        var blip = drawing.Descendants(A + "blip").FirstOrDefault();
        if (blip is null || this.images is null)
        {
            return null;
        }

        // An SVG keeps its SVG beside the PNG: the SVG is the image.
        var id = blip.Descendants(Asvg + "svgBlip").FirstOrDefault()?.Attribute(R + "embed")?.Value ?? blip.Attribute(R + "embed")?.Value;
        if (id is null || !part.Rels.TryGetValue(id, out var target) || this.package.ReadBytes(target) is not { } bytes)
        {
            return null;
        }

        var file = target[(target.LastIndexOf('/') + 1)..];
        var path = this.images.Add(string.IsNullOrWhiteSpace(name) ? file : name + System.IO.Path.GetExtension(file), bytes);
        return new Picture(alt, path, title, attributes);
    }

    // A native chart without its Markdown: a `chart` block from the values the chart caches.
    private string ChartBlock(XElement reference, Part part)
    {
        var id = reference.Attribute(R + "id")?.Value;
        var space = id is not null && part.Rels.TryGetValue(id, out var target) ? this.package.ReadXml(target)?.Root : null;
        var plot = space?.Descendants(C + "plotArea").FirstOrDefault()?.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));
        if (plot is null)
        {
            return "```chart\n```";
        }

        var type = plot.Name.LocalName switch
        {
            "lineChart" => "line",
            "areaChart" => "area",
            "pieChart" or "doughnutChart" => "pie",
            _ => "bar",
        };
        var text = new StringBuilder("```chart\n").Append("type: ").Append(type).Append('\n');
        var title = string.Concat(space!.Element(C + "chart")?.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? []);
        if (title.Length > 0)
        {
            text.Append("title: ").Append(title).Append('\n');
        }

        if (plot.Element(C + "grouping")?.Attribute("val")?.Value is "stacked" or "percentStacked")
        {
            text.Append("stacked: true\n");
        }

        var series = plot.Elements(C + "ser").ToList();
        static List<string> Points(XElement? data) => data?.Descendants(C + "pt")
            .OrderBy(p => int.TryParse(p.Attribute("idx")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Select(p => p.Element(C + "v")?.Value ?? string.Empty).ToList() ?? [];
        var categories = Points(series.FirstOrDefault()?.Element(C + "cat"));
        text.Append("---\n").Append(string.Join(", ", new[] { "category" }.Concat(series.Select((s, n) =>
            string.Concat(s.Element(C + "tx")?.Descendants(C + "v").Select(v => v.Value) ?? []) is { Length: > 0 } name ? name : "series " + (n + 1).ToString(CultureInfo.InvariantCulture))))).Append('\n');
        var values = series.Select(s => Points(s.Element(C + "val"))).ToList();
        for (var row = 0; row < categories.Count; row++)
        {
            text.Append(string.Join(", ", new[] { categories[row] }.Concat(values.Select(v => row < v.Count ? v[row] : string.Empty)))).Append('\n');
        }

        return text.Append("```").ToString();
    }

    // ---- Inline ----

    [Flags]
    private enum Emphasis
    {
        None = 0,
        Strike = 1,
        Mark = 2,
        Bold = 4,
        Italic = 8,
    }

    // Text (escaped when written), Markdown as it is, or code.
    private sealed record Segment(string Text, Emphasis Flags, char Kind);

    private string Inline(XElement paragraph, Part part, bool dropLeadingStrong = false, bool inCell = false) =>
        this.InlineOf([.. paragraph.Elements().Where(e => e.Name != W + "pPr")], part, dropLeadingStrong, inCell);

    private string InlineOf(List<XElement> children, Part part, bool dropLeadingStrong = false, bool inCell = false)
    {
        var segments = new List<(Segment Segment, string? Link, string? Colour)>();
        var dropping = dropLeadingStrong;
        foreach (var child in children)
        {
            this.Collect(child, part, null, segments, ref dropping, inCell);
        }

        // Links and coloured words are groups of runs: their common emphasis outside, the rest inside.
        var items = new List<Segment>();
        for (var i = 0; i < segments.Count;)
        {
            var (segment, link, colour) = segments[i];
            if (link is null && colour is null)
            {
                items.Add(segment);
                i++;
                continue;
            }

            var group = new List<Segment>();
            for (; i < segments.Count && segments[i].Link == link && segments[i].Colour == colour; i++)
            {
                group.Add(segments[i].Segment);
            }

            var common = group.Aggregate(Emphasis.Bold | Emphasis.Italic | Emphasis.Strike | Emphasis.Mark, (f, s) => f & s.Flags);
            var inner = Emit([.. group.Select(s => s with { Flags = s.Flags & ~common })]);
            var markdown = colour is not null ? $"[{inner}]{{.{colour.ToLowerInvariant()}}}"
                : $"[{inner}]({MarkdownText.Destination(link!)})";
            items.Add(new Segment(markdown, common, 'm'));
        }

        return Emit(items).Trim(' ');
    }

    private void Collect(XElement node, Part part, string? link, List<(Segment, string?, string?)> segments, ref bool dropping, bool inCell)
    {
        if (node.Name == W + "hyperlink")
        {
            var target = node.Attribute(W + "anchor")?.Value is { } anchor
                ? "#" + this.anchors.GetValueOrDefault(anchor, anchor)
                : node.Attribute(R + "id")?.Value is { } id && part.Rels.TryGetValue(id, out var url) ? url : null;
            foreach (var child in node.Elements())
            {
                this.Collect(child, part, target, segments, ref dropping, inCell);
            }

            return;
        }

        if (node.Name == W + "sdt")
        {
            // A task's check box is read by the list; other content controls by their content.
            if (node.Descendants(W14 + "checkbox").Any())
            {
                return;
            }

            foreach (var child in node.Element(W + "sdtContent")?.Elements() ?? [])
            {
                this.Collect(child, part, link, segments, ref dropping, inCell);
            }

            return;
        }

        if (node.Name == W + "smartTag" || node.Name == W + "ins" || node.Name == W + "fldSimple" || node.Name == W + "customXml" || node.Name == W + "moveTo")
        {
            foreach (var child in node.Elements())
            {
                this.Collect(child, part, link, segments, ref dropping, inCell);
            }

            return;
        }

        if (node.Name != W + "r")
        {
            return;
        }

        var properties = node.Element(W + "rPr");
        var style = properties?.Element(W + "rStyle")?.Attribute(W + "val")?.Value;
        var role = style is null ? null : this.roles.GetValueOrDefault(style, style);
        if (dropping)
        {
            // An alert's title, and the space after it.
            if (role == Strong)
            {
                return;
            }

            dropping = false;
            if (node.Elements(W + "t").Any() && node.Elements().All(e => e.Name == W + "t" || e.Name == W + "rPr")
                && string.Concat(node.Elements(W + "t").Select(t => t.Value)).Trim().Length == 0)
            {
                return;
            }
        }

        if (node.Element(W + "footnoteReference")?.Attribute(W + "id")?.Value is { } footnote)
        {
            this.footnoteOrder.Add(footnote);
            segments.Add((new Segment($"[^{this.footnoteOrder.Count.ToString(CultureInfo.InvariantCulture)}]", Emphasis.None, 'm'), null, null));
            return;
        }

        if (node.Element(W + "footnoteRef") is not null)
        {
            return;
        }

        var flags = Emphasis.None;
        if (IsOn(properties?.Element(W + "b")))
        {
            flags |= Emphasis.Bold;
        }

        if (IsOn(properties?.Element(W + "i")))
        {
            flags |= Emphasis.Italic;
        }

        if (IsOn(properties?.Element(W + "strike")))
        {
            flags |= Emphasis.Strike;
        }

        if (role == Mark)
        {
            flags |= Emphasis.Mark;
        }
        else if (role == Strong)
        {
            flags |= Emphasis.Bold;
        }

        string? colour = null;
        string? label = null;
        foreach (var category in Categories)
        {
            if (role == "Colour" + category)
            {
                colour = category;
            }
            else if (role == "Label" + category)
            {
                label = category;
            }
        }

        var code = role is CodeCharacter or Tag || label is not null;
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0)
            {
                var value = text.ToString();
                if (label is not null)
                {
                    if (!this.labels.TryGetValue(label, out var list))
                    {
                        this.labels[label] = list = [];
                    }

                    if (!list.Contains(value))
                    {
                        list.Add(value);
                    }
                }

                segments.Add((new Segment(value, flags, code ? 'c' : 't'), link, colour));
                text.Clear();
            }
        }

        foreach (var child in node.Elements())
        {
            if (child.Name == W + "t")
            {
                text.Append(child.Value);
            }
            else if (child.Name == W + "tab")
            {
                text.Append('\t');
            }
            else if (child.Name == W + "noBreakHyphen")
            {
                text.Append('-');
            }
            else if ((child.Name == W + "br" && child.Attribute(W + "type")?.Value is null or "textWrapping") || child.Name == W + "cr")
            {
                Flush();
                segments.Add((new Segment(inCell ? " " : "\\\n", Emphasis.None, 'm'), link, null));
            }
            else if (child.Name == W + "drawing")
            {
                Flush();
                if (this.InlineDrawing(child, part) is { } drawing)
                {
                    segments.Add((new Segment(drawing, flags, 'm'), link, colour));
                }
            }
        }

        Flush();
    }

    // An icon (its title names it), or a picture in the line.
    private string? InlineDrawing(XElement drawing, Part part)
    {
        if (IsCoverImage(drawing))
        {
            return null;
        }

        var properties = drawing.Descendants(WP + "docPr").FirstOrDefault();
        if (properties?.Attribute("title")?.Value is { } title && title.StartsWith(IconTitle, StringComparison.Ordinal))
        {
            return ":icon-" + title[IconTitle.Length..] + ":";
        }

        if (drawing.Descendants(C + "chart").Any())
        {
            return null;
        }

        return this.PictureOf(drawing, part)?.Markdown;
    }

    // Emphasis as delimiters, nested like a stack: what the next segment has not is closed, what it adds is opened.
    // Whitespace stays outside the delimiters, which Markdown needs.
    private static string Emit(List<Segment> segments)
    {
        var text = new StringBuilder();
        var open = new List<Emphasis>();
        static string Delimiter(Emphasis flag) => flag switch
        {
            Emphasis.Strike => "~~",
            Emphasis.Mark => "==",
            Emphasis.Bold => "**",
            _ => "*",
        };

        void Close(int from)
        {
            var trailing = text.Length - text.ToString().TrimEnd(' ', '\t').Length;
            var spaces = text.ToString(text.Length - trailing, trailing);
            text.Length -= trailing;
            for (var k = open.Count - 1; k >= from; k--)
            {
                text.Append(Delimiter(open[k]));
            }

            open.RemoveRange(from, open.Count - from);
            text.Append(spaces);
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var blank = segment.Kind == 't' && segment.Text.Trim().Length == 0;
            var wanted = blank ? open.Aggregate(Emphasis.None, (f, o) => f | o) & (i + 1 < segments.Count ? segments[i + 1].Flags : Emphasis.None) : segment.Flags;
            var keep = 0;
            while (keep < open.Count && wanted.HasFlag(open[keep]))
            {
                keep++;
            }

            if (keep < open.Count)
            {
                Close(keep);
            }

            var content = segment.Kind switch
            {
                't' => EscapeText(segment.Text),
                'c' => MarkdownText.Code(segment.Text),
                _ => segment.Text,
            };
            foreach (var flag in new[] { Emphasis.Strike, Emphasis.Mark, Emphasis.Bold, Emphasis.Italic })
            {
                if (wanted.HasFlag(flag) && !open.Contains(flag))
                {
                    var leading = content.Length - content.TrimStart(' ', '\t').Length;
                    text.Append(content[..leading]);
                    content = content[leading..];
                    text.Append(Delimiter(flag));
                    open.Add(flag);
                }
            }

            text.Append(content);
        }

        Close(0);
        return text.ToString();
    }

    // ---- Helpers ----

    private string? Role(XElement element)
    {
        var id = element.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        if (id is null)
        {
            return element.Name == W + "p" ? Normal : null;
        }

        if (this.roles.TryGetValue(id, out var role))
        {
            return role;
        }

        // A caller's heading styles, which the generator found by name.
        var name = this.styleNames.GetValueOrDefault(id, id);
        return HeadingName().Match(name) is { Success: true } heading ? "Heading" + heading.Groups[1].Value : id;
    }

    private string? TableStyle(XElement table)
    {
        var id = table.Element(W + "tblPr")?.Element(W + "tblStyle")?.Attribute(W + "val")?.Value;
        return id is null ? null : this.roles.GetValueOrDefault(id, id);
    }

    private static bool PageBreakBefore(XElement element) =>
        (element.Name == W + "tbl" ? element.Descendants(W + "p").FirstOrDefault() : element)?.Element(W + "pPr")?.Element(W + "pageBreakBefore") is { } flag && IsOn(flag);

    private static bool StartsWithStrong(XElement paragraph) => paragraph.Elements(W + "r").FirstOrDefault()?.Element(W + "rPr")?.Element(W + "rStyle") is not null
        && paragraph.Elements().FirstOrDefault(e => e.Name != W + "pPr")?.Name == W + "r";

    private static string CodeText(XElement paragraph) => string.Concat(paragraph.Descendants().Select(e =>
        e.Name == W + "t" ? e.Value : e.Name == W + "tab" ? "\t" : string.Empty));

    private static IEnumerable<string> PlainLines(XElement paragraph)
    {
        var line = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == W + "t")
            {
                line.Append(element.Value);
            }
            else if (element.Name == W + "br" && element.Attribute(W + "type")?.Value is null or "textWrapping")
            {
                yield return line.ToString();
                line.Clear();
            }
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    private void LoadNumbering()
    {
        var numbering = this.package.ReadXml("word/numbering.xml")?.Root;
        var lettered = (numbering?.Elements(W + "abstractNum") ?? [])
            .Where(a => a.Elements(W + "lvl").FirstOrDefault()?.Element(W + "numFmt")?.Attribute(W + "val")?.Value == "upperLetter")
            .Select(a => a.Attribute(W + "abstractNumId")?.Value).ToHashSet();
        foreach (var num in numbering?.Elements(W + "num") ?? [])
        {
            var id = num.Attribute(W + "numId")?.Value;
            if (id is not null && lettered.Contains(num.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value))
            {
                // The appendices' numbering in letters (`Appendix A`), when headings are numbered.
                this.letteredNumbers.Add(id);
            }

            foreach (var over in num.Elements(W + "lvlOverride"))
            {
                if (id is not null && int.TryParse(over.Attribute(W + "ilvl")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
                    && int.TryParse(over.Element(W + "startOverride")?.Attribute(W + "val")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
                {
                    this.starts[(id, level)] = start;
                }
            }
        }
    }

    private void LoadFootnotes()
    {
        foreach (var footnote in this.package.ReadXml(FootnotesPart)?.Root?.Elements(W + "footnote") ?? [])
        {
            if (footnote.Attribute(W + "type") is null && footnote.Attribute(W + "id")?.Value is { } id)
            {
                this.footnotes[id] = footnote;
            }
        }
    }

    private static bool IsOn(XElement? toggle) =>
        toggle is not null && toggle.Attribute(W + "val")?.Value is null or "1" or "true" or "on";

    private static int LongestRun(string text, char c)
    {
        var longest = 0;
        var run = 0;
        foreach (var x in text)
        {
            run = x == c ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    // Text as Markdown: what would be formatting is escaped, also `==` (marks) and `&` before a name (entities).
    private static string EscapeText(string text)
    {
        var escaped = MarkdownText.Escape(text).Replace("==", "\\=\\=", StringComparison.Ordinal);
        return EntityPattern().Replace(escaped, "\\&");
    }

    private static string EscapeAlt(string alt) => EscapeText(alt.Replace('\n', ' ')).Replace("]", "\\]", StringComparison.Ordinal);

    // Each line of a paragraph made safe at its start, where `#`, `>`, `-` or `1.` would start another block.
    private static string EscapeLines(string text) => string.Join("\n", text.Split('\n').Select(MarkdownText.EscapeLineStart));

    private static string EscapeHeading(string text) => text.StartsWith('#') ? "\\" + text : text;

    private static string Prefix(string text, string prefix) =>
        string.Join("\n", text.Split('\n').Select(l => l.Length == 0 ? prefix.TrimEnd() : prefix + l));

    private static string Indent(string text, string indent, bool first) =>
        string.Join("\n", text.Split('\n').Select((l, n) => (n == 0 && !first) || l.Length == 0 ? l : indent + l));

    [GeneratedRegex(@"\\o\s+""1-([1-9])""", RegexOptions.CultureInvariant)]
    private static partial Regex TocDepthPattern();

    [GeneratedRegex(@"^\s*SEQ\s+(Table|Figure)\b", RegexOptions.CultureInvariant)]
    private static partial Regex SeqPattern();

    [GeneratedRegex(@"^(?:Appendix|Anhang) [A-Z](?= |$)", RegexOptions.CultureInvariant)]
    private static partial Regex AppendixLabel();

    [GeneratedRegex(@"^(?:heading|berschrift|Überschrift)\s*([1-6])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingName();

    [GeneratedRegex(@"&(?=[A-Za-z#][A-Za-z0-9]*;)", RegexOptions.CultureInvariant)]
    private static partial Regex EntityPattern();
}
