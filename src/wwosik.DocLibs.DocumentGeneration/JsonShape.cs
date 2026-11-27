using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// Checks JSON against the part of JSON Schema that the schemas of structured input use: <c>type</c> (one or a list),
/// <c>properties</c>, <c>required</c>, <c>additionalProperties: false</c>, <c>enum</c>, <c>items</c>, <c>anyOf</c> and
/// <c>format: date</c>. Every problem comes with its JSON path, such as <c>$.sheets[0].rows[2][1]</c>; other keywords
/// (<c>description</c>, <c>default</c>) are ignored.
/// </summary>
internal static class JsonShape
{
    /// <summary>A value that does not have the shape the schema asks for.</summary>
    /// <param name="Path">Where it is: <c>$</c>, then <c>.name</c> per property and <c>[i]</c> per array item.</param>
    /// <param name="Message">What is wrong, in English, without the path.</param>
    public sealed record Problem(string Path, string Message);

    /// <summary>The problems of <paramref name="value"/> against <paramref name="schema"/>, at most <paramref name="max"/>, in document order.</summary>
    public static List<Problem> Check(JsonNode schema, JsonElement value, int max = 500)
    {
        var problems = new List<Problem>();
        Visit(schema, value, "$", problems, max);
        return problems;
    }

    /// <summary><paramref name="path"/> with the property <paramref name="name"/>: <c>.name</c>, or <c>['a b']</c> for a name that is not an identifier.</summary>
    public static string Property(string path, string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_')
            ? path + "." + name
            : path + "['" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "']";

    /// <summary><paramref name="path"/> with the array item <paramref name="index"/>: <c>[3]</c>.</summary>
    public static string Item(string path, int index) => string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");

    /// <summary>What a JSON value is, for a message: <c>a string</c>, <c>an array</c>, <c>null</c>.</summary>
    public static string Kind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null",
    };

    private static void Visit(JsonNode? schema, JsonElement value, string path, List<Problem> problems, int max)
    {
        if (problems.Count >= max || schema is null)
        {
            return;
        }

        if (schema is JsonValue flag && flag.TryGetValue<bool>(out var allowed))
        {
            if (!allowed)
            {
                Add(problems, max, path, "No value is allowed here.");
            }

            return;
        }

        if (schema is not JsonObject node)
        {
            return;
        }

        if (node["anyOf"] is JsonArray alternatives)
        {
            AnyOf(alternatives, value, path, problems, max);
            return;
        }

        if (node["type"] is { } type && !Matches(type, value))
        {
            Add(problems, max, path, $"Expected {Describe(node)}, not {Kind(value.ValueKind)}.");
            return;
        }

        if (node["enum"] is JsonArray values && !values.Any(v => Equal(v, value)))
        {
            var names = values.Where(v => v is not null).Select(v => v!.ToJsonString()).ToList();
            Add(problems, max, path, $"{value.GetRawText()} is not allowed here; give {Join(names, "or")}.");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String when node["format"]?.GetValue<string>() == "date":
                if (!DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    Add(problems, max, path, $"\"{value.GetString()}\" is not a date; write it as year-month-day, such as \"2026-03-15\".");
                }

                break;
            case JsonValueKind.Number when !value.TryGetDouble(out var number) || !double.IsFinite(number):
                Add(problems, max, path, $"{value.GetRawText()} is too large a number.");
                break;
            case JsonValueKind.Object:
                Object(node, value, path, problems, max);
                break;
            case JsonValueKind.Array when node["items"] is { } items:
                var i = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Visit(items, item, Item(path, i++), problems, max);
                }

                break;
        }
    }

    private static void Object(JsonObject node, JsonElement value, string path, List<Problem> problems, int max)
    {
        var properties = node["properties"] as JsonObject;
        var closed = node["additionalProperties"] is JsonValue extra && extra.TryGetValue<bool>(out var open) && !open;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var at = Property(path, property.Name);
            if (!seen.Add(property.Name))
            {
                Add(problems, max, at, $"The property '{property.Name}' is given twice.");
                continue;
            }

            if (properties is not null && properties.TryGetPropertyValue(property.Name, out var schema))
            {
                Visit(schema, property.Value, at, problems, max);
            }
            else if (closed)
            {
                var names = properties?.Select(p => p.Key).ToList() ?? [];
                var close = names.FirstOrDefault(n => string.Equals(n, property.Name, StringComparison.OrdinalIgnoreCase));
                Add(
                    problems,
                    max,
                    at,
                    close is not null
                        ? $"Unknown property '{property.Name}'. Did you mean '{close}'?"
                        : names.Count == 0
                            ? $"Unknown property '{property.Name}'. No properties are allowed here."
                            : $"Unknown property '{property.Name}'. The properties here are {Join([.. names.Select(n => "'" + n + "'")], "and")}.");
            }
        }

        if (node["required"] is JsonArray required)
        {
            foreach (var name in required.Select(r => r?.GetValue<string>()).OfType<string>())
            {
                if (!seen.Contains(name))
                {
                    Add(problems, max, Property(path, name), $"The property '{name}' is missing.");
                }
            }
        }
    }

    // Valid when one alternative fits; else the problems of the one alternative of the value's type, or what is expected.
    private static void AnyOf(JsonArray alternatives, JsonElement value, string path, List<Problem> problems, int max)
    {
        var ofKind = new List<JsonNode>();
        foreach (var alternative in alternatives.OfType<JsonNode>())
        {
            var found = new List<Problem>();
            Visit(alternative, value, path, found, max);
            if (found.Count == 0)
            {
                return;
            }

            if (alternative is JsonObject o && o["type"] is { } type && Matches(type, value))
            {
                ofKind.Add(alternative);
            }
        }

        if (ofKind.Count == 1)
        {
            Visit(ofKind[0], value, path, problems, max);
            return;
        }

        var expected = alternatives.OfType<JsonObject>().Select(Describe).Distinct().ToList();
        Add(problems, max, path, $"Expected {Join(expected, "or")}, not {Kind(value.ValueKind)}.");
    }

    // What a schema asks for, for a message: "a string", "an object with 'formula'".
    private static string Describe(JsonObject node)
    {
        var types = node["type"] switch
        {
            JsonArray list => [.. list.Select(t => t?.GetValue<string>()).OfType<string>()],
            JsonValue one when one.TryGetValue<string>(out var name) => new List<string> { name },
            _ => [],
        };
        if (types.Count == 0 && node["enum"] is JsonArray values)
        {
            return "one of " + Join([.. values.Where(v => v is not null).Select(v => v!.ToJsonString())], "or");
        }

        return Join([.. types.Select(t => t switch
        {
            "object" when node["required"] is JsonArray { Count: > 0 } required => "an object with " + Join([.. required.Select(r => "'" + r?.GetValue<string>() + "'")], "and"),
            "object" => "an object",
            "array" => "an array",
            "string" when node["format"]?.GetValue<string>() == "date" => "a date such as \"2026-03-15\"",
            "string" => "a string",
            "integer" => "a whole number",
            "number" => "a number",
            "boolean" => "true or false",
            "null" => "null",
            _ => t,
        })], "or");
    }

    private static bool Matches(JsonNode type, JsonElement value) => type switch
    {
        JsonArray list => list.Any(t => t is not null && Matches(t, value)),
        JsonValue one when one.TryGetValue<string>(out var name) => name switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        },
        _ => true,
    };

    private static bool Equal(JsonNode? expected, JsonElement value)
    {
        if (expected is null)
        {
            return value.ValueKind == JsonValueKind.Null;
        }

        if (expected is JsonValue v && v.TryGetValue<string>(out var text))
        {
            return value.ValueKind == JsonValueKind.String && value.GetString() == text;
        }

        using var document = JsonDocument.Parse(expected.ToJsonString());
        return JsonElement.DeepEquals(document.RootElement, value);
    }

    private static string Join(IReadOnlyList<string> items, string last) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " " + last + " " + items[^1];

    private static void Add(List<Problem> problems, int max, string path, string message)
    {
        if (problems.Count < max)
        {
            problems.Add(new Problem(path, message));
        }
    }
}
