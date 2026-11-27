using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

/// <summary>Workbooks as JSON: the schema, its descriptions, the check with JSON paths, and the structured generator.</summary>
public partial class JsonTests
{
    private const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    // ---- Schema ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Schema_MatchesExpected()
    {
        AssertMatchesExpected("workbook.schema.expected.json", XlsxWorkbookJson.Schema);
    }

    [Fact]
    public void Schema_NamesTheProperties_RequiresTheEssentials_AndForbidsOthers()
    {
        var schema = JsonNode.Parse(XlsxWorkbookJson.Schema)!.AsObject();
        var sheet = Items(schema["properties"]!["sheets"]!);
        var column = Items(sheet["properties"]!["columns"]!);
        var chart = Items(sheet["properties"]!["charts"]!);
        var marking = schema["properties"]!["marking"]!.AsObject();

        Assert.Equal(["title", "subtitle", "description", "author", "date", "language", "marking", "brand", "sheets"], Names(schema));
        Assert.Equal(["sheets"], Required(schema));
        Assert.Equal(["name", "columns", "title", "description", "totalsLabel", "rows", "charts"], Names(sheet));
        Assert.Equal(["name", "columns"], Required(sheet));
        Assert.Equal(["header", "type", "currency", "decimals", "width", "align", "total"], Names(column));
        Assert.Equal(["header"], Required(column));
        Assert.Equal(["category", "series", "type", "stacked", "title", "unit", "source", "placement", "sheetName"], Names(chart));
        Assert.Equal(["category", "series"], Required(chart));
        Assert.Equal(["classification", "draft"], Names(marking));
        Assert.Equal(
            ["text", "number", "integer", "currency", "percent", "date", "dateTime", "boolean"],
            column["properties"]!["type"]!["enum"]!.AsArray().OfType<JsonNode>().Select(n => n.GetValue<string>()));
        Assert.Equal("date", schema["properties"]!["date"]!["format"]!.GetValue<string>());

        // Every object is closed, and every property has the model's description.
        Assert.All(Objects(schema), o => Assert.False(o["additionalProperties"]!.GetValue<bool>()));
        Assert.All(Objects(schema).SelectMany(o => o["properties"]!.AsObject()), p => Assert.False(string.IsNullOrWhiteSpace(p.Value!["description"]?.GetValue<string>()), p.Key));
        Assert.StartsWith("A workbook for", schema["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("\"dateTime\": A date and time", column["properties"]!["type"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptions_ComeFromTheModelsDocumentation()
    {
        var docs = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "wwosik.DocLibs.DocumentGeneration.Xlsx.xml")).Descendants("member")
            .Concat(XDocument.Load(Path.Combine(AppContext.BaseDirectory, "wwosik.DocLibs.DocumentGeneration.xml")).Descendants("member"))
            .GroupBy(m => (string)m.Attribute("name")!).ToDictionary(g => g.Key, g => g.First());
        var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string member) => texts[key] = Text(docs[member].Element("summary")!);

        (Type Json, Type Model)[] types =
        [
            (typeof(JsonWorkbook), typeof(XlsxWorkbook)), (typeof(JsonMarking), typeof(XlsxMarking)), (typeof(JsonSheet), typeof(XlsxSheet)),
            (typeof(JsonColumn), typeof(XlsxColumn)), (typeof(JsonChart), typeof(XlsxChart)),
        ];
        foreach (var (json, model) in types)
        {
            Add(model.Name, "T:" + model.FullName);
            foreach (var property in json.GetProperties())
            {
                var member = json == typeof(JsonChart) && property.Name == "Type" ? "Kind" : property.Name;
                Add(model.Name + "." + member, $"P:{model.FullName}.{member}");
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type.IsEnum)
                {
                    foreach (var name in Enum.GetNames(type))
                    {
                        Add(type.Name + "." + name, $"F:{type.FullName}.{name}");
                    }
                }
            }
        }

        var code = new StringBuilder();
        code.Append("namespace wwosik.DocLibs.DocumentGeneration.Xlsx;\n\n");
        code.Append("/// <summary>Generated from the model's XML documentation by the tests (DOCLIBS_UPDATE_EXPECTED=1); do not edit.</summary>\n");
        code.Append("internal static class XlsxJsonDescriptions\n{\n");
        code.Append("    public static IReadOnlyDictionary<string, string> Texts { get; } = new Dictionary<string, string>(StringComparer.Ordinal)\n    {\n");
        foreach (var (key, text) in texts)
        {
            code.Append("        [\"").Append(key).Append("\"] = @\"").Append(text.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\",\n");
        }

        code.Append("    };\n}\n");
        var path = Path.Combine(SourceDirectory(), "..", "wwosik.DocLibs.DocumentGeneration.Xlsx", "XlsxJsonDescriptions.cs");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, code.ToString());
            return;
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), code.ToString());
    }

    // ---- Shape ----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSample_HasTheSchemasShape_AndNoProblems()
    {
        using var sample = JsonDocument.Parse(Sample());

        Assert.Empty(JsonShape.Check(JsonNode.Parse(XlsxWorkbookJson.Schema)!, sample.RootElement));
        Assert.Empty(XlsxWorkbookJson.Check(Sample(), Samples.Options));
    }

    [Fact]
    public void ExtraProperties_AndWrongCellTypes_AreJsonInvalid_WithTheirPaths()
    {
        var problems = XlsxWorkbookJson.Check("""
            {
              "titel": "Figures",
              "sheets": [
                {
                  "name": "A",
                  "columns": [{ "header": "X", "colour": "red" }, { "header": "Y", "type": "money" }],
                  "rows": [["a", 1], [[1], { "formula": 2 }], [{ "value": 1 }]]
                },
                { "columns": [], "charts": [{ "category": "X", "series": "Y" }] }
              ]
            }
            """);

        Assert.All(problems, p => Assert.Equal((NoteCodes.JsonInvalid, true), (p.Code, p.IsError)));
        Assert.Equal(
            [
                "$.titel: Unknown property 'titel'. The properties here are 'title', 'subtitle', 'description', 'author', 'date', 'language', 'marking', 'brand' and 'sheets'.",
                "$.sheets[0].columns[0].colour: Unknown property 'colour'. The properties here are 'header', 'type', 'currency', 'decimals', 'width', 'align' and 'total'.",
                "$.sheets[0].columns[1].type: \"money\" is not allowed here; give \"text\", \"number\", \"integer\", \"currency\", \"percent\", \"date\", \"dateTime\" or \"boolean\".",
                "$.sheets[0].rows[1][0]: Expected a number, a string, true or false, null or an object with 'formula', not an array.",
                "$.sheets[0].rows[1][1].formula: Expected a string, not a number.",
                "$.sheets[0].rows[2][0].value: Unknown property 'value'. The properties here are 'formula'.",
                "$.sheets[0].rows[2][0].formula: The property 'formula' is missing.",
                "$.sheets[1].charts[0].series: Expected an array, not a string.",
                "$.sheets[1].name: The property 'name' is missing.",
            ],
            problems.Select(p => p.ToString()));
    }

    [Theory]
    [InlineData("[]", "$: Expected an object with 'sheets', not an array.")]
    [InlineData("{}", "$.sheets: The property 'sheets' is missing.")]
    [InlineData("{\"sheets\": [], \"date\": \"31.03.2026\"}", "$.date: \"31.03.2026\" is not a date; write it as year-month-day, such as \"2026-03-15\".")]
    [InlineData("{\"sheets\": [{\"name\": \"A\", \"columns\": [{\"header\": \"X\", \"decimals\": 1.5}]}]}", "$.sheets[0].columns[0].decimals: Expected a whole number or null, not a number.")]
    [InlineData("{\"sheets\": [], \"sheets\": []}", "$.sheets: The property 'sheets' is given twice.")]
    [InlineData("{\"sheets\": [], \"marking\": {\"classification\": \"secret\"}}", "$.marking.classification: \"secret\" is not allowed here; give \"public\", \"internal\" or \"confidential\".")]
    [InlineData("{\"sheets\": [{\"name\": \"A\", \"columns\": [{\"header\": \"D\", \"type\": \"date\"}], \"rows\": [[\"15.01.2026\"]]}]}", "$.sheets[0].rows[0][0]: \"15.01.2026\" is in a date column, which takes dates as year-month-day, such as \"2026-03-15\".")]
    [InlineData("{\"sheets\": [{\"name\": \"A\", \"columns\": [{\"header\": \"F\"}], \"rows\": [[{\"formula\": \" \"}]]}]}", "$.sheets[0].rows[0][0].formula: The formula is empty. Give a formula, or null for an empty cell.")]
    [InlineData("{\"sheets\": [], \"brand\": \"acme\"}", "$.brand: The workbook names the brand 'acme'")]
    [InlineData("{\"sheets\": [], \"language\": \"not a tag\"}", "$.language: ")]
    [InlineData("{\"sheets\": [", "$: The text is not valid JSON")]
    public void ShapeProblems_NameTheirPath(string json, string expected)
    {
        var problem = Assert.Single(XlsxWorkbookJson.Check(json, Samples.Options));

        Assert.Equal((NoteCodes.JsonInvalid, true), (problem.Code, problem.IsError));
        Assert.StartsWith(expected, problem.ToString(), StringComparison.Ordinal);
    }

    // ---- The model's check, at JSON paths -------------------------------------------------------------------------

    [Fact]
    public void TheModelsProblems_ComeAtTheirJsonPaths_NotesLast()
    {
        var problems = XlsxWorkbookJson.Check("""
            {
              "sheets": [
                {
                  "name": "Sales",
                  "columns": [{ "header": "Item" }, { "header": "Price", "type": "number", "currency": "EUR" }, { "header": "Total" }],
                  "rows": [["Pens", 1.5, { "formula": "[Price]*[Amount]" }], ["Pads", "cheap", { "formula": "FOO([Price])" }], ["Ink", 2, null, "extra"]],
                  "charts": [{ "category": "Item", "series": ["Item"] }, { "category": "Item", "series": ["Price"], "placement": "sheet", "sheetName": "Sales" }]
                },
                { "name": "sales", "columns": [{ "header": "A" }] }
              ]
            }
            """, Samples.Options);

        Assert.Equal(
            [
                ("$.sheets[1].name", NoteCodes.SheetName, true),
                ("$.sheets[0].columns[1].currency", NoteCodes.ColumnInvalid, true),
                ("$.sheets[0].rows[0][2].formula", NoteCodes.FormulaInvalid, true),
                ("$.sheets[0].rows[1][1]", NoteCodes.CellType, true),
                ("$.sheets[0].rows[2][3]", NoteCodes.RowTooWide, true),
                ("$.sheets[0].charts[0].series[0]", NoteCodes.ChartInvalid, true),
                ("$.sheets[0].charts[1].sheetName", NoteCodes.ChartInvalid, true),
                ("$.sheets[0].rows[1][2].formula", NoteCodes.FunctionUnknown, false),
            ],
            problems.Select(p => (p.Path, p.Code, p.IsError)));
        Assert.Contains("'cheap'", problems[3].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_GivesTheModel_OrThrowsWithEveryProblem()
    {
        var workbook = XlsxWorkbookJson.Read(Sample(), Samples.Options);
        Assert.Equal(["Sales", "Orders", "Months"], workbook.Sheets.Select(s => s.Name));
        Assert.Equal(XlsxCell.FromDate(new DateOnly(2024, 1, 1)), workbook.Sheets[0].Rows[0][5]);
        Assert.Equal(XlsxCell.FromDateTime(new DateTime(2026, 3, 2, 9, 5, 0)), workbook.Sheets[0].Rows[1][6]);
        Assert.Equal(XlsxCell.Formula("[Price]*[Quantity]"), workbook.Sheets[1].Rows[1][3]);
        Assert.Equal(XlsxChartKind.Bar, workbook.Sheets[1].Charts[0].Kind);

        var rows = string.Join(", ", Enumerable.Range(0, 12).Select(i => $"[\"x{i}\"]"));
        var error = Assert.Throws<DocumentConversionException>(() => XlsxWorkbookJson.Read(
            $"{{\"sheets\": [{{\"name\": \"A\", \"columns\": [{{\"header\": \"N\", \"type\": \"number\"}}], \"rows\": [{rows}]}}]}}"));
        Assert.StartsWith("The workbook has 12 problems:\n- $.sheets[0].rows[0][0]: ", error.Reason, StringComparison.Ordinal);
        Assert.Contains("\n- $.sheets[0].rows[11][0]: ", error.Reason, StringComparison.Ordinal);
    }

    // ---- Generating -----------------------------------------------------------------------------------------------

    [Fact]
    public void TheJsonSample_AndTheMarkdownSample_GiveTheSameBytes()
    {
        var generator = (IStructuredGenerator)XlsxGenerator.ForOptions(Samples.Options);
        using var json = JsonDocument.Parse(Sample());
        using var output = new MemoryStream();
        var result = generator.Generate(json.RootElement, [], output);
        var (markdown, _) = MarkdownTests.Generate(MarkdownTests.Sample());

        Samples.AssertValid(output.ToArray());
        Assert.Empty(result.Notes);
        Assert.Equal(5, result.Pages);
        Assert.Equal(markdown, output.ToArray());
        Samples.WriteForReview("json-features.xlsx", output.ToArray());
    }

    [Fact]
    public void TheStructuredGenerator_ChecksWithPaths_AndThrowsWithThem()
    {
        var generator = (IStructuredGenerator)XlsxGenerator.ForOptions(Samples.Options);
        using var json = JsonDocument.Parse("{\"sheets\": [{\"name\": \"A\", \"columns\": [{\"header\": \"N\", \"type\": \"number\"}], \"rows\": [[\"x\"]]}]}");

        Assert.Equal(("xlsx", "workbook"), (generator.Format, generator.ParameterName));
        Assert.Equal(XlsxWorkbookJson.Schema, generator.ParameterSchema);
        var check = generator.Check(json.RootElement);
        var note = Assert.Single(check.Notes);
        Assert.Equal(NoteCodes.CellType, note.Code);
        Assert.StartsWith("$.sheets[0].rows[0][0]: Sheet 'A', row 1", note.Message, StringComparison.Ordinal);
        Assert.Equal(1, check.Statistics.Tables);
        Assert.StartsWith("The workbook has a problem: $.sheets[0].rows[0][0]: ", check.Error, StringComparison.Ordinal);
        var error = Assert.Throws<DocumentConversionException>(() => generator.Generate(json.RootElement, [], Stream.Null));
        Assert.Equal(check.Error, error.Reason);
        Assert.StartsWith("## Workbooks as JSON\n", generator.StructuredGuide(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuidesExample_GeneratesWithoutProblems()
    {
        var example = ExamplePattern().Match(XlsxWorkbookJson.Guide()).Groups["body"].Value;

        Assert.Empty(XlsxWorkbookJson.Check(example, Samples.Options));
        using var json = JsonDocument.Parse(example);
        using var output = new MemoryStream();
        ((IStructuredGenerator)XlsxGenerator.ForOptions(Samples.Options)).Generate(json.RootElement, [], output);
        Samples.AssertValid(output.ToArray());
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    private static string Sample() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "features.json"));

    private static JsonObject Items(JsonNode array) => array["items"]!.AsObject();

    private static List<string> Names(JsonObject node) => [.. node["properties"]!.AsObject().Select(p => p.Key)];

    private static List<string> Required(JsonObject node) => [.. node["required"]!.AsArray().Select(n => n!.GetValue<string>())];

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
    {
        if (node is JsonObject o)
        {
            if (o["properties"] is not null)
            {
                yield return o;
            }

            foreach (var child in o.SelectMany(p => Objects(p.Value)))
            {
                yield return child;
            }
        }
        else if (node is JsonArray a)
        {
            foreach (var child in a.SelectMany(Objects))
            {
                yield return child;
            }
        }
    }

    // A summary as plain text: <c> in backticks, the model's properties by their JSON names, enum members as JSON strings.
    private static string Text(XElement summary)
    {
        var text = new StringBuilder();
        foreach (var node in summary.Nodes())
        {
            text.Append(node switch
            {
                XText t => t.Value,
                XElement { Name.LocalName: "c" } c => "`" + c.Value + "`",
                XElement { Name.LocalName: "see" } see when see.Attribute("langword") is { } word => word.Value,
                XElement { Name.LocalName: "see" } see => Reference((string)see.Attribute("cref")!),
                XElement { Name.LocalName: "paramref" } p => (string)p.Attribute("name")!,
                XElement e => e.Value,
                _ => string.Empty,
            });
        }

        return WhiteSpace().Replace(text.ToString(), " ").Trim();
    }

    private static string Reference(string cref)
    {
        var name = cref[2..];
        var paren = name.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            name = name[..paren];
        }

        var parts = name.Split('.');
        var (type, member) = (parts[^2], parts[^1]);
        string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
        return cref[0] switch
        {
            'F' when type.StartsWith("Xlsx", StringComparison.Ordinal) || type == "Classification" => "\"" + Camel(member) + "\"",
            'P' when type is "XlsxWorkbook" or "XlsxSheet" or "XlsxColumn" or "XlsxChart" or "XlsxMarking" => "`" + (type == "XlsxChart" && member == "Kind" ? "type" : Camel(member)) + "`",
            'T' => member,
            _ => type + "." + member,
        };
    }

    private static string SourceDirectory([CallerFilePath] string caller = "") => Path.GetDirectoryName(caller)!;

    private static void AssertMatchesExpected(string name, string actual)
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(SourceDirectory(), "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name)).ReplaceLineEndings("\n"), actual);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex("```json\\n(?<body>.*?)\\n```", RegexOptions.Singleline)]
    private static partial Regex ExamplePattern();
}
