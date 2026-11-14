# E05-S05 AI tool description

> Status: in progress (doc-3)
> Epic: [E05 Authoring feedback](../../50-todo/E05-authoring-feedback.md)

## Summary

A host that lets an AI agent write documents gets a ready tool description from the library:
- a name;
- a JSON Schema of the parameters;
- a combined authoring guide for the formats it offers;
- `Run`, which takes the agent's arguments as JSON, generates or checks the document, and returns
  a `Reply` in a fixed JSON shape.

Everything is written with System.Text.Json only, with English parameter names. The library does
not tie the host to a model vendor.

## Context

- **What exists for writers:**
  - the authoring guides, `PptxGenerator.AuthoringGuide(options)` and
    `PdfGenerator.AuthoringGuide(options)` (E05-S02);
  - the generation notes in `GenerationResult` (E05-S03);
  - `Check` (E05-S04, planned).

  The guides are Markdown starting at `##`, each written for one format. Their sections overlap:
  front matter, characters, notes.
- **The generators are static classes**, `PptxGenerator.Generate(MarkdownDocument, Stream,
  PptxOptions?)` and `PdfGenerator.Generate(…, PdfOptions?)`.
  - No interface joins them.
  - Each lives in its own package with heavy, disjoint dependencies (Open XML SDK; bundled fonts),
    and `wwosik.DocLibs.DocumentGeneration` is what they share (`ai/memory.md`).
  - The DOCX generator (E09) will be a third package.
- **Errors** in the Markdown throw `DocumentConversionException`. An invalid brand or options
  throws `ArgumentException` (`PdfGenerator.cs:24`, `PptxGenerator.cs:31`).
- **Images.** A `MarkdownDocument` carries the Markdown, the resources (images by path) and
  metadata. An agent can only reference images the host gives it. The guides already tell the
  writer that images are "referenced by the path given to you".
- **The fork's tool description** came from another .NET derivative. It offers name, schema, a
  combined guide, and `Run` / `Reply` in fixed JSON (analysis, Foundations). Here, by decision,
  it uses System.Text.Json only and English names.
- **Hosts call models through their own SDKs.** Vendors wrap the same three parts in different
  envelopes: `input_schema`, `parameters`, `inputSchema` for MCP. The library supplies the parts,
  not an envelope.

## Proposed approach

1. **A generator interface**, public in `wwosik.DocLibs.DocumentGeneration`:

   ```csharp
   public interface IMarkdownGenerator
   {
       string Format { get; }                 // "pptx", "pdf", later "docx": the tool's enum value
       string Title { get; }                  // "PowerPoint deck", "PDF document"
       string FileExtension { get; }          // ".pptx"
       string ContentType { get; }
       string AuthoringGuide();
       GenerationResult Generate(MarkdownDocument document, Stream output);
       CheckResult Check(MarkdownDocument document);   // E05-S04
   }
   ```

   - Each generator package adds one factory: `PptxGenerator.ForOptions(PptxOptions? options)`
     and `PdfGenerator.ForOptions(PdfOptions? options)`, which return an internal implementation
     bound to those options. E09 adds `DocxGenerator.ForOptions`.
   - The static methods stay as they are.
2. **The tool**, in `wwosik.DocLibs.DocumentGeneration` (question 1). It needs System.Text.Json
   from the shared framework, no new package reference, and no reference to any generator
   package:

   ```csharp
   public sealed class DocumentTool
   {
       public DocumentTool(IReadOnlyList<IMarkdownGenerator> generators, DocumentToolOptions? options = null);
       public string Name { get; }               // default "create_document"
       public string Description { get; }        // one paragraph, names the formats
       public string ParametersSchema { get; }   // JSON Schema (draft 2020-12 subset), as a string
       public string Guide { get; }              // the combined guide, Markdown
       public DocumentToolRun Run(string arguments, IReadOnlyList<DocumentResource> resources, Stream output);
   }

   public sealed record DocumentToolOptions
   {
       public string Name { get; init; } = "create_document";   // checked: ^[a-zA-Z0-9_-]{1,64}$
       public string? Description { get; init; }                // replaces the generated one
       public bool OfferCheck { get; init; } = true;            // the "check" mode
   }

   public sealed record DocumentToolRun(string Reply)
   {
       public bool Succeeded { get; init; }      // a file was written to output
       public IMarkdownGenerator? Generator { get; init; }   // which format, for the host's file name and type
       public GenerationResult? Result { get; init; }
       public CheckResult? Check { get; init; }
   }
   ```

   - **Constructor checks.** The constructor throws `ArgumentException` for no generators, two
     with the same `Format`, or an invalid name.
3. **Parameters**, with English names and fixed meanings:
   - `format`: a string, one of the offered formats (`enum`). It is required when more than one
     format is offered; with one format it is optional and defaults to it.
   - `markdown`: a string, required. The whole document, front matter included.
   - `mode`: `"generate"` (the default) or `"check"`. It is present only with `OfferCheck`.
   - **Schema.** The schema is written by hand with `Utf8JsonWriter`, with
     `"additionalProperties": false`. Every property has a description that points to the guide.
     It uses only the part of JSON Schema all major tool APIs accept: `type`, `enum`,
     `description`, `required`, `default`.
4. **The combined guide**:
   - **Introduction.** A short part, `# Writing documents with create_document` (with the tool's
     name), says:
     - what the tool does and which formats it offers;
     - how to call it: one call per document, the whole Markdown in `markdown`, `mode: "check"`
       to check first;
     - what comes back.
   - **Formats.** Each offered generator's `AuthoringGuide()` follows under `## <Title>
     (format: "<format>")`, with all its headings moved one level down (question 2).
   - **Hosts** add what only they know (image paths, limits) around it, as the docs already say
     for the single guides.
5. **`Run`**:
   - **Arguments.** It parses `arguments` with `JsonDocument`. An argument that is not JSON, not
     an object, has a property of the wrong type, an unknown property, an unknown `format` or a
     missing `markdown` gives an error reply that names the problem and the valid values. It does
     not throw: the agent can fix its call.
   - **Generate.** It builds the `MarkdownDocument` from `markdown` and `resources` and calls
     `Generate` on the chosen generator, writing to `output`.
   - **Check.** `mode: "check"` calls `Check` instead and writes nothing.
   - **Errors.** A `DocumentConversionException` becomes an error reply with its message, and
     nothing counts as written. `output` may hold partial bytes, and the docs tell hosts to write
     to a buffer.
   - **Host errors.** `ArgumentException` and other exceptions are the host's problem, not the
     agent's, and are thrown.
6. **The reply**, fixed JSON, written with `Utf8JsonWriter` in a fixed property order, camelCase,
   indented:
   - **Success:**

     ```json
     {"status":"ok","format":"pdf","pages":4,
      "notes":[{"code":"slide-split","line":12,"message":"…"}]}
     ```

   - **Check:**

     ```json
     {"status":"checked","format":"pptx","pages":9,"notes":[…],
      "statistics":{"words":612,"images":3,"tables":2,"diagrams":1,
                    "slidesByKind":{"cover":1,"text":5},"talkTimeMinutes":11}}
     ```

     With a document error, `"error":"…"` is added and the status stays `checked`.
   - **Failure:** `{"status":"error","error":"The image 'chart.png' is not among the document's
     resources."}`
   - Statuses and keys are stable. A shape change is a breaking change in the CHANGELOG. No
     reflection-based serializer is used, so the code stays trimming- and AOT-safe.
7. **Docs and changelog.**
   - **Docs.** `docs/wwosik.DocLibs.DocumentGeneration.md` gets *A tool for AI agents*:
     - a host example that builds the tool from both generators and maps it to one vendor's
       request shape in a few lines;
     - the parameters, the reply shapes and the error rules;
     - the advice to keep the reply as the tool result.

     The Pptx and Pdf pages mention `ForOptions`. `docs/README.md` lists the tool under the
     shared package.
   - **CHANGELOG.** Under new: `IMarkdownGenerator`, `ForOptions`, `DocumentTool` and its
     records. Nothing breaks.
   - `ai/memory.md`: the tool lives in the shared package and knows no generator; the reply shape
     is a public contract.
8. **Tests.**
   - **Schema.** It is valid JSON, its `enum` matches the offered formats, and `format` is
     optional with one format. A golden file `tool-schema.expected.json` holds the schema for
     PPTX plus PDF.
   - **Guide.** A golden `tool-guide.expected.md`, regenerated with `DOCLIBS_UPDATE_EXPECTED=1`.
     It has exactly one `#` heading, and every format guide's headings are moved down.
   - **Run:**
     - a deck and a PDF are generated, and their bytes equal those of `Generate` with the same
       options (normalised as in E05-S03);
     - check mode writes nothing;
     - each argument error gives its error reply;
     - a document error gives `status: error` and `Succeeded = false`;
     - golden replies for the sample documents.
   - **Formats.** The generated PPTX passes the Open XML validator and the PDF stays PDF/A-2b, as
     through `Generate`.
   - **Dependencies.** A test asserts that the shared package references no generator package.

## Acceptance criteria

- [ ] `new DocumentTool([PptxGenerator.ForOptions(…), PdfGenerator.ForOptions(…)])` gives a name,
  a description, a JSON Schema with `format`, `markdown` and `mode`, and a combined guide with
  both formats' guides under one `#` heading.
- [ ] `Run` writes the document and returns `{"status":"ok", …}` with pages and notes. In check
  mode it writes nothing and returns statistics.
- [ ] Malformed arguments and document errors return `{"status":"error", …}` with a message the
  agent can act on; only host errors throw.
- [ ] Only System.Text.Json is used, without reflection-based serialization; parameter and reply
  names are English and stable.
- [ ] The shared package depends on no generator; a host offering only PDFs references no Open
  XML SDK.
- [ ] Docs, CHANGELOG and memory are updated, and all tests pass.

## Open questions

1. Where does the tool live? In `wwosik.DocLibs.DocumentGeneration`, behind `IMarkdownGenerator`,
   so it depends on no generator (proposed: no new package, and a host pays only for the formats
   it offers)? Or in a new package `wwosik.DocLibs.DocumentGeneration.Tool` that references every
   generator and offers all formats with one call?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. The combined guide: each format's guide in full, headings moved down (proposed: simple, and
   always in step with the single guides)? Or the shared sections (front matter, characters,
   notes) merged once, with per-format differences marked, which is shorter but needs the guides
   split into parts?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Parameters: `format`, `markdown` and `mode` only (proposed)? Or also a `fileName` or `title`
   the agent may suggest? The title already comes from front matter, and file names are the
   host's.

> Answered by the human (2026-11-14): as proposed.

4. Should a `"check"` mode be part of the tool (proposed: an agent can check before it commits
   to a file, and E05-S04 makes it cheap)? Or should the tool only generate, with checks left to
   the host?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. The tool name: default `create_document`, changeable by the host (proposed)? Or one name per
   format (`create_pptx`, `create_pdf`), one tool each?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. A second tool for the converters (`read_document`: file → Markdown for the agent)? Not in this
   story (proposed: the converters need no guide and their output is plain Markdown). Or add it
   now, while the pattern is fresh?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
