# E07 Presenting

> Status: todo

## Goal

Decks that are presented, not just read: speaker notes, builds, transitions, scenes, and the deck as an HTML page and as preview images.

## Why

A generated deck should be ready to present without opening PowerPoint to add notes and animations, and a host should be able to show it without Office.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E07-S01](../60-verify/E07-S01-speaker-notes-and-transitions.md) | Speaker notes and transitions | in verification |
| [E07-S02](../60-verify/E07-S02-html-deck.md) | HTML deck | in verification |
| [E07-S03](../55-in-progress/doc-3/E07-S03-preview-images.md) | Preview images | in progress (doc-3) |
| [E07-S04](../55-in-progress/doc-3/E07-S04-scenes.md) | Scenes | in progress (doc-3) |
| [E07-S05](../60-verify/E07-S05-builds.md) | Builds | in verification |
| [E07-S06](../55-in-progress/doc-3/E07-S06-presenting-the-html-deck.md) | Presenting the HTML deck | in progress (doc-3) |

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
