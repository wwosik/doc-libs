# E03-S02 Contents, heading numbers and captions

> Status: in progress (doc-3)
> Epic: [E03 PDF layout](../../50-todo/E03-pdf-layout.md)

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

- [ ] `toc: true` gives a contents page after the cover. Its entries link to their headings and
  show the page numbers the footer shows, for `tocDepth` 1 to 3.
- [ ] `headingNumbers: true` numbers three levels from the highest level after the cover, with a
  hanging indent, and the bookmarks carry the numbers. `{.unnumbered}` headings get none and do
  not count.
- [ ] `![Alt](a.png "Text")`, `![Alt](a.png "caption")`, `Table: …` after a table and `Figure: …`
  after a drawn diagram give "Figure n: …" and "Table n: …" in the document language. Images
  with only `"scale=…"` get none.
- [ ] Captions stay on the page of their visual; table captions are not repeated on continuation
  pages.
- [ ] `toc-empty` and `caption-ignored` are reported with their lines.
- [ ] Decks accept `toc`, `tocDepth`, `headingNumbers` and `{.unnumbered}` without effect. Decks
  without `Table:` / `Figure:` paragraphs are unchanged, and decks still pass the Open XML
  validator.
- [ ] PDFs without the new keys and captions come out the same as before; with them, they are
  still PDF/A-2b.
- [ ] Guides, expected guides, docs, CHANGELOG and memory are updated, and all tests pass.

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

<!-- Free space for remarks that are not answers to the questions above. -->
