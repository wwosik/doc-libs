using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using WordDrawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// What code-block renderers, <c>qr:</c> images, drawn containers and icons put into a Word document: a chart becomes a
/// native Word chart (<see cref="ChartWriter"/>, its data in an embedded workbook); every drawing becomes a picture, the
/// PNG an image converter makes of its SVG (<see cref="DrawingSvg"/>) with the SVG beside it (<see cref="SvgBlip"/>).
/// Without an image converter a drawing stays what it was (code, alt text, content) and an icon is left out, each with a
/// note: the package itself draws no pixels.
/// </summary>
internal sealed partial class DocxBody
{
    // Alt text taken from a block's first line is cut here.
    private const int AltTextLength = 120;

    // An icon is as high as a line of body text is large: 10.5 pt.
    private const long IconSize = 10 * 12700 + (12700 / 2);

    private const string ConverterAdvice = "Register SvgImageConverter from wwosik.DocLibs.DocumentConversions.Svg to draw diagrams in Word.";

    private readonly Dictionary<(OpenXmlPart, string), (string Png, string Svg)> icons = [];

    /// <summary>
    /// The picture of <paramref name="icon"/>, one line high, in the diagram text colour; <see langword="null"/>, with an
    /// <c>icon-not-drawn</c> note, when no image converter draws it.
    /// </summary>
    public Run? IconRun(InlineIcon icon, OpenXmlPart part)
    {
        if (!this.icons.TryGetValue((part, icon.Name), out var ids))
        {
            var colour = ResolvedBrand.Resolve(this.Context.Brand).Diagram.Text;
            if (this.DrawingParts(IconSyntax.Colour(icon.Drawing, colour), part) is not var (png, svg, _, _))
            {
                this.Context.Notes.Add(
                    NoteCodes.IconNotDrawn,
                    icon.Line,
                    $"The icon '{icon.Name}' is left out: drawing it in a Word document needs an image converter. {ConverterAdvice}");
                return null;
            }

            ids = (png, svg);
            this.icons[(part, icon.Name)] = ids;
        }

        return new Run(this.Picture(ids.Png, IconSize, IconSize, icon.Name, ids.Svg));
    }

    // A fenced block a renderer draws: a chart, a picture of a drawing or the renderer's image. False when it stays code:
    // no renderer takes it, or it is a drawing and no image converter makes a picture of it.
    private bool Graphic(FencedCodeBlock fenced, Frame frame, List<OpenXmlElement> target, bool captioned)
    {
        var renderers = this.Context.Options.CodeBlockRenderers;
        if (renderers.Count == 0)
        {
            return false;
        }

        var line = this.LineOf(fenced);
        var graphic = CodeBlockRenderers.Render(
            renderers,
            new CodeBlockRequest(fenced.Info ?? string.Empty, fenced.Lines.ToString(), this.Context.Brand)
            {
                Line = line,
                Report = (code, message) => this.Context.Notes.Add(code, line, message),
                Texts = this.Context.Texts,
            },
            Format.Docx);
        var alt = FirstLine(fenced.Lines.ToString()) ?? fenced.Info ?? string.Empty;
        var maxWidth = this.Context.Page.TextWidth * DocxPage.EmuPerTwip;
        Drawing drawing;
        switch (graphic)
        {
            case null:
                return false;
            case ChartGraphic chart when frame.Part is MainDocumentPart:
                this.Chart(chart.Chart, frame, target, captioned);
                return true;
            case ChartGraphic chart:
                // A footnote cannot hold a chart part: the chart's drawing instead.
                drawing = chart.Drawing;
                alt = chart.Chart.Title ?? this.Context.Texts.Chart;
                break;
            case ImageGraphic image:
                target.Add(this.PictureParagraph(frame, this.ResourcePicture(image.Image, 1.0, alt, maxWidth, frame.Part), captioned));
                return true;
            case DrawingGraphic drawn:
                drawing = drawn.Drawing;
                break;
            default:
                return false;
        }

        if (this.DrawingPicture(drawing, alt, maxWidth, frame.Part) is { } picture)
        {
            target.Add(this.PictureParagraph(frame, picture, captioned));
            return true;
        }

        this.Context.Notes.Add(
            NoteCodes.CodeNotDrawn,
            line,
            $"The '{fenced.Info}' block is shown as code: a drawing needs an image converter in a Word document. {ConverterAdvice}");
        return false;
    }

    // A native chart as wide as the text and 9/16 of that high, at most 80 % of the text height, in a centred paragraph;
    // its source under it in the caption style. Gridlines in the table line colour, text in the theme's colour and the
    // brand's family.
    private void Chart(ChartSpec chart, Frame frame, List<OpenXmlElement> target, bool captioned)
    {
        var brand = ResolvedBrand.Resolve(this.Context.Brand);
        var width = (long)(this.Context.Page.TextWidth * DocxPage.EmuPerTwip);
        var height = (long)Math.Min(width * 9.0 / 16, this.TextHeight * 0.8);
        var relationship = ChartWriter.Add(this.MainPart, chart, null, ChartWriter.Solid(brand.TableLine), brand.Fonts?.Family);
        var id = (++this.drawingId).ToString(CultureInfo.InvariantCulture);
        var drawing = new WordDrawing(
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
            + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" "
            + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            + "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" "
            + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{width.ToString(CultureInfo.InvariantCulture)}\" cy=\"{height.ToString(CultureInfo.InvariantCulture)}\"/>"
            + "<wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>"
            + $"<wp:docPr id=\"{id}\" name=\"Chart {id}\" descr=\"{SecurityElement.Escape(XmlSafe(chart.Title ?? this.Context.Texts.Chart))}\"/>"
            + "<wp:cNvGraphicFramePr/>"
            + $"<a:graphic><a:graphicData uri=\"{ChartWriter.GraphicUri}\"><c:chart r:id=\"{relationship}\"/></a:graphicData></a:graphic>"
            + "</wp:inline></w:drawing>");
        target.Add(this.PictureParagraph(frame, new Run(drawing), captioned || chart.Source is not null));
        if (chart.Source is { } source)
        {
            var paragraph = this.Paragraph(DocxRoles.Caption);
            if (captioned)
            {
                paragraph.ParagraphProperties!.Append(new KeepNext());
            }

            paragraph.Append(DocxWriter.Run($"{this.Context.Texts.Source}: {source}"));
            target.Add(paragraph);
        }
    }

    // `::: timeline` and the other drawn containers as a picture; false, with a note, when no image converter draws it,
    // so its content follows in sequence.
    private bool VisualContainer(CustomContainer container, Frame frame, List<OpenXmlElement> target)
    {
        var name = container.Info ?? string.Empty;
        var drawing = VisualContainers.Draw(
            container,
            new VisualContext(ResolvedBrand.Resolve(this.Context.Brand), VisualContext.TodayOf(this.Context.Options.TimeProvider), this.Context.Texts, this.LineOf, Format.Docx));
        var alt = this.FirstContentLine(container) ?? name;
        if (this.DrawingPicture(drawing, alt, this.Context.Page.TextWidth * DocxPage.EmuPerTwip, frame.Part) is { } picture)
        {
            target.Add(this.PictureParagraph(frame, picture, keepNext: false));
            return true;
        }

        this.Context.Notes.Add(
            NoteCodes.CodeNotDrawn,
            this.LineOf(container),
            $"The '::: {name}' is shown as its content: a drawing needs an image converter in a Word document. {ConverterAdvice}");
        return false;
    }

    // A `qr:` image as the picture of its code; its alt text, with a note, when no image converter draws it.
    private void Qr(LinkInline link, string url, Frame frame, List<OpenXmlElement> target)
    {
        var line = this.LineOf(link);
        var alt = AltText(link);
        var attributes = ImageAttributes.Take(link, line, Format.Docx);
        var (drawing, _) = QrDrawing.ForImage(url, alt, link.Title, attributes, ResolvedBrand.Resolve(this.Context.Brand), line, Format.Docx);
        if (this.DrawingPicture(drawing, alt, this.Context.Page.TextWidth * DocxPage.EmuPerTwip, frame.Part) is { } picture)
        {
            target.Add(this.PictureParagraph(frame, picture, keepNext: false));
            return;
        }

        this.Context.Notes.Add(
            NoteCodes.CodeNotDrawn,
            line,
            $"The QR code '{GenerationNotes.Quote(alt)}' is shown as its alt text: a drawing needs an image converter in a Word document. {ConverterAdvice}");
        target.Add(this.Paragraph(frame, [new DocxRun(alt, RunFlags.None, null)]));
    }

    // A picture centred in a paragraph of its own, at the indent of the list item it continues.
    private Paragraph PictureParagraph(Frame frame, Run picture, bool keepNext)
    {
        var paragraph = this.Paragraph(frame.Style);
        var properties = paragraph.ParagraphProperties!;
        if (keepNext)
        {
            properties.Append(new KeepNext());
        }

        if (frame.Indent is { } indent)
        {
            if (frame.SuppressNumbering)
            {
                properties.Append(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 0 }));
            }

            properties.Append(new Indentation { Left = indent.ToString(CultureInfo.InvariantCulture) });
        }

        properties.Append(new Justification { Val = JustificationValues.Center });
        paragraph.Append(picture);
        return paragraph;
    }

    // A drawing as a picture at its natural size, fitted; null when no image converter makes a PNG of it.
    private Run? DrawingPicture(Drawing drawing, string alt, double maxWidth, OpenXmlPart part)
    {
        if (this.DrawingParts(drawing, part) is not var (png, svg, width, height))
        {
            return null;
        }

        var (cx, cy) = this.Fit(width, height, maxWidth);
        return new Run(this.Picture(png, cx, cy, alt, svg));
    }

    // The drawing's SVG, in the brand's family (Arial without one), and the PNG the image converters make of it at twice
    // its size, as image parts of `part`, with the natural size the converter reports.
    private (string Png, string Svg, double Width, double Height)? DrawingParts(Drawing drawing, OpenXmlPart part)
    {
        if (this.Context.Options.ImageConverters.Count == 0)
        {
            return null;
        }

        var brand = ResolvedBrand.Resolve(this.Context.Brand);
        var markup = DrawingSvg.Write(Still(drawing), new DrawingSvgOptions { FontFamily = brand.FontFamily, MonoFamily = this.Context.CodeFont });
        var svg = new DocumentResource("drawing.svg", "image/svg+xml", Encoding.UTF8.GetBytes(markup));
        if (ImageConverters.Convert(this.Context.Options.ImageConverters, svg, 2, Format.Docx) is not var (png, width, height)
            || ImageInfo.Read(png.Content.Span) is not { Format: ImageFormat.Png or ImageFormat.Jpeg } info)
        {
            return null;
        }

        return (AddImage(part, PartType(info.Format), png.Content), AddImage(part, ImagePartType.Svg, svg.Content), Math.Max(1, width), Math.Max(1, height));
    }

    // A picture does not hover: hover texts and the invisible points that carry them are left out, so that no tooltip is
    // drawn into the PNG.
    private static Drawing Still(Drawing drawing) =>
        drawing.Items.Any(i => i.Hover is not null || i is DrawingHotspot)
            ? drawing with { Items = [.. drawing.Items.Where(i => i is not DrawingHotspot).Select(i => i.Hover is null ? i : i with { Hover = null })] }
            : drawing;

    // The text of the container's first line of content, for its alt text: its first paragraph or list item, or the
    // cells of its table's first row.
    private string? FirstContentLine(CustomContainer container)
    {
        var text = container.FirstOrDefault() is Markdig.Extensions.Tables.Table { Count: > 0 } table && table[0] is ContainerBlock row
            ? string.Join(" · ", row.OfType<ContainerBlock>().Select(cell => PlainText(cell.Descendants<LeafBlock>().FirstOrDefault())))
            : PlainText(container.Descendants<LeafBlock>().FirstOrDefault());
        return text.Trim().Length > 0 ? Cut(text.Trim()) : null;
    }

    private static string PlainText(LeafBlock? block) => block?.Inline is not { } inline ? string.Empty
        : string.Concat(inline.Descendants<Inline>().Select(i => i switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            LineBreakInline => " ",
            _ => string.Empty,
        }));

    // The first line with a letter or a digit, so that a `{` of JSON is passed over, cut at the alt text's length; null
    // when there is none.
    private static string? FirstLine(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Any(char.IsLetterOrDigit)) is { } line ? Cut(line) : null;

    private static string Cut(string text) => text.Length <= AltTextLength ? text : text[..(AltTextLength - 1)] + "…";

    // XML 1.0 has no place for most control characters.
    private static string XmlSafe(string text) =>
        text.Any(c => c < ' ' && c is not '\t') ? new string([.. text.Where(c => c >= ' ' || c is '\t')]) : text;
}
