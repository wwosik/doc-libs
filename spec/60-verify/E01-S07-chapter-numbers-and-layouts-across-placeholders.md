# E01-S07 Chapter numbers and layouts across placeholders

> Status: in verification
> Epic: [E01 Brands and templates](../50-todo/E01-brands-and-templates.md)

## Summary

Two opt-in uses of the template roles from [E01-S06](E01-S06-template-roles-for-decks.md):

- **Numbered chapter dividers.** Every `#` divider after the cover gets "01", "02", … in the
  section layout's chapter-number placeholder.
- **`layout="…"` across placeholders.** `## Title {layout="…"}` fills all of the layout's
  placeholders, not just its first body:
  - text split at `---` goes into its text placeholders in order;
  - images go into its picture placeholders, cropped to fill them;
  - charts go into its chart placeholders.

## Context

- **Dividers today.**
  - `DeckParser.Slides` makes every `#` after the cover a `TitleSlide` with `IsSection`. The text
    below it becomes its subtitle.
  - `WriteTitleSlide` writes the title into the title placeholder and the subtitle into
    `subTitle` or the next body.
  - Nothing numbers sections. The fork numbers them and treats content under a `#` as an error.
    Here content stays the subtitle (analysis, behaviour difference 2).
- **`layout="…"` today** (E01-S05):
  - `HeadingAttributes` reads `layout`, and `DeckWriter.LayoutOf` finds the layout by name,
    case-insensitively, or throws.
  - A `##` whose only attribute is `layout` stays a `ContentSlide` or `VisualSlide`
    (`SectionBuilder`). The body goes into the first body placeholder (`FindPlaceholder(layout,
    IsBody)`). A visual goes into the content area as a shape (`WriteVisualSlide`), and a second
    visual starts a continuation slide.
  - A second body, a picture placeholder or a chart placeholder of the layout stays unused.
- **`---` today.**
  - `Walk` ignores a `ThematicBreakBlock`, except inside `::: columns` (`DeckParser.Columns`),
    which splits at it.
  - A `---` right after a paragraph line is a setext heading, so a blank line must come before it
    (trap in `ai/memory.md`).
- **Pictures and charts.**
  - `DeckWriter.Picture` fits an image into a box keeping its aspect ratio, enlarged up to
    `PptxOptions.MaxImageUpscale`.
  - `Chart` writes a `p:graphicFrame` with the chart part from `ChartWriter`.
  - Neither is ever written as a placeholder (`p:nvPr/p:ph`).
  - Behaviour difference 5 of the analysis: crop into a picture placeholder only where the
    template declares one. `focus`/`fit` to control the crop come with E04-S05.
- **Fitting** (`DeckWriter.Fitting.cs`) shrinks content slides with `normAutofit` and splits them
  between list groups. It knows nothing about several placeholders.
- **Notes** (E05-S03): every code needs an entry in `NoteCodes.All` and a row in the docs table.
  The fork's code for a picture cut by its placeholder is `image-cropped`.

## Proposed approach

1. **Chapter numbers.**
   - **Switching it on.**
     - `PptxTemplateRoles` (E01-S06) gains `NumberChapters` (`bool`, default `false`), so a
       design can number its dividers. Brand pack key `pptx.numberChapters`.
     - Front matter `numberChapters: true | false` overrides it on every template; it is added to
       each template's `Keys` with its guide text.
   - **Counting.** Every `#` after the cover counts, from 01. Neither the cover nor the
     `{.closing}` slide does.
   - **Format.** At least two digits ("01" … "09", "10", …), invariant digits.
   - `TitleSlide` gains `Number` (`int?`), set by the parser when numbering is on. E02-S05's
     agenda reads it.
   - **Where the number goes.**
     - The section layout's `ChapterNumber` placeholder (E01-S06), declared or the built-in
       master's.
     - A section layout without one gets the number as a text box above the title area. It is
       set in the title's font at the size of the subtitle, in the palette's accent colour
       (question 2).
   - Content under a `#` stays its subtitle, as today.

2. **Layouts across placeholders.** A `##` with `layout="…"` becomes a new `PlaceholderSlide`
   when either is true:
   - the section has a top-level `---`;
   - the layout has a picture or chart placeholder (E01-S06 discovery) and the section has an
     image or chart.

   Otherwise it stays a content or visual slide, exactly as today.

   ```csharp
   internal sealed record PlaceholderSlide(IReadOnlyList<TextRun> Title, IReadOnlyList<IReadOnlyList<TextParagraph>> Texts,
       IReadOnlyList<PlaceholderVisual> Visuals) : Slide(Title);   // Layout set from the heading
   ```

   - **Text.** The section's text is cut at top-level `---` into parts. The parts fill the
     layout's text placeholders in their E01-S06 order: declared `Body` roles, else reading order,
     with the subtitle counted first.
   - **Visuals.** Images, image rows, charts and rendered drawings fill the visual placeholders in
     order of appearance, whatever the `---`:
     - an image goes into a `Picture` placeholder, else an `obj` body;
     - a chart goes into a `chart` placeholder, else an `obj` body;
     - drawings and tables go only into `obj` bodies.

     An `obj` body that takes a visual is no longer a text placeholder.
   - **Too much content.** More text parts or visuals than there are placeholders continue on a
     further slide on the same layout. The usual `slide-split` note says so.
   - **Too little content.** Placeholders left over stay empty and are not written, so the slide
     shows none of their prompts.
   - Byline, chart title, unit and source placeholders are filled only by E02-S05's slide kinds.
     Here they stay empty.
   - Containers and alerts in such a section throw `DocumentConversionException`: "layout="…"
     with `---` takes text, images and charts; use a flow slide for containers". The same
     applies to `.dark` and `kicker` combined with a split (question 4).

3. **Writing into placeholders.**
   - **Text.** `Placeholder(…)` per part, with the template's levels, or E01-S06's text levels
     when declared.
   - **Pictures.** A `p:pic` whose `p:nvPr` carries the placeholder's `ph` (`type="pic"` and its
     `idx`), without its own `spPr` transform, so it takes the layout's box. The image is cropped
     to the box's aspect ratio, centred, with `a:srcRect`. A crop of more than 10 % of either side
     gives a new note `image-cropped`, naming the image and what to change. `scale=` does not
     apply in placeholders.
   - **Charts.** `p:graphicFrame` with the chart part and the `ph` of the chart placeholder.
     PowerPoint takes the frame's size from the layout. Writing an explicit `p:xfrm` with the
     layout's box as well is safer for other viewers; this is to be checked in PowerPoint and
     LibreOffice.
   - **Visuals in `obj` bodies** are placed as today's shapes inside that body's box (fitted, not
     cropped), not as placeholders.

4. **Fitting.**
   - Each text placeholder is fitted on its own: shrunk by `normAutofit` steps to 12 pt like
     content slides. A part that still does not fit gives `slide-overfull`. Parts are not split
     between slides, since the writer's `---` is a deliberate cut.
   - Footnotes keep their room below the content area only on layouts without bodies at the foot.
     Otherwise they go below the lowest text placeholder.

5. **PDF.**
   - `numberChapters` is accepted and ignored. PDF heading numbers are E03-S02's.
   - A `---` in a section with `layout` stays what the PDF does with a thematic break today.

6. **Guides, docs, changelog.**
   - The deck guide gets these, and the expected guides are regenerated:
     - `numberChapters` in the front-matter table;
     - a paragraph on `layout="…"` with `---`, with an example and the blank-line rule;
     - `image-cropped` in *Notes you may get back*.
   - The PPTX docs describe both features and the order in which placeholders are filled.
   - `CHANGELOG.md`:
     - **Breaking:** a `##` with `layout="…"` on a layout with a picture or chart placeholder now
       puts its image or chart into that placeholder, cropped, instead of fitting it into the
       content area. A `---` in a `layout="…"` section now splits its text.
     - **New:** numbered chapters, `layout="…"` across placeholders, `image-cropped`.

7. **Tests.**
   - Chapter numbers:
     - on the built-in master, in its placeholder;
     - on `brand-dark`, whose section layout has none, so in the text box;
     - front matter overriding the brand;
     - cover and closing slides not counted;
     - off by default, with every existing round trip unchanged.
   - Placeholder slides on the `brand-roles` test template (E01-S06):
     - two text parts into two bodies;
     - three parts into two bodies, continuing on a second slide;
     - an image cropped into a picture placeholder, with its `srcRect` and the note;
     - a chart in a chart placeholder;
     - a table in an `obj` body;
     - a container throwing;
     - a `layout="…"` section without `---` or a visual staying a content slide.
   - `Samples.AssertValid` on every new deck.
   - **In PowerPoint:** picture and chart placeholders open without repair, keep the layout's box,
     and *Reset slide* keeps the content.

## Acceptance criteria

- [ ] With `numberChapters`, dividers after the cover show "01", "02", … in the chapter-number
  placeholder, or above the title where the layout has none. Without it, decks are unchanged.
- [ ] `## T {layout="…"}` with `---` fills the layout's text placeholders in order, and its
  images and charts go into picture and chart placeholders. Images are cropped to fill, with
  `image-cropped` when much is cut.
- [ ] Surplus content continues on a further slide on the same layout, with `slide-split`.
  Unused placeholders are not written.
- [ ] Sections without `layout`, and `layout` sections without `---` or a visual for a picture or
  chart placeholder, come out as before.
- [ ] Decks pass the Open XML validator and open in PowerPoint without repair.
- [ ] The guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Who switches chapter numbers on: the design (`PptxTemplateRoles.NumberChapters`, the brand
   pack) with front matter to override it (proposed), or only the document (front matter /
   `PptxOptions`)? A design whose dividers have a number placeholder usually wants it filled.

> Answered by the human (2026-11-14): as proposed.

2. A section layout without a chapter-number placeholder: the number in a text box above the
   title (proposed), the number prefixed to the title ("01 Market"), or no number with a note?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Number format: two digits from "01" (proposed, as the fork), or plain "1", "2", or a pattern
   in the roles (`"00"`, `"0."`)?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Containers, alerts, `.dark` and `kicker` in a split `layout="…"` section: throw (proposed:
   placeholders have no room for flow blocks, and an error says so at once), or fall back to a
   flow slide on that layout with a note?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Images in picture placeholders: cropped to fill, centred (proposed, as the fork and PowerPoint
   do), or fitted inside like everywhere else, with cropping only after E04-S05's `fit=cover`?

> Answered by the human (2026-11-14): as proposed.

6. Too many `---` parts: continue on a further slide on the same layout (proposed, as E02-S04
   does), or join the surplus to the last placeholder?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

- **Images and charts go into picture and chart placeholders** (question 5, accepted): a `##` with
  `layout="…"` on a layout that has a picture or chart placeholder, whose image or chart (and every
  other part of the section) finds a placeholder there, becomes a placeholder slide: the image is
  cropped to the placeholder's shape instead of being fitted whole into the content area, the
  chart takes the placeholder's box, and the text goes into the layout's text placeholders instead
  of a text box above the visual. Only brand-own and caller templates are affected (`layout="…"`
  is an error on the built-in master). In `CHANGELOG.md`.
- **`---` in a `layout="…"` section** now cuts its text, where it was dropped; containers, alerts,
  `.dark` and `kicker` in such a section now throw, and so does content the layout has no
  placeholder for. In `CHANGELOG.md`.
- **`numberChapters` is a new front-matter key** of every deck template and of PDFs; it changes
  nothing unless set, but hosts that list keys (and the guides' front-matter tables) show it.
- Nothing else: with numbering off (the default everywhere, the built-in master included) and
  without `layout="…"`, decks are unchanged part by part (see *Verification*).

### Decisions and deviations

- **Model.** `PlaceholderSlide(Title, Items)` holds one ordered list of `PlaceholderItem`s (a text
  part or one `PlaceholderVisual`) instead of the planned separate `Texts` and `Visuals`: texts and
  visuals are placed in the order they appear, each into the first free placeholder that takes it,
  which is what decides who gets an `obj` body (text before an image takes the first one).
  `PlaceholderFill.Distribute` is the one assignment, shared by the parser's check, fitting and the
  writer. Slots in order: the subtitle, the bodies in their E01-S06 order (`obj` and **untyped**
  bodies are `obj` slots, since an untyped placeholder is OOXML's `obj`; `body` ones take text
  only), then pictures, then charts. An image looks for a picture slot first, a chart for a chart
  slot, then both for an `obj` body; tables and diagrams only `obj` bodies.
- **When a section becomes a placeholder slide.** With a top-level `---` always (errors as below);
  without `---` only when the layout has a picture or chart placeholder, the section has an image or
  chart, **and every item finds a placeholder** on that layout. The last condition is an addition to
  the plan: without it, an old section with an image and a table on a picture layout would start to
  throw. Otherwise the section comes out exactly as before: the parser records the `ITarget` calls
  once (`PlaceholderCollector`) and replays them into the `SectionBuilder`, so nothing is walked
  twice and no note repeats.
- **Errors** (`DocumentConversionException`): a container, alert, `.dark` or `kicker` with `---` and
  `layout` ("layout="…" with '---' takes text, images, charts, tables and diagrams (the slide 'T' at
  line n); use a flow slide, without '---', for containers, alerts, .dark and kicker="…"."), and in a
  split section an item the layout has no placeholder kind for ("The layout 'Picture' has no
  placeholder for text …"), checked in the parser and again in `Fit`. Without that check a
  continuation slide could never take the item.
- **Text parts** without text (two `---` in a row, a part with only an image) are left out. Each
  part is fitted in its own placeholder: `normAutofit` by 2 pt steps from the placeholder's level-1
  size (its own list style, else the master's body style) down to 12 pt, measured at that size
  (declared text levels are not measured per level here). A part that does not fit gives
  `slide-overfull`; there is no `slide-shrunk` for placeholder slides. Paragraphs use the template's
  levels, or the declared text levels, and `ListBullets.Always`, as on content slides.
- **Images in rows** (`![A](a.png) ![B](b.png)`) are one item per image, so a row fills several
  picture placeholders (the contact layout's five); a row marked `{.build}` builds image by image.
- **Pictures:** `p:pic` with `ph type="pic"` and the layout's `idx`, `noGrp` and `noChangeAspect`
  locks, an empty `spPr`, the crop from `ImageAttributes.Crop` at the box's aspect ratio (so
  `focus=` chooses what stays; without it, centred), `.grey` as elsewhere; `.round`, captions and
  `fit=` have no effect there. The `focus` "not cropped" note is not given while a section on a
  layout with picture placeholders is walked. `scale=` gives `attribute-ignored`. `image-cropped`
  when more than 10 % of the width or height is cut; `image-enlarged` as for other pictures.
- **Charts** in a chart placeholder: `p:graphicFrame` with the chart's `ph`, `noGrp`, and an
  explicit `p:xfrm` of the placeholder's box less one line for the chart's source, which is written
  as today's text box under it (the chart-source placeholder is E02-S05's).
- **Footnotes** of a placeholder slide go below the lowest placeholder written (a simplification of
  the plan's "below the content area unless bodies reach the foot").
- **Chapter numbers:** `TitleSlide.Number` (1-based), formatted `00` with invariant digits. In the
  layout's `ChapterNumber` placeholder, else a text box whose bottom is the title area's top (at
  least at the slide's top edge), as wide as the title, `+mj-lt`, at the level-1 size of the
  subtitle placeholder (else the first body, else 18 pt), in `SlidePalette.Accent` (the brand's
  `Link`, theme accent 1 on caller templates). Numbering is front matter `numberChapters` ?? the
  template's `PptxTemplateRoles.NumberChapters` (the built-in master: `false`).
- **`PptxGenerator`** now opens the `DeckWriter` before parsing (the deck title moved from the
  constructor to `Write`), because the parser asks it for a layout's slots (`DeckWriter.Slots`,
  `null` on the built-in master or for an unknown layout, which the writer then reports as before).
- **PDF:** `numberChapters` is accepted and ignored like `transition` and `build`, with a row in the
  PDF guide's front-matter table; a `---` stays what it is in PDFs.
- **Guide:** the deck guide's word-limit test is now 3,500 words, the human's decision of
  2026-11-14 (the sample guide reached 2,649 with the new key row and note); no guide text was cut.
  The paragraph on `---` (with a `text` example, not a `markdown` one, since the layout name depends
  on the template and the examples are generated with every guide's options) appears for brand-own
  and caller templates only, where `layout="…"` works. Expected guides regenerated: PPTX sample and
  custom, PDF neutral and sample.
- **A loose consistency:** a plain content slide on a layout with a declared body order still uses
  the first body in tree order (E01-S06, unchanged); a placeholder slide uses the declared order.

### Verification

- After rebasing onto E04-S08 (`64b59c0`): `./run.sh build` without warnings of this story (one
  `CS8602` in E04-S08's `StepsAndRisksTests` remains); `./run.sh test` all green: 1, 123, 251, 78,
  71, 22, 197 and 337 passed (1,080), 6 Windows-only skipped.
- New `PlaceholderLayoutTests` (19): numbering in the parser (cover and closing not counted, off by
  default), the built-in master's placeholder (both brands), the text box on `brand-dark`, the
  design switching it on and front matter switching it off; on `brand-roles` two parts into two
  bodies in declared order, three parts continuing on a second slide with `slide-split` and the
  unused placeholder not written, an image cropped into the picture placeholder (`srcRect`, no
  `xfrm`, `image-cropped`), `focus=left` and an image row filling two contact pictures, a chart in
  the chart placeholder with the layout's box, a table in an `obj` body, containers, alerts, `.dark`
  and `kicker` throwing, text on a picture-only layout throwing, layout sections without split or a
  fitting visual staying as before, and a text part shrunk and reported overfull. Brand pack
  `numberChapters` and equality in `BrandPackTests`, the PDF accepting the key in `PresentingTests`.
- **Open XML validator** (`Microsoft365`): every new deck passes (`Samples.AssertValid`).
- **Unchanged decks:** every sample (`deck`, `layouts`, `presenting`, `presenting-lists`,
  `schedules`, `steps-and-risks`) plus a deck with `---` in a plain section and `layout="…"`
  sections, generated by this branch and by `origin/main` (`06c2d5d` before the rebase, `64b59c0`
  after it, from `git archive`) on the neutral and sample brands, a
  caller template and a brand-own template (both a deck of the built-in master), fixed date,
  relationship ids masked: every part is identical except the one slide with an image on
  `layout="Picture"` (the accepted breaking change). All round-trip expectations are unchanged.

### To verify by hand (no PowerPoint on this machine)

The tests write the decks to `.tmp/review/`.

1. **No repair prompt** opening `chapters-neutral.pptx`, `chapters-sample.pptx`,
   `chapters-brand-dark.pptx`, `placeholders-two-parts.pptx`, `placeholders-picture.pptx`,
   `placeholders-chart.pptx` and `placeholders-table.pptx`.
2. **Chapter numbers:** in `chapters-neutral.pptx` and `chapters-sample.pptx` the dividers *Market*
   and *Plan* show "01" and "02" in the white chapter-number box above the title; the cover and
   *Thank you* show none. In `chapters-brand-dark.pptx` the number sits just above the title, in the
   heading font, in the accent colour, at the subtitle's size, without overlapping the title.
3. **Picture placeholder** (`placeholders-picture.pptx`): the wide image fills the *Picture*
   layout's box, cropped left and right, undistorted; *Reset slide* (Home → Reset) keeps the picture
   in the placeholder with its crop; *Picture Format → Crop* shows the crop as editable.
4. **Chart placeholder** (`placeholders-chart.pptx`): the chart sits exactly in the *Figure*
   layout's chart box, opens with *Edit Data*, and *Reset slide* keeps it there. Also open the deck
   in LibreOffice Impress: the chart keeps that box (the explicit `p:xfrm`).
5. **Two bodies** (`placeholders-two-parts.pptx`): "Basic" in the right column (declared first on
   `brand-roles`), "Premium" in the left; neither shows a placeholder prompt; *Reset slide* keeps
   both.
6. **Table in an `obj` body** (`placeholders-table.pptx`): the table sits inside the left column's
   box and the text in the right one; the left placeholder's prompt does not show behind the table.

### Loose ends

- The acceptance "opens in PowerPoint without repair" depends on item 1.
- E02-S05 fills byline, chart title, unit and source placeholders and the agenda (from
  `TitleSlide.Number`).
- Placeholder text is measured at the placeholder's level-1 size even with declared text levels.
