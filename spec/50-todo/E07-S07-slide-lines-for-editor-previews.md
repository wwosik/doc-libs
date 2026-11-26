# E07-S07 Slide lines for editor previews

> Status: todo
> Epic: [E07 Presenting](E07-presenting.md)

## Summary

An editor that shows an HTML deck beside its Markdown can follow the cursor. The generation result
says on which Markdown line each slide starts, and the deck accepts a message that jumps to a slide.

## Context

- **`GenerationResult`** (`src/wwosik.DocLibs.DocumentGeneration/GenerationResult.cs`) has `Notes` and
  `Pages`, the number of slides or pages.
- **HTML decks** are drawn from the generated `.pptx`: `HtmlDeck.Write(pptx, html, options)`
  (`src/wwosik.DocLibs.DocumentGeneration.Pptx/HtmlDeck.cs`). So the PPTX generator knows the lines,
  and the HTML deck only knows the slides.
- **The deck runtime** (`HtmlDeck/deck.js`, about lines 555–615) already speaks a protocol with a
  presenter window, over `postMessage` to its opener and a `BroadcastChannel`:
  - messages carry `app: 'html-deck'`;
  - `kind: 'hello'` asks for the state, and `kind: 'state'` (`slide`, `step`) reports or sets it.

## Proposed approach

1. **The lines.** `GenerationResult.SlideLines`: for a deck, the Markdown line (from 1, front matter
   included, counted like note lines) each written slide starts at. Continuation slides get their
   source slide's line. It is `null` for PDFs and Word.
2. **Keeping them in the deck.** `PptxGenerator` records them. `HtmlDeck.Write` cannot see the
   Markdown, so the PPTX generator stores each slide's line in an ignorable attribute of its own on the
   slide (or in the slide's name extension), and `HtmlDeck.Write` returns it in its result's
   `SlideLines`.
3. **The jump message.** `{ app: 'html-deck', kind: 'go', slide: n }` (from 1) jumps to slide n
   without an effect, with every build of the slide shown, and answers with `kind: 'state'`. Unknown
   slides are ignored. It goes through the same receiver as `state`, so a presenter window follows too.
4. **The shown slide.** When the slide changes (keys, clicks or `go`), the deck sends
   `{ app: 'html-deck', kind: 'shown', slide: n, line: l }`, so an editor can jump back to the
   Markdown line.
5. **Docs.** The HTML deck section describes the protocol: `hello`, `state`, `go` and `shown`. A small sample
   page shows an editor `iframe` driving the deck.

## Acceptance criteria

- [ ] `SlideLines` is right for a sample with splits, dividers, a closing slide and front matter, both
      from `PptxGenerator` and from `HtmlDeck.Write` on its output.
- [ ] A `go` message moves the deck and its presenter window; a playwright test covers it.
- [ ] Decks without the new attribute (any other `.pptx`) still draw; `SlideLines` is `null` for them.

## Open questions

1. Should the deck also report the slide under the mouse (`kind: 'shown'`), so the editor can jump
   back to the Markdown?

> Yes: the deck reports the shown slide with `kind: 'shown'`. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
