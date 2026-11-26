# E05 Authoring feedback

> Status: in progress

## Goal

Whoever writes the Markdown, a person or an AI agent through a host app, learns what is possible and what went wrong: a guide generated from the caller's options, one exception type, notes about what the generator did, and a check before generating.

## Why

The writer cannot see the generator's decisions in the output; telling them closes the loop so they fix the source.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E05-S01](../60-verify/E05-S01-renderer-errors-as-documentconversionexception.md) | Renderer errors as DocumentConversionException | in verification |
| [E05-S02](../60-verify/E05-S02-authoring-guides-for-markdown-writers.md) | Authoring guides for Markdown writers | in verification |
| [E05-S03](../60-verify/E05-S03-generation-results-and-notes.md) | Generation results and notes | in verification |
| [E05-S04](../60-verify/E05-S04-check.md) | Check | in verification |
| [E05-S05](../60-verify/E05-S05-ai-tool-description.md) | AI tool description | in verification |
| [E05-S06](../55-in-progress/port-1/E05-S06-contact-sheet-in-the-tool-reply.md) | Contact sheet in the tool reply | in progress (port-1) |

Stories marked *to plan* come from the
[2026-11-13 analysis](../10-analysis/2026-11-13%20feature-plan.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- `Generate` returns a `GenerationResult` with notes; there is no strict mode, the host decides what to do with them (E05-S03).

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
