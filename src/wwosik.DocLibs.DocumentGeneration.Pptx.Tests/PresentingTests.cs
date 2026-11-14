using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Speaker notes and transitions.</summary>
public class PresentingTests
{
    private static MarkdownDocument Presenting() => Samples.Markdown(File.ReadAllText(Samples.Path("presenting.md")).ReplaceLineEndings("\n"));

    private static (GenerationResult Result, byte[] Pptx) Generate(string markdown, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Samples.Markdown(markdown), output, options);
        return (result, output.ToArray());
    }

    // The text of each paragraph in the notes page's body placeholder; null without a notes page.
    private static List<string>? SpeakerNotes(SlidePart slide) =>
        slide.NotesSlidePart?.NotesSlide!.Descendants<P.Shape>()
            .Single(s => s.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape?.Type?.Value == P.PlaceholderValues.Body)
            .TextBody!.Elements<A.Paragraph>().Select(p => p.InnerText).ToList();

    // The name of the slide's transition effect, `morph` for Morph's alternate content; null without one.
    private static string? TransitionOf(SlidePart slide)
    {
        var xml = slide.Slide!.OuterXml;
        if (xml.Contains("p159:morph", StringComparison.Ordinal))
        {
            return "morph";
        }

        return slide.Slide.GetFirstChild<P.Transition>()?.FirstChild?.LocalName;
    }

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsValid_AndRoundTripsWithItsSpeakerNotes(string template)
    {
        var options = new PptxOptions { Template = Samples.BuiltIn(template) };
        var pptx = Samples.Generate(Presenting(), options);
        Samples.WriteForReview($"presenting-{template}.pptx", pptx);

        Samples.AssertValid(pptx);
        var back = new PptxConverter().Convert(new MemoryStream(pptx), ConversionOptions.Default);
        Samples.AssertMatchesExpected($"presenting-{template}.roundtrip", back.Markdown);
    }

    [Fact]
    public void Sample_PutsTheNotesOnEverySlideOfTheirSection_AndEachTransitionOnItsSlide()
    {
        using var output = new MemoryStream();
        var result = PptxGenerator.Generate(Presenting(), output);
        Assert.Contains(result.Notes, n => n.Code == NoteCodes.SlideSplit);
        Assert.DoesNotContain(result.Notes, n => n.Code is NoteCodes.NotesSimplified or NoteCodes.NotesDropped);

        using var package = Samples.Open(output.ToArray());
        var slides = Samples.Slides(package);
        string Title(SlidePart slide) => Samples.PlaceholderText(slide, P.PlaceholderValues.Title)
            ?? Samples.PlaceholderText(slide, P.PlaceholderValues.CenteredTitle) ?? string.Empty;

        // The cover, the two parts of the split slide, the two table slides and the flow slide.
        Assert.Equal(["Welcome everyone. Introduce yourself and the three parts of the talk."], SpeakerNotes(slides[0]));
        var route = slides.Where(s => Title(s) == "Every stop on the route").ToList();
        Assert.Equal(2, route.Count);
        Assert.All(route, s => Assert.Equal(["This slide continues on the next one; the notes are on both."], SpeakerNotes(s)));
        var numbers = slides.Where(s => Title(s) == "The numbers").ToList();
        Assert.Equal(2, numbers.Count);
        Assert.All(numbers, s => Assert.Equal(["Two tables, so two slides: say the totals out loud."], SpeakerNotes(s)));
        Assert.NotNull(SpeakerNotes(slides.Single(s => Title(s) == "What changes")));
        Assert.Null(slides.Single(s => Title(s) == "Timeline").NotesSlidePart);

        // A content slide with speaker notes stays a content slide; the flow slide stays a flow slide.
        Assert.Equal("Title and Content", Samples.LayoutName(slides.Single(s => Title(s) == "Where we stand")));
        Assert.Equal("Title Only", Samples.LayoutName(slides.Single(s => Title(s) == "What changes")));

        Assert.Equal("fade", TransitionOf(slides[0]));
        Assert.Equal(["push", "wipe", "wipe", "split", "split", "cover", "pull", "zoom", "cut", "morph", null, "fade"], slides.Skip(1).Select(TransitionOf));
    }

    [Fact]
    public void SpeakerNotes_KeepListsStylesCodeAndLinks_InTheDocumentLanguage()
    {
        var (result, pptx) = Generate("---\nlang: de\n---\n# Deck\n\n## Plan\n\nText.\n\n::: notes\nSay **this** and *that*.\n\n1. First\n2. Second, see [the page](https://example.com/page)\n   - deeper\n- Use `code`\n:::\n");

        Assert.False(result.HasNotes, result.ToString());
        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var notes = Samples.Slides(package)[1].NotesSlidePart!;
        var body = notes.NotesSlide!.Descendants<P.Shape>().Single(s => s.InnerText.Contains("Say", StringComparison.Ordinal));
        var paragraphs = body.TextBody!.Elements<A.Paragraph>().ToList();
        Assert.Equal(["Say this and that.", "First", "Second, see the page", "deeper", "code"], paragraphs.Select(p => p.InnerText).Select(t => t.Replace("Use ", string.Empty, StringComparison.Ordinal)));

        var runs = body.Descendants<A.Run>().ToList();
        Assert.True(runs.Single(r => r.InnerText == "this").RunProperties!.Bold!.Value);
        Assert.True(runs.Single(r => r.InnerText == "that").RunProperties!.Italic!.Value);
        Assert.Equal("Consolas", runs.Single(r => r.InnerText == "code").RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
        var link = runs.Single(r => r.InnerText == "the page").RunProperties!.GetFirstChild<A.HyperlinkOnClick>()!;
        Assert.Equal("https://example.com/page", notes.HyperlinkRelationships.Single(h => h.Id == link.Id).Uri.ToString());
        Assert.NotNull(paragraphs[1].ParagraphProperties!.GetFirstChild<A.AutoNumberedBullet>());
        Assert.Equal(1, paragraphs[3].ParagraphProperties!.Level!.Value);
        Assert.NotNull(paragraphs[3].ParagraphProperties!.GetFirstChild<A.CharacterBullet>());
        Assert.All(body.Descendants<A.RunProperties>(), r => Assert.Equal("de", r.Language!.Value));
    }

    [Fact]
    public void SeveralNotes_AreJoined_AndOtherBlocksArePlainText_WithANote()
    {
        var (result, pptx) = Generate("## Plan\n\nText.\n\n::: notes\nFirst.\n:::\n\nMore text.\n\n::: notes\n| A | B |\n| - | - |\n| 1 | 2 |\n\n```\nlet x = 1;\n```\n:::\n");

        var simplified = result.Notes.Where(n => n.Code == NoteCodes.NotesSimplified).ToList();
        Assert.Equal([12, 16], simplified.Select(n => n.Line));
        Assert.Contains("a table", simplified[0].Message, StringComparison.Ordinal);
        Assert.Contains("a code block", simplified[1].Message, StringComparison.Ordinal);
        using var package = Samples.Open(pptx);
        Assert.Equal(["First.", "A | B", "1 | 2", "let x = 1;"], SpeakerNotes(Samples.Slides(package)[0]));
    }

    [Fact]
    public void NotesBeforeTheFirstHeading_AreDropped_WithANote()
    {
        var (result, pptx) = Generate("::: notes\nNobody sees this.\n:::\n\n## Plan\n\nText.\n");

        var note = Assert.Single(result.Notes);
        Assert.Equal(NoteCodes.NotesDropped, note.Code);
        Assert.Equal(1, note.Line);
        using var package = Samples.Open(pptx);
        Assert.Null(package.PresentationPart!.NotesMasterPart);
    }

    [Theory]
    [InlineData("## Plan\n\n::: panel\n::: notes\nHidden.\n:::\n:::\n")]
    [InlineData("## Plan\n\n- Item\n\n  ::: notes\n  Hidden.\n  :::\n")]
    [InlineData("# Deck\n\n> ::: notes\n> Hidden.\n> :::\n")]
    [InlineData("## Plan\n\n:::: notes\n::: notes\nHidden.\n:::\n::::\n")]
    public void NestedNotes_Throw(string markdown)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Generate(markdown));
        Assert.Contains("'::: notes' goes at the top level", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownTransition_Throws()
    {
        var heading = Assert.Throws<DocumentConversionException>(() => Generate("## Plan {transition=\"spin\"}\n\nText.\n"));
        Assert.Contains("Unknown transition 'spin'", heading.Message, StringComparison.Ordinal);
        var frontMatter = Assert.Throws<DocumentConversionException>(() => Generate("---\ntransition: spin\n---\n## Plan\n\nText.\n"));
        Assert.Contains("'transition'", frontMatter.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadingWins_OverTheFrontMatter_AndThatOverTheOptions()
    {
        var options = new PptxOptions { Transition = SlideTransition.Push };
        var markdown = "## One\n\nText.\n\n## Two {transition=\"none\"}\n\nText.\n\n## Three {transition=\"zoom\"}\n\nText.\n";

        using var fromOptions = Samples.Open(Generate(markdown, options).Pptx);
        Assert.Equal(["push", null, "zoom"], Samples.Slides(fromOptions).Select(TransitionOf));
        using var fromFrontMatter = Samples.Open(Generate("---\ntransition: Wipe\n---\n" + markdown, options).Pptx);
        Assert.Equal(["wipe", null, "zoom"], Samples.Slides(fromFrontMatter).Select(TransitionOf));
    }

    [Fact]
    public void AHeadingTransition_KeepsAPlainSlidePlain()
    {
        using var package = Samples.Open(Generate("## Plan {transition=\"fade\"}\n\n- One\n- Two\n").Pptx);
        Assert.Equal("Title and Content", Samples.LayoutName(Samples.Slides(package)[0]));
    }

    [Fact]
    public void Morph_FallsBackToFade()
    {
        var (_, pptx) = Generate("## One {transition=\"morph\"}\n\nText.\n");

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var slide = Samples.Slides(package)[0].Slide!;
        var alternate = Assert.Single(slide.ChildElements.OfType<AlternateContent>());
        Assert.Equal("p159", alternate.GetFirstChild<AlternateContentChoice>()!.Requires!.Value);
        Assert.Contains("<p:fade", alternate.GetFirstChild<AlternateContentFallback>()!.OuterXml, StringComparison.Ordinal);
        Assert.IsType<P.ColorMapOverride>(alternate.PreviousSibling());
    }

    [Fact]
    public void ADeckWithoutTheNewSyntax_HasNoNotesMaster_AndNoTransitions_AndNoneChangesNothing()
    {
        var document = Samples.Deck();
        var options = new PptxOptions { Template = new BrandTemplateOptions { ShowDate = false } };
        var pptx = Samples.Generate(document, options);
        using var package = Samples.Open(pptx);
        Assert.Null(package.PresentationPart!.NotesMasterPart);
        Assert.Null(package.PresentationPart.Presentation!.NotesMasterIdList);
        Assert.All(Samples.Slides(package), s =>
        {
            Assert.Null(s.NotesSlidePart);
            Assert.Null(s.Slide!.GetFirstChild<P.Transition>());
            Assert.Empty(s.Slide.ChildElements.OfType<AlternateContent>());
        });

        // `transition: none` and PptxOptions.Transition None write the same deck.
        var none = Samples.Generate(document with { Markdown = "---\ntransition: none\n---\n" + document.Markdown }, options);
        Assert.Equal(Parts(pptx), Parts(none));
    }

    [Fact]
    public void ACallerTemplatesOwnNotesMaster_IsUsedAsItIs()
    {
        var withNotes = Samples.Generate(Samples.Markdown("## One\n\nText.\n\n::: notes\nFirst deck.\n:::\n"));
        var template = new CustomTemplateOptions(new MemoryStream(withNotes));
        string masterXml;
        using (var source = Samples.Open(withNotes))
        {
            masterXml = source.PresentationPart!.NotesMasterPart!.NotesMaster!.OuterXml;
        }

        var (_, pptx) = Generate("## Two\n\nText.\n\n::: notes\nSecond deck.\n:::\n", new PptxOptions { Template = template });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.Single(package.PresentationPart!.GetPartsOfType<NotesMasterPart>());
        Assert.Equal(masterXml, package.PresentationPart.NotesMasterPart!.NotesMaster!.OuterXml);
        var slide = Assert.Single(Samples.Slides(package));
        Assert.Equal(["Second deck."], SpeakerNotes(slide));
        Assert.Single(package.PresentationPart.SlideParts);
    }

    [Fact]
    public void ACallerTemplateWithoutANotesMaster_GetsTheBuiltInOne()
    {
        var template = new CustomTemplateOptions(new MemoryStream(Samples.TemplateBytes(Samples.BuiltIn("sample"))));
        var (_, pptx) = Generate("## One\n\nText.\n\n::: notes\nSpeak.\n:::\n", new PptxOptions { Template = template });

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        var master = package.PresentationPart!.NotesMasterPart!;
        Assert.NotNull(master.ThemePart);
        Assert.Equal(master, Samples.Slides(package)[0].NotesSlidePart!.NotesMasterPart);
        Assert.Equal(package.PresentationPart.GetIdOfPart(master), package.PresentationPart.Presentation!.NotesMasterIdList!.GetFirstChild<P.NotesMasterId>()!.Id!.Value);
    }

    [Fact]
    public void ABrandsOwnTemplate_GetsANotesMaster()
    {
        var (_, pptx) = Generate("## One {.dark}\n\nText.\n\n::: notes\nSpeak.\n:::\n", DarkTemplate.Options);

        Samples.AssertValid(pptx);
        using var package = Samples.Open(pptx);
        Assert.NotNull(package.PresentationPart!.NotesMasterPart);
        Assert.Equal(["Speak."], SpeakerNotes(Samples.Slides(package)[0]));
    }

    [Fact]
    public void TheBuiltInNotesMaster_TakesTheBrandsFont()
    {
        var (_, pptx) = Generate("## One\n\nText.\n\n::: notes\nSpeak.\n:::\n", new PptxOptions { Template = Samples.BuiltIn("sample") });

        using var package = Samples.Open(pptx);
        var theme = package.PresentationPart!.NotesMasterPart!.ThemePart!.Theme!;
        Assert.Equal(ResolvedBrand.Resolve(Samples.Brand).FontFamily, theme.ThemeElements!.FontScheme!.MinorFont!.LatinFont!.Typeface!.Value);
    }

    [Fact]
    public void CodeInSpeakerNotes_EmbedsTheCodeFont()
    {
        var mono = ResolvedBrand.Resolve(Samples.Brand).Fonts!.MonoFamily;
        string[] Embedded(string markdown)
        {
            using var package = Samples.Open(Generate(markdown, new PptxOptions { Template = Samples.BuiltIn("sample") }).Pptx);
            return [.. package.PresentationPart!.Presentation!.EmbeddedFontList!.Elements<P.EmbeddedFont>().Select(f => f.Font!.Typeface!.Value!)];
        }

        Assert.DoesNotContain(mono, Embedded("## One\n\nText.\n\n::: notes\nPlain.\n:::\n"));
        Assert.Contains(mono, Embedded("## One\n\nText.\n\n::: notes\nUse `code`.\n:::\n"));
    }

    // Each part's content, for comparing two decks whose relationship ids are random.
    private static Dictionary<string, string> Parts(byte[] pptx)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(pptx));
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e =>
            {
                using var reader = new StreamReader(e.Open());
                return System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "R[0-9a-f]{16}", "R*");
            });
    }
}
