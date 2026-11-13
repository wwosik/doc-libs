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
| E03-S02 | PDF additions shared with DOCX: contents with page numbers, heading numbers, captions "Figure n" / "Table n", footnotes at the page foot, justified text, page breaks, classification marking in all formats | to plan |

Stories marked *to plan* come from the
[Python fork analysis](../10-analysis/2026-11-13%20python-fork-port.md); each is written and agreed
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
