# E03 PDF layout

> Status: in progress

## Goal

The PDF generator sets long documents the way a reader expects: headings kept with their content, and the furniture of a report (contents, numbers, captions, footnotes).

## Why

PDF/A reports are the second output of the generators; they are read on paper and screen and have to look typeset.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E03-S01](../60-verify/E03-S01-keep-headings-with-their-content.md) | Keep headings with their content | in verification |
| [E03-S02](../60-verify/E03-S02-contents-heading-numbers-and-captions.md) | Contents, heading numbers and captions | in verification |
| [E03-S03](../60-verify/E03-S03-footnotes-justification-and-page-breaks.md) | Footnotes at the page foot, justified text, page breaks | in verification |
| [E03-S04](../60-verify/E03-S04-classification-marking.md) | Classification marking | in verification |
| [E03-S05](../60-verify/E03-S05-columns-and-plain-documents.md) | Columns and plain documents (invoices) | in verification |
| [E03-S06](../55-in-progress/port-1/E03-S06-dark-brands-in-pdfs.md) | Dark brands in PDFs | in progress (port-1) |

Stories marked *to plan* come from the
[2026-11-13 analysis](../10-analysis/2026-11-13%20feature-plan.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- The PDF takes its look from the `Brand`, never from a Word template.
- A character the fonts lack stays an exception unless `PdfOptions.ReplaceUnsupported` is set (the human's decision, 2026-11-13).
- Features shared with DOCX are written once for both generators where the model allows it.

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
