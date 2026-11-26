# E09 DOCX generator

> Status: in progress

## Goal

A new package that writes Markdown to Word: a built-in template written from the brand, caller templates with a role to style-name map, the deck syntax in Word, charts and drawings.

## Why

Word is the format most reports and proposals still have to be delivered in; the generators cover decks and PDF only.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E09-S01](../60-verify/E09-S01-docx-package-and-templates.md) | DOCX package and templates | in verification |
| [E09-S02](../60-verify/E09-S02-deck-syntax-in-word.md) | Deck syntax in Word | in verification |
| [E09-S03](../60-verify/E09-S03-charts-drawings-and-guide.md) | Charts, drawings and the DOCX authoring guide | in verification |
| [E09-S04](../60-verify/E09-S04-docx-content.md) | DOCX content | in verification |
| [E09-S05](../60-verify/E09-S05-image-styles-in-word.md) | Image styles in Word | in verification |
| [E09-S06](../60-verify/E09-S06-word-core-properties.md) | Word core properties | in verification |

Stories marked *to plan* come from the
[2026-11-13 analysis](../10-analysis/2026-11-13%20feature-plan.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- Drawings go into Word as SVG with a PNG fallback (the human's decision, 2026-11-13).
- Paragraphs reference template styles by name only; no direct formatting except in code.

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
