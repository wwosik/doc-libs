using System.Text;
using UglyToad.PdfPig;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Pdf.Tests;

/// <summary><c>::: steps</c>, <c>::: riskmatrix</c> and Harvey balls in PDFs.</summary>
public class StepsAndRisksTests
{
    private static readonly PageGeometry A4 = PageGeometry.For(PdfPageSize.A4, PdfOrientation.Portrait);

    private static DocumentModel Parse(string markdown) => MarkdownParser.Parse(Samples.Markdown(markdown), new PdfOptions());

    private static LaidOutPage Layout(DocumentModel model) =>
        new LayoutEngine(new FakeMetrics(), A4, new Design(ResolvedBrand.Neutral, ShowTagline: false, null, null)).Run(model).Pages.Single();

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void Sample_IsPdfA_AndRoundTrips(string template)
    {
        var (result, pdf) = Generate(Samples.Document("steps-and-risks.md"), new PdfOptions { Template = Samples.BuiltIn(template), TimeProvider = Samples.FixedTime });
        Samples.WriteForReview($"steps-and-risks-{template}.pdf", pdf);

        Assert.False(result.HasNotes, result.ToString());
        Assert.Contains("pdfaid:part", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Samples.AssertMatchesExpected($"steps-and-risks-{template}.roundtrip", Samples.RoundTrip(pdf));
    }

    [Fact]
    public void StepsAndRiskMatrix_AreDrawings_InsteadOfTheirContentShownPlainly()
    {
        var model = Parse("::: steps\n- **Analyse**\n- **Design** {.current}\n:::\n\n::: riskmatrix\n| Risk | L | I |\n|---|---|---|\n| Delay | 4 | 3 |\n:::\n");

        Assert.All(model.Blocks, b => Assert.IsType<DrawingBlock>(b));
        var page = Layout(model);
        Assert.Contains(page.Items.OfType<ShapeItem>(), s => s.Fill == ResolvedBrand.Neutral.Highlight);
        Assert.Contains(page.Items.OfType<TextItem>(), t => t.Text == "Likelihood");
        Assert.Contains(page.Items.OfType<TextItem>(), t => t.Text == "Delay");
    }

    [Theory]
    [InlineData("0/4", 1)]
    [InlineData("1/4", 2)]
    [InlineData("1/2", 2)]
    [InlineData("3/4", 2)]
    [InlineData("4/4", 2)]
    public void AHarveyBall_IsDrawnInTheTextColour_WithoutAnyText(string written, int shapes)
    {
        var page = Layout(Parse($"| Option | Cost |\n|---|---|\n| Buy | [{written}] |\n"));

        var balls = page.Items.OfType<ShapeItem>().ToList();
        Assert.Equal(shapes, balls.Count);
        Assert.Equal(ResolvedBrand.Neutral.ThemeColors[0], balls[0].Stroke);
        Assert.All(balls.Skip(1), b => Assert.Equal(ResolvedBrand.Neutral.ThemeColors[0], b.Fill));
        Assert.DoesNotContain(page.Items.OfType<TextItem>(), t => t.Text.Contains('[', StringComparison.Ordinal) || t.Text.Contains("/4", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("---", 0)]
    [InlineData(":---:", 1)]
    [InlineData("---:", 2)]
    public void AHarveyBall_FollowsTheCellsAlignment(string marker, int position)
    {
        var page = Layout(Parse($"| Option | Cost |\n|---|{marker}|\n| Buy | [4/4] |\n"));

        var ball = page.Items.OfType<ShapeItem>().First();
        var left = ball.Path.SelectMany(s => s.Points).Min(p => p.X);
        var cells = page.Items.OfType<LineItem>().Where(l => l.X1 == l.X2).Select(l => l.X1).Distinct().Order().ToList();
        var (cellLeft, cellRight) = (cells[1], cells[2]);
        var share = (left - cellLeft) / (cellRight - cellLeft);
        Assert.InRange(share, position * 0.45, (position * 0.45) + 0.1);
    }

    [Fact]
    public void AHarveyBall_IsOneTextLineHigh()
    {
        var page = Layout(Parse("| Option | Cost |\n|---|---|\n| Buy | [4/4] |\n"));

        var points = page.Items.OfType<ShapeItem>().First().Path.SelectMany(s => s.Points).ToList();
        var height = points.Max(p => p.Y) - points.Min(p => p.Y);
        Assert.InRange(height, 8, 10);
    }

    [Fact]
    public void InARealPdf_NoCharacterIsReplacedOrReported()
    {
        var (result, pdf) = Generate(Samples.Markdown("| A | B | C | D | E |\n|---|---|---|---|---|\n| [0/4] | [1/4] | [1/2] | [3/4] | [4/4] |\n"), new PdfOptions());

        Assert.False(result.HasNotes, result.ToString());
        using var document = PdfDocument.Open(pdf);
        var text = document.GetPage(1).Text;
        Assert.DoesNotContain("/4", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u25D1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACellWithMoreThanTheBall_StaysText()
    {
        var model = Parse("| A | B |\n|---|---|\n| [3/4] done | \\[3/4] |\n");

        var row = Assert.IsType<TableBlock>(Assert.Single(model.Blocks)).Rows[1];
        Assert.Equal(["[3/4] done", "[3/4]"], row.Select(cell => string.Concat(cell.Select(r => r.Text))));
    }

    [Fact]
    public void InGerman_TheAxisTitlesAreGerman()
    {
        var (_, pdf) = Generate(Samples.Markdown("---\nlang: de\n---\n\n::: riskmatrix\n| Risiko | W | A |\n|---|---|---|\n| Verzug | 4 | 3 |\n:::\n"), new PdfOptions());

        using var document = PdfDocument.Open(pdf);
        var text = document.GetPage(1).Text;
        Assert.Contains("Eintrittswahrscheinlichkeit", text, StringComparison.Ordinal);
        Assert.Contains("Auswirkung", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("::: steps\n- **A** {.current}\n- **B** {.current}\n:::\n", "at line 3 is the second one marked {.current}")]
    [InlineData("::: riskmatrix\n| R | L | I |\n|---|---|---|\n| A | 2 | 0 |\n:::\n", "at line 4 has the impact '0'")]
    public void Mistakes_ThrowInThePdfsFormat(string markdown, string expected)
    {
        var error = Assert.Throws<DocumentConversionException>(() => Parse(markdown));

        Assert.Equal(DocumentFormat.Pdf, error.Format);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    private static (GenerationResult Result, byte[] Pdf) Generate(MarkdownDocument document, PdfOptions options)
    {
        using var output = new MemoryStream();
        var result = PdfGenerator.Generate(document, output, options);
        return (result, output.ToArray());
    }

    private sealed class FakeMetrics : IFontMetrics
    {
        public double Width(string text, FontFace face, double size) => text.Length * size * 0.5;

        public bool HasGlyph(Rune character, FontFace face) => character.IsBmp;
    }
}
