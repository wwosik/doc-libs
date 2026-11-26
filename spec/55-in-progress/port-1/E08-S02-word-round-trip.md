# E08-S02 Word round trip

> Status: in progress (port-1)
> Epic: [E08 Document converters](../../50-todo/E08-document-converters.md)

## Summary

`DocxConverter` recognises a Word document written by `DocxGenerator` and reads it back into the
Markdown it came from, so that generating that Markdown again gives the same document. Footnotes are
also read from any `.docx`.

## Context

- **The DOCX converter** (`src/wwosik.DocLibs.DocumentConversions/OpenXml/DocxConverter.cs`, 463 lines)
  maps:
  - heading styles (English and German names, `ClassifyHeading`) to `#` headings;
  - numbering (`LoadNumbering`) to lists;
  - tables (`RenderTable`), bold, italic, hyperlinks and line breaks;
  - text boxes to `> [Text box]` quotes;
  - DrawingML and VML pictures with their alt text (`Images`).
- **What it does not read** (`docs/wwosik.DocLibs.DocumentConversions.md`, "What each format
  produces"): footnotes, comments, headers and footers. Everything the generator adds on top of plain
  Word is also lost:
  - the cover and front matter;
  - alerts and conclusions (note styles);
  - code fences and their language;
  - captions (`SEQ` fields);
  - image rows (borderless tables);
  - task lists, definition lists, icons and Harvey balls;
  - status words (character styles);
  - charts and diagrams.
- **The generator** (`src/wwosik.DocLibs.DocumentGeneration.Docx/`):
  - It writes styles by role (`DocxRoles.cs`, `DocxStyles.cs`, `src/wwosik.DocLibs.DocumentGeneration/DocxStyleNames.cs`).
  - It writes footnotes (`DocxFootnotes.cs`), page breaks (`DocxBody.cs`) and core properties
    (`src/Shared/OpenXml/CoreProperties.cs`).
  - It writes native charts (`src/Shared/OpenXml/ChartWriter.cs`) and drawings as PNG with their SVG
    (`DocxBody.Graphics.cs`).
  - The Markdown source of a chart or diagram is not kept anywhere in the file.

## Proposed approach

1. **Recognise a generated document.** It has a marker in an ignorable namespace of our own
   (`urn:wwosik:doclibs:wordprocessingml`, declared in `mc:Ignorable`), written by `DocxGenerator` into
   `settings.xml`. Documents without the marker convert exactly as today.
2. **Keep what Word cannot express in that namespace**, so the converter can restore it:
   - the language of a code block;
   - the Markdown of a diagram, chart, drawn container or Gantt plan, as an element beside the
     picture or chart;
   - icons and Harvey balls, named in the picture's properties (`Icon: check`, `Harvey ball: 3/4`).

   Word drops ignorable content when a person saves the file. The converter then falls back: a fence
   without a language, a `chart` block from the chart's cached values, a picture, a table.
3. **Front matter** from the core properties and the cover:
   - `title`, `author`, `lang`;
   - `subtitle` and `date` from the cover;
   - `classification` and `draft` (content status);
   - `toc`, `tocDepth`, `headingNumbers`, `justify` from the settings, styles and fields the
     generator writes.

   The table of contents itself is not written back.
4. **Blocks by role:**
   - note styles → `> [!KIND]` alerts and `::: conclusion`;
   - the code role → a fence with its language;
   - captions → `Table: …` / `Figure: …` lines, or image titles;
   - borderless image tables → image rows;
   - the generator's task boxes, icon bullets and numbering → task, icon and numbered lists;
   - definition roles → definition lists;
   - rules → `---`;
   - page breaks → `::: pagebreak` / `{.newpage}`;
   - headings without numbers → `{.unnumbered}`;
   - a heading in a layout container → a bold paragraph, as the generator wrote it.
5. **Inline:** inline code, `==mark==`, `~~strike~~`, status words (`[words]{.success}`), links to
   headings, icons.
6. **Footnotes from any `.docx`:**
   - `w:footnoteReference` → `[^n]`;
   - the footnotes part → definitions at the end of the document.
7. **Tests.** Every sample under the DOCX generator's tests goes Markdown → `.docx` → Markdown →
   `.docx`, and the two `.docx` files are compared part by part. The same file saved by Word (with
   ignorable content dropped) still converts with the fallbacks.

## Acceptance criteria

- [ ] A document of `DocxGenerator` converts back to Markdown that generates the same document again,
      for every generator sample.
- [ ] With the ignorable namespace removed, the conversion still succeeds, with the documented
      fallbacks.
- [ ] Footnotes are read from any `.docx`.
- [ ] Other Word documents convert as before, apart from footnotes.
- [ ] `docs/wwosik.DocLibs.DocumentConversions.md` describes the round trip and its limits; the
      changelog says that generated Word files carry the new namespace.

## Open questions

1. Should the cover's subtitle and date go back as front matter, or as the paragraphs under the first
   `#` that the generator reads them from?

> As front matter (`subtitle:`, `date:`). (2026-11-26)

2. Should a comment or tracked change in a reviewed document be read at all (accepted insertions are
   read through today), or left for a later story?

> Later: insertions are read through as today; comments and tracked changes are E08-S03. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
