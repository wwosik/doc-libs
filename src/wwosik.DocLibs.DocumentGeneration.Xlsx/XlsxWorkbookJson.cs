using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// A workbook as JSON: the same workbook as an <see cref="XlsxWorkbook"/>, as data that follows <see cref="Schema"/>.
/// Names are camelCase, values of a fixed set (column types, totals, chart types) camelCase strings, and a property the
/// schema does not name is an error. A cell is a number (a percentage as a share, <c>0.12</c>), a string (in date and
/// date-time columns an ISO date, <c>"2026-03-15"</c>, or date and time, <c>"2026-03-15T14:30:00"</c>), a boolean,
/// <c>null</c> for an empty cell, or <c>{"formula": "[Price]*[Quantity]"}</c>. Every problem comes with its JSON path.
/// </summary>
public static class XlsxWorkbookJson
{
    private const string CellDescription =
        "One value of a row, in the order of the columns: a number (a percentage as a share, 0.12 for 12 %), a string (in a date column an ISO date such as \"2026-03-15\", in a dateTime column \"2026-03-15T14:30:00\"), true or false, null for an empty cell, or {\"formula\": \"[Price]*[Quantity]\"}.";

    private const string FormulaDescription =
        "A formula in Excel's English form, with or without the leading '=': [Header] is the cell of the column with that header in the same row, {row} the row's number; A1 references are kept as written. Excel computes it on opening.";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

    private static readonly string[] DateTimeFormats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd"];

    private static readonly Lazy<JsonObject> SchemaNode = new(Build);

    private static readonly Lazy<string> SchemaText = new(() => Write(SchemaNode.Value));

    /// <summary>
    /// The JSON Schema of the JSON form, as indented JSON: generated from the model, with the descriptions of its
    /// documentation. It uses <c>type</c>, <c>properties</c>, <c>required</c>, <c>additionalProperties</c>,
    /// <c>enum</c>, <c>items</c>, <c>anyOf</c>, <c>format</c> and <c>description</c>.
    /// </summary>
    public static string Schema => SchemaText.Value;

    /// <summary>
    /// Reads the workbook in <paramref name="json"/>, checked as <see cref="Check(string, XlsxOptions?)"/> checks it.
    /// </summary>
    /// <exception cref="DocumentConversionException">The JSON has errors: the message lists every one with its JSON path.</exception>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static XlsxWorkbook Read(string json, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        var read = Read(json, options ?? XlsxOptions.Default, measure: false);
        if (read.Problems.Exists(p => p.IsError))
        {
            throw Failure(read.Problems);
        }

        return read.Workbook!;
    }

    /// <summary>
    /// Checks the workbook in <paramref name="json"/> without writing it and returns every problem with its JSON path:
    /// first what does not have the schema's shape (<c>json-invalid</c>); when the shape is right, the problems of
    /// <see cref="XlsxGenerator.Check(XlsxWorkbook, XlsxOptions?)"/>, errors first, then its notes (not errors). At most
    /// 500.
    /// </summary>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static IReadOnlyList<XlsxJsonProblem> Check(string json, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Read(json, options ?? XlsxOptions.Default, measure: false).Problems;
    }

    /// <summary>The structured generator's check: problems as notes <c>$.path: message</c>, with the plan's statistics.</summary>
    internal static CheckResult Check(JsonElement input, XlsxOptions options)
    {
        var read = Read(input, options, measure: false);
        return new CheckResult(
            [.. read.Problems.Select(p => new GenerationNote(p.Code, 0, p.ToString()))],
            read.Plan?.Statistics() ?? new CheckStatistics())
        {
            Error = read.Problems.Exists(p => p.IsError) ? Failure(read.Problems).Reason : null,
        };
    }

    /// <summary>The structured generator's generate: the notes as <c>$.path: message</c>.</summary>
    /// <exception cref="DocumentConversionException">The JSON has errors, each with its JSON path.</exception>
    internal static GenerationResult Generate(JsonElement input, Stream output, XlsxOptions options)
    {
        var read = Read(input, options, measure: true);
        if (read.Problems.Exists(p => p.IsError))
        {
            throw Failure(read.Problems);
        }

        XlsxWriter.Write(read.Plan!, output);
        return new GenerationResult([.. read.Problems.Select(p => new GenerationNote(p.Code, 0, p.ToString()))])
        {
            Pages = read.Plan!.Statistics().Sheets,
        };
    }

    /// <summary>The guide to the JSON form, Markdown starting at <c>##</c>.</summary>
    internal static string Guide() => """
        ## Workbooks as JSON

        A workbook can also be given as JSON: the same workbook as data instead of Markdown, which suits figures that
        come from a program. It follows the JSON Schema of the workbook. Names are camelCase, values of a fixed set are
        camelCase strings (`dateTime`, `sum`, `bar`), and a property the schema does not name is an error.

        ```json
        {
          "title": "Quarterly figures",
          "date": "2026-03-31",
          "marking": { "classification": "internal" },
          "sheets": [
            {
              "name": "Orders",
              "columns": [
                { "header": "Item" },
                { "header": "Price", "type": "currency", "currency": "USD" },
                { "header": "Quantity", "type": "integer", "total": "sum" },
                { "header": "Amount", "type": "currency", "currency": "USD", "total": "sum" },
                { "header": "Ordered", "type": "date" },
                { "header": "Share", "type": "percent", "decimals": 1 }
              ],
              "rows": [
                ["Pens", 1.5, 10, { "formula": "[Price]*[Quantity]" }, "2026-01-15", 0.25],
                ["Pads", 3.2, 4, { "formula": "[Price]*[Quantity]" }, "2026-02-03", null]
              ],
              "charts": [{ "type": "bar", "category": "Item", "series": ["Quantity"] }]
            }
          ]
        }
        ```

        - **Workbook:** `sheets` is required; `title`, `subtitle`, `description`, `author`, `date`, `language`,
          `marking` (`classification`, `draft`) and `brand` are optional. A title or a description gives the info sheet.
        - **Sheet:** `name` and `columns` are required; `title`, `description`, `totalsLabel`, `rows` and `charts` are
          optional.
        - **Column:** `header` is required; `type` (`text`, the default, `number`, `integer`, `currency`, `percent`,
          `date`, `dateTime`, `boolean`), `currency` (an ISO 4217 code), `decimals`, `width`, `align` and `total`
          (`sum`, `average`, `count`, `min`, `max`) are optional.
        - **Chart:** `category` and `series` (column headers) are required; `type` (`column`, `bar`, `line`, `area`),
          `stacked`, `title`, `unit`, `source`, `placement` (`beside`, `sheet`) and `sheetName` are optional.
        - **Cells:** each row is an array with one value per column, in their order: a number (a percentage as a
          share, `0.25` for 25 %), a string (in a `date` column a date such as `"2026-03-15"`, in a `dateTime` column
          `"2026-03-15T14:30:00"`), `true` or `false`, `null` for an empty cell, or `{"formula": "…"}` with a formula
          as in *Formulas*.

        Every problem names the JSON path of its value, such as `$.sheets[0].rows[2][1]`. Problems with the shape come
        first, as `json-invalid`; once the shape is right, the workbook is checked as one from Markdown, with the codes
        under *Errors and notes*.

        """.ReplaceLineEndings("\n");

    // The JSON read, checked and planned.
    private static Reading Read(string json, XlsxOptions options, bool measure)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new Reading([new XlsxJsonProblem("$", NoteCodes.JsonInvalid, true, $"The text is not valid JSON: {ex.Message}")], null, null);
        }

        using (document)
        {
            return Read(document.RootElement, options, measure);
        }
    }

    private static Reading Read(JsonElement root, XlsxOptions options, bool measure)
    {
        var shape = JsonShape.Check(SchemaNode.Value, root, XlsxPlan.MaxProblems);
        if (shape.Count > 0)
        {
            return new Reading([.. shape.Select(p => new XlsxJsonProblem(p.Path, NoteCodes.JsonInvalid, true, p.Message))], null, null);
        }

        var dto = root.Deserialize(XlsxJsonContext.Default.JsonWorkbook)!;
        var problems = new List<XlsxJsonProblem>();
        var workbook = Model(dto, problems);
        if (problems.Count > 0)
        {
            return new Reading(problems, workbook, null);
        }

        XlsxPlan plan;
        try
        {
            plan = XlsxPlan.Of(workbook, options, measure);
        }
        catch (DocumentConversionException ex)
        {
            return new Reading([new XlsxJsonProblem(ex.Data[XlsxPlan.PathKey] as string ?? "$", NoteCodes.JsonInvalid, true, ex.Reason)], workbook, null);
        }

        for (var i = 0; i < plan.Problems.Count; i++)
        {
            problems.Add(new XlsxJsonProblem(plan.ProblemPaths[i], plan.Problems[i].Code, true, plan.Problems[i].Message));
        }

        foreach (var note in plan.Notes.Result(0).Notes.Take(Math.Max(0, XlsxPlan.MaxProblems - problems.Count)))
        {
            var path = plan.NotePath(note.Code, note.Message) ?? (note.Code == NoteCodes.LanguageUnknown ? "$.language" : "$");
            problems.Add(new XlsxJsonProblem(path, note.Code, false, note.Message));
        }

        return new Reading(problems, workbook, plan);
    }

    // The model of the JSON, whose shape is right; a cell that does not fit its column's JSON form is a problem.
    private static XlsxWorkbook Model(JsonWorkbook dto, List<XlsxJsonProblem> problems) => new()
    {
        Title = dto.Title,
        Subtitle = dto.Subtitle,
        Description = dto.Description,
        Author = dto.Author,
        Date = dto.Date,
        Language = dto.Language,
        Marking = dto.Marking is { } marking ? new XlsxMarking(marking.Classification, marking.Draft ?? false) : null,
        Brand = dto.Brand,
        Sheets = [.. dto.Sheets.Select((s, i) => Sheet(s, JsonShape.Item("$.sheets", i), problems))],
    };

    private static XlsxSheet Sheet(JsonSheet sheet, string path, List<XlsxJsonProblem> problems)
    {
        var columns = sheet.Columns.Select(c => new XlsxColumn(c.Header, c.Type)
        {
            Currency = c.Currency,
            Decimals = c.Decimals,
            Width = c.Width,
            Align = c.Align,
            Total = c.Total,
        }).ToList();
        var rows = new List<IReadOnlyList<XlsxCell>>();
        var r = 0;
        foreach (var row in sheet.Rows ?? [])
        {
            var cells = new List<XlsxCell>(row.Count);
            for (var c = 0; c < row.Count; c++)
            {
                var at = JsonShape.Item(JsonShape.Item(path + ".rows", r), c);
                cells.Add(Cell(row[c], c < columns.Count ? columns[c].Type : null, at, problems));
            }

            rows.Add(cells);
            r++;
        }

        return new XlsxSheet
        {
            Name = sheet.Name,
            Title = sheet.Title,
            Description = sheet.Description,
            TotalsLabel = sheet.TotalsLabel,
            Columns = columns,
            Rows = rows,
            Charts = [.. (sheet.Charts ?? []).Select(c => new XlsxChart
            {
                Category = c.Category,
                Series = c.Series,
                Kind = c.Type ?? XlsxChartKind.Column,
                Stacked = c.Stacked ?? false,
                Title = c.Title,
                Unit = c.Unit,
                Source = c.Source,
                Placement = c.Placement ?? XlsxChartPlacement.Beside,
                SheetName = c.SheetName,
            })],
        };
    }

    private static XlsxCell Cell(JsonElement value, XlsxColumnType? type, string path, List<XlsxJsonProblem> problems)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                return XlsxCell.FromNumber(value.GetDouble());
            case JsonValueKind.True or JsonValueKind.False:
                return XlsxCell.FromBoolean(value.GetBoolean());
            case JsonValueKind.Object:
                var formula = value.GetProperty("formula").GetString()!;
                if (string.IsNullOrWhiteSpace(formula))
                {
                    problems.Add(new XlsxJsonProblem(path + ".formula", NoteCodes.JsonInvalid, true, "The formula is empty. Give a formula, or null for an empty cell."));
                    return XlsxCell.Empty;
                }

                return XlsxCell.Formula(formula);
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (text.Length == 0 || type is not (XlsxColumnType.Date or XlsxColumnType.DateTime))
                {
                    return XlsxCell.FromText(text);
                }

                if (type == XlsxColumnType.Date && DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return XlsxCell.FromDate(date);
                }

                if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                {
                    return type == XlsxColumnType.Date && dateTime.TimeOfDay == TimeSpan.Zero ? XlsxCell.FromDate(DateOnly.FromDateTime(dateTime)) : XlsxCell.FromDateTime(dateTime);
                }

                problems.Add(new XlsxJsonProblem(
                    path,
                    NoteCodes.JsonInvalid,
                    true,
                    type == XlsxColumnType.Date
                        ? $"\"{GenerationNotes.Quote(text)}\" is in a date column, which takes dates as year-month-day, such as \"2026-03-15\"."
                        : $"\"{GenerationNotes.Quote(text)}\" is in a dateTime column, which takes a date and time such as \"2026-03-15T14:30:00\"."));
                return XlsxCell.Empty;
            default:
                return XlsxCell.Empty;
        }
    }

    private static DocumentConversionException Failure(List<XlsxJsonProblem> problems)
    {
        var errors = problems.Where(p => p.IsError).ToList();
        var text = new StringBuilder(errors.Count == 1
            ? "The workbook has a problem: " + errors[0]
            : string.Create(CultureInfo.InvariantCulture, $"The workbook has {(errors.Count >= XlsxPlan.MaxProblems ? "at least " : string.Empty)}{errors.Count} problems:"));
        if (errors.Count > 1)
        {
            foreach (var error in errors)
            {
                text.Append("\n- ").Append(error);
            }
        }

        return new DocumentConversionException(Format.Xlsx, text.ToString());
    }

    // ---- The schema ---------------------------------------------------------------------------------------------

    private static JsonObject Build()
    {
        var options = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        };
        return (JsonObject)XlsxJsonContext.Default.JsonWorkbook.GetJsonSchemaAsNode(options);
    }

    // The model's description first in each node; a cell's alternatives in place of JsonElement's "anything".
    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode node)
    {
        if (context.TypeInfo.Type == typeof(JsonElement))
        {
            return Cell();
        }

        // A row: the exporter leaves out the items of JsonElement, which allow anything.
        if (context.TypeInfo.Type == typeof(List<JsonElement>) && node is JsonObject row)
        {
            row["items"] = Cell();
            return row;
        }

        string? description = null;
        if (context.PropertyInfo is { } property)
        {
            description = Description(property);
        }
        else if (ModelName(context.TypeInfo.Type) is { } model)
        {
            description = Text(model);
        }

        if (description is null || node is not JsonObject obj)
        {
            return node;
        }

        var result = new JsonObject { ["description"] = description };
        foreach (var (name, value) in obj.ToList())
        {
            obj.Remove(name);
            if (name != "description")
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static string? Description(JsonPropertyInfo property)
    {
        if (ModelName(property.DeclaringType) is not { } model)
        {
            return null;
        }

        var member = property.DeclaringType == typeof(JsonChart) && property.Name == "type" ? "Kind" : char.ToUpperInvariant(property.Name[0]) + property.Name[1..];
        var text = Text(model + "." + member);
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (text is not null && type.IsEnum)
        {
            var values = Enum.GetNames(type).Select(n => (Json: char.ToLowerInvariant(n[0]) + n[1..], Text: Text(type.Name + "." + n)));
            text += " Values: " + string.Join(" ", values.Select(v => $"\"{v.Json}\": {v.Text}"));
        }

        return text;
    }

    private static string? Text(string key) => XlsxJsonDescriptions.Texts.GetValueOrDefault(key);

    private static string? ModelName(Type type) =>
        type == typeof(JsonWorkbook) ? nameof(XlsxWorkbook)
        : type == typeof(JsonMarking) ? nameof(XlsxMarking)
        : type == typeof(JsonSheet) ? nameof(XlsxSheet)
        : type == typeof(JsonColumn) ? nameof(XlsxColumn)
        : type == typeof(JsonChart) ? nameof(XlsxChart)
        : null;

    private static JsonObject Cell() => new()
    {
        ["description"] = CellDescription,
        ["anyOf"] = new JsonArray(
            new JsonObject { ["type"] = "number" },
            new JsonObject { ["type"] = "string" },
            new JsonObject { ["type"] = "boolean" },
            new JsonObject { ["type"] = "null" },
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["formula"] = new JsonObject { ["description"] = FormulaDescription, ["type"] = "string" },
                },
                ["required"] = new JsonArray("formula"),
                ["additionalProperties"] = false,
            }),
    };

    private static string Write(JsonNode node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private sealed record Reading(List<XlsxJsonProblem> Problems, XlsxWorkbook? Workbook, XlsxPlan? Plan);
}

/// <summary>A problem of a workbook given as JSON (<see cref="XlsxWorkbookJson.Check(string, XlsxOptions?)"/>).</summary>
/// <param name="Path">Where it is in the JSON: <c>$.sheets[0].rows[2][1]</c>; <c>$</c> for the whole.</param>
/// <param name="Code">What it is, one of <see cref="NoteCodes"/>: <c>json-invalid</c> for the shape, else a code of workbooks such as <c>cell-type</c>.</param>
/// <param name="IsError">Whether it stops the workbook from being written; a note when not.</param>
/// <param name="Message">What is wrong and what to change, in English.</param>
public sealed record XlsxJsonProblem(string Path, string Code, bool IsError, string Message)
{
    /// <summary><c>$.sheets[0].rows[2][1]: Message</c>.</summary>
    public override string ToString() => this.Path + ": " + this.Message;
}
