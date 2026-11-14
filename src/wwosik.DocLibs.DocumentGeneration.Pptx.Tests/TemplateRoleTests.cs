using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Template roles (E01-S06): layout and placeholder roles, text levels, footer pattern and content grid.</summary>
public class TemplateRoleTests
{
    private const string Sections = "# Deck\n\n## One\n\n- a\n\n# Part two\n\n## Two\n\n- b\n";

    private static byte[] BuiltInBytes => Samples.TemplateBytes(new BrandTemplateOptions());

    private static List<SlideLayoutPart> LayoutsOf(PresentationDocument package) =>
        [.. package.PresentationPart!.SlideMasterParts.SelectMany(m => m.SlideLayoutParts)];

    private static string? Name(SlideLayoutPart? layout) => layout?.SlideLayout?.CommonSlideData?.Name?.Value;

    private static string? ShapeName(P.PlaceholderShape? placeholder) => placeholder is null ? null : TemplateRoles.ShapeName(placeholder);

    private static PptxOptions Custom(byte[] template, PptxTemplateRoles roles) =>
        new() { Template = new CustomTemplateOptions(new MemoryStream(template)) { Roles = roles } };

    // The paragraphs of the slide's body placeholder: their level (0 when not set) and whether their first run is bold.
    private static List<(int Level, bool Bold, string Text)> BodyParagraphs(SlidePart slide) =>
        [.. slide.Slide!.Descendants<P.Shape>()
            .Single(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape is { Index.Value: 1 })
            .TextBody!.Elements<A.Paragraph>()
            .Select(p => (p.ParagraphProperties?.Level?.Value ?? 0, p.Elements<A.Run>().FirstOrDefault()?.RunProperties?.Bold?.Value ?? false, p.InnerText))];

    [Fact]
    public void TheBuiltInMaster_HasALayoutForEveryRole_AndDeclaresItsPlaceholders()
    {
        using var package = Samples.Open(BuiltInBytes);
        var layouts = LayoutsOf(package);
        var roles = TemplateRoles.Resolve(layouts, TemplateBuilder.Roles);

        Assert.All(Enum.GetValues<LayoutRole>(), role => Assert.NotNull(roles.Layout(role)));
        Assert.Equal(13, layouts.Count);

        // The first five keep their relationship ids, so existing decks keep their parts; the new ones follow.
        var master = package.PresentationPart!.SlideMasterParts.Single();
        Assert.Equal(
            ["rId1", "rId2", "rId3", "rId4", "rId7", .. Enumerable.Range(8, 8).Select(i => $"rId{i}")],
            master.SlideMaster!.SlideLayoutIdList!.Elements<P.SlideLayoutId>().Select(id => id.RelationshipId!.Value));
        Assert.Equal(
            ["Title Slide", "Title and Content", "Title Only", "Section Header", "Title Only Dark", "Two Content", "Picture with Caption", "Picture", "Agenda", "Quote", "Statement", "Contact", "Chart"],
            master.SlideMaster.SlideLayoutIdList.Elements<P.SlideLayoutId>().Select(id => Name((SlideLayoutPart)master.GetPartById(id.RelationshipId!))));
        Samples.AssertValid(Samples.Generate(Samples.Markdown(Sections), new PptxOptions { Template = Samples.BuiltIn("sample") }));
        Assert.Equal("Two Content", Name(roles.Layout(LayoutRole.TwoCharts)));
        Assert.Equal("Picture with Caption", Name(roles.Layout(LayoutRole.TextAndPicture)));

        var chart = roles.Placeholders(roles.Layout(LayoutRole.Chart)!);
        Assert.Equal(["Chart Title", "Chart Unit", "Chart Source"], new[] { chart.ChartTitle, chart.ChartUnit, chart.ChartSource }.Select(ShapeName));
        Assert.Single(chart.Charts);
        Assert.Empty(chart.Bodies);

        var contact = roles.Placeholders(roles.Layout(LayoutRole.Contact)!);
        Assert.Equal(Enumerable.Range(7, 5).Select(i => $"Text Placeholder {i}"), contact.Bodies.Select(ShapeName));
        Assert.Equal(Enumerable.Range(2, 5).Select(i => $"Picture Placeholder {i}"), contact.Pictures.Select(ShapeName));

        var quote = roles.Placeholders(roles.Layout(LayoutRole.Quote)!);
        Assert.Equal("Byline", ShapeName(quote.Byline));
        Assert.Equal(["Text Placeholder 1"], quote.Bodies.Select(ShapeName));

        var section = layouts.Single(l => l.SlideLayout!.Type!.Value == P.SlideLayoutValues.SectionHeader);
        Assert.Equal("Chapter Number", ShapeName(roles.Placeholders(section).ChapterNumber));
        Assert.Equal(["Text Placeholder 2"], roles.Placeholders(section).Bodies.Select(ShapeName));

        var caption = roles.Placeholders(roles.Layout(LayoutRole.TextAndPicture)!);
        Assert.Equal(["Picture Placeholder 3"], caption.Pictures.Select(ShapeName));
        Assert.Equal(["Text Placeholder 2"], caption.Bodies.Select(ShapeName));
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void EveryNewLayoutOfTheBuiltInMaster_TakesASlide(string brand)
    {
        // Through a caller's template, since `layout="…"` is for templates other than the built-in master; for review.
        var layouts = new[] { "Two Content", "Picture with Caption", "Picture", "Agenda", "Quote", "Statement", "Contact", "Chart", "Section Header" };
        var markdown = "# Layouts\n\n" + string.Concat(layouts.Select(l => $"## {l} {{layout=\"{l}\"}}\n\n- First point\n- Second point\n\n"));
        var template = Samples.TemplateBytes(Samples.BuiltIn(brand));

        var pptx = Samples.Generate(Samples.Markdown(markdown), new PptxOptions { Template = new CustomTemplateOptions(new MemoryStream(template)) });
        Samples.WriteForReview($"roles-layouts-{brand}.pptx", pptx);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Equal(layouts, Samples.Slides(package).Skip(1).Select(Samples.LayoutName).Distinct());
    }

    [Fact]
    public void TheBuiltInMasterAsACallersTemplate_HasOnlyTheRolesItsTypesOrNamesSay()
    {
        using var package = Samples.Open(BuiltInBytes);
        var roles = TemplateRoles.Resolve(LayoutsOf(package), null);

        Assert.Equal("Two Content", Name(roles.Layout(LayoutRole.TwoContent)));
        Assert.Equal("Picture with Caption", Name(roles.Layout(LayoutRole.TextAndPicture)));
        Assert.Equal("Chart", Name(roles.Layout(LayoutRole.Chart)));
        Assert.Equal("Title Only Dark", Name(roles.Layout(LayoutRole.Dark)));
        Assert.All(
            new[] { LayoutRole.Picture, LayoutRole.Agenda, LayoutRole.Quote, LayoutRole.Statement, LayoutRole.Contact, LayoutRole.TwoCharts },
            role => Assert.Null(roles.Layout(role)));

        // Undeclared, a byline is just another body, in reading order: top to bottom.
        var quote = LayoutsOf(package).Single(l => Name(l) == "Quote");
        Assert.Equal(["Text Placeholder 1", "Byline"], roles.Placeholders(quote).Bodies.Select(ShapeName));
        Assert.Null(roles.Placeholders(quote).Byline);
    }

    [Fact]
    public void DeclaredNames_FindLayoutsWithoutTypes_AndDeclaredBodiesKeepTheirOrder()
    {
        using var package = Samples.Open(RolesTemplate.Content);
        var roles = TemplateRoles.Resolve(LayoutsOf(package), RolesTemplate.Roles);

        Assert.Equal(RolesTemplate.TwoColumns, Name(roles.Layout(LayoutRole.TwoContent)));
        Assert.Equal(RolesTemplate.Quote, Name(roles.Layout(LayoutRole.Quote)));
        Assert.Equal(RolesTemplate.Chart, Name(roles.Layout(LayoutRole.Chart)));
        Assert.Equal(RolesTemplate.Dark, Name(roles.Layout(LayoutRole.Dark)));
        Assert.Null(roles.Layout(LayoutRole.TwoCharts));
        Assert.Equal(["Content Placeholder 3", "Content Placeholder 2"], roles.Placeholders(roles.Layout(LayoutRole.TwoContent)!).Bodies.Select(ShapeName));
        Assert.Equal("Byline", ShapeName(roles.Placeholders(roles.Layout(LayoutRole.Quote)!).Byline));

        var content = RolesTemplate.Layout(package.PresentationPart!.SlideMasterParts.Single(), RolesTemplate.ContentLayout);
        Assert.Equal(["Content Placeholder 2"], roles.Placeholders(content).Bodies.Select(ShapeName));
    }

    [Fact]
    public void UndeclaredBodies_AreInReadingOrder_WhateverTheirOrderInTheTree()
    {
        var edited = Samples.EditedTemplate(new BrandTemplateOptions(), master =>
        {
            foreach (var name in new[] { "Contact", "Quote" })
            {
                var tree = RolesTemplate.Layout(master, name).SlideLayout!.CommonSlideData!.ShapeTree!;
                var bodies = tree.Elements<P.Shape>().Where(s => s.Descendants<P.PlaceholderShape>().Any(p => p.Type?.Value == P.PlaceholderValues.Body)).ToList();
                foreach (var body in bodies)
                {
                    body.Remove();
                }

                foreach (var body in Enumerable.Reverse(bodies))
                {
                    tree.Append(body);
                }
            }
        });
        using var package = Samples.Open(edited);
        var roles = TemplateRoles.Resolve(LayoutsOf(package), null);
        var master = package.PresentationPart!.SlideMasterParts.Single();

        Assert.Equal(Enumerable.Range(7, 5).Select(i => $"Text Placeholder {i}"), roles.Placeholders(RolesTemplate.Layout(master, "Contact")).Bodies.Select(ShapeName));
        Assert.Equal(["Text Placeholder 1", "Byline"], roles.Placeholders(RolesTemplate.Layout(master, "Quote")).Bodies.Select(ShapeName));
    }

    [Fact]
    public void UnknownNames_Throw_ListingWhatTheTemplateHas()
    {
        var layout = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown(Sections),
            Custom(BuiltInBytes, new PptxTemplateRoles { Layouts = new PptxLayoutRoles { Agenda = "Agendas" } })));
        var placeholder = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown(Sections),
            Custom(BuiltInBytes, new PptxTemplateRoles { Placeholders = [new("Quote", "Author", PlaceholderRole.Byline)] })));
        var layoutOfPlaceholder = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown(Sections),
            Custom(BuiltInBytes, new PptxTemplateRoles { Placeholders = [new("Quotes", "Byline", PlaceholderRole.Byline)] })));

        Assert.Contains("no layout named 'Agendas' (asked for as the Agenda layout)", layout.Message, StringComparison.Ordinal);
        Assert.Contains("'Agenda'", layout.Message, StringComparison.Ordinal);
        Assert.Contains("The layout 'Quote' has no placeholder named 'Author'", placeholder.Message, StringComparison.Ordinal);
        Assert.Contains("'Text Placeholder 1', 'Byline'", placeholder.Message, StringComparison.Ordinal);
        Assert.Contains("no layout named 'Quotes'", layoutOfPlaceholder.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidRolesOfACallersTemplate_ThrowNamingTheProperty()
    {
        var error = Assert.Throws<ArgumentException>(() => Samples.Generate(
            Samples.Markdown(Sections),
            Custom(BuiltInBytes, new PptxTemplateRoles { TextLevels = new(2, 2, 1) })));

        Assert.Contains("CustomTemplateOptions.Roles.TextLevels.Heading is 2, deeper than Bullet (1)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADecorativePlaceholder_IsNeverWritten_NotEvenAsTheBody()
    {
        var pptx = Samples.Generate(Samples.Markdown(Sections), RolesTemplate.Options);
        Samples.WriteForReview("roles-brand.pptx", pptx);
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);

        var slide = Samples.Slides(package)[1];
        Assert.Equal(RolesTemplate.ContentLayout, Samples.LayoutName(slide));
        var indices = slide.Slide!.Descendants<P.PlaceholderShape>().Select(p => p.Index?.Value).ToList();
        Assert.Contains(1U, indices);
        Assert.DoesNotContain(20U, indices);
        Assert.Equal([(0, false, "a")], BodyParagraphs(slide));

        // Undeclared, the bar is the first body and takes the text.
        using var undeclared = Samples.Open(Samples.Generate(Samples.Markdown(Sections), Custom(RolesTemplate.Content, new PptxTemplateRoles())));
        Assert.Contains(20U, Samples.Slides(undeclared)[1].Slide!.Descendants<P.PlaceholderShape>().Select(p => p.Index?.Value));
    }

    [Fact]
    public void ADeclaredDarkLayout_TakesTheDarkSlides_AlsoFromFrontMatter()
    {
        const string Dark = "## Key message {.dark}\n\n::: statement\nOne thing.\n:::\n";
        using var declared = Samples.Open(Samples.Generate(Samples.Markdown("---\nlayouts.dark: Night\n---\n" + Dark), Custom(RolesTemplate.Content, new PptxTemplateRoles())));
        using var undeclared = Samples.Open(Samples.Generate(Samples.Markdown(Dark), Custom(RolesTemplate.Content, new PptxTemplateRoles())));

        Assert.Equal(RolesTemplate.Dark, Samples.LayoutName(Samples.Slides(declared)[0]));
        Assert.Null(Samples.Slides(declared)[0].Slide!.CommonSlideData!.Background);
        Assert.Equal("Title Only", Samples.LayoutName(Samples.Slides(undeclared)[0]));
        Assert.NotNull(Samples.Slides(undeclared)[0].Slide!.CommonSlideData!.Background);
    }

    [Fact]
    public void TextLevels_PutHeadingsParagraphsAndBullets_OnTheDeclaredLevels()
    {
        const string Markdown = "## Levels\n\n### Sub-headline\n\nA paragraph.\n\n- One\n  - Two\n    - Three\n\n1. First\n";
        var roles = new PptxTemplateRoles { TextLevels = new(1, 2, 3) };
        var pptx = Samples.Generate(Samples.Markdown(Markdown), Custom(BuiltInBytes, roles));
        Samples.WriteForReview("roles-text-levels.pptx", pptx);
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        using var plain = Samples.Open(Samples.Generate(Samples.Markdown(Markdown), Custom(BuiltInBytes, new PptxTemplateRoles())));

        Assert.Equal(
            [(0, false, "Sub-headline"), (1, false, "A paragraph."), (2, false, "One"), (3, false, "Two"), (4, false, "Three"), (2, false, "First")],
            BodyParagraphs(Samples.Slides(package)[0]));
        Assert.Equal(
            [(0, true, "Sub-headline"), (0, false, "A paragraph."), (0, false, "One"), (1, false, "Two"), (2, false, "Three"), (0, false, "First")],
            BodyParagraphs(Samples.Slides(plain)[0]));

        var body = Samples.Slides(package)[0].Slide!.Descendants<A.Paragraph>().ToList();
        Assert.Null(body.Single(p => p.InnerText == "Sub-headline").ParagraphProperties?.GetFirstChild<A.NoBullet>());
        Assert.NotNull(body.Single(p => p.InnerText == "A paragraph.").ParagraphProperties!.GetFirstChild<A.NoBullet>());
        Assert.NotNull(body.Single(p => p.InnerText == "First").ParagraphProperties!.GetFirstChild<A.AutoNumberedBullet>());
    }

    [Fact]
    public void TextLevels_WithListBulletsAlways_WriteMarkdownsBulletsAtTheMappedLevels()
    {
        var options = Custom(BuiltInBytes, new PptxTemplateRoles { TextLevels = new(1, 1, 2) }) with { ListBullets = ListBullets.Always };
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("## Levels\n\n- One\n  - Two\n"), options));

        var paragraphs = Samples.Slides(package)[0].Slide!.Descendants<A.Paragraph>().Where(p => p.InnerText is "One" or "Two").ToList();
        Assert.Equal([1, 2], paragraphs.Select(p => p.ParagraphProperties!.Level!.Value));
        Assert.Equal(["•", "–"], paragraphs.Select(p => p.ParagraphProperties!.GetFirstChild<A.CharacterBullet>()!.Char!.Value));
        Assert.All(paragraphs, p => Assert.Null(p.ParagraphProperties!.LeftMargin));
    }

    [Fact]
    public void Fitting_MeasuresEachParagraph_AtItsLevelsSize()
    {
        // Level 5 of the master's body style is set at 60 pt: eight bullets on it do not fit one slide.
        var large = Samples.EditedTemplate(new BrandTemplateOptions(), master =>
            master.SlideMaster!.TextStyles!.BodyStyle!.GetFirstChild<A.Level5ParagraphProperties>()!.GetFirstChild<A.DefaultRunProperties>()!.FontSize = 6000);
        var markdown = "## Many\n\n" + string.Concat(Enumerable.Range(1, 8).Select(i => $"- Point {i}\n"));

        using var output = new MemoryStream();
        var onLevel5 = PptxGenerator.Generate(Samples.Markdown(markdown), output, Custom(large, new PptxTemplateRoles { TextLevels = new(1, 1, 5) }));
        var onLevel1 = PptxGenerator.Generate(Samples.Markdown(markdown), new MemoryStream(), Custom(large, new PptxTemplateRoles { TextLevels = new(1, 1, 1) }));

        Assert.Contains(onLevel5.Notes, n => n.Code == NoteCodes.SlideSplit);
        Assert.DoesNotContain(onLevel1.Notes, n => n.Code is NoteCodes.SlideSplit or NoteCodes.SlideShrunk);
        Samples.AssertValid(output.ToArray());
    }

    [Fact]
    public void FooterPattern_ShowsTheFooterTextAndTheChapter_AndShowFooterSwitchesItOff()
    {
        using var package = Samples.Open(Samples.Generate(Samples.Markdown(Sections), RolesTemplate.Options));
        using var off = Samples.Open(Samples.Generate(Samples.Markdown("---\nshowFooter: false\n---\n" + Sections), RolesTemplate.Options));

        Assert.Equal(
            [null, "Made-up Ltd", "Made-up Ltd | Part two", "Made-up Ltd | Part two"],
            Samples.Slides(package).Select(s => Samples.PlaceholderText(s, P.PlaceholderValues.Footer)));
        Assert.All(Samples.Slides(off), s => Assert.Null(Samples.PlaceholderText(s, P.PlaceholderValues.Footer)));
    }

    [Fact]
    public void FooterPattern_OfACallersTemplate_ShowsTitleAndAuthor_WithoutFooterText()
    {
        var options = Custom(RolesTemplate.Content, new PptxTemplateRoles { FooterPattern = "{footer} | {title} · {author}" });
        using var package = Samples.Open(Samples.Generate(Samples.Markdown("---\ntitle: The deck\nauthor: Someone\n---\n" + Sections), options));

        Assert.Equal("The deck · Someone", Samples.PlaceholderText(Samples.Slides(package)[1], P.PlaceholderValues.Footer));
    }

    [Fact]
    public void ContentAreaGuides_PlacesContentInsideTheMastersGuides()
    {
        const string Table = "## Figures\n\n| A | B |\n| - | - |\n| 1 | 2 |\n";
        var pptx = Samples.Generate(Samples.Markdown(Table), Custom(RolesTemplate.Content, new PptxTemplateRoles { ContentArea = PptxContentArea.Guides }));
        Samples.WriteForReview("roles-guides.pptx", pptx);
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        using var placeholders = Samples.Open(Samples.Generate(Samples.Markdown(Table), Custom(RolesTemplate.Content, new PptxTemplateRoles())));

        var frame = Samples.Slides(package)[0].Slide!.Descendants<P.GraphicFrame>().Single().Transform!;
        Assert.Equal((RolesTemplate.GuideArea.X, RolesTemplate.GuideArea.Y), (frame.Offset!.X!.Value, frame.Offset.Y!.Value));
        Assert.True(frame.Extents!.Cx!.Value <= RolesTemplate.GuideArea.W);
        Assert.NotEqual(RolesTemplate.GuideArea.X, Samples.Slides(placeholders)[0].Slide!.Descendants<P.GraphicFrame>().Single().Transform!.Offset!.X!.Value);
    }

    [Fact]
    public void ContentAreaGuides_OnATemplateWithoutGuides_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Samples.Generate(
            Samples.Markdown(Sections),
            Custom(RolesTemplate.WithoutGuides, new PptxTemplateRoles { ContentArea = PptxContentArea.Guides })));

        Assert.Contains("The content area of the caller's template is set to its guides", error.Message, StringComparison.Ordinal);
        Assert.Contains("0 vertical and 0 horizontal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatter_NamesTheLayoutRolesOfACallersTemplate()
    {
        var template = new CustomTemplateOptions(Stream.Null);
        var keys = new[] { "twoContent", "textAndPicture", "picture", "agenda", "quote", "statement", "contact", "chart", "twoCharts", "dark" };

        var applied = keys.Aggregate((PptxTemplateOptions)template, (current, key) => current.With(FrontMatter.Read("---\nx: y\n---\n", Format.Pptx), "layouts." + key, key.ToUpperInvariant()));

        var layouts = ((CustomTemplateOptions)applied).Roles.Layouts;
        Assert.Equal(
            new PptxLayoutRoles
            {
                TwoContent = "TWOCONTENT", TextAndPicture = "TEXTANDPICTURE", Picture = "PICTURE", Agenda = "AGENDA", Quote = "QUOTE",
                Statement = "STATEMENT", Contact = "CONTACT", Chart = "CHART", TwoCharts = "TWOCHARTS", Dark = "DARK",
            },
            layouts);
        Assert.All(keys, key => Assert.Contains("layouts." + key, template.Keys));
    }

    [Fact]
    public void TheGuide_ListsTheLayoutsOfTheRoles_AndWhatTextLevelsMakeOfHeadings()
    {
        var withLevels = RolesTemplate.Brand with { Pptx = RolesTemplate.Brand.Pptx! with { Roles = RolesTemplate.Roles with { TextLevels = new(1, 2, 3) } } };
        var guide = PptxGenerator.AuthoringGuide(RolesTemplate.Options);
        var levels = PptxGenerator.AuthoringGuide(new PptxOptions { Template = new BrandTemplateOptions { Brand = withLevels } });
        var custom = PptxGenerator.AuthoringGuide(Custom(BuiltInBytes, new PptxTemplateRoles { Layouts = new PptxLayoutRoles { Agenda = "Agenda" } }));

        Assert.Contains("two columns `Side by Side`", guide, StringComparison.Ordinal);
        Assert.Contains("quote `Big Quote`", guide, StringComparison.Ordinal);
        Assert.Contains("text and picture `Picture with Caption`", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("sub-headline", guide, StringComparison.Ordinal);
        Assert.Contains("a `###` heading is a sub-headline", levels, StringComparison.Ordinal);
        Assert.Contains("for `layout=\"Name\"`: agenda `Agenda`.", custom, StringComparison.Ordinal);
    }
}
