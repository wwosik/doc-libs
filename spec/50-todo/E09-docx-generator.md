# E09 DOCX generator

> Status: todo

## Goal

A new package that writes Markdown to Word: a built-in template written from the brand, caller templates with a role to style-name map, the deck syntax in Word, charts and drawings.

## Why

Word is the format most reports and proposals still have to be delivered in; the generators cover decks and PDF only.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| E09-S01 | DOCX generator: built-in template from `ResolvedBrand`, caller templates with role → style-name map, content | to plan |
| E09-S02 | Deck syntax in Word: containers in sequence, alerts as note styles, `==mark==`, coloured words, status labels, image rows | to plan |
| E09-S03 | Charts, drawings (SVG + PNG) and the DOCX authoring guide | to plan |

Stories marked *to plan* come from the
[Python fork analysis](../10-analysis/2026-11-13%20python-fork-port.md); each is written and agreed
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
