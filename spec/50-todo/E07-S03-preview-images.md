# E07-S03 Preview images

> Status: todo
> Epic: [E07 Presenting](E07-presenting.md)

## Summary

Each slide of a deck as a PNG, and a contact sheet of all slides as one image, painted from the
`.pptx` with SkiaSharp, with no browser and no Office. The slides are the SVGs E07-S02's reader
writes, painted by E04-S04's `SvgConverter`, so a preview shows exactly what the HTML deck shows.
Hosts use them for thumbnails, chat replies, review pages and documentation.

## Context

- **E07-S02** (planned before this story) reads a deck's DrawingML into a model of positioned
  shapes, pictures and text already broken into lines, resolving placeholders, text styles, theme
  colours, backgrounds and style references from layout and master, and writes one SVG per slide
  (`SceneSvg`). Its SVG uses only what Svg.Skia also draws: paths, gradients, clip paths, `data:`
  images, `<symbol>`/`<use>`, `<text>`/`<tspan>` at fixed baselines. It reports what it cannot draw
  as `drawing-unsupported` and missing faces as `font-missing`.
- **E04-S04** gives `wwosik.DocLibs.DocumentConversions.Svg`:
  - `SvgConverter.ToPng(ReadOnlySpan<byte> svg, SvgPngOptions?)` with `Scale` (default 2) or
    `Width`, `Background`, `Fonts`;
  - `SvgConverter.MaxPixels` = 16384 per side;
  - its own typeface provider matching passed fonts by family name and nearest weight (Svg.Skia's
    own matches width exactly);
  - `SvgSanitizer` in front of every load, which keeps `data:` URIs and internal links.

  SkiaSharp 4.153.1 and Svg.Skia 5.2.3 are in `Directory.Packages.props`; the managed SkiaSharp
  must match `SkiaSharp.NativeAssets.Linux` (memory, *Traps*). The package is AOT-compatible.
- **Fonts:** decks embed brand faces as EOT (E01-S03), and E07-S02 extracts the TTF from each.
  Without brand fonts, a deck names Arial and Consolas. E07-S02 proposes moving Liberation Sans and
  Liberation Mono from the PDF package to DocumentGeneration as stand-ins. Svg.Skia falls back to
  the system's default typeface for an unknown family, which on a Linux container without
  fontconfig may be none.
- **Decided:**
  - in the analysis (question 8): a Skia painter of the PPTX, no browser;
  - also in the analysis (question 9): SkiaSharp is accepted as a dependency of the generators;
  - in the epic: previews are painted with SkiaSharp, not in a browser.
- **Real Office:** PowerPoint exports slides as PNG through COM (`.tmp/scripts/export.ps1`,
  `SaveAs` format 18), which hangs while another PowerPoint has a dialog open (memory, *Traps*).
  Test samples are self-made only (`tools/sample-generator`); PNGs exported from our own sample
  decks are self-made too.

## Proposed approach

1. **Architecture: one reader, one drawing, two hosts.**
   - E07-S02's reader and `SceneSvg` are the only DrawingML code. This story adds no second
     interpretation of DrawingML: the HTML page shows the SVG, the preview paints it with
     Svg.Skia.
   - Text is broken into lines by the reader with the same font metrics both ways, so line breaks
     are the same in the HTML deck and the PNG.
   - If painting through SVG falls short (question 1), a direct `SKCanvas` backend over the same
     `DeckScene` model can replace it later without changing the API.
   - **Order:** E07-S02's reader and SVG (its first part, if it splits) → this story → E07-S02's
     presenting parts. The golden images of this story also become E07-S02's pixel tests.
2. **Package.** A new package `wwosik.DocLibs.DocumentGeneration.Pptx.Preview`.
   - References `wwosik.DocLibs.DocumentGeneration.Pptx` (the reader, reached through
     `InternalsVisibleTo`, as the generator packages already reach DocumentGeneration) and
     `wwosik.DocLibs.DocumentConversions.Svg` (`SvgConverter`, and SkiaSharp for the contact sheet).
     No new entry in `Directory.Packages.props`.
   - The Pptx package itself gets no native dependency, whatever E04-S05 decides for image styles
     (see question 2).
   - The test project references `SkiaSharp.NativeAssets.Linux`, like the Svg tests. The docs say
     which native-assets package a Linux host adds, as the Svg page does.
   - Checked for Native AOT like the Svg package; if the Open XML SDK warns, the package is marked
     not AOT-compatible and the docs say so.
3. **API.**
   - `public static class DeckPreview` in namespace `wwosik.DocLibs.DocumentGeneration.Pptx.Preview`:
     - `DeckPreviewResult Render(Stream pptx, DeckPreviewOptions? options = null)`: every visible
       slide as PNG bytes, in order.
     - `byte[] ContactSheet(Stream pptx, ContactSheetOptions? options = null)`, and
       `ContactSheet(DeckPreviewResult slides, ContactSheetOptions? options = null)` to reuse
       rendered slides.
   - `DeckPreviewResult`: `Slides` (`IReadOnlyList<SlidePreview>` with `Number`, `Title` and
     `Png`) and `Notes` (the reader's notes); it follows `GenerationResult`'s shape (`Pages`,
     `Notes`, `HasNotes`).
   - `DeckPreviewOptions` (record, init-only):
     - `Width`: pixels per slide, default 1280 (720 high on 16:9); the height follows the slide's
       aspect ratio. At most `SvgConverter.MaxPixels`.
     - `Fonts`: TrueType files for families the deck does not embed, as in `HtmlDeckOptions.Fonts`.
     - `Slides`: optional slide numbers to render (a thumbnail of slide 1 only); default all.
     - `Background`: what is behind a slide without a background; default white, as PowerPoint.
   - `ContactSheetOptions`: `Columns` (default 4), `ThumbnailWidth` (default 320 px), `Gap`
     (default 16 px), `Labels` (default `true`: the slide number under each thumbnail, set in the
     stand-in sans face), `Background` (default light grey `F2F2F2`).
   - Errors: a package that is not a presentation throws `DocumentConversionException` with
     `Format = Pptx`, as in E07-S02; a slide that Svg.Skia cannot paint throws
     `DocumentConversionException` with `Format = Svg` naming the slide.
4. **Painting a slide.**
   - The reader's SVG for the slide (final build state: everything shown that some step shows, so
     a build slide looks complete) goes to `SvgConverter.ToPng` with `Width` and the faces in use.
   - **Fonts** passed to Svg.Skia, in order: the deck's embedded faces, `DeckPreviewOptions.Fonts`,
     the stand-ins (Liberation Sans for Arial and Helvetica, Liberation Mono for Courier New and
     Consolas), each registered under the family the slide names, so Svg.Skia never falls back to
     a system face for a family the reader measured. A family with no face is painted in Liberation
     Sans; the reader has already reported `font-missing`.
   - **Pictures:** the PNG/JPEG/GIF of the picture part, not the `svgBlip` alternative, so a slide
     is never an SVG nested inside an SVG in Svg.Skia.
   - Hyperlinks, `data-id` groups and anything only the HTML uses are ignored by Svg.Skia and
     cost nothing.
5. **Contact sheet.**
   - Painted with SkiaSharp directly: each slide's PNG scaled to `ThumbnailWidth` with
     high-quality sampling, in rows of `Columns`, a hairline border around each, the label under
     it.
   - A sheet taller than `SvgConverter.MaxPixels` is refused with an `ArgumentException` that names
     the largest thumbnail width that fits (see question 6).
6. **Calibration first.** Before any golden image is committed, the sample decks are exported from
   PowerPoint and painted here, and the text rules of E07-S02 (line pitch, first baseline, bullet
   positions) are adjusted until line breaks match and positions are within 2 px at 1280 px. What
   cannot be matched (kerning, hinting, effects) goes in the docs' known limits.
7. **Tests.**
   - **Unit:** size of the PNG from `Width` and the aspect ratio (16:9 and 4:3 decks); `Slides`
     picks slides; hidden slides are skipped; the final build state shows every built paragraph;
     a brand deck's embedded face is used (a glyph that only that face has is drawn, checked by
     pixels); a deck naming an unknown family paints in Liberation Sans; contact sheet columns,
     rows, labels and the size limit; errors for a non-presentation.
   - **Golden images,** two kinds:
     - **Own goldens** (`<sample>-<template>-<n>.expected.png` at 640 px in the test project's
       `Samples/`): our previous output, compared with a tight tolerance (mean absolute difference
       per channel ≤ 1 %, and no more than 0.5 % of pixels off by more than 25 %), so rasteriser
       differences between platforms pass and real changes fail. `DOCLIBS_UPDATE_EXPECTED=1`
       rewrites them, as for the `*.expected.md` files; the diff is reviewed as images.
     - **PowerPoint references** (`<sample>-<template>-<n>.office.png` at 640 px), exported once
       from the sample decks in PowerPoint and committed: compared with a loose tolerance on a
       blurred greyscale copy (a similarity score whose threshold is set during calibration and
       recorded in the test). A failure here means the preview drifted from what Office shows.
     - The samples: `deck.md` and `layouts.md` on the neutral, sample and dark templates, the E07-S02
       test template, and one deck each with a table, a chart, a Mermaid diagram, a financial chart
       and an SVG image; at most about 40 images, a few MB in all.
   - CI runs everything on Linux; the goldens were made on Linux and pass on Windows within the
     tolerance (checked once by running the tests on Windows).
8. **Real-Office comparison.** The PowerPoint references of step 7 are the comparison: the docs
   show one sample slide both ways, and every remaining difference is listed in the known limits.
   The exports are re-made by hand when a later story changes what the generator writes, through
   a documented step in the test project's README (the export script stays in `.tmp/`, since it is
   Windows-only and not a tool of the repo).
9. **Docs and changelog.**
   - A package page `docs/wwosik.DocLibs.DocumentGeneration.Pptx.Preview.md`: what it does, the
     API, sizes, fonts and stand-ins, native assets per platform and fonts in containers (as the
     Svg page), the relation to the HTML deck, known limits, and an example of both calls.
   - The package in `README.md` and the package table of `docs/README.md`; a pointer from the Pptx
     page's *HTML deck* section.
   - `CHANGELOG.md` under *New*: the new package. No existing output changes.

## Acceptance criteria

- [ ] `DeckPreview.Render` paints every visible slide of the sample decks as a PNG of the requested
  width, with no browser, no Office and no network.
- [ ] A slide's PNG and the same slide in the HTML deck break lines in the same places.
- [ ] Brand decks paint in their embedded faces; neutral decks in the Liberation stand-ins; a
  missing family is reported, never painted in a random system face.
- [ ] `DeckPreview.ContactSheet` lays out all slides in the requested columns with slide numbers,
  and refuses a sheet above the size limit with a message naming a width that fits.
- [ ] Own golden images pass on Linux and Windows; PowerPoint references pass at the calibrated
  tolerance, and the remaining differences are documented.
- [ ] SkiaSharp is referenced only through `wwosik.DocLibs.DocumentConversions.Svg` and the new
  package; the Pptx package gains no native dependency.
- [ ] Docs, README, the package table and CHANGELOG are updated, and all tests pass.

## Open questions

1. How is a slide painted? E07-S02's SVG through `SvgConverter` and Svg.Skia (proposed: one
   interpretation of DrawingML for HTML and PNG, little new code); or a second backend that draws
   the reader's model straight onto an `SKCanvas` (no SVG step, full control over text, but a
   second painter to keep in step)?

> Answered by the human (2026-11-14): as proposed.

2. Where does it live? A new package `wwosik.DocLibs.DocumentGeneration.Pptx.Preview` on top of
   the Pptx and Svg packages (proposed: the native dependency stays out of the generator); inside
   `wwosik.DocLibs.DocumentConversions.Svg`, which would then depend on the PPTX generator; or in
   the Pptx package itself, if E04-S05 already brings SkiaSharp there for image styles?

> Answered by the human (2026-11-14): Inside `wwosik.DocLibs.DocumentConversions.Svg`, not a new package (the human chose fewer packages).

3. Build slides: paint the final state only (proposed: a thumbnail should look complete); or add
   an option that paints each build step as its own image?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Default size: 1280 px wide per slide (proposed: sharp on a laptop screen, small enough for chat
   replies), or 1920 px?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Image formats: PNG only (proposed: lossless, what the analysis asked for); or also JPEG or WebP
   with a quality setting, for smaller files of photo-heavy decks?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. A contact sheet of a long deck that exceeds the size limit: refuse with a hint (proposed);
   shrink the thumbnails until it fits; or return several sheets?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Golden tests: commit both our own goldens (tight tolerance) and PowerPoint references (loose
   tolerance) as PNGs in the test project (proposed: a few MB, catches regressions and drift from
   Office); only our own goldens; or no images at all, only structural tests of the SVG?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Contact sheet labels: the slide number only (proposed: neutral, never too long), or number and
   title?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
