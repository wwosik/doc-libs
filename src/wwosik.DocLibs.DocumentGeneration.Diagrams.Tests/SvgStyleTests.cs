namespace wwosik.DocLibs.DocumentGeneration.Diagrams.Tests;

public class SvgStyleTests
{
    private static readonly SvgStyle Style = SvgStyle.Read(
        "--bg:#FFFFFF;--fg:#1F2937;--accent:#0E7C86;background:var(--bg)",
        """
        text { font-family: 'X', sans-serif; }
        svg {
          --_text: var(--fg);
          --_line: var(--line, color-mix(in srgb, var(--fg) 50%, var(--bg)));
          --_arrow: var(--accent, var(--fg));
          --fs-s: 0.875rem;
        }
        """);

    [Theory]
    [InlineData("#0e7c86", "0E7C86")]
    [InlineData("#abc", "AABBCC")]
    [InlineData("var(--_text)", "1F2937")]
    [InlineData("var(--_arrow)", "0E7C86")]
    [InlineData("var(--missing, #123456)", "123456")]
    [InlineData("var(--_line)", "8F949B")]
    [InlineData("color-mix(in srgb, #000000 25%, #FFFFFF)", "BFBFBF")]
    [InlineData("color-mix(in srgb, #000000, #FFFFFF 75%)", "BFBFBF")]
    [InlineData("color-mix(in srgb, color-mix(in srgb, var(--accent, var(--fg)) 50%, var(--bg)) 50%, #000000)", "445F62")]
    [InlineData("white", "FFFFFF")]
    public void Colours_Resolve(string value, string expected) => Assert.Equal(expected, Style.Color(value));

    [Theory]
    [InlineData("none")]
    [InlineData("transparent")]
    public void None_IsNoColour(string value) => Assert.Null(Style.Color(value));

    [Theory]
    [InlineData("var(--fs-s)", 14)]
    [InlineData("12", 12)]
    [InlineData("12.5px", 12.5)]
    [InlineData("1.5em", 24)]
    public void Lengths_Resolve(string value, double expected) => Assert.Equal(expected, Style.Length(value));

    [Theory]
    [InlineData("rgb(1, 2, 3)")]
    [InlineData("var(--undefined)")]
    [InlineData("color-mix(in oklch, #000000, #FFFFFF)")]
    [InlineData("url(#nowhere)")]
    public void UnknownCss_Throws(string value)
    {
        var error = Assert.Throws<InvalidDataException>(() => Style.Color(value));

        Assert.Contains("cannot be drawn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gradients_StandForTheirFirstStop()
    {
        var style = SvgStyle.Read(null, null);
        style.Gradients["g"] = "#336699";

        Assert.Equal("336699", style.Color("url(#g)"));
    }
}
