# E07-S01 Speaker notes, builds, transitions

> Status: todo
> Epic: [E07 Presenting](E07-presenting.md)

## Summary

Decks become ready to present. `::: notes` under a heading gives the slide speaker notes, a
paragraph `{.build}` (and its variants) before a block makes it appear on a click, and
`transition:` in the front matter or `transition="…"` on a heading sets how slides change. The
built-in master gets a notes master, and the PPTX converter reads speaker notes back as
`::: notes`. PDFs accept the same Markdown and leave the presenting parts out.

## Context

- **Slide model.** `Slide` (`SlideModel.cs`) carries `Title`, `Layout`, `Line` and `Footnotes`.
  Nothing about notes, builds or transitions exists in the model, the parser or the writer.
- **Parsing.**
  - `DeckParser.Parse` cuts the document at every `HeadingBlock { Level: <= 2 }` and passes each
    heading's blocks to `Section`. Blocks before the first heading are dropped silently:
    `Section` returns when `heading` is `null`.
  - `UsesLayouts` counts any `CustomContainer`, alert or definition list as layout syntax. Such
    a section becomes a `FlowSlide` instead of a `ContentSlide`/`VisualSlide`.
  - `LayoutSyntax.CheckContainer` knows `cards`, `rows`, `panel`, `statement` and `columns`.
    Both generators call it (`DeckParser.Container`, `MarkdownParser.Add`), so `::: notes`
    throws "Unknown container" in both formats today.
  - `HeadingAttributes` knows `.dark`, `kicker="…"`, `layout="…"` and `.closing` and throws on
    anything else. It is shared, so the PDF rejects the same mistakes and then drops the
    attributes (`MarkdownParser.WithoutAttributes`).
  - A paragraph `{.build}` is not syntax today. Markdig leaves it as a literal, so both formats
    write the text `{.build}`.
- **Writing.**
  - `DeckWriter.NewSlide` writes `p:sld` with only `p:cSld` and `p:clrMapOvr`: no
    `p:transition`, no `p:timing`.
  - `Canvas.NextId` numbers shapes from 2. Each block's shapes are appended in order by
    `Stack`/`Place` (flow slides), `Placeholder` (body text) or `Picture`/`Table`/`Chart`/
    `Drawing` (visuals).
  - `DeckWriter.Tag` sets `lang` on every run of a slide part. A notes part would need the same.
- **Fitting.** `Fit` (`DeckWriter.Fitting.cs`) builds the parts of a split slide with
  `new ContentSlide(…) { Layout = …, Line = … }` and the same for visual and flow slides.
  `WithFootnotes` then gives each part the footnotes it references. Any new per-slide property
  has to be copied to the parts in the same way.
- **Templates.**
  - `TemplateBuilder.Write` writes the built-in master, its theme, table styles and
    presentation properties. It writes `p:notesSz` but **no notes master**, and its relationship
    ids are fixed (`rId1`–`rId7`, see the trap in `ai/memory.md`).
  - Caller and brand templates usually bring a notes master. `DeckWriter.RemoveSlides` deletes
    their slides (with their notes slides) but keeps the master.
- **Front matter.** Deck keys come from each template kind's `Keys` (`PptxTemplateOptions.cs`).
  `listBullets` is taken by every template through `WithListBullets`, with the host default
  `PptxOptions.ListBullets`; new deck-wide keys follow that pattern. The PDF accepts only
  `showDate`, `showTagline`, `pageSize` and `orientation` (`PdfGenerator`), so it rejects deck
  keys such as `listBullets` today.
- **Notes for the writer.** Generation notes (E05-S03) go through `GenerationNotes`, and
  `NoteCodes.All` needs an entry per code. The word "notes" is taken by them in the API, docs and
  guides ("Notes you may get back"), so this story always says **speaker notes** (`SpeakerNotes`
  in code).
- **Converter.**
  - `PptxConverter.RenderSlide` reads only the slide's shape tree. `docs/…DocumentConversions.md`
    says "Speaker notes and charts are not read".
  - `OpenXmlPackage.Relationships` returns id → target without the relationship type, so a
    slide's notes part cannot be found by type yet.
- **Tests and docs.**
  - `Samples.AssertValid` runs the Open XML validator (`FileFormatVersions.Microsoft365`).
  - The PPTX tests round-trip `deck.md` and `layouts.md` through the converter
    (`*.roundtrip.expected.md`).
  - `docs/…Pptx.md` *Known limits* says "No speaker notes".
- **Decided** (analysis, 2026-11-13):
  - Speaker notes are repeated on every part of a split slide.
  - The built-in master gets a notes master.
  - The converter reads speaker notes back as `::: notes`.
  - Keys and values are English.
  - Existing decks do not change.
- **Later stories.** E07-S02 (HTML deck) and E07-S03 (preview images) read the generated PPTX.
  Assumed here: the HTML deck shows the speaker notes in its notes view and steps through the main
  animation sequence and transitions it understands. The previews paint each slide as PowerPoint's
  normal view shows it, with every built shape visible. E07-S04 (scenes) uses this story's builds
  and the `morph` transition.

## Proposed approach

1. **Shared syntax** (`wwosik.DocLibs.DocumentGeneration`).
   - `LayoutSyntax.CheckContainer` accepts `notes`, which takes no arguments, and lists it among
     the containers.
   - **Build markers.** A new internal record `BuildMarker` with `BuildMarker.TryParse(paragraph
     text)`. A paragraph that is nothing but one `{…}` block is a marker. It may hold:
     - `.build`: the block appears on a click, item by item. A list goes by its top-level items
       (sub-items come with their item). `cards`, `rows` and `columns` go card by card, row by
       row and column by column. An image row goes image by image. Any other block appears whole.
     - `.build=appear`, `.build=fly`, `.build=wipe`: the same, with that effect. Plain `.build`
       is `appear`.
     - `.frag`: the whole block appears on one click.
     - `.exit`: the block is shown at first and disappears on a click.
     - `.emphasize`: the block is emphasized on a click.

     Classes in one marker combine in their written order as consecutive clicks
     (`{.frag .exit}`: in on one click, out on the next). A `{…}`-only paragraph with any other
     class or key throws `DocumentConversionException` listing the valid ones, as heading
     attributes do (see question 6).
   - **Transitions.** `HeadingAttributes` gains `transition="…"` on `#` and `##` headings. A new
     internal list `LayoutSyntax.Transitions` holds `none`, `fade`, `push`, `wipe`, `split`,
     `cover`, `uncover`, `zoom`, `cut` and `morph`, and an unknown value throws in both formats.
     `transition="…"` alone keeps a plain slide plain, as `layout="…"` does.
2. **Parsing decks** (`DeckParser`).
   - **Speaker notes.**
     - `::: notes` at the top level of a section is taken out of the section's blocks before
       `UsesLayouts` decides the slide kind. Adding notes therefore never turns a content slide
       into a flow slide.
     - Its content becomes `SpeakerNotes`, a list of `TextParagraph`: paragraphs, lists (bullets,
       numbers, levels), bold, italic, code spans, links and line breaks. What other blocks do
       there is question 3.
     - Several `::: notes` in one section are joined in order.
     - A `::: notes` nested in another container or a list throws.
     - A `::: notes` before the first heading is dropped with a new note `notes-dropped`.
   - **Which slides get them.** Every slide a section produces gets its speaker notes. That
     covers the cover or divider of a `#` and each visual slide of a `##` (question 2). Since
     `Section` already loops over the section's slides to set `Line` and `Footnotes`, it sets
     `SpeakerNotes` there too.
   - **Builds.**
     - A marker paragraph is not written. It is held as pending and attached to the next block
       of the same section: `TextParagraph.Build` for paragraphs and list items,
       `FlowBlock.Build` for flow blocks, and `VisualSlide.VisualBuild` for the visual of a
       visual slide.
     - The build is a new internal record `Build(BuildKind Kind, BuildEffect Effect, bool
       ByItem)`, where `BuildKind` is `Entrance`, `Exit` or `Emphasis`.
     - A marker followed by a heading, by `::: notes`, by another marker or by the end of the
       section throws, naming its line.
     - Markers inside containers apply to the next block inside that container.
   - **`build: lists`** (front matter, or the host default `PptxOptions.Build = DeckBuild.Lists`)
     gives every top-level list of a section without its own marker the `.build` marker. The
     default is `DeckBuild.None`.
   - **Transitions.** `Slide.Transition` is filled from the heading's `transition="…"`, else from
     the deck-wide value (front matter `transition:`, else `PptxOptions.Transition`, default
     `SlideTransition.None`). A heading's `transition="none"` overrides the deck-wide value.
3. **Fitting.** `Fit` copies `SpeakerNotes` and `Transition` to every part, as `WithFootnotes`
   does for footnotes.
   - Builds stay on their paragraphs and blocks, so each part plays the steps of what it holds,
     from its first click.
   - A list split across parts builds item by item on each part.
4. **Writing speaker notes.**
   - **Notes master.** A new internal `NotesMasterBuilder` writes a notes master and its own
     theme:
     - a slide image placeholder (`sldImg`) and a body placeholder (`body`, `idx="1"`) on the
       portrait notes page (`p:notesSz` 6858000 × 9144000);
     - the header, date, footer and slide-number placeholders;
     - body text in 12 pt, with bullet levels like the slide master's.

     For the built-in master it takes the brand's text font and colours. A caller or brand
     template that has a notes master keeps its own, and one without gets the built-in one with
     theme fonts.
   - **Only when needed.** The notes master is added only when at least one slide has speaker
     notes. Decks without notes keep their parts and relationship ids byte for byte. This keeps
     the `TemplateBuilder` relationship trap intact: the notes master hangs off the presentation
     part as the next free id, written after the slides are known, and is listed in
     `p:notesMasterIdLst`.
   - **Notes slide.** Each slide with speaker notes gets a `NotesSlidePart`, related to its slide
     and the notes master. It has the slide image placeholder and the body placeholder with the
     paragraphs, written by the same `Paragraph`/`Run` code as slide text:
     - explicit bullets and numbers for lists;
     - links as hyperlink relationships on the notes part;
     - code in the deck's code font.

     `Tag` also runs over the notes part.
5. **Writing builds.**
   - `Canvas` records, while a slide is written, which shapes (and, for a text body, which
     paragraph range) each build step covers: the shape ids from `NextId` before and after the
     block is placed, and the paragraph indices inside a body placeholder or text box.
   - After the slide is written, a new internal `TimingWriter` writes `p:timing`, if any step
     exists:
     - the main sequence with one click group per step, in document order;
     - every shape of a step started with the first (`clickEffect`, then `withEffect`);
     - text targets addressed by paragraph range (`p:txEl/p:pRg`);
     - `p:bldLst` entries (`p:bldP` for text shapes, `p:bldGraphic` for tables and charts),
       as PowerPoint writes them.
   - **Effects.** Each effect uses PowerPoint's own preset, so the Animation pane names it:
     - entrance: Appear, Fly In (from the bottom), Wipe (from the left);
     - exit: Disappear;
     - emphasis: Pulse (see question 7).

     The XML of each one is copied from a deck PowerPoint saved, and that reference is kept as
     a test fixture.
   - Tables and charts build as a whole. PowerPoint cannot build a table by row, and charts by
     series are left for later.
6. **Writing transitions.** `NewSlide` (or a step after the slide is written) adds
   `p:transition` after `p:clrMapOvr`, as the schema orders it:
   - `fade`, `push`, `wipe`, `split`, `cover`, `uncover` (`p:pull`), `zoom` and `cut`, with
     PowerPoint's default speed and direction;
   - `none` writes nothing;
   - `morph` writes `mc:AlternateContent`: a `p159:morph` choice (`option="byObject"`), with a
     `fade` fallback for viewers without Morph.
7. **PDF.** `MarkdownParser` skips `::: notes` and build-marker paragraphs, and drops
   `transition="…"` with the other heading attributes, after checking all of them like decks do.
   Whether the PDF also accepts the front-matter keys `transition` and `build` is question 5.
8. **Converter** (`PptxConverter`).
   - `OpenXmlPackage` gets a way to find a part's relationship by type.
   - `RenderSlide` follows the slide's `notesSlide` relationship and reads the notes part's body
     placeholder (not `sldImg`, `sldNum`, `hdr`, `dt` or `ftr`).
   - Its paragraphs are written after the slide's content as `::: notes` … `:::`, escaped like
     other text. Empty notes write nothing.
   - Builds and transitions are not read back.
9. **Options and keys.**
   - `PptxOptions.Transition` (public enum `SlideTransition`) and `PptxOptions.Build` (public
     enum `DeckBuild { None, Lists }`) are the host defaults.
   - The front-matter keys `transition` and `build` are taken by every template kind, like
     `listBullets`, and listed in `KeyMeanings`.
   - The new note code `notes-dropped` goes in `NoteCodes`/`NoteCodes.All` and the docs table.
10. **Guides, docs, changelog.**
    - `AuthoringGuide/Pptx.md` gets a section *Presenting*: speaker notes, build markers with
      examples, transitions, `build: lists`.
    - `AuthoringGuide/Pdf.md` *Syntax written for slides* says that speaker notes, markers and
      transitions are accepted and left out.
    - The expected guides (`authoring-guide-*.expected.md`, PPTX and PDF) are regenerated with
      `DOCLIBS_UPDATE_EXPECTED=1`, and the diff is reviewed.
    - `docs/…Pptx.md` gets a section *Presenting*, the new keys in the front-matter table, the
      notes master under *Templates*, and "No speaker notes" removed from *Known limits*.
    - `docs/…Pdf.md` gets the same as its guide, `docs/…DocumentGeneration.md` the new note code,
      and `docs/…DocumentConversions.md` the `.pptx` row (speaker notes are read).
    - `CHANGELOG.md` *Unreleased* gets, under breaking changes:
      - a paragraph that is only `{.build}`, `{.frag}`, `{.exit}`, `{.emphasize}` or
        `{.build=…}` is no longer written as text, and any other `{…}`-only paragraph throws;
      - `PptxConverter` output now includes `::: notes` for slides with speaker notes.

      Under new: speaker notes, builds, transitions, `notes-dropped`.
11. **Tests.**
    - A new sample `presenting.md` (speaker notes on a cover, a content slide, a split slide and
      a flow slide; every marker; every transition; `build: lists`) generates, validates with
      the Open XML validator and round-trips to a `presenting.roundtrip.expected.md` with its
      `::: notes`.
    - Unit tests check:
      - the timing tree per effect against the PowerPoint reference XML;
      - the click order across a split slide;
      - notes repeated on each part;
      - markers inside containers;
      - the errors (stray marker, unknown class, nested `::: notes`, unknown transition) in
        both formats;
      - `notes-dropped`;
      - that a deck without the new syntax is byte-identical to before (no notes master);
      - that PDFs from Markdown with the new syntax equal PDFs from the same Markdown without
        it.
    - `sample-generator`'s `sample.pptx` gets a notes slide as PowerPoint writes it (with a
      slide-number placeholder), so the converter is tested on a deck it did not write. Only that
      sample is committed.

## Acceptance criteria

- [ ] `::: notes` under a `#` or `##` heading puts its text in the speaker notes of every slide the
  section produces, every part of a split slide included. The slide's kind and layout are the
  same as without it.
- [ ] Speaker notes keep paragraphs, bullet and numbered lists with levels, bold, italic, code and
  links. Their runs carry the document's language tag.
- [ ] Decks on the built-in master, on a brand template and on a caller template without a notes
  master all get a notes master when they have speaker notes. A caller template's own notes master
  is used as it is.
- [ ] A deck without speaker notes, markers or transitions is byte-identical to the one the
  previous version wrote.
- [ ] `{.build}`, `{.build=appear|fly|wipe}`, `{.frag}`, `{.exit}` and `{.emphasize}` produce
  PowerPoint's own effects in document order:
  - on content slides by paragraph;
  - on visual slides for the text above, the visual and the text below;
  - on flow slides by block or by card, row, column or image.
- [ ] `build: lists` builds every top-level list item by item.
- [ ] A stray marker, an unknown `{…}`-only paragraph, a nested `::: notes` and an unknown
  `transition` throw `DocumentConversionException` in both formats.
- [ ] Each of the ten transition values writes the matching `p:transition` (none writes nothing).
  `morph` falls back to `fade`. A heading's value wins over `transition:` in front matter, and
  that over `PptxOptions.Transition`.
- [ ] `PptxConverter` writes a slide's speaker notes as `::: notes`, and the generated
  `presenting.md` round-trips to its expected Markdown.
- [ ] PDFs leave speaker notes, markers and transitions out and are otherwise unchanged.
- [ ] Every generated test deck passes the Open XML validator (`Microsoft365`).
- [ ] **Checked in real PowerPoint** (Windows, through the export script and by hand), and the
  result recorded in the implementation notes:
  - every deck opens without a repair prompt;
  - speaker notes show in the Notes pane and in Notes Page view, on the built-in and a caller
    notes master;
  - every effect plays in the slide show in the expected order and shows its proper name in the
    Animation pane;
  - every transition plays;
  - Morph works between two slides with matching shapes, and the fallback shows in a viewer
    without Morph (or an older PowerPoint, if available).
- [ ] The guides, expected guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Split this story? It is three features of very different size: speaker notes with the converter
   (about 150 lines in the fork plus the converter), transitions (about 100) and builds (about 680).
   Proposed: E07-S01 keeps speaker notes and transitions, and a new E07-S05 *Builds* takes steps 1
   (markers), 2 (builds), 5 and the build parts of 9–11, implemented after E07-S01. E07-S04 then
   depends on both. This plan stays one file until the split is agreed.

>

2. A `##` section that produces several slides without being split (text, then two tables, so two
   visual slides): its speaker notes on every one of them (proposed, consistent with split slides),
   or only on the first?

>

3. Blocks in `::: notes` other than paragraphs and lists (tables, code blocks, images, quotes,
   alerts): write them as plain text with a new note `notes-simplified` (proposed), or throw?

>

4. PDFs: leave speaker notes out silently (proposed), or add an option, such as
   `PdfOptions.SpeakerNotes`, that prints them as a quote under the section, for a handout?

>

5. Should the PDF accept the front-matter keys `transition` and `build`, check their values and
   ignore them, so one Markdown serves both formats (proposed)? Or reject them as deck-only, as it
   rejects `listBullets` today?

>

6. A paragraph that is only `{…}` with a class other than the build markers, such as `{.note}`:
   throw and list the valid markers (proposed, as for heading attributes), or leave it as text?
   Throwing is a breaking change for documents that have such a paragraph.

>

7. The effects: `{.build}` as Appear, `fly` from the bottom, `wipe` from the left, `{.exit}` as
   Disappear and `{.emphasize}` as Pulse (proposed: Pulse leaves nothing changed after it plays and
   works on any shape). Or should `.emphasize` change the colour to the accent, as a lasting
   highlight?

>

8. Transition options: only the names, with PowerPoint's default direction and speed (proposed), or
   also a direction and speed (`transition="push left slow"`)?

>

9. The converter reads speaker notes by default (proposed, as the analysis says; a breaking change to
   its output), or only with a new `ConversionOptions.IncludeSpeakerNotes`?

>

10. Speaker notes repeated on continuation slides come back from the converter once per slide.
    Leave them repeated (proposed: the converter reads slides, not the Markdown they came from), or
    drop a slide's notes when they equal the previous slide's and its title is the same?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
