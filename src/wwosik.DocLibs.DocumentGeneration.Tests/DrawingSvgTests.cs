namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class DrawingSvgTests
{
    [Fact]
    public void Shapes_BecomeTheirSvgElements_WithPaintDashAndJoins()
    {
        var svg = DrawingSvg.Write(new Drawing(200, 100,
        [
            new DrawingShape(DrawingPath.Rectangle(10, 10, 50, 20), "FF0000", "000000", 1.5) { Kind = DrawingShapeKind.Rectangle, Dash = [3, 2] },
            new DrawingShape(DrawingPath.Rectangle(70, 10, 40, 40), null, "00FF00", 1) { Kind = DrawingShapeKind.Ellipse },
            new DrawingShape(new DrawingPath([new MoveTo(0, 0), new LineTo(5, 5), new CubicTo(6, 6, 7, 7, 8, 8), new ClosePath()]), "0000FF", null, 0) { RoundJoins = true },
        ]));

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\" viewBox=\"0 0 200 100\"", svg, StringComparison.Ordinal);
        Assert.Contains("<rect x=\"10\" y=\"10\" width=\"50\" height=\"20\" fill=\"#FF0000\" stroke=\"#000000\" stroke-width=\"1.5\" stroke-dasharray=\"3,2\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<ellipse cx=\"90\" cy=\"30\" rx=\"20\" ry=\"20\" fill=\"none\" stroke=\"#00FF00\" stroke-width=\"1\"/>", svg, StringComparison.Ordinal);
        Assert.Contains("<path d=\"M0,0 L5,5 C6,6 7,7 8,8 Z\" fill=\"#0000FF\"/>", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>", svg, StringComparison.Ordinal); // nothing to hover
    }

    [Fact]
    public void Text_KeepsAnchorWeightAndSpanColours_Escaped()
    {
        var svg = DrawingSvg.Write(new Drawing(200, 50,
        [
            new DrawingText(100, 20, [new DrawingSpan("a < b & "), new DrawingSpan("bold", Bold: true), new DrawingSpan(" red") { Color = "DC2626" }], 12, "111111") { Anchor = TextAnchor.Middle },
        ]));

        Assert.Contains("<text x=\"100\" y=\"20\" font-size=\"12\" fill=\"#111111\" text-anchor=\"middle\" xml:space=\"preserve\">a &lt; b &amp; <tspan font-weight=\"bold\">bold</tspan><tspan fill=\"#DC2626\"> red</tspan></text>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ALineTooWideForItsRoom_IsSetSmaller_DownTo70Percent()
    {
        static string Size(string svg) => System.Text.RegularExpressions.Regex.Match(svg, "font-size=\"([0-9.]+)\"").Groups[1].Value;

        Assert.Equal("10", Size(DrawingSvg.Write(new Drawing(100, 20, [new DrawingText(0, 10, "short", 10, "000000") { MaxWidth = 100 }]))));
        Assert.Equal("7", Size(DrawingSvg.Write(new Drawing(100, 20, [new DrawingText(0, 10, new string('x', 60), 10, "000000") { MaxWidth = 10 }]))));
    }

    [Fact]
    public void HoverText_IsAPrePlacedTooltip_ClampedToTheCanvas_WithAnInlineStyleAndNoScript()
    {
        var svg = DrawingSvg.Write(
            new Drawing(100, 60,
            [
                new DrawingShape(DrawingPath.Rectangle(90, 30, 10, 10), "FF0000", null, 0) { Kind = DrawingShapeKind.Rectangle, Hover = new DrawingHover("A long tooltip text", 95, 30) },
                new DrawingHotspot(20, 40, 10) { Dot = "1D4ED8", Hover = new DrawingHover("Jan: 5", 20, 40) },
            ]),
            new DrawingSvgOptions { TooltipFill = "222222", Background = "FAFAFA" });

        Assert.Contains("<style>", svg, StringComparison.Ordinal);
        Assert.Contains(".chart-tooltip-bg{fill:#222222;fill-opacity:.94}", svg, StringComparison.Ordinal);
        Assert.Contains("<rect x=\"0\" y=\"0\" width=\"100\" height=\"60\" fill=\"#FAFAFA\"/>", svg, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<g class=\"chart-hoverable\">").Count);
        Assert.Contains("<circle cx=\"20\" cy=\"40\" r=\"10\" fill=\"transparent\"/><circle class=\"chart-hover-dot\"", svg, StringComparison.Ordinal);

        // 19 characters at 10 px plus padding is about 122 px: wider than the canvas, so it starts at the left edge.
        Assert.Contains("<rect class=\"chart-tooltip-bg\" x=\"2\" y=\"7\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void SpanColours_TakePartInEquality()
    {
        Assert.NotEqual(new DrawingSpan("a") { Color = "FF0000" }, new DrawingSpan("a"));
        Assert.Equal(new DrawingSpan("a") { Color = "FF0000" }, new DrawingSpan("a") { Color = "FF0000" });
    }
}
