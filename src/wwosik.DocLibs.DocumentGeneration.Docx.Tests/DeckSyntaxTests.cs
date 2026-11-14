using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using static wwosik.DocLibs.DocumentGeneration.Docx.Tests.DocxGeneratorTests;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>The syntax written for decks in Word (E09-S02): alerts, containers, marks, labels, image rows, page breaks, the marking.</summary>
public class DeckSyntaxTests
{
    [Theory]
    [InlineData("NOTE", "Note", "en", "Note")]
    [InlineData("TIP", "Tip", "en", "Tip")]
    [InlineData("IMPORTANT", "Important", "en", "Important")]
    [InlineData("WARNING", "Warning", "de", "Warnung")]
    [InlineData("CAUTION", "Caution", "de", "Vorsicht")]
    public void EachAlert_IsInItsStyle_WithItsTitleInStrong(string kind, string style, string language, string title)
    {
        using var package = Open($"---\nlang: {language}\n---\n> [!{kind}]\n> First line.\n>\n> Second paragraph.\n>\n> - An item\n");
        var paragraphs = Paragraphs(package);

        var first = paragraphs[0];
        Assert.Equal(style, Style(first));
        Assert.Equal($"{title} First line.", Texts(first));
        var strong = first.Descendants<Run>().First();
        Assert.Equal("Strong", strong.RunProperties!.RunStyle!.Val!.Value);
        Assert.Equal(title, Texts(strong));
        Assert.Equal(style, Style(paragraphs[1]));

        // The list keeps its style and takes the bar and shading of the alert.
        var item = paragraphs[2];
        Assert.Equal("ListBullet", Style(item));
        Assert.NotNull(item.ParagraphProperties!.ParagraphBorders!.LeftBorder);
        Assert.NotNull(item.ParagraphProperties.Shading);
    }

    [Fact]
    public void AlertStyles_HaveABarOnTheLeft_InThePrimaryOrTheHighlightColour()
    {
        using var package = Open("Text\n");
        var styles = Styles(package);
        var brand = ResolvedBrand.Neutral;

        Assert.Equal(brand.Primary, styles["Note"].StyleParagraphProperties!.ParagraphBorders!.LeftBorder!.Color!.Value);
        Assert.Equal(brand.TableBand, styles["Tip"].StyleParagraphProperties!.Shading!.Fill!.Value);
        Assert.Equal(brand.Highlight, styles["Warning"].StyleParagraphProperties!.ParagraphBorders!.LeftBorder!.Color!.Value);
        Assert.Equal(ResolvedBrand.Mix(brand.Highlight, 0.15), styles["Caution"].StyleParagraphProperties!.Shading!.Fill!.Value);
    }

    [Fact]
    public void ACardsHeading_IsAContainerHeading_NotNumberedAndNotInTheContents()
    {
        var docx = Generate("# Cover\n\n## Section\n\n::: cards\n### First card\n**26 tables**\n\nText.\n\n### Second card\nMore.\n:::\n", new DocxOptions { TableOfContents = true });
        using var package = Samples.Open(docx);
        var paragraphs = Paragraphs(package);
        var styles = Styles(package);

        var heading = paragraphs.Single(p => Texts(p) == "First card");
        Assert.Equal("ContainerHeading", Style(heading));
        Assert.Null(styles["ContainerHeading"].StyleParagraphProperties!.OutlineLevel);
        Assert.Null(styles["ContainerHeading"].StyleParagraphProperties!.NumberingProperties);
        Assert.Equal("Statement", Style(paragraphs.Single(p => Texts(p) == "26 tables")));
        Assert.DoesNotContain(paragraphs, p => Style(p).StartsWith("TOC", StringComparison.Ordinal) && Texts(p).Contains("card", StringComparison.Ordinal));
        Assert.Equal("Heading1", Style(paragraphs.Single(p => Texts(p) == "Section")));
        Samples.AssertValid(docx);
    }

    [Fact]
    public void ShortCardsAndRows_AreTermsAndDescriptions()
    {
        using var package = Open("::: rows\n- **Fast** – it streams\n- **Simple:** storage only\n:::\n\n::: cards\n- **Locking** – fixed\n:::\n");
        var paragraphs = Paragraphs(package);

        Assert.Equal(
            [("DefinitionTerm", "Fast"), ("Definition", "it streams"), ("DefinitionTerm", "Simple"), ("Definition", "storage only"), ("DefinitionTerm", "Locking"), ("Definition", "fixed")],
            paragraphs.Select(p => (Style(p), Texts(p))));
        Assert.DoesNotContain(paragraphs.SelectMany(p => p.Descendants<Bold>()), b => true);
    }

    [Fact]
    public void Panels_Statements_Columns_AndTags_TakeTheirStyles()
    {
        using var package = Open(
            "::: panel\nIn a box.\n\nStill in it.\n:::\n\n::: panel dark\n### Dark title\nOn dark.\n:::\n\n"
            + "::: statement\nThe statement.\n\nThe rest.\n:::\n\n::: columns\nLeft.\n\n---\n\nRight.\n:::\n\n"
            + "::: cards\n### Tags\n`a` `b` `c`\n\n- `write-once` results\n:::\n");
        var paragraphs = Paragraphs(package);
        string StyleOf(string text) => Style(paragraphs.Single(p => Texts(p) == text));

        Assert.Equal("Panel", StyleOf("In a box."));
        Assert.Equal("Panel", StyleOf("Still in it."));
        Assert.Equal("PanelDark", StyleOf("On dark."));
        Assert.Equal("PanelDark", StyleOf("Dark title"));
        Assert.Equal("Statement", StyleOf("The statement."));
        Assert.Equal("Normal", StyleOf("The rest."));
        Assert.Equal("Normal", StyleOf("Left."));
        Assert.Equal("Normal", StyleOf("Right."));
        Assert.DoesNotContain(paragraphs, p => Style(p) == "Rule");
        var tags = paragraphs.Single(p => Texts(p) == "a b c").Descendants<Run>().Where(r => Texts(r).Trim().Length > 0).ToList();
        Assert.All(tags, r => Assert.Equal("Tag", r.RunProperties!.RunStyle!.Val!.Value));
        var item = paragraphs.Single(p => Texts(p) == "write-once results");
        Assert.Equal("Tag", item.Descendants<Run>().First().RunProperties!.RunStyle!.Val!.Value);
    }

    [Fact]
    public void Marks_ColouredWords_AndHighlightedRows()
    {
        using var package = Open(
            "Some ==marked== text and [done]{.success} and [late]{.danger}.\n\n| Status | Who |\n| --- | --- |\n| ==Waiting== | Human |\n| Created | System |\n");
        var paragraph = Paragraphs(package).First();
        string RunStyleOf(string text) => paragraph.Descendants<Run>().Single(r => Texts(r) == text).RunProperties!.RunStyle!.Val!.Value!;
        var styles = Styles(package);

        Assert.Equal("Mark", RunStyleOf("marked"));
        Assert.Equal("ColourSuccess", RunStyleOf("done"));
        Assert.Equal("ColourDanger", RunStyleOf("late"));
        Assert.Equal(ResolvedBrand.Mix(ResolvedBrand.Neutral.Highlight, 0.15), styles["Mark"].StyleRunProperties!.Shading!.Fill!.Value);
        Assert.Equal(ResolvedBrand.Neutral.Labels.Success, styles["ColourSuccess"].StyleRunProperties!.Color!.Val!.Value);

        var rows = Body(package).Descendants<TableRow>().ToList();
        Assert.All(rows[1].Elements<TableCell>(), c => Assert.Equal(ResolvedBrand.Mix(ResolvedBrand.Neutral.Highlight, 0.15), c.TableCellProperties!.Shading!.Fill!.Value));
        Assert.All(rows[2].Elements<TableCell>(), c => Assert.Null(c.TableCellProperties!.Shading));
    }

    [Fact]
    public void ACodeCellThatLabelsName_IsAStatusLabel_AnyOtherStaysCode()
    {
        using var package = Open("---\nlabels.success: Done\n---\n| Part | Status |\n| --- | --- |\n| Host | `Done` |\n| Queue | `Open` |\n");
        var cells = Body(package).Descendants<TableCell>().ToList();
        string RunStyleOf(string text) => cells.SelectMany(c => c.Descendants<Run>()).Single(r => Texts(r) == text).RunProperties!.RunStyle!.Val!.Value!;

        Assert.Equal("LabelSuccess", RunStyleOf("Done"));
        Assert.Equal("CodeCharacter", RunStyleOf("Open"));
        var label = Styles(package)["LabelSuccess"].StyleRunProperties!;
        Assert.Equal(ResolvedBrand.Neutral.Labels.Success, label.Color!.Val!.Value);
        Assert.Equal(ResolvedBrand.Mix(ResolvedBrand.Neutral.Labels.Success, 0.22), label.Shading!.Fill!.Value);
        Assert.NotNull(label.Bold);
    }

    [Fact]
    public void AParagraphOfImages_IsABorderlessRow_OfEqualCells_WithCaptionsInThem()
    {
        var docx = Generate("![Bar chart](diagram.png \"Revenue\") ![Photo](photo.png \"caption\")\n");
        using var package = Samples.Open(docx);
        var table = Body(package).Elements<Table>().Single();

        Assert.Equal("ImageRow", table.GetFirstChild<TableProperties>()!.TableStyle!.Val!.Value);
        var row = table.Elements<TableRow>().Single();
        Assert.NotNull(row.TableRowProperties!.GetFirstChild<CantSplit>());
        var widths = table.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Select(c => int.Parse(c.Width!.Value!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(2, widths.Count);
        Assert.InRange(Math.Abs(widths[0] - widths[1]), 0, 1);
        var cells = row.Elements<TableCell>().ToList();
        Assert.All(cells, c => Assert.Single(c.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>()));
        Assert.Equal(["Figure 1: Revenue", "Figure 2: Photo"], cells.Select(c => Texts(c.Elements<Paragraph>().Last())));
        Assert.All(cells, c => Assert.Equal("Caption", Style(c.Elements<Paragraph>().Last())));
        var borders = Styles(package)["ImageRow"].StyleTableProperties!.TableBorders!;
        Assert.All(borders.ChildElements, b => Assert.Equal("nil", b.GetAttribute("val", b.NamespaceUri).Value));
        Samples.AssertValid(docx);
    }

    [Fact]
    public void PageBreaks_AndNewPage_StartTheNextBlockOnANewPage()
    {
        using var package = Open(
            "## One\n\nText.\n\n::: pagebreak\n:::\n\nAfter the break.\n\n::: pagebreak\n## Held\n\nInside.\n:::\n\n## Two {.newpage}\n\n### Deep {.newpage}\n\n"
            + "| A |\n| --- |\n| b |\n\n::: pagebreak\n\nUnclosed.\n\n## Last\n");
        var body = Body(package);
        bool Breaks(string text) => Paragraphs(package).Single(p => Texts(p) == text).ParagraphProperties?.PageBreakBefore is not null;

        Assert.False(Breaks("One"));
        Assert.False(Breaks("Text."));
        Assert.True(Breaks("After the break."));
        Assert.True(Breaks("Held"));
        Assert.False(Breaks("Inside."));
        Assert.True(Breaks("Two"));
        Assert.True(Breaks("Deep"));
        Assert.True(Breaks("Unclosed."));
        Assert.False(Breaks("Last"));
        Assert.Equal("Heading1", Style(Paragraphs(package).Single(p => Texts(p) == "Last")));
        Assert.DoesNotContain(body.Descendants<Break>(), b => b.Type?.Value == BreakValues.Page);
    }

    [Fact]
    public void APageBreak_AtTheStartOrEnd_GivesNoEmptyPage()
    {
        using var package = Open("::: pagebreak\n:::\n\nFirst.\n\n::: pagebreak\n:::\n");

        Assert.Null(Paragraphs(package).Single().ParagraphProperties!.PageBreakBefore);
    }

    [Theory]
    [InlineData("::: pagebreak now\n:::\n", "'::: pagebreak now' takes no arguments")]
    [InlineData("# Cover {.newpage}\n\nText\n", "{.newpage} cannot go on the cover heading")]
    [InlineData("::: grid\nx\n:::\n", "Unknown container '::: grid'")]
    [InlineData("> [!INFO]\n> x\n", "Unknown alert '[!INFO]'")]
    [InlineData("---\nclassification: secret\n---\nx\n", "'classification' has the value 'secret'")]
    [InlineData("---\ndraft: maybe\n---\nx\n", "'draft' has the value 'maybe'")]
    public void Mistakes_Throw(string markdown, string message)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Confidential_IsInEveryContentHeader_OnTheCover_AndInTheProperties()
    {
        var docx = Generate("---\nclassification: confidential\n---\n# Report\n\n## Text\n\nBody.\n", new DocxOptions { Template = Samples.Template("sample") });
        using var package = Samples.Open(docx);
        var main = package.MainDocumentPart!;
        var section = Section(package);

        var content = Header(main, section, HeaderFooterValues.Default);
        var marking = content.Elements<Paragraph>().First();
        Assert.Equal("Classification", Style(marking));
        Assert.Equal("Confidential", Texts(marking));
        Assert.Equal("ColourDanger", marking.Descendants<RunStyle>().Single().Val!.Value);
        Assert.NotNull(marking.ParagraphProperties!.Indentation?.Right); // clears the logo
        Assert.DoesNotContain("Confidential", Texts(Header(main, section, HeaderFooterValues.First)), StringComparison.Ordinal);

        var cover = Body(package).Elements<Paragraph>().Take(2).ToList();
        Assert.Equal(("ClassificationCover", "Confidential"), (Style(cover[0]), Texts(cover[0])));
        Assert.Equal(("Title", "Report"), (Style(cover[1]), Texts(cover[1])));
        Assert.Equal("Confidential", ContentStatus(package));
        Assert.Empty(content.Descendants<Picture>());
        Samples.AssertValid(docx);
    }

    [Fact]
    public void ADraft_HasWordsWatermark_InEveryHeader_AndTheMarkingInGerman()
    {
        var docx = Generate("---\nlang: de\ndraft: true\n---\n# Bericht\n\n## Text\n\nInhalt.\n", new DocxOptions { Classification = Classification.Internal });
        using var package = Samples.Open(docx);
        var main = package.MainDocumentPart!;

        Assert.Equal("Intern · Entwurf", ContentStatus(package));
        Assert.Equal("Intern · Entwurf", Texts(Header(main, Section(package), HeaderFooterValues.Default).Elements<Paragraph>().First()));
        foreach (var header in main.HeaderParts)
        {
            var xml = header.Header!.OuterXml;
            Assert.Contains("PowerPlusWaterMarkObject", xml, StringComparison.Ordinal);
            Assert.Contains("string=\"ENTWURF\"", xml, StringComparison.Ordinal);
            Assert.Contains("rotation:315", xml, StringComparison.Ordinal);
        }

        Samples.AssertValid(docx);
    }

    [Fact]
    public void FrontMatter_CanRaiseTheHostsClassification_ButNotLowerIt()
    {
        var (lowered, result) = Samples.GenerateWithResult(
            Samples.Markdown("---\nclassification: internal\ndraft: false\n---\nText\n"),
            new DocxOptions { Classification = Classification.Confidential, Draft = true });
        var raised = Generate("---\nclassification: confidential\n---\nText\n", new DocxOptions { Classification = Classification.Internal });
        var plain = Generate("Text\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal((NoteCodes.ClassificationKept, 2), (note.Code, note.Line));
        using (var package = Samples.Open(lowered))
        {
            Assert.Equal("Confidential · Draft", ContentStatus(package));
        }

        using (var package = Samples.Open(raised))
        {
            Assert.Equal("Confidential", ContentStatus(package));
        }

        using (var package = Samples.Open(plain))
        {
            Assert.Null(ContentStatus(package));
            Assert.DoesNotContain(package.MainDocumentPart!.HeaderParts, h => h.Header!.Descendants<ParagraphStyleId>().Any(s => s.Val == "Classification"));
        }
    }

    [Fact]
    public void ACallersTemplate_FillsItsClassificationControl_OrGetsAParagraphInEachHeader()
    {
        var tagged = WithClassificationControl();
        var withControl = Generate("---\nclassification: internal\n---\n## A\n\nText\n", new DocxOptions { Template = new CustomDocxTemplateOptions(new MemoryStream(tagged)) });
        var without = Generate("---\nclassification: internal\n---\n## A\n\nText\n", new DocxOptions { Template = CustomTemplate.Options });

        using (var package = Samples.Open(withControl))
        {
            var control = package.MainDocumentPart!.HeaderParts.SelectMany(h => h.Header!.Descendants<SdtElement>())
                .Single(c => c.SdtProperties!.GetFirstChild<Tag>()?.Val?.Value == "classification");
            Assert.Equal("Internal", Texts(control));
            Assert.DoesNotContain(package.MainDocumentPart.HeaderParts, h => h.Header!.Descendants<ParagraphStyleId>().Any(s => s.Val == "Classification"));
            Samples.AssertValid(withControl);
        }

        using (var package = Samples.Open(without))
        {
            var header = Header(package.MainDocumentPart!, Section(package), HeaderFooterValues.Default);
            Assert.Equal("Internal", Texts(header.Elements<Paragraph>().First()));
            Samples.AssertValid(without);
        }
    }

    [Fact]
    public void ACallersTemplate_GetsTheDeckStyles_InItsThemeColours()
    {
        var docx = Generate("> [!NOTE]\n> x\n", new DocxOptions { Template = CustomTemplate.Options });
        using var package = Samples.Open(docx);
        var styles = Styles(package);

        Assert.Equal("Standard", styles["Note"].BasedOn!.Val!.Value);
        Assert.Equal(ThemeColorValues.Accent1, styles["Note"].StyleParagraphProperties!.ParagraphBorders!.LeftBorder!.ThemeColor!.Value);
        Assert.Equal(ThemeColorValues.Background2, styles["Note"].StyleParagraphProperties!.Shading!.ThemeFill!.Value);
        Assert.Equal(ThemeColorValues.Accent2, styles["Mark"].StyleRunProperties!.Shading!.ThemeFill!.Value);
        Assert.Equal(StyleValues.Character, styles["LabelSuccess"].Type!.Value);
        Assert.Equal(StyleValues.Table, styles["ImageRow"].Type!.Value);
        Samples.AssertValid(docx);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("confidential")]
    public void EachClassification_IsValid_OnEveryTemplate(string classification)
    {
        foreach (var template in new[] { "neutral", "sample", "custom" })
        {
            var markdown = File.ReadAllText(Samples.Path("pages.md")).ReplaceLineEndings("\n")
                .Replace("classification: confidential", "classification: " + classification, StringComparison.Ordinal)
                .Replace("draft: true\n", string.Empty, StringComparison.Ordinal);
            var docx = Samples.Generate(Samples.Markdown(markdown, Samples.Image("diagram.png"), Samples.Image("photo.png")), new DocxOptions { Template = Samples.Template(template) });
            Samples.AssertValid(docx);
            using var package = Samples.Open(docx);
            Assert.Equal(classification == "public" ? null : char.ToUpperInvariant(classification[0]) + classification[1..], ContentStatus(package));
        }
    }

    // The custom template with a content control tagged `classification` in its content header.
    private static byte[] WithClassificationControl()
    {
        using var buffer = new MemoryStream();
        buffer.Write(CustomTemplate.Content);
        DocxPackage.ChangeMainType(buffer, DocxPackage.TemplateType, DocxPackage.DocumentType);
        using (var package = WordprocessingDocument.Open(buffer, true))
        {
            var main = package.MainDocumentPart!;
            var header = main.HeaderParts.Single(h => main.GetIdOfPart(h) == "rId5");
            header.Header!.AppendChild(new Paragraph(new SdtRun(
                new SdtProperties(new SdtAlias { Val = "Classification" }, new Tag { Val = "classification" }, new ShowingPlaceholder(), new SdtContentText()),
                new SdtContentRun(new Run(new Text("[Classification]"))))));
            header.Header.Save();
        }

        DocxPackage.ChangeMainType(buffer, DocxPackage.DocumentType, DocxPackage.TemplateType);
        return buffer.ToArray();
    }

    private static Header Header(MainDocumentPart main, SectionProperties section, HeaderFooterValues type) =>
        ((HeaderPart)main.GetPartById(section.Elements<HeaderReference>().Single(r => r.Type!.Value == type).Id!.Value!)).Header!;

    private static string? ContentStatus(WordprocessingDocument package)
    {
        using var stream = package.CoreFilePropertiesPart!.GetStream();
        var xml = System.Xml.Linq.XDocument.Load(stream);
        return xml.Root!.Element(System.Xml.Linq.XName.Get("contentStatus", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"))?.Value;
    }

    private static byte[] Generate(string markdown, DocxOptions? options = null) =>
        Samples.Generate(Samples.Markdown(markdown, Samples.Image("diagram.png"), Samples.Image("photo.png")), options);

    private static WordprocessingDocument Open(string markdown, DocxOptions? options = null) => Samples.Open(Generate(markdown, options));

    private static List<Paragraph> Paragraphs(WordprocessingDocument package) => [.. Body(package).Descendants<Paragraph>()];

    private static Dictionary<string, Style> Styles(WordprocessingDocument package) =>
        package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);
}
