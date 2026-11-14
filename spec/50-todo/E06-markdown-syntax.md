# E06 Markdown syntax

> Status: in progress

## Goal

The Markdown the generators understand: inline and block syntax that works the same in every output format, and the generated texts in the document's language.

## Why

One syntax for every format means a writer learns it once and the same source goes to a deck, a PDF and later a Word document.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E06-S01](../60-verify/E06-S01-coloured-words.md) | Coloured words | in verification |
| [E06-S02](../60-verify/E06-S02-markdown-foundations.md) | Markdown foundations: footnotes, task lists, definition lists, document language (`lang`) with English and German generated-text tables, `Drawing` even-odd fill and validation | in verification |
| [E06-S03](E06-S03-typography.md) | Typography | todo |
| [E06-S04](E06-S04-code-highlighting.md) | Code highlighting | todo |

Stories marked *to plan* come from the
[Python fork analysis](../10-analysis/2026-11-13%20python-fork-port.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- Raw HTML stays text and is reported (`html-as-text`); it is not dropped (the human's decision, 2026-11-13).
- Front-matter keys and values are English; other languages come as data.
- A missing italic face stays an error; no synthetic italic.

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
