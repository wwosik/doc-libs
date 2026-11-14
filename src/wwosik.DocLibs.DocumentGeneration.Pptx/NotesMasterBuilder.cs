using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// The notes master for decks whose template has none, laid out as PowerPoint lays out its own on the portrait notes
/// page (6858000 × 9144000): header and date at the top, the slide image, the speaker notes in 12 pt below it, footer
/// and slide number at the foot. Its fonts and colours are the theme's: the brand's on the built-in master. Written as
/// XML here so it can be reviewed like code.
/// </summary>
internal static class NotesMasterBuilder
{
    // The notes page and the boxes on it, in EMU.
    private const long PageWidth = 6858000;
    private const long PageHeight = 9144000;
    private const long HalfWidth = 2971800;
    private const long BandHeight = 458788;
    private const long RightColumn = PageWidth - HalfWidth;
    private const long ImageLeft = 685800;
    private const long ImageTop = 1143000;
    private const long ImageWidth = PageWidth - (2 * ImageLeft);
    private const long BodyBottom = PageHeight - BandHeight - 228600;
    private const int NotesSize = 1200;

    private const string Namespaces =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
        + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
        + "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private const string TextFont = "<a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/>";

    /// <summary>
    /// Writes the notes master into <paramref name="part"/>, with a slide image in the deck's proportions
    /// (<paramref name="slideWidth"/> × <paramref name="slideHeight"/>). The part needs its theme already.
    /// </summary>
    public static void Write(NotesMasterPart part, long slideWidth, long slideHeight)
    {
        var imageHeight = slideWidth > 0 ? ImageWidth * slideHeight / slideWidth : ImageWidth * 9 / 16;
        var bodyTop = ImageTop + imageHeight + 228600;
        var xml =
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:notesMaster {Namespaces}><p:cSld>"
            + "<p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>"
            + "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
            + "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>"
            + Band(2, "Header Placeholder 1", "<p:ph type=\"hdr\" sz=\"quarter\"/>", 0, 0, "l", "t", null)
            + Band(3, "Date Placeholder 2", "<p:ph type=\"dt\" idx=\"1\"/>", RightColumn, 0, "r", "t", null)
            + "<p:sp><p:nvSpPr><p:cNvPr id=\"4\" name=\"Slide Image Placeholder 3\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr>"
            + "<p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr>"
            + $"<p:spPr>{Box(ImageLeft, ImageTop, ImageWidth, imageHeight)}<a:noFill/><a:ln w=\"12700\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></p:spPr></p:sp>"
            + "<p:sp><p:nvSpPr><p:cNvPr id=\"5\" name=\"Notes Placeholder 4\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr>"
            + "<p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr>"
            + $"<p:spPr>{Box(ImageLeft, bodyTop, ImageWidth, Math.Max(BodyBottom - bodyTop, BandHeight))}</p:spPr>"
            + "<p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\"/><a:lstStyle/>"
            + "<a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Click to edit Master text styles</a:t></a:r></a:p></p:txBody></p:sp>"
            + Band(6, "Footer Placeholder 5", "<p:ph type=\"ftr\" sz=\"quarter\" idx=\"4\"/>", 0, PageHeight - BandHeight, "l", "b", null)
            + Band(7, "Slide Number Placeholder 6", "<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"5\"/>", RightColumn, PageHeight - BandHeight, "r", "b",
                $"<a:fld id=\"{TemplateBuilder.SlideNumberFieldId}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld>")
            + "</p:spTree></p:cSld>"
            + "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" "
            + "accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>"
            + "<p:notesStyle>" + string.Concat(Enumerable.Range(1, 9).Select(Level)) + "</p:notesStyle>"
            + "</p:notesMaster>";
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(xml));
        part.FeedData(stream);
    }

    // Header, date, footer or slide number: a band half the page wide, in small text.
    private static string Band(int id, string name, string ph, long x, long y, string align, string anchor, string? field) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr>{ph}</p:nvPr></p:nvSpPr>"
        + $"<p:spPr>{Box(x, y, HalfWidth, BandHeight)}</p:spPr>"
        + $"<p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\" anchor=\"{anchor}\"/>"
        + $"<a:lstStyle><a:lvl1pPr algn=\"{align}\"><a:defRPr sz=\"1200\"/></a:lvl1pPr></a:lstStyle>"
        + $"<a:p>{field}<a:endParaRPr lang=\"en-US\"/></a:p></p:txBody></p:sp>";

    private static string Box(long x, long y, long cx, long cy) =>
        $"<a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>";

    // Each level indented by half an inch, as on the notes pages PowerPoint writes; lists bring their own bullets.
    private static string Level(int n) =>
        $"<a:lvl{n}pPr marL=\"{((n - 1) * 457200L).ToString(CultureInfo.InvariantCulture)}\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">"
        + $"<a:defRPr sz=\"{NotesSize}\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill>{TextFont}</a:defRPr></a:lvl{n}pPr>";
}
