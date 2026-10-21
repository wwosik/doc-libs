using System.Globalization;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig.Core;
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
    private readonly Dictionary<FontFace, List<FontSlot>> slots = [];
    private readonly Dictionary<object, PdfPageBuilder.AddedImage> images = new(ReferenceEqualityComparer.Instance);

    private PdfRenderer(FontSet fonts)
    {
        this.fonts = fonts;
    }

    public static byte[] Render(Layout layout, FontSet fonts, string? title, string? author)
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

        return FixXmp(FlagAnnotations(document.Build()));
    }

    private const string Creator = "wwosik.DocLibs.DocumentGeneration.Pdf";
    private const string Producer = "wwosik.DocLibs.DocumentGeneration.Pdf (PdfPig)";

    // PdfPig writes dc:title and dc:creator as plain text; PDF/A wants a language alternative and an ordered list.
    // The packet is an uncompressed stream, so it is rewritten in place: without PdfPig's indentation and its
    // optional toolkit name (x:xmptk) the fixed XML is shorter whatever the title, and the rest is filled with the
    // padding XMP allows, so no offset in the file moves.
    private static byte[] FixXmp(byte[] pdf)
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
        foreach (var (name, container, language) in new[] { (dc + "title", rdf + "Alt", true), (dc + "creator", rdf + "Seq", false) })
        {
            foreach (var property in xml.Descendants(name).Where(e => !e.HasElements).ToList())
            {
                var item = new XElement(rdf + "li", property.Value);
                if (language)
                {
                    item.SetAttributeValue(XNamespace.Xml + "lang", "x-default");
                }

                property.ReplaceNodes(new XElement(container, item));
            }
        }

        var replacement = Encoding.UTF8.GetBytes(xml.Root!.ToString(SaveOptions.DisableFormatting));
        var room = end - header;
        if (replacement.Length + 1 > room)
        {
            throw new InvalidOperationException($"The corrected XMP ({replacement.Length} bytes) does not fit into PdfPig's packet ({room} bytes).");
        }

        replacement.CopyTo(span[header..]);
        span[(header + replacement.Length)..end].Fill((byte)' ');
        span[end - 1] = (byte)'\n';
        return pdf;
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
        if (!this.slots.TryGetValue(face, out var list))
        {
            list = [];
            this.slots[face] = list;
        }

        var slot = list.FirstOrDefault(s => s.Characters.Contains(character)) ?? list.FirstOrDefault(s => s.Characters.Count < CharactersPerFont);
        if (slot is null)
        {
            slot = new FontSlot(this.builder.AddTrueTypeFont(this.fonts.File(face)));
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
