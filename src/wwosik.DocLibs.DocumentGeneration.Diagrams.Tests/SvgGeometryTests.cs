namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

public class SvgGeometryTests
{
    [Fact]
    public void Path_HandlesAbsoluteAndRelativeCommands()
    {
        var path = SvgGeometry.Path("M10,20 l5 0 H30 v10 L 0 0 z m 1 1");

        Assert.Equal(
            [new MoveTo(10, 20), new LineTo(15, 20), new LineTo(30, 20), new LineTo(30, 30), new LineTo(0, 0), new ClosePath(), new MoveTo(11, 21)],
            path);
    }

    [Fact]
    public void Path_ImplicitPairsAfterMoveAreLines() =>
        Assert.Equal([new MoveTo(0, 0), new LineTo(10, 0), new LineTo(10, 10)], SvgGeometry.Path("M0 0 10 0 10 10"));

    [Fact]
    public void Quadratic_BecomesTheEquivalentCubic()
    {
        var curve = Assert.IsType<CubicTo>(SvgGeometry.Path("M0,0 Q30,30 60,0")[1]);

        Assert.Equal(new CubicTo(20, 20, 40, 20, 60, 0), curve);
    }

    [Fact]
    public void Arc_EndsExactlyAtItsEndPoint_InPiecesOfAtMostAQuarter()
    {
        var path = SvgGeometry.Path("M 232 240 L 232 112 A 128 128 0 1 1 108.91 204.88 Z");

        var arcs = path.OfType<CubicTo>().ToList();
        Assert.Equal(4, arcs.Count); // 286°
        Assert.Equal((108.91, 204.88), (arcs[^1].X, arcs[^1].Y));
        Assert.All(arcs, a => Assert.InRange(Math.Sqrt(Math.Pow(a.X - 232, 2) + Math.Pow(a.Y - 240, 2)), 127.99, 128.01));
    }

    [Fact]
    public void Semicircle_PassesThroughTheFarSide()
    {
        var arcs = SvgGeometry.Path("M0,0 A10,10 0 0 1 20,0").OfType<CubicTo>().ToList();

        Assert.Equal(2, arcs.Count);
        Assert.Equal(10, arcs[0].X, 6);
        Assert.Equal(-10, arcs[0].Y, 6); // sweep 1 runs clockwise on screen: over the top
    }

    [Fact]
    public void Transform_ComposesLeftToRight()
    {
        var transform = SvgGeometry.Transform("translate(10 20) scale(2)");

        Assert.Equal((12.0, 24.0), transform.Apply(1, 2));
        Assert.True(transform.IsAxisAligned);
        Assert.False(SvgGeometry.Transform("rotate(45)").IsAxisAligned);
    }

    [Theory]
    [InlineData("skewX(10)")]
    [InlineData("translate(1 2) nonsense")]
    public void UnknownTransform_Throws(string value) => Assert.Throws<InvalidDataException>(() => SvgGeometry.Transform(value));

    [Fact]
    public void MalformedPath_Throws() => Assert.Throws<InvalidDataException>(() => SvgGeometry.Path("M 1"));
}
