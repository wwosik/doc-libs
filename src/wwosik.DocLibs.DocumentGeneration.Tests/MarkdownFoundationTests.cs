using Markdig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The document language, the footnote check and drawing validation the generators share.</summary>
public class MarkdownFoundationTests
{
    private static readonly Dictionary<string, GeneratedTexts> None = [];

    [Theory]
    [InlineData("en", "Note")]
    [InlineData("EN-us", "Note")]
    [InlineData("de", "Hinweis")]
    [InlineData("de-AT", "Hinweis")]
    public void ATag_IsMatchedInFull_ThenByItsLanguage(string tag, string note)
    {
        var notes = new GenerationNotes();
        var language = DocumentLanguage.Resolve(tag, "en", None, notes, DocumentFormat.Pdf);

        Assert.Equal(tag, language.Tag);
        Assert.Equal(note, language.Texts.Note);
        Assert.False(notes.Result(0).HasNotes);
    }

    [Fact]
    public void HostTexts_WinOverTheBuiltInOnes_InFullBeforeByLanguage()
    {
        var swiss = GeneratedTexts.German with { Footnotes = "Fussnoten" };
        var texts = new Dictionary<string, GeneratedTexts> { ["de-CH"] = swiss, ["fr"] = GeneratedTexts.English with { Note = "Remarque" } };

        Assert.Equal("Fussnoten", DocumentLanguage.Resolve("de-ch", "en", texts, new GenerationNotes(), DocumentFormat.Pdf).Texts.Footnotes);
        Assert.Equal("Anmerkungen", DocumentLanguage.Resolve("de", "en", texts, new GenerationNotes(), DocumentFormat.Pdf).Texts.Footnotes);
        Assert.Equal("Remarque", DocumentLanguage.Resolve("fr-CA", "en", texts, new GenerationNotes(), DocumentFormat.Pdf).Texts.Note);
        Assert.Equal("Notes", DocumentLanguage.Resolve(null, "fr", texts, new GenerationNotes(), DocumentFormat.Pdf).Texts.Footnotes);
    }

    [Fact]
    public void AnUnknownLanguage_GetsEnglish_AndOneNote()
    {
        var notes = new GenerationNotes();
        var language = DocumentLanguage.Resolve(null, "pl", None, notes, DocumentFormat.Pptx);

        Assert.Equal("pl", language.Tag);
        Assert.Same(GeneratedTexts.English, language.Texts);
        var note = Assert.Single(notes.Result(0).Notes);
        Assert.Equal(NoteCodes.LanguageUnknown, note.Code);
        Assert.Equal(0, note.Line);
    }

    [Theory]
    [InlineData("deutsch-x")]
    [InlineData("d")]
    [InlineData("de_CH")]
    public void AnInvalidTag_Throws_InTheGeneratorsFormat(string tag)
    {
        var error = Assert.Throws<DocumentConversionException>(() => DocumentLanguage.Resolve(tag, "en", None, new GenerationNotes(), DocumentFormat.Pptx));
        Assert.Equal(DocumentFormat.Pptx, error.Format);
        Assert.Contains($"'{tag}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GermanTexts_DifferFromEnglishInEveryText()
    {
        foreach (var property in typeof(GeneratedTexts).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            Assert.NotEqual(property.GetValue(GeneratedTexts.English), property.GetValue(GeneratedTexts.German));
        }
    }

    [Fact]
    public void MissingFootnotes_AreReportedOnce_NotInCodeSpans_NorWhenDefined()
    {
        var pipeline = new MarkdownPipelineBuilder().UseDocumentSyntax().Build();
        var markdown = Markdown.Parse("Defined[^a] and missing[^b], `[^code]` and missing[^b] again.\n\n[^a]: Here.\n", pipeline);
        var notes = new GenerationNotes();

        MarkdownSyntax.ReportMissingFootnotes(markdown, block => block.Line + 1, notes);

        var note = Assert.Single(notes.Result(0).Notes);
        Assert.Equal(NoteCodes.FootnoteMissing, note.Code);
        Assert.Equal(1, note.Line);
        Assert.Contains("'[^b]'", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvenOdd_IsWrittenToSvg_OnlyForFilledShapes()
    {
        var svg = DrawingSvg.Write(new Drawing(20, 20,
        [
            new DrawingShape(DrawingPath.Rectangle(0, 0, 20, 20), "1F4E79", null, 0) { FillRule = DrawingFillRule.EvenOdd },
            new DrawingShape(DrawingPath.Rectangle(0, 0, 10, 10), null, "000000", 1) { FillRule = DrawingFillRule.EvenOdd },
            new DrawingShape(DrawingPath.Rectangle(0, 0, 5, 5), "000000", null, 0),
        ]));

        Assert.Equal(1, svg.Split("fill-rule=\"evenodd\"").Length - 1);
        Assert.NotEqual(
            new DrawingShape(DrawingPath.Rectangle(0, 0, 1, 1), "000000", null, 0),
            new DrawingShape(DrawingPath.Rectangle(0, 0, 1, 1), "000000", null, 0) { FillRule = DrawingFillRule.EvenOdd });
    }

    public static TheoryData<DrawingItem, string> Invalid => new()
    {
        { new DrawingShape(DrawingPath.Rectangle(0, double.NaN, 1, 1), "000000", null, 0), "item 0 (a shape) has the coordinate NaN" },
        { new DrawingShape(DrawingPath.Rectangle(0, 0, 1, 1), "red", null, 0), "the fill 'red'" },
        { new DrawingShape(DrawingPath.Rectangle(0, 0, 1, 1), null, "#000000", 1), "the stroke '#000000'" },
        { new DrawingShape(DrawingPath.Rectangle(0, 0, 1, 1), null, "000000", -1), "stroke width -1" },
        { new DrawingShape(DrawingPath.Rectangle(0, 0, 1, 1), null, "000000", 1) { Dash = [2, double.PositiveInfinity] }, "dash Infinity" },
        { new DrawingText(0, 0, "Hi", double.NaN, "000000"), "item 0 (the text 'Hi') has a size NaN" },
        { new DrawingText(0, 0, [new DrawingSpan("Hi") { Color = "12345" }], 10, "000000"), "the colour '12345'" },
        { new DrawingHotspot(0, 0, 3) { Dot = "zzzzzz" }, "the dot colour 'zzzzzz'" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidDrawings_Throw_NamingTheItem(DrawingItem item, string expected)
    {
        var error = Assert.Throws<ArgumentException>(() => DrawingSvg.Write(new Drawing(10, 10, [item])));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidCanvas_Throws()
    {
        var error = Assert.Throws<ArgumentException>(() => DrawingSvg.Write(new Drawing(-1, 10, [])));
        Assert.Contains("width is -1", error.Message, StringComparison.Ordinal);
    }
}
