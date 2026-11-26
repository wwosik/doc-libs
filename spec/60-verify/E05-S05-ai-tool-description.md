# E05-S05 AI tool description

> Status: in verification
> Epic: [E05 Authoring feedback](../50-todo/E05-authoring-feedback.md)

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
- **The tool description** offers name, schema, a combined guide, and `Run` / `Reply` in fixed
  JSON (analysis, Foundations). By decision it uses System.Text.Json only and English names.
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

## Implementation notes

### Breaking changes

None. Everything is added: `IMarkdownGenerator`, `PptxGenerator.ForOptions`, `PdfGenerator.ForOptions`,
`DocxGenerator.ForOptions`, `DocumentTool`, `DocumentToolOptions`, `DocumentToolRun`. The static
generator methods, their output and the format guides are unchanged. From now on the reply shape
(statuses, keys, their order) is a public contract: changing it is a breaking change.

### Decisions and deviations

- **DOCX is offered** through `DocxGenerator.ForOptions`, as the plan foresaw for E09 (the generator
  exists now). Titles: `PowerPoint deck`, `PDF document`, `Word document`; content types the
  registered Office and PDF MIME types.
- **`ForOptions(options = null)`** returns a private `Bound` class per generator that calls the static
  methods with the options (`null` means the options' `Default`). The options are not checked when
  bound, only when a method runs, as with the static methods.
- **`DocumentTool.Generators`** (the offered generators, in order) is public in addition to the plan,
  so a host can list the formats.
- **Format names are unique ignoring case**, and `format` and `mode` are matched ignoring case and
  surrounding spaces: a model sending `"PDF"` gets a PDF, not an error. The schema's `enum` keeps the
  exact names.
- **Argument errors beyond the plan's list**, each an error reply: a property given twice (`JsonDocument`
  would otherwise silently take one), and a blank `markdown` (whitespace only), which would otherwise
  give an empty file. A JSON `null` counts as not given, because some models send every property with
  `null` for the ones they skip; `{"markdown": null}` is "missing".
- **Line endings** of `markdown` are made `\n` (`\r\n` and lone `\r`), so lines in notes match what the
  converters and tests use; nothing else is changed.
- **Order of the check reply:** `status`, `format`, `error` (only with a document error), `pages`,
  `notes`, `statistics`. The plan said `error` "is added"; it comes right after `format` so a model reads it
  first. `slidesByKind` and `talkTimeMinutes` are written when `CheckStatistics.TalkTime` is set, which is
  exactly decks whose check got as far as the slides (E05-S04: with an error, decks have no kinds or
  talk time).
- **`pages` is always present**, 0 for Word documents, so the keys of a status never depend on the format;
  the guide says that 0 means the program that opens the file lays out the pages.
- **`line` is always present**, 0 when unknown, as in `GenerationNote`.
- **The error reply** holds only `status` and `error`, as planned; with a document error in generate mode
  `DocumentToolRun.Generator` still says which format was asked for.
- **JSON writing:** indented, `\n` line breaks on every OS (golden files), and
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so `é`, `'` and `<` stay readable for the model instead
  of `é`; the JSON is never embedded in HTML. The schema is written the same way.
- **Description** (generated): one paragraph naming the formats as "a PowerPoint deck (format "pptx")",
  saying one call per document, the check mode (when offered), the three statuses, and the guide by its
  title "Writing documents with <name>". A blank `DocumentToolOptions.Description` throws.
- **Guide:** after the `#` heading, two short sections *Calling the tool* and *What comes back*, then
  `## <Title> (format: "<format>")` per generator. `HeadingsDown` moves ATX headings (up to three spaces
  of indent) down one level and skips fenced code (backticks or tildes, closed by a fence at least as long),
  so the examples inside the guides keep their `#` and `##`. A `######` heading stays at level 6. Setext
  headings are not moved (no guide has one). The guide is built on first use of `Guide` and kept.
- **Tests live in the shared test project,** which now references the three generators (only the tests;
  the package references none). Because the Open XML SDK's namespace `DocumentFormat` would hide the
  `DocumentFormat` enum used throughout those tests, a target `AliasOpenXml` in the test csproj gives the
  SDK's assemblies the alias `OpenXml`; `DocumentToolTests` uses `extern alias OpenXml` for the
  validator. The samples `deck.md`, `report.md` and the two images are linked from the PPTX and PDF tests.

### Verification

- `./run.sh build`: 0 warnings, 0 errors. `./run.sh test`: all green, 1 + 123 + 78 + 22 + 339 + 71 + 148 +
  236 + 441 = 1,459 passed, 6 Windows-only skipped (shared tests 295 → 339: 44 new in `DocumentToolTests`), after
  rebasing onto E03-S02 and E07-S02 (whose PDF guide changes are in `tool-guide.expected.md`).
- **New tests** (`DocumentToolTests`):
  - `ForOptions`: format, title, extension, content type of all three; the bound methods give the same
    guide and check as the static ones with the same options;
  - construction: default name, a custom name and description, invalid names (empty, space, non-ASCII,
    dot, 65 characters), no generator, a `null` one, two with the same format, a blank description;
  - schema: golden `tool-schema.expected.json` (PPTX + PDF); valid JSON, `enum` equal to the offered
    formats (three), `required`, only the allowed keywords, a description on every property; with one
    format `format` is optional with a `default`; without `OfferCheck` there is no `mode` anywhere;
  - guide: golden `tool-guide.expected.md` (PPTX + PDF); exactly one `#` heading (parsed with Markdig),
    the `##` headings expected for all three formats, every format's guide contained with its headings one
    level down; `HeadingsDown` leaves fenced (backtick, tilde, nested) and indented code alone;
  - `Run`: a deck, a PDF and a Word document generated through the tool equal `Generate` with the same
    options (decks and Word documents part by part with relationship ids masked, PDFs without `/ID`), the
    deck passes the Open XML validator, the PDF carries its PDF/A identification; check mode writes nothing
    and returns the same `CheckResult` as `Check`; golden replies `tool-reply-deck`, `-deck-check`,
    `-report`, `-report-check`; a check with a front-matter error stays `checked` with `error`; a missing
    image gives `status: error` with the generator's `Reason` and `Succeeded = false`; thirteen argument
    errors give their error replies; `mode` is refused without `OfferCheck`; CRLF, `null`s, `"PDF"` and
    `"mode": "generate"` are accepted; invalid check options (host error) throw;
  - dependencies: the shared assembly references no `wwosik.DocLibs.DocumentGeneration.*` and no
    `DocumentFormat.OpenXml*`, nor does the PDF generator.
- **Native AOT:** `.tmp/aot-tool`, a console app referencing only the PDF generator, building the tool,
  reading schema and guide and running generate, check and an argument error, publishes with
  `PublishAot` for linux-x64 without warnings and runs, giving the expected replies.
- **Generator output unchanged:** the generators only gained `ForOptions` and the private `Bound` class;
  no generation code changed, and all existing golden and round-trip tests pass unchanged.

### To verify by hand

1. Give `tool.Guide`, `tool.Description` and `tool.ParametersSchema` to a real model (any vendor) and let
   it write a deck and a PDF: does it check first, read the replies and fix its notes?
2. Read the generated description and the two intro sections of the guide: clear and short enough?
3. Judge whether `error` before `pages` in the check reply, and `pages: 0` for Word documents, read well.

### Loose ends

- The combined guide for all three formats is long (about 9,000 words); merging the shared sections was
  declined (question 2), and hosts can offer fewer formats.
- A converter tool (`read_document`) is out of scope (question 6).
- Other agents changing a format guide now also change `tool-guide.expected.md` in the shared tests
  (recorded in `ai/memory.md`).
- The HTML deck (E07-S02, `HtmlDeck.Write`) turns a `.pptx` into HTML, not Markdown into a document, so it
  is not an `IMarkdownGenerator`; offering it through the tool would be a story of its own.
