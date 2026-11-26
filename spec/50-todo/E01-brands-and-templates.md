# E01 Brands and templates

> Status: in progress

## Goal

A client or project brings its own design (colours, logo, fonts, footer text, its own PowerPoint template) to every generator, and the generators work as well on that template as on the built-in master, without anything client-specific living in this repo.

## Why

A deck or PDF is only useful to a consumer if it looks like theirs. Everything about a design is data a caller passes in (`Brand`, brand packs, `PptxBrandTemplate`); this repo only holds the mechanism.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E01-S01](../60-verify/E01-S01-client-brands-for-the-generators.md) | Client brands for the generators | in verification |
| [E01-S02](../60-verify/E01-S02-table-font.md) | Table font | in verification |
| [E01-S03](../60-verify/E01-S03-brand-fonts-embedded-in-decks.md) | Brand fonts embedded in decks | in verification |
| [E01-S04](../60-verify/E01-S04-slides-on-dark-templates.md) | Slides on dark templates | in verification |
| [E01-S05](../60-verify/E01-S05-brand-template-features.md) | Brand template features | in verification |
| [E01-S06](../60-verify/E01-S06-template-roles-for-decks.md) | Template roles for decks | in verification |
| [E01-S07](../60-verify/E01-S07-chapter-numbers-and-layouts-across-placeholders.md) | Chapter numbers and layouts across placeholders | in verification |
| [E01-S08](../60-verify/E01-S08-cover-image.md) | Cover image | in verification |

Stories marked *to plan* come from the
[2026-11-13 analysis](../10-analysis/2026-11-13%20feature-plan.md); each is written and agreed
on its own before it is implemented, and may split into several stories.

## Decisions

- Template roles are brand-pack data with English names; nothing names a client's layouts or placeholder indices.
- Existing decks do not move: every new role is opt-in, and what is not mapped falls back to today's layouts.

## Acceptance

- Every story in the scope table is completed.
- Existing decks and PDFs do not change unless a story says so and records it as a breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
