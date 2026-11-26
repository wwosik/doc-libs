# E10-S05 Workbooks as JSON, and structured input for the document tool

> Status: todo
> Epic: [E10 Workbook generator](E10-workbook-generator.md)

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

- [ ] The schema validates the samples, and rejects extra properties and wrong cell types with
      their paths.
- [ ] A JSON workbook and the equivalent Markdown give the same bytes.
- [ ] `DocumentTool` with PPTX, PDF, DOCX and XLSX offered accepts `markdown` for all four and
      `workbook` for XLSX, and rejects both together.
- [ ] `DocumentTool` without XLSX has the same schema as before, by test.
- [ ] The schema is AOT-safe: the trimming and AOT analysers report nothing.

## Open questions

1. Should `IStructuredGenerator` be generic (any structured format, for example a future
   financial-chart or deck model), or specific to workbooks for now?

>

2. Should the tool also accept a workbook as an attached `.json` resource, or only inline in the
   arguments?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
