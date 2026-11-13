# E05-S03 Generation results and notes

> Status: verify
> Epic: [E05 Authoring feedback](../50-todo/E05-authoring-feedback.md)
> Former number: 00019

## Summary

`PptxGenerator.Generate` and `PdfGenerator.Generate` return a `GenerationResult`: the number of
slides or pages written, and **notes** about what the generator did to the writer's Markdown that the
writer cannot see in the output. Examples are a slide split into three, text set smaller to fit, a
diagram left as code, or a character replaced. A host passes the notes back to whoever wrote the
Markdown, often an AI agent, so they can fix the source instead of guessing.

## Context

- Both generators return `void` today (`PptxGenerator.cs:28`, `PdfGenerator.cs:24`). Everything below
  happens without a trace:
  - Fitting (`DeckWriter.Fitting.cs`) shrinks a slide's text or table by steps, then splits it into
    parts with the same title (`Pack`). A slide whose one unit is bigger than the content area even
    at the smallest size, such as a table with fewer than two body rows or one huge list item, is
    written overfull.
  - Images on slides are enlarged up to `PptxOptions.MaxImageUpscale` (default 4×) to fill their
    room.
  - A code block whose renderer declines stays code. That covers a Mermaid type outside
    `MermaidRenderer.Supported`, and a `chart` block when no chart renderer is configured.
  - Raw HTML is written as text: `DeckParser.cs:206`, and the matching place in the PDF
    `MarkdownParser`. This stays, by the decision in the
    [analysis](../10-analysis/2026-11-13%20python-fork-port.md) (question 5), but it gets a note.
  - `PdfOptions.ReplaceUnsupported` replaces characters the fonts lack. The default stays an
    exception (question 6); a replacement gets a note.
- This is the first plan from the analysis of the Python fork. The analysis decided that `Generate`
  returns the result as a **breaking change** (question 4). Every later plan reports through these
  notes: speaker notes dropped, an image cropped by a placeholder, a scene drawn as a still, and so
  on.
- The design follows the fork's `GenerationResult` / `GenerationNote(Code, Line, Message)`, which
  came from another .NET derivative. Its codes are kept where the meaning is the same, so notes
  read alike everywhere.

## Proposed approach

1. **Public types in `wwosik.DocLibs.DocumentGeneration`:**

   ```csharp
   public sealed record GenerationResult(IReadOnlyList<GenerationNote> Notes)
   {
       public int Pages { get; init; }          // slides for decks, pages for PDFs
       public bool HasNotes => Notes.Count > 0;
       public override string ToString();       // one "- line 12: …" per note, or "No notes."
   }

   public sealed record GenerationNote(string Code, int Line, string Message)
   {
       public override string ToString();       // "line 12: Message", or just Message when Line is 0
   }

   public static class NoteCodes { public const string SlideSplit = "slide-split"; … }
   ```

   - `Line` is counted from 1 with front matter included, like `CodeBlockRequest.Line`; 0 means
     unknown.
   - `Message` is English and complete in itself: it names the slide title or the block, and says
     what to change.
   - The codes are stable strings, so a host can filter on them. Every code is documented in the
     docs pages.

2. **An internal collector, `GenerationNotes`.** It is created per `Generate` call and passed to the
   parser, layout and writer that need it. It reads `Add(code, line, message)` and keeps the order
   of the source. A note repeated for the same code and line is kept once.

3. **Line numbers on slides.** `Slide` gets a `Line` (its heading's line), set by `DeckParser`. A
   split part keeps its original's line. The PDF model already knows lines where it throws;
   blocks that can produce notes carry theirs the same way.

4. **The notes of this plan:**

   | Code | Format | When |
   | ---- | ------ | ---- |
   | `slide-split` | PPTX | A slide was split into *n* slides; the message names the title and *n* |
   | `slide-shrunk` | PPTX | Text or table had to be set at the smallest size (12 pt, or the flow minimum) to fit; smaller steps above it stay silent |
   | `slide-overfull` | PPTX | Content still overflows the content area at the smallest size, in one part |
   | `image-enlarged` | PPTX | An image was drawn at more than 2× its natural size (a blur risk); its own `scale=` silences it |
   | `code-not-drawn` | both | A renderer declined a block in a language that renderer is responsible for; the message says why, e.g. "Mermaid `journey` diagrams are not supported" |
   | `html-as-text` | both | Raw HTML was written as text; comments stay silent |
   | `character-replaced` | PDF | `ReplaceUnsupported` replaced a grapheme cluster; one note per distinct cluster, with its first line |

   The PDF's page-break rules (headings kept with what follows, capped visuals) are normal layout,
   not mistakes, so they get no notes.

5. **Renderers report why they decline.** `CodeBlockRequest` gets a `Report` init property: an
   `Action<string, string>?` taking code and message. It is excluded from equality, so requests
   stay comparable. The generators set it; a renderer that declines a block it owns calls it before
   it returns `null`. `MermaidRenderer` does this for unsupported types and for an SVG its
   translator refuses. `ChartRenderer` does it for a malformed block it currently refuses
   silently, if any. Blocks in languages nobody claims stay code silently, as they are ordinary
   code.

6. **The generators** return `new GenerationResult(notes) { Pages = … }`. Nothing else changes in
   the output: the generated file is byte-identical to today's for the same input. The existing
   expected samples prove that.

7. **Authoring guides.** Both guides get a short section, *Notes you may get back*, listing each
   code of the format with what to change in the Markdown. It is generated from `NoteCodes` and a
   text per code, and tests check that every code has its text, like the container texts. The
   `*.expected.md` guides are updated.

8. **Documentation.** Update `docs/wwosik.DocLibs.DocumentGeneration.md` with the types and the
   table of codes, and the Pptx and Pdf pages with "what `Generate` returns". Update
   `ai/memory.md`. Add a `CHANGELOG.md` entry with a *Breaking changes* section:
   - Source that calls `Generate(...)` as a statement compiles unchanged.
   - It is **binary**-breaking: assemblies compiled against the old `void` signature must be
     rebuilt.
   - A method group used as `Action<…>` no longer converts.

9. **Tests.** For each code, one test with the smallest Markdown that triggers it. The `Line` must
   be right, including front matter. One test checks that no notes appear for every existing
   sample that should be clean. Every `NoteCodes` constant must appear in the docs table (the
   same pattern as the guide tests).

## Acceptance criteria

- [ ] `PptxGenerator.Generate` and `PdfGenerator.Generate` return a `GenerationResult` with
      `Pages` equal to the slides or pages written.
- [ ] Each code in the table above is produced in its case, with the right line and a message that
      names the slide or block.
- [ ] Generated files are byte-identical to before for all existing samples and tests.
- [ ] `MermaidRenderer` reports an unsupported diagram type through `CodeBlockRequest.Report`;
      a custom renderer that ignores `Report` still works.
- [ ] Both authoring guides list their format's codes with advice; a code without a text fails a test.
- [ ] Every code is listed in the docs; `CHANGELOG.md` has the breaking change with the
      binary-compatibility warning.
- [ ] The main package is still AOT-compatible (the new types live in the generation package, not
      in it).

## Open questions

1. `Pages` for both slides and pages, or two properties `Slides` and `Pages`, one of them always 0?

> One `Pages`: slides for decks, pages for PDFs.

2. Should `GenerationOptions`/`PptxOptions`/`PdfOptions` get a `Strict` switch that throws a
   `DocumentConversionException` on the first note? Or leave that to the host, which can check
   `HasNotes`?

> No. The host checks `HasNotes`.

3. `slide-shrunk`: is shrinking worth a note at all, or only when it reaches the minimum (12 pt)?
   A note on every shrunk slide may be noise for decks with dense slides by design.

> Only when the smallest size was reached.

4. Should the authoring guides mention that hosts may report these notes, and list the codes? Or
   are the notes a matter between host and library only?

> Yes: a short section listing the codes and what to change.

5. The 2× threshold for `image-enlarged` (the fork's value): keep it, or tie it to
   `MaxImageUpscale`, reporting only when the cap is reached?

> Keep 2×; an image's own `scale=` silences it.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

**Breaking changes:** `PptxGenerator.Generate` and `PdfGenerator.Generate` return `GenerationResult`
instead of `void`. Callers that use the call as a statement compile unchanged; the change is
binary-breaking, and a method group converted to `Action<…>` no longer compiles. Recorded in
`CHANGELOG.md` under *Unreleased*. Nothing else changes for consumers: the output is the same.

**Decisions and deviations**

- `GenerationResult`, `GenerationNote` and `NoteCodes` are public in
  `wwosik.DocLibs.DocumentGeneration` (`GenerationResult.cs`). The internal collector
  `GenerationNotes` lives in the same file. `NoteCodes.All` is internal and pairs each code with
  its formats and the guides' advice text.
- Notes are ordered by line, with line-0 notes last. Notes repeated word for word are dropped,
  rather than "one per code and line" as the plan said. Two different images on the same slide
  must each get their note.
- **`slide-shrunk` is not given for the parts of a split slide.** Those parts are usually at the
  smallest size, and the `slide-split` note already tells the writer to shorten. Without this, a
  split slide would produce two notes for the same problem.
- **`slide-overfull`** is given when any part still does not fit at the largest step. For a
  visual slide this means text or a table; an image is measured as a quarter of the area, so it
  never counts as overfull, matching how fitting has always measured it.
- **`image-enlarged`** is skipped for converted SVGs (`SlideImage.Vector`), because PowerPoint
  draws the vector. It only applies on decks; the PDF never enlarges images beyond their natural
  size in the same way, so it has no image note.
- **`character-replaced` has no line** (Line 0): the PDF layout engine does not keep source lines
  on its blocks. The message names the character, its code points and the replacement. Adding
  lines to the PDF block model is a loose end, worth doing when a later plan needs lines there
  anyway.
- **`code-not-drawn`** comes from `MermaidRenderer` for types outside `Supported`, and for
  syntax errors with `OnError = KeepCode`. `ChartRenderer` never declines a block of its own
  silently (malformed blocks throw), so it needed no change. The message quotes the block's first
  line.
- **`html-as-text`** covers HTML blocks and inline HTML in both formats, including `<br>`.
  Comments stay silent, as before.
- `CodeBlockRequest` got its own `Equals`/`GetHashCode` so that `Report` (a delegate) is not part
  of equality.
- The guides' new section is `{{notes}}` in `Pptx.md` and `Pdf.md`, filled by
  `AuthoringGuides.Notes(format, without)`. The PDF guide leaves out `character-replaced` unless
  `ReplaceUnsupported` is set. The expected guides were rewritten with `DOCLIBS_UPDATE_EXPECTED=1`;
  the diff only adds the section.

**Verification done**

- The 654 tests pass, with the 6 Office tests skipped on Linux. New tests:
  `Pptx.Tests/GenerationNoteTests.cs`, `Pdf.Tests/GenerationNoteTests.cs` and
  `DocumentGeneration.Tests/GenerationResultTests.cs`. They cover every code, including lines
  with front matter. The sample deck and the sample PDFs generate without notes. A test also
  checks that every code is in the docs table and has advice.
- **Same output:** the four sample Markdown files were generated as PPTX and PDF by the previous
  commit (in a temporary worktree) and by this change, then compared.
  - The files are not byte-identical between two runs of the *old* code either: the Open XML
    SDK picks random relationship IDs, and PDFs carry an ID and dates.
  - After normalising those, every PPTX part and every PDF is identical.
  - The acceptance criterion "byte-identical" therefore holds in this sense only. A literal
    byte-for-byte check is impossible with the current code.

**Worth attention**

- The thresholds are fixed constants: 2× for images, and the smallest-size rule. If hosts want
  to tune them, that is a later option.
- In this environment the .NET CLI crashed now and then with "Internal CLR error (0x80131506)"
  during `build`/`test`; reruns succeeded. It is unrelated to the change, but worth knowing if CI
  shows it.

