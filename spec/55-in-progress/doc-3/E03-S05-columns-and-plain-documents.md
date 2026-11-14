# E03-S05 Columns and plain documents (invoices)

> Status: in progress (doc-3)
> Epic: [E03 PDF layout](../../50-todo/E03-pdf-layout.md)

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

- [ ] `::: columns` with two blocks renders them side by side in the PDF.
- [ ] A `layout: plain` document has no cover and no header; its first page starts with the
      title and the logo.
- [ ] Existing decks and PDFs do not change.

## Open questions

1. DOCX: should the plain layout and columns apply there too in the same story, or PDF only
   (faktury needs only PDF)?

> Answered by the human (2026-11-14): PDF only. DOCX later, as its own story, if needed.

## Notes
