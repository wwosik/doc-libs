using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary><c>::: timeline</c> and <c>::: gantt</c> in PDFs.</summary>
public class ScheduleTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static DocumentModel Parse(string markdown, PdfOptions? options = null) =>
        MarkdownParser.Parse(Samples.Markdown(markdown), (options ?? new PdfOptions()) with { TimeProvider = Samples.FixedTime });

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsPdfA_AndRoundTrips(string template)
    {
        var pdf = Samples.Generate(Samples.Document("schedules.md"), new PdfOptions { Template = Samples.BuiltIn(template), TimeProvider = Samples.FixedTime });
        Samples.WriteForReview($"schedules-{template}.pdf", pdf);

        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Samples.AssertMatchesExpected($"schedules-{template}.roundtrip", Samples.RoundTrip(pdf));
    }

    [Fact]
    public void ADrawnContainer_IsADrawing_InsteadOfItsContentShownPlainly()
    {
        var model = Parse("Before.\n\n::: gantt today=2026-03-01\n| Task | Start | End |\n|---|---|---|\n| Build | 2026-02 | 2026-04 |\n:::\n");

        Assert.Equal(2, model.Blocks.Count);
        var drawing = Assert.IsType<DrawingBlock>(model.Blocks[1]).Drawing;
        Assert.Contains(drawing.Items.OfType<DrawingText>(), t => t.Text == "Build");

        var page = new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null)).Run(model).Pages.Single();
        Assert.Contains(page.Items.OfType<ShapeItem>(), s => s.Fill == ResolvedBrand.Neutral.Diagram.Palette[0]);
        Assert.Contains(page.Items.OfType<TextItem>(), t => t.Text == "Build");
    }

    [Fact]
    public void TheClock_IsTheOptionsTimeProvider()
    {
        // FixedTime is 9 March 2026: January is past, May is next.
        var drawing = Assert.IsType<DrawingBlock>(Assert.Single(Parse("::: timeline\n- **2026-01** Start\n- **2026-05** Pilot\n:::\n").Blocks)).Drawing;

        var dots = drawing.Items.OfType<DrawingShape>().Where(s => s.Kind == DrawingShapeKind.Ellipse && s.Fill is not null).ToList();
        Assert.Equal([ResolvedBrand.Neutral.Diagram.Line, ResolvedBrand.Neutral.Diagram.Accent], dots.Select(d => d.Fill));
    }

    [Fact]
    public void InGerman_TheDatesAreGerman()
    {
        using var output = new MemoryStream();
        PdfGenerator.Generate(Samples.Markdown("---\nlang: de\n---\n\n::: timeline\n- **2026-03-15** Start\n- **2026-05** Pilot\n:::\n"), output, new PdfOptions { TimeProvider = Samples.FixedTime });

        using var document = PdfDocument.Open(output.ToArray());
        var text = document.GetPage(1).Text;
        Assert.Contains("März", text, StringComparison.Ordinal);
        Assert.Contains("Mai", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("::: timeline\n- **March 2026** Start\n- **2026-05** Pilot\n:::\n", "at line 2 has the date 'March 2026'; write it as 2026, 2026-Q2, 2026-03 or 2026-03-15")]
    [InlineData("::: gantt grid=day\n| T | S | E |\n|---|---|---|\n| A | 2026-01 | 2026-02 |\n:::\n", "grid=week|month|quarter expected")]
    public void Mistakes_ThrowInThePdfsFormat(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Parse(markdown));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
