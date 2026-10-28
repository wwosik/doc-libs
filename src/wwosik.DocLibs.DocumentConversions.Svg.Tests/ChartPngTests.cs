using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Charts;
using wwosik.DocLibs.DocumentGeneration.Charts.Tests;

namespace wwosik.DocLibs.DocumentConversions.Svg.Tests;

/// <summary>The financial charts as PNG, via their SVG; the PNGs land in the test output's <c>review</c> folder for a look.</summary>
public class ChartPngTests
{
    public static TheoryData<string> Charts => [.. All.Keys];

    private static Dictionary<string, FinancialChart> All => new()
    {
        ["development"] = FinancialSamples.Development(),
        ["development-growth"] = FinancialSamples.Development(DevelopmentPanel.Growth),
        ["growth-by-row"] = FinancialSamples.GrowthByRow(),
        ["actual-vs-reference"] = FinancialSamples.ActualVsReference(),
        ["actual-vs-reference-value"] = FinancialSamples.ActualVsReference(ReferenceDisplay.Value),
        ["growth-trend"] = FinancialSamples.GrowthTrend(),
        ["two-year"] = FinancialSamples.TwoYear(),
        ["summary-table"] = FinancialSamples.Summary(),
    };

    [Theory]
    [MemberData(nameof(Charts))]
    public void EveryFinancialChart_BecomesAPng_AtTwiceItsSize(string name)
    {
        var chart = All[name];
        var drawing = FinancialCharts.Draw(chart, FinancialSamples.Options);
        var png = SvgConverter.ToPng(FinancialCharts.ToSvg(chart, FinancialSamples.Options));

        var (width, height) = PngSize(png);
        Assert.Equal(Math.Round(drawing.Width * 2), width, 1.0);
        Assert.Equal(Math.Round(drawing.Height * 2), height, 1.0);
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "review"));
        File.WriteAllBytes(Path.Combine(folder.FullName, name + ".png"), png);
        File.WriteAllText(Path.Combine(folder.FullName, name + ".svg"), FinancialCharts.ToSvg(chart, FinancialSamples.Options));
    }

    internal static (double Width, double Height) PngSize(byte[] png) =>
        ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19], (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
}
