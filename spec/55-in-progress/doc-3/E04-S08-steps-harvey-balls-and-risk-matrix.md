# E04-S08 Steps, Harvey balls and risk matrix

> Status: in progress (doc-3)
> Epic: [E04 Diagrams and charts](../../50-todo/E04-diagrams-and-charts.md)

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

- [ ] `::: steps` draws 2–7 chevrons; with `{.current}` on one item, earlier ones are done and later
  ones outlined; 1 or 8 items, or two `.current`, throw.
- [ ] `[0/4]` to `[4/4]` in a table cell are drawn Harvey balls in PDFs, without a character being
  replaced or reported, and the matching glyphs in decks.
- [ ] `::: riskmatrix` places each risk in its cell, numbered, with a legend, cells coloured by score
  in the brand's label colours; `lang: de` gives German axis titles.
- [ ] Decks pass the Open XML validator and open in PowerPoint without repair, the Harvey glyphs
  showing; PDFs stay PDF/A-2b.
- [ ] Samples without the new syntax produce the same decks and PDFs as before.
- [ ] Guides, docs and CHANGELOG are updated, and all tests pass.

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
