using System.Text.Json;
using System.Text.Json.Serialization;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

// The JSON form of a workbook: the shape the schema is generated from and the JSON is read into, then turned into the
// model. Every property is nullable, so that a missing one is null, never a default the reader would not see.
#pragma warning disable SA1402 // The JSON form is one file.

/// <summary>The JSON form of <see cref="XlsxWorkbook"/>.</summary>
internal sealed class JsonWorkbook
{
    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public string? Description { get; set; }

    public string? Author { get; set; }

    public DateOnly? Date { get; set; }

    public string? Language { get; set; }

    public JsonMarking? Marking { get; set; }

    public string? Brand { get; set; }

    public required List<JsonSheet> Sheets { get; set; }
}

/// <summary>The JSON form of <see cref="XlsxMarking"/>.</summary>
internal sealed class JsonMarking
{
    [JsonConverter(typeof(CamelCaseEnum<Classification>))]
    public required Classification Classification { get; set; }

    public bool? Draft { get; set; }
}

/// <summary>The JSON form of <see cref="XlsxSheet"/>.</summary>
internal sealed class JsonSheet
{
    public required string Name { get; set; }

    public required List<JsonColumn> Columns { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? TotalsLabel { get; set; }

    public List<List<JsonElement>>? Rows { get; set; }

    public List<JsonChart>? Charts { get; set; }
}

/// <summary>The JSON form of <see cref="XlsxColumn"/>.</summary>
internal sealed class JsonColumn
{
    public required string Header { get; set; }

    [JsonConverter(typeof(CamelCaseEnum<XlsxColumnType>))]
    public XlsxColumnType? Type { get; set; }

    public string? Currency { get; set; }

    public int? Decimals { get; set; }

    public double? Width { get; set; }

    [JsonConverter(typeof(CamelCaseEnum<XlsxAlign>))]
    public XlsxAlign? Align { get; set; }

    [JsonConverter(typeof(CamelCaseEnum<XlsxTotal>))]
    public XlsxTotal? Total { get; set; }
}

/// <summary>The JSON form of <see cref="XlsxChart"/>; its <c>type</c> is <see cref="XlsxChart.Kind"/>.</summary>
internal sealed class JsonChart
{
    public required string Category { get; set; }

    public required List<string> Series { get; set; }

    [JsonConverter(typeof(CamelCaseEnum<XlsxChartKind>))]
    public XlsxChartKind? Type { get; set; }

    public bool? Stacked { get; set; }

    public string? Title { get; set; }

    public string? Unit { get; set; }

    public string? Source { get; set; }

    [JsonConverter(typeof(CamelCaseEnum<XlsxChartPlacement>))]
    public XlsxChartPlacement? Placement { get; set; }

    public string? SheetName { get; set; }
}

/// <summary>An enum as its member's name in camelCase: <c>"dateTime"</c>.</summary>
internal sealed class CamelCaseEnum<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    where TEnum : struct, Enum;

/// <summary>The source-generated type information of the JSON form, which works without reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(JsonWorkbook))]
internal sealed partial class XlsxJsonContext : JsonSerializerContext;
