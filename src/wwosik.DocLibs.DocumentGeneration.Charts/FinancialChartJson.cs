using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// Reads a <c>financial-chart</c> code block: a JSON object with the chart's <c>type</c> and its properties in camel
/// case, as the C# records name them, and an optional <c>options</c> object (culture, scale, unit, labels,
/// tolerance, restShare, width). Unknown properties are errors, so a misspelt one is never silently ignored.
/// </summary>
internal static class FinancialChartJson
{
    public static (FinancialChart Chart, FinancialChartOptions Options) Parse(string code, int line, Brand brand)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(code, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                ?? throw Error(line, "The block is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw Error(line, $"The block is not valid JSON: {ex.Message}");
        }

        if (root["type"] is not JsonValue type || type.GetValueKind() != JsonValueKind.String)
        {
            throw Error(line, "The block needs a \"type\": development, growth-by-row, actual-vs-reference, growth-trend, two-year or summary-table.");
        }

        var settings = root["options"];
        root.Remove("options");
        try
        {
            var chart = root.Deserialize(FinancialChartContext.Default.FinancialChart)
                ?? throw Error(line, "The block holds no chart.");
            var options = new FinancialChartOptions { Brand = brand };
            if (settings is not null)
            {
                var read = settings.Deserialize(FinancialChartContext.Default.JsonOptions) ?? new JsonOptions();
                options = options with
                {
                    Culture = read.Culture is { } culture ? Culture(culture, line) : options.Culture,
                    Scale = read.Scale ?? options.Scale,
                    Unit = read.Unit ?? options.Unit,
                    Labels = read.Labels ?? options.Labels,
                    Tolerance = read.Tolerance ?? options.Tolerance,
                    RestShare = read.RestShare ?? options.RestShare,
                    Width = read.Width ?? options.Width,
                };
            }

            return (chart, options);
        }
        catch (JsonException ex)
        {
            throw Error(line, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            throw Error(line, ex.Message);
        }
    }

    public static ArgumentException Error(int line, string reason) =>
        new(line > 0 ? $"The financial-chart code block at line {line} cannot be drawn: {reason}" : $"The financial-chart code block cannot be drawn: {reason}");

    private static CultureInfo Culture(string name, int line)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            throw Error(line, $"'{name}' is not a culture; use a name like 'en-US' or 'pl-PL'.");
        }
    }

    internal sealed record JsonOptions(
        string? Culture = null,
        double? Scale = null,
        string? Unit = null,
        FinancialChartLabels? Labels = null,
        double? Tolerance = null,
        double? RestShare = null,
        double? Width = null);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowOutOfOrderMetadataProperties = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(FinancialChart))]
[JsonSerializable(typeof(FinancialChartJson.JsonOptions))]
internal sealed partial class FinancialChartContext : JsonSerializerContext;
