# E10-S05 Workbooks as JSON, and structured input for the document tool

> Status: in verification
> Epic: [E10 Workbook generator](../50-todo/E10-workbook-generator.md)

## Summary

A workbook can be given as JSON that follows a JSON Schema generated from the model. Every problem is
reported with its JSON path. `DocumentTool` learns to offer a generator that takes structured input
beside Markdown: the tool gets a `workbook` parameter that carries the schema, and the AI sends the
workbook as data instead of writing a Markdown table.

## Context

- `DocumentTool` (`src/wwosik.DocLibs.DocumentGeneration/DocumentTool.cs`) takes a list of
  `IMarkdownGenerator`s. Its parameters schema has `format`, `markdown` (required) and `mode`. `Run`
  parses the arguments and replies in fixed JSON.
- `IMarkdownGenerator` (`IMarkdownGenerator.cs`) has `Format`, `Title`, generate, check and guide
  over a `MarkdownDocument` only.
- System.Text.Json's `JsonSchemaExporter` writes a schema from source-generated type info, which
  works under AOT.

## Proposed approach

1. **JSON form** (`XlsxWorkbookJson`):
   - Names are camelCase, and enums are camelCase strings. Extra properties are not allowed.
   - **Workbook:** `title`, `subtitle`, `description`, `author`, `date`, `language`, `marking`,
     `brand`, and `sheets` (required).
   - **Sheet:** `name` and `columns` (required); `title`, `description`, `totalsLabel`, `rows`,
     `charts`.
   - **Column:** `header` (required); `type`, `currency`, `decimals`, `width`, `align`, `total`.
   - **Chart:** `category` and `series` (required); `type`, `stacked`, `title`, `unit`, `source`,
     `placement`, `sheetName`.
   - **Cell:** a number (a percentage as a share, `0.12`), a string (ISO dates in date columns), a
     boolean, `null`, or `{"formula": "…"}`.

2. **Schema**:
   - `XlsxWorkbookJson.Schema` is generated with `JsonSchemaExporter` from source-generated type
     info.
   - Descriptions come from the model's XML documentation through a transform, so the schema and
     the docs say the same.
   - A test checks the schema against a committed copy, so a change is visible in review.

3. **Reading**:
   - `XlsxWorkbookJson.Read(string json, XlsxOptions?)` returns an `XlsxWorkbook`.
   - `Check(string json, XlsxOptions?)` returns problems with `Path` (`$.sheets[0].rows[2][1]`),
     `Code`, `IsError` and `Message`.
   - Shape problems (`json-invalid`) come first. When the shape is right, the model check of S01 to
     S03 follows, and its problems are mapped back to JSON paths.
   - `Read` throws `DocumentConversionException` listing every problem.

4. **Structured input for the tool**:
   - A new interface `IStructuredGenerator` in `DocumentGeneration`, beside `IMarkdownGenerator`:
     - `Format`;
     - `ParameterName` (`workbook`);
     - `ParameterSchema` (the JSON Schema);
     - `Generate(JsonElement, resources, Stream)`;
     - `Check(JsonElement)`;
     - a guide section.
   - The interface is generic: any structured model can implement it later; the workbook is the
     first.
   - The tool takes the structured input inline in the arguments or as an attached `.json`
     resource (`workbookResource`: the resource's path).
   - `XlsxGenerator.ForOptions` returns an object that implements both interfaces.
   - `DocumentTool` accepts both kinds of generator:
     - The parameters schema gets each structured generator's parameter with its schema.
     - `markdown` is required only when no structured generator is offered. Otherwise the reply
       requires one of `markdown` or a structured parameter.
     - A structured parameter implies its format. Giving both, or a structured parameter with
       another format, is an error.
     - In check mode, problems become notes `"$.path: message"`.
     - The content type of `xlsx` is
       `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
   - The combined guide gets a section on workbooks that shows both ways in.

5. **Compatibility**: a tool without a structured generator keeps its schema and replies exactly as
   today, by test.

## Acceptance criteria

- [x] The schema validates the samples, and rejects extra properties and wrong cell types with
      their paths.
- [x] A JSON workbook and the equivalent Markdown give the same bytes.
- [x] `DocumentTool` with PPTX, PDF, DOCX and XLSX offered accepts `markdown` for all four and
      `workbook` for XLSX, and rejects both together.
- [x] `DocumentTool` without XLSX has the same schema as before, by test.
- [x] The schema is AOT-safe: the trimming and AOT analysers report nothing.

## Open questions

1. Should `IStructuredGenerator` be generic (any structured format, for example a future
   financial-chart or deck model), or specific to workbooks for now?

> Generic: `IStructuredGenerator` for any structured model; the workbook is its first implementation. (2026-11-26)

2. Should the tool also accept a workbook as an attached `.json` resource, or only inline in the
   arguments?

> Both: inline in the arguments and as an attached `.json` resource. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- **JSON form.** Internal DTOs (`XlsxJsonTypes.cs`: `JsonWorkbook`, `JsonMarking`, `JsonSheet`, `JsonColumn`,
  `JsonChart`) with a source-generated `XlsxJsonContext` (camelCase, `UnmappedMemberHandling.Disallow`, enums through a
  camelCase `JsonStringEnumConverter<T>`). Every optional property is nullable, so a missing one never becomes a
  default the reader cannot see. `marking` is an object `{classification, draft}` like the model's `XlsxMarking`;
  the chart's `type` is the model's `Kind`. `null` counts as not given everywhere.
- **Schema.** `XlsxWorkbookJson.Schema` comes from `JsonSchemaExporter.GetJsonSchemaAsNode` with a transform that puts
  the description first in each node, lists each enum's values with their own descriptions, and replaces a cell
  (`JsonElement`, which the exporter leaves out of a row's `items`) by `anyOf` number, string, boolean, null and
  `{formula}`. The descriptions come from the model's XML documentation through `XlsxJsonDescriptions.cs`, a table that
  `JsonTests.Descriptions_ComeFromTheModelsDocumentation` regenerates from the `.xml` files with
  `DOCLIBS_UPDATE_EXPECTED=1` and otherwise compares (the XML file cannot be embedded in the assembly that produces
  it). In the texts, model properties appear by their JSON names and enum members as JSON strings; other references
  keep their C# names (`XlsxOptions.TimeProvider`). The schema is compared with `Samples/workbook.schema.expected.json`.
- **Shape check.** `JsonShape` (internal, in `DocumentGeneration`, reusable by later structured generators) checks
  JSON against the subset the schema uses (`type` and type lists, `properties`, `required`,
  `additionalProperties: false`, `enum`, `items`, `anyOf`, `format: date`), with paths. All shape problems are
  reported (capped at 500), then nothing else: the model check runs only on a JSON of the right shape. Strings in
  `date`/`dateTime` columns must be ISO (`json-invalid` otherwise; `dateTime` also takes `…T14:30` and a date
  alone), and an empty formula is `json-invalid`; both also stop before the model check.
- **Paths of the model's problems.** `XlsxPlan` records a JSON path beside every problem (`ProblemPaths`) and note
  (`NotePath`), from a "where am I" field set as it walks the workbook; the paths are model indexes, which are the
  JSON's. Problems of formulas point at `….formula`, column problems at the property (`columns[1].currency`), chart
  problems at `category`, `series[k]`, `type`, `placement`, `sheetName`. The brand and language exceptions carry their
  path in `Exception.Data` and become `json-invalid` at `$.brand` / `$.language`. Markdown problems are unchanged (path
  `$`, never shown).
- **API.** `XlsxWorkbookJson.Schema`, `Read(json, options)` (throws listing every error with its path, not only ten),
  `Check(json, options)` returning `XlsxJsonProblem(Path, Code, IsError, Message)`. There is no public
  `Generate(json)`: hosts use `Read` and `XlsxGenerator.Generate`, or the structured generator. New code
  `json-invalid` (XLSX only), listed in the JSON guide section but kept out of the Markdown guide.
- **`IStructuredGenerator`** (generic): `Format`, `ParameterName`, `ParameterSchema`, `StructuredGuide()` (Markdown
  from `##`), `Generate(JsonElement, resources, Stream)`, `Check(JsonElement)`. Its check and generate notes have the
  path in front of the message (`$.sheets[0].rows[2][1]: …`), line 0. `XlsxGenerator.ForOptions` still returns
  `IMarkdownGenerator`; the object also implements `IStructuredGenerator`.
- **`DocumentTool`** discovers structured input on the generators it is given (no new constructor). It adds
  `workbook` (the generator's schema with the tool's sentence in front of its description) and `workbookResource`
  (the path of an attached `.json` resource, UTF-8 with or without BOM; comments and trailing commas tolerated) after
  `markdown`; `markdown` leaves `required`, `format` stays required when several formats are offered (a structured
  parameter implies its format at run time, so leaving `format` out also works). Errors: both inputs, `markdown`
  with structured input, another `format`, a non-object `workbook`, a resource not attached or not JSON. A
  structured generator whose formats differ, whose parameter name is not lower camelCase ASCII or is taken, or whose
  schema is not an object throws `ArgumentException`. The guide gets a *Data instead of Markdown* bullet and the
  format's section ends with `StructuredGuide()`; the description names the parameters.
- **Deviation:** titles get their article right ("an Excel workbook"); before, the tool wrote "a Excel workbook" for
  workbooks (unreleased since S04). Decks, PDFs and Word documents read as before.
- **Tests.** `JsonTests` (schema snapshot, properties and required lists, descriptions, shape problems with paths,
  model problems at their paths, `Read`, `features.json` giving the same bytes as `features.md`, Open XML validation,
  the guide's example). `DocumentToolStructuredTests` (schema with all four formats in
  `tool-schema-xlsx.expected.json`, guide for PDF + XLSX in `tool-guide-xlsx.expected.md`, Markdown for all four,
  `workbook` inline and as a resource, check notes with paths, call errors, invalid structured generators, and the
  schema without XLSX equal to `tool-schema.expected.json`). The existing tool expected files are unchanged.
- **AOT:** the build reports no trimming or AOT analyser warnings; a scratch Native AOT publish of a console app
  calling `Schema`, `Check` and the tool published without warnings and wrote the same schema as the expected file.
- **Verification concerns:** open `.tmp/review/json-features.xlsx` (written by the tests; byte-identical to
  `markdown-features.xlsx`). Try the tool schema with the model APIs in use: it now contains `anyOf`, `items`,
  `format` and type lists inside `workbook`, beyond the keywords the Markdown-only schema keeps to.
- **Breaking changes:** none. A tool without a structured generator keeps its schema, description, guide and replies;
  everything workbook-related is unreleased.
