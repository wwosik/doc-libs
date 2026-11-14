using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using static wwosik.DocLibs.DocumentGeneration.Docx.Tests.DocxGeneratorTests;

namespace wwosik.DocLibs.DocumentGeneration.Docx.Tests;

/// <summary>Caller templates, brands' own templates, and the styles of roles found by name.</summary>
public class TemplateTests
{
    private const string Markdown = "# Cover\n\nThe subtitle\n\n# Part\n\n## Section\n\nText\n";

    [Fact]
    public void ACallerTemplate_IsUsedByStyleName_WithItsLocalizedIds()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Markdown), new DocxOptions { Template = CustomTemplate.Options }));
        var paragraphs = Body(package).Elements<Paragraph>().ToList();

        Assert.Equal(["Titel", "UntertitelHaus"], paragraphs.Take(2).Select(Style));
        Assert.Equal(["berschrift1", "berschrift2", "Standard"], paragraphs.Skip(paragraphs.Count - 3).Select(Style));
        Assert.Equal(WordprocessingDocumentType.Document, package.DocumentType);
    }

    [Fact]
    public void ACallerTemplate_KeepsItsHeadersAndFooters_AndGetsItsPlaceholdersFilled()
    {
        var markdown = Samples.Markdown("---\nauthor: Team Docs\n---\n" + Markdown);
        using var package = Samples.Open(Samples.Generate(markdown, new DocxOptions { Template = CustomTemplate.Options }));
        var main = package.MainDocumentPart!;
        var header = main.HeaderParts.Single(h => h.Header!.Descendants<SimpleField>().Any(f => f.Instruction!.Value!.Contains("TITLE", StringComparison.Ordinal))).Header!;

        Assert.Equal("Cover\tTeam Docs", string.Concat(header.Descendants().Select(e => e is TabChar ? "\t" : e is Text t ? t.Text : string.Empty)));
        Assert.Empty(header.Descendants<ShowingPlaceholder>());
        Assert.NotNull(main.DocumentSettingsPart!.Settings!.GetFirstChild<UpdateFieldsOnOpen>());
        Assert.Equal(4, Section(package).Elements<HeaderReference>().Count() + Section(package).Elements<FooterReference>().Count());
    }

    [Fact]
    public void AMissingRoleStyle_IsAddedBasedOnTheTemplatesNormal()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Document("report.md"), new DocxOptions { Template = CustomTemplate.Options, TableOfContents = true }));
        var styles = package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().ToList();

        var details = Assert.Single(styles, s => s.StyleName?.Val == "Cover Details");
        Assert.Equal("Standard", details.BasedOn!.Val!.Value);
        Assert.Contains(Body(package).Elements<Paragraph>(), p => Style(p) == details.StyleId!.Value);
        Assert.Single(styles, s => s.StyleName?.Val == "TOC Heading");
        Assert.Single(styles, s => s.StyleId == "Standard");
    }

    [Fact]
    public void AnAddedStyle_TakesAFreeId()
    {
        var template = CustomTemplate.Content.ToArray();
        using (var stream = new MemoryStream())
        {
            stream.Write(template);
            using (var package = WordprocessingDocument.Open(stream, true))
            {
                // A character style that has the id the cover details would take.
                package.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Append(new Style(new StyleName { Val = "Busy" }) { Type = StyleValues.Character, StyleId = "CoverDetails" });
            }

            template = stream.ToArray();
        }

        using var result = Samples.Open(Samples.Generate(Samples.Markdown(Markdown), new DocxOptions { Template = new CustomDocxTemplateOptions(new MemoryStream(template)) }));

        Assert.Equal("CoverDetails2", result.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().Single(s => s.StyleName?.Val == "Cover Details").StyleId!.Value);
    }

    [Fact]
    public void AMappedStyleTheTemplateLacks_ThrowsListingItsParagraphStyles()
    {
        var options = new DocxOptions { Template = CustomTemplate.Options with { Styles = new DocxStyleNames { Heading1 = "Chapter" } } };

        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown(Markdown), options));

        Assert.Contains("The style 'Chapter' that styles.heading1 names is not in the caller's template. Its paragraph styles: ", error.Message, StringComparison.Ordinal);
        Assert.Contains("'heading 1'", error.Message, StringComparison.Ordinal);
        Assert.Contains($"'{CustomTemplate.SubtitleName}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleNames_AreMatchedCaseInsensitively_AndComeFromFrontMatter()
    {
        var markdown = Samples.Markdown("---\nstyles.heading1: HEADING 3\nstyles.subtitle: untertitel haus\n---\n" + Markdown);

        using var package = Samples.Open(Samples.Generate(markdown, new DocxOptions { Template = new CustomDocxTemplateOptions(new MemoryStream(CustomTemplate.Content)) }));

        Assert.Contains(Body(package).Elements<Paragraph>(), p => Texts(p) == "Part" && Style(p) == "berschrift3");
        Assert.Contains(Body(package).Elements<Paragraph>(), p => Texts(p) == "The subtitle" && Style(p) == "UntertitelHaus");
    }

    [Fact]
    public void ABrandsOwnTemplate_IsUsedWithItsStyles_AndTakesOnlyEmbedFonts()
    {
        var brand = Samples.Brand with { Name = "house", Docx = new DocxBrandTemplate(CustomTemplate.Content) { Styles = CustomTemplate.Styles } };
        var options = new DocxOptions { Template = new BrandDocxTemplateOptions { Brand = brand } };

        var docx = Samples.Generate(Samples.Markdown(Markdown), options);
        using var package = Samples.Open(docx);

        Assert.Equal(["Titel", "UntertitelHaus"], Body(package).Elements<Paragraph>().Take(2).Select(Style));
        Assert.NotEmpty(package.MainDocumentPart!.FontTablePart!.FontParts);
        Samples.AssertValid(docx);
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("---\nshowDate: false\n---\n## A\n"), options));
        Assert.Contains("Valid keys: template, title, author, lang, embedFonts, toc, tocDepth", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrandWithAnEmptyTemplate_IsInvalid()
    {
        var brand = Brand.Neutral with { Name = "empty", Docx = new DocxBrandTemplate(ReadOnlyMemory<byte>.Empty) };

        var error = Assert.Throws<ArgumentException>(() => Samples.Generate(Samples.Markdown("## A\n"), new DocxOptions { Template = new BrandDocxTemplateOptions { Brand = brand } }));

        Assert.Contains("Docx is empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatterKeys_FollowTheTemplate()
    {
        var builtIn = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown("---\nstyles.title: Title\n---\n## A\n")));
        var custom = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown("---\nshowDate: false\n---\n## A\n"),
            new DocxOptions { Template = CustomTemplate.Options }));

        Assert.Contains("Valid keys: template, title, author, lang, showDate, showTagline, embedFonts, toc, tocDepth, transition, build, numberChapters.", builtIn.Message, StringComparison.Ordinal);
        Assert.Contains("styles.normal, styles.title, styles.subtitle, styles.coverDetails, styles.tocHeading, styles.heading1", custom.Message, StringComparison.Ordinal);
        Assert.Contains("styles.heading6, toc, tocDepth", custom.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("toc: maybe", "'toc' has the value 'maybe'; expected 'true' or 'false'")]
    [InlineData("tocDepth: 4", "'tocDepth' has the value '4'; expected '1' or '2' or '3'")]
    [InlineData("template: acme", "'template' has the value 'acme'; expected 'neutral'")]
    [InlineData("transition: spin", "'transition' has the value 'spin'")]
    public void InvalidFrontMatter_Throws(string line, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(Samples.Markdown($"---\n{line}\n---\n## A\n")));

        Assert.Equal(Format.Docx, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeckKeys_AreAcceptedAndIgnored()
    {
        var docx = Samples.Generate(Samples.Markdown("---\ntransition: fade\nbuild: lists\nnumberChapters: true\nlabels.success: Done\n---\n## A\n"));

        Samples.AssertValid(docx);
    }

    [Fact]
    public void TemplateKey_SelectsARegisteredBrand()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\ntemplate: sample\n---\n## A\n"), new DocxOptions { Brands = [Samples.Brand] }));

        Assert.Equal("Liberation Serif", package.MainDocumentPart!.ThemePart!.Theme!.ThemeElements!.FontScheme!.MinorFont!.LatinFont!.Typeface!.Value);
    }
}
