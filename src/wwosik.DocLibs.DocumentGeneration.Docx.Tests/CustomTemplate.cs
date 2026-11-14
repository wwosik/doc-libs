using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>
/// <c>custom</c>: a caller's <c>.dotx</c> like a localized house template, built from the built-in neutral template at
/// test time, so it needs no binary sample. Its style ids are German (<c>berschrift1</c>, <c>Titel</c>, …) while the
/// names stay Word's, the subtitle style has a house name (<see cref="SubtitleName"/>) that the map names, the cover
/// details, TOC heading and deck-syntax styles are missing, and the content header holds a <c>TITLE</c> field and a content control
/// bound to the author.
/// </summary>
internal static class CustomTemplate
{
    public const string SubtitleName = "Untertitel Haus";

    // Before Content, which Build reads it for: static fields are set in the order they are written.
    /// <summary>The localized id of each built-in id.</summary>
    public static readonly IReadOnlyDictionary<string, string> Ids = new Dictionary<string, string>
    {
        ["Normal"] = "Standard",
        ["Title"] = "Titel",
        ["Subtitle"] = "UntertitelHaus",
        ["Heading1"] = "berschrift1",
        ["Heading2"] = "berschrift2",
        ["Heading3"] = "berschrift3",
        ["Heading4"] = "berschrift4",
        ["Heading5"] = "berschrift5",
        ["Heading6"] = "berschrift6",
        ["TOC1"] = "Verzeichnis1",
        ["TOC2"] = "Verzeichnis2",
        ["TOC3"] = "Verzeichnis3",
        ["Header"] = "Kopfzeile",
        ["Footer"] = "Fuzeile",
    };

    public static readonly byte[] Content = Build();

    public static DocxStyleNames Styles => new() { Subtitle = SubtitleName, Heading2 = "Heading 2" };

    public static CustomDocxTemplateOptions Options => new(new MemoryStream(Content)) { Styles = Styles };

    private static byte[] Build()
    {
        using var buffer = new MemoryStream();
        buffer.Write(Samples.TemplateBytes(new BrandDocxTemplateOptions { Brand = Brand.Neutral with { Name = "plain" }, ShowDate = false }));
        using (var package = WordprocessingDocument.Open(buffer, true))
        {
            var main = package.MainDocumentPart!;
            var styles = main.StyleDefinitionsPart!.Styles!;
            // Missing as in a house template: the cover details, the TOC heading and every style of the deck syntax.
            foreach (var id in new[] { "CoverDetails", "TOCHeading" }.Concat(DocxRoles.All.Skip(DocxRoles.All.ToList().IndexOf(DocxRoles.Hyperlink) + 1).Select(r => r.Id)))
            {
                styles.Elements<Style>().Single(s => s.StyleId == id).Remove();
            }

            styles.Elements<Style>().Single(s => s.StyleId == "Subtitle").StyleName!.Val = SubtitleName;
            foreach (var element in new OpenXmlElement[] { styles }.Concat(main.HeaderParts.Select(h => h.Header!)).Concat(main.FooterParts.Select(f => f.Footer!)))
            {
                foreach (var reference in element.Descendants().SelectMany(e => e.GetAttributes().Select(a => (e, a))).ToList())
                {
                    if (reference.a.LocalName is "styleId" or "val" && Ids.TryGetValue(reference.a.Value ?? string.Empty, out var localized)
                        && reference.e is Style or ParagraphStyleId or BasedOn or NextParagraphStyle)
                    {
                        reference.e.SetAttribute(new OpenXmlAttribute(reference.a.Prefix, reference.a.LocalName, reference.a.NamespaceUri, localized));
                    }
                }
            }

            styles.Save();

            // The content header: a TITLE field, and the author in a content control bound to the core properties.
            var header = main.HeaderParts.Single(h => main.GetIdOfPart(h) == "rId5");
            header.Header = new Header(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Kopfzeile" }),
                new SimpleField(new Run(new Text("Old title"))) { Instruction = " TITLE \\* MERGEFORMAT " },
                new Run(new TabChar()),
                new SdtRun(
                    new SdtProperties(
                        new SdtAlias { Val = "Author" },
                        new ShowingPlaceholder(),
                        new DataBinding
                        {
                            PrefixMappings = "xmlns:ns0='http://purl.org/dc/elements/1.1/' xmlns:ns1='http://schemas.openxmlformats.org/package/2006/metadata/core-properties'",
                            XPath = "/ns1:coreProperties[1]/ns0:creator[1]",
                            StoreItemId = "{6C3C8BC8-F283-45AE-878A-BAB7291924A1}",
                        },
                        new SdtContentText()),
                    new SdtContentRun(new Run(new Text("[Author]"))))));
            header.Header.Save();
        }

        // A .dotx as Word saves it: the main part keeps its name, only its content type says template.
        DocxPackage.ChangeMainType(buffer, DocxPackage.DocumentType, DocxPackage.TemplateType);
        return buffer.ToArray();
    }
}
