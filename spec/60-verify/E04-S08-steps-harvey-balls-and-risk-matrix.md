# E04-S08 Steps, Harvey balls and risk matrix

> Status: in verification
> Epic: [E04 Diagrams and charts](../50-todo/E04-diagrams-and-charts.md)

## Summary

Three more graphics common in consulting decks and reports. `::: steps` draws a list as a row of
chevrons, with the current step marked by `{.current}`. `[3/4]` alone in a table cell becomes a
Harvey ball, a circle filled by quarters. `::: riskmatrix` places risks from a table on a 5 × 5
grid of likelihood and impact, coloured by score. Steps and the risk matrix are drawn containers on
the ground E04-S07 lays; Harvey balls live inside tables.

## Context

- **Drawn containers:** E04-S07 adds `VisualContainers` (shared, internal): name and argument
  checks through `LayoutSyntax`, `Draw(CustomContainer, VisualContext) → Drawing`, a `DrawingBlock`
  in both models, inline text as `DrawingSpan`s, the slide's brand on dark slides. This story adds
  two names to it and depends on E04-S07.
- **Item attributes:** heading attributes are parsed by `HeadingAttributes.Split` only at the end of
  `#`/`##` headings; elsewhere `{…}` stays text, so `- Build {.current}` is written as typed today.
- **Tables:**
  - Decks: `DeckParser.Table` / `Cell` build `SlideTable` cells as runs. On flow slides a cell that
    is one code span becomes a status label (`RunStyle.Label`), the precedent for a cell with a
    special meaning. `DeckWriter` writes an `a:tbl` with grid widths from `TextMetrics` (E02-S02) and
    row heights from the same estimate (`MinRowHeight`); PowerPoint still grows a row when its text
    needs more, so shapes laid over a cell can drift.
  - PDFs: `MarkdownParser.Table` / `Cell` build `TableBlock` rows; `LayoutEngine` lays the cells out
    itself (`PreparedTable`), so it knows every cell's box exactly.
  - Markdig leaves an unresolved `[3/4]` as literal text, so a cell `[3/4]` is written as typed
    today (as `[words]{.success}` was before E06-S01).
- **Glyphs:** Liberation Sans has no ◔ ◑ ◕ (U+25D4, U+25D1, U+25D5); a PDF would throw on them. In
  Office on Windows and Mac they come from font fallback (Segoe UI Symbol, Apple Symbols).
- **Colours:** `ResolvedLabelColors` (`Success`, `Warning`, `Danger`, …) for status, the diagram
  colours, `Highlight`.
- `GeneratedTexts` (E06-S02, extended in E04-S07) for axis titles.

## Proposed approach

1. **`::: steps`.**

   ```markdown
   ::: steps
   - **Analyse** – understand the current process
   - **Design** – agree the target picture {.current}
   - **Build**
   - **Roll out**
   :::
   ```

   - 2 to 7 list items, each a bold title and an optional description after a dash or colon (as
     `::: cards` items are read, `DeckParser.Item`).
   - `{.current}` at the end of at most one item marks it. Items before it are done (accent fill,
     white text), the current one is in `Highlight` with bold text, later ones are outlined
     (surface fill, border colour). Without `.current`, all are drawn alike in the accent colour.
     `{.current}` is the only item attribute; anything else in braces at the end of an item throws.
   - Drawn as chevrons in one row (the first with a flat left end), titles inside, descriptions
     below each chevron, all shrunk with `MaxWidth`. 960 px wide; height from the longest
     description, at most three lines.
2. **Harvey balls.**
   - A table cell whose whole content is `[n/4]`, n from 0 to 4, is a Harvey ball. `[1/2]` is
     accepted as `[2/4]`. A cell with anything else beside it stays text.
   - **PDFs:** `LayoutEngine` draws it at the cell's alignment, one text line high: a circle in the
     text colour and a filled sector for n/4 (Bézier arcs), as `ShapeItem`s.
   - **Decks:** the cell gets the glyph ○ ◔ ◑ ◕ ● (U+25CB, U+25D4, U+25D1, U+25D5, U+25CF) in its
     text, so it stays aligned with its row and editable (question 2). The run names a symbol font
     (`Segoe UI Symbol`) so PowerPoint does not depend on the theme font having the glyphs; it is
     never embedded. Reading the glyphs back as `[n/4]` in the PPTX converter is left to E08.
   - Applies to every table, on plain and flow slides and in PDFs.
3. **`::: riskmatrix`.**

   ```markdown
   ::: riskmatrix
   | Risk               | Likelihood | Impact |
   |--------------------|------------|--------|
   | Supplier delay     | 4          | 3      |
   | Key person leaves  | 2          | 5      |
   :::
   ```

   - One pipe table: risk, likelihood (1–5), impact (1–5) by position; 1 to 15 rows.
   - A 5 × 5 grid, likelihood up, impact right; each cell coloured by score (likelihood × impact):
     1–4 `Success`, 5–9 `Warning`, 10–25 `Danger`, as pale fills (as status labels tint cells). Each
     risk is a numbered dot in its cell (several in one cell side by side), and a numbered legend
     beside the grid (below it when the drawing would be taller than wide).
   - Axis titles from `GeneratedTexts`: `Likelihood` / `Eintrittswahrscheinlichkeit`, `Impact` /
     `Auswirkung`; values 1–5 along both axes.
4. **Notes.** No new note codes: malformed entries throw, as other containers do.
5. **Dependencies and licences.** None new.
6. **Guides, docs, changelog.**
   - Both authoring guides: `::: steps` and `{.current}`, Harvey balls, `::: riskmatrix` and its
     score bands, each with an example. The expected guides are regenerated.
   - Docs: Pptx and Pdf (where each appears, the glyph font in decks), DocumentGeneration (the new
     `GeneratedTexts` properties).
   - `CHANGELOG.md`: new: the two containers and Harvey balls. Breaking: a table cell that is
     exactly `[0/4]` … `[4/4]` or `[1/2]` is now drawn, not written as typed.
7. **Tests.** Steps: limits, `.current` at each position, a second `.current` or another attribute
   throws, dark slides. Harvey balls: all values in both formats (glyphs in decks, shapes in PDFs at
   each alignment), a cell with more text stays text. Risk matrix: score colours, several risks in one
   cell, values out of range throw, German axis titles. Every deck through the Open XML validator;
   PDF/A checks; one deck opened in PowerPoint on Windows and checked for glyph fallback.

## Acceptance criteria

- [x] `::: steps` draws 2–7 chevrons; with `{.current}` on one item, earlier ones are done and later
  ones outlined; 1 or 8 items, or two `.current`, throw.
- [x] `[0/4]` to `[4/4]` in a table cell are drawn Harvey balls in PDFs, without a character being
  replaced or reported, and the matching glyphs in decks.
- [x] `::: riskmatrix` places each risk in its cell, numbered, with a legend, cells coloured by score
  in the brand's label colours; `lang: de` gives German axis titles.
- [ ] Decks pass the Open XML validator and open in PowerPoint without repair, the Harvey glyphs
  showing; PDFs stay PDF/A-2b. *(Validator clean and PDF/A marker present; PowerPoint and veraPDF are
  open items, see Implementation notes.)*
- [x] Samples without the new syntax produce the same decks and PDFs as before.
- [x] Guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Order: implement after E04-S07, reusing its drawn-container ground (proposed), or make the shared
   part its own technical story first?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Harvey balls in decks: glyphs in the cell (proposed: aligned and editable, but they depend on
   Office's font fallback), or drawn shapes laid over the table, which look like the PDF's but can
   drift when PowerPoint grows a row?

> Answered by the human (2026-11-14): as proposed.

3. Harvey values: quarters only, plus `[1/2]` (proposed), or any fraction drawn as a sector, such as
   `[2/3]`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Risk matrix input: a table (proposed, like the Gantt), or list items with attributes such as
   `- Supplier delay {likelihood=4 impact=3}`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Score bands: 1–4 / 5–9 / 10–25 (proposed), or configurable per brand or per container
   (`bands=4,9`)?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Steps without `.current`: all alike in the accent colour (proposed), or all treated as done?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Axis orientation of the risk matrix: likelihood up, impact right (proposed, the common layout), or
   the other way round, or an argument to choose?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

- **A table cell that is exactly `[0/4]`, `[1/4]`, `[2/4]`, `[3/4]`, `[4/4]` or `[1/2]`** (white space
  around it aside) is now a Harvey ball in both generators and every table, header rows and plain
  slides included: decks write the glyph ○ ◔ ◑ ◕ ● in `Segoe UI Symbol`, PDFs draw it, where before
  the text was written as typed. Accepted by the human. A cell with anything beside it, in code or
  emphasis, or with the bracket escaped (`\[3/4]`) stays text; the CHANGELOG names `\[3/4]` as the way
  to keep the text.
- `::: steps` and `::: riskmatrix` were an "Unknown container" error before, so no working document
  changes; the error's list of valid containers now ends in `timeline, gantt, steps, riskmatrix`.
  `GeneratedTexts` gains `Likelihood` and `Impact` with English defaults (a host's `with { … }` copy of
  `English` keeps working). Output without the new syntax is unchanged (see Verification).

### Decisions

- **The human's decision (2026-11-14): the PPTX authoring guide's word limit is 3,500 words** (was
  2,600). `AuthoringGuideTests.Guide_StaysUnder3500Words` checks `< 3500`; no existing guide text was
  cut. The PDF guide stays under its 2,500 limit with the new paragraph.
- **Ground:** `VisualContainers.Names` gains `steps` and `riskmatrix`; `Arguments` returns none for
  them, and `Parse` throws "The container '::: steps big' takes no arguments." for any argument. `Draw`
  dispatches to `StepsDrawing` and `RiskMatrixDrawing` (both in DocumentGeneration, internal). Both
  parsers needed no change for them: E04-S07's `DrawingBlock` path carries them, dark slides included.
- **Steps drawing** (`StepsDrawing`): 960 px wide, chevrons 64 px high with a 22 px point, 6 px apart;
  chevron width `(960 + (n − 1) · 16) / n`, so the row fills the width. The first has a flat left end,
  the others a notch; the last is pointed too. Titles 16 px, up to two lines inside the chevron;
  descriptions 14 px, up to three lines below it, 20 px line pitch, cut with "…"; every line has a
  `MaxWidth`. Height 68 px without descriptions, else up to the third description line. Colours: done
  `Diagram.Accent` with white regular titles; current `Highlight` with a white **bold** title; later
  `Diagram.Surface` fill, `Diagram.Border` 1.5 px outline, `Diagram.Text` regular titles; without
  `{.current}` all accent with white bold titles. Descriptions in `Diagram.Text`.
- **Item syntax:** the item is one paragraph starting with `**bold**` (the title); the rest, after a
  leading `–`, `—`, `:` or `- ` (the `::: cards` separator pattern, copied), is the description.
  `{…}` at the very end of the item's text is the attribute: `{.current}` marks it; any other content
  throws "has the attribute '{.done}'; a step takes only {.current}", a second `{.current}` throws
  "is the second one marked {.current}". Braces elsewhere in the text stay text.
- **Risk matrix drawing** (`RiskMatrixDrawing`): 960 px wide; grid cells 72 px (360 px grid) from x 60,
  y 34; each cell drawn 3 px smaller so the background shows between them (no white lines, which
  would glare on dark slides). Cell fill `ResolvedBrand.Mix(colour, 0.22)` — the factor of
  `SlidePalette.LabelFill` on light slides — of `Labels.Success` (score 1–4), `Labels.Warning` (5–9) or
  `Labels.Danger` (10–25), `RiskMatrixDrawing.Band(score)`. Values 1–5 left of the rows and under the
  columns (14 px), `Likelihood` in bold above the grid's top left, `Impact` in bold centred under the
  values (15 px). Dots in the band's full colour with a white 1 px outline and the risk's number in
  white bold; in rows of up to three (four when more than nine share a cell), 24 px apart and radius 11,
  smaller when a cell holds more (15 in one cell fit inside its 72 px). Legend from x 460: a dot (r 11)
  with the number, the name 15 px at x 492, up to two lines of 20 px, 8 px between entries, in
  `Diagram.Text`. Values: exactly one digit 1–5 after trimming; anything else, an empty value or an
  empty risk throws, naming the row's line.
- **Harvey balls** (`HarveyBall`, shared): `Quarters(leaves)` takes a cell whose one leaf's inlines are
  all unescaped `LiteralInline`s whose text matches `^\[(?:([0-4])/4|1/2)\]$` after trimming. Decks:
  `DeckParser.Cell` checks it before the status-label rule and returns one run with the glyph and the
  new `TextRun.Symbol`; `DeckWriter.Run` then writes `a:latin`, `a:ea` and `a:cs` with `Segoe UI
  Symbol` instead of the table font (the East Asian and complex-script slots too, since PowerPoint may
  classify geometric shapes into either). The font is not a brand font, so `FontEmbedding` never
  embeds it (a test checks). The speaker-notes text of a table (`NotesText`) shows the glyph as well.
  PDFs: `MarkdownParser.Cell` returns an icon run (`RunStyle.Icon`, an `InlineIcon` named
  `harvey-<n>` with `HarveyBall.Drawing(n)`): a 24 × 24 px ring (r 10.5, 1.5 px) and a filled sector
  from twelve o'clock clockwise of quarter Bézier arcs (a full disc for 4). The layout's icon-in-text
  path draws it one em wide plus thin spaces, in the cell's text colour (white in the header row),
  centred on the cap height, aligned like text; no character is written, so `ReplaceUnsupported` and
  `CheckGlyphs` never see one.
- **Sample:** a new `steps-and-risks.md` (PPTX tests' `Samples`, linked into the PDF tests): steps with
  `{.current}` and descriptions, a plain-slide table with every Harvey value, both alignments and an
  escaped `\[3/4]`, a risk matrix with two risks in one cell, and steps on a `{.dark}` slide. Its round
  trips through `PptxConverter` and `PdfConverter` are committed for both built-in brands. The PPTX
  converter reads the glyphs back as glyphs (reading them as `[n/4]` is left to E08, as planned); the
  PDF converter reads drawn balls as empty cells.
- **Guides:** the same paragraph and example in both, after the timeline example. The docs page
  *Steps, Harvey balls and risk matrices* in DocumentGeneration holds the details; Pptx and Pdf
  docs point to it from their containers and tables.

### Deviations from the plan

- **The legend always sits beside the grid.** The plan put it below "when the drawing would be taller
  than wide"; with a 960 px width, a 446 px grid part and at most 15 legend entries of at most two
  lines (at most 38 + 15 · 48 = 758 px), the drawing is never taller than wide, so no below layout was
  written. Recorded in the docs.
- **The `Likelihood` title is horizontal above the grid**, not rotated beside it: `DrawingText` has no
  rotation, and adding one would change the public drawing model and every writer.
- **Titles of non-current steps are regular, not bold**, when a step is current, so the current one's
  bold title stands out; without `{.current}` all titles are bold, as written.
- **Where `Highlight` equals the diagram accent** (dark slides, whose dark-slide brand uses the
  highlight as accent), the current step would look like a done one; it then gets a 1.5 px outline in
  `Diagram.Text` (white on dark slides). Not in the plan.
- **Escaped brackets keep the text** (`\[3/4]`): not in the plan, but a cheap way out of the breaking
  change for a writer who means the text.

### Verification

- **Tests:** `./run.sh build` 0 errors (the 5 known `CS0618` warnings in `Pdf.Tests`); `./run.sh test`
  all green: DocumentConversions 123, WindowsOnly 7 (6 skipped), DocumentGeneration 251, Charts 78,
  Diagrams 71, Svg 22, Pptx 318, Pdf 197 — 1,067, 1,061 run. New: `StepsAndRiskDrawingTests`
  (DocumentGeneration, 51: chevron row filling the width, flat first end, `.current` at the first,
  second and last position, no `.current`, separators, heights, the dark-slide outline, limits, a
  second `.current`, another attribute, no bold title, arguments; score bands at their edges, cell
  tints and orientation, numbered dots side by side and the legend, 15 risks in one cell, values out of
  range, 16 rows, German axis titles; Harvey values in both forms, cells that stay text, the glyphs, the
  drawings), `StepsAndRisksTests` in Pptx (10: round trip, editable chevrons on a flow slide, German
  risk matrix, every Harvey glyph with the symbol font in all three slots on a plain and a flow slide
  at centred alignment, cells that stay text, no embedded symbol font, errors, dark-slide text) and in
  Pdf (17: PDF/A marker and round trip, drawings instead of content, every value drawn in the text
  colour with no text, left/centre/right alignment, one line high, no notes and no glyph in a real
  PDF, cells that stay text, German, errors). `SlideLayoutTests`' unknown-container message lists the
  two new names.
- **Unchanged output:** every deck and PDF the suite writes to `.tmp/review/`, before this change
  (`origin/main` at `06c2d5d`) and after it: decks part by part identical with relationship ids masked
  (also inside embedded chart workbooks), PDFs identical pixel for pixel (pypdfium2) — 38 files, among
  them `deck`, `layouts`, `presenting`, `diagrams`, `financial-charts`, `qr-and-icons`, `schedules`,
  `roles-*`, `report` and `long`.
- **Open XML validator:** clean for every deck the new tests write (`Samples.AssertValid`).
- **Rendered PDF:** page 2 of `.tmp/review/steps-and-risks-{neutral,sample}.pdf` looked at (pypdfium2):
  chevrons with done, current and outlined steps, descriptions below; Harvey balls centred or
  right-aligned with their columns, the escaped cell as text; the risk grid tinted green, amber and red
  by score, two risks side by side in one cell, the legend beside the grid.

### To verify by hand (no PowerPoint, no veraPDF here)

1. **PowerPoint on Windows** — open `.tmp/review/steps-and-risks-sample.pptx` and
   `steps-and-risks-neutral.pptx` (written by `StepsAndRisksTests.Sample_RoundTripsThroughThePptxConverter`):
   no repair prompt. Slide 2: four chevrons, *Analyse* filled in the accent, *Design* in the
   highlight with a bold title, *Build* and *Roll out* outlined, descriptions centred under them.
   Slide 3 (*Options*): **the Harvey glyphs ◔ ● ◑ ◕ ○ show as circles, not boxes**, centred in
   their columns (right in *Effort*), sized like the text, in `Segoe UI Symbol` (select one: the font
   box shows it); the last *Effort* cell reads `[3/4]`. Slide 4: the risk grid under the kicker
   *Assessment*, dots *1* and *4* side by side in likelihood 4 / impact 3, the legend readable beside
   the grid. Slide 5 on the dark layout: five chevrons in the highlight colour with white titles.
2. **PowerPoint on a Mac (or any machine without Segoe UI Symbol)** — the same slide 3: the glyphs
   should come from font fallback (Apple Symbols); check they show and keep the row height.
3. **veraPDF** — `.tmp/review/steps-and-risks-neutral.pdf` and `steps-and-risks-sample.pdf` validate as
   PDF/A-2b.
