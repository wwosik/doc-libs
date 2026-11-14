# E01-S07 Chapter numbers and layouts across placeholders

> Status: in progress (doc-3)
> Epic: [E01 Brands and templates](../../50-todo/E01-brands-and-templates.md)

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
