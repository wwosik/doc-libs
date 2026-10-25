# 00016 Native charts

> Status: verify — implemented 2026-10-25

## Summary

Bar, line and area charts drawn from data in the Markdown, coloured by the brand: native, editable
PowerPoint charts in decks and vector charts in PDFs — as an alternative to supplying charts as
PNGs.

## Context

- Charts reach slides only as images today. They do not follow the brand's colours or fonts, look
  soft when scaled, and cannot be edited in PowerPoint.
- Code blocks already become graphics through `ICodeBlockRenderer` (Mermaid diagrams, 00003). A
  renderer returns a `Drawing` (vectors), which decks write as shapes. A native PowerPoint chart is a
  different object (a chart part with an embedded workbook), which the renderer interface cannot
  express today.
- Priority: *could*, but done in the same round as 00010–00015 (the human's answer to question 3).

## Proposed approach

1. **Syntax:** a fenced code block ```` ```chart ```` with a small header and CSV data:

   ```text
   type: bar            # bar | line | area; stacked: true for stacked bars or areas
   title: Orders by month
   unit: k
   ---
   month, plan, actual
   Jan, 120, 132
   Feb, 118, 121
   ```

   The first column is the category axis, every further column a series.
2. **Model:** a new `ChartGraphic(ChartSpec)` beside `DrawingGraphic` and `ImageGraphic`
   (`CodeBlockGraphic` is a closed set the generators switch on), produced by a new built-in
   `ChartRenderer` in a new package `wwosik.DocLibs.DocumentGeneration.Charts` (no dependencies
   beyond the shared package).
3. **Decks:** a native chart part (`c:chartSpace` with `c:barChart` / `c:lineChart` / `c:areaChart`)
   and its embedded workbook, written with the Open XML SDK; series colours from the brand's diagram
   palette (slide colours on dark templates, 00010); fonts from the theme. Placed like an image
   (00012 rules: fills its box, side by side in a row).
4. **PDFs:** the same spec drawn as a `Drawing` (axes, gridlines, bars or lines, legend) by the layout
   engine's vector path, in the brand's table font.
5. **Errors:** malformed data throws with the block's line, like Mermaid.
6. **Authoring guide:** a *Charts* section from the renderer (`ICodeBlockRenderer.AuthoringGuide`).
7. **Tests:** each chart type in decks (validator, chart XML values, colours) and PDFs (drawing
   snapshot), data errors, a deck opened in PowerPoint to check it is editable.

## Acceptance criteria

- [ ] ```` ```chart ```` blocks become native, editable charts in decks and vector charts in PDFs.
- [ ] Bar (clustered and stacked), line and area work, in the brand's colours, on light and dark
      templates.
- [ ] Data errors throw with the line; tests, docs and the authoring guide updated.

## Breaking changes

- None: new package and a new code-block language.

## Open questions

1. Data as CSV inside the block (proposed) or as a Markdown table followed by a chart directive?

> Answered by the human (2026-10-25): CSV inside the ```` ```chart ```` block.

2. A separate `Charts` package (proposed, like Diagrams) or part of the generators?

> Answered by the human (2026-10-25): a separate package, `wwosik.DocLibs.DocumentGeneration.Charts`.

3. Is this wanted now, or only after 00010–00015 have been verified?

> Answered by the human (2026-10-25): now, together with 00010–00015.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- **Shared model:** `ChartGraphic(ChartSpec, Drawing)` with `ChartSpec` (kind, categories, series,
  stacked, title, unit, colours), `ChartSeries`, `ChartKind`. The renderer draws the chart itself, so
  generators without native charts (PDFs) just draw the `Drawing`.
- **Package `wwosik.DocLibs.DocumentGeneration.Charts`:** `ChartParser` (settings, `---`, CSV with
  quoted cells, errors naming the block's line), `ChartDrawing` (640 × 380 px, about six round steps
  from zero, legend at the bottom), `ChartRenderer` (diagram palette as series colours), and its
  `## Charts` guide section.
- **Decks:** `ChartWriter` writes the chart part as XML in schema order and the data as an embedded
  workbook (`Sheet1`), so *Edit Data* works. Bars and areas start at zero (`c:min`), number format
  `General`. The chart fills the width it has, at most 5:8 tall; text in the table font; white text
  and translucent gridlines on dark slides.

### Deviations from the plan

- `stacked: true` is ignored for lines (stacked lines mislead); documented.

### Breaking changes

- `CodeBlockGraphic` has a third kind, `ChartGraphic`. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
- Bar, line and stacked area charts were exported from PowerPoint and looked at, and the PDF drawing
  rendered to PNG; the decks pass the Open XML validator.

### Loose ends

- Pie, scatter and combined charts; data labels.
