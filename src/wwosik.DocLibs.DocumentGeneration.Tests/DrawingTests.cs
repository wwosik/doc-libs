namespace wwosik.DocLibs.DocumentGeneration.Tests;

public class DrawingTests
{
    [Fact]
    public void Drawings_CompareByContent()
    {
        static Drawing Make(string label) => new(10, 20,
        [
            new DrawingShape(DrawingPath.Rectangle(0, 0, 10, 20), "FFFFFF", "000000", 1) { Dash = [2, 1], Kind = DrawingShapeKind.Rectangle },
            new DrawingText(5, 10, label, 12, "000000") { Anchor = TextAnchor.Middle, MaxWidth = 8 },
        ]);

        Assert.Equal(Make("a"), Make("a"));
        Assert.NotEqual(Make("a"), Make("b"));
    }

    [Fact]
    public void Bounds_HoldEveryPoint_ControlPointsToo()
    {
        var path = new DrawingPath([new MoveTo(0, 0), new CubicTo(-5, 10, 20, 10, 15, 0), new ClosePath()]);

        Assert.Equal((-5.0, 0.0, 25.0, 10.0), path.Bounds);
    }

    [Fact]
    public void Text_IsTheConcatenatedSpans() =>
        Assert.Equal("Bold rest", new DrawingText(0, 0, [new DrawingSpan("Bold", Bold: true), new DrawingSpan(" rest")], 12, "000000").Text);

    [Fact]
    public void FrontMatter_ReportsHowManyLinesItTakes()
    {
        Assert.Equal(3, FrontMatter.Read("---\ntitle: T\n---\n# A\n", DocumentConversions.DocumentFormat.Pdf).BodyLine);
        Assert.Equal(0, FrontMatter.Read("# A\n", DocumentConversions.DocumentFormat.Pdf).BodyLine);
    }
}
