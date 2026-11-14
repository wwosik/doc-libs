# E07-S05 Builds

> Status: in verification
> Epic: [E07 Presenting](../50-todo/E07-presenting.md)

## Summary

Slides can reveal their content click by click. A paragraph `{.build}` (and its variants) before a
block makes it appear, disappear or be emphasized on a click, and `build: lists` builds every list
item by item. The deck gets PowerPoint's own animation effects in a `p:timing` click sequence. PDFs
accept the same Markdown and leave the builds out.

## Context

- **Depends on E07-S01** (speaker notes and transitions), implemented after it. This story
  reuses:
  - the *Presenting* sections of `AuthoringGuide/Pptx.md` and `docs/…Pptx.md`;
  - the `presenting.md` sample and its round trip;
  - the PDF's acceptance of deck-only front-matter keys (E07-S01 question 5 covers `build` too);
  - the per-part copying in `Fit`;
  - `::: notes` taken out of a section's blocks, which a marker must not be followed by.

  `p:timing` goes before E07-S01's `p:transition` in `p:sld`, as the schema orders them.
- **Slide model.** `Slide` (`SlideModel.cs`) carries `Title`, `Layout`, `Line` and `Footnotes`
  (and, after E07-S01, `SpeakerNotes` and `Transition`). Nothing about builds exists in the model,
  the parser or the writer.
- **Parsing.**
  - `DeckParser.Parse` cuts the document at every `HeadingBlock { Level: <= 2 }` and passes each
    heading's blocks to `Section`.
  - A paragraph `{.build}` is not syntax today. Markdig leaves it as a literal, so both formats
    write the text `{.build}`.
  - `HeadingAttributes` throws on an unknown attribute and lists the valid ones; markers follow
    that pattern.
- **Writing.**
  - `DeckWriter.NewSlide` writes `p:sld` with `p:cSld` and `p:clrMapOvr` (and `p:transition`
    after E07-S01): no `p:timing`.
  - `Canvas.NextId` numbers shapes from 2. Each block's shapes are appended in order by
    `Stack`/`Place` (flow slides), `Placeholder` (body text) or `Picture`/`Table`/`Chart`/
    `Drawing` (visuals).
- **Fitting.** `Fit` (`DeckWriter.Fitting.cs`) builds the parts of a split slide from the blocks
  and paragraphs of the original, so builds attached to paragraphs and blocks travel with them.
- **Front matter.** Deck keys come from each template kind's `Keys` (`PptxTemplateOptions.cs`).
  `listBullets` is taken by every template through `WithListBullets`, with the host default
  `PptxOptions.ListBullets`; `build` follows that pattern, as E07-S01's `transition` does.
- **Converter.** `PptxConverter` reads shapes and text; it does not read `p:timing`.
- **Tests.** `Samples.AssertValid` runs the Open XML validator (`FileFormatVersions.Microsoft365`).
- **Decided** (analysis, 2026-11-13): keys and values are English; existing decks do not change.
  Split out of E07-S01 by its question 1.
- **Later stories.** E07-S06 (presenting the HTML deck) steps through the main animation sequence it
  understands, and E07-S03 (preview images) paints each slide as PowerPoint's normal view shows
  it, with every built shape visible. E07-S04 (scenes) uses this story's build model and
  `TimingWriter`.

## Proposed approach

1. **Build markers** (`wwosik.DocLibs.DocumentGeneration`). A new internal record `BuildMarker`
   with `BuildMarker.TryParse(paragraph text)`. A paragraph that is nothing but one `{…}` block is
   a marker. It may hold:
   - `.build`: the block appears on a click, item by item. A list goes by its top-level items
     (sub-items come with their item). `cards`, `rows` and `columns` go card by card, row by row
     and column by column. An image row goes image by image. Any other block appears whole.
   - `.build=appear`, `.build=fly`, `.build=wipe`: the same, with that effect. Plain `.build` is
     `appear`.
   - `.frag`: the whole block appears on one click.
   - `.exit`: the block is shown at first and disappears on a click.
   - `.emphasize`: the block is emphasized on a click.

   Classes in one marker combine in their written order as consecutive clicks (`{.frag .exit}`:
   in on one click, out on the next). A `{…}`-only paragraph with any other class or key throws
   `DocumentConversionException` listing the valid ones, as heading attributes do (question 1).
   This is a breaking change for documents that have such a paragraph.
2. **Parsing decks** (`DeckParser`).
   - A marker paragraph is not written. It is held as pending and attached to the next block of
     the same section: `TextParagraph.Build` for paragraphs and list items, `FlowBlock.Build` for
     flow blocks, and `VisualSlide.VisualBuild` for the visual of a visual slide.
   - The build is a new internal record `Build(BuildKind Kind, BuildEffect Effect, bool ByItem)`,
     where `BuildKind` is `Entrance`, `Exit` or `Emphasis`.
   - A marker followed by a heading, by `::: notes`, by another marker or by the end of the
     section throws, naming its line.
   - Markers inside containers apply to the next block inside that container.
   - **`build: lists`** (front matter, or the host default `PptxOptions.Build = DeckBuild.Lists`)
     gives every top-level list of a section without its own marker the `.build` marker. The
     default is `DeckBuild.None`.
3. **Fitting.** Builds stay on their paragraphs and blocks, so each part of a split slide plays
   the steps of what it holds, from its first click. A list split across parts builds item by
   item on each part.
4. **Recording steps.** `Canvas` records, while a slide is written, which shapes (and, for a text
   body, which paragraph range) each build step covers: the shape ids from `NextId` before and
   after the block is placed, and the paragraph indices inside a body placeholder or text box.
5. **Writing the timing.** After the slide is written, a new internal `TimingWriter` writes
   `p:timing`, if any step exists:
   - the main sequence with one click group per step, in document order;
   - every shape of a step started with the first (`clickEffect`, then `withEffect`);
   - text targets addressed by paragraph range (`p:txEl/p:pRg`);
   - `p:bldLst` entries (`p:bldP` for text shapes, `p:bldGraphic` for tables and charts), as
     PowerPoint writes them.
6. **Effects.** Each effect uses PowerPoint's own preset, so the Animation pane names it
   (question 2):
   - entrance: Appear, Fly In (from the bottom), Wipe (from the left);
   - exit: Disappear;
   - emphasis: Pulse, which leaves nothing changed after it plays and works on any shape.

   The XML of each one is copied from a deck PowerPoint saved, and that reference is kept as a
   test fixture. Tables and charts build as a whole. PowerPoint cannot build a table by row, and
   charts by series are left for later.
7. **PDF.** `MarkdownParser` skips build-marker paragraphs after checking them like decks do, so
   an unknown `{…}`-only paragraph throws there too. The front-matter key `build` is accepted,
   checked and ignored (E07-S01 question 5).
8. **Converter.** No change: builds are not read back.
9. **Options and keys.**
   - `PptxOptions.Build` (public enum `DeckBuild { None, Lists }`) is the host default.
   - The front-matter key `build` is taken by every template kind, like `listBullets`, and listed
     in `KeyMeanings`.
10. **Guides, docs, changelog.**
    - `AuthoringGuide/Pptx.md` *Presenting* (from E07-S01) gets build markers with examples and
      `build: lists`.
    - `AuthoringGuide/Pdf.md` *Syntax written for slides* says that markers are accepted and left
      out.
    - The expected guides (`authoring-guide-*.expected.md`, PPTX and PDF) are regenerated with
      `DOCLIBS_UPDATE_EXPECTED=1`, and the diff is reviewed.
    - `docs/…Pptx.md` *Presenting* gets builds, and the front-matter table the key `build`;
      `docs/…Pdf.md` gets the same as its guide.
    - `CHANGELOG.md` *Unreleased* gets, under breaking changes: a paragraph that is only
      `{.build}`, `{.frag}`, `{.exit}`, `{.emphasize}` or `{.build=…}` is no longer written as
      text, and any other `{…}`-only paragraph throws. Under new: builds and `build: lists`.
11. **Tests.**
    - The sample `presenting.md` (from E07-S01) gets every marker, on a content slide, a split
      slide, a visual slide and a flow slide with containers, and a second sample with
      `build: lists`. Both generate and validate with the Open XML validator. The round trip of
      `presenting.md` changes only by the text the new slides add: markers are not read back.
    - Unit tests check:
      - the timing tree per effect against the PowerPoint reference XML;
      - the click order across a split slide;
      - markers inside containers;
      - combined classes (`{.frag .exit}`);
      - the errors (stray marker, unknown class) in both formats;
      - that a deck without markers is byte-identical to before (no `p:timing`);
      - that PDFs from Markdown with markers equal PDFs from the same Markdown without them.

## Acceptance criteria

- [ ] `{.build}`, `{.build=appear|fly|wipe}`, `{.frag}`, `{.exit}` and `{.emphasize}` produce
  PowerPoint's own effects in document order:
  - on content slides by paragraph;
  - on visual slides for the text above, the visual and the text below;
  - on flow slides by block or by card, row, column or image.
- [ ] Classes in one marker play as consecutive clicks in their written order.
- [ ] `build: lists` builds every top-level list item by item; a list with its own marker keeps it.
- [ ] Each part of a split slide plays the steps of what it holds, from its first click.
- [ ] A stray marker and an unknown `{…}`-only paragraph throw `DocumentConversionException` in
  both formats, listing the valid markers.
- [ ] A deck without markers or `build:` is byte-identical to the one the previous version wrote.
- [ ] PDFs leave markers out, accept `build:` in front matter, and are otherwise unchanged.
- [ ] Every generated test deck passes the Open XML validator (`Microsoft365`).
- [ ] **Checked in real PowerPoint** (Windows, through the export script and by hand), and the
  result recorded in the implementation notes:
  - every deck opens without a repair prompt;
  - every effect plays in the slide show in the expected order and shows its proper name in the
    Animation pane.
- [ ] The guides, expected guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. A paragraph that is only `{…}` with a class other than the build markers, such as `{.note}`:
   throw and list the valid markers (proposed, as for heading attributes), or leave it as text?
   Throwing is a breaking change for documents that have such a paragraph.

> Answered by the human (2026-11-14): as proposed.

2. The effects: `{.build}` as Appear, `fly` from the bottom, `wipe` from the left, `{.exit}` as
   Disappear and `{.emphasize}` as Pulse (proposed: Pulse leaves nothing changed after it plays and
   works on any shape). Or should `.emphasize` change the colour to the accent, as a lasting
   highlight?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- Split out of E07-S01 (its question 1, answered 2026-11-14). Questions 1 and 2 were E07-S01's
  questions 6 and 7, with their answers.

## Implementation notes

### Breaking changes

- A paragraph that is nothing but one `{…}` of classes and quoted `key="value"` pairs is now a build
  marker in both generators (question 1): `{.build}`, `{.build=appear|fly|wipe}`, `{.frag}`, `{.exit}`
  and `{.emphasize}` (also combined) are no longer written as text, any other such paragraph
  (`{.note}`, `{.build=zoom}`, `{width="50%"}`) throws `DocumentConversionException` listing the valid
  markers, and so does a marker that builds nothing or stands inside a list, a quote or speaker
  notes. Other braces (`{"a": 1}`, `{x}`, `{.build} and more`) stay text; `\{` keeps a marker as text.
  In `CHANGELOG.md` under *Unreleased*, breaking changes.
- Nothing else: decks without markers or `build:` are unchanged, and so are PDFs (see *Verification*).

### Decisions

- **Shared syntax** (`BuildMarker`, `wwosik.DocLibs.DocumentGeneration`): `Build(BuildKind, BuildEffect,
  ByItem)` as planned, with `BuildEffect` `Appear`, `Fly`, `Wipe`, `Disappear`, `Pulse`. A marker is
  read from the paragraph's **inlines**, not its source lines: Markdig releases a paragraph's `Lines`
  after inline parsing. Only literal text and line breaks count, so an escaped `\{` or any formatting
  keeps the paragraph text. `.build=` values are lower case, like the classes.
- **One check for both formats:** `BuildMarker.Check` walks the whole document once, right after
  parsing, and throws for unknown markers anywhere (table cells excluded) and for stray ones, with
  the line. Allowed places: the top level (that is, a section) and directly inside a `:::` container
  other than `notes` or an alert. Stray: followed by a heading (any level, also `###` inside
  `::: cards`), `::: notes`, another marker, a `---`, a comment or nothing. Decks and PDFs therefore
  reject exactly the same Markdown; the PDF then skips every marker paragraph, the cover's subtitle
  included.
- **Parsing decks** (deviation in detail): a marker becomes a `BuildMark(Builds, Block, Item)` on the
  content it covers (`TextParagraph.Build`, `FlowBlock.Build`, `VisualSlide.VisualBuild`) rather than
  a bare `Build`, because a marker can hold several builds and its clicks are ordered across the
  slide. `Block` is the marked block's `Span.Start`, so clicks follow the Markdown even through nested
  containers; `Item` is the top-level list item (sub-items carry their item's number). The parser
  keeps the current mark while it walks a block (`WalkMarked`), and both `ITarget`s put it on every
  paragraph, block and visual that block produces, so a paragraph split at an image builds its text
  and the image together.
- **Containers:** blocks inside a marked container build with it, as shapes of the container; a
  marker inside builds its block on its own click, and inner blocks without one do not take the
  container's mark. `::: statement` is the exception: its blocks follow on the slide itself, so they
  take the statement's marker unless they have their own, and only a list with a marker of its own
  goes item by item there.
- **Items:** besides lists, cards, rows, columns and image rows (as planned), the labelled tags of a
  container list (``- `A` text``) go item by item, because they are a list in the Markdown. Rows: the
  rule under the last row comes with the last row. An image row of more than four wraps and keeps
  counting its images. Visual slides with an image row build it image by image too.
- **`build: lists`** applies to lists directly at the top level of a section only (not inside
  containers, quotes or speaker notes), also on a cover or divider; a list with its own marker keeps
  it. Front matter wins over `PptxOptions.Build`.
- **Recording** (deviation in detail from "ids before and after"): `Canvas.Building(mark, …)` records
  every shape added to the top of the shape tree while a marked block is written (a drawing is its
  group, not the group's children), `Canvas.Item` switches the item inside cards, rows, columns,
  labelled tags and image rows, and `Canvas.AddText` records the marked paragraphs of a text body by
  index. Measuring passes (no canvas) record nothing. Title, footer, footnotes and slide number are
  never built.
- **Timing** (`TimingWriter`, XML strings like `Transitions`): root `tmRoot`, main sequence, per step
  a click `par` (`delay="indefinite"`) with one inner `par` holding every effect (`clickEffect` first,
  then `withEffect`), `prevCondLst`/`nextCondLst` as PowerPoint writes them. Text targets are merged
  into ranges of neighbouring paragraphs. `p:bldLst`: `p:bldP build="p"` (group 0) for a shape built
  by paragraph, `p:bldP animBg="1"` for a shape built whole (group 1 when the same shape also builds
  by paragraph), `p:bldGraphic` with `p:bldAsOne` for tables and charts, nothing for pictures and
  groups; an empty build list is left out.
- **`p:timing` goes after `p:transition`**, not before it: the schema orders `p:sld` as `cSld`,
  `clrMapOvr`, `transition`, `timing`, `extLst`, and the validator agrees. The plan's Context and
  E07-S01's loose end had the order the wrong way round; `TimingWriter.Write` appends the timing after
  whatever `Transitions.Write` added.
- **Effects** (question 2): Appear (`presetID` 1), Fly In from the bottom (2, subtype 4, 0.5 s on
  `ppt_x`/`ppt_y` from `1+#ppt_h/2`), Wipe from the left (22, subtype 8, `wipe(left)`, 0.5 s),
  Disappear (exit 1), Pulse (emphasis 26: a fade with `tmFilter` and a 105 % `animScale` that
  reverses).
- **Reference fixture** (deviation): the plan wanted the XML of each effect copied from a deck
  PowerPoint saved. No PowerPoint is available here, so the XML is written from PowerPoint's preset
  format as known, and the fixture `build-effects.expected.md` (the whole timing once, each effect
  node, and a build list) records what the generator writes. It guards against changes, but it is not
  yet a PowerPoint reference: see verification item 3.

### Verification

- **Tests.** All pass after rebasing onto E04-S05 and E08-S01: 109, 123, 78, 22, 270, 71, 164 and 1
  (838), 6 Windows-only tests skipped. The build has no warnings from this change (5 `CS0618` warnings
  come from E04-S05's `ImageAttributeTests.cs`). New: `BuildTests` in Pptx.Tests (35: every effect against the fixture, the
  click order on content, split, visual and flow slides, containers, cards, rows, columns, labelled
  tags, tags, image rows, drawings and charts, combined classes, cover and dark slides on three
  templates, `build: lists` from front matter and options, stray and unknown markers, other braces,
  no timing without markers, the converter) and 10 more in Pdf.Tests' `PresentingTests`; updated
  `PresentingTests` (transition list, the new slide's speaker notes) and the front-matter key list.
- **Open XML validator** (`Microsoft365`): `presenting.md` on both built-in brands, the new
  `presenting-lists.md` on both, and every deck `BuildTests` generates (`brand-dark` included) pass.
- **Unchanged decks.** `deck.md`, `layouts.md` and E07-S01's `presenting.md` were generated with the
  code before this change (`24010ee`, and again after the rebase `b51d5e1`, a detached worktree in
  `.tmp/base`) and with this change, on the
  neutral and the sample brand without a date: every part is identical once the SDK's random
  relationship ids are masked (`.tmp/baseline/`). A test checks that `deck.md` has no `p:timing` and
  that `build: none` writes the same parts.
- **Unchanged PDFs.** A test checks that Markdown with markers everywhere (cover subtitle, lists,
  containers, alerts, tables) and `build: lists` gives the same PDF as without them, but for the
  random `/ID`.
- **Guide length:** with E04-S05's image attributes the PPTX guide went over its 2500-word limit,
  so the *Builds* part of the guide was cut to the essentials (the docs keep the details).
- **Expected files** regenerated with `DOCLIBS_UPDATE_EXPECTED=1` and reviewed: the four authoring
  guides, `presenting-neutral/-sample.roundtrip.expected.md` (only the five new slides added, markers
  not read back) and the new `build-effects.expected.md`.

### To verify by hand (no PowerPoint on this machine)

The decks are written by the tests to `.tmp/review/presenting-neutral.pptx`, `presenting-sample.pptx`
and `presenting-lists-neutral.pptx` / `-sample.pptx`.

1. **No repair prompt** when opening all four decks.
2. **Animation pane** (Animations → Animation Pane) on *Building the plan*: seven entries in this
   order, Appear ×3 (the second item with its sub-item), Fly In, Appear, Disappear, Pulse, each named
   so and set to *On Click*. Effect Options of Fly In say *From Bottom*, of Wipe *From Left* (on
   *Every berth, one by one*).
3. **Compare with PowerPoint's own XML:** on a new slide, give a text box each of the five effects by
   hand, save, and compare its `p:timing` with `build-effects.expected.md` (ignoring ids). Where they
   differ (notably Pulse's `presetID` 26 and its `tmFilter`/`animScale`, and whether PowerPoint writes
   `p:bldP` for emphasis-only shapes), adopt PowerPoint's XML in `TimingWriter.Effect` and regenerate
   the fixture.
4. **Slide show** of each new slide, clicking through:
   - *Building the plan*: items one by one, then the flying line, the line that comes and goes, and
     the pulse that leaves the text as it was.
   - *Every berth, one by one* (two slides): each starts with nothing shown and wipes in its own
     berths; the second starts at the berth after the first slide's last.
   - *Arrivals, step by step*: text above, then the table (wipe), then the total.
   - *Who we talked to*: card by card (box and text together), then *The rollout follows in autumn.*
     inside the panel (the panel and its first line are shown from the start), then the callout
     pulses.
   - *Before and after*: column by column, then the statement disappears.
   - `presenting-lists`: the first and the last list item by item, the marked list at once, the list
     in the panel and the text slide without clicks.
5. **Normal view** shows every built shape (for E07-S03's preview images), and the *Animations* tab
   lets each effect be edited or removed without errors.

### Loose ends

- A marker inside a marked container builds a paragraph of a shape that the container's build also
  covers as a whole (`p:bldP` group 0 by paragraph, group 1 whole). Valid, but how PowerPoint plays
  "whole shape in, then one paragraph in" is part of item 4; if it misbehaves, the outer build should
  leave such paragraphs out.
- `p:bldP build="p"` is written also when only some paragraphs of a shape build; PowerPoint may write
  another value there (item 3).
- Charts build as a whole; by series, as the plan says, is left for later.
- E07-S06 (HTML deck) and E07-S04 (scenes) can reuse `BuildMark`, `TimingWriter.Steps` and the
  recorded `BuildTarget`s.
