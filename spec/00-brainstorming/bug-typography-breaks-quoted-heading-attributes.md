# Bug: `typography: true` breaks quoted heading attributes in decks

Reported 2026-11-15 against 2611.15.8 by a consumer upgrading from 2610.25.18.

## What happens

With `typography: true` in the front matter (or `PptxOptions.Typography`), a `##` heading whose
attributes hold a quoted value keeps the whole block as title text, with its quotes curled, and
the attributes are not applied:

```markdown
---
typography: true
---

# Deck

## Status {kicker="Plan"}

- One
- Two

## Results {transition="fade"}

- Three

## Unnumbered {.unnumbered}

Text.
```

| Heading | `typography: false` | `typography: true` |
| ------- | ------------------- | ------------------ |
| `## Status {kicker="Plan"}` | title `Status`, kicker `Plan` | title `Status {kicker=”Plan”}`, no kicker |
| `## Results {transition="fade"}` | title `Results`, fade | title `Results {transition=”fade”}`, no transition |
| `## Unnumbered {.unnumbered}` | title `Unnumbered` | title `Unnumbered` (classes only: fine) |

The same holds for `layout="…"`, so a `{layout="…"}` slide falls back to the default layout. In a
real deck the longer titles also changed the layout of the slides they head: a plain content
slide with a four-item task list and a footnote was split into four slides (`slide-split`,
`slide-overfull`), and fitted on one with `typography: false`.

`Check` reports none of this, since the heading is valid text.

## Why

`DeckParser.Attributes(title)` takes the attributes off the title's runs after `Runs()` has
typeset them, so the straight `"` are already `“ ”`. `HeadingAttributes.BlockPattern` and
`TokenPattern` match only straight quotes, so the block no longer counts as attributes.

## Expected

Heading attributes are read before typography is applied (or typography leaves a trailing
attribute block alone), so a deck generates the same slides with and without `typography: true`.

## Worth checking alongside

- `HeadingAttributes.SplitDeep` (`###` and deeper) and the PDF and Word parsers, if they also split
  attributes after typesetting. Only decks were verified.
- Other places where quoted `key="value"` follows typeset text: image attributes, build-marker
  paragraphs (`{width="50%"}`), container arguments.
- A test that runs the attribute cases of the deck tests with typography on.
