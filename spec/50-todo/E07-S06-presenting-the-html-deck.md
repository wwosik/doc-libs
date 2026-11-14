# E07-S06 Presenting the HTML deck

> Status: todo
> Epic: [E07 Presenting](E07-presenting.md)

## Summary

The HTML deck of E07-S02 becomes something to present from: keys step through the builds of
E07-S05, slides change with the transitions of E07-S01, a notes view shows the speaker notes beside
the slide, and a presenter window shows the current slide, the next step, the notes and a timer.
Everything stays in the one self-contained file.

## Context

- **Depends on E07-S02, E07-S01 and E07-S05.**
  - **E07-S02** (HTML deck) gives the reader (`DeckReader`, `DeckScene`, `SlideScene`), one SVG per
    slide (`SceneSvg`) and the static page (`HtmlDeckWriter`, `HtmlDeck/deck.js`,
    `HtmlDeck/deck.css`): slides, keys, overview, black screen, print, CSP and fonts. Every item in
    the SVG is a `<g>` with `data-id` (its `cNvPr id`) and every paragraph a `<g>` with `data-p`,
    so this story can show and hide them without touching the drawing. Its keys move by slide and
    its URL hash is `#7`.
  - **E07-S01** writes speaker notes (`notesSlide` parts, a notes master for the built-in master,
    `::: notes` in Markdown) and transitions (`p:transition`, Morph as `p159:morph` in
    `mc:AlternateContent` with a fade fallback).
  - **E07-S05** writes builds (`p:timing`): the main click sequence with entrances, exits and
    emphasis on shapes and paragraphs, and their effects.
- **E07-S04** (scenes) uses E07-S05's builds and E07-S01's Morph; its shapes carry the same names
  across slides. Moving matched shapes in HTML is E07-S04's (question 2).
- **Decided:** in the analysis, an HTML deck with keys, a notes view and print. The split of the
  HTML deck into E07-S02 and this story was E07-S02's question 7.

## Proposed approach

1. **Reader additions** (in E07-S02's `DeckReader`, namespace `…Pptx.Rendering`).
   - Each `SlideScene` gains the speaker notes, the build steps and the transition.
   - **Notes:** the body placeholder of the slide's `notesSlide`, as paragraphs with bold, italic,
     links (`http`, `https`, `mailto` only) and list levels; the slide image placeholder and the
     notes master's decorations are ignored.
   - **Builds:** the reader turns `p:timing`'s click sequence into steps; each step shows, hides or
     emphasizes items (by `cNvPr id`) and paragraphs (by index). Anything after a click that the
     reader does not know is treated as appear and reported with `drawing-unsupported`.
   - **Transitions:** the kind, direction and speed from `p:transition` (`spd`, `p14:dur`), and for
     `mc:AlternateContent` the first `Choice` the reader knows, else the `Fallback`.
2. **API.** `HtmlDeckOptions` gains `Notes`: default `true`; `false` leaves the speaker notes out of
   the file (a deck handed to an audience).
3. **The page** (extending `HtmlDeckWriter`, `deck.js` and `deck.css`):
   - **Keys:** next and previous move by build step, and to the next or previous slide after the
     last or before the first step. `N` notes view; `P` presenter window. The URL hash follows the
     step (`#7`, `#7.2` for a build step), so reload and links land on the same step.
   - **Builds:** each step shows, hides or emphasizes items and paragraphs. Effects map to CSS:
     appear (instant), fade, fly in from the effect's direction, wipe (`clip-path`), exit as the
     reverse, emphasis as a short scale pulse; anything else is appear. Going back reverses a step
     without animation.
   - **Transitions:** fade, push, wipe, cover, uncover, split and zoom as CSS animations of the
     section; cut and none instant; Morph as fade (see question 2). Speeds from `spd`/`dur`.
     `prefers-reduced-motion` turns them all into instant changes.
   - **Notes view** (`N`): the slide shrinks to the left two thirds and its speaker notes show beside
     it, as HTML paragraphs with bold, italic, links and lists. Without notes it says so.
   - **Presenter window** (`P`): opens the same file with `#presenter`, synced over
     `BroadcastChannel` (falling back to `postMessage` between the two windows when the file is
     opened from disk): current slide, next step, notes, slide number of total, elapsed time with a
     reset. Keys work in both windows.
   - **Overview:** shows each slide in its final build state.
   - **Print:** each slide in its final build state, transitions hidden; with the notes view on,
     each sheet has the slide on top and its notes below.
   - The CSP stays as in E07-S02: the script remains one inline `<script>` allowed by its hash.
4. **Coverage.** E07-S02's coverage test runs over the sample decks with notes, builds and
   transitions from E07-S01 and E07-S05 and fails on any `drawing-unsupported` note.
5. **Tests.**
   - **Model:** notes read from `notesSlide` (formatting, lists, a slide without notes); click
     sequences of E07-S05's samples become the expected steps (entrance, exit, emphasis, paragraph
     builds, several items on one click); transitions with kind, direction and speed; Morph read
     through `mc:AlternateContent`.
   - **HTML:** notes are escaped like slide text (a note `</script><img onerror>` stays text);
     `Notes = false` leaves no note text in the file; each step's items are addressable by
     `data-id` and `data-p`.
   - The script's key handling, effects and window sync are not unit-tested; they are checked by
     hand in browsers (below).
6. **Browser check.** Open the HTML file of the E07-S01, E07-S04 and E07-S05 samples in current
   Chrome, Firefox, Edge and Safari (or WebKit), from disk and from a web server: stepping forward
   and back, effects, transitions with and without `prefers-reduced-motion`, notes view, presenter
   window and its sync, print to PDF with and without the notes view.
7. **Docs and changelog.**
   - `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md`, section *HTML deck*: builds and the effects
     that play, transitions and Morph as fade, the notes view, the presenter window, the `N` and `P`
     keys, `Notes`, and printing with notes.
   - `CHANGELOG.md` under *New*. No existing deck changes.

## Acceptance criteria

- [ ] Builds step through in order and back, with their effects mapped to CSS; the URL hash names
  the step.
- [ ] Transitions play, Morph plays as a fade, and all respect `prefers-reduced-motion`.
- [ ] Notes from `::: notes` appear in the notes view and the presenter window; `Notes = false`
  leaves them out of the file.
- [ ] The presenter window stays in sync with the main window, from disk and from a server.
- [ ] Keys, notes view, presenter window and print work in Chrome, Firefox, Edge and Safari, from
  disk and from a server.
- [ ] Print and the overview show each slide in its final build state; printing with the notes
  view on puts the notes under each slide.
- [ ] Docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Build effects in HTML: map E07-S05's effects to CSS (appear, fade, fly, wipe, exit, a pulse for
   emphasis; proposed); or show steps instantly with no effects?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Morph in HTML: fade for now, and E07-S04 adds the movement of shapes matched by name (proposed:
   scenes are E07-S04's); or interpolate matched shapes' position, size and colour here already?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Speaker notes: a notes view beside the slide and a presenter window synced between two windows
   (proposed); or only the notes view in the same window?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- Split out of E07-S02 on 2026-11-14 (E07-S02's question 7). The three questions above were E07-S02's
  questions 7, 8 and 9; their answers are copied unchanged.
