using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The built-in Word template, one design for every brand, written as XML here so it can be reviewed like code: the
/// theme from the brand's colours and font family, the styles of every role (<see cref="DocxRoles"/>) in the PDF's
/// sizes and colours, the page from the options, and headers and footers. Content pages show the document title and
/// the logo in the header, the footer text, the date and "n / N" in the footer; the cover (the first page,
/// <c>w:titlePg</c>) shows the logo top right, the cover artwork across the bottom and the tagline bottom right.
/// </summary>
internal static class DocxTemplateBuilder
{
    /// <summary>The <c>w:storeItemID</c> of the core properties, which content controls bind to.</summary>
    public const string CorePropertiesStore = "{6C3C8BC8-F283-45AE-878A-BAB7291924A1}";

    /// <summary>The prefixes a binding to the core properties uses.</summary>
    public const string CorePropertiesPrefixes =
        "xmlns:ns0='http://purl.org/dc/elements/1.1/' xmlns:ns1='http://schemas.openxmlformats.org/package/2006/metadata/core-properties'";

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Namespaces =
        $"xmlns:w=\"{W}\" xmlns:r=\"{R}\" "
        + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" "
        + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
        + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    // The header and footer distance from the page edge, 10 mm.
    private const int HeaderDistance = 567;

    private const long EmuPerPoint = 12700;

    // Relationship ids of the document part, fixed so that the template reads the same every time.
    private const string StylesId = "rId1";
    private const string SettingsId = "rId2";
    private const string FontTableId = "rId3";
    private const string ThemeId = "rId4";
    private const string HeaderId = "rId5";
    private const string FooterId = "rId6";
    private const string CoverHeaderId = "rId7";
    private const string CoverFooterId = "rId8";
    private const string NumberingId = "rId9";

    /// <summary>Writes the template for <paramref name="brand"/> as a <c>.docx</c> with an empty body.</summary>
    /// <param name="output">Where the package goes.</param>
    /// <param name="brand">The design.</param>
    /// <param name="page">The page size and margins.</param>
    /// <param name="showTagline">Whether the cover shows the brand's tagline.</param>
    /// <param name="date">The date in the footer; <see langword="null"/>: none.</param>
    /// <param name="headingNumbers">Whether Heading 1–3 are numbered 1, 1.1, 1.1.1.</param>
    /// <param name="codeFont">The family of code.</param>
    public static void Write(Stream output, ResolvedBrand brand, DocxPage page, bool showTagline, string? date, bool headingNumbers = true, string codeFont = DocxOptions.DefaultCodeFont)
    {
        using var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        var design = new StyleDesign(brand, page.CoverTitleSpace, page.TextWidth) { CodeFont = codeFont, HeadingNumbers = headingNumbers };
        Feed(main.AddNewPart<StyleDefinitionsPart>(StylesId), Styles(design, page));
        Feed(main.AddNewPart<DocumentSettingsPart>(SettingsId), Settings);
        Feed(main.AddNewPart<FontTablePart>(FontTableId), FontTable(brand));
        Feed(main.AddNewPart<ThemePart>(ThemeId), Theme(brand));
        Feed(main.AddNewPart<NumberingDefinitionsPart>(NumberingId), DocxNumbering.BuiltInXml(Namespaces));

        var ids = new DrawingIds();
        var header = main.AddNewPart<HeaderPart>(HeaderId);
        (string Id, ImageInfo Info)? headerLogo = brand.Logo is { } logo ? Image(header, logo) : null;
        Feed(header, Header(brand, page, headerLogo, ids));
        Feed(main.AddNewPart<FooterPart>(FooterId), Footer(brand, date));

        var cover = main.AddNewPart<HeaderPart>(CoverHeaderId);
        var coverImages = new List<string>();
        if (brand.CoverArtwork is { } artwork)
        {
            coverImages.Add(Artwork(page, Image(cover, artwork), ids));
        }

        if (brand.Logo is { } coverLogo)
        {
            coverImages.Add(CoverLogo(page, Image(cover, coverLogo), ids));
        }

        if (showTagline && brand.Tagline is { } tagline)
        {
            coverImages.Add(Tagline(page, Image(cover, tagline), ids));
        }

        Feed(cover, $"<w:hdr {Namespaces}><w:p><w:pPr><w:pStyle w:val=\"Header\"/></w:pPr>{string.Concat(coverImages)}</w:p></w:hdr>");
        Feed(main.AddNewPart<FooterPart>(CoverFooterId), $"<w:ftr {Namespaces}><w:p><w:pPr><w:pStyle w:val=\"Footer\"/></w:pPr></w:p></w:ftr>");

        Feed(main, $"<w:document {Namespaces}><w:body>{Section(page)}</w:body></w:document>");
    }

    /// <summary>The body's section: page, margins, the headers and footers, and a first page (the cover) of its own.</summary>
    private static string Section(DocxPage page) =>
        "<w:sectPr>"
        + $"<w:headerReference w:type=\"default\" r:id=\"{HeaderId}\"/><w:headerReference w:type=\"first\" r:id=\"{CoverHeaderId}\"/>"
        + $"<w:footerReference w:type=\"default\" r:id=\"{FooterId}\"/><w:footerReference w:type=\"first\" r:id=\"{CoverFooterId}\"/>"
        + $"<w:pgSz w:w=\"{N(page.Width)}\" w:h=\"{N(page.Height)}\"{(page.Landscape ? " w:orient=\"landscape\"" : string.Empty)}/>"
        + $"<w:pgMar w:top=\"{N(page.Top)}\" w:right=\"{N(page.Right)}\" w:bottom=\"{N(page.Bottom)}\" w:left=\"{N(page.Left)}\" "
        + $"w:header=\"{N(HeaderDistance)}\" w:footer=\"{N(HeaderDistance)}\" w:gutter=\"0\"/>"
        + "<w:cols w:space=\"708\"/><w:titlePg/><w:docGrid w:linePitch=\"360\"/></w:sectPr>";

    private static string Styles(StyleDesign design, DocxPage page)
    {
        var text = new StringBuilder($"<w:styles {Namespaces}>");
        text.Append("<w:docDefaults><w:rPrDefault><w:rPr>")
            .Append("<w:rFonts w:asciiTheme=\"minorHAnsi\" w:eastAsiaTheme=\"minorEastAsia\" w:hAnsiTheme=\"minorHAnsi\" w:cstheme=\"minorBidi\"/>")
            .Append("<w:sz w:val=\"21\"/><w:szCs w:val=\"21\"/>")
            .Append("</w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"324\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
        foreach (var role in DocxRoles.All)
        {
            var normal = role == DocxRoles.Normal;
            text.Append(DocxRoles.Xml(role, design, role.Id, normal ? null : DocxRoles.Normal.Id, normal ? null : DocxRoles.Normal.Id, isDefault: normal));
        }

        text.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/>")
            .Append("<w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>")
            .Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/>")
            .Append("<w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/>")
            .Append("<w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>")
            .Append("<w:style w:type=\"numbering\" w:default=\"1\" w:styleId=\"NoList\"><w:name w:val=\"No List\"/><w:uiPriority w:val=\"99\"/>")
            .Append("<w:semiHidden/><w:unhideWhenUsed/></w:style>");

        // Header and footer: small, in the footer colour, with a centre and a right tab stop.
        foreach (var (id, name) in new[] { ("Header", "header"), ("Footer", "footer") })
        {
            text.Append(CultureInfo.InvariantCulture, $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/><w:basedOn w:val=\"Normal\"/>")
                .Append("<w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:pPr>")
                .Append(CultureInfo.InvariantCulture, $"<w:tabs><w:tab w:val=\"center\" w:pos=\"{N(page.TextWidth / 2)}\"/><w:tab w:val=\"right\" w:pos=\"{N(page.TextWidth)}\"/></w:tabs>")
                .Append("<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>")
                .Append(CultureInfo.InvariantCulture, $"<w:rPr><w:color w:val=\"{design.Brand.Footer}\"/><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr></w:style>");
        }

        return text.Append("</w:styles>").ToString();
    }

    private const string Settings =
        $"<w:settings xmlns:w=\"{W}\"><w:zoom w:percent=\"100\"/><w:defaultTabStop w:val=\"709\"/>"
        + "<w:characterSpacingControl w:val=\"doNotCompress\"/>"
        + "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat>"
        + "</w:settings>";

    // The text family; the embedding of its faces is added by DocxFontEmbedding.
    private static string FontTable(ResolvedBrand brand) =>
        $"<w:fonts xmlns:w=\"{W}\" xmlns:r=\"{R}\">"
        + $"<w:font w:name=\"{Escape(brand.FontFamily)}\"><w:charset w:val=\"00\"/><w:family w:val=\"auto\"/><w:pitch w:val=\"variable\"/></w:font>"
        + "</w:fonts>";

    private static string Theme(ResolvedBrand brand)
    {
        string[] names = ["dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];
        var colors = string.Concat(names.Select((name, i) => $"<a:{name}><a:srgbClr val=\"{brand.ThemeColors[i]}\"/></a:{name}>"));
        var family = Escape(brand.FontFamily);
        const string Solid = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";
        return $"<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"{brand.Name}\"><a:themeElements>"
            + $"<a:clrScheme name=\"{brand.Name}\">{colors}</a:clrScheme>"
            + $"<a:fontScheme name=\"{family}\">"
            + $"<a:majorFont><a:latin typeface=\"{family}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>"
            + $"<a:minorFont><a:latin typeface=\"{family}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>"
            + "</a:fontScheme>"
            + $"<a:fmtScheme name=\"{brand.Name}\">"
            + "<a:fillStyleLst>" + string.Concat(Enumerable.Repeat(Solid, 3)) + "</a:fillStyleLst>"
            + "<a:lnStyleLst>" + string.Concat(new[] { 6350, 12700, 19050 }.Select(w => $"<a:ln w=\"{w}\">{Solid}</a:ln>")) + "</a:lnStyleLst>"
            + "<a:effectStyleLst>" + string.Concat(Enumerable.Repeat("<a:effectStyle><a:effectLst/></a:effectStyle>", 3)) + "</a:effectStyleLst>"
            + "<a:bgFillStyleLst>" + string.Concat(Enumerable.Repeat(Solid, 3)) + "</a:bgFillStyleLst>"
            + "</a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
    }

    // Content pages: the document title left, bound to the title in the core properties; the logo right, 18 pt high.
    private static string Header(ResolvedBrand brand, DocxPage page, (string Id, ImageInfo Info)? logo, DrawingIds ids)
    {
        var picture = string.Empty;
        if (logo is { } image)
        {
            var height = 18 * EmuPerPoint;
            var width = height * image.Info.Width / image.Info.Height;
            var x = ((page.Width - page.Right) * DocxPage.EmuPerTwip) - width;
            picture = Anchor(ids.Next(), "Logo", image.Id, x, 24 * EmuPerPoint, width, height);
        }

        return $"<w:hdr {Namespaces}><w:p><w:pPr><w:pStyle w:val=\"Header\"/></w:pPr>{picture}"
            + "<w:sdt><w:sdtPr><w:alias w:val=\"Title\"/><w:tag w:val=\"title\"/>"
            + $"<w:id w:val=\"{ids.Next()}\"/>"
            + $"<w:dataBinding w:prefixMappings=\"{CorePropertiesPrefixes}\" w:xpath=\"/ns1:coreProperties[1]/ns0:title[1]\" w:storeItemID=\"{CorePropertiesStore}\"/>"
            + "<w:text/></w:sdtPr><w:sdtContent><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:sdtContent></w:sdt>"
            + "</w:p></w:hdr>";
    }

    // Content pages: the footer text left, the date in the centre, "n / N" right.
    private static string Footer(ResolvedBrand brand, string? date)
    {
        static string Text(string text) => $"<w:r><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r>";
        static string Field(string instruction) => $"<w:fldSimple w:instr=\" {instruction} \"><w:r><w:t>1</w:t></w:r></w:fldSimple>";
        const string Tab = "<w:r><w:tab/></w:r>";
        return $"<w:ftr {Namespaces}><w:p><w:pPr><w:pStyle w:val=\"Footer\"/></w:pPr>"
            + (brand.FooterText is { } footer ? Text(footer) : string.Empty)
            + Tab + (date is null ? string.Empty : Text(date))
            + Tab + Field("PAGE") + Text(" / ") + Field("NUMPAGES")
            + "</w:p></w:ftr>";
    }

    // The cover artwork as wide as the page and at most 42 % of its height, in the bottom right corner, behind the text.
    private static string Artwork(DocxPage page, (string Id, ImageInfo Info) image, DrawingIds ids)
    {
        var pageWidth = page.Width * DocxPage.EmuPerTwip;
        var pageHeight = page.Height * DocxPage.EmuPerTwip;
        var ratio = (double)image.Info.Height / image.Info.Width;
        var width = (double)pageWidth;
        var height = width * ratio;
        if (height > pageHeight * 0.42)
        {
            height = pageHeight * 0.42;
            width = height / ratio;
        }

        return Anchor(ids.Next(), "Cover artwork", image.Id, pageWidth - (long)width, pageHeight - (long)height, (long)width, (long)height);
    }

    // The logo top right on the cover, 16 % of the page wide.
    private static string CoverLogo(DocxPage page, (string Id, ImageInfo Info) image, DrawingIds ids)
    {
        var margin = page.Left * DocxPage.EmuPerTwip;
        var width = (long)(page.Width * DocxPage.EmuPerTwip * 0.16);
        var height = width * image.Info.Height / image.Info.Width;
        return Anchor(ids.Next(), "Logo", image.Id, (page.Width * DocxPage.EmuPerTwip) - margin - width, (long)(margin * 0.7), width, height);
    }

    // The tagline bottom right on the cover, 17 % of the page wide.
    private static string Tagline(DocxPage page, (string Id, ImageInfo Info) image, DrawingIds ids)
    {
        var margin = page.Left * DocxPage.EmuPerTwip;
        var width = (long)(page.Width * DocxPage.EmuPerTwip * 0.17);
        var height = width * image.Info.Height / image.Info.Width;
        return Anchor(
            ids.Next(),
            "Tagline",
            image.Id,
            (page.Width * DocxPage.EmuPerTwip) - margin - width,
            (page.Height * DocxPage.EmuPerTwip) - (long)(margin * 0.6) - height,
            width,
            height);
    }

    // A picture placed on the page, behind the text and not moving it.
    private static string Anchor(int id, string name, string relationship, long x, long y, long width, long height) =>
        $"<w:r><w:drawing><wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"{id}\" behindDoc=\"1\" "
        + "locked=\"1\" layoutInCell=\"1\" allowOverlap=\"1\"><wp:simplePos x=\"0\" y=\"0\"/>"
        + $"<wp:positionH relativeFrom=\"page\"><wp:posOffset>{N(x)}</wp:posOffset></wp:positionH>"
        + $"<wp:positionV relativeFrom=\"page\"><wp:posOffset>{N(y)}</wp:posOffset></wp:positionV>"
        + $"<wp:extent cx=\"{N(width)}\" cy=\"{N(height)}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapNone/>"
        + $"<wp:docPr id=\"{id}\" name=\"{name}\"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>"
        + "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>"
        + $"<pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{name}\"/><pic:cNvPicPr/></pic:nvPicPr>"
        + $"<pic:blipFill><a:blip r:embed=\"{relationship}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
        + $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{N(width)}\" cy=\"{N(height)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
        + "</pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>";

    private static (string Id, ImageInfo Info) Image(OpenXmlPart part, byte[] image)
    {
        var info = ImageInfo.Read(image) ?? throw new InvalidDataException("A brand image is not a PNG or JPEG.");
        var type = info.Format == ImageFormat.Jpeg ? ImagePartType.Jpeg : ImagePartType.Png;
        var imagePart = part switch
        {
            HeaderPart header => header.AddImagePart(type),
            _ => throw new ArgumentException("Brand images go in headers only.", nameof(part)),
        };
        using (var stream = new MemoryStream(image))
        {
            imagePart.FeedData(stream);
        }

        return (part.GetIdOfPart(imagePart), info);
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(stream);
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string text) => SecurityElement.Escape(text);

    /// <summary>Ids of drawings and content controls, unique across the document's parts.</summary>
    private sealed class DrawingIds
    {
        private int last = 1000;

        public int Next() => ++this.last;
    }
}
