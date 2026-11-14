using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Operations.TextPositioning;
using UglyToad.PdfPig.PdfFonts;
using UglyToad.PdfPig.Writer;
using wwosik.DocLibs.DocumentConversions.Pdf;

namespace wwosik.DocLibs.DocumentConversions.Tests;

/// <summary>Tables, lists, code, images in place, superscripts and watermarks read from small PDFs built here.</summary>
public class PdfStructureTests
{
    [Fact]
    public void AGridDrawnCellByCell_BecomesAPipeTable_WhereItStands()
    {
        var pdf = new TestPdf().Text("Before the table.", 72, 760);
        double[] xs = [72, 180, 300];
        string[][] cells = [["Name", "Size"], ["Alpha", "12"], ["Beta", "7"]];
        pdf.Grid(xs, 740, cells);
        pdf.Text("After the table.", 72, 660);

        var markdown = pdf.Convert();

        Assert.Equal(
            "## Page 1\n\nBefore the table.\n\n| Name | Size |\n| --- | --- |\n| Alpha | 12 |\n| Beta | 7 |\n\nAfter the table.\n",
            markdown);
    }

    [Fact]
    public void ACellSpanningColumns_KeepsItsTextInTheFirst_AndALinesTextIsJoined()
    {
        var pdf = new TestPdf();
        double[] xs = [72, 180, 300, 420];
        pdf.Grid(xs, 740, [["A", "B", "C"], ["one", "two", "three"], ["wide", "", ""]], merged: (2, 1), height: 40);
        pdf.Text("long", 184, 675, 10).Text("cell", 184, 664, 10);

        var markdown = pdf.Convert();

        Assert.Contains("| A | B | C |", markdown, StringComparison.Ordinal);
        Assert.Contains("| one | two long cell | three |", markdown, StringComparison.Ordinal);
        Assert.Contains("| wide |  |  |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuledTable_GetsItsColumnsFromTheGutters()
    {
        var pdf = new TestPdf().Text("Figures:", 72, 760);
        pdf.Line(72, 745, 400, 745).Line(72, 725, 400, 725);
        string[][] rows = [["Region", "Revenue", "Change"], ["North", "1,200", "+4 %"], ["South", "950", "-2 %"], ["East", "80", "+1 %"]];
        var y = 731.0;
        for (var r = 0; r < rows.Length; r++)
        {
            y = r == 0 ? 731 : y - (r == 1 ? 18 : 14);
            pdf.Text(rows[r][0], 72, y, 10).Text(rows[r][1], 200, y, 10).Text(rows[r][2], 320, y, 10);
        }

        pdf.Line(72, y - 6, 400, y - 6);

        var markdown = pdf.Convert();

        Assert.Contains("Figures:\n\n| Region | Revenue | Change |\n| --- | --- | --- |\n| North | 1,200 | +4 % |\n| South | 950 | -2 % |\n| East | 80 | +1 % |\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesAroundProse_AreNoTable()
    {
        var pdf = new TestPdf();
        pdf.Line(72, 760, 500, 760);
        pdf.Text("A paragraph of prose that runs across the whole width of the text,", 72, 745);
        pdf.Line(72, 735, 500, 735);
        pdf.Text("and another one that does the same, so no gutter runs through it.", 72, 720);
        pdf.Line(72, 710, 500, 710);

        var markdown = pdf.Convert();

        Assert.DoesNotContain("|", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void DrawnDots_AreBullets_NestedByTheirPosition()
    {
        var pdf = new TestPdf();
        var y = 760.0;
        foreach (var (level, text) in new[] { (0, "One"), (1, "One a"), (2, "One a i"), (1, "One b"), (0, "Two") })
        {
            var x = 90 + (level * 20);
            pdf.Page.DrawCircle(new PdfPoint(x - 8, y + 3.5), 4, 0.5, true);
            pdf.Text(text, x, y);
            y -= 15;
        }

        Assert.Equal("## Page 1\n\n- One\n  - One a\n    - One a i\n  - One b\n- Two\n", pdf.Convert());
    }

    [Fact]
    public void Boxes_AreTasks_TickedWhenTheyHoldATickOrAreFilled_DotsAreBullets()
    {
        var pdf = new TestPdf();
        pdf.Page.DrawRectangle(new PdfPoint(78, 760), 8, 8, 0.75, false);
        pdf.Text("open", 90, 760);
        pdf.Page.DrawRectangle(new PdfPoint(78, 745), 8, 8, 0.75, false);
        pdf.Line(79.5, 749, 81.5, 746.5, 1).Line(81.5, 746.5, 85, 752, 1);
        pdf.Text("ticked", 90, 745);
        pdf.Page.DrawRectangle(new PdfPoint(78, 730), 8, 8, 0.75, true);
        pdf.Text("filled", 90, 730);
        pdf.Page.DrawRectangle(new PdfPoint(80, 717), 3, 3, 0.5, true);
        pdf.Text("dot", 90, 715);

        Assert.Equal("## Page 1\n\n- [ ] open\n- [x] ticked\n- [x] filled\n- dot\n", pdf.Convert());
    }

    [Fact]
    public void NumberedLinesWithAHangingIndent_AreAnOrderedList_ASingleOneIsNot()
    {
        var pdf = new TestPdf();
        pdf.Text("1.", 76, 760).Text("First item that wraps onto", 90, 760).Text("a second line.", 90, 746);
        pdf.Text("2.", 76, 730).Text("Second item.", 90, 730);
        pdf.Text("Between the lists.", 72, 700);
        pdf.Text("7.", 76, 680).Text("A numbered line alone.", 90, 680);

        Assert.Equal(
            "## Page 1\n\n1. First item that wraps onto a second line.\n2. Second item.\n\nBetween the lists.\n\n7\\. A numbered line alone.\n",
            pdf.Convert());
    }

    [Fact]
    public void MonospaceLines_AreAFencedBlock_WithTheirIndentation()
    {
        var pdf = new TestPdf().Text("Code:", 72, 760);
        string[] code = ["def main():", "    if ready:", "        start()", string.Empty, "main()"];
        var y = 745.0;
        foreach (var line in code)
        {
            if (line.Length > 0)
            {
                var indent = line.Length - line.TrimStart().Length;
                pdf.Text(line.TrimStart(), 72 + (indent * 6), y, 10, pdf.Mono);
            }

            y -= 12;
        }

        pdf.Text("Done.", 72, y - 10);

        Assert.Equal("## Page 1\n\nCode:\n\n```\ndef main():\n    if ready:\n        start()\n\nmain()\n```\n\nDone.\n", pdf.Convert());
    }

    [Fact]
    public void Backticks_InCode_LengthenTheFence()
    {
        var markdown = new TestPdf().Text("echo ```", 72, 760, 10, mono: true).Convert();

        Assert.Contains("````\necho ```\n````", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Monospace_IsFoundByName_OrByEqualAdvances()
    {
        static Letter Letter(string value, double width, string font) =>
            new(value, new PdfRectangle(0, 0, width, 8), new PdfRectangle(0, 0, width, 8), new PdfPoint(0, 0), new PdfPoint(width, 0), width, 10,
                new FontDetails(font, false, 400, false), TextRenderingMode.Fill, GrayColor.Black, GrayColor.Black, 10, 0);

        Assert.True(PdfConverter.Monospace([.. "abc".Select(c => Letter(c.ToString(), 6, "Courier"))], 10));
        Assert.True(PdfConverter.Monospace([.. "while".Select(c => Letter(c.ToString(), 6, "Unnamed"))], 10));
        Assert.False(PdfConverter.Monospace([.. "while".Select((c, i) => Letter(c.ToString(), 4 + i, "Unnamed"))], 10));
        Assert.False(PdfConverter.Monospace([.. "aaaa".Select(c => Letter(c.ToString(), 6, "Unnamed"))], 10));
    }

    [Fact]
    public void AnImage_StandsBetweenTheParagraphsItSitsBetween()
    {
        var pdf = new TestPdf().Text("Above.", 72, 760);
        pdf.Page.AddPng(TestPdf.Png, new PdfRectangle(72, 640, 172, 740));
        pdf.Text("Below.", 72, 620);

        Assert.Equal("## Page 1\n\nAbove.\n\n![](images/page1-image1.png)\n\nBelow.\n", pdf.Convert());
    }

    [Fact]
    public void ASuperscript_StaysOnItsLine_AndPairedWithANoteAtTheFoot_BecomesAFootnote()
    {
        var pdf = new TestPdf();
        var x = pdf.EndOf("An area of 20 m", 72, 760);
        pdf.Text("An area of 20 m", 72, 760).Text("2", x + 0.5, 764, 7);
        pdf.Text("and a claim", x + 8, 760);
        x = pdf.EndOf("and a claim", x + 8, 760);
        pdf.Text("1", x + 0.5, 764, 7).Text("that needs a source.", x + 8, 760);
        pdf.Text("1", 72, 100, 8).Text("The source of the claim.", 80, 100, 8);

        var markdown = pdf.Convert();

        Assert.Equal("## Page 1\n\nAn area of 20 m2 and a claim[^1] that needs a source.\n\n[^1]: The source of the claim.\n", markdown);
    }

    [Fact]
    public void ANoteNobodyRefersTo_StaysText()
    {
        var pdf = new TestPdf().Text("Text without references.", 72, 760);
        pdf.Text("1", 72, 100, 8).Text("A note.", 80, 100, 8);

        Assert.Equal("## Page 1\n\nText without references.\n\n1 A note.\n", pdf.Convert());
    }

    [Fact]
    public void RotatedText_IsLeftOut_AndDoesNotChangeTheBodySize()
    {
        var pdf = new TestPdf();
        pdf.Rotated("CONFIDENTIAL CONFIDENTIAL CONFIDENTIAL", 14, 120, 300);
        pdf.Text("Heading", 72, 760, 14);
        pdf.Text("A paragraph in the body size.", 72, 740);

        var markdown = pdf.Convert();

        Assert.Equal("## Page 1\n\n### Heading\n\nA paragraph in the body size.\n", markdown);
    }

    [Fact]
    public void RotatedText_IsLeftOut_OfTheFallbackToo()
    {
        var pdf = new TestPdf();
        pdf.Rotated("DRAFT", 40, 200, 300);
        pdf.Text("Only this.", 72, 760);
        var converter = new PdfConverter { AnalysisStarting = (_, _) => throw new InvalidOperationException("broken") };

        Assert.Equal("## Page 1\n\nOnly this.\n", pdf.Convert(converter));
    }

    [Fact]
    public void AFailingStructuredAnalysis_FallsBackToPlainParagraphs()
    {
        var pdf = new TestPdf().Text("Before.", 72, 760);
        pdf.Grid([72, 180, 300], 740, [["Name", "Size"], ["Alpha", "12"]]);
        var converter = new PdfConverter { AnalysisStarting = (_, structured) => { if (structured) { throw new InvalidOperationException("broken"); } } };

        var markdown = pdf.Convert(converter);

        Assert.DoesNotContain("|", markdown, StringComparison.Ordinal);
        Assert.Contains("Name", markdown, StringComparison.Ordinal);
        Assert.Contains("Alpha", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStructureSample_HasEveryKindOfBlock()
    {
        using var stream = Samples.Open("structure.pdf");

        var markdown = new PdfConverter().Convert(stream, ConversionOptions.Default).Markdown;

        Assert.Contains("| Plums | Out of stock |  |", markdown, StringComparison.Ordinal);
        Assert.Contains("| March | 1,380 | +20 % |", markdown, StringComparison.Ordinal);
        Assert.Contains("refers to it.[^1]", markdown, StringComparison.Ordinal);
        Assert.Contains("[^1]: The figures are made up for this sample.", markdown, StringComparison.Ordinal);
        Assert.Contains("  - Apples", markdown, StringComparison.Ordinal);
        Assert.Contains("- [x] Water the plants", markdown, StringComparison.Ordinal);
        Assert.Contains("2. Second step", markdown, StringComparison.Ordinal);
        Assert.Contains("```\nif (ready)\n{\n    Start();\n\n}\n```", markdown, StringComparison.Ordinal);
        Assert.Contains("Before the image.\n\n![](images/page2-image1.png)\n\nAfter the image.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", markdown, StringComparison.Ordinal);
    }

    /// <summary>A one-page PDF built with PdfPig's writer, in PDF coordinates (points, y upwards).</summary>
    private sealed class TestPdf
    {
        // A 20 × 20 PNG, the generator tests' photo.png.
        public static readonly byte[] Png = System.Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAIAAAAC64paAAACf0lEQVR4nAXBwQAAIBAAwQMIIIAAAgjgAAIIIICe+wgggAACCCCAAAI4kGZEBCd4IQhRSIIKWShCFZrQhSFMYQlbOMIVnmCCiMM5vCM4oiM51JEdxVEdzdEdwzEdy7Edx3Edz2EOEY/zeE/wRE/yqCd7iqd6mqd7hmd6lmd7jud6nsc8IgEX8IEQiIEU0EAOlEANtEAPjMAMrMAOnMANvIAFRCIu4iMhEiMpopEcKZEaaZEeGZEZWZEdOZEbeRGLiCRcwidCIiZSQhM5URI10RI9MRIzsRI7cRI38RKWEFGc4pWgRCUpqmSlKFVpSleGMpWlbOUoV3mKKSIZl/GZkImZlNFMzpRMzbRMz4zMzKzMzpzMzbyMZUQKruALoRALqaCFXCiFWmiFXhiFWViFXTiFW3gFK4hUXMVXQiVWUkUruVIqtdIqvTIqs7Iqu3Iqt/IqVhFpuIZvhEZspIY2cqM0aqM1emM0ZmM1duM0buM1rCHScR3fCZ3YSR3t5E7p1E7r9M7ozM7q7M7p3M7rWEdk4AZ+EAZxkAY6yIMyqIM26IMxmIM12IMzuIM3sIHIxE38JEziJE10kidlUidt0idjMidrsidncidvYhORhVv4RVjERVroIi/Koi7aoi/GYi7WYi/O4i7ewhYiG7fxm7CJm7TRTd6UTd20Td+Mzdyszd6czd28jW1EDu7gD+EQD+mgh3woh3poh34Yh3lYh304h3t4BzuIXNzFX8IlXtJFL/lSLvXSLv0yLvOyLvtyLvfyLnYRebiHf4RHfKSHPvKjPOqjPfpjPOZjPfbjPO7jPewhYjjDG8GIRjLUyEYxqtGMbgxjGsvYxjGu8QwzPsfjLF9mkRmmAAAAAElFTkSuQmCC");

        private readonly PdfDocumentBuilder builder = new();

        public TestPdf()
        {
            this.Regular = this.builder.AddStandard14Font(Standard14Font.Helvetica);
            this.Mono = this.builder.AddStandard14Font(Standard14Font.Courier);
            this.Page = this.builder.AddPage(PageSize.A4);
        }

        public PdfPageBuilder Page { get; }

        public PdfDocumentBuilder.AddedFont Regular { get; }

        public PdfDocumentBuilder.AddedFont Mono { get; }

        public TestPdf Text(string text, double x, double y, double size = 11, PdfDocumentBuilder.AddedFont? font = null, bool mono = false)
        {
            this.Page.AddText(text, size, new PdfPoint(x, y), mono ? this.Mono : font ?? this.Regular);
            return this;
        }

        public double EndOf(string text, double x, double y, double size = 11) =>
            this.Page.MeasureText(text, size, new PdfPoint(x, y), this.Regular)[^1].BoundingBox.Right;

        public TestPdf Line(double x1, double y1, double x2, double y2, double width = 0.5)
        {
            this.Page.DrawLine(new PdfPoint(x1, y1), new PdfPoint(x2, y2), width);
            return this;
        }

        // Rows from top down, every rule drawn per cell; merged = (row, column) whose right border is left out.
        public TestPdf Grid(double[] xs, double top, string[][] cells, (int Row, int Column)? merged = null, double height = 20)
        {
            for (var r = 0; r < cells.Length; r++)
            {
                var bottom = top - height;
                for (var c = 0; c < xs.Length - 1; c++)
                {
                    this.Line(xs[c], top, xs[c + 1], top).Line(xs[c], bottom, xs[c + 1], bottom);
                    if (cells[r][c].Length > 0)
                    {
                        this.Text(cells[r][c], xs[c] + 4, top - 14, 10);
                    }
                }

                for (var c = 0; c < xs.Length; c++)
                {
                    if (merged is not { } m || m.Row != r || m.Column + 1 != c)
                    {
                        this.Line(xs[c], top, xs[c], bottom);
                    }
                }

                top = bottom;
            }

            return this;
        }

        public TestPdf Rotated(string text, double size, double x, double y)
        {
            var start = this.Page.CurrentStream.Operations.Count;
            this.Page.AddText(text, size, new PdfPoint(x, y), this.Regular);
            var operations = this.Page.CurrentStream.Operations;
            for (var i = start; i < operations.Count; i++)
            {
                if (operations[i] is MoveToNextLineWithOffset)
                {
                    operations[i] = new SetTextMatrix([0.7071, 0.7071, -0.7071, 0.7071, x, y]);
                }
            }

            return this;
        }

        public string Convert(PdfConverter? converter = null) =>
            (converter ?? new PdfConverter()).Convert(new MemoryStream(this.builder.Build()), ConversionOptions.Default).Markdown;
    }
}
