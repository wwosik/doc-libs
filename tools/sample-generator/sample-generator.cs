#:package PdfPig@0.1.16
#:property PublishAot=false
#:property BuiltInComInteropSupport=true

using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

// sample-generator: writes the self-made sample documents the test projects compare against.
//
//   ./run.sh sample-generator              .pdf, .docx, .pptx, .xlsx, .rtf, .txt, .zip
//   run.ps1 sample-generator --office      additionally .doc and .xls, saved by Word and Excel (Windows only)
//
// Everything is built in code, so the samples contain nothing but the text below. After
// regenerating, refresh the *.expected.md files (see docs/README.md) and review the diff.

var office = args.Contains("--office");
var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", ".."));
var main = Path.Combine(root, "src", "wwosik.DocLibs.DocumentConversions.Tests", "Samples");
var windows = Path.Combine(root, "src", "wwosik.DocLibs.DocumentConversions.WindowsOnly.Tests", "Samples");
string[] generators =
[
    Path.Combine(root, "src", "wwosik.DocLibs.DocumentGeneration.Pptx.Tests", "Samples"),
    Path.Combine(root, "src", "wwosik.DocLibs.DocumentGeneration.Pdf.Tests", "Samples"),
];
Directory.CreateDirectory(main);
Directory.CreateDirectory(windows);

Write(Path.Combine(main, "sample.pdf"), Samples.Pdf());
Write(Path.Combine(main, "sample.docx"), Samples.Docx());
Write(Path.Combine(main, "sample.pptx"), Samples.Pptx());
Write(Path.Combine(main, "sample.txt"), Encoding.UTF8.GetBytes("Plain text sample.\nSecond line.\n"));
Write(Path.Combine(main, "sample.zip"), Samples.Zip());
Write(Path.Combine(windows, "sample.xlsx"), Samples.Xlsx());
Write(Path.Combine(windows, "sample.rtf"), Encoding.ASCII.GetBytes(Samples.Rtf()));
foreach (var generator in generators)
{
    Directory.CreateDirectory(generator);
    Write(Path.Combine(generator, "diagram.png"), Samples.Diagram);
    Write(Path.Combine(generator, "photo.png"), Samples.Photo);
}

if (office)
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("--office needs Windows with Word and Excel installed.");
        return 1;
    }

    Office.SaveAsDoc(Path.Combine(main, "sample.docx"), Path.Combine(windows, "sample.doc"));
    Console.WriteLine("wrote " + Path.Combine(windows, "sample.doc"));
    Office.SaveAsXls(Path.Combine(windows, "sample.xlsx"), Path.Combine(windows, "sample.xls"));
    Console.WriteLine("wrote " + Path.Combine(windows, "sample.xls"));
}

return 0;

static void Write(string path, byte[] content)
{
    File.WriteAllBytes(path, content);
    Console.WriteLine("wrote " + path);
}

static string SourcePath([CallerFilePath] string path = "") => path;

internal static class Samples
{
    // Fixed so that regenerating gives byte-identical OOXML packages.
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly byte[] Logo = Png.Create(16, 16, (x, y) => (0x1F, 0x4E, 0x9A));

    public static readonly byte[] Chart = Png.Create(24, 16, (x, y) =>
        y >= 16 - (((x / 6) + 1) * 4) ? ((byte)(x < 12 ? 0xC0 : 0x30), (byte)(x < 12 ? 0x30 : 0xA0), (byte)0x30) : ((byte)0xFF, (byte)0xFF, (byte)0xFF));

    public static readonly byte[] Photo = Png.Create(20, 20, (x, y) => ((byte)(x * 12), (byte)(y * 12), 0x80));

    // Big enough to be scaled down on a slide: four bars on a light grid.
    public static readonly byte[] Diagram = Png.Create(480, 270, (x, y) =>
    {
        var bar = (x - 40) / 110;
        var inBar = x >= 40 && (x - 40) % 110 < 70 && bar < 4 && y >= 250 - ((bar + 1) * 50);
        return inBar ? ((byte)0x1D, (byte)0x4E, (byte)0xD8) : y % 50 == 0 ? ((byte)0xD1, (byte)0xD5, (byte)0xDB) : ((byte)0xFF, (byte)0xFF, (byte)0xFF);
    });

    private const string Intro =
        "This sample document is generated in code. It exists so that the converters can be tested against "
        + "a known input, and every sentence in it is made up for that purpose only.";

    private const string Details =
        "Identifiers such as customfield_10682 keep their underscores, arithmetic such as 2 * 3 = 6 stays as "
        + "it is, and a paragraph that wraps over several lines comes back as a single paragraph.";

    private const string Summary =
        "The logo in the top corner repeats on every page and is therefore left out, while the chart on the "
        + "second page is kept as an image.";

    public static byte[] Pdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = "Sample document";
        builder.DocumentInformation.Author = "DocLibs";
        builder.DocumentInformation.Producer = "sample-generator";
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        string[] headings = ["1 Introduction", "2 Details", "3 Summary"];
        string[] bodies = [Intro, Details, Summary];
        for (var index = 0; index < 3; index++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddPng(Logo, new PdfRectangle(40, 790, 64, 814));
            page.AddText("Sample document", 9, new PdfPoint(72, 800), regular);
            page.AddText($"Page {index + 1} of 3", 9, new PdfPoint(72, 40), regular);

            var y = 740.0;
            page.AddText(headings[index], 16, new PdfPoint(72, y), bold);
            y -= 30;
            foreach (var line in Wrap(page, bodies[index], 11, regular, 451))
            {
                page.AddText(line, 11, new PdfPoint(72, y), regular);
                y -= 14;
            }

            if (index == 0)
            {
                y -= 10;
                foreach (var item in new[] { "First item", "Second item" })
                {
                    page.AddText("\u2022 " + item, 11, new PdfPoint(72, y), regular);
                    y -= 14;
                }
            }

            if (index == 1)
            {
                page.AddPng(Chart, new PdfRectangle(72, y - 140, 282, y - 20));
            }
        }

        return builder.Build();
    }

    private static List<string> Wrap(PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font, double width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            var letters = page.MeasureText(candidate, size, new PdfPoint(0, 0), font);
            if (current.Length > 0 && letters[^1].BoundingBox.Right > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        lines.Add(current);
        return lines;
    }

    public static byte[] Docx()
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var document = $$"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="{{w}}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <w:body>
                <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>Sample document</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Introduction</w:t></w:r></w:p>
                <w:p>
                  <w:r><w:t xml:space="preserve">This paragraph has </w:t></w:r>
                  <w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r>
                  <w:r><w:t xml:space="preserve">, </w:t></w:r>
                  <w:r><w:rPr><w:i/></w:rPr><w:t>italic</w:t></w:r>
                  <w:r><w:t xml:space="preserve"> and </w:t></w:r>
                  <w:r><w:rPr><w:b/><w:i/></w:rPr><w:t>bold italic</w:t></w:r>
                  <w:r><w:t xml:space="preserve"> text, and a </w:t></w:r>
                  <w:hyperlink r:id="rIdLink"><w:r><w:t>link</w:t></w:r></w:hyperlink>
                  <w:r><w:t>.</w:t></w:r>
                </w:p>
                <w:p>
                  <w:r><w:t xml:space="preserve">Literal characters stay literal: customfield_10682, *not bold* and [not a link].</w:t></w:r>
                  <w:r><w:br/><w:t>A line break precedes this sentence.</w:t></w:r>
                </w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>First bullet</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Nested bullet</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>First step</w:t></w:r></w:p>
                <w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="2"/></w:numPr></w:pPr><w:r><w:t>Second step</w:t></w:r></w:p>
                <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Table</w:t></w:r></w:p>
                <w:tbl>
                  <w:tblPr><w:tblW w:w="0" w:type="auto"/></w:tblPr>
                  <w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid>
                  <w:tr><w:tc><w:p><w:r><w:t>Item</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Price</w:t></w:r></w:p></w:tc></w:tr>
                  <w:tr><w:tc><w:p><w:r><w:t>Apple</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>1.20</w:t></w:r></w:p></w:tc></w:tr>
                  <w:tr><w:tc><w:p><w:r><w:t>Pear | green</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>0.90</w:t></w:r></w:p></w:tc></w:tr>
                </w:tbl>
                <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Images</w:t></w:r></w:p>
                <w:p><w:r><w:t xml:space="preserve">A chart: </w:t></w:r>{{Drawing(1, "rIdImage1", "Bar chart")}}</w:p>
                <w:p><w:r><w:t xml:space="preserve">The same chart, stored a second time: </w:t></w:r>{{Drawing(2, "rIdImage2", "Bar chart again")}}</w:p>
                <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1417" w:right="1417" w:bottom="1134" w:left="1417" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>
              </w:body>
            </w:document>
            """;

        var styles = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="{w}">
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:rPr><w:sz w:val="56"/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
              <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="28"/></w:rPr></w:style>
            </w:styles>
            """;

        var numbering = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="{w}">
              <w:abstractNum w:abstractNumId="0">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="&#8226;"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl>
                <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="o"/><w:pPr><w:ind w:left="1440" w:hanging="360"/></w:pPr></w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="1">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl>
              </w:abstractNum>
              <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
              <w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """;

        return Package(new()
        {
            ["[Content_Types].xml"] = Text(ContentTypes(
                ("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
                ("/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"),
                ("/word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"),
                ("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"))),
            ["_rels/.rels"] = Text(Relationships(
                ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml", false),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml", false))),
            ["word/document.xml"] = Text(document),
            ["word/styles.xml"] = Text(styles),
            ["word/numbering.xml"] = Text(numbering),
            ["word/_rels/document.xml.rels"] = Text(Relationships(
                ("rIdStyles", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml", false),
                ("rIdNumbering", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering", "numbering.xml", false),
                ("rIdLink", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", "https://example.org/docs", true),
                ("rIdImage1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image1.png", false),
                ("rIdImage2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image2.png", false))),
            ["word/media/image1.png"] = Chart,
            ["word/media/image2.png"] = Chart,
            ["docProps/core.xml"] = Text(CoreProperties("Sample document", "DocLibs")),
        });
    }

    private static string Drawing(int id, string relationship, string description)
    {
        const long width = 24 * 9525 * 4;
        const long height = 16 * 9525 * 4;
        return $"""
            <w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{width}" cy="{height}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:docPr id="{id}" name="Picture {id}" descr="{description}"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect="1"/></wp:cNvGraphicFramePr><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="{id}" name="image{id}.png"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="{relationship}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{width}" cy="{height}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>
            """;
    }

    // Only the parts the converter reads; PowerPoint itself would also want a slide master and theme.
    public static byte[] Pptx()
    {
        const string ns = """xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """;

        static string Shape(int id, string? placeholder, params string[] paragraphs) =>
            $"""<p:sp><p:nvSpPr><p:cNvPr id="{id}" name="Shape {id}"/><p:cNvSpPr/><p:nvPr>{(placeholder is null ? string.Empty : $"""<p:ph type="{placeholder}"/>""")}</p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/>{string.Concat(paragraphs.Select(t => $"<a:p><a:r><a:t>{t}</a:t></a:r></a:p>"))}</p:txBody></p:sp>""";

        static string Slide(string ns, string content) =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sld {ns}><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>{content}</p:spTree></p:cSld></p:sld>""";

        var slide1 = Slide(ns, Shape(2, "ctrTitle", "Quarterly review") + Shape(3, "subTitle", "Generated sample presentation"));
        var slide2 = Slide(ns, Shape(2, "title", "Highlights")
            + Shape(3, "body", "Revenue grew in every region", "Two new products shipped", "Costs stayed flat")
            + """<p:pic><p:nvPicPr><p:cNvPr id="4" name="Picture 4" descr="Team photo"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed="rIdPhoto"/></p:blipFill><p:spPr/></p:pic>""");
        var slide3 = Slide(ns, Shape(2, null, "A slide without a title")
            + """<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id="3" name="Table 3"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table"><a:tbl>"""
            + """<a:tr h="0"><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>Region</a:t></a:r></a:p></a:txBody></a:tc><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>Growth</a:t></a:r></a:p></a:txBody></a:tc></a:tr>"""
            + """<a:tr h="0"><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>North</a:t></a:r></a:p></a:txBody></a:tc><a:tc><a:txBody><a:bodyPr/><a:p><a:r><a:t>4 %</a:t></a:r></a:p></a:txBody></a:tc></a:tr>"""
            + """</a:tbl></a:graphicData></a:graphic></p:graphicFrame>""");

        var presentation = $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:presentation {ns}><p:sldIdLst><p:sldId id="256" r:id="rId1"/><p:sldId id="257" r:id="rId2"/><p:sldId id="258" r:id="rId3"/></p:sldIdLst><p:sldSz cx="12192000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>""";
        const string slideType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";

        return Package(new()
        {
            ["[Content_Types].xml"] = Text(ContentTypes(
                ("/ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"),
                ("/ppt/slides/slide1.xml", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"),
                ("/ppt/slides/slide2.xml", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"),
                ("/ppt/slides/slide3.xml", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"),
                ("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"))),
            ["_rels/.rels"] = Text(Relationships(
                ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "ppt/presentation.xml", false),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml", false))),
            ["ppt/presentation.xml"] = Text(presentation),
            ["ppt/_rels/presentation.xml.rels"] = Text(Relationships(
                ("rId1", slideType, "slides/slide1.xml", false),
                ("rId2", slideType, "slides/slide2.xml", false),
                ("rId3", slideType, "slides/slide3.xml", false))),
            ["ppt/slides/slide1.xml"] = Text(slide1),
            ["ppt/slides/slide2.xml"] = Text(slide2),
            ["ppt/slides/_rels/slide2.xml.rels"] = Text(Relationships(
                ("rIdPhoto", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/image1.png", false))),
            ["ppt/slides/slide3.xml"] = Text(slide3),
            ["ppt/media/image1.png"] = Photo,
            ["docProps/core.xml"] = Text(CoreProperties("Quarterly review", "DocLibs")),
        });
    }

    public static byte[] Xlsx()
    {
        const string ns = """xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """;

        static string Cell(string reference, object value) => value switch
        {
            string s => $"""<c r="{reference}" t="inlineStr"><is><t>{s}</t></is></c>""",
            bool b => $"""<c r="{reference}" t="b"><v>{(b ? 1 : 0)}</v></c>""",
            _ => $"""<c r="{reference}"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>""",
        };

        var prices = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet {ns}><sheetData>
            <row r="1">{Cell("A1", "Item")}{Cell("B1", "Price")}{Cell("C1", "In stock")}</row>
            <row r="2">{Cell("A2", "Apple")}{Cell("B2", 1.2)}{Cell("C2", true)}</row>
            <row r="3">{Cell("A3", "Pear | green")}{Cell("B3", 0.9)}{Cell("C3", false)}</row>
            <row r="4">{Cell("A4", "customfield_10682")}{Cell("B4", 1000000)}<c r="C4" t="e"><v>#DIV/0!</v></c></row>
            </sheetData></worksheet>
            """;
        var empty = $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet {ns}><sheetData/></worksheet>""";
        var workbook = $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook {ns}><sheets><sheet name="Prices" sheetId="1" r:id="rId1"/><sheet name="Empty" sheetId="2" r:id="rId2"/></sheets></workbook>""";
        const string sheetType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

        return Package(new()
        {
            ["[Content_Types].xml"] = Text(ContentTypes(
                ("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                ("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                ("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                ("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"))),
            ["_rels/.rels"] = Text(Relationships(
                ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml", false),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml", false))),
            ["xl/workbook.xml"] = Text(workbook),
            ["xl/_rels/workbook.xml.rels"] = Text(Relationships(
                ("rId1", sheetType, "worksheets/sheet1.xml", false),
                ("rId2", sheetType, "worksheets/sheet2.xml", false))),
            ["xl/worksheets/sheet1.xml"] = Text(prices),
            ["xl/worksheets/sheet2.xml"] = Text(empty),
            ["docProps/core.xml"] = Text(CoreProperties("Price list", "DocLibs")),
        });
    }

    public static string Rtf() =>
        """
        {\rtf1\ansi\deff0
        {\fonttbl{\f0\fswiss Arial;}}
        {\stylesheet{\s0 Normal;}{\s1\b\fs32 heading 1;}}
        {\info{\title Sample RTF}{\author DocLibs}}
        \pard\s1\b\fs32 Rich text sample\par
        \pard\s0\b0\fs22 This paragraph has \b bold\b0  and \i italic\i0  text.\par
        \trowd\cellx3000\cellx6000
        \intbl Item\cell Price\cell\row
        \trowd\cellx3000\cellx6000
        \intbl Apple\cell 1.20\cell\row
        \pard\s0 The end.\par
        }
        """;

    public static byte[] Zip() => Package(new()
    {
        ["readme.txt"] = Text("A plain ZIP archive, not an Office document.\n"),
    });

    private static string ContentTypes(params (string Part, string Type)[] overrides) =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">"""
        + """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/>"""
        + string.Concat(overrides.Select(o => $"""<Override PartName="{o.Part}" ContentType="{o.Type}"/>"""))
        + "</Types>";

    private static string Relationships(params (string Id, string Type, string Target, bool External)[] relationships) =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
        + string.Concat(relationships.Select(r => $"""<Relationship Id="{r.Id}" Type="{r.Type}" Target="{r.Target}"{(r.External ? " TargetMode=\"External\"" : string.Empty)}/>"""))
        + "</Relationships>";

    private static string CoreProperties(string title, string creator) =>
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>{title}</dc:title><dc:creator>{creator}</dc:creator></cp:coreProperties>""";

    private static byte[] Text(string text) => new UTF8Encoding(false).GetBytes(text);

    private static byte[] Package(Dictionary<string, byte[]> parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = Timestamp;
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }
}

// Minimal RGB PNG encoder, so the samples need no imaging library.
internal static class Png
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    public static byte[] Create(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var raw = new byte[height * ((width * 3) + 1)];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                raw[offset++] = r;
                raw[offset++] = g;
                raw[offset++] = b;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var buffer = new byte[4];
        WriteBigEndian(buffer, 0, (uint)data.Length);
        output.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes.Concat(data))
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        WriteBigEndian(buffer, 0, crc ^ 0xFFFFFFFFu);
        output.Write(buffer);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}

// Late-bound Word and Excel, used only to save the generated .docx and .xlsx in the legacy formats.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class Office
{
    private const int WordFormatDocument = 0;
    private const int ExcelFormatExcel8 = 56;

    public static void SaveAsDoc(string source, string target) => With("Word.Application", word =>
    {
        Set(word, "DisplayAlerts", 0);
        var document = Call(Get(word, "Documents")!, "Open", source, false, true)!;
        try
        {
            Call(document, "SaveAs2", target, WordFormatDocument);
        }
        finally
        {
            Call(document, "Close", 0);
        }
    });

    public static void SaveAsXls(string source, string target) => With("Excel.Application", excel =>
    {
        Set(excel, "DisplayAlerts", false);
        var workbook = Call(Get(excel, "Workbooks")!, "Open", source, 0, true)!;
        try
        {
            Call(workbook, "SaveAs", target, ExcelFormatExcel8);
        }
        finally
        {
            Call(workbook, "Close", false);
        }
    });

    private static void With(string progId, Action<object> action)
    {
        var application = Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new NotSupportedException(progId + " is not installed."))!;
        try
        {
            Set(application, "Visible", false);
            action(application);
        }
        finally
        {
            Call(application, "Quit");
        }
    }

    // No culture argument: Excel rejects calls made with the invariant culture's LCID.
    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string name, object? value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);
}
