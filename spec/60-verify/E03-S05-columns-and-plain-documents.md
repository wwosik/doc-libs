# E03-S05 Columns and plain documents (invoices)

> Status: in verification
> Epic: [E03 PDF layout](../50-todo/E03-pdf-layout.md)

## Summary

Two PDF features asked for by `faktury` (invoice PDFs, its E03-S01): `::: columns` set side by
side in PDFs, and a document kind without a cover page and without the running header — a
one- or two-page business document that starts with its content on page 1.

## Context

- Today in PDFs (`docs/wwosik.DocLibs.DocumentGeneration.Pdf.md`): containers including
  `::: columns` are shown as normal blocks one after another; a document that starts with `#`
  gets a cover; content pages always have a header with the document title and the logo, and a
  footer with the footer text, the date and "n / m".
- `faktury` (decision 2026-11-14) renders every invoice through DocLibs from Markdown: the
  seller and buyer blocks belong side by side, the logo and the brand colour per business, the
  label "Wizualizacja faktury wystawionej w KSeF", no cover, page numbers kept for long
  invoices, table header repeated (already there).

## Proposed approach

1. **Columns in PDF:** `::: columns` with two (later n) child blocks sets them side by side,
   equal width with the brand's gutter; a column that does not fit moves together to the next
   page; content inside a column is the normal block set (paragraphs, lists, small tables).
2. **Plain document:** front matter `layout: plain` (name to be settled) — no cover even when
   the document starts with `#`, the first `#` is set as the title on page 1 (logo beside it),
   no running header; the footer stays (footer text, "n / m"; the date per `ShowDate`).
3. Golden tests: an invoice-like document (two columns, items table over two pages), a plain
   document with and without logo; existing documents unchanged.
4. Guide (`docs/…Pdf.md`, `…DocumentGeneration.md`) and `CHANGELOG.md`.

## Acceptance criteria

- [x] `::: columns` with two blocks renders them side by side in the PDF.
- [x] A `layout: plain` document has no cover and no header; its first page starts with the
      title and the logo.
- [x] Existing decks and PDFs do not change.

## Open questions

1. DOCX: should the plain layout and columns apply there too in the same story, or PDF only
   (faktury needs only PDF)?

> Answered by the human (2026-11-14): PDF only. DOCX later, as its own story, if needed.

## Notes

## Implementation notes

### Breaking changes

- **`::: columns` in PDFs is set side by side** instead of one part after the other (plan step 1). A
  PDF with `::: columns` looks and paginates differently; `layouts-neutral.pdf` (the deck sample
  `layouts.md`, "Two views") is the only test output that changed, and its round trip through
  `PdfConverter` now reads the two columns line by line, interleaved (expected file regenerated).
  This is the one exception to "existing PDFs do not change", and it is what criterion 1 asks for.
- **`::: columns` with the wrong number of parts now throws in PDFs** (one part, more than three, an
  empty part, a ratio that does not match the parts), with the decks' message; PDFs used to accept
  them. Decks and PDFs now share the check (`LayoutSyntax.Columns`); deck messages are unchanged.
- **PDF authoring guide:** new section "Plain documents and columns", a `layout` row, one sentence
  each on contents and the structure table; the "Syntax written for slides" paragraph no longer says
  columns follow one another. `tool-guide.expected.md` changed with it.
- Everything else is additive: `PdfOptions.Layout`, `PdfLayout { Report, Plain }`, front matter
  `layout: report|plain`.

### Decisions

- **Human decision (2026-11-15): the PDF authoring-guide word limit goes from 2,500 to 3,500**
  (`Guide_StaysUnder3500Words`, `words < 3500`). The PPTX limit (3,500) is unchanged. The PDF guide is
  now at about 2,900 (neutral) and 3,000 (sample brand) words by `wc`.
- **Name:** front matter `layout`, values `report` (default, the old behaviour) and `plain`;
  `PdfOptions.Layout` / `PdfLayout`. PDF only, as the human answered: `layout` is **not** added to
  `FrontMatter.DocumentKeys`, so decks and Word documents reject it as an unknown key (no deck or DOCX
  change). The DOCX story can add it there later.
- **Columns:** two or three parts (as decks), widths from the ratio, an **18 pt gap** (the brand has no
  gutter; image rows use 12 pt, which is tight for text columns). Top-aligned; what follows starts
  under the tallest. Any block works inside a column, at the column's width.
- **"Moves together":** each column is laid out on the real page with the bottom lifted
  (`unbounded`), the tallest is checked against the page's bottom less its footnotes, and a miss is
  undone (`Save`/`Restore`) and tried once more at the top of a new page. A heading right above the
  columns is kept with all of them (`KeepHeight` measures the columns the same way, capped at a page).
- **Fallback (not in the plan):** columns taller than a page, or holding a block that starts a page
  (`#`, `{.newpage}`, `::: pagebreak`, at any depth), are set one after another at the full width, as
  before, with no note. Splitting columns across pages is out of scope.
- **Plain document:** no cover even with a leading `#`; the `#` is the title at the top of page 1,
  set like a `#` heading (20 pt bold, brand colour); the paragraphs after it are body text (no
  subtitle). The logo stands top right, top-aligned with the title, 36 pt high and at most 30 % of the
  text width; the title wraps short of it (12 pt gap). Without a leading `#` there is no title and no
  logo. No running header on any page; the footer (footer text, date per `ShowDate`, "n / m") stays,
  page 1 included. Cover artwork and the tagline are not used. Later `#` headings still start pages;
  `{.newpage}` on the title throws with its own message.
- **Classification marking in a plain document:** right-aligned at the right margin at the header's
  baseline (38 pt) on every page, as a header without a logo has it (8 pt bold, danger colour for
  confidential); the draft watermark as before. There is no cover to mark.
- **Contents in a plain document:** right after the title on page 1, and the body follows on the same
  page (not on a page of its own).
- **Footnotes, bookmarks, anchors:** unchanged; the title is the top bookmark and its anchor's target,
  footnotes in columns go to the foot of the page. Inside a quote the quote's rule is drawn once along
  the tallest column.

### Verification

- `./run.sh build` hit "Internal CLR error" once; after `dotnet build-server shutdown`,
  `dotnet build src/wwosik.DocLibs.slnx -m:1`: 0 warnings, 0 errors. Tests per project, all green:
  35 + 123 + 1 (6 Windows-only skipped) + 78 + 71 + 161 + 323 + 525 + 466 = 1,783 passed.
- **New tests** `Pdf.Tests/ColumnsAndPlainDocumentsTests.cs` (23 cases): two columns side by side,
  equal widths and the gap; a ratio and wrapping inside the column; three columns and the next block
  under the tallest; columns moving to the next page together; taller than a page and with a page
  break: one after another; a heading moving with its columns; a footnote in a column; a quote's rule
  once; the parser's split; the three errors as in decks; a plain document without cover and header,
  title and logo on page 1, footer kept, anchor and bookmark; title wrapping short of the logo; the
  marking and watermark on every page; `layout: plain` end to end (no subtitle, one page); contents
  after the title; `layout: report` the same as no key; an unknown value; `{.newpage}` on the title;
  and the golden **invoice** (`Samples/invoice.md`: plain, two column blocks, a footnote, a 45-row
  items table over two pages) for the neutral (no logo) and sample (logo) brands, with round-trip
  expectations. `SlideLayoutSyntaxTests.Columns_StandSideBySide_WithoutRules` replaces the old
  "follow one another" test.
- **Unchanged output:** all 218 files the tests write to `.tmp/review/`, from `dac2898` before any
  change and from this change: identical but `layouts-neutral.pdf` (it has `::: columns`, see above)
  — PDFs byte for byte with `/ID` and dates masked, decks and Word documents part by part with
  relationship ids masked, embedded workbooks included. New: `invoice-neutral.pdf`, `invoice-sample.pdf`.

### To verify by hand (no veraPDF or PDF viewer here)

1. **veraPDF** (PDF/A-2b) on `.tmp/review/invoice-neutral.pdf` and `invoice-sample.pdf`.
2. **In a viewer:** seller and buyer side by side, the 2:1 row, the items table continuing on page 2
   with its header row, the footnote at the foot of page 1, the logo beside the title on
   `invoice-sample.pdf`, no header on either page, "1 / 2" and "2 / 2" in the footer; and the
   "Two views" columns in `layouts-neutral.pdf`. Nothing was rendered visually here, only checked by
   layout tests and extraction.
3. Whether 18 pt between columns and a 36 pt logo look right for the invoice app.

### Loose ends

- `PdfConverter` reads side-by-side columns line by line (interleaved); detecting columns is E08 work.
- DOCX: plain layout and columns side by side, as their own story if needed (Word documents still show
  columns one after another).
- No note when columns fall back to one after another.
