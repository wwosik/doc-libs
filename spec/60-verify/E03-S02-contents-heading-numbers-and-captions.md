# E03-S02 Contents, heading numbers and captions

> Status: in verification
> Epic: [E03 PDF layout](../50-todo/E03-pdf-layout.md)

## Summary

The PDF generator sets the furniture of a report: a table of contents with real page numbers,
opt-in heading numbers (1, 1.2, 1.2.3) with a hanging indent and numbered bookmarks, and numbered
captions "Figure n" and "Table n". The story also fixes the Markdown syntax and front-matter keys
for these features, so the DOCX generator (E09) reads the same source the same way.

## Context

- The epic's scope row E03-S02 bundles seven features. This plan proposes to split them into three
  stories (question 1):
  - E03-S02, this story: contents, heading numbers, captions. All three number or list headings
    and visuals.
  - [E03-S03](E03-S03-footnotes-justification-and-page-breaks.md): footnotes at the page foot,
    justified text, page breaks. All three change how lines and pages are filled.
  - [E03-S04](E03-S04-classification-marking.md): classification marking in PDFs and decks. It
    touches the page furniture of every format, not the flow.
- **Layout today.** The layout runs once (`LayoutEngine.Run`, `LayoutEngine.cs:78`):
  - It places the cover, flows the blocks greedily, and then adds header and footer to every
    page once the page count is known (`Decorate`, `LayoutEngine.cs:892`).
  - The footer's page number is `n / N` and counts the cover.
  - Every heading gets a bookmark and its anchor (`Mark`, `LayoutEngine.cs:301`).
  - Anchors are Markdig's GitHub-style ids (`UseAutoIdentifiers`), and `#anchor` links already
    become `LinkItem`s.
  - A `#` heading after the cover starts a new page (`Heading`, `LayoutEngine.cs:197`).
- **Headings.** `MarkdownParser.Add` checks and drops heading attributes on `#` and `##` only
  (`WithoutAttributes` → `HeadingAttributes.Split`). On `###` to `######`, a trailing `{…}`
  stays text. `HeadingAttributes` knows `.dark`, `kicker`, `layout` and `.closing`, and throws for
  anything else.
- **Image titles.** Today the title only carries settings:
  - PDF: `"scale=0.5"` (`MarkdownParser.Image`, `ScalePattern`). Any other title is ignored.
  - Decks: `scale=` plus the word `caption`, which puts the alt text under the image
    (`DeckParser.Image`, `CaptionPattern`; `SlideImage.Caption`). Any other title is ignored.
- **Text after a visual.**
  - On a deck's visual slide, text after a table or image already goes below it as a caption
    (`VisualSlide.Below`).
  - In a PDF, a `Table: …` line after a table is an ordinary paragraph.
- **Generated texts.** `GeneratedTexts` (E06-S02) holds the generators' own texts in English and
  German. It is built so later stories add "Contents", "Figure" and "Table".
- **Front matter.**
  - PDF-only keys are handled in `PdfGenerator.Apply` (`pageSize`, `orientation`); design keys are
    handled by `BrandPdfTemplateOptions.Keys`.
  - Decks accept only their template's keys (`PptxTemplateOptions.Keys`). An unknown key throws,
    naming the valid ones.
  - `lang` and `labels.*` are skipped by `FrontMatter.Apply` and accepted everywhere.
- **The DOCX generator (E09).** It is planned in parallel and reuses what this story defines:
  - a TOC field;
  - numbered Heading 1–3;
  - `Table: …` captions after a table, and the image title as the figure caption, with `SEQ`
    fields.

  It assumes the keys and syntax below; the names need to be agreed here.
- **Out of scope**, decided in the analysis: cross-references (`[](#costs)` → "Table 3"), lists of
  figures and tables, and appendix lettering.

## Proposed approach

1. **Shared syntax and keys**, defined in DocumentGeneration so that all generators read them
   alike:
   - **Front-matter keys:**
     - `toc: true|false`: a table of contents. Default `false`.
     - `tocDepth: 1|2|3`: how many heading levels the table of contents lists. Default `2`.
     - `headingNumbers: true|false`: heading numbers. Default `false`.

     Hosts set the defaults through `PdfOptions.TableOfContents`, `PdfOptions.TableOfContentsDepth`
     and `PdfOptions.HeadingNumbers`.
   - **Document keys in decks** (question 7). A shared list `FrontMatter.DocumentKeys` names the
     keys that concern documents only. Decks accept those keys and ignore them, as they already
     do with `lang` and `labels.*`, so one Markdown file can serve every format.
   - **Heading attribute `{.unnumbered}`.** A heading with it gets no number and does not count.
     `HeadingAttributes` gains `Unnumbered`, and decks accept and ignore it.
     - On `###` to `######` in PDFs, a trailing `{…}` is now taken off only when every token in it
       is one that applies at that level (`.unnumbered`, and `.newpage` from E03-S03).
     - Any other `{…}` stays text, as it does today, so existing PDFs do not change.
   - **Captions.** A new internal `Captions` helper in DocumentGeneration reads them:
     - **Image.** The title of an image is its caption, after the settings are taken out: a
       `scale=N` token, and the word `caption` when it stands alone. A title of settings only
       gives no caption, except that `"caption"` alone means "use the alt text". So
       `![Sales](s.png "Sales by region, 2026")` and `![Sales by region](s.png "caption")` both
       get a caption.
     - **Table.** A paragraph directly after a table whose text starts with `Table: ` is that
       table's caption. A blank line between them is allowed.
     - **Diagram or chart.** A paragraph directly after a fenced block that a renderer drew,
       starting with `Figure: `, is that figure's caption.
     - `Table:` and `Figure:` are syntax and stay English whatever `lang` says; the generated
       label is in the document language.
     - Anywhere else, these paragraphs stay ordinary text.
2. **Generated texts.** `GeneratedTexts` gains:
   - `Contents`: "Contents" / "Inhalt";
   - `Figure`: "Figure" / "Abbildung";
   - `Table`: "Table" / "Tabelle".

   A caption reads `<label> <n>: <text>`, for example "Abbildung 3: Umsatz nach Region".
3. **Heading numbers** (PDF):
   - **Levels.** The cover heading is never numbered. Numbering starts at the highest heading
     level used after the cover: `#` chapters when there are any, otherwise `##`. It numbers
     three levels from there (`1`, `1.2`, `1.2.3`), and deeper headings stay unnumbered.
   - **Skipped levels.** A level skipped in the Markdown is not counted as `0`. A `###` straight
     after a `#` is `1.1`.
   - **Where.** Numbers are worked out in the parser into `HeadingBlock.Number` (a string,
     `null` for none). The parser also numbers the end-of-document footnotes heading of E06-S02:
     it gets no number.
   - **Drawing.** The number is set in the heading's face and colour in a column of its own, the
     width of the widest number at that level plus a gap. Wrapped lines of the heading start at
     the text, not under the number (a hanging indent). `HeadingLines` wraps at the reduced width.
   - **Bookmarks** read "1.2 Title". Anchors do not change, so existing `#anchor` links keep
     working.
4. **Table of contents** (PDF):
   - **Model.** `DocumentModel` gets `Contents` (on, and its depth). The parser collects entries
     from the headings: level, number, text after `ReplaceUnsupported`, and anchor. A heading
     without an anchor gets one.
   - **Placement.** The contents start on a new page after the cover, or on the first page when
     there is no cover. They are headed by `GeneratedTexts.Contents`, set like a `##` heading,
     not numbered and not listed in the contents themselves, but bookmarked. The first chapter
     then starts on a new page.
   - **Entries.**
     - Indented by level; `#` chapters in bold.
     - The number (if any) sits in a hanging column, then the title, then a dot leader to the page
       number at the right margin.
     - The whole entry is a link to its heading.
     - Long titles wrap before the number column.
   - **Page numbers.** The number is the one in the footer (the cover counts).
   - **Layout passes** (question 2). The number column has a fixed width: the width of the page
     count's digits, measured as the widest digit. The contents therefore take the same height
     whatever the numbers are.
     - The engine lays the document out once with the entries in place.
     - It then writes the page numbers from the anchors, the way `Decorate` writes footers.
     - A test checks that the anchors' pages are where the entries point.
     - If a later story makes the contents' height depend on the numbers, the engine repeats the
       layout until no entry's page changes, at most 4 passes. After that it keeps the last pass
       and adds a note `toc-unsettled`. Until then this path is not built.
   - **Empty contents.** A document whose headings give no entries gets no contents page, and a
     note `toc-empty`.
5. **Captions** (PDF):
   - **Model.** `ImageBlock`, `TableBlock` and `DrawingBlock` get `Caption` (runs, or `null`).
     The caption paragraph is consumed, not written again.
   - **Numbering.** Figures (images and drawings) and tables are numbered separately, in
     document order, through the whole document (question 4). Uncaptioned visuals get no number.
   - **Set.** The label and its number in bold, then the text, at 9 pt in the text colour, with
     the same `ReplaceUnsupported` handling as body text.
     - **Figures:** the caption is centred under the visual and kept on its page with it. A
       visual capped to the room under its headings (`VisualRoom`) also leaves room for its
       caption.
     - **Tables:** the caption goes above the table (question 3) and is kept with its header and
       first row through `PreparedTable.KeepHeight`. It is not repeated on continuation pages.
     - **Image rows** (`ImageRowBlock`): each image's caption sits under it, in its cell, and is
       numbered on its own.
   - **Links.** Links in a caption work, and footnote references in a caption follow E06-S02.
6. **Decks** (question 5):
   - Image titles keep today's meaning: `scale=` and `caption` (alt text below). A title with
     other words stays without effect, so no existing deck changes.
   - A `Table: …` or `Figure: …` paragraph after a visual is already shown below it as a caption.
     It now loses its `Table: ` / `Figure: ` prefix. Decks do not number captions.
   - `toc`, `tocDepth` and `headingNumbers` are accepted and ignored (step 1). An agenda slide is
     E02-S05's `{.agenda}`.
7. **Notes.** Two new codes in `NoteCodes.All`, with advice and a docs row:
   - `toc-empty` (PDF): the contents were asked for, but the document has no headings at the
     depth asked.
   - `caption-ignored` (both formats): a `Table:` or `Figure:` paragraph does not follow a table
     or a drawn diagram, so it is shown as text.

   `toc-unsettled` is added only if the repeated layout passes of step 4 are ever built.
8. **Guides, docs, changelog.**
   - **Guides.** The PDF authoring guide describes contents, heading numbers, `{.unnumbered}` and
     the three caption forms. Its front matter table lists `toc`, `tocDepth` and
     `headingNumbers`. The deck guide says the keys are for documents, and how `Table:` /
     `Figure:` and image titles behave on slides. The expected guides are regenerated.
   - **Docs.** `docs/wwosik.DocLibs.DocumentGeneration.md` gets the shared syntax: the keys and
     the caption rules, for E09 to point at. The Pdf and Pptx pages get their behaviour, and the
     notes table gets the new codes.
   - **CHANGELOG**, under breaking changes:
     - In PDFs, an image title that is not only `scale=` now shows as a caption.
     - A `Table: …` paragraph after a table is now a numbered caption above it.
     - In decks, a `Table: ` or `Figure: ` prefix under a visual is dropped.

     Under new: the keys, `{.unnumbered}`, the new `GeneratedTexts` properties and options, and
     the notes.
   - `ai/memory.md`: the caption syntax and the single-pass contents.
9. **Tests.**
   - **Layout**, with made-up metrics (`LayoutTests`):
     - every entry's page number equals its heading's page;
     - the entries link to the right anchor;
     - numbers follow the rules of step 3, including skipped levels, `{.unnumbered}` and a
       document without `#` chapters;
     - wrapped headings hang;
     - figure and table numbering, and captions kept with their visual;
     - `lang: de` labels.
   - **Generator tests.** The bookmarks read with their numbers. The PDFs stay PDF/A-2b: the
     existing structural checks pass, and veraPDF is run by hand on a document with contents,
     numbers and captions.
   - **Samples.** `long.md` gets `toc: true`, `headingNumbers: true` and one caption of each
     kind. Its `*.roundtrip.expected.md` files are regenerated and reviewed. `report.md` stays as
     it is, to show that output without the new keys is unchanged.
   - **Decks.** A deck with a `Table:` caption still passes the Open XML validator, and a deck
     without the new syntax is unchanged.

## Acceptance criteria

- [x] `toc: true` gives a contents page after the cover. Its entries link to their headings and
  show the page numbers the footer shows, for `tocDepth` 1 to 3.
- [x] `headingNumbers: true` numbers three levels from the highest level after the cover, with a
  hanging indent, and the bookmarks carry the numbers. `{.unnumbered}` headings get none and do
  not count.
- [x] `![Alt](a.png "Text")`, `![Alt](a.png "caption")`, `Table: …` after a table and `Figure: …`
  after a drawn diagram give "Figure n: …" and "Table n: …" in the document language. Images
  with only `"scale=…"` get none.
- [x] Captions stay on the page of their visual; table captions are not repeated on continuation
  pages.
- [x] `toc-empty` and `caption-ignored` are reported with their lines.
- [x] Decks accept `toc`, `tocDepth`, `headingNumbers` and `{.unnumbered}` without effect. Decks
  without `Table:` / `Figure:` paragraphs are unchanged, and decks still pass the Open XML
  validator.
- [x] PDFs without the new keys and captions come out the same as before; with them, they are
  still PDF/A-2b.
- [x] Guides, expected guides, docs, CHANGELOG and memory are updated, and all tests pass.

## Open questions

1. Split E03-S02 into three stories: S02 contents, numbers and captions (this one); S03
   page-foot footnotes, justified text and page breaks; S04 classification marking (proposed).
   Or keep it as one story?

> Answered by the human (2026-11-14): as proposed.

2. Table of contents: one layout pass with a fixed-width number column, numbers filled in
   afterwards (proposed: the contents cannot change height, so one pass is exact and there is
   no loop to tune)? Or repeat the layout until the numbers settle, at most 4 passes, as the fork
   does?

> Answered by the human (2026-11-14): as proposed.

3. Table captions above the table (proposed: the usual convention in reports, and what Word's
   caption style suggests), or below it, where the `Table:` line is written?

> Answered by the human (2026-11-14): as proposed.

4. Numbering of figures and tables: through the whole document (proposed: simple, and it matches
   DOCX `SEQ` fields without chapter numbering), or per chapter ("Figure 2.3") when heading
   numbers are on?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Decks and captions: keep image titles as they are and only strip the `Table:` / `Figure:`
   prefix (proposed: no deck changes beyond the prefix)? Or show any image title as a caption on
   slides too, as in PDFs and DOCX?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Heading numbers: three levels from the top (proposed, as the DOCX plan's numbered
   Heading 1–3), or as deep as the headings go?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Should decks accept and ignore the document keys (`toc`, `tocDepth`, `headingNumbers`, and
   later `justify`) (proposed: one source serves all formats, as with `lang`)? Or reject them as
   unsupported, as decks do with every other unknown key today?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Key names: `toc`, `tocDepth` and `headingNumbers` (proposed: short, and camelCase like
   `pageSize` and `showDate`)? Or a nested-looking `toc.depth`, like `labels.*` and `layouts.*`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

- **Decision by the human (2026-11-14):** heading numbering with a skipped level follows this story's
  rule in **every** format: the highest level used after the cover is level 1, and a skipped level
  never yields zeros (`##` sections before a later `#` must not become `0.1`, `0.2`; a `###` straight
  under a `#` is not `1.0.1`). This answers the question E09-S04 left open: the DOCX generator, which
  followed Word's own counting, now counts like the PDF (see *Implementation notes*). It is a change
  to the unreleased DOCX package.

## Implementation notes

### Breaking changes

- **PDFs read captions.** An image title with words besides `scale=N` (and a standalone `caption`)
  is now a "Figure n: …" caption under the image, where it had no effect; `"caption"` / `.caption`
  (which gave `attribute-ignored`) now give the alt text as caption. A `Table: …` paragraph after a
  table becomes its numbered caption above it, a `Figure: …` paragraph after a drawn diagram its
  caption below, instead of ordinary paragraphs. PDFs without these are byte-identical (verified).
- **Decks drop the `Table: ` / `Figure: ` prefix** of the paragraph under a table or a drawn diagram.
  Decks without such paragraphs are unchanged (verified).
- **New syntax that used to fail or stay text:** `{.unnumbered}` on `#`/`##` (was "Unknown heading
  attribute" in PDFs and decks), and taken off `###`–`######` (stayed text); `toc`, `tocDepth`,
  `headingNumbers` in PDFs and decks (were unknown keys). The valid-attributes list in error messages
  names `.unnumbered`.
- **DOCX (unreleased package, E09-S01/S04):** headings whose number's depth differs from their
  heading style get a paragraph `w:numPr`; the pre-filled TOC numbers follow (`report.md`: `1`, `2`,
  `3` instead of `0.1`, `0.2`, `1`). Recorded in CHANGELOG under the DOCX package's *New* entry, not
  as breaking, since nothing of it was released.

`CHANGELOG.md` has the three consumer-facing breaks first and the additions under *New*.

### Decisions and deviations

- **One numbering rule, shared** (`HeadingNumbering` in DocumentGeneration; the human's decision in
  *Notes*). `Top(blocks after the cover)` is the highest level outside `:::` containers. `Next(level)`
  closes the open numbered headings at that level or below; the number's depth is the number still
  open plus one, and counters below it reset. So a skipped level never gives 0: `##` before the first
  `#` are `1`, `2`, and that `#` is `3`; `###` under `#` is `1.1`. Levels from `top + 3` on are
  unnumbered and do not touch the counting; `{.unnumbered}` and container headings are never passed in.
  Word counts the same way once each paragraph is on the list level of its number's depth.
- **DOCX** uses it in place of its own counters: `shift = Top − 1` (as before), and a numbered heading
  whose depth ≠ its (shifted) level gets `w:numPr` with `ilvl` depth − 1 and `HeadingNumId`, on the
  built-in template only (where `DocxContext.HeadingNumbers` is true). The heading keeps its style
  (look, outline level, TOC level); only Word's number changes. Headings at their own depth get no
  direct `numPr`, so documents without skipped levels are unchanged. The guide's advice "do not skip
  levels, `###` under `#` is `1.0.1`" now says the opposite; the docs explain the `numPr`.
- **`{.unnumbered}`** is known to every format (`HeadingAttributes.KnowsUnnumbered` removed).
  `SplitDeep` reads it in every format; `.newpage` on `###`+ stays Word's until E03-S03. Decks take it
  off `#`/`##` (`Split`) and `###`+ paragraphs (`DeckParser.WithoutDeepAttributes`) and ignore it.
- **Document keys** (`FrontMatter.DocumentKeys`, `TocKey`, `TocDepthKey`, `HeadingNumbersKey`,
  `DocumentValue`): `FrontMatter.Apply` checks and skips them for a template that does not list them,
  so decks accept them without each deck template knowing them. Their values are checked (`tocDepth:
  4` throws in a deck too); the deck error message's list of valid keys does not name them.
- **PDF options:** `TableOfContents`, `TableOfContentsDepth` (1–3, `ArgumentOutOfRangeException`
  otherwise, as in DOCX), `HeadingNumbers` (default off); front matter as planned.
- **Parser:** numbers into `HeadingBlock.Number`; the TOC entries (`DocumentModel.Contents`,
  `ContentsEntry(Level, HeadingBlock)`) refer to the very heading blocks, so the layout replaces a
  heading's text once (`headingRuns`) for both the heading and its entry. Entry level counts from the
  top; container headings and title-less slide-kind headings are not listed, `{.unnumbered}` ones are.
  A heading without an anchor gets `contents-1`, … (unique against every Markdig id).
- **The footnotes heading** (E06-S02) is never numbered and is listed in the contents at the top
  level (the plan only said it gets no number).
- **Contents layout (question 2, one pass):** the page-number column is three widest digits (regular
  and bold) plus a 6 pt gap; the heading "Contents" is set like `##` and bookmarked at level 1 (so with
  a cover it nests under the cover's bookmark like every `##`). Entries: 16 pt indent per level, the
  number in a column per level (widest number at that level + 0.6 em), chapters bold, links stripped
  from the title runs (the entry is one link), one entry never split. `ContentsPages` writes page
  numbers and dot leaders (". . .", aligned on one grid from the margin) after the body, like
  `Decorate`. A test checks every entry's number against its anchor's page for `tocDepth` 1–3.
  **Deviation:** the plan said "the width of the page count's digits"; the page count is only known
  after the layout, so the column is fixed at three digits. A document of 1,000 pages or more gets a
  four-digit number one digit into the gap before it; the contents still never change height.
- **Heading numbers in the layout:** a number column per Markdown level (widest number + 0.6 em),
  drawn at the left on the first line's baseline after an explicit `Ensure`; the title's lines start
  after it (hanging). An `{.unnumbered}` heading starts at the margin. `Mark` prefixes the bookmark.
- **Captions (`Caption(Kind, Number, Text)`)**: numbered in the parser in document order, figures and
  tables apart. Set 9 pt, centred (also tables, as Word's caption style is), label and number bold.
  Figures 4 pt below their visual; the visual's `Ensure`, the room it may take under its headings
  (`VisualRoom`) and the heading's `KeepHeight` all include the caption. Tables: above, inside the
  table's `Ensure` with its `KeepHeight`, not repeated. Image rows: each caption in its cell, the row
  as tall as the tallest caption. With no caption every height adds `0`, so old PDFs come out
  byte-identical.
- **`Figure:` only after a fenced block a renderer drew** (as in DOCX): after an image, a `qr:`
  code or a drawn container (`::: timeline`) it is text with `caption-ignored`. A QR code's `"caption"`
  keeps its own alt-text caption inside the drawing, unnumbered.
- **Decks:** `DeckParser.WalkMarked` looks at the block before a caption paragraph: a `Table` for
  `Table:`, a fenced block in `drawn` (set when a renderer drew it) for `Figure:`; then
  `Captions.TakePrefix`, else `caption-ignored`. Paragraphs inside containers that are walked through
  `Walk` directly (not `WalkMarked`) keep their prefix without a note.
- **`caption-ignored`** message moved to `Captions.Ignored(kind)` and is shared by all three formats.
  `toc-empty` (PDF) has the `toc` key's line, 0 when the option asked.
- **Samples:** `long.md` got `toc: true`, `headingNumbers: true`, a `Table:` caption, an image with
  a title and a Mermaid block with `Figure:`; its PDF tests now draw Mermaid (as `report.md`'s do), so
  `TheSampleDocuments_GenerateWithoutNotes` gives the notes test an image renderer too. `report.md`
  is unchanged (its PDFs byte-identical). The roundtrip expectations of `long` (PDF ×2, DOCX ×3) and
  of `report`/`report-diagrams` (DOCX ×4: TOC numbers `1`, `2`, `3`) were regenerated and reviewed.

### Verification

- `./run.sh build`: 0 warnings, 0 errors. `./run.sh test` all green: 1 (+6 Windows-only skipped) +
  123 + 282 + 78 + 22 + 71 + 142 + 224 + 376 = 1,319 passed; after rebasing onto E05-S04: 1 + 123 + 295 +
  78 + 22 + 71 + 148 + 236 + 395 = 1,369 passed.
- **New tests.** `DocumentGeneration.Tests/HeadingNumberingTests.cs` (4): three levels, skipped
  levels never 0 (the `report.md` shape), sections as top, top outside containers;
  `CaptionSyntaxTests` updated (`.unnumbered` in every format, `SplitDeep`).
  `Pdf.Tests/ContentsAndCaptionsTests.cs` (17): numbers off by default, the rules incl. `{.unnumbered}`
  and containers, skipped levels, deep braces, hanging indent and numbered bookmarks; contents entries,
  depth, anchors, footnotes entry; every entry's page number equals its heading's page for `tocDepth`
  1–3, with leaders and links; contents without a cover and wrapping before the number column; German
  "Inhalt"; `toc-empty` with its line; captions of all three kinds, numbering, links, consumed
  paragraphs and `caption-ignored` with its line; captions kept on the visual's page across page
  breaks; table captions above with the header, not repeated; image-row captions; German labels; a full
  PDF with contents, numbers and captions: numbered bookmarks and the PDF/A markers.
  `Pptx.Tests/DocumentSyntaxTests.cs` (4): document keys accepted, checked, ignored; `{.unnumbered}`;
  prefixes dropped under a table and a drawn diagram (Open XML validator clean); `caption-ignored`.
  `Docx.Tests/ContentTests` (+1): skipped levels get `w:numPr` on their number's level and the TOC
  reads `1`, `1.1`, `2`, `3`, `3.1`.
- **Unchanged output.** Every PDF and deck the tests write to `.tmp/review/`, from the claim commit
  and from this change: 57 files, 55 identical (decks part by part with relationship ids masked,
  embedded workbooks included; PDFs byte for byte with `/ID` and dates masked), the two different
  ones `long-neutral.pdf` and `long-sample.pdf`, whose Markdown changed on purpose.
- **Open XML validator:** clean on every `.docx` the tests write and on `captions.pptx`.
- **PDF/A:** the structural checks pass on a document with contents, numbers and captions (XMP
  `pdfaid`, link annotations with `/F 4`, embedded fonts). veraPDF is not available on this machine.
- **Looked at** (rendered with pypdfium2): `long-sample.pdf` pages 2 (contents with leaders and page
  numbers), 3 (numbered heading), 8 (figure captions) and `contents-and-captions.pdf` page 3.

### To verify by hand

1. **veraPDF** (`--flavour 2b`) on `.tmp/review/contents-and-captions.pdf` and `long-*.pdf`.
2. **Contents** in `long-*.pdf`: click each entry, it jumps to its heading; the numbers match the
   footers; the leaders look even.
3. **Captions** in `long-*.pdf` and `contents-and-captions.pdf`: table caption above, figure captions
   below and centred, numbers bold.
4. **Word** (`report-*.docx`, `long-*.docx`): after updating fields, `report`'s `##` headings read
   `1`, `2` and `# Outlook` `3` (not `0.1`, `0.2`, `1`), with the number in the hanging indent, and the
   TOC shows the same numbers. This also checks that Word honours a paragraph `numPr` on a heading
   style that has its own.
5. **Decks:** `captions.pptx`: "Sales by region" and "The flow" under their visuals, no prefix.

### Loose ends

- The plan's step 4 loop for contents whose height depends on the numbers (`toc-unsettled`) is not
  built, as agreed.
- The standalone word `caption` in an image title is a setting (E09-S04's shared rule), so a title
  such as "a photo with a caption under it" loses that word. Kept as agreed syntax; worth a mention if
  writers trip over it.
- `## Title {.unnumbered}` keeps the attribute in its Markdig anchor (`#title-unnumbered`), as every
  heading attribute does (known limit).
- `Figure:` after drawn containers (`::: timeline` …) is not a caption in any format; a later story
  could allow it in all three together.
- E03-S03 adds `.newpage` on `###`+ for PDFs in `HeadingAttributes.SplitDeep` (`format == Docx` today)
  and page breaks in the layout; E03-S05's plain documents can switch the contents off in their own
  template. The contents' extension points: `LayoutEngine.Contents` / `ContentsPages`.
