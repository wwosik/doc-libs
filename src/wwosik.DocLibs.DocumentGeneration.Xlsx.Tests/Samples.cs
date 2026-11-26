using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

internal static class Samples
{
    /// <summary>The made-up brand of <c>Samples/brand-sample</c>, with fonts and a logo.</summary>
    public static readonly Brand Brand = BrandPack.Load(Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample"));

    public static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 3, 9, 10, 30, 0, TimeSpan.Zero));

    public static XlsxOptions Options { get; } = new() { TimeProvider = Clock };

    /// <summary>Three sheets with every column type, a title and a description.</summary>
    public static XlsxWorkbook EveryType() => new()
    {
        Title = "Quarterly figures",
        Subtitle = "First quarter 2026",
        Description = "Sales, stock and staff of the first quarter, with every kind of column.",
        Author = "DocLibs",
        Sheets =
        [
            new XlsxSheet
            {
                Name = "Sales",
                Title = "Sales by region",
                Description = "Net, in euros",
                Columns =
                [
                    new XlsxColumn("Region"),
                    new XlsxColumn("Units", XlsxColumnType.Integer),
                    new XlsxColumn("Revenue", XlsxColumnType.Currency) { Currency = "EUR" },
                    new XlsxColumn("Revenue (USD)", XlsxColumnType.Currency) { Currency = "USD" },
                    new XlsxColumn("Share", XlsxColumnType.Percent) { Decimals = 1 },
                    new XlsxColumn("Factor", XlsxColumnType.Number) { Decimals = 3 },
                ],
                Rows =
                [
                    ["North", 1200, 15000.5m, 16200.25, 0.25, 1.234],
                    ["South", 800, 9800m, 10600, 0.18, 0.987],
                    ["West", 3100, 41000.75m, 44300, 0.57, 1.5],
                ],
            },
            new XlsxSheet
            {
                Name = "Stock",
                Columns =
                [
                    new XlsxColumn("Article"),
                    new XlsxColumn("Delivered", XlsxColumnType.Date),
                    new XlsxColumn("Counted", XlsxColumnType.DateTime),
                    new XlsxColumn("In stock", XlsxColumnType.Boolean),
                ],
                Rows =
                [
                    ["01234", new DateOnly(2026, 1, 15), new DateTime(2026, 3, 1, 14, 30, 0), true],
                    ["A-77", new DateOnly(2026, 2, 3), new DateTime(2026, 3, 2, 9, 5, 0), false],
                    ["B&C <x>"],
                ],
            },
            new XlsxSheet
            {
                Name = "Staff",
                Title = "Staff",
                Columns = [new XlsxColumn("Name"), new XlsxColumn("Since", XlsxColumnType.Date), new XlsxColumn("Hours", XlsxColumnType.Number) { Align = XlsxAlign.Center }],
                Rows = [["Ada", new DateOnly(2020, 5, 1), 38.5], ["Grace", new DateOnly(2019, 9, 16), 40]],
            },
        ],
    };

    public static byte[] Generate(XlsxWorkbook workbook, XlsxOptions? options = null)
    {
        using var output = new MemoryStream();
        XlsxGenerator.Generate(workbook, output, options ?? Options);
        return output.ToArray();
    }

    public static SpreadsheetDocument Open(byte[] xlsx) => SpreadsheetDocument.Open(new MemoryStream(xlsx), false);

    public static void AssertValid(byte[] xlsx)
    {
        using var package = Open(xlsx);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.Empty(errors);
    }

    /// <summary>The XML of the part at <paramref name="path"/>, such as <c>xl/styles.xml</c>.</summary>
    public static string Part(byte[] xlsx, string path)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(xlsx));
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<string> Entries(byte[] xlsx)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(xlsx));
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    /// <summary>Writes a generated file to <c>.tmp/review/</c> at the repo root, for a human to open.</summary>
    public static void WriteForReview(string name, byte[] content)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CHANGELOG.md")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return;
        }

        var review = Path.Combine(directory.FullName, ".tmp", "review");
        Directory.CreateDirectory(review);
        File.WriteAllBytes(Path.Combine(review, name), content);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
