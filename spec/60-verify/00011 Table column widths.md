# 00011 Table column widths

> Status: verify — implemented 2026-10-25

## Summary

Bug: in decks, a table cell that is all `==marked==` (or any short number in a column next to wide
text columns) gets a column so narrow that its text wraps one or two characters per line. Columns
must never be narrower than their longest word, and a table that still does not fit gets a smaller
font before anything wraps mid-word.

## Context

- `DeckWriter.Table` gives each column a share of the width proportional to its longest text
  segment, clamped to 3–60 characters, with no minimum. A long header (`Value this month vs plan`)
  takes a big share; a column of six-digit numbers gets about its character count, at an estimated
  half an em per character.
- The estimate is too low for many fonts: digits, bold text (header rows, `==marked==` cells, which
  are set bold) and wide sans faces run well past half an em. Columns then wrap their content, and
  numbers break inside (`48,2` / `15`). With a highlight fill the break is very visible; without it
  the header words still break (`Forec` / `ast`).
- Reproduction (made-up data):

  ```markdown
  ## Bookings

  | Area | Plan | Actual |
  | --- | --: | --: |
  | Online shop | ==48,215== | 39,870 |
  ```

  With six columns and a long header, on a brand template with a wide sans font, the `Plan` column
  comes out narrower than `48,215` in bold. The fix is checked on the built-in master, `sample` and
  `brand-dark` (00010).
- Row heights come from the same estimate, so the table's height is also wrong, which feeds 00013.

## Proposed approach

1. **Measure words, not characters.** Width per character from a small table of em widths by class
   (digits and uppercase 0.6, lowercase 0.5, punctuation and spaces 0.3), × 1.1 for bold runs (header
   row, `**`, `==`, labels). Used for column widths and row heights.
2. **Minimum width per column** = its longest unbreakable word (a number with its separators, a
   word, a code span, a status label with its padding) plus cell insets. Never wrap inside a word.
3. **Distribute the rest** by today's weights (longest segment), over the room left after the
   minimums.
4. **Too wide even at the minimums:** step the font down (16 → 14 → 12 → 10 pt) until the minimums
   fit; at 10 pt still too wide, let the widest text column wrap between words and break only
   inside its longest word. Never shrink a column below its word at the chosen size.
5. **Highlight a single cell:** a cell that is all `==marked==` and not in the first column keeps
   today's meaning (marked text, bold), and its column is sized like any other. A test pins it.
6. Tests: the reproduction above (column ≥ the bold width of `48,215`), a 6-column table with a
   long header, a table that needs 12 pt, row heights against the new estimate.
7. Docs: the *Tables* line in the PPTX page (widths follow the longest word; the font shrinks to 10 pt
   before words break).

## Acceptance criteria

- [ ] No column is narrower than its longest word; numbers never break inside.
- [ ] The reproduction keeps `48,215` on one line, with only that cell highlighted.
- [ ] Wide tables shrink their font before wrapping; existing expected files change only where a
      column was too narrow before (reviewed).
- [ ] Tests and docs updated.

## Breaking changes

- Column widths and row heights in decks change for tables whose estimate was off. Text is
  unchanged.

## Open questions

1. Smallest table font: 10 pt (proposed) or 12 pt?

> Answered by the human (2026-10-25): 10 pt.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- `TextMetrics` (new): ems per character class (digits 0.6, upper case 0.68, lower case 0.53, narrow
  letters and punctuation 0.3–0.4, wide ones 0.85–0.9), ×1.08 for bold (header row, `**`, `==`,
  labels), greedy wrapping between words.
- `TableLayout`: minimum width per column = longest word + insets; the font steps down by 2 pt to
  10 pt while the minimums do not fit; only then are the columns squeezed. The rest of the width goes
  to the columns by the old weights.
- Tests: `TableLayoutTests` (no column narrower than its longest word on the built-in templates, the
  marked cell alone highlighted, the font stepping down, wrapping).

### Breaking changes

- Column widths and row heights change where the old estimate was off. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
