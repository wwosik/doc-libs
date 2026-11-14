# E02 Deck layouts

> Status: in progress

## Goal

Deck Markdown can ask for more than title plus bullets, and whatever it asks for stays on the slide: layouts, images, tables and slide kinds that fit the content area.

## Why

Management decks are the main use of the PPTX generator; their content has to land on readable slides without hand-editing in PowerPoint.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E02-S01](../60-verify/E02-S01-slide-layouts-from-markdown.md) | Slide layouts from Markdown | in verification |
| [E02-S02](../60-verify/E02-S02-table-column-widths.md) | Table column widths | in verification |
| [E02-S03](../60-verify/E02-S03-images-on-slides.md) | Images on slides | in verification |
| [E02-S04](../60-verify/E02-S04-nothing-runs-off-the-slide.md) | Nothing runs off the slide | in verification |
| [E02-S05](../55-in-progress/doc-3/E02-S05-slide-kinds.md) | Slide kinds | in progress (doc-3) |

Stories marked *to plan* come from the
[Python fork analysis](../10-analysis/2026-11-13%20python-fork-port.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- Nothing runs off the slide: content is shrunk within limits, then continued on further slides with the same title (E02-S04).
- Each new slide kind needs its layout role from E01-S06.

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
