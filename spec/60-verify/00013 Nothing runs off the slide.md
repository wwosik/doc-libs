# 00013 Nothing runs off the slide

> Status: verify — implemented 2026-10-25

## Summary

No slide content ends below the content area. What does not fit is first shrunk within set limits,
then continued on further slides with the same title. Applies to text, lists, tables, flow slides
and text with an image.

## Context

- Every placement in `DeckWriter` uses estimated heights (`EstimateHeight`, `EstimateLines`); nothing
  compares the total with the content area.
  - **Content slides** put the body into the layout's placeholder. PowerPoint's own autofit
    (`normAutofit`) is not requested, so a long list overflows the placeholder.
  - **Visual slides** place a table at its estimated height wherever it starts; a long table runs off
    the bottom. An image is fitted to the room, but the text above it is not limited.
  - **Flow slides** stack blocks; a dense one runs off (00005 left this open).
- The estimates themselves are improved by 00011 (tables), which this plan relies on.
- PDFs already break pages; this plan is about decks only.

## Proposed approach

### 1. Measure, then decide

Each slide is laid out without a canvas first (the writer already measures this way for flow slides)
and its height compared with the content area. If it fits, nothing changes.

### 2. Shrink, within limits

- Text sizes step down: body 20 → 18 → 16 → 14 pt (flow slides 16 → 14 → 12), never below 12 pt;
  tables down to their 00011 minimum; gaps between blocks to half.
- An image or drawing beside or below text gives up height first, down to a quarter of the content
  area, before text shrinks.
- On content slides, the placeholder also gets `a:normAutofit` with the computed `fontScale`, so
  PowerPoint keeps the text fitted when someone edits it.

### 3. Continue on another slide

When the smallest sizes still do not fit:

- **Lists and text** split between paragraphs (never inside a list item); a nested list stays with
  its parent item.
- **Tables** split between rows; the header row repeats on every continuation slide.
- **Flow slides** split between blocks; a card row, a panel or a `columns` container moves as a whole
  (if it alone does not fit, it is shrunk to the limit and left as it is).
- Continuation slides have the same title and kicker; the title gets no "(cont.)" unless
  `PptxOptions.ContinuationSuffix` is set (default `null`).

### 4. Tests, docs

- One test per case: a 30-item list, a 40-row table (header repeated), a flow slide of six panels,
  text plus an image; a slide that fits stays byte-identical.
- An overflow check helper used by the sample tests: no shape's bottom below the content area's
  bottom on any generated sample deck (`deck`, `layouts`, `diagrams`, on every template).
- Docs: *Fitting* in the PPTX page; the authoring guide's *Limits* section changes from "keep to
  about six bullets" to what now happens.

## Acceptance criteria

- [ ] On every sample deck and template, no shape ends below the content area.
- [ ] Long lists, tables and flow slides continue on slides with the same title; table headers repeat.
- [ ] Slides that fit today come out unchanged.
- [ ] Tests, docs and authoring guide updated.

## Breaking changes

- Decks with overfull slides get smaller text or more slides.

## Open questions

1. Smallest text size before splitting: 12 pt (proposed) or 14 pt?

> Answered by the human (2026-10-25): 12 pt.

2. Mark continuation slides in the title — no (proposed), or "(cont.)" by default?

> Answered by the human (2026-10-25): no; only with `PptxOptions.ContinuationSuffix`.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- `DeckWriter.Fitting.cs`: every slide goes through `Fit` before writing. Content slides: the body
  size is read from the layout's or the master's body style, shrunk by 2 pt steps to 12 pt with
  `normAutofit fontScale`, then split between list groups. Visual slides: text, caption and table
  sizes step down together, then tables split between rows with the header repeated. Flow slides:
  `FlowStyle.Scale` 1 → 0.875 → 0.75 (gaps halved), then split between units.
- `EstimateLines` now uses `TextMetrics` (00011) for all text, so heights are generous rather than
  optimistic. Slides that fit come out as before: no existing expectation changed.
- `PptxOptions.ContinuationSuffix` marks continuation slides (default none).
- Tests (`FittingTests`): shrink before split, a 30-item list, a 40-row table, an overfull flow
  slide, the suffix, and an overflow check over the `deck`, `layouts` and `diagrams` samples on the
  neutral, sample and dark templates.

### Deviations from the plan

- The overflow check covers the shapes the generator places (text boxes, boxes, pictures, tables,
  groups); text inside placeholders is fitted by the estimate and `normAutofit`, which a shape bound
  cannot show.

### Breaking changes

- Overfull slides get smaller text or more slides. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
