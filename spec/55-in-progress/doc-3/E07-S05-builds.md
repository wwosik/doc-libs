# E07-S05 Builds

> Status: in progress (doc-3)
> Epic: [E07 Presenting](../../50-todo/E07-presenting.md)

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
