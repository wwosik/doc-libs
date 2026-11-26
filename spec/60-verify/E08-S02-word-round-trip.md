# E08-S02 Word round trip

> Status: in verification
> Epic: [E08 Document converters](../50-todo/E08-document-converters.md)

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

## Implementation notes

**Done as proposed**, with these decisions where the story left a choice:

- **Generator side** (`DocxRoundTrip.cs`): the marker `dl:generated` in `settings.xml` holds the
  version (`1`), the heading shift, the marking (`classification`, `draft`) and the style id of
  every role by its default id, so a caller's template with renamed styles reads back exactly. The
  body keeps `dl:md` (the Markdown) on the first element of every top-level `:::` container and
  drawn code block, `dl:skip` on its other elements; `dl:code` (language) on a code block's first
  line; `dl:attrs` (heading attributes as written) on headings; `dl:id` on a bookmark whose name
  differs from the Markdown id; `dl:before` on the element after a heading that writes nothing
  (`## {.quote}`); `dl:title`, `dl:attrs`, `dl:url` on a picture's `wp:docPr` and `dl:skip` on the
  caption its title made. The namespace is declared and listed in `mc:Ignorable` on the document,
  the footnotes and the settings. The Open XML validator accepts all of it.
- **Deviation: layout containers keep their Markdown too.** The story names drawn containers;
  cards, rows, panels, statements, columns, `conclusion` and `comment` are kept the same way,
  because Word shows only their content and reading that back cannot give the same document. Without
  the namespace they come back as their content (a card's heading as a bold paragraph).
- **Deviation: heading attributes are kept as written** (`dl:attrs`), because the heading's id, and
  so its bookmark and every link to it, is made from their text; `{kicker="…"}` would otherwise
  change the document. Slide kinds then keep their checks.
- **Icons** are named in the picture's `title` (`Icon: circle-check`), not in the namespace: it is
  where the story puts them, and it survives a save by Word. **Harvey balls** are text in Word
  documents (the generator draws none), so nothing was needed for them.
- **Subtitle and date** go back as front matter (answer 1): `subtitle` when the cover has exactly one
  subtitle paragraph without a line break, else the paragraphs stay under `#`; `date` from the
  cover's second detail line. The generator gained both keys (front matter `subtitle` first, before
  the paragraphs; `date` replaces today's date on the cover and in the footer). `FrontMatter`
  accepts them for every generator, so converted Markdown also works for PDFs and decks, which ignore
  them.
- **Front matter keys are written where they differ from the defaults:** `toc` always (true or
  false), `tocDepth` with a contents, `headingNumbers: false`, `justify: true`; `lang` whenever the
  core properties have one; `labels.<category>` from the status labels found in tables.
- **Recognition without the marker:** the styles `CoverDetails` and `ImageRow`, which the generator
  adds to every document. A caller's template that maps those roles to its own styles is then read
  like any other Word document. Without the marker the heading shift is guessed from the page
  breaks of Heading 1.
- **New lists** at the same level are separated by a blank line, and an ordered list right after
  another switches between `1.` and `1)` so that Markdown starts a new one.
- **Features merged into main meanwhile** (rebased onto them): appendices come back as
  `{.appendix}` (with the marker from `dl:attrs`, and `dl:label` strips the "Appendix A " the
  generator writes as text without heading numbers; without it from that text or from the lettered
  numbering); the cover image as `coverImage` (named after the picture's description, the resource's
  file name), `coverImageSize` and `coverImagePosition` (kept on its `wp:docPr`, else from the anchor);
  `description` and `keywords` from the core properties (the subject is the subtitle's, so there is
  one key, `subtitle`, not two); `audience` from the marker when the document is not the customer's version
  (`AudienceTests.ADocumentWithoutTheSyntax_IsTheSameInEveryVersion` ignores that line for Word); image styles (`.round`, `.grey`, `fit=cover`, `focus`) through the image's `dl:attrs`, and
  without them `.round` from the ellipse. `{.backup}`, which Word ignores, comes back through
  `dl:attrs`. `BackMatterTests.Word_WithoutNumbers_HeadsTheAppendicesAsText` now expects
  `# Glossary {.appendix}` instead of the label as text.
- **Footnotes from any `.docx`:** one `[^n]` per reference, in order, definitions at the end; also in
  the old path for other documents.

**Tests:** `RoundTripTests` — every sample (report, report with diagrams, long, layouts, pages,
drawings) on every template (neutral, sample, custom): Markdown → `.docx` → Markdown → `.docx`,
compared part by part (PNG and fonts by hash, the chart workbooks by their entries, whose ZIP times
differ), and the converted Markdown is a fixed point; the same files without the namespace still
convert; the fallbacks (fence without language, `chart` block from cached values, picture); front
matter; blocks and inlines by role; footnotes from a plain Word document; appendices (numbered
and as text), the cover image, file properties, an internal version with exercises and solutions,
and image styles, each round trip and without the namespace. The `*.roundtrip.expected.md`
files were regenerated and now show the full Markdown.

**Verification concerns:** the comparison regenerates with the same options. A Word save was
simulated by removing the namespace, not done in Word itself. Converted Markdown is not normalised
with `MarkdownText.Normalize` (it would collapse blank lines inside code).

**Loose ends:** a code block or table inside a list item ends the list there; two different images
with the same bytes become one resource; a page break before a list item inside a list is not kept.

**Breaking changes:** `DocxConverter` output for generated documents (now their Markdown, without the
table of contents), footnotes read from every `.docx`, the new namespace and icon titles in
generated files, and the Word authoring guide's new front-matter row (see `CHANGELOG.md`). To stay under the guide's
3,500 words, two sentences of the cover table are shorter.
