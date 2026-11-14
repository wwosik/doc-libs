# E07-S06 Presenting the HTML deck

> Status: in verification
> Epic: [E07 Presenting](../50-todo/E07-presenting.md)

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

- [x] Builds step through in order and back, with their effects mapped to CSS; the URL hash names
  the step.
- [x] Transitions play, Morph plays as a fade, and all respect `prefers-reduced-motion`.
- [x] Notes from `::: notes` appear in the notes view and the presenter window; `Notes = false`
  leaves them out of the file.
- [x] The presenter window stays in sync with the main window, from disk and from a server.
- [ ] Keys, notes view, presenter window and print work in Chrome, Firefox, Edge and Safari, from
  disk and from a server.
- [x] Print and the overview show each slide in its final build state; printing with the notes
  view on puts the notes under each slide.
- [x] Docs and CHANGELOG are updated, and all tests pass.

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

## Implementation notes

Implemented by doc-3 on 2026-11-14.

### Breaking changes

None. Decks, PDFs, Word documents and the preview images are unchanged (the 48 golden PNGs of E07-S03
pass as they were: the painting SVG is untouched). The HTML page changes, but the HTML deck of E07-S02 is
itself unreleased (*Unreleased* in `CHANGELOG.md`), so the entry is under *New*.

### What was built

- **Reader** (`Rendering/DeckReader.Presenting.cs`): `SlideScene` gains `Notes`, `Steps`, `Transition` and
  `Decorations` as init properties, so its constructor is unchanged.
  - **Notes:** the `body` placeholder (or an untyped `idx="1"`) of the notes page; runs with `b`, `i` and
    `hlinkClick` (web and mail only, through `PartXml.Link`), `a:br`, fields as text; per paragraph `lvl`,
    `buChar` (bullet) or `buAutoNum` (number, `startAt`). Empty paragraphs are dropped. Only explicit bullets
    count, as E07-S01 writes them; a notes master's own list style is not read.
  - **Steps:** the `mainSeq` of `p:timing`, one step per click `par` (`delay="indefinite"`); a `par` that starts
    by itself joins the step before (the first one is a step of its own). Effects are the `cTn`s with
    `presetClass`; target `spTgt spid` with an optional `txEl/pRg`. Known: entrance and exit presets 1
    (appear), 10 (fade), 2 (fly), 22 (wipe), emphasis 26 (pulse). Others: an entrance plays as appear, an exit
    as disappear, an emphasis as a pulse, each reported as `drawing-unsupported`; motion paths and `verb`
    effects are not played and reported; `mediacall` is ignored; an `interactiveSeq` is reported. The side
    comes from `presetSubtype` (1 top, 2 right, 4 bottom, 8 left, corners 3/6/9/12), the duration from the
    longest inner `dur` (0.5 s if none; appear 0). Effects on ids the slide's own tree does not draw are dropped.
  - **Transitions:** `p:transition`, or in `mc:AlternateContent` the first `Choice` whose transition is known,
    else the last alternative (the `Fallback`). Kinds `fade`, `push`, `wipe`, `cover`, `pull` (as `uncover`),
    `split`, `zoom`, `cut`, `p159:morph`; `dir` with the schema's defaults (`l`; split `horz-out`; zoom `in`);
    duration `p14:dur`, else `spd` (fast 500, med 750, slow 1000 ms; fast when missing). An unknown kind plays
    as a fade and is reported. A transition without an effect element counts as none.
- **Page** (`HtmlDeck.cs`): `HtmlDeckOptions.Notes` (default `true`; `false` writes `data-notes="off"` on the
  deck and no notes). Each section gets `data-transition`, `data-direction`, `data-duration` and `data-steps`
  (JSON, HTML-escaped: `id`, `p` range, class `in`/`out`/`em`, effect, side, ms), and an
  `<aside class="notes">` with the notes as `<p>`, nested `<ul>`/`<ol start>`, `<strong>`, `<em>`, `<br>`
  and links (`target="_blank" rel="noopener noreferrer"`), all escaped. A second `@page withnotes` (slide
  width × √2) for printing with notes. The key list gains `N` and `P`. The CSP is unchanged; the script is
  still one inline `<script>`, its hash computed from the resource.
- **SVG** (`SceneSvg`): in `SvgTarget.Html` only, the slide's own items are wrapped in `<g class="own">`.
  Decorations from master and layout can carry the same `cNvPr id`s as slide shapes (they come from other
  parts), so the script looks for build targets there, outermost first (a picture with an outline is a group
  and a shape sharing its id). `SvgTarget.Painting` is unchanged.
- **Script** (`deck.js`, rewritten around E07-S02's): a position (slide, step); `render` sets a slide to a
  step at once (`build-hidden` on what has not entered yet or has left; `build-gone` on what is hidden after
  the last step); `play` animates one step with the Web Animations API (keyframes of CSS properties:
  `opacity`, the individual `translate` and `scale` properties, `clip-path`). It never animates `transform`
  on an item, because pictures' `<g data-id>` carry a `transform` attribute that a CSS `transform` would
  replace; a pulse is a `scale` with a compensating `translate` around the item's centre. Fly offsets are
  computed from the item's box in slide points (`getScreenCTM`). Exits play the entrance in reverse, then
  hide. A key pressed during an effect or transition ends it first (`settle`).
  - Transitions animate the sections (`transform`, `opacity`, `clip-path`) with the outgoing slide kept as
    `.leaving` (under it, or `.on-top` for uncover, split in and zoom out), only when moving forward onto a
    slide. `dir` is read as the direction of motion (`l`: the new slide comes from the right); see below.
  - `prefers-reduced-motion: reduce` (checked at each step) and the presenter window make everything instant.
  - Every slide but the current one is kept in its final state; the overview renders the current one final
    too and restores the step on leaving. Print shows final states through CSS (`build-hidden` back to
    `visibility: inherit`, `build-gone` hidden), so it does not depend on `beforeprint` (which only ends
    running effects).
  - Hash `#7` / `#7.2`, read on load and on `hashchange`; a step out of range is clamped.
  - Notes view: a class on `body`; sections without notes get a script-made "No speaker notes for this
    slide." (or "The speaker notes are not in this file." with `Notes = false`), set as `textContent`.
  - Presenter window: `window.open(<file>#presenter, 'html-deck-presenter', 'popup,…')`. Messages
    `{app: 'html-deck', kind: 'hello' | 'state', slide, step}` go both over `postMessage(…, '*')` to the peer
    window (the opener, the opened window, or whoever wrote last — needed from disk, where the origin is
    opaque) and over a `BroadcastChannel` named after the path. Applying a state never sends one back, and
    the same state twice is a no-op, so the duplicates are harmless. The deck window plays a received single
    step or next-slide move with its effect; anything else jumps. The presenter's panel (the next view as a
    copy of the slide's SVG set to the next step, notes copied from the section, position, clock and reset) is
    built with DOM calls only. Only numbers are taken from messages.
- **CSS** (`deck.css`): build classes, the leaving slide, notes view (grid 2fr/1fr), presenter layout, print
  with and without notes.
- **Docs:** section *HTML deck* of the Pptx page (option, keys, address, builds with an effect table,
  transitions, notes view, presenter window, overview and print), `docs/README.md`, `CHANGELOG.md` under
  *New*, `ai/memory.md`.

### Decisions and deviations

- **Effects through the Web Animations API, not CSS classes with `@keyframes`:** the keyframes are CSS
  properties, but the script needs per-item offsets (fly) and must be able to end an effect when the next key
  comes. Still no inline `style` attribute and no `eval`; the CSP needed no change.
- **The overview also shows the current slide final,** as the plan says, and goes back to the step on leaving.
- **Previews (`DeckSvg`) still show every item,** also those a build takes away, as E07-S03 decided; the HTML
  deck's print hides them. Left as it is so the golden images stay unchanged.
- **Unknown emphasis plays as a pulse,** not as appear: an emphasis has nothing to make appear. Motion paths
  are not played at all (the item stays where it is).
- **Going back to the previous slide** lands on its last step (final state); `Home`, `End`, a number and the
  overview land on step 0 of a slide.
- **Coverage:** `presenting-lists.md` (E07-S05) joined the samples of `EverySampleDeck_IsDrawn_WithNothingLeftOut`
  (`presenting.md` was already there), so the generator's builds and transitions give no note on any template.

### Verification

- **Tests:** `./run.sh build` 0 warnings; `./run.sh test` 1553 passed after rebasing onto E06-S03, 6 Windows-only skipped (Pptx.Tests 464,
  Svg.Tests 34 with the golden PNGs unchanged). New `HtmlDeckPresentingTests` (10): notes with formatting,
  link and nested lists, repeated on split slides, none on a slide without; transitions with kind, direction
  and speed for every kind in `presenting.md`; `AlternateContent` choice and fallback, `p14:dur`, an unknown
  transition as a fade with a note; the click sequence of *Building the plan* as exact steps (paragraph
  ranges, fly from the bottom, exit, pulse), wipes, cards (three shapes on one click), a whole-shape exit;
  unknown presets as appear and pulse with notes; the page's attributes and notes HTML, and every effect's
  `data-id` and `data-p` found inside the slide's own group; a deck without any of it; `Notes = false`; notes
  text escaped (`</script><img onerror>`, `javascript:` links dropped); the painting SVG without the own group.
- **Chromium** (Playwright, headless; throwaway scripts `.tmp/check-*.js`, not a dependency) on `presenting.md`
  on the neutral, sample and dark templates, from disk: opening on `#13` with the builds hidden, step by step
  with the hash following (`#13.1` …), fly, wipe and pulse animating (frames frozen halfway in
  `.tmp/review/e07s06/mid-*.png`), the exit hiding its line, the next slide after the last step with its
  transition, back to `#13.7` without animation, reload on `#13.6`, the overview in final states and back to
  the step; every transition (push, wipe, split, cover, uncover, zoom, cut, Morph as a fade, none) runs and
  cleans up; `prefers-reduced-motion` gives no animation; the notes view with lists and link, "No speaker
  notes" on a slide without; print to PDF (19 sheets of 960 × 540 pt; with notes 960 × 1357.9 pt, slide on top,
  notes below; final states); the presenter window opened by `P` on the deck's slide, sync both ways,
  following a hash jump, the next view one step on, still driving the deck after the deck window reloads; no
  console errors and no requests. From a local web server: the same sync, and a presenter tab opened by hand
  with `#presenter` (no opener) found the deck over `BroadcastChannel`.

### Open verification items (for the human)

- **Other browsers:** only Chromium was available. Firefox, Edge and Safari (or WebKit), from disk and from a
  web server: stepping forward and back, effects, transitions with and without `prefers-reduced-motion`, the
  notes view, the presenter window and its sync (especially Firefox and Safari from disk, where
  `BroadcastChannel` and `postMessage` with an opaque origin behave differently), print to PDF with and
  without the notes view (the named page `@page withnotes` needs Firefox 110+ or Safari 18+). That acceptance
  box stays unticked.
- **Pop-up blockers:** `P` opens a pop-up from a key press; check that browsers allow it without a prompt.
- **Compare with PowerPoint's slide show** (no Office here): the direction of push, wipe, cover and uncover
  (`dir="l"` read as moving leftwards, the new slide coming from the right), split in and out, and zoom; the
  speed of each; how fly, wipe and pulse look; and E07-S05's loose end (a paragraph built inside a shape that
  itself builds whole): in the HTML the shape appears with that paragraph still hidden until its own click.
- Presenting for real on two screens: the presenter window on the laptop, the deck full screen on the projector.

### Loose ends

- Morph moves nothing yet (E07-S04).
- Builds by word or letter (`p:iterate`) play the paragraph or shape as a whole; `charRg` targets play the
  whole item. Neither gives a note.
- After-previous delays inside a click are not kept: everything of a click starts together.
- The presenter window does not mirror the deck window's black screen, overview or notes view.
