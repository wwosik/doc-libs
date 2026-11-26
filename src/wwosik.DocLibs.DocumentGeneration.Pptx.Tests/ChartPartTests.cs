using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentGeneration.Charts;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>A deck's chart parts, compared with the committed ones: charts on sheet cells must not change them.</summary>
public class ChartPartTests
{
    /// <summary>Every chart kind, stacked and not, with a title, a unit, a source, an empty and a negative value.</summary>
    public const string Charts = """
        ## Bars

        ```chart
        type: bar
        title: Orders & returns
        ---
        month, plan, actual
        Jan, 120, 132
        Feb, 118, 121
        ```

        ## Stacked bars

        ```chart
        type: bar
        stacked: true
        unit: k
        source: Sales report
        ---
        region, north, south, east
        Q1, 1.5, 2, -0.5
        Q2, 1.25, , 3
        ```

        ## Lines

        ```chart
        type: line
        title: Trend
        ---
        week, visits
        W1, 10
        W2,
        W3, 14.75
        ```

        ## Areas

        ```chart
        type: area
        stacked: true
        ---
        year, a, b
        2024, 3, 4
        2025, 5, 6
        ```

        """;

    [Theory]
    [MemberData(nameof(Samples.BuiltInTemplates), MemberType = typeof(Samples))]
    public void ChartParts_AreAsBefore(string template)
    {
        var pptx = Samples.Generate(
            Samples.Markdown(Charts.ReplaceLineEndings("\n")),
            new PptxOptions { Template = Samples.BuiltIn(template), CodeBlockRenderers = [new ChartRenderer()] });

        using var package = Samples.Open(pptx);
        var parts = package.PresentationPart!.SlideParts.SelectMany(s => s.ChartParts).ToList();
        Assert.Equal(4, parts.Count);
        Samples.AssertMatchesExpected($"chart-parts-{template}", Dump(parts));
    }

    /// <summary>Each chart part's XML and its embedded sheet's XML, one per line.</summary>
    public static string Dump(IEnumerable<ChartPart> parts)
    {
        var text = new StringBuilder();
        foreach (var part in parts)
        {
            text.Append(Read(part)).Append('\n');
            using var workbook = SpreadsheetDocument.Open(part.EmbeddedPackagePart!.GetStream(), false);
            foreach (var sheet in workbook.WorkbookPart!.WorksheetParts)
            {
                text.Append(Read(sheet)).Append('\n');
            }
        }

        return text.ToString();
    }

    // The SDK's relationship ids are random: masked.
    private static string Read(OpenXmlPart part)
    {
        using var reader = new StreamReader(part.GetStream());
        return Regex.Replace(reader.ReadToEnd(), "r:id=\"R[0-9a-f]{16}\"", "r:id=\"R…\"");
    }
}
