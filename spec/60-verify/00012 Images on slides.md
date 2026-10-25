# 00012 Images on slides

> Status: verify — implemented 2026-10-25

## Summary

Images on slides fill the room they have — scaled up as well as down, keeping their aspect ratio —
and several images can share one slide: side by side, with equal widths, and an image can sit
beside a column of text. The common case is a management deck of two charts per slide, supplied as
PNGs.

## Context

- `DeckWriter.Picture` places an image at its natural size (`EmuPerPixel` × the `scale=` title
  attribute) and only scales it **down** to fit (`Math.Min(1.0, …)`). A chart exported at 800 px
  comes out a third of the slide wide, even when the slide is empty below it. Drawings do the same.
- `DeckParser`: on a content slide, every image is a visual; the second visual of a section starts a
  continuation slide. Two images in one paragraph (`![](a.png) ![](b.png)`) become two slides. On
  flow slides they stack vertically. The only way to put two images side by side today is two
  `::: cards`, each with a title and an outlined box around the image.
- There is no syntax for columns; "two columns" was left out of 00005.

## Proposed approach

### 1. Images fill their box

- An image or drawing that is the only visual on a slide fills the room left after the text above and
  below: the largest size that keeps its aspect ratio, **up or down**, centred.
- `scale=` in the image title keeps working as an upper bound (`scale=1` = never larger than natural
  size), so a writer can stop a small icon from being blown up.
- New option `PptxOptions.MaxImageUpscale` (default `4.0`): no image is enlarged beyond 4 × its
  natural size, so low-resolution images do not turn into blurs. Drawings (vectors) have no limit.

### 2. Images side by side

- **A paragraph of only images** (separated by spaces or soft line breaks) is one row of images:
  equal widths, sharing the content width with a gap between them, each fitted into its cell, the
  row as tall as the room allows. Works on content slides (no continuation slide) and flow slides.
- Alt text becomes a caption under each image when the writer asks for it (`"caption"` in the image
  title, e.g. `![Orders by month](orders.png "caption")`); otherwise there is no caption.
- Three or four images in a paragraph: one row up to 4; more than four wrap to a second row.
- In PDFs, the same paragraph is a row of images across the text width (the layout engine places
  images side by side when they fit; otherwise one below the other, as today).

### 3. Columns: text beside an image

- New container `::: columns`, its columns separated by a thematic break (`---`) inside the
  container:

  ```markdown
  ::: columns
  Stock levels stayed within plan all year.

  A new review cycle starts next quarter.

  ---

  ![Stock levels](stock.png)
  :::
  ```

  Each part is a column of blocks; widths are equal unless the container says otherwise
  (`::: columns 1:2`). Two or three columns. The slide becomes a flow slide.
- In PDFs, `columns` shows its parts one below the other (as other containers do).
- The authoring guide (00008) gets the row-of-images rule and `columns`, with an example each.

### 4. Tests, docs

- Placement tests: a single image upscaled to the content width or height; `scale=` and
  `MaxImageUpscale` caps; two images of different aspect ratios in one row (equal cell widths, same
  top); four and five images; `columns` with text and an image; `columns 1:2`.
- Round trips for a new sample section in `layouts.md` (two images in a row, a `columns` slide).
- Docs: *Images* and *Layouts* in the PPTX page, the PDF page's image paragraph, the authoring guides.

## Acceptance criteria

- [ ] A lone image fills its slide's free area, up to `MaxImageUpscale`.
- [ ] Images in one paragraph share a slide, side by side, with equal widths.
- [ ] `::: columns` puts text beside an image (or any blocks beside each other).
- [ ] PDFs accept the same Markdown; nothing that generated before fails.
- [ ] Tests, docs and authoring guides updated; the sample deck looked at in PowerPoint.

## Breaking changes

- Images in decks get bigger (they are scaled up). Two images in one paragraph now share a slide
  instead of taking one each. `scale=1` restores natural size per image.

## Open questions

1. Separator between columns: a thematic break `---` inside `::: columns` (proposed: plain Markdown,
   readable in previews), or nested containers (`:::: column` … `::::`)?

> Answered by the human (2026-10-25): a thematic break `---` inside `::: columns`.

2. Upscale limit: 4 × natural size (proposed), or none?

> Answered by the human (2026-10-25): 4 × natural size (`MaxImageUpscale`).

3. Captions under side-by-side images: only on request (proposed) or always from the alt text?

> Answered by the human (2026-10-25): only on request (`"caption"` in the image title).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- `SlideImage.Scale` is now a cap (`double?`); `PptxOptions.MaxImageUpscale` (4) applies without it.
  Images rendered by code-block renderers keep `scale=1`.
- A paragraph of only images is one visual (`VisualSlide.Images`, `ImageRowBlock`); more than four
  wrap into further rows that share the height. Captions work on any image, not only in rows.
- On flow slides, images, drawings and charts share the height the other blocks leave.
- `::: columns` splits at thematic breaks; a `---` needs a blank line before it, or Markdown reads the
  line above as a heading (documented in the guide).

### Deviations from the plan

- **Drawings are enlarged at most 1.5 times**, not without limit: their text grows with them, and a
  small Mermaid diagram filling a slide had 40-pt labels.
- **`layouts.md`** got a `columns` slide of text only; images side by side and columns with an image
  are covered by `ImageLayoutTests` instead, so the shared sample needs no image resources.
- PDFs place a row of images in equal cells, each only scaled down, as PDFs never enlarge images.

### Breaking changes

- Images in decks get bigger; images in one paragraph share a slide. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
