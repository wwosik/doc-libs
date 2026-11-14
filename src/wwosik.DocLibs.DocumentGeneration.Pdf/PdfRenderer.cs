using System.Globalization;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Core;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.ClippingPaths;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf;

/// <summary>Draws a <see cref="Layout"/> through PdfPig's <see cref="PdfDocumentBuilder"/> as PDF/A-2b.</summary>
internal sealed class PdfRenderer
{
    // PdfPig writes TrueType fonts as simple fonts, which hold at most 255 distinct characters.
    private const int CharactersPerFont = 255;

    private readonly PdfDocumentBuilder builder = new() { ArchiveStandard = PdfAStandard.A2B };
    private readonly FontSet fonts;
    // By file, not by face: faces sharing a file (tables without fonts of their own) share its embedded copies.
    private readonly Dictionary<byte[], List<FontSlot>> slots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, PdfPageBuilder.AddedImage> images = new(ReferenceEqualityComparer.Instance);

    private PdfRenderer(FontSet fonts)
    {
        this.fonts = fonts;
    }

    /// <remarks><paramref name="language"/> is the document's language tag, for the catalog's <c>/Lang</c> and the XMP.</remarks>
    public static byte[] Render(Layout layout, FontSet fonts, string? title, string? author, string language = "en")
    {
        var renderer = new PdfRenderer(fonts);
        var document = renderer.builder;
        var info = document.DocumentInformation;
        info.Title = title;
        info.Author = author;
        info.Creator = Creator;
        info.Producer = Producer;

        var pages = layout.Pages.Select(_ => document.AddPage(layout.Geometry.Width, layout.Geometry.Height)).ToList();
        for (var i = 0; i < layout.Pages.Count; i++)
        {
            foreach (var item in layout.Pages[i].Items)
            {
                renderer.Draw(pages[i], item, layout);
            }
        }

        if (layout.Bookmarks.Count > 0)
        {
            document.Bookmarks = new Bookmarks(Nest(layout.Bookmarks, layout.Geometry));
        }

        return AddLanguage(FixXmp(FlagAnnotations(document.Build()), language), language);
    }

    private const string Creator = "wwosik.DocLibs.DocumentGeneration.Pdf";
    private const string Producer = "wwosik.DocLibs.DocumentGeneration.Pdf (PdfPig)";

    // PdfPig writes dc:title and dc:creator as plain text; PDF/A wants a language alternative and an ordered list. The
    // document language goes next to them. The packet is an uncompressed stream: rewritten in place, padded with the
    // white space XMP allows, where the fixed XML (without PdfPig's indentation and its optional toolkit name) fits;
    // else the stream grows, and its /Length and the offsets behind it with it.
    private static byte[] FixXmp(byte[] pdf, string language)
    {
        var span = pdf.AsSpan();
        var begin = span.IndexOf("<?xpacket begin"u8);
        var end = span.IndexOf("<?xpacket end"u8);
        if (begin < 0 || end < begin)
        {
            throw new InvalidOperationException("PdfPig wrote no XMP packet.");
        }

        var header = span[begin..].IndexOf("?>"u8) + begin + 2;
        var xml = XDocument.Parse(Encoding.UTF8.GetString(pdf, header, end - header));
        xml.Root!.Attribute((XNamespace)"adobe:ns:meta/" + "xmptk")?.Remove();
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        foreach (var (name, container, isAlternative) in new[] { (dc + "title", rdf + "Alt", true), (dc + "creator", rdf + "Seq", false) })
        {
            foreach (var property in xml.Descendants(name).Where(e => !e.HasElements).ToList())
            {
                var item = new XElement(rdf + "li", property.Value);
                if (isAlternative)
                {
                    item.SetAttributeValue(XNamespace.Xml + "lang", "x-default");
                }

                property.ReplaceNodes(new XElement(container, item));
            }
        }

        if (xml.Descendants(dc + "title").FirstOrDefault()?.Parent is { } description && !description.Elements(dc + "language").Any())
        {
            description.Add(new XElement(dc + "language", new XElement(rdf + "Bag", new XElement(rdf + "li", language))));
        }

        var replacement = Encoding.UTF8.GetBytes(xml.Root!.ToString(SaveOptions.DisableFormatting));
        var room = end - header;
        if (replacement.Length + 1 <= room)
        {
            replacement.CopyTo(span[header..]);
            span[(header + replacement.Length)..end].Fill((byte)' ');
            span[end - 1] = (byte)'\n';
            return pdf;
        }

        var grown = replacement.Length + 1 - room;
        pdf = Splice(pdf, header, room, [.. replacement, (byte)'\n']);

        // The stream's /Length, in its dictionary just before the packet.
        var lengthKey = "/Length "u8;
        var key = pdf.AsSpan(0, begin).LastIndexOf(lengthKey) + lengthKey.Length;
        var digits = pdf.AsSpan(key).IndexOfAnyExceptInRange((byte)'0', (byte)'9');
        var length = int.Parse(Encoding.ASCII.GetString(pdf, key, digits), CultureInfo.InvariantCulture) + grown;
        return Splice(pdf, key, digits, Encoding.ASCII.GetBytes(length.ToString(CultureInfo.InvariantCulture)));
    }

    // The catalog gets `/Lang (tag)`, which PdfPig cannot write, right after its `/Type /Catalog`.
    private static byte[] AddLanguage(byte[] pdf, string language)
    {
        var marker = "/Type /Catalog"u8;
        var at = pdf.AsSpan().IndexOf(marker);
        if (at < 0)
        {
            throw new InvalidOperationException("PdfPig wrote no catalog.");
        }

        return Splice(pdf, at + marker.Length, 0, Encoding.ASCII.GetBytes($" /Lang ({language})"));
    }

    // Replaces `length` bytes at `start` of an uncompressed object. Every offset behind it in the cross-reference table
    // ("nnnnnnnnnn ggggg n", fixed width, rewritten in place) and the table's own offset after `startxref` move.
    private static byte[] Splice(byte[] pdf, int start, int length, byte[] replacement)
    {
        var delta = replacement.Length - length;
        byte[] result = [.. pdf.AsSpan(0, start), .. replacement, .. pdf.AsSpan(start + length)];
        if (delta == 0)
        {
            return result;
        }

        var text = Encoding.Latin1.GetString(result);
        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var table = startxref < 0 ? -1 : text.LastIndexOf("\nxref", startxref, StringComparison.Ordinal) + 1;
        if (table <= 0 || table < start)
        {
            throw new InvalidOperationException("PdfPig wrote no cross-reference table after the objects.");
        }

        var span = result.AsSpan();
        var line = text.IndexOf('\n', table) + 1;
        while (line < startxref && text.IndexOf('\n', line) is var next and > 0)
        {
            var row = text[line..next].TrimEnd('\r', ' ');
            if (row.Length == 18 && row[17] == 'n' && long.TryParse(row[..10], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset > start)
            {
                Encoding.ASCII.GetBytes((offset + delta).ToString("D10", CultureInfo.InvariantCulture)).CopyTo(span[line..]);
            }

            line = next + 1;
        }

        var numberStart = text.IndexOf('\n', startxref) + 1;
        var numberEnd = text.IndexOf('\n', numberStart);
        var moved = (long.Parse(text[numberStart..numberEnd].Trim(), CultureInfo.InvariantCulture) + delta).ToString(CultureInfo.InvariantCulture);
        return [.. result.AsSpan(0, numberStart), .. Encoding.ASCII.GetBytes(moved), .. result.AsSpan(numberEnd)];
    }

    // PDF/A wants every annotation printable (/F 4), and PdfPig writes no flags. Its link annotations are inline,
    // uncompressed dictionaries in the page's /Annots array, so the optional "/Type /Annot" is swapped for the flag,
    // padded to the same length: no offset in the cross-reference table moves.
    private static byte[] FlagAnnotations(byte[] pdf)
    {
        var from = Encoding.ASCII.GetBytes("<</Type /Annot /Subtype /Link ");
        var to = Encoding.ASCII.GetBytes("<</F 4 /Subtype /Link ".PadRight(from.Length));
        var annots = Encoding.ASCII.GetBytes("/Annots [");
        var span = pdf.AsSpan();
        var start = 0;
        while (span[start..].IndexOf(annots) is var found and >= 0)
        {
            var i = start + found + annots.Length;
            for (var depth = 1; i < pdf.Length && depth > 0; i++)
            {
                if (pdf[i] == (byte)'[')
                {
                    depth++;
                }
                else if (pdf[i] == (byte)']')
                {
                    depth--;
                }
                else if (span[i..].StartsWith(from))
                {
                    to.CopyTo(span[i..]);
                }
            }

            start = i;
        }

        return pdf;
    }

    private void Draw(PdfPageBuilder page, PageItem item, Layout layout)
    {
        var height = layout.Geometry.Height;
        switch (item)
        {
            case TextItem text:
                SetFill(page, text.Color);
                this.Text(page, text.Text, text.Face, text.Size, new PdfPoint(text.X, height - text.Baseline));
                break;
            case RectItem rect:
                SetFill(page, rect.Color);
                var (r, g, b) = Rgb(rect.Color);
                page.SetStrokeColor(r, g, b);
                page.DrawRectangle(new PdfPoint(rect.X, height - rect.Y - rect.Height), rect.Width, rect.Height, 0.1, fill: true);
                break;
            case LineItem line:
                var (lr, lg, lb) = Rgb(line.Color);
                page.SetStrokeColor(lr, lg, lb);
                page.DrawLine(new PdfPoint(line.X1, height - line.Y1), new PdfPoint(line.X2, height - line.Y2), line.Width);
                break;
            case ImageItem image:
                var area = new PdfRectangle(image.X, height - image.Y - image.Height, image.X + image.Width, height - image.Y);
                if (image.Clip is { } clip)
                {
                    Clip(page, clip, height);
                }

                if (this.images.TryGetValue(image.Key, out var added))
                {
                    page.AddImage(added, area);
                }
                else
                {
                    var bytes = image.Content.ToArray();
                    try
                    {
                        this.images[image.Key] = image.Format == ImageFormat.Png ? page.AddPng(bytes, area) : page.AddJpeg(bytes, area);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        var name = image.Key is DocumentResource resource ? resource.Path : "a built-in image";
                        throw new DocumentConversionException(DocumentFormat.Pdf, $"The image '{name}' could not be read: {ex.Message}", ex);
                    }
                }

                if (image.Clip is not null)
                {
                    page.CurrentStream.Operations.Add(Pop.Value);
                }

                break;
            case ShapeItem shape:
                DrawShape(page, shape, height);
                break;
            case LinkItem link:
                var linkArea = new PdfRectangle(link.X, height - link.Y - link.Height, link.X + link.Width, height - link.Y);
                if (link.Target.StartsWith('#'))
                {
                    if (layout.Anchors.TryGetValue(link.Target[1..], out var target))
                    {
                        page.AddLink(Destination(target.Page, target.Y, layout.Geometry), linkArea);
                    }
                }
                else if (Uri.TryCreate(link.Target, UriKind.Absolute, out _))
                {
                    page.AddLink(link.Target, linkArea);
                }

                break;
        }
    }

    // Opens q and narrows what is drawn next to the rectangle, or the ellipse in it (four curves, as shapes draw it);
    // the image is drawn into it and Q follows.
    private static void Clip(PdfPageBuilder page, ImageClip clip, double pageHeight)
    {
        var operations = page.CurrentStream.Operations;
        operations.Add(Push.Value);
        var (left, bottom) = (clip.X, pageHeight - clip.Y - clip.Height);
        if (clip.Round)
        {
            var (rx, ry) = (clip.Width / 2, clip.Height / 2);
            var (cx, cy) = (left + rx, bottom + ry);
            var (kx, ky) = (rx * 0.5523, ry * 0.5523);
            operations.Add(new BeginNewSubpath(cx + rx, cy));
            operations.Add(new AppendDualControlPointBezierCurve(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry));
            operations.Add(new AppendDualControlPointBezierCurve(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy));
            operations.Add(new AppendDualControlPointBezierCurve(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry));
            operations.Add(new AppendDualControlPointBezierCurve(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy));
            operations.Add(CloseSubpath.Value);
        }
        else
        {
            operations.Add(new AppendRectangle(left, bottom, clip.Width, clip.Height));
        }

        operations.Add(ModifyClippingByNonZeroWindingIntersect.Value);
        operations.Add(EndPath.Value);
    }

    // PdfPig has no API for curves, so the path operators go straight into the page's content stream, inside q … Q
    // so the colours and line state set here do not leak into what PdfPig writes next.
    private static void DrawShape(PdfPageBuilder page, ShapeItem shape, double pageHeight)
    {
        var stroke = shape.Stroke is not null && shape.Width > 0;
        if ((shape.Fill is null && !stroke) || shape.Path.Count == 0)
        {
            return;
        }

        var operations = page.CurrentStream.Operations;
        operations.Add(Push.Value);
        if (shape.Fill is { } fill)
        {
            var (r, g, b) = Rgb(fill);
            operations.Add(new SetNonStrokeColorDeviceRgb(r / 255.0, g / 255.0, b / 255.0));
        }

        if (stroke)
        {
            var (r, g, b) = Rgb(shape.Stroke!);
            operations.Add(new SetStrokeColorDeviceRgb(r / 255.0, g / 255.0, b / 255.0));
            operations.Add(new SetLineWidth(shape.Width));
            operations.Add(new SetLineJoin(shape.RoundJoins ? LineJoinStyle.Round : LineJoinStyle.Miter));
            operations.Add(new SetLineCap(shape.RoundJoins ? LineCapStyle.Round : LineCapStyle.Butt));
            if (shape.Dash is { Count: > 0 } dash)
            {
                operations.Add(new SetLineDashPattern([.. dash], 0));
            }
        }

        foreach (var segment in shape.Path)
        {
            operations.Add(segment switch
            {
                MoveTo m => new BeginNewSubpath(m.X, pageHeight - m.Y),
                LineTo l => new AppendStraightLineSegment(l.X, pageHeight - l.Y),
                CubicTo c => new AppendDualControlPointBezierCurve(c.X1, pageHeight - c.Y1, c.X2, pageHeight - c.Y2, c.X, pageHeight - c.Y),
                _ => CloseSubpath.Value,
            });
        }

        var evenOdd = shape.FillRule == DrawingFillRule.EvenOdd;
        operations.Add(shape.Fill is null ? StrokePath.Value
            : stroke ? evenOdd ? FillEvenOddAndStroke.Value : FillPathNonZeroWindingAndStroke.Value
            : evenOdd ? FillPathEvenOddRule.Value : FillPathNonZeroWinding.Value);
        operations.Add(Pop.Value);
    }

    // Writes the text segment by segment, each in a copy of the font that still has room for its characters.
    private void Text(PdfPageBuilder page, string text, FontFace face, double size, PdfPoint start)
    {
        var x = start.X;
        var segment = new System.Text.StringBuilder();
        FontSlot? slot = null;
        foreach (var character in text)
        {
            var next = this.SlotFor(face, character);
            if (slot is not null && next != slot && segment.Length > 0)
            {
                x = Write(page, segment.ToString(), slot, size, x, start.Y);
                segment.Clear();
            }

            slot = next;
            slot.Characters.Add(character);
            segment.Append(character);
        }

        if (slot is not null && segment.Length > 0)
        {
            Write(page, segment.ToString(), slot, size, x, start.Y);
        }
    }

    private static double Write(PdfPageBuilder page, string text, FontSlot slot, double size, double x, double y)
    {
        var letters = page.AddText(text, size, new PdfPoint(x, y), slot.Font);
        return letters.Count == 0 ? x : letters[^1].EndBaseLine.X;
    }

    private FontSlot SlotFor(FontFace face, char character)
    {
        var file = this.fonts.File(face);
        if (!this.slots.TryGetValue(file, out var list))
        {
            list = [];
            this.slots[file] = list;
        }

        var slot = list.FirstOrDefault(s => s.Characters.Contains(character)) ?? list.FirstOrDefault(s => s.Characters.Count < CharactersPerFont);
        if (slot is null)
        {
            slot = new FontSlot(this.builder.AddTrueTypeFont(file));
            list.Add(slot);
        }

        return slot;
    }

    // Bookmarks nested by level: each one goes under the nearest earlier one with a lower level.
    private static List<BookmarkNode> Nest(IReadOnlyList<Bookmark> bookmarks, PageGeometry geometry)
    {
        var roots = new List<BookmarkNode>();
        var levels = new List<int>();
        var nodes = new List<(Bookmark Bookmark, List<BookmarkNode> Children, int Depth)>();
        foreach (var bookmark in bookmarks)
        {
            while (levels.Count > 0 && levels[^1] >= bookmark.Level)
            {
                levels.RemoveAt(levels.Count - 1);
            }

            nodes.Add((bookmark, [], levels.Count));
            levels.Add(bookmark.Level);
        }

        // Built bottom-up, because a node takes its children when it is created.
        var built = new BookmarkNode?[nodes.Count];
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var (bookmark, children, depth) = nodes[i];
            var node = new DocumentBookmarkNode(bookmark.Title, depth, Destination(bookmark.Page, bookmark.Y, geometry), children);
            built[i] = node;
            var parent = Enumerable.Range(0, i).Reverse().Where(j => nodes[j].Depth == depth - 1).Select(j => (int?)j).FirstOrDefault();
            if (depth == 0 || parent is null)
            {
                roots.Insert(0, node);
            }
            else
            {
                nodes[parent.Value].Children.Insert(0, node);
            }
        }

        return roots;
    }

    private static ExplicitDestination Destination(int page, double y, PageGeometry geometry) =>
        new(page + 1, ExplicitDestinationType.XyzCoordinates, new ExplicitDestinationCoordinates(geometry.Left, geometry.Height - y + 4));

    private static void SetFill(PdfPageBuilder page, string color)
    {
        var (r, g, b) = Rgb(color);
        page.SetTextAndFillColor(r, g, b);
    }

    private static (byte R, byte G, byte B) Rgb(string hex) => (
        byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private sealed class FontSlot(PdfDocumentBuilder.AddedFont font)
    {
        public PdfDocumentBuilder.AddedFont Font { get; } = font;

        public HashSet<char> Characters { get; } = [];
    }
}

/// <summary><c>B*</c>: fills the path by the even-odd rule, then strokes it; PdfPig has the operator only with a close.</summary>
internal sealed class FillEvenOddAndStroke : IGraphicsStateOperation
{
    public static FillEvenOddAndStroke Value { get; } = new();

    public string Operator => "B*";

    public void Run(UglyToad.PdfPig.Graphics.IOperationContext operationContext) => throw new NotSupportedException("Written only.");

    public void Write(Stream stream)
    {
        stream.Write("B*"u8);
        stream.WriteByte((byte)'\n');
    }
}
