# E07-S07 Slide lines for editor previews

> Status: in verification
> Epic: [E07 Presenting](../50-todo/E07-presenting.md)

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

- [x] `SlideLines` is right for a sample with splits, dividers, a closing slide and front matter, both
      from `PptxGenerator` and from `HtmlDeck.Write` on its output.
- [x] A `go` message moves the deck and its presenter window; a playwright test covers it.
- [x] Decks without the new attribute (any other `.pptx`) still draw; `SlideLines` is `null` for them.

## Open questions

1. Should the deck also report the slide under the mouse (`kind: 'shown'`), so the editor can jump
   back to the Markdown?

> Yes: the deck reports the shown slide with `kind: 'shown'`. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

Implemented by port-1 on 2026-11-26.

### Breaking changes

None. Every generated slide's XML gains one `p:extLst/p:ext` (below), which viewers skip: slides look
the same, and the files are a few bytes per slide larger. `GenerationResult` gains a property; its
equality now also compares `SlideLines`. The HTML deck's sections gain `data-line`, and the script two
message kinds; nothing else in the page changes.

### What was built

- **`GenerationResult.SlideLines`** (`IReadOnlyList<int>?`, init-only, shared package): `null` unless set.
  `GenerationNotes.Result(pages, slideLines)` takes it as an optional argument, so PDFs and Word stay `null`
  without a change.
- **`PptxGenerator.Generate`**: `DeckWriter` collects `slide.Line` for every slide it writes (split parts
  included, so they repeat their source slide's line) into `SlideLines`, and writes it into the slide through
  `SourceLines.Write` after the transition and timing, as the schema orders `p:extLst` last. Every slide
  comes from a heading (`DeckParser.Section` sets the line on all slides of a section), so cover, dividers,
  agenda and closing slides all have their heading's line. `Check` does not return lines (it returns a
  `CheckResult`, unchanged).
- **The extension**: `<p:ext uri="{0DC58704-E0A8-4DBD-9516-3D8E211A6220}"><dl:source line="12"/></p:ext>`,
  namespace `https://github.com/wwosik/doc-libs/pptx/source` (`SourceLines`). The Open XML validator
  (Microsoft 365) accepts it.
- **`HtmlDeck.Write`**: `DeckReader` reads the line into `SlideScene.Line` (0 when absent or not a positive
  number); the result's `SlideLines` is the list of every visible slide's line when at least one slide has
  one, else `null`. Each section with a line gets `data-line` after its `aria-label`.
- **`deck.js`**: `go` (`slide` from 1, a whole number within the deck, else ignored) calls `go(n - 1, all
  steps)` without an effect or transition, which sends `state` as every other move does, so the presenter
  window follows over the existing channel. `shown` (`slide` from 1, `line`, 0 without one) is sent by the
  deck — not the presenter window — whenever the current slide changes, whatever moved it. A deck in an
  `iframe` also posts its messages to `window.parent` (the *embedder*), which never becomes the presenter
  `peer`, so `P` still opens the presenter window after an editor has sent `go`.
- **Docs**: *Editor previews* in the PPTX page's HTML deck part (the lines, the extension, the four message
  kinds in a table, a small editor page with an `iframe`); `SlideLines` in the shared page's
  `GenerationResult` list and in the Markdown-to-HTML example. `CHANGELOG.md` under *Unreleased* → *New*.

### Decisions and deviations

- The story left the storage open (an attribute of its own on the slide, or the name extension): an
  extension in the slide's `p:extLst` is the place the format reserves for this, and it needs no
  `mc:Ignorable` markup.
- A deck with lines on some slides only (slides added to a generated deck by hand) gives 0 for the others,
  not `null`; `null` only when no slide has a line.
- `shown` reports `line: 0` for a slide without a line, matching `GenerationNote.Line`'s "0 if unknown".
  It is not sent when the page loads, only on a change; an editor asks with `hello` or `go`.
- `go` to the slide already shown answers `state` only (no `shown`: the slide did not change).
- Slides in `state` stay 0-based as before, while `go` and `shown` count from 1 as the story says; the docs
  table says so.
- The "slide under the mouse" of the open question is read as the shown slide, as the answer says; the
  overview's selection does not send `shown`.
- Tests that compare two decks, one with front matter, now ignore the line values
  (`PresentingTests.Parts`) or the extension (`SlideLayoutTests`), because front matter moves the lines.

### Verification

- xUnit (`SlideLineTests`, 7 tests): lines of a sample with front matter, a cover, a divider, a split slide,
  a slide with notes and a closing slide, from `Generate` and from `HtmlDeck.Write` on its output; the
  `data-line` attributes; the extension valid and in place; a deck with the extension removed from every
  slide (draws, `SlideLines` `null`, no `data-line`) and from one slide (0 there); result equality.
  The whole solution's tests pass.
- **Chromium** (Playwright, headless; throwaway scripts in `.tmp/e07s07/`, not a dependency, as for
  E07-S06) on `presenting.md`, from disk, the deck in an `iframe` of an editor page: `go` 5 moves the deck
  and answers `state` then `shown` with line 63; `go` 0, 99, 2.5, `'3'` and `null` are ignored; `go` to the
  current slide answers `state` only; a click and a key send `shown`; `P` inside the frame opens the
  presenter window, which follows `go` 3 (*Slide 3 of 19*); `End` in the presenter window moves the deck,
  which sends `shown` 19 with line 235 to the editor; `go` 13 shows the slide after all seven steps
  (`#13.7`, only the exit's item hidden). The same deck in an `iframe` with `srcdoc` answers `go` too.

### Open verification items (for the human)

- Other browsers (Firefox, Safari), and an editor on another origin than the deck.
- That PowerPoint opens, edits and saves a generated deck without complaint about the extension (no
  Office here). PowerPoint may drop the extension when it saves; the lines are then gone, which is
  the documented `null` case.

### Loose ends

- `PptxGenerator.Check` could return the lines as well, for an editor that checks rather than generates.
