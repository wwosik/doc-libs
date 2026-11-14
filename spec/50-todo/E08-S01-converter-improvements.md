# E08-S01 Converter improvements

> Status: todo
> Epic: [E08 Document converters](E08-document-converters.md)

## Summary

The PDF converter keeps more of a page's structure:
- tables as pipe tables;
- drawn bullets and checkboxes as lists;
- monospace lines as code fences;
- images where they stand;
- superscripts on their line;
- rotated watermarks left out.

It uses only PdfPig. The PPTX converter reads speaker notes as `::: notes`, unless E07-S01 has
already done so (question 1).

## Context

- **The PDF converter** (`Pdf/PdfConverter.cs`) works on words only:
  - `BuildLines` groups PdfPig's words by baseline into lines and keeps only words with
    `TextOrientation.Horizontal`.
  - `PageParagraphs` joins lines into paragraphs. It breaks on gaps, early line ends, outdents,
    size changes and bullet glyphs (`StripBullet`). Larger or bold-numbered lines become `###`.
  - `Render` writes `## Page n` per page. It drops running headers and footers (seen in the same
    place on three or more pages) and images that appear on every page.
  - Images come **after** the page's text, top to bottom (`PageImages`).
- **What is lost today:**
  - **Tables** come out as one paragraph. The generator's own report gives
    "Region Revenue Change North 1,200 +4 % …" (`report-neutral.roundtrip.expected.md`).
  - **Nested lists** are flattened.
  - **Code** becomes one paragraph per line.
  - **Task boxes** are lost. The PDF generator draws them as shapes (`LayoutEngine.Box`), and
    shapes are not read.
  - **Footnote references.** A superscript (65 % size, raised, E06-S02) is grouped as a line of
    its own, because its baseline is outside the row tolerance. It splits its paragraph.
  - **Watermarks.** Rotated letters are kept out of lines but still count in the body-size
    statistics (`page.Letters`). The fallback path (`ContentOrderTextExtractor`) keeps them.
- **PdfPig 0.1.16** offers what is needed and nothing more:
  - `Page.Paths` (`PdfPath` with fill and stroke flags, subpaths and bounds) for rules, cell
    borders, boxes and drawn bullets;
  - `Letter.FontName`, `Letter.Width`, `Letter.PointSize`, `Letter.TextOrientation` and
    `Letter.StartBaseLine`;
  - `IPdfImage.BoundingBox` for image positions.

  The main package must stay PdfPig-only and AOT-compatible (`ai/memory.md`).
- **The PPTX converter** (`OpenXml/PptxConverter.cs`) reads each slide's shapes in order, skips
  the title and the footer placeholders, writes tables through `MarkdownText.Table` (cells joined
  with `<br>`) and pictures with their alt text. It never opens the notes slide part. The docs
  list "speaker notes are not read" as a limit (`docs/wwosik.DocLibs.DocumentConversions.md`).
- **E07-S01** (planned in parallel) adds `::: notes` to the deck generator. Its epic row also says
  "the PPTX converter reads notes back".
- **Expected output.**
  - The converter samples are made by `tools/sample-generator` (`sample.pdf`, `sample.pptx`),
    with `*.expected.md` beside them.
  - The generator tests convert their own output back to Markdown (`*.roundtrip.expected.md` in
    the Pdf and Pptx test projects).
  - All of these change with this story. `DOCLIBS_UPDATE_EXPECTED=1` rewrites them for review.

## Proposed approach

1. **Page graphics** (PDF). A new internal `PageGraphics.Read(page)` sorts `page.Paths` into:
   - **rules:** stroked lines, or filled rectangles thinner than 1.5 pt, horizontal or vertical;
   - **boxes:** rectangles, small squares up to 1.2 × the body size;
   - **dots:** small filled circles or squares up to 0.6 × the body size;
   - **ticks:** small open paths inside a box;
   - **fills:** filled rectangles (cell and code backgrounds).

   It works in PDF coordinates, like the rest of the converter. It also merges the pieces of a
   rule drawn per cell into one rule. The generator draws each row's lines on its own.
2. **Tables** (PDF):
   - **Full grids.**
     - Vertical and horizontal rules that cross form a grid. At least 2 × 2 cells, with every
       inner line spanning the grid.
     - The words inside the grid go to the cell holding their centre. A cell's lines are joined
       with a space (question 4).
     - The grid's area is taken out of the paragraph flow, and the table is written at the grid's
       position through `MarkdownText.Table`.
     - The first row is the header (question 3).
   - **Horizontally ruled tables.**
     - Three or more horizontal rules of the same left and right ends, with no verticals between
       them.
     - The text between them has columns: the x ranges that no word on any line of the block
       crosses.
     - At least two columns, and at least two rows besides the header.
     - Each line between two rules is one row; a row of several lines is joined like a cell.
   - **Merged cells.** A cell spanning columns (missing inner rule) repeats its text in the first
     column and leaves the others empty.
   - **Across pages.** A table continued on the next page is written on each page, with its
     header (question 5).
3. **Lists** (PDF):
   - **Drawn markers.** A *dot* left of a line's first word, vertically within the line, makes
     the line a bullet item (`- `), like a bullet glyph does today.
   - **Task boxes.** A *box* in that place makes `- [ ] `, and a box with a *tick* inside, or
     filled, makes `- [x] `. The glyph boxes ☐ ☑ ☒ are recognised too.
   - **Nesting.** Items are nested by the x of their markers: each distinct marker position
     (within 2 pt) further right is one level deeper, written with two spaces per level.
   - **Ordered lists** (question 6). A line starting with `1.` or `1)` and a hanging indent,
     where the next item continues the numbering, becomes an ordered item, nested the same way.
     Single numbered lines keep today's rule: heading if large or bold, else escaped text.
4. **Code** (PDF):
   - **Monospace lines.** A line is monospace when at least 90 % of its letters are in a
     monospace font:
     - by name (`Mono`, `Courier`, `Consolas`, `Code`, `Inconsolata`);
     - or by equal advances: all letters of the line's size within 2 % of one width.
   - **Fences.** Consecutive monospace lines, a *fill* behind them included, become one fenced
     block with three backticks, or more when the content has a run of backticks.
     - Leading spaces are rebuilt from each line's x relative to the block's left edge, at one
       character width.
     - Blank lines inside the block are kept from the vertical gaps.
     - Text is not escaped, and there is no language.
   - Inline monospace words in a proportional line stay plain (question 7).
5. **Images in place** (PDF):
   - Paragraphs, tables, code blocks and images become one list of page items, each with its top
     y, written top to bottom. An image appears between the paragraphs it sits between.
   - Images on every page are still dropped. Image names stay `page<n>-image<k>`, numbered in
     reading order.
6. **Superscripts** (PDF):
   - **Grouping.** In `BuildLines`, a word at under 80 % of its neighbours' size, whose box lies
     within a line's vertical extent and whose baseline is above that line's, joins that line at
     its x position. It no longer starts a row.
   - **Writing** (question 2). A superscript number, when a line at the foot of the same page
     starts with that number in a smaller size, becomes `[^n]`. That foot line becomes the
     definition `[^n]: text` at the end of the page's section, and leaves the flow. Any other
     superscript is written directly after the word it follows, as plain text on the same line.
7. **Watermarks** (PDF):
   - Letters whose `TextOrientation` is not `Horizontal` are left out everywhere: the body-size
     and margin statistics, the fallback extractor's text (filtered by letters, not lines), and
     the headings check.
   - Horizontal watermarks are not detected.
8. **Speaker notes** (PPTX), unless E07-S01 has done it (question 1):
   - **Reading.** For each slide, the converter follows its `notesSlide` relationship and reads
     the body placeholder (`p:ph type="body"`). It skips the slide image (`sldImg`), the slide
     number and the header and footer placeholders.
   - **Writing.** Paragraphs are escaped as today, and their indentation levels become list items
     only where the paragraph has a bullet. They are written after the slide's content as
     `::: notes`, the paragraphs, and `:::`. Empty notes are skipped.
   - **Marking.** A text box named `Classification` (E03-S04) is skipped like the footer
     placeholders. The PPTX converter's other limits stay as they are: charts, bullet levels.
9. **Fallback.** `FallbackParagraphs` stays for pages where analysis throws, now without rotated
   letters. Every new step is guarded per page: a failure falls back to today's paragraphs for
   that page, never to an exception.
10. **Docs, changelog, samples.**
    - **Docs.** The converters' table in `docs/wwosik.DocLibs.DocumentConversions.md` is
      rewritten for PDF (tables, lists, code, images in place, footnotes, watermarks, with the
      limits: no borderless tables, no columns, no inline code) and PPTX (notes).
    - **CHANGELOG**, under breaking changes: the Markdown from PDFs and decks changes shape.
      Tables, lists, code fences, image positions and `::: notes` appear where text did before. A
      consumer that parses the output should check it.
    - **Sample generator.** `tools/sample-generator` gets a second PDF, `structure.pdf`, written
      with PdfPig like `sample.pdf`. It holds:
      - a full-grid table, with one merged cell;
      - a horizontally ruled table;
      - drawn bullets on two levels;
      - drawn checkboxes, empty and ticked;
      - a numbered list;
      - a code block in Courier on a grey fill;
      - an image between two paragraphs;
      - a footnote reference with its note at the foot;
      - a rotated "DRAFT" watermark.

      `sample.pptx` gets speaker notes on two slides. All are self-made.
    - **Expected files.** `structure.pdf.expected.md` is new. `sample.pdf.expected.md`,
      `sample.pptx.expected.md` and the generators' `*.roundtrip.expected.md` are regenerated,
      and the diff is reviewed: the PDF reports' tables, lists and code now come back as Markdown.
11. **Tests.**
    - **Unit tests**, on small PDFs built in the test with PdfPig's builder:
      - grid detection with per-cell rules;
      - a ruled table's column gutters;
      - marker nesting;
      - box vs tick vs dot;
      - monospace by name and by advance;
      - code indentation;
      - superscript grouping and footnote pairing;
      - rotated letters ignored in the body size;
      - images ordered between paragraphs.
    - **Round trip.** A PDF generated from Markdown with a table, a nested list, a task list, code
      and a footnote converts back to that structure (a test in the Pdf generator tests).
    - **Notes.** A deck with notes converts to `::: notes`, and one without notes is unchanged.
    - **Unaffected.** Nothing is generated, so PDF/A and the Open XML validator are not affected.
      The generators' own validation tests still run on the regenerated samples.

## Acceptance criteria

- [ ] Full-grid and horizontally ruled tables in a PDF come out as pipe tables in place, with
  the first row as header.
- [ ] Drawn bullets, nested by indentation, come out as `-` lists. Drawn checkboxes come out as
  `- [ ]` / `- [x]`. Numbered lists with a hanging indent come out as `1.` lists.
- [ ] Runs of monospace lines come out as a fenced code block with their indentation.
- [ ] Images sit between the paragraphs they stand between on the page.
- [ ] Superscripts stay on their line; a superscript number with its note at the page foot
  becomes `[^n]` and `[^n]: …`.
- [ ] Rotated text is in no output and does not change the detected body size.
- [ ] Speaker notes come out as `::: notes` after their slide (here or in E07-S01).
- [ ] A page whose analysis fails falls back to today's paragraphs.
- [ ] The new and regenerated expected files are reviewed and pass. The docs and the CHANGELOG
  breaking change are written, and all tests pass.

## Open questions

1. Speaker notes in the PPTX converter: does E07-S01 own them (proposed: it defines `::: notes`,
   comes earlier in the order, and its round trip through the converter is the natural test)?
   Then this story drops step 8. Or does E08-S01 own them, as the epic's scope row says?

>

2. Superscripts: `[^n]` with its definition when the note is found at the page foot, otherwise
   plain text on the line (proposed)? Or always plain text on the line, the fork's minimum?

>

3. Header row: always the first row (proposed: pipe tables need one, and the generator's PDFs
   always have it). Or only when the first row is bold or filled, otherwise an empty header row?

>

4. A cell's several lines: joined with a space (proposed: wrapped lines are usually one
   sentence, and `<br>` is shown as text by the generators). Or kept apart with `<br>`, as the
   PPTX converter does?

>

5. A table continued across pages: written on each page with its header (proposed: the output
   keeps one section per page). Or merged into one table on the first page?

>

6. Numbered lists with a hanging indent as `1.` lists (proposed), or leave numbered lines as
   escaped text, as today? The rule must not catch numbered headings such as "1 Introduction".

>

7. Inline monospace words in normal lines as `` `code` ``? Not in this story (proposed: font
   switches inside a line are noisy in PDFs from other tools). Or yes, since the generator writes
   inline code that way?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
