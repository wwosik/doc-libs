# 00004 Keep headings with their content

> Status: verify — implemented 2026-10-22

## Summary

In PDFs, never leave a heading alone at the bottom of a page: a heading moves to the next page
together with the start of whatever follows it, whether that is text, a table, code, an image or
a diagram. Paragraphs also stop leaving single lines behind: no widows and no orphans.

## Context

- `LayoutEngine.Heading` (`src/wwosik.DocLibs.DocumentGeneration.Pdf/LayoutEngine.cs`) reserves
  the heading's own lines plus **two lines of body text** (`Ensure(height + 2 × 10.5 pt × 1.35)`),
  whatever actually follows the heading.
- Each block then decides alone whether it fits on the page:
  - **Images and drawings** are never split. A block taller than the space left moves to the
    next page **as a whole** (`Ensure(height)`), and the heading stays behind. This was seen in
    the 00003 samples: "Sequence" and "State" were left at the bottom of their pages.
  - **Tables** keep their header row with the start of the first row, up to 3 lines of it. That
    can be more than two body lines.
  - **Code blocks** need their padding plus one line, in 9 pt.
  - **Consecutive headings** (`##` followed by `###`): the second heading checks only its own
    room, so the first can be left alone.
- `#` headings always start a new page, so the problem concerns levels 2–6.
- **Paragraphs** (`LayoutEngine.Paragraph`, also used for list items and inside quotes) place
  line by line with `Ensure(lineHeight)`. A paragraph can therefore leave one line at the bottom
  of a page (an orphan) or carry one line over to the top of the next (a widow).
- PPTX is not affected: every `##` heading starts a slide of its own.
- Documentation: the Design table in
  [docs/wwosik.DocLibs.DocumentGeneration.Pdf.md](../../docs/wwosik.DocLibs.DocumentGeneration.Pdf.md#design)
  says headings are "kept on the same page as the first lines of what follows". The Known limits
  in the Diagrams docs mention the alone heading.

## Proposed approach

### 1. The keep height of a block

A new `LayoutEngine.KeepHeight(Block, Frame)` measures, without placing anything, how much of a
block must be on the page right after a heading:

| Block | Keep height |
| ----- | ----------- |
| Paragraph | its first two lines, or one if it has only one (as today) |
| List | the first two lines of the first item's first block |
| Quote | the keep height of its first block |
| Code | padding plus its first two lines |
| Table | what the table itself requires: the header row plus the start of the first row (up to 3 lines) |
| Image, drawing | the **whole** placed height, because they are never split |
| Heading | its own height, plus the keep height of what follows it (a chain of headings stays together) |
| Rule | its 12 pt |

The sizes come from the same code that later places the block, so the two cannot drift apart:
the image and drawing sizing, `Wrap` and the table's row heights are factored out and shared.

### 2. Headings use it

`Heading` gets the blocks that follow it (the `Block` loop passes its siblings) and reserves
`heading + gap + KeepHeight(next)` instead of two fixed body lines. Without a following block, it
reserves its own height only.

### 3. When even a full page is too small

An image or a drawing that is scaled down to the page height (see `Image` and `Drawing`) does
not fit on one page with its heading. It is then **scaled down further**, so that the heading
(and any headings chained above it) and the visual fit on one empty page together. The heading
is never left alone. This affects only visuals taller than a page minus their headings.

### 4. Widows and orphans in paragraphs

When a page break falls inside a paragraph, **at least two lines stay on the first page and at
least two go to the next**:

- Before placing a paragraph, `Paragraph` works out where its lines break: `r` lines fit on the
  page, out of `n`.
  - If `r < 2`, the whole paragraph starts on the next page.
  - If `n − r < 2`, the break moves up to `n − 2`.
  - If that leaves fewer than two lines on the first page, as with a paragraph of two or three
    lines, the paragraph moves whole.
- A paragraph taller than a page still breaks: the rule applies at each break.
- This covers every paragraph laid out by `Paragraph`: body text, list items (with their
  marker), and paragraphs in quotes.
- **Code blocks, table cells and headings are not affected.** Code lines are independent, tables
  have their own row rules, and headings are kept with what follows (step 2).
- A paragraph's keep height after a heading (step 1) becomes "its first two lines, or the whole
  paragraph when it has three lines or fewer", consistent with this rule.

### 5. Tests and docs

- Layout tests with made-up widths, each starting near the bottom of a page:
  - a heading followed by an image, a drawing, a table, code, a list, a quote, and by another
    heading followed by an image: the heading moves with its content;
  - a heading followed by a paragraph that fits: the layout is unchanged;
  - a visual taller than a page minus its heading: scaled down to fit with it;
  - paragraphs of 2, 3, 4 and many lines broken at every possible position: never a single line
    at the bottom or top of a page; a paragraph longer than a page breaks with at least two lines
    on each side of every break;
  - list items: the marker moves with the first line.
- Regenerate the `*.roundtrip.expected.md` files. Page breaks may move, and the diff is to be
  checked for exactly those moves. Then look at the pages of `diagrams-sample.pdf` that left
  "Sequence" and "State" alone.
- Docs: the Design table and a paragraph on page breaks (headings, widows and orphans) in the
  PDF docs, and the Known limits in the Diagrams docs (drop the alone heading).

## Acceptance criteria

- [x] No heading of level 2–6 ends a page while the block after it starts on the next page, for
      every kind of block.
- [x] Consecutive headings stay together with the content that follows them.
- [x] A visual too tall to fit with its heading is scaled down until both fit on one page.
- [x] No paragraph leaves a single line at the bottom or at the top of a page.
- [x] Layouts where nothing was left alone are unchanged.
- [x] Tests and docs updated; all tests pass; Release build has 0 warnings.

## Out of scope

- PPTX, where every `##` already starts a slide.
- Keeping other blocks together, such as a paragraph ending in ":" and the list after it.

## Open questions

1. An image or a diagram so tall that it does not fit on one page **together with its heading**:
   should it be scaled down further so that both fit on one page, or keep its size, with the
   heading staying at the bottom of the previous page?

> Scale it down, so that it fits with its heading.

2. Should widows and orphans inside paragraphs (a single line left at the top or bottom of a page)
   be handled in this plan too?

> Yes, together (step 4).

## Notes


## Implementation notes

### Decisions

- **One measure for both places.** `KeepHeight` and the layout share the same code and the same
  results:
  - paragraphs (`ParagraphLines`), headings (`HeadingLines`) and tables (`Prepare`, now returning
    `PreparedTable` with the table's own start height) are replaced and wrapped once, then cached
    by block reference and width;
  - images and drawings are sized by `ImageSize` and `DrawingScale`.

  The cache also keeps `ReplaceUnsupported` at one call per text element, although a heading now
  measures what follows it.
- **Chained headings and the visual cap.** `chainTop` and `chainPage` record where the headings
  directly above the current block start. Any other block resets them.
  - `VisualRoom` is the page less those headings: `Image` and `Drawing` shrink to it.
  - `KeepHeight` caps a visual the same way, using the height of the headings above it (`above`).
- **`Ensure` has a 1e-6 pt tolerance.** A capped visual makes headings plus visual exactly one
  page. Without the tolerance, float noise sent each heading of the chain to a page of its own
  (caught by `AVisualTooTallForItsHeading_IsShrunkToShareThePage`).
- **Paragraph breaks** are worked out per chunk:
  - `room` is the whole lines left on the page, `rest` the lines still to place, and `take` how
    many of them go on this page;
  - `take` is everything if it fits, else `room`, lowered so that at least two lines are left
    for the next page;
  - if fewer than two lines would stay, the paragraph moves to the next page;
  - on an empty page that cannot hold two lines, the rule gives way rather than looping.
  - The list marker is drawn with the first line, wherever that lands. The old separate `Ensure`
    for the marker is gone.
- **Keep height of code** is its padding plus two lines (padding, one line and padding for a
  one-line block), counted before long lines are broken. That is a slight underestimate for code
  with very long first lines, which break by characters anyway.
- A `#` heading after a heading ends the chain: it starts a page of its own anyway.

### Changed expectations

- None. The `*.roundtrip.expected.md` files and the Mermaid goldens are unchanged, because the
  round-trip text does not show page breaks that move whole blocks. All existing layout tests
  pass unchanged.

### Verified

- New layout tests run every case at every position on the page, with 0–59 filler paragraphs
  before it:
  - a heading followed by a drawing, image, table, code, list, quote, paragraph, rule, or another
    heading and a drawing;
  - paragraphs of 2, 3, 4, 5, 7 and 120 lines, which never leave a single line;
  - list markers, which move with their first line.

  The too-tall visual is checked once.
- All tests pass; Release build with 0 warnings.
- `diagrams-sample.pdf`: "Sequence" and "Class" now start their pages with their diagrams. Before,
  "Sequence" and "State" were left at the bottom of the previous page.
- veraPDF 1.30.3, PDF/A-2b: `diagrams-sample.pdf`, `long-sample.pdf` and `report-neutral.pdf`
  pass.

### Worth a look during verification

- Documents with many visuals now leave more empty space at page bottoms. That is the price of
  keeping headings with their content.
- A paragraph of two or three lines that does not fit moves whole, which can leave up to two
  lines of space empty.
- Code blocks still break between any two lines, as planned.
