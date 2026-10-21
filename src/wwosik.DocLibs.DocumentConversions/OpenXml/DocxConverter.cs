using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentConversions.OpenXml;

/// <summary>
/// Converts Word documents (<c>.docx</c>) into Markdown: headings from paragraph styles, lists from
/// numbering, tables, bold and italic, hyperlinks, line breaks, text boxes and images.
/// </summary>
public sealed class DocxConverter : IDocumentConverter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";

    private const string DocumentPart = "word/document.xml";

    // A backslash break, because MarkdownText.Normalize trims the two-space form away.
    private const string HardBreak = "\\\n";

    /// <inheritdoc/>
    public bool CanConvert(DocumentFormat format) => format == DocumentFormat.Docx;

    /// <inheritdoc/>
    public MarkdownDocument Convert(Stream input, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        return OpenXmlPackage.Guard(DocumentFormat.Docx, () =>
        {
            using var package = new OpenXmlPackage(input);
            var body = package.ReadXml(DocumentPart)?.Root?.Element(W + "body")
                ?? throw new DocumentConversionException(DocumentFormat.Docx, "word/document.xml has no body.");
            var context = new Context(
                package,
                package.Relationships(DocumentPart),
                LoadStyles(package),
                LoadNumbering(package),
                options.IncludeImages ? new ResourceCollector(options.ResourcePathPrefix) : null);

            var builder = new StringBuilder();
            foreach (var block in body.Elements())
            {
                RenderBlock(block, context, builder);
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            package.AddCoreProperties(metadata);
            return new MarkdownDocument(
                MarkdownText.Normalize(builder.ToString()),
                context.Images?.Resources ?? [],
                metadata);
        });
    }

    private sealed record Context(
        OpenXmlPackage Package,
        IReadOnlyDictionary<string, string> Rels,
        IReadOnlyDictionary<string, string> Styles,
        IReadOnlyDictionary<(string NumId, int Level), bool> NumberingIsOrdered,
        ResourceCollector? Images);

    private static Dictionary<string, string> LoadStyles(OpenXmlPackage package)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var styles = package.ReadXml("word/styles.xml");
        if (styles?.Root is null)
        {
            return map;
        }

        foreach (var style in styles.Root.Elements(W + "style"))
        {
            var id = style.Attribute(W + "styleId")?.Value;
            if (id is null)
            {
                continue;
            }

            var name = style.Element(W + "name")?.Attribute(W + "val")?.Value ?? id;
            map[id] = ClassifyHeading(id, name);
        }

        return map;
    }

    private static Dictionary<(string, int), bool> LoadNumbering(OpenXmlPackage package)
    {
        var result = new Dictionary<(string, int), bool>();
        var numbering = package.ReadXml("word/numbering.xml");
        if (numbering?.Root is null)
        {
            return result;
        }

        var abstractFormats = new Dictionary<string, Dictionary<int, bool>>();
        foreach (var abs in numbering.Root.Elements(W + "abstractNum"))
        {
            var absId = abs.Attribute(W + "abstractNumId")?.Value;
            if (absId is null)
            {
                continue;
            }

            var levels = new Dictionary<int, bool>();
            foreach (var lvl in abs.Elements(W + "lvl"))
            {
                var level = int.TryParse(lvl.Attribute(W + "ilvl")?.Value, out var n) ? n : 0;
                var format = lvl.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "bullet";
                levels[level] = !format.Equals("bullet", StringComparison.OrdinalIgnoreCase)
                    && !format.Equals("none", StringComparison.OrdinalIgnoreCase);
            }

            abstractFormats[absId] = levels;
        }

        foreach (var num in numbering.Root.Elements(W + "num"))
        {
            var numId = num.Attribute(W + "numId")?.Value;
            var absId = num.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value;
            if (numId is null || absId is null || !abstractFormats.TryGetValue(absId, out var levels))
            {
                continue;
            }

            foreach (var (level, ordered) in levels)
            {
                result[(numId, level)] = ordered;
            }
        }

        return result;
    }

    private static void RenderBlock(XElement block, Context context, StringBuilder builder)
    {
        if (block.Name == W + "p")
        {
            RenderParagraph(block, context, builder);
        }
        else if (block.Name == W + "tbl")
        {
            RenderTable(block, context, builder);
        }
        else if (block.Name == W + "sdt" || block.Name == W + "customXml")
        {
            var content = block.Name == W + "sdt" ? block.Element(W + "sdtContent") : block;
            if (content is not null)
            {
                foreach (var child in content.Elements())
                {
                    RenderBlock(child, context, builder);
                }
            }
        }
    }

    private static void RenderParagraph(XElement paragraph, Context context, StringBuilder builder)
    {
        RenderParagraphText(paragraph, context, builder);

        // Text boxes anchored in this paragraph; mc:Fallback repeats what mc:Choice already holds.
        foreach (var box in paragraph.Descendants(W + "txbxContent").Where(t => !t.Ancestors(MC + "Fallback").Any()))
        {
            var text = string.Join(" / ", box.Descendants(W + "p")
                .Select(p => RenderRuns(p, context).Trim())
                .Where(t => t.Length > 0));
            if (text.Length == 0)
            {
                continue;
            }

            EnsureBlankLine(builder);
            builder.Append("> [Text box] ").Append(text.Replace(HardBreak, " ", StringComparison.Ordinal)).Append("\n\n");
        }
    }

    private static void RenderParagraphText(XElement paragraph, Context context, StringBuilder builder)
    {
        var text = RenderRuns(paragraph, context).TrimEnd();

        var properties = paragraph.Element(W + "pPr");
        var styleId = properties?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        var heading = styleId is not null && context.Styles.TryGetValue(styleId, out var h) ? h : string.Empty;
        if (heading.Length == 0 && styleId is not null)
        {
            heading = ClassifyHeading(styleId, styleId);
        }

        if (text.Trim().Length == 0)
        {
            builder.Append('\n');
            return;
        }

        if (heading is "title" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            EnsureBlankLine(builder);
            var level = heading == "title" ? 1 : heading[1] - '0';
            builder.Append('#', level).Append(' ').Append(text.Replace(HardBreak, " ", StringComparison.Ordinal).Trim()).Append("\n\n");
            return;
        }

        var numbering = properties?.Element(W + "numPr");
        if (numbering is not null)
        {
            var numId = numbering.Element(W + "numId")?.Attribute(W + "val")?.Value ?? "0";
            var level = int.TryParse(numbering.Element(W + "ilvl")?.Attribute(W + "val")?.Value, out var n) ? n : 0;
            var ordered = context.NumberingIsOrdered.TryGetValue((numId, level), out var o) && o;
            builder.Append(' ', level * 2).Append(ordered ? "1. " : "- ").Append(text.TrimStart()).Append('\n');
            return;
        }

        EnsureBlankLine(builder);
        builder.Append(text).Append("\n\n");
    }

    private static void RenderTable(XElement table, Context context, StringBuilder builder)
    {
        var grid = new List<IReadOnlyList<string>>();
        foreach (var row in table.Elements(W + "tr"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements(W + "tc"))
            {
                // Nested tables are flattened into the cell.
                var parts = cell.Descendants(W + "p")
                    .Where(p => !p.Ancestors(W + "txbxContent").Any())
                    .Select(p => RenderRuns(p, context).Trim())
                    .Where(t => t.Length > 0);
                cells.Add(string.Join("<br>", parts));
            }

            grid.Add(cells);
        }

        if (grid.All(r => r.All(c => c.Length == 0)))
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.Append(MarkdownText.Table(grid));
    }

    private static string RenderRuns(XElement container, Context context)
    {
        var segments = new List<(string Text, string Mark)>();
        RenderInline(container, context, segments);
        return Emit(segments);
    }

    // Adjacent runs with the same emphasis are merged, because Word splits runs mid-word.
    private static string Emit(List<(string Text, string Mark)> segments)
    {
        var builder = new StringBuilder();
        var i = 0;
        while (i < segments.Count)
        {
            var mark = segments[i].Mark;
            var buffer = new StringBuilder();
            while (i < segments.Count && (segments[i].Mark == mark || (segments[i].Text.Trim().Length == 0 && mark.Length > 0)))
            {
                buffer.Append(segments[i++].Text);
            }

            var text = buffer.ToString();
            if (mark.Length == 0 || text.Trim().Length == 0)
            {
                builder.Append(text);
                continue;
            }

            // Surrounding whitespace stays outside the emphasis markers.
            var lead = text[..(text.Length - text.TrimStart().Length)];
            var trail = text[text.TrimEnd().Length..];
            builder.Append(lead).Append(mark).Append(text.Trim()).Append(mark).Append(trail);
        }

        return builder.ToString();
    }

    private static void RenderInline(XElement parent, Context context, List<(string Text, string Mark)> segments)
    {
        foreach (var node in parent.Elements())
        {
            if (node.Name == W + "r")
            {
                RenderRun(node, context, segments);
            }
            else if (node.Name == W + "hyperlink")
            {
                var inner = new List<(string, string)>();
                RenderInline(node, context, inner);
                var text = Emit(inner);
                var id = node.Attribute(R + "id")?.Value;
                if (id is not null && context.Rels.TryGetValue(id, out var target) && text.Trim().Length > 0)
                {
                    segments.Add(($"[{text}]({MarkdownText.Destination(target)})", string.Empty));
                }
                else
                {
                    segments.Add((text, string.Empty));
                }
            }
            else if (node.Name == W + "sdt")
            {
                var content = node.Element(W + "sdtContent");
                if (content is not null)
                {
                    RenderInline(content, context, segments);
                }
            }
            else if (node.Name == W + "smartTag" || node.Name == W + "ins" || node.Name == W + "fldSimple"
                     || node.Name == W + "customXml" || node.Name == W + "moveTo")
            {
                RenderInline(node, context, segments);
            }
        }
    }

    private static void RenderRun(XElement run, Context context, List<(string Text, string Mark)> segments)
    {
        var properties = run.Element(W + "rPr");
        var bold = IsOn(properties?.Element(W + "b"));
        var italic = IsOn(properties?.Element(W + "i"));

        var content = new StringBuilder();
        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t")
            {
                content.Append(MarkdownText.Escape(child.Value));
            }
            else if (child.Name == W + "tab")
            {
                content.Append('\t');
            }
            else if (child.Name == W + "br" || child.Name == W + "cr")
            {
                content.Append(HardBreak);
            }
            else if (child.Name == W + "noBreakHyphen")
            {
                content.Append('-');
            }
        }

        if (content.Length > 0)
        {
            segments.Add((content.ToString(), bold && italic ? "***" : bold ? "**" : italic ? "*" : string.Empty));
        }

        if (context.Images is not null)
        {
            foreach (var image in Images(run, context))
            {
                segments.Add((image, string.Empty));
            }
        }
    }

    // DrawingML pictures (a:blip) and legacy VML pictures (v:imagedata); text boxes are rendered separately.
    private static IEnumerable<string> Images(XElement run, Context context)
    {
        foreach (var element in run.Descendants())
        {
            if (element.Ancestors(MC + "Fallback").Any() || element.Ancestors(W + "txbxContent").Any())
            {
                continue;
            }

            string? id;
            string? alt;
            if (element.Name == A + "blip")
            {
                id = element.Attribute(R + "embed")?.Value;
                var properties = element.Ancestors(W + "drawing").Descendants(WP + "docPr").FirstOrDefault();
                alt = properties?.Attribute("descr")?.Value ?? properties?.Attribute("title")?.Value;
            }
            else if (element.Name == V + "imagedata")
            {
                id = element.Attribute(R + "id")?.Value;
                alt = element.Parent?.Attribute("alt")?.Value ?? element.Attribute("title")?.Value;
            }
            else
            {
                continue;
            }

            if (id is null || !context.Rels.TryGetValue(id, out var part) || context.Package.ReadBytes(part) is not { } bytes)
            {
                continue;
            }

            var path = context.Images!.Add(part[(part.LastIndexOf('/') + 1)..], bytes);
            yield return $"![{MarkdownText.Escape(alt ?? string.Empty).Replace('\n', ' ')}]({MarkdownText.Destination(path)})";
        }
    }

    private static void EnsureBlankLine(StringBuilder builder)
    {
        if (builder.Length == 0 || (builder.Length >= 2 && builder[^1] == '\n' && builder[^2] == '\n'))
        {
            return;
        }

        if (builder[^1] != '\n')
        {
            builder.Append('\n');
        }

        builder.Append('\n');
    }

    private static bool IsOn(XElement? toggle)
    {
        if (toggle is null)
        {
            return false;
        }

        var value = toggle.Attribute(W + "val")?.Value;
        return value is null or "1" or "true" or "on";
    }

    // Style ids and names are localized; the German ones are matched as well.
    private static string ClassifyHeading(string id, string name)
    {
        var probe = (id + " " + name).ToLowerInvariant();
        if (probe.Contains("subtitle", StringComparison.Ordinal) || probe.Contains("untertitel", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (probe.Contains("title", StringComparison.Ordinal) || probe.Contains("titel", StringComparison.Ordinal))
        {
            return "title";
        }

        var isHeading = probe.Contains("heading", StringComparison.Ordinal) || probe.Contains("berschrift", StringComparison.Ordinal);
        if (!isHeading || probe.Contains("toc", StringComparison.Ordinal) || probe.Contains("verzeichnis", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        for (var level = 1; level <= 6; level++)
        {
            if (probe.Contains(level.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return "h" + level;
            }
        }

        return "h1";
    }
}
