# E07 Presenting

> Status: todo

## Goal

Decks that are presented, not just read: speaker notes, builds, transitions, scenes, and the deck as an HTML page and as preview images.

## Why

A generated deck should be ready to present without opening PowerPoint to add notes and animations, and a host should be able to show it without Office.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| E07-S01 | Speaker notes, builds, transitions: `::: notes` (repeated on continuation slides), a notes master for the built-in master, `{.build}` and its variants, `transition:`; the PPTX converter reads notes back | to plan |
| E07-S02 | HTML deck: one self-contained file drawn from the generated PPTX | to plan |
| E07-S03 | Preview images: each slide as PNG and a contact sheet, painted from the PPTX with SkiaSharp | to plan |
| E07-S04 | Scenes: `::: scene` with named shapes on a 160 × 90 grid over the whole slide, steps as builds, Morph between slides, a still image in PDF | to plan |

Stories marked *to plan* come from the
[Python fork analysis](../10-analysis/2026-11-13%20python-fork-port.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- Speaker notes are repeated on every part of a split slide (the human's decision, 2026-11-13).
- Previews are painted with SkiaSharp, not rendered in a browser.
- The scene grid is 160 × 90 over the whole slide, not the content area.

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
