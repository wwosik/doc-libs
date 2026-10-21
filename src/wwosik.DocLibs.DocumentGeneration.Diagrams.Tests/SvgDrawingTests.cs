namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

public class SvgDrawingTests
{
    [Fact]
    public void Shapes_KeepTheirKind_AndResolveTheirColours()
    {
        var drawing = Convert("""
            <rect x="1" y="2" width="30" height="20" fill="var(--surface)" stroke="#000" />
            <rect x="1" y="2" width="30" height="20" rx="4" fill="#fff" />
            <circle cx="10" cy="10" r="5" fill="none" stroke="var(--border)" stroke-width="2" stroke-dasharray="3 2" />
            <ellipse cx="10" cy="10" rx="8" ry="3" fill="#123456" />
            """);

        Assert.Equal(200, drawing.Width);
        var shapes = drawing.Items.Cast<DrawingShape>().ToList();
        Assert.Equal([DrawingShapeKind.Rectangle, DrawingShapeKind.RoundedRectangle, DrawingShapeKind.Ellipse, DrawingShapeKind.Ellipse], shapes.Select(s => s.Kind));
        Assert.Equal(("E6F4F5", "000000"), (shapes[0].Fill, shapes[0].Stroke));
        Assert.Equal((1.0, 2.0, 30.0, 20.0), shapes[0].Path.Bounds);
        Assert.Equal(4, shapes[1].CornerRadius);
        Assert.Null(shapes[1].Stroke);
        Assert.Equal((null, "0E7C86", 2.0), (shapes[2].Fill, shapes[2].Stroke, shapes[2].StrokeWidth));
        Assert.Equal([3.0, 2.0], shapes[2].Dash!);
        Assert.Equal((2.0, 7.0, 16.0, 6.0), shapes[3].Path.Bounds);
    }

    [Fact]
    public void Markers_ArePlacedAtTheEnd_TurnedAlongThePath()
    {
        var drawing = Convert("""
            <path d="M10,10 L10,50" fill="none" stroke="#000" marker-end="url(#arrow)" />
            <defs>
              <marker id="arrow" markerUnits="userSpaceOnUse" refX="10" refY="6" orient="auto">
                <polygon points="0,1.5 10,6 0,10.5" fill="#FF0000" />
              </marker>
            </defs>
            """);

        var arrow = Assert.IsType<DrawingShape>(drawing.Items[1]);
        Assert.Equal("FF0000", arrow.Fill);
        var points = arrow.Path.Segments.SelectMany(s => s.Points).Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).ToList();
        Assert.Equal([(14.5, 40.0), (10.0, 50.0), (5.5, 40.0)], points); // pointing down, tip on the end
    }

    [Fact]
    public void MarkerStart_PointsAlongTheFirstSegment()
    {
        var drawing = Convert("""
            <line x1="0" y1="0" x2="20" y2="0" stroke="#000" marker-start="url(#m)" />
            <defs><marker id="m" markerUnits="userSpaceOnUse" refX="0" refY="0" orient="auto"><path d="M0,0 L5,0" stroke="#000" /></marker></defs>
            """);

        var marker = Assert.IsType<DrawingShape>(drawing.Items[1]);
        Assert.Equal([new MoveTo(0, 0), new LineTo(5, 0)], marker.Path.Segments);
    }

    [Fact]
    public void Text_SplitsIntoLines_WithBoldSpans_AndCollapsedWhitespace()
    {
        var drawing = Convert("""
            <g class="node">
              <rect x="0" y="0" width="116" height="60" />
              <text x="58" y="30" text-anchor="middle" font-size="var(--fs-s)" font-weight="500" fill="var(--_text)"><tspan x="58" dy="-4.2"><tspan font-weight="bold">x</tspan> &amp;   y</tspan><tspan x="58" dy="18.2">second line</tspan></text>
            </g>
            <text x="5" y="100" class="mono" font-weight="600" dy="4.9">  plain  </text>
            """);

        var texts = drawing.Items.OfType<DrawingText>().ToList();
        Assert.Equal(3, texts.Count);
        Assert.Equal([new DrawingSpan("x", true), new DrawingSpan(" & y")], texts[0].Spans);
        Assert.Equal((58.0, 25.8, 14.0, TextAnchor.Middle, "1F2937"), (texts[0].X, Math.Round(texts[0].Baseline, 6), texts[0].Size, texts[0].Anchor, texts[0].Color));
        Assert.Equal(100, texts[0].MaxWidth);
        Assert.Equal(("second line", 44.0), (texts[1].Text, Math.Round(texts[1].Baseline, 6)));
        Assert.Equal([new DrawingSpan("plain", true)], texts[2].Spans);
        Assert.True(texts[2].Mono);
        Assert.Null(texts[2].MaxWidth);
        Assert.Equal(104.9, texts[2].Baseline);
    }

    [Fact]
    public void Groups_MoveTheirContent_AndPassOnTheirPaint()
    {
        var drawing = Convert("""<g transform="translate(100 50)" fill="none" stroke="#00FF00" stroke-linejoin="round"><circle cx="0" cy="0" r="2" /></g>""");

        var circle = Assert.IsType<DrawingShape>(Assert.Single(drawing.Items));
        Assert.Equal((98.0, 48.0, 4.0, 4.0), circle.Path.Bounds);
        Assert.Equal((null, "00FF00", true), (circle.Fill, circle.Stroke, circle.RoundJoins));
    }

    [Fact]
    public void Opacity_IsMixedWithWhite() =>
        Assert.Equal("8080FF", Assert.IsType<DrawingShape>(Assert.Single(Convert("""<rect width="1" height="1" fill="#0000FF" fill-opacity="0.5" />""").Items)).Fill);

    [Theory]
    [InlineData("""<image href="x.png" width="1" height="1" />""", "<image>")]
    [InlineData("""<foreignObject width="1" height="1"><div xmlns="http://www.w3.org/1999/xhtml">x</div></foreignObject>""", "<foreignObject>")]
    [InlineData("""<rect width="1" height="1" fill="rgb(1,2,3)" />""", "rgb(1,2,3)")]
    [InlineData("""<path d="M0,0 L1,1" stroke="#000" marker-mid="url(#m)" />""", "marker-mid")]
    [InlineData("""<text x="0" y="0" transform="rotate(90)">x</text>""", "rotated text")]
    public void WhatCannotBeDrawn_Throws(string content, string named)
    {
        var error = Assert.Throws<InvalidDataException>(() => Convert(content));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctypes_AreRefused() =>
        Assert.Throws<InvalidDataException>(() => SvgDrawing.Convert("""<!DOCTYPE svg [<!ENTITY x "y">]><svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>"""));

    private static Drawing Convert(string content) => SvgDrawing.Convert($$"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 120" width="200" height="120" style="--bg:#FFFFFF;--fg:#1F2937;--surface:#E6F4F5;--border:#0E7C86">
        <style>
          svg { --_text: var(--fg); --fs-s: 0.875rem; }
        </style>
        {{content}}
        </svg>
        """);
}
