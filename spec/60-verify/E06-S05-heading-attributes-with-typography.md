# E06-S05 Heading attributes with typography

> Status: in verification
> Epic: [E06 Markdown syntax](../50-todo/E06-markdown-syntax.md)

## Summary

With `typography: true`, a heading's quoted attributes (`{kicker="Plan"}`, `{transition="fade"}`,
`{layout="…"}`) are no longer recognised: the typography pass curls their quotes before the
attributes are split off, so the whole block stays in the title and nothing is applied. This story
reads heading attributes before typography in every generator, so a document generates the same
slides and pages with and without typography, apart from the typeset text itself.

## Context

- Reported against 2611.15.8:
  [bug-typography-breaks-quoted-heading-attributes.md](../00-brainstorming/bug-typography-breaks-quoted-heading-attributes.md).
  Typography is off by default (E06-S03), so only documents that switch it on are affected.
- **Where it goes wrong.** Each parser builds a heading's runs with its `Runs(...)` helper, which
  applies coloured words, then icons, then typography (`DeckParser.Icons` → `DeckParser.Typeset`,
  the same chain in the PDF `MarkdownParser` and in `DocxRuns`). Only afterwards are the attributes
  taken off the last run:
  - decks: `DeckParser.Attributes(title)` (`##`, `#`) and `WithoutDeepAttributes` (`###`–`######`);
  - PDF: `MarkdownParser.Split` / `WithoutAttributes` (cover, chapters, contents, headings);
  - Word: `DocxBody` heading split (`HeadingAttributes.Split` / `SplitDeep`).
  `HeadingAttributes.BlockPattern` and `TokenPattern` accept only straight `"`, so after typesetting
  (`"Plan"` → `“Plan”`, or `„Plan“` in German) the block no longer matches. Classes without values
  (`{.dark}`, `{.unnumbered}`, `{.newpage}`) still work, because they contain no quotes.
- **Verified by the report:** decks only. **From reading the code:** PDF and Word split the same
  way after typesetting, so they are expected to show the same fault (the attribute block printed
  in the title, `kicker` lost, `{.newpage}`-style classes fine). Step 1 confirms this with tests.
- **Not affected, from reading the code:**
  - image attributes: `ImageAttributes.Take` reads the raw Markdig literals after the image;
  - build markers: `BuildMarker` reads the paragraph's raw text;
  - container arguments (`::: columns 1:2`, `::: panel dark`): read from the container's info string;
  - the Word cover title and front-matter `title`/`author`: attributes are taken off first, then
    `DocxGenerator.Typeset` runs.
  Step 1 adds regression tests for these too, so that stays true.
- `Check` does not report the fault, because the heading is valid text.

## Proposed approach

1. **Tests first (they fail today).** A shared test matrix, run for decks, PDF and Word, with
   `typography: true` and `false`, in English and German:
   - `## Title {kicker="Plan"}`, `{transition="fade"}`, `{layout="…"}`, `{.dark kicker="A"}`,
     `{.unnumbered}`, `{.newpage}` on `##`, and `{.unnumbered .newpage}` on `###`;
   - the `#` cover and a chapter divider with attributes;
   - a value with characters typography would change: `{kicker="Plan -- 2027..."}` keeps them as typed.
   Each test asserts the same title text (typeset or not), the same attributes, and for decks the
   same slide count and layouts in both settings. Regression tests with typography on for image
   attributes (`![a](x.png){fit=cover}`), a build marker (`{.build}`) and container arguments.
2. **Split before typesetting.** In each parser, take the attribute block off the last run of the
   heading *before* typography runs; then typeset the remaining title runs:
   - give the runs helper a way to stop before typography (e.g. `Runs(inline, typeset: false)`) and
     a `Typeset(runs, line)` to call after the split — the same three steps as today, in a new order;
   - decks: `Attributes(title)` and `WithoutDeepAttributes`; PDF: every heading path that calls
     `Split`/`WithoutAttributes` (cover, chapter list, contents entries, bookmarks, headings); Word:
     the `DocxBody` heading split;
   - attribute values are never typeset: they are settings, not text (a `kicker` value is typeset
     when it is drawn as text, like any other title text — check where the kicker is rendered and
     apply typography there, so it looks the same as the title).
3. **No change to the attribute syntax.** The patterns keep accepting only straight `"`. An author
   who types curly quotes by hand (`{kicker=“Plan”}`) still gets text, as before (question 2).
4. **Docs and changelog.** CHANGELOG `## Unreleased` gets a fix entry under *Breaking changes* only
   if output of documents without typography changes (it should not); otherwise under a new
   *Fixed* heading: "With `typography: true`, quoted heading attributes were kept as title text and
   not applied." No guide change. `ai/memory.md`: "heading attributes are split before typography".

## Acceptance criteria

- [x] With `typography: true`, `## Status {kicker="Plan"}` gives the title "Status" and the kicker
  "Plan" in decks; `transition="…"` and `layout="…"` apply as without typography.
- [x] PDF and Word take the same attribute blocks off headings with typography on (cover, chapters,
  contents entries, bookmarks, `###` and deeper).
- [x] A deck with typography on has the same number of slides and the same layouts as with it off
  (the bug report's sample: no `slide-split` / `slide-overfull` that are not there with it off).
- [x] Attribute values are kept exactly as typed; the title and kicker text are typeset.
- [x] Regression tests with typography on pass for image attributes, build markers and container
  arguments.
- [x] All existing outputs with typography off are unchanged (decks and Word part by part with
  relationship ids masked, PDFs with `/ID` and dates masked); the full test suite is green.
- [x] CHANGELOG entry; `ai/memory.md` note.

## Open questions

1. Fix approach: split the attributes off before typography (proposed: the attribute block is
   syntax, not text, and every format gets the same order), or teach typography to leave a trailing
   `{…}` block alone (smaller change, but typography would need to know heading syntax)?

> Answered by the human (2026-11-15): Split the attributes off before typography, in every generator.

2. Curly quotes typed by the author (`{kicker=“Plan”}`, e.g. pasted from Word): keep treating them
   as text (proposed: unchanged behaviour, out of scope), or accept `“ ” „ ‚ ‘ ’` as attribute
   quotes too?

> Answered by the human (2026-11-15): Keep treating hand-typed curly quotes as text; unchanged behaviour.

3. Release: cut a fix release (`v2611.15.<hour>` or the next day's) right after this story is
   verified (proposed), or right after it is implemented, since 2611.15.8 is already out with the
   fault?

> Answered by the human (2026-11-15): Release after the human has verified the fix.

4. Changelog heading: add a *Fixed* section for bug fixes (proposed: the changelog has only
   *Breaking changes* and *New* so far), or file the fix under *New*?

> Answered by the human (2026-11-15): A new *Fixed* section in the changelog for bug fixes.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

None. Documents without typography are unchanged: the 158 decks, PDFs and Word documents the test
suite writes to `.tmp/review/` are identical before and after the fix, part by part, with
relationship ids, `/ID` and dates masked (embedded chart workbooks included). With typography on,
quoted heading attributes now apply, which is the fix. CHANGELOG: a new *Fixed* section under
`## Unreleased` (question 4).

### Decisions and deviations

- **Same three steps, new order** (question 1): each parser's runs helper got `typeset: false`
  (coloured words and icons, no typography); the heading paths take the attribute block off the last
  run, then typeset the title runs. `Icons(...)` is now `Typeset(IconsOnly(...))` in all three.
  - Decks: `DeckParser.Slides` (`#`, `##`) and the `###`–`######` heading in flow content
    (`WithoutDeepAttributes`).
  - PDF: a new `MarkdownParser.TitleOf(heading, level)` serves the cover, the chapter list, kind
    checks, `AttributesOf` and headings (which give contents entries and bookmarks);
    `WithoutAttributes` is gone.
  - Word: `DocxBody.HeadingParts` (split in `HeadingSplit`, then `DocxRuns.Typeset`); `DocxContent`
    (cover title, `{.newpage}` check) works on raw text and was already right.
- **Kicker:** the plan said both "values are kept as typed" and "the kicker is typeset where it is
  drawn". Implemented: every value is read as typed (`layout`, `transition` match exactly), and the
  kicker *text*, which decks draw, is typeset with the title (`Plan -- 2027...` → `Plan – 2027…`).
  PDF and Word do not draw kickers.
- **Curly quotes typed by hand** still make the block text (question 2); no pattern change.

### Tests

`HeadingAttributeTypographyTests` in the Pptx, Pdf and Docx test projects, written first (they fail
without the fix: 5 of 6 in decks, 2 of 3 in PDFs and in Word):
- English and German, typography off vs on: cover, `#` chapter with an attribute, `kicker` with
  `--`/`...`, `transition`, `layout` (decks, on a template with that layout), `.dark kicker`,
  `{.unnumbered .newpage}` on `##` and `###`, `{.closing}`, `{.agenda}` (PDF); same slides, layouts,
  transitions and shapes (decks), pages, bookmarks and lines (PDF), paragraph styles, numbering, page
  breaks and sections (Word); no `{` left in any text.
- The sample decks `layouts.md`, `presenting.md`, `deck.md` with typography on: the same slides,
  layouts, transitions and notes as off (the report's "no extra `slide-split`/`slide-overfull`").
- Regression with typography on: image attributes, `{.build}`, `::: columns 1:2`.

Full suite: 1795 passed, 6 skipped (Windows only).

### Loose ends

- **Fixed at the human's request (2026-11-15): Word printed image attributes as text,** with and
  without typography: `![a](x.png){.round}` or `{scale=0.5}` stayed in the paragraph as `{.round}`.
  Cause: `DocxRuns.Collector` read the literal after an image as text before `DocxBody.Picture`
  called `ImageAttributes.Take`, which cuts the block off that literal. The collector now calls
  `Take` when it meets the image (as decks and PDFs do); the result is cached on the image, so
  `Picture` gets the same attributes. `scale=` applied before too (`Picture` read the literal, which was not yet cut); still, `fit=cover`, `focus`, `.round`, `.grey`
  give their `attribute-ignored` note as the guide says. Test:
  `ImageAttributes_AreTakenOff_NotPrinted_ScaleApplies_AndTheOthersGiveANote` (fails without the
  fix). No sample document uses image attributes in Word, so the review outputs are unchanged.
  Full suite: 1796 passed, 6 skipped.
- Release after the human has verified the fix (question 3).
