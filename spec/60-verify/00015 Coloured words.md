# 00015 Coloured words

> Status: verify — implemented 2026-10-25

## Summary

Writers can colour a few words in running text with the brand's status colours — e.g. "delivered"
in the success colour and "watch closely" in the warning colour — in decks and PDFs.

## Context

- Status labels (00005) colour a whole table cell by category (`labels.<category>` front matter,
  `BrandColors.Labels`: success, info, warning, danger, neutral). Running text has no colour of its
  own; `==marked==` is the only emphasis beyond bold and italic, and it uses the highlight colour.
- Markdig parses `[text]{.class}` (Pandoc's bracketed span) only with generic attributes switched
  on, which would also take `{…}` after links, headings and code — the reason 00005 parses heading
  attributes itself.

## Proposed approach

1. **Syntax:** `[words]{.success}`, `.info`, `.warning`, `.danger`, `.neutral` — a bracketed span with
   exactly one category class, parsed by our own inline parser (not Markdig's generic attributes).
   Anything else in braces after brackets stays text, as today. An unknown category in that exact
   shape (`[x]{.ok}`) throws, naming the valid ones.
2. **Decks:** the words in the category's colour, bold; on dark slides (00010) the colour as given by
   the slide colours, checked for contrast like the rest of the dark palette.
3. **PDFs:** the words in the category's colour (PDF colours), bold.
4. **Plain preview:** `[words]{.success}` reads acceptably as plain text; documented.
5. Tests (decks and PDFs, every category, unknown category, braces that are not a span), the
   authoring guides (one line with an example), docs.

## Acceptance criteria

- [ ] `[words]{.<category>}` colours the words in decks and PDFs with the brand's label colours.
- [ ] Unknown categories throw; other braces stay text.
- [ ] Tests, docs and authoring guides updated.

## Breaking changes

- Markdown with `[text]{.success}`-shaped spans (exactly that shape) now renders coloured instead of
  literally; `[x]{.other}` now throws.

## Open questions

1. Bold as well as coloured (proposed: colour alone is easy to miss), or colour only?

> Answered by the human (2026-10-25): coloured and bold.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- `ColouredWords` (shared) finds `[words]{.category}` in a paragraph's runs after Markdig, with code
  and line breaks masked, and cuts the runs; both parsers call it for paragraphs, titles and cells.
- Decks colour the words with the brand's label colours, bold; on dark slides lightened halfway to
  white (`SlidePalette.WordColour`), which keeps them at a contrast of 4.5 or more (in the palette
  test). PDFs colour the pieces of a line (`Piece.Colour`).
- Tests: decks (colours, bold, brackets gone, other braces stay text, unknown category, dark slides)
  and PDFs (letter colour).

### Breaking changes

- `[words]{.success}`-shaped spans now render coloured; an unknown category throws. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
