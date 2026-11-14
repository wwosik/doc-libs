using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

public class DocxGeneratorTests
{
    public static TheoryData<string, string> SamplesAndTemplates
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var sample in new[] { "report", "long", "layouts" })
            {
                foreach (var template in new[] { "neutral", "sample", "custom" })
                {
                    data.Add(sample, template);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(SamplesAndTemplates))]
    public void Samples_AreValid_AndRoundTripThroughTheDocxConverter(string sample, string template)
    {
        var docx = Samples.Generate(Samples.Document(sample + ".md"), new DocxOptions { Template = Samples.Template(template), TableOfContents = true });
        Samples.WriteForReview($"{sample}-{template}.docx", docx);

        Samples.AssertValid(docx);
        Samples.AssertMatchesExpected($"{sample}-{template}.roundtrip", Samples.RoundTrip(docx));
    }

    [Fact]
    public void Result_HasNoPages_AndNoNotes()
    {
        var (_, result) = Samples.GenerateWithResult(Samples.Document("report.md"));

        Assert.Equal(0, result.Pages);
        Assert.False(result.HasNotes);
    }

    [Fact]
    public void Cover_HasTitleSubtitleAuthorAndDate_ThenAPageBreak()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Document("report.md")));
        var paragraphs = Body(package).Elements<Paragraph>().ToList();

        Assert.Equal(("Title", "Quarterly review"), (Style(paragraphs[0]), Texts(paragraphs[0])));
        Assert.Equal(("Subtitle", "Generated from Markdown for the DocLibs team."), (Style(paragraphs[1]), Texts(paragraphs[1])));
        Assert.Equal("CoverDetails", Style(paragraphs[2]));
        Assert.Equal(["DocLibs", "09.03.2026"], paragraphs[2].Descendants<Text>().Select(t => t.Text));
        Assert.Contains(paragraphs[2].Descendants<Break>(), b => b.Type?.Value == BreakValues.Page);
        Assert.Equal(("Heading2", "Summary"), (Style(paragraphs[3]), Texts(paragraphs[3])));
    }

    [Fact]
    public void ADocumentWithoutCover_HasNoFirstPageOfItsOwn()
    {
        using var withCover = Samples.Open(Samples.Generate(Samples.Markdown("# Cover\n\n## A\n\nText\n")));
        using var without = Samples.Open(Samples.Generate(Samples.Markdown("## A\n\nText\n")));

        Assert.NotNull(Section(withCover).GetFirstChild<TitlePage>());
        Assert.Null(Section(without).GetFirstChild<TitlePage>());
        Assert.Equal("Heading1", Style(Body(without).Elements<Paragraph>().First()));
    }

    [Fact]
    public void BuiltInTemplate_HasHeadersAndFootersForTheCoverAndTheContentPages()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Document("report.md"), new DocxOptions { Template = Samples.Template("sample") }));
        var main = package.MainDocumentPart!;
        var section = Section(package);
        HeaderPart HeaderOf(HeaderFooterValues type) =>
            (HeaderPart)main.GetPartById(section.Elements<HeaderReference>().Single(r => r.Type?.Value == type).Id!);
        FooterPart FooterOf(HeaderFooterValues type) =>
            (FooterPart)main.GetPartById(section.Elements<FooterReference>().Single(r => r.Type?.Value == type).Id!);

        var cover = HeaderOf(HeaderFooterValues.First);
        Assert.Equal(["Cover artwork", "Logo", "Tagline"], cover.Header!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>().Select(d => d.Name!.Value));
        Assert.Equal(3, cover.ImageParts.Count());
        Assert.Equal(string.Empty, FooterOf(HeaderFooterValues.First).Footer!.InnerText);

        var header = HeaderOf(HeaderFooterValues.Default);
        Assert.Equal("Quarterly review", Texts(header.Header!));
        Assert.Single(header.ImageParts);
        var footer = FooterOf(HeaderFooterValues.Default).Footer!;
        Assert.Contains("Sample Corporation", Texts(footer), StringComparison.Ordinal);
        Assert.Contains("09.03.2026", Texts(footer), StringComparison.Ordinal);
        Assert.Equal([" PAGE ", " NUMPAGES "], footer.Descendants<SimpleField>().Select(f => f.Instruction!.Value));
    }

    [Fact]
    public void ShowDateAndShowTagline_LeaveThemOut()
    {
        var markdown = Samples.Markdown("---\ntemplate: sample\nshowDate: false\nshowTagline: false\n---\n# Cover\n\n## A\n");
        using var package = Samples.Open(Samples.Generate(markdown, new DocxOptions { Brands = [Samples.Brand] }));
        var main = package.MainDocumentPart!;

        Assert.DoesNotContain(main.FooterParts, f => Texts(f.Footer!).Contains("09.03.2026", StringComparison.Ordinal));
        Assert.DoesNotContain(Body(package).Descendants<Text>(), t => t.Text == "09.03.2026");
        Assert.DoesNotContain(main.HeaderParts.SelectMany(h => h.Header!.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>()), d => d.Name == "Tagline");
    }

    [Theory]
    [InlineData(DocxPageSize.A4, DocxOrientation.Portrait, 11906, 16838)]
    [InlineData(DocxPageSize.Letter, DocxOrientation.Portrait, 12240, 15840)]
    [InlineData(DocxPageSize.A3, DocxOrientation.Landscape, 23811, 16838)]
    public void PageSize_AndOrientation_SetThePage(DocxPageSize size, DocxOrientation orientation, int width, int height)
    {
        var docx = Samples.Generate(Samples.Markdown("# T\n\n## A\n"), new DocxOptions { Template = new BrandDocxTemplateOptions { PageSize = size, Orientation = orientation } });
        using var package = Samples.Open(docx);
        var page = Section(package).GetFirstChild<PageSize>()!;

        Assert.Equal(((uint)width, (uint)height), (page.Width!.Value, page.Height!.Value));
        Assert.Equal(orientation == DocxOrientation.Landscape, page.Orient?.Value == PageOrientationValues.Landscape);
        Samples.AssertValid(docx);
    }

    [Fact]
    public void CoreProperties_HaveTitleAuthorLanguageAndDates()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Document("report.md")));
        var core = Core(package);

        Assert.Equal("Quarterly review", core.Title);
        Assert.Equal("DocLibs", core.Creator);
        Assert.Equal("en", core.Language);
        Assert.Equal("2026-03-09T12:00:00Z", core.Created);
        Assert.Equal("2026-03-09T12:00:00Z", core.Modified);
    }

    [Fact]
    public void FrontMatter_SetsTitleAuthorAndLanguage()
    {
        var markdown = Samples.Markdown("---\ntitle: Override\nauthor: Team Docs\nlang: de\ntoc: true\n---\n# Bericht\n\n## Teil\n");
        using var package = Samples.Open(Samples.Generate(markdown));
        var core = Core(package);

        Assert.Equal(("Override", "Team Docs", "de"), (core.Title, core.Creator, core.Language));
        Assert.Contains(Body(package).Elements<Paragraph>(), p => Style(p) == "TOCHeading" && Texts(p) == "Inhalt");
    }

    [Fact]
    public void AnUnknownLanguage_GivesANote()
    {
        var (_, result) = Samples.GenerateWithResult(Samples.Markdown("---\nlang: pl\n---\n## A\n"));

        Assert.Equal(NoteCodes.LanguageUnknown, Assert.Single(result.Notes).Code);
    }

    [Fact]
    public void TableOfContents_IsAFieldPrefilledWithTheHeadings_DownToItsDepth()
    {
        var markdown = Samples.Markdown("---\ntoc: true\n---\n# Cover\n\n## One\n\n### One a\n\n#### Deep\n\n## Two\n\nText\n");
        var docx = Samples.Generate(markdown);
        using var package = Samples.Open(docx);
        var paragraphs = Body(package).Elements<Paragraph>().ToList();
        var heading = paragraphs.FindIndex(p => Style(p) == "TOCHeading");

        Assert.Equal("Contents", Texts(paragraphs[heading]));
        Assert.Equal(" TOC \\o \"1-2\" \\h \\z \\u ", Assert.Single(Body(package).Descendants<FieldCode>()).Text);
        var entries = paragraphs.Skip(heading + 1).TakeWhile(p => Style(p).StartsWith("TOC", StringComparison.Ordinal)).ToList();
        Assert.Equal([("TOC1", "1 One"), ("TOC2", "1.1 One a"), ("TOC1", "2 Two")], entries.Select(p => (Style(p), Texts(p))));
        Assert.Equal(FieldCharValues.Begin, entries[0].Descendants<FieldChar>().First().FieldCharType!.Value);
        Assert.Equal(FieldCharValues.End, entries[^1].Descendants<FieldChar>().Last().FieldCharType!.Value);
        Assert.Contains(entries[^1].Descendants<Break>(), b => b.Type?.Value == BreakValues.Page);
        Assert.NotNull(package.MainDocumentPart!.DocumentSettingsPart!.Settings!.GetFirstChild<UpdateFieldsOnOpen>());
        Samples.AssertValid(docx);
    }

    [Fact]
    public void TableOfContentsDepth_FromOptionsOrFrontMatter()
    {
        var markdown = "# Cover\n\n## One\n\n### One a\n\n#### Deep\n";
        using var fromOptions = Samples.Open(Samples.Generate(Samples.Markdown(markdown), new DocxOptions { TableOfContents = true, TableOfContentsDepth = 3 }));
        using var fromFrontMatter = Samples.Open(Samples.Generate(Samples.Markdown("---\ntoc: true\ntocDepth: 1\n---\n" + markdown)));

        Assert.Contains("\"1-3\"", Assert.Single(Body(fromOptions).Descendants<FieldCode>()).Text, StringComparison.Ordinal);
        Assert.Equal(["1 One", "1.1 One a", "1.1.1 Deep"], Body(fromOptions).Elements<Paragraph>().Where(p => Style(p).StartsWith("TOC", StringComparison.Ordinal) && Style(p) != "TOCHeading").Select(p => Texts(p)));
        Assert.Contains("\"1-1\"", Assert.Single(Body(fromFrontMatter).Descendants<FieldCode>()).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTableOfContents_NothingAsksToUpdateFields()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Document("report.md")));

        Assert.Empty(Body(package).Descendants<FieldCode>());
        Assert.Null(package.MainDocumentPart!.DocumentSettingsPart!.Settings!.GetFirstChild<UpdateFieldsOnOpen>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void TableOfContentsDepth_OutsideOneToThree_Throws(int depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Samples.Generate(Samples.Markdown("## A\n"), new DocxOptions { TableOfContentsDepth = depth }));
    }

    [Fact]
    public void BuiltInStyles_FollowThePdfsSizesAndTheBrandsColours()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## A\n"), new DocxOptions { Template = Samples.Template("sample") }));
        var styles = package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToDictionary(s => s.StyleId!.Value!);

        Assert.Equal(["40", "32", "27", "24", "22", "21"], Enumerable.Range(1, 6).Select(l => styles["Heading" + l].StyleRunProperties!.FontSize!.Val!.Value));
        Assert.All(Enumerable.Range(1, 6), l => Assert.Equal("0E7C86", styles["Heading" + l].StyleRunProperties!.Color!.Val!.Value));
        Assert.All(Enumerable.Range(1, 6), l => Assert.NotNull(styles["Heading" + l].StyleParagraphProperties!.KeepNext));
        Assert.Equal("21", package.MainDocumentPart.StyleDefinitionsPart.Styles.DocDefaults!.RunPropertiesDefault!.RunPropertiesBaseStyle!.FontSize!.Val!.Value);
        Assert.Null(styles["Normal"].StyleRunProperties);
        Assert.Equal("60", styles["Title"].StyleRunProperties!.FontSize!.Val!.Value);
        Assert.Equal("heading 1", styles["Heading1"].StyleName!.Val!.Value);
        Assert.Equal("toc 2", styles["TOC2"].StyleName!.Val!.Value);
    }

    [Fact]
    public void Theme_HasTheBrandsColoursAndFont()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## A\n"), new DocxOptions { Template = Samples.Template("sample") }));
        var theme = package.MainDocumentPart!.ThemePart!.Theme!;

        Assert.Equal("Liberation Serif", theme.ThemeElements!.FontScheme!.MinorFont!.LatinFont!.Typeface!.Value);
        Assert.Equal("0E7C86", theme.ThemeElements.ColorScheme!.Dark2Color!.RgbColorModelHex!.Val!.Value);
    }

    [Fact]
    public void GeneratingTwice_GivesTheSameDocument()
    {
        var one = Samples.Generate(Samples.Document("report.md"), new DocxOptions { Template = Samples.Template("sample"), TableOfContents = true });
        var two = Samples.Generate(Samples.Document("report.md"), new DocxOptions { Template = Samples.Template("sample"), TableOfContents = true });

        Assert.Equal(Parts(one), Parts(two));
    }

    [Fact]
    public void ADamagedTemplate_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown("## A\n"),
            new DocxOptions { Template = new CustomDocxTemplateOptions(new MemoryStream([1, 2, 3])) }));

        Assert.Equal(Format.Docx, error.Format);
        Assert.Contains("The template is damaged (the caller's template)", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The text of an element: its <c>w:t</c> only, not field codes or drawing positions.</summary>
    internal static string Texts(DocumentFormat.OpenXml.OpenXmlElement element) => string.Concat(element.Descendants<Text>().Select(t => t.Text));

    internal static Body Body(WordprocessingDocument package) => package.MainDocumentPart!.Document!.Body!;

    internal static SectionProperties Section(WordprocessingDocument package) => Body(package).Elements<SectionProperties>().Single();

    internal static string Style(Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;

    internal static (string? Title, string? Creator, string? Language, string? Created, string? Modified) Core(WordprocessingDocument package)
    {
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace terms = "http://purl.org/dc/terms/";
        using var stream = package.CoreFilePropertiesPart!.GetStream();
        var root = XDocument.Load(stream).Root!;
        return (root.Element(dc + "title")?.Value, root.Element(dc + "creator")?.Value, root.Element(dc + "language")?.Value,
            root.Element(terms + "created")?.Value, root.Element(terms + "modified")?.Value);
    }

    // Every part's content with the SDK's random relationship ids masked.
    internal static List<string> Parts(byte[] docx)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
        return [.. archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).Select(e =>
        {
            using var reader = new StreamReader(e.Open());
            var text = e.FullName.EndsWith(".odttf", StringComparison.Ordinal) || e.FullName.EndsWith(".png", StringComparison.Ordinal)
                ? $"{reader.ReadToEnd().Length}"
                : System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "R[0-9a-f]{16}", "R*");
            return System.Text.RegularExpressions.Regex.Replace(e.FullName, "R[0-9a-f]{16}", "R*") + ": " + text;
        })];
    }
}
