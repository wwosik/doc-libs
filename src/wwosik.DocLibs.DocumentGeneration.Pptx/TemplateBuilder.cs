using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// The built-in 16:9 template, one master design for every <see cref="Brand"/>: title top left in the brand's
/// primary colour, a footer line with slide number, footer text and date, the logo bottom right, and the layouts
/// <c>title</c>, <c>obj</c>, <c>titleOnly</c> and <c>secHead</c>. Geometry follows a common 16:9 corporate layout.
/// Written as XML here so it can be reviewed like code.
/// </summary>
internal static class TemplateBuilder
{
    /// <summary>The id of the table style defined in <c>tableStyles.xml</c> from the brand colours.</summary>
    public const string TableStyleId = "{3B4B98B0-60AC-42C2-AFA5-B58CD77FA1E5}";

    /// <summary>The <c>id</c> of slide number fields.</summary>
    public const string SlideNumberFieldId = "{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}";

    /// <summary>The <c>idx</c> of the date and slide number placeholders, the same on master, layouts and slides.</summary>
    public const uint DateIndex = 2;

    public const uint SlideNumberIndex = 4;

    private const string Namespaces =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
        + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
        + "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private const string GroupHeader =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
        + "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private const string TextFont = "<a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/>";
    private const string HeadingFont = "<a:latin typeface=\"+mj-lt\"/><a:ea typeface=\"+mj-ea\"/><a:cs typeface=\"+mj-cs\"/>";
    private const string ClrMap =
        "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" "
        + "accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>";

    private const string LayoutEnd = "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
    private const string White = "FFFFFF";

    // Geometry in EMU, from a common 16:9 corporate layout.
    private const long Left = 912283;
    private const long Width = 10896600;
    private const long TitleTop = 256032;
    private const long TitleHeight = 1188720;
    private const long BodyTop = 1682496;
    private const long BodyHeight = 4370832;
    private const long FooterTop = 6453337;
    private const long FooterHeight = 164148;
    private const long FooterTextLeft = 1560896;
    private const long FooterTextWidth = 1056117;
    private const long DateWithFooterTextLeft = 2748329;
    private const long DateWidth = 722841;
    private const int FooterSize = 1067;

    private const string MasterLogoRelationship = "rId6";
    private const string LogoRelationship = "rId2";
    private const string ArtworkRelationship = "rId3";
    private const string ClaimRelationship = "rId4";

    /// <summary>Writes the template for <paramref name="brand"/> as a <c>.pptx</c> without slides.</summary>
    public static void Write(Stream output, Brand brand, bool showClaim)
    {
        using var package = PresentationDocument.Create(output, PresentationDocumentType.Presentation);
        var presentation = package.AddPresentationPart();
        var master = presentation.AddNewPart<SlideMasterPart>("rId1");
        var theme = master.AddNewPart<ThemePart>("rId5");
        presentation.AddPart(theme, "rId2");
        Feed(presentation.AddNewPart<TableStylesPart>("rId3"), TableStyles(brand));
        Feed(presentation.AddNewPart<PresentationPropertiesPart>("rId4"), $"<p:presentationPr {Namespaces}/>");

        var logo = brand.Logo is { } logoBytes ? Image(master, MasterLogoRelationship, logoBytes) : null;
        var claim = showClaim ? brand.Claim : null;
        var layouts = new (string Xml, Action<SlideLayoutPart> AddImages)[]
        {
            (TitleLayout(brand, claim is not null), layout =>
            {
                if (logo is not null)
                {
                    layout.AddPart(logo, LogoRelationship);
                }

                if (brand.Artwork is { } artwork)
                {
                    Image(layout, ArtworkRelationship, artwork);
                }

                if (claim is not null)
                {
                    Image(layout, ClaimRelationship, claim);
                }
            }),
            (ContentLayout, _ => { }),
            (TitleOnlyLayout, _ => { }),
            (SectionLayout(brand), layout =>
            {
                if (brand.WhiteLogo is { } whiteLogo)
                {
                    Image(layout, LogoRelationship, whiteLogo);
                }
            }),
        };

        for (var i = 0; i < layouts.Length; i++)
        {
            var layout = master.AddNewPart<SlideLayoutPart>("rId" + (i + 1).ToString(CultureInfo.InvariantCulture));
            layout.AddPart(master, "rId1");
            layouts[i].AddImages(layout);
            Feed(layout, layouts[i].Xml);
        }

        Feed(theme, Theme(brand));
        Feed(master, Master(brand, logo is not null));
        Feed(presentation, Presentation);
    }

    private static ImagePart Image(OpenXmlPartContainer container, string id, byte[] png)
    {
        var part = container switch
        {
            SlideMasterPart master => master.AddImagePart(ImagePartType.Png, id),
            SlideLayoutPart layout => layout.AddImagePart(ImagePartType.Png, id),
            _ => throw new ArgumentException("Images go on masters and layouts only.", nameof(container)),
        };
        using var stream = new MemoryStream(png);
        part.FeedData(stream);
        return part;
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(stream);
    }

    private static readonly string Presentation =
        $"<p:presentation {Namespaces} saveSubsetFonts=\"1\">"
        + "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>"
        + "<p:sldSz cx=\"12192000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>"
        + $"<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr>{Level(1, 0, 0, null, 1800)}</p:defaultTextStyle>"
        + "</p:presentation>";

    private static string Theme(Brand brand) =>
        $"<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"{brand.Name}\"><a:themeElements>"
        + $"<a:clrScheme name=\"{brand.Name}\">{ColorScheme(brand.ThemeColors)}</a:clrScheme>"
        + "<a:fontScheme name=\"Arial\">"
        + "<a:majorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>"
        + "<a:minorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>"
        + "</a:fontScheme>"
        + $"<a:fmtScheme name=\"{brand.Name}\">"
        + "<a:fillStyleLst>" + Repeat(SolidPlaceholder, 3) + "</a:fillStyleLst>"
        + "<a:lnStyleLst>"
        + string.Concat(new[] { 6350, 12700, 19050 }.Select(w => $"<a:ln w=\"{w}\">{SolidPlaceholder}</a:ln>"))
        + "</a:lnStyleLst>"
        + "<a:effectStyleLst>" + Repeat("<a:effectStyle><a:effectLst/></a:effectStyle>", 3) + "</a:effectStyleLst>"
        + "<a:bgFillStyleLst>" + Repeat(SolidPlaceholder, 3) + "</a:bgFillStyleLst>"
        + "</a:fmtScheme>"
        + "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";

    private static string ColorScheme(IReadOnlyList<string> colors)
    {
        string[] names = ["dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];
        return string.Concat(names.Select((name, i) => $"<a:{name}><a:srgbClr val=\"{colors[i]}\"/></a:{name}>"));
    }

    private const string SolidPlaceholder = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";

    // Header row in the brand colour with white bold text, light banded rows, thin lines.
    private static string TableStyles(Brand brand)
    {
        var line = $"<a:ln w=\"6350\" cmpd=\"sng\"><a:solidFill><a:srgbClr val=\"{brand.TableLine}\"/></a:solidFill></a:ln>";
        var borders = string.Concat(new[] { "left", "right", "top", "bottom", "insideH", "insideV" }.Select(side => $"<a:{side}>{line}</a:{side}>"));
        return $"<a:tblStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" def=\"{TableStyleId}\">"
            + $"<a:tblStyle styleId=\"{TableStyleId}\" styleName=\"{brand.Name}\">"
            + "<a:wholeTbl><a:tcTxStyle><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:schemeClr val=\"tx1\"/></a:tcTxStyle>"
            + $"<a:tcStyle><a:tcBdr>{borders}</a:tcBdr><a:fill><a:solidFill><a:schemeClr val=\"bg1\"/></a:solidFill></a:fill></a:tcStyle></a:wholeTbl>"
            + $"<a:band1H><a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:srgbClr val=\"{brand.TableBand}\"/></a:solidFill></a:fill></a:tcStyle></a:band1H>"
            + $"<a:firstRow><a:tcTxStyle b=\"on\"><a:fontRef idx=\"minor\"><a:prstClr val=\"black\"/></a:fontRef><a:srgbClr val=\"{White}\"/></a:tcTxStyle>"
            + $"<a:tcStyle><a:tcBdr/><a:fill><a:solidFill><a:srgbClr val=\"{brand.TableHeader}\"/></a:solidFill></a:fill></a:tcStyle></a:firstRow>"
            + "</a:tblStyle></a:tblStyleLst>";
    }

    private static string Master(Brand brand, bool hasLogo) =>
        $"<p:sldMaster {Namespaces}><p:cSld>"
        + "<p:bg><p:bgPr><a:solidFill><a:schemeClr val=\"bg1\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>"
        + "<p:spTree>" + GroupHeader
        + Placeholder(2, "Title Placeholder 1", "<p:ph type=\"title\"/>", Box(Left, TitleTop, Width, TitleHeight), " anchor=\"t\"", "Click to edit Master title style")
        + Placeholder(3, "Text Placeholder 2", "<p:ph type=\"body\" idx=\"1\"/>", Box(Left, BodyTop, Width, BodyHeight), string.Empty, "Click to edit Master text styles")
        + FooterPlaceholder(4, "Date Placeholder 3", $"<p:ph type=\"dt\" sz=\"half\" idx=\"{DateIndex}\"/>", Box(DateLeft(brand), FooterTop, DateWidth, FooterHeight), brand.Footer, slideNumber: false)
        + FooterPlaceholder(5, "Slide Number Placeholder 4", $"<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"{SlideNumberIndex}\"/>", Box(Left, FooterTop, 302400, FooterHeight), brand.Footer, slideNumber: true)
        + FooterText(6, brand, brand.Footer)
        + (hasLogo ? Picture(7, "Logo", MasterLogoRelationship, 10986386, 6165304, 815143, 576752) : string.Empty)
        + "</p:spTree></p:cSld>"
        + ClrMap
        + "<p:sldLayoutIdLst>"
        + string.Concat(Enumerable.Range(1, 4).Select(i => $"<p:sldLayoutId id=\"{2147483648L + i}\" r:id=\"rId{i}\"/>"))
        + "</p:sldLayoutIdLst>"
        + "<p:txStyles>"
        + "<p:titleStyle><a:lvl1pPr algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">"
        + "<a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc><a:spcBef><a:spcPct val=\"0\"/></a:spcBef><a:buNone/>"
        + $"<a:defRPr sz=\"3200\" b=\"1\" kern=\"1200\"><a:solidFill><a:srgbClr val=\"{brand.Primary}\"/></a:solidFill>{HeadingFont}</a:defRPr></a:lvl1pPr></p:titleStyle>"
        + "<p:bodyStyle>"
        + string.Concat(Enumerable.Range(1, 9).Select(n => Level(
            n,
            285750 + ((n - 1) * 457200L),
            -285750,
            n % 2 == 1 ? "•" : "–",
            n switch { 1 => 2000, 2 => 1800, _ => 1600 })))
        + "</p:bodyStyle>"
        + $"<p:otherStyle><a:defPPr><a:defRPr lang=\"en-US\"/></a:defPPr>{Level(1, 0, 0, null, 1800)}</p:otherStyle>"
        + "</p:txStyles></p:sldMaster>";

    // The cover: large title, subtitle, logo top right, artwork and claim bottom right; no footer line.
    private static string TitleLayout(Brand brand, bool hasClaim) =>
        $"<p:sldLayout {Namespaces} type=\"title\" preserve=\"1\" showMasterSp=\"0\"><p:cSld name=\"Title Slide\"><p:spTree>" + GroupHeader
        + (brand.Artwork is not null ? Artwork(2) : string.Empty)
        + Placeholder(
            3,
            "Title 1",
            "<p:ph type=\"ctrTitle\"/>",
            Box(Left, 1749110, Width, 2659189),
            " anchor=\"b\"",
            null,
            $"<a:lstStyle><a:lvl1pPr><a:lnSpc><a:spcPct val=\"80000\"/></a:lnSpc><a:defRPr sz=\"6000\"><a:solidFill><a:srgbClr val=\"{brand.Primary}\"/></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>")
        + Placeholder(
            4,
            "Subtitle 2",
            "<p:ph type=\"subTitle\" idx=\"1\"/>",
            Box(Left, 4480560, Width, 1005840),
            string.Empty,
            null,
            "<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\"><a:buNone/><a:defRPr sz=\"2000\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>")
        + (brand.Logo is not null ? Picture(5, "Logo", LogoRelationship, 10137562, 359861, 1586924, 1122823) : string.Empty)
        + (hasClaim ? Picture(6, "Claim", ClaimRelationship, 10176934, 6087210, 1626375, 475637) : string.Empty)
        + LayoutEnd;

    private static readonly string ContentLayout =
        $"<p:sldLayout {Namespaces} type=\"obj\" preserve=\"1\"><p:cSld name=\"Title and Content\"><p:spTree>" + GroupHeader
        + Placeholder(2, "Title 1", "<p:ph type=\"title\"/>", null, string.Empty, null)
        + Placeholder(3, "Content Placeholder 2", "<p:ph idx=\"1\"/>", null, string.Empty, null)
        + FooterPlaceholders(4, null)
        + LayoutEnd;

    private static readonly string TitleOnlyLayout =
        $"<p:sldLayout {Namespaces} type=\"titleOnly\" preserve=\"1\"><p:cSld name=\"Title Only\"><p:spTree>" + GroupHeader
        + Placeholder(2, "Title 1", "<p:ph type=\"title\"/>", null, string.Empty, null)
        + FooterPlaceholders(3, null)
        + LayoutEnd;

    // Section divider: brand gradient, white title and footer line, white logo.
    private static string SectionLayout(Brand brand) =>
        $"<p:sldLayout {Namespaces} type=\"secHead\" preserve=\"1\" showMasterSp=\"0\"><p:cSld name=\"Section Header\">"
        + "<p:bg><p:bgPr><a:gradFill rotWithShape=\"1\"><a:gsLst>"
        + string.Concat(brand.Gradient.Select((color, i) =>
            $"<a:gs pos=\"{(i * 100000 / (brand.Gradient.Count - 1)).ToString(CultureInfo.InvariantCulture)}\"><a:srgbClr val=\"{color}\"/></a:gs>"))
        + "</a:gsLst><a:lin ang=\"0\" scaled=\"0\"/></a:gradFill><a:effectLst/></p:bgPr></p:bg>"
        + "<p:spTree>" + GroupHeader
        + Placeholder(
            2,
            "Title 1",
            "<p:ph type=\"title\"/>",
            Box(Left, 1741449, Width, 2538908),
            " anchor=\"b\"",
            null,
            $"<a:lstStyle><a:lvl1pPr><a:lnSpc><a:spcPct val=\"80000\"/></a:lnSpc><a:defRPr sz=\"5400\"><a:solidFill><a:srgbClr val=\"{White}\"/></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>")
        + Placeholder(
            3,
            "Text Placeholder 2",
            "<p:ph type=\"body\" idx=\"1\"/>",
            Box(Left, 4389120, Width, 1005840),
            string.Empty,
            null,
            $"<a:lstStyle><a:lvl1pPr marL=\"0\" indent=\"0\"><a:buNone/><a:defRPr sz=\"2400\"><a:solidFill><a:srgbClr val=\"{White}\"/></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>")
        + FooterPlaceholders(4, White)
        + FooterText(6, brand, White)
        + (brand.WhiteLogo is not null ? Picture(7, "Logo", LogoRelationship, 10986386, 6165304, 815143, 576752) : string.Empty)
        + LayoutEnd;

    private static long DateLeft(Brand brand) => brand.FooterText is null ? FooterTextLeft : DateWithFooterTextLeft;

    private static string Box(long x, long y, long cx, long cy) =>
        $"<a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>";

    private static string Placeholder(int id, string name, string ph, string? box, string bodyAttributes, string? prompt, string listStyle = "<a:lstStyle/>") =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr>{ph}</p:nvPr></p:nvSpPr>"
        + $"<p:spPr>{box}</p:spPr>"
        + $"<p:txBody><a:bodyPr lIns=\"0\" rIns=\"0\"{bodyAttributes}><a:normAutofit/></a:bodyPr>{listStyle}"
        + (prompt is null
            ? "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>"
            : $"<a:p><a:r><a:rPr lang=\"en-US\"/><a:t>{prompt}</a:t></a:r></a:p>")
        + "</p:txBody></p:sp>";

    // Date and slide number. On layouts they carry no position and inherit the master's; color overrides the master's.
    private static string FooterPlaceholders(int firstId, string? color) =>
        FooterPlaceholder(firstId, "Date Placeholder", $"<p:ph type=\"dt\" sz=\"half\" idx=\"{DateIndex}\"/>", null, color, slideNumber: false)
        + FooterPlaceholder(firstId + 1, "Slide Number Placeholder", $"<p:ph type=\"sldNum\" sz=\"quarter\" idx=\"{SlideNumberIndex}\"/>", null, color, slideNumber: true);

    private static string FooterPlaceholder(int id, string name, string ph, string? box, string? color, bool slideNumber) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr>{ph}</p:nvPr></p:nvSpPr>"
        + $"<p:spPr>{box}</p:spPr>"
        + (box is null
            ? "<p:txBody><a:bodyPr/>"
            : "<p:txBody><a:bodyPr wrap=\"none\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" anchor=\"ctr\"><a:noAutofit/></a:bodyPr>")
        + (color is null
            ? "<a:lstStyle/>"
            : $"<a:lstStyle><a:lvl1pPr algn=\"l\"><a:defRPr sz=\"{FooterSize}\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill></a:defRPr></a:lvl1pPr></a:lstStyle>")
        + (slideNumber
            ? $"<a:p><a:fld id=\"{SlideNumberFieldId}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld><a:endParaRPr lang=\"en-US\"/></a:p>"
            : "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>")
        + "</p:txBody></p:sp>";

    private static string FooterText(int id, Brand brand, string color) =>
        brand.FooterText is not { } text
            ? string.Empty
            : $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"Footer Text\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr userDrawn=\"1\"/></p:nvSpPr>"
                + $"<p:spPr>{Box(FooterTextLeft, FooterTop, FooterTextWidth, FooterHeight)}<a:noFill/></p:spPr>"
                + "<p:txBody><a:bodyPr wrap=\"none\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" anchor=\"ctr\"><a:noAutofit/></a:bodyPr><a:lstStyle/>"
                + $"<a:p><a:pPr algn=\"l\"/><a:r><a:rPr lang=\"de-DE\" sz=\"{FooterSize}\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill>{TextFont}</a:rPr>"
                + $"<a:t>{SecurityElement.Escape(text)}</a:t></a:r></a:p></p:txBody></p:sp>";

    private static string Picture(int id, string name, string relationshipId, long x, long y, long cx, long cy) =>
        $"<p:pic><p:nvPicPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr userDrawn=\"1\"/></p:nvPicPr>"
        + $"<p:blipFill><a:blip r:embed=\"{relationshipId}\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>"
        + $"<p:spPr>{Box(x, y, cx, cy)}</p:spPr></p:pic>";

    // The wave as on the corporate cover: the upper left part of the image, tilted and clipped to a quadrilateral.
    private static string Artwork(int id) =>
        $"<p:pic><p:nvPicPr><p:cNvPr id=\"{id}\" name=\"Artwork\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr userDrawn=\"1\"/></p:nvPicPr>"
        + $"<p:blipFill rotWithShape=\"1\"><a:blip r:embed=\"{ArtworkRelationship}\"/><a:srcRect r=\"44748\" b=\"46413\"/><a:stretch/></p:blipFill>"
        + "<p:spPr><a:xfrm rot=\"21061222\"><a:off x=\"2647948\" y=\"2970822\"/><a:ext cx=\"9962517\" cy=\"4579780\"/></a:xfrm>"
        + "<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"l\" t=\"t\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"7471888\" h=\"3434835\">"
        + "<a:moveTo><a:pt x=\"7471888\" y=\"0\"/></a:moveTo><a:lnTo><a:pt x=\"6929115\" y=\"3434835\"/></a:lnTo>"
        + "<a:lnTo><a:pt x=\"0\" y=\"2339896\"/></a:lnTo><a:lnTo><a:pt x=\"0\" y=\"0\"/></a:lnTo><a:close/></a:path></a:pathLst></a:custGeom>"
        + "</p:spPr></p:pic>";

    private static string Level(int n, long marL, long indent, string? bullet, int size) =>
        $"<a:lvl{n}pPr marL=\"{marL}\" indent=\"{indent}\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">"
        + "<a:lnSpc><a:spcPct val=\"100000\"/></a:lnSpc>"
        + (bullet is null ? "<a:buNone/>" : $"<a:spcBef><a:spcPts val=\"600\"/></a:spcBef><a:buFont typeface=\"Arial\"/><a:buChar char=\"{bullet}\"/>")
        + $"<a:defRPr sz=\"{size}\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill>{TextFont}</a:defRPr></a:lvl{n}pPr>";

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));
}
