using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using WordDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The cover image (<c>coverImage</c>, E01-S08) in Word: with <c>below</c> an inline picture in a paragraph of its own
/// under the subtitle, left-aligned like the title; with <c>right</c> a picture anchored in the title's paragraph at the
/// right margin, top-aligned with the title at 28 % of the page, the title and subtitle wrapping left of it. An SVG goes
/// in as a PNG with the SVG beside it, which Word 2016 and later draw.
/// </summary>
internal static partial class DocxCoverImage
{
    private const long EmuPerTwip = DocxPage.EmuPerTwip;

    // Between the picture and the text wrapped beside it: 1/8 inch.
    private const long WrapGap = 114300;

    /// <summary>The picture as a run: inline for <c>below</c>, anchored for <c>right</c>.</summary>
    /// <remarks>Its drawing id follows those of <paramref name="body"/>, the body written so far.</remarks>
    public static Run Picture(CoverImage image, DocxContext context, IEnumerable<OpenXmlElement> body)
    {
        var page = context.Page;
        var heightInEmu = page.Height * EmuPerTwip * image.HeightShare;
        var (placeable, _, width, height, svg) = image.Placeable(
            context.Options.ImageConverters, heightInEmu / 9525, Format.Docx, ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif);
        var ratio = width / Math.Max(1, height);
        var right = image.Position == CoverImagePosition.Right;
        var maxWidth = page.TextWidth * EmuPerTwip * (right ? 0.4 : 1.0);
        var cy = Math.Min(heightInEmu, maxWidth / ratio);
        var size = $"cx=\"{N((long)(cy * ratio))}\" cy=\"{N((long)cy)}\"";

        var main = context.Main;
        var info = ImageInfo.Read(placeable.Content.Span)!.Value;
        var pngId = Add(main, info.Format switch { ImageFormat.Jpeg => ImagePartType.Jpeg, ImageFormat.Gif => ImagePartType.Gif, _ => ImagePartType.Png }, placeable.Content);
        var svgId = svg is null ? null : Add(main, ImagePartType.Svg, svg.Content);
        var id = N(NextDrawingId(main, body));
        var name = "Cover Image " + id;
        var description = SecurityElement.Escape(Path.GetFileNameWithoutExtension(image.Resource.Path));
        var graphic = "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>"
            + $"<pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{name}\" descr=\"{description}\"/><pic:cNvPicPr/></pic:nvPicPr>"
            + $"<pic:blipFill><a:blip r:embed=\"{pngId}\">{(svgId is null ? string.Empty : SvgBlip.Extension(svgId))}</a:blip><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext {size}/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            + "</pic:pic></a:graphicData></a:graphic>";
        var common = $"<wp:extent {size}/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>";
        var frame = $"<wp:docPr id=\"{id}\" name=\"{name}\" descr=\"{description}\"/>"
            + "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" + graphic;
        var placement = right
            ? $"<wp:anchor distT=\"0\" distB=\"0\" distL=\"{N(WrapGap)}\" distR=\"0\" simplePos=\"0\" relativeHeight=\"251659264\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"0\">"
                + "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"margin\"><wp:align>right</wp:align></wp:positionH>"
                + $"<wp:positionV relativeFrom=\"page\"><wp:posOffset>{N((long)(page.Height * 0.28) * EmuPerTwip)}</wp:posOffset></wp:positionV>"
                + common + "<wp:wrapSquare wrapText=\"left\"/>" + frame + "</wp:anchor>"
            : "<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" + common + frame + "</wp:inline>";
        return new Run(new WordDrawing(
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
            + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" "
            + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + placement + "</w:drawing>"));
    }

    private static string Add(MainDocumentPart main, PartTypeInfo type, ReadOnlyMemory<byte> content)
    {
        var part = main.AddImagePart(type);
        using (var stream = new MemoryStream(content.ToArray()))
        {
            part.FeedData(stream);
        }

        return main.GetIdOfPart(part);
    }

    // One more than every drawing id in the body written so far and in the template's headers and footers.
    private static long NextDrawingId(MainDocumentPart main, IEnumerable<OpenXmlElement> body)
    {
        var texts = body.Select(e => e.OuterXml).ToList();
        foreach (var part in main.HeaderParts.Cast<OpenXmlPart>().Concat(main.FooterParts))
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            texts.Add(reader.ReadToEnd());
        }

        return 1 + texts.SelectMany(t => DocPrIdPattern().Matches(t)).Select(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"<(?:wp:docPr|pic:cNvPr)\b[^>]*\sid=""([0-9]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex DocPrIdPattern();
}
