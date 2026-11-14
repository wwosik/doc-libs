using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary>The embedded Tabler outline icons, <c>:icon-name:</c> in runs, and the import tool.</summary>
public partial class IconTests
{
    // The budget of question 2 of E04-S06, held against every consumer of the shared package.
    private const long Budget = 1_000_000;

    private const string Resource = "Icons.tabler-outline.txt.gz";

    private static string RepoRoot([CallerFilePath] string caller = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, "..", ".."));

    [Fact]
    public void TheEmbeddedIcons_StayWithinTheSizeBudget()
    {
        using var stream = typeof(TablerIcons).Assembly.GetManifestResourceStream(Resource);

        Assert.NotNull(stream);
        Assert.True(stream.Length <= Budget, $"The embedded icon data is {stream.Length:N0} bytes compressed, over the budget of {Budget:N0} bytes. Cut the set to a curated list, and ask the human first.");
    }

    [Fact]
    public void TheEmbeddedIcons_AreAsManyAsTheDocsPageSays()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "wwosik.DocLibs.DocumentGeneration.md"));
        var recorded = int.Parse(CountPattern().Match(page) is { Success: true } match ? match.Groups[1].Value : "0", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(recorded > 5000, "the docs page records the icon count as 'The set holds N icons'");
        Assert.Equal(recorded, TablerIcons.Outline.Names.Count);
    }

    [Fact]
    public void AnIcon_IsA24PixelDrawingOfRoundStrokedLines()
    {
        var check = TablerIcons.Outline.Find("check");

        Assert.NotNull(check);
        Assert.Equal((24.0, 24.0), (check.Width, check.Height));
        var shape = Assert.IsType<DrawingShape>(Assert.Single(check.Items));
        Assert.Null(shape.Fill);
        Assert.NotNull(shape.Stroke);
        Assert.Equal(2, shape.StrokeWidth);
        Assert.True(shape.RoundJoins);
        Assert.Equal<PathSegment>([new MoveTo(5, 12), new LineTo(10, 17), new LineTo(20, 7)], shape.Path.Segments);
        check.Validate();
        Assert.Same(check, TablerIcons.Outline.Find("check"));
    }

    [Fact]
    public void ACircle_IsMadeOfCubics()
    {
        var icon = TablerIcons.Outline.Find("circle")!;

        var segments = ((DrawingShape)icon.Items[0]).Path.Segments;
        Assert.IsType<MoveTo>(segments[0]);
        Assert.True(segments.Skip(1).All(s => s is CubicTo or ClosePath));
        var bounds = ((DrawingShape)icon.Items[0]).Path.Bounds;
        Assert.Equal(3, bounds.X, 1);
        Assert.Equal(18, bounds.Width, 1);
    }

    [Fact]
    public void AnUnknownName_IsNotFound()
    {
        Assert.Null(TablerIcons.Outline.Find("no-such-icon"));
        Assert.Contains("circle-check", TablerIcons.Outline.Names);
    }

    [Fact]
    public void EveryIcon_Parses()
    {
        foreach (var name in TablerIcons.Outline.Names)
        {
            var icon = TablerIcons.Outline.Find(name)!;
            icon.Validate();
            Assert.NotEmpty(((DrawingShape)icon.Items[0]).Path.Segments);
        }
    }

    [Fact]
    public void PathData_TakesNumbersWithoutLeadingZeros_AndNegativesWithoutSpaces()
    {
        var path = IconPaths.Parse("M.5-1.25L3 4C1 2 3 4 5 6Z");

        Assert.Equal<PathSegment>([new MoveTo(0.5, -1.25), new LineTo(3, 4), new CubicTo(1, 2, 3, 4, 5, 6), new ClosePath()], path.Segments);
        Assert.Throws<FormatException>(() => IconPaths.Parse("M1 2Q3 4"));
    }

    [Fact]
    public void UnknownIcon_SuggestsCloseNames()
    {
        var error = IconSyntax.Unknown("chek", TablerIcons.Outline, 3, DocumentFormat.Pptx);

        Assert.Contains("Unknown icon 'chek' at line 3. Did you mean check, checks, chess?", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, error.Message.Split("Did you mean ")[1].Split(", ").Length);
    }

    [Fact]
    public void Apply_TakesIconsOutOfRuns_ButNotOutOfCode()
    {
        (string Text, bool Code, InlineIcon? Icon)[] runs = [("Done :icon-check: and :icon-x:", false, null), (":icon-check:", true, null)];

        var result = IconSyntax.Apply(runs, r => r.Text, r => r.Code, (r, t) => r with { Text = t }, (r, i) => r with { Icon = i }, TablerIcons.Outline, 7, DocumentFormat.Pdf);

        Assert.Equal(["Done ", "", " and ", "", ":icon-check:"], result.Select(r => r.Text));
        Assert.Equal(["check", "x"], result.Where(r => r.Icon is not null).Select(r => r.Icon!.Name));
        Assert.All(result.Where(r => r.Icon is not null), r => Assert.Equal(7, r.Icon!.Line));
    }

    [Fact]
    public void Apply_WithoutASet_LeavesTheText_AndChecksNothing()
    {
        (string Text, InlineIcon? Icon)[] runs = [(":icon-chek:", null)];

        var result = IconSyntax.Apply(runs, r => r.Text, _ => false, (r, t) => r with { Text = t }, (r, i) => r with { Icon = i }, null, 1, DocumentFormat.Pdf);

        Assert.Equal(runs, result);
    }

    [Fact]
    public void Apply_ThrowsOnAnUnknownName()
    {
        (string Text, InlineIcon? Icon)[] runs = [("x :icon-chek: y", null)];

        var error = Assert.Throws<DocumentConversionException>(() =>
            IconSyntax.Apply(runs, r => r.Text, _ => false, (r, t) => r with { Text = t }, (r, i) => r with { Icon = i }, TablerIcons.Outline, 1, DocumentFormat.Pdf));
        Assert.Contains("Did you mean check", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Colour_ReplacesEveryStrokeAndFill()
    {
        var icon = new Drawing(24, 24, [new DrawingShape(DrawingPath.Rectangle(0, 0, 4, 4), "112233", null, 0), new DrawingShape(DrawingPath.Rectangle(0, 0, 4, 4), null, "445566", 2)]);

        var coloured = IconSyntax.Colour(icon, "ABCDEF");

        Assert.Equal(("ABCDEF", (string?)null), (((DrawingShape)coloured.Items[0]).Fill, ((DrawingShape)coloured.Items[0]).Stroke));
        Assert.Equal(((string?)null, "ABCDEF"), (((DrawingShape)coloured.Items[1]).Fill, ((DrawingShape)coloured.Items[1]).Stroke));
    }

    /// <summary>Runs the import tool on two SVGs of its own: a circle element and a path with arcs and relative commands.</summary>
    [Fact]
    public async Task TheImportTool_TurnsCirclesAndArcsIntoAbsolutePaths()
    {
        var work = Path.Combine(RepoRoot(), ".tmp", "icon-import-test", Guid.NewGuid().ToString("N"));
        var svgs = Path.Combine(work, "svg");
        Directory.CreateDirectory(svgs);
        const string Head = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">"
            + "<path stroke=\"none\" d=\"M0 0h24v24H0z\" fill=\"none\"/>";
        File.WriteAllText(Path.Combine(svgs, "dot.svg"), Head + "<circle cx=\"12\" cy=\"12\" r=\"9\"/></svg>");
        File.WriteAllText(Path.Combine(svgs, "half.svg"), Head + "<path d=\"M3 12a9 9 0 0 0 18 0h-2.5v.5\"/></svg>");
        var output = Path.Combine(work, "icons.txt.gz");
        try
        {
            var start = new ProcessStartInfo("dotnet", ["run", Path.Combine(RepoRoot(), "tools", "icon-import", "icon-import.cs"), "--", "--source", svgs, "--out", output])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepoRoot(),
            };

            // The test host's MSBuild settings would confuse the tool's own build.
            foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                start.Environment.Remove(name);
            }

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
            var (outText, errText) = (await stdout, await stderr);
            Assert.True(process.ExitCode == 0, outText + errText);
            Assert.Contains("2 icons", outText, StringComparison.Ordinal);

            using var gzip = new GZipStream(File.OpenRead(output), CompressionMode.Decompress);
            var lines = new StreamReader(gzip).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('\t'))
                .ToDictionary(l => l[0], l => l[1]);

            // The circle: from its rightmost point, four quarter cubics, closed.
            Assert.Equal("M21 12C21 16.97 16.97 21 12 21C7.03 21 3 16.97 3 12C3 7.03 7.03 3 12 3C16.97 3 21 7.03 21 12Z", lines["dot"]);

            // The arc (half a circle below the centre, counter-clockwise in SVG's y-down sense) in two quarter cubics,
            // then the relative h and v as absolute lines; the frame path with stroke="none" is left out.
            Assert.Equal("M3 12C3 16.97 7.03 21 12 21C16.97 21 21 16.97 21 12L18.5 12L18.5 12.5", lines["half"]);
            var path = IconPaths.Parse(lines["half"]);
            Assert.Equal(5, path.Segments.Count);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [GeneratedRegex(@"The set holds (\d+) icons")]
    private static partial Regex CountPattern();
}
