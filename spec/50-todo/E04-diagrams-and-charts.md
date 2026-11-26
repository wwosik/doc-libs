# E04 Diagrams and charts

> Status: in progress

## Goal

Graphics drawn from the Markdown (diagrams, charts, visual blocks) in the brand's colours and fonts: native shapes and charts in decks, vectors in PDFs, SVG and PNG where those are needed.

## Why

Writers, often AI agents, describe a graphic in text; the generators draw it, so no one has to produce and keep images by hand.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E04-S01](../60-verify/E04-S01-mermaid-diagrams.md) | Mermaid diagrams | in verification |
| [E04-S02](../60-verify/E04-S02-native-charts.md) | Native charts | in verification |
| [E04-S03](../60-verify/E04-S03-financial-charts.md) | Financial charts | in verification |
| [E04-S04](../60-verify/E04-S04-svg-to-png.md) | SVG to PNG | in verification |
| [E04-S05](../60-verify/E04-S05-chart-source-and-image-attributes.md) | Chart source, semicolon CSV and image attributes | in verification |
| [E04-S06](../60-verify/E04-S06-qr-codes-and-icons.md) | QR codes and icons | in verification |
| [E04-S07](../60-verify/E04-S07-timeline-and-gantt.md) | Timeline and Gantt | in verification |
| [E04-S08](../60-verify/E04-S08-steps-harvey-balls-and-risk-matrix.md) | Steps, Harvey balls and risk matrix | in verification |

Stories marked *to plan* come from the
[2026-11-13 analysis](../10-analysis/2026-11-13%20feature-plan.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- Diagrams are our own vector `Drawing`, never images (E04-S01).
- SkiaSharp may be used for image processing in the generators (the human's decision, 2026-11-13).

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
