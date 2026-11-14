using System.Globalization;
using System.Security;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The classification marking in a Word document (E03-S04's keys): "Confidential · Draft" at the top of the header of
/// every content page in the <c>Classification</c> style, or in a content control tagged <c>classification</c> that the
/// template has in a header or footer instead; above the cover title in <c>Classification Cover</c>; a confidential
/// marking in <c>Colour Danger</c>. A draft also gets Word's own watermark, a VML text path in every header, which Word
/// lists under <i>Design → Watermark</i> and can remove there.
/// </summary>
internal static class DocxMarking
{
    /// <summary>The tag of the content control a template marks the place of the classification with.</summary>
    public const string Tag = "classification";

    /// <summary>The marking as a paragraph in <paramref name="role"/>; <see langword="null"/> when there is none.</summary>
    /// <param name="context">The document.</param>
    /// <param name="role">The style.</param>
    /// <param name="rightIndent">An indent from the right, in twentieths of a point, to clear the built-in header's logo.</param>
    public static Paragraph? Paragraph(DocxContext context, StyleRole role, int? rightIndent = null)
    {
        var text = context.Marking.Text(context.Texts);
        if (text.Length == 0)
        {
            return null;
        }

        var properties = new ParagraphProperties(new ParagraphStyleId { Val = context.Styles.Id(role) });
        if (rightIndent is { } indent)
        {
            properties.Indentation = new Indentation { Right = indent.ToString(CultureInfo.InvariantCulture) };
        }

        return new Paragraph(properties, Run(context, text));
    }

    /// <summary>
    /// Puts the marking into the headers, and the watermark of a draft into every header: after the body is written,
    /// so that the section knows whether the first page is a cover of its own.
    /// </summary>
    public static void Apply(DocxContext context, int? rightIndent, string font)
    {
        var text = context.Marking.Text(context.Texts);
        if (text.Length == 0)
        {
            return;
        }

        var main = context.Main;
        var body = main.Document!.Body!;
        var section = body.Elements<SectionProperties>().LastOrDefault();
        if (section is null)
        {
            section = new SectionProperties();
            body.Append(section);
        }

        var cover = section.GetFirstChild<TitlePage>() is not null;
        var content = Headers(main, section, cover, first: false);
        if (!Controls(main, text))
        {
            foreach (var header in content)
            {
                var paragraph = Paragraph(context, DocxRoles.Classification, rightIndent)!;
                header.Header!.PrependChild(paragraph);
            }
        }

        if (context.Marking.Draft)
        {
            var watermark = Marking.Watermark(context.Texts, context.Language);
            var id = 0;
            foreach (var header in (cover ? [.. content, .. Headers(main, section, cover, first: true)] : content).Distinct())
            {
                var paragraph = header.Header!.GetFirstChild<Paragraph>() ?? header.Header.AppendChild(new Paragraph());
                var run = Watermark(watermark, font, context.Page, ++id);
                if (paragraph.ParagraphProperties is { } properties)
                {
                    properties.InsertAfterSelf(run);
                }
                else
                {
                    paragraph.PrependChild(run);
                }
            }
        }

        foreach (var header in main.HeaderParts)
        {
            header.Header?.Save();
        }

        foreach (var footer in main.FooterParts)
        {
            footer.Footer?.Save();
        }
    }

    /// <summary>The marking's text for <c>cp:contentStatus</c>; <see langword="null"/>: none.</summary>
    public static string? Status(DocxContext context) => context.Marking.Text(context.Texts) is { Length: > 0 } text ? text : null;

    // The run of the marking: a confidential one in the danger colour.
    private static Run Run(DocxContext context, string text)
    {
        var run = DocxWriter.Run(text);
        if (context.Marking.Classification == Classification.Confidential)
        {
            run.PrependChild(new RunProperties(new RunStyle { Val = context.Styles.Id(DocxRoles.Colours[LabelCategory.Danger]) }));
        }

        return run;
    }

    // The headers of the content pages (default and even), or of the first page; a missing one is added.
    private static List<HeaderPart> Headers(MainDocumentPart main, SectionProperties section, bool cover, bool first)
    {
        var references = section.Elements<HeaderReference>()
            .Where(r => first ? r.Type?.Value == HeaderFooterValues.First : r.Type?.Value != HeaderFooterValues.First)
            .ToList();
        if (first ? references.Count == 0 : !references.Any(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default))
        {
            var part = main.AddNewPart<HeaderPart>();
            part.Header = new Header(new Paragraph());
            var reference = new HeaderReference { Type = first ? HeaderFooterValues.First : HeaderFooterValues.Default, Id = main.GetIdOfPart(part) };
            section.PrependChild(reference);
            references.Add(reference);
        }

        return [.. references.Select(r => r.Id?.Value).OfType<string>().Distinct().Select(id => main.GetPartById(id)).OfType<HeaderPart>()];
    }

    // Content controls tagged `classification` in the headers and footers get the text; whether there was one.
    private static bool Controls(MainDocumentPart main, string text)
    {
        var roots = main.HeaderParts.Select(h => (OpenXmlElement?)h.Header).Concat(main.FooterParts.Select(f => (OpenXmlElement?)f.Footer)).OfType<OpenXmlElement>();
        var found = false;
        foreach (var control in roots.SelectMany(r => r.Descendants<SdtElement>()).ToList())
        {
            if (string.Equals(control.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value, Tag, StringComparison.OrdinalIgnoreCase)
                && DocxPlaceholders.SetText(control, text))
            {
                control.SdtProperties!.GetFirstChild<ShowingPlaceholder>()?.Remove();
                found = true;
            }
        }

        return found;
    }

    // Word's own watermark: a WordArt text path named PowerPlusWaterMarkObject, centred on the margins at 45°, light
    // grey, behind the text. Word finds it by that name; LibreOffice shows it too.
    private static Run Watermark(string text, string font, DocxPage page, int id)
    {
        var width = page.TextWidth / 20.0 * 0.9;
        var height = width / Math.Max(2.0, text.Length * 0.6);
        static string Pt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "pt";
        var number = id.ToString(CultureInfo.InvariantCulture);
        var shape = (2048 + id).ToString(CultureInfo.InvariantCulture);
        var xml =
            "<w:pict xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:v=\"urn:schemas-microsoft-com:vml\" "
            + "xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w10=\"urn:schemas-microsoft-com:office:word\">"
            + "<v:shapetype id=\"_x0000_t136\" coordsize=\"21600,21600\" o:spt=\"136\" adj=\"10800\" path=\"m@7,l@8,m@5,21600l@6,21600e\">"
            + "<v:formulas>"
            + string.Concat(new[]
            {
                "sum #0 0 10800", "prod #0 2 1", "sum 21600 0 @1", "sum 0 0 @2", "sum 21600 0 @3", "if @0 @3 0", "if @0 21600 @1",
                "if @0 0 @2", "if @0 @4 21600", "mid @5 @6", "mid @8 @5", "mid @7 @8", "mid @6 @7", "sum @6 0 @5",
            }.Select(f => $"<v:f eqn=\"{f}\"/>"))
            + "</v:formulas>"
            + "<v:path textpathok=\"t\" o:connecttype=\"custom\" o:connectlocs=\"@9,0;@10,10800;@11,21600;@12,10800\" o:connectangles=\"270,180,90,0\"/>"
            + "<v:textpath on=\"t\" fitshape=\"t\"/>"
            + "<v:handles><v:h position=\"#0,bottomRight\" xrange=\"6629,14971\"/></v:handles>"
            + "<o:lock v:ext=\"edit\" text=\"t\" shapetype=\"t\"/>"
            + "</v:shapetype>"
            + $"<v:shape id=\"PowerPlusWaterMarkObject{number}\" o:spid=\"_x0000_s{shape}\" type=\"#_x0000_t136\" "
            + $"style=\"position:absolute;margin-left:0;margin-top:0;width:{Pt(width)};height:{Pt(height)};rotation:315;z-index:-251654144;"
            + "mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin\" "
            + $"o:allowincell=\"f\" fillcolor=\"#{Grey}\" stroked=\"f\">"
            + $"<v:textpath style=\"font-family:&quot;{SecurityElement.Escape(font)}&quot;;font-size:1pt\" string=\"{SecurityElement.Escape(text)}\"/>"
            + "<w10:wrap anchorx=\"margin\" anchory=\"margin\"/>"
            + "</v:shape></w:pict>";
        return new Run(new RunProperties(new NoProof()), new Picture(xml));
    }

    /// <summary>The grey of the watermark, light enough to read text over it.</summary>
    public const string Grey = "D9D9D9";
}
