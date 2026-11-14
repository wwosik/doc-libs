# E07-S03 Preview images

> Status: in verification
> Epic: [E07 Presenting](../50-todo/E07-presenting.md)

## Summary

Each slide of a deck as a PNG, and a contact sheet of all slides as one image, painted from the
`.pptx` with SkiaSharp, with no browser and no Office. The Pptx package writes the slides as the
SVGs of E07-S02's reader; `wwosik.DocLibs.DocumentConversions.Svg` paints them with E04-S04's
`SvgConverter` and lays out the contact sheet, so a preview shows exactly what the HTML deck shows.
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
- **Package references today** (`src/*/*.csproj`): DocumentConversions is the base;
  DocumentConversions.Svg references only it (plus SkiaSharp and Svg.Skia); DocumentGeneration
  references DocumentConversions, and DocumentGeneration.Pptx references DocumentGeneration (plus
  the Open XML SDK and Markdig). Svg and Pptx do not reference each other. They meet through
  contracts in the base package: `IImageConverter` and `ConvertedImage` live in
  DocumentConversions, `SvgImageConverter` implements them, and `PptxOptions.ImageConverters`
  takes them. The Svg test project already references the Pptx project.
- **Where it lives** (question 2): inside `wwosik.DocLibs.DocumentConversions.Svg`, not a new
  package.
- **Fonts:** decks embed brand faces as EOT (E01-S03), and E07-S02 extracts the TTF from each.
  Without brand fonts, a deck names Arial and Consolas. E07-S02 moves Liberation Sans and
  Liberation Mono from the PDF package to DocumentGeneration as stand-ins, where the Svg package
  cannot reach them. Svg.Skia falls back to
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
   - **Order:** E07-S02 → this story → E07-S06 (presenting the HTML deck). The golden images of
     this story also become E07-S02's pixel tests.
2. **Packages and the dependency direction.**
   - The preview lives in `wwosik.DocLibs.DocumentConversions.Svg` (question 2), but the reader
     lives in `wwosik.DocLibs.DocumentGeneration.Pptx`. The Svg package does **not** reference the
     Pptx package: that would make every Svg user (the PDF and deck generators' SVG images
     included) take the Open XML SDK, Markdig and the generators, and turn the base conversion
     layer into a consumer of a generator. The Pptx package does not reference the Svg package
     either: it stays free of SkiaSharp's native dependency, whatever E04-S05 decides for image
     styles.
   - Instead the **slide SVGs are the hand-over**: the Pptx package writes them (step 3), the Svg
     package paints them and lays out the contact sheet (steps 4–6). A host that wants previews
     references both packages and makes two calls, as Markdown to HTML deck is two calls in
     E07-S02.
   - The one type both sides need, `SlideSvg`, goes into the base package
     `wwosik.DocLibs.DocumentConversions`, beside `IImageConverter` and `ConvertedImage`, so the
     result of one call is the argument of the other without mapping.
   - No new package and no new entry in `Directory.Packages.props`. The Svg package stays
     AOT-compatible: it gains no Open XML SDK.
   - The Svg test project, which already references the Pptx project and
     `SkiaSharp.NativeAssets.Linux`, holds the preview tests. The docs say which native-assets
     package a Linux host adds, as the Svg page does.
3. **Slide SVGs from the Pptx package.**
   - `public static class DeckSvg` in namespace `wwosik.DocLibs.DocumentGeneration.Pptx`:
     `DeckSvgResult Write(Stream pptx, DeckSvgOptions? options = null)`, a thin public entry point
     to E07-S02's internal reader and `SceneSvg`.
   - `DeckSvgOptions` (record, init-only): `Fonts` (TrueType files for families the deck does not
     embed, as in `HtmlDeckOptions.Fonts`), `Slides` (optional slide numbers, so a thumbnail of
     slide 1 reads and draws only slide 1; default all).
   - The SVGs are written for painting, not for the browser:
     - the final build state (everything shown that some step shows, so a build slide looks
       complete);
     - pictures as the PNG/JPEG/GIF of the picture part, never the `svgBlip` alternative, so a
       slide is never an SVG nested inside an SVG in Svg.Skia;
     - each run's `font-family` names the face the reader measured it with (the deck's face, a
       passed font or the stand-in), so Svg.Skia never falls back to a system face for a family
       the reader measured. A family with no face is written as Liberation Sans; the reader has
       already reported `font-missing`.
   - `DeckSvgResult` follows `GenerationResult`'s shape (`Pages`, `Notes`, `HasNotes`; the notes
     are the reader's `drawing-unsupported` and `font-missing`) and adds:
     - `Slides`: `IReadOnlyList<SlideSvg>`, visible slides in order;
     - `Fonts`: `IReadOnlyList<ReadOnlyMemory<byte>>`, each face the SVGs use once, as TTF (the
       TTF inside each EOT, passed fonts, the Liberation stand-ins);
     - `LabelFont`: the Liberation Sans regular face, for the contact sheet's labels.
   - `SlideSvg` (record in `wwosik.DocLibs.DocumentConversions`): `Number` (the slide's position in
     the deck), `Title`, `Svg` (`ReadOnlyMemory<byte>`), `Width` and `Height` (the slide's size in
     points).
   - A package that is not a presentation throws `DocumentConversionException` with
     `Format = Pptx`, as in E07-S02.
4. **API in the Svg package.**
   - `public static class DeckPreview` in namespace `wwosik.DocLibs.DocumentConversions.Svg`:
     - `DeckPreviewResult Render(IReadOnlyList<SlideSvg> slides, DeckPreviewOptions? options = null)`:
       each slide as PNG bytes, in order.
     - `byte[] ContactSheet(DeckPreviewResult slides, ContactSheetOptions? options = null)`.
   - The documented call, in both packages' docs:
     `var svgs = DeckSvg.Write(pptx); var previews = DeckPreview.Render(svgs.Slides, new() { Fonts = svgs.Fonts });`
     then `DeckPreview.ContactSheet(previews, new() { LabelFont = svgs.LabelFont })`.
   - `DeckPreviewResult`: `Slides` (`IReadOnlyList<SlidePreview>` with `Number`, `Title` and
     `Png`). It has no notes: what could not be drawn is in `DeckSvgResult.Notes`, and the Svg
     package does not reference DocumentGeneration, where `GenerationNote` lives.
   - `DeckPreviewOptions` (record, init-only):
     - `Width`: pixels per slide, default 1280 (720 high on 16:9); the height follows the slide's
       aspect ratio. At most `SvgConverter.MaxPixels`.
     - `Fonts`: the faces to paint with, normally `DeckSvgResult.Fonts`, as in
       `SvgPngOptions.Fonts`.
     - `Background`: what is behind a slide without a background; default white, as PowerPoint.
   - `ContactSheetOptions`: `Columns` (default 4), `ThumbnailWidth` (default 320 px), `Gap`
     (default 16 px), `Labels` (default `true`: the slide number under each thumbnail),
     `LabelFont` (TrueType bytes, normally `DeckSvgResult.LabelFont`; without one, the labels use
     SkiaSharp's default typeface, and a platform without one gets no labels), `Background`
     (default light grey `F2F2F2`).
   - Errors: a slide that Svg.Skia cannot paint throws `DocumentConversionException` with
     `Format = Svg` naming the slide number.
5. **Painting a slide.**
   - The slide's SVG goes to `SvgConverter.ToPng` with `Width` and the fonts.
   - Hyperlinks, `data-id` groups and anything only the HTML uses are ignored by Svg.Skia and
     cost nothing.
6. **Contact sheet.**
   - Painted with SkiaSharp directly: each slide's PNG scaled to `ThumbnailWidth` with
     high-quality sampling, in rows of `Columns`, a hairline border around each, the label under
     it.
   - A sheet taller than `SvgConverter.MaxPixels` is refused with an `ArgumentException` that names
     the largest thumbnail width that fits (see question 6).
7. **Calibration first.** Before any golden image is committed, the sample decks are exported from
   PowerPoint and painted here, and the text rules of E07-S02 (line pitch, first baseline, bullet
   positions) are adjusted until line breaks match and positions are within 2 px at 1280 px. What
   cannot be matched (kerning, hinting, effects) goes in the docs' known limits.
8. **Tests.**
   - **Unit, Pptx package:** `DeckSvg.Write` gives one `SlideSvg` per visible slide (hidden
     slides skipped), `Slides` picks slides, the final build state shows every built paragraph,
     pictures are raster and never `svgBlip`, every `font-family` names a face in `Fonts`, each
     face is in `Fonts` once, errors for a non-presentation.
   - **Unit, Svg package** (in its test project, decks generated through the Pptx project): size
     of the PNG from `Width` and the aspect ratio (16:9 and 4:3 decks); a brand deck's embedded
     face is used (a glyph that only that face has is drawn, checked by pixels); a deck naming an
     unknown family paints in Liberation Sans; contact sheet columns, rows, labels with and
     without `LabelFont`, and the size limit.
   - **Golden images,** two kinds:
     - **Own goldens** (`<sample>-<template>-<n>.expected.png` at 640 px in the Svg test project's
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
9. **Real-Office comparison.** The PowerPoint references of step 8 are the comparison: the docs
   show one sample slide both ways, and every remaining difference is listed in the known limits.
   The exports are re-made by hand when a later story changes what the generator writes, through
   a documented step in the test project's README (the export script stays in `.tmp/`, since it is
   Windows-only and not a tool of the repo).
10. **Docs and changelog.**
    - `docs/wwosik.DocLibs.DocumentConversions.Svg.md`: a section *Deck previews* with what it
      does, the API, the two calls with `DeckSvg`, sizes, fonts and stand-ins, native assets per
      platform and fonts in containers, the relation to the HTML deck, known limits, and an
      example.
    - `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md`: `DeckSvg` beside the *HTML deck* section,
      with a pointer to the Svg page; `docs/wwosik.DocLibs.DocumentConversions.md`: `SlideSvg`.
    - The Svg package's description in `README.md`, the package table of `docs/README.md` and its
      `.csproj` `Description` names deck previews.
    - `CHANGELOG.md` under *New*: `DeckPreview` in the Svg package, `DeckSvg` in the Pptx
      package, `SlideSvg` in DocumentConversions. No existing output changes.

## Acceptance criteria

- [x] `DeckSvg.Write` followed by `DeckPreview.Render` paints every visible slide of the sample
  decks as a PNG of the requested width, with no browser, no Office and no network.
- [x] A slide's PNG and the same slide in the HTML deck break lines in the same places.
- [x] Brand decks paint in their embedded faces; neutral decks in the Liberation stand-ins; a
  missing family is reported, never painted in a random system face.
- [x] `DeckPreview.ContactSheet` lays out all slides in the requested columns with slide numbers,
  and refuses a sheet above the size limit with a message naming a width that fits.
- [ ] Own golden images pass on Linux and Windows; PowerPoint references pass at the calibrated
  tolerance, and the remaining differences are documented.
- [x] The preview is in `wwosik.DocLibs.DocumentConversions.Svg`, which still references only
  DocumentConversions and stays AOT-compatible; the Pptx package references neither the Svg
  package nor SkiaSharp.
- [x] Docs, README, the package table and CHANGELOG are updated, and all tests pass.

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

- **Consequence of question 2's answer** (preview inside the Svg package): the reader that the
  preview needs is in the Pptx package, and the Svg package must not reference it (step 2), so the
  preview cannot read a `.pptx` itself. What it means for callers and the code:
  - Previews are **two calls in two packages**: `DeckSvg.Write` (Pptx) and then
    `DeckPreview.Render` / `ContactSheet` (Svg). A host references both. There is no
    `DeckPreview.Render(Stream pptx)`.
  - The Pptx package gains a public `DeckSvg` (the reader stays internal) and the base package
    gains one record, `SlideSvg`, so the two calls fit together.
  - The notes about what could not be drawn come from `DeckSvg.Write`, not from the preview.
  - The Liberation stand-ins sit in DocumentGeneration, out of the Svg package's reach, so they
    travel in `DeckSvgResult.Fonts` and `LabelFont`.
  - In return, Svg users who never touch decks pay nothing, the Svg package stays AOT-compatible,
    and the Pptx package stays without a native dependency.

## Implementation notes

Implemented by doc-3 on 2026-11-14.

**What was built**

- `SlideSvg(Number, Title, Svg, Width, Height)` in `wwosik.DocLibs.DocumentConversions` (`SlideSvg.cs`), as planned.
- `DeckSvg.Write(Stream pptx, DeckSvgOptions?)`, `DeckSvgOptions` (`Fonts`, `Slides`) and `DeckSvgResult` (`Notes`,
  `HasNotes`, `Pages`, `Slides`, `Fonts`, `LabelFont`, `ToString` as `GenerationResult`) in
  `wwosik.DocLibs.DocumentGeneration.Pptx` (`DeckSvg.cs`): a thin wrapper over E07-S02's `DeckReader.Read(pptx, fonts,
  slides)` and `SceneSvg.Write(…, SvgTarget.Painting)`. Nothing in the reader or `SceneSvg` changed.
- `DeckPreview.Render`, `DeckPreview.ContactSheet`, `DeckPreviewOptions` (`Width` 1280, `Fonts`, `Background`
  `FFFFFF`), `DeckPreviewResult`, `SlidePreview` (`Number`, `Title`, `Png`) and `ContactSheetOptions` (`Columns` 4,
  `ThumbnailWidth` 320, `Gap` 16, `Labels`, `LabelFont`, `Background` `F2F2F2`) in
  `wwosik.DocLibs.DocumentConversions.Svg` (`DeckPreview.cs`). Package references are unchanged: Svg references only
  DocumentConversions (still `IsAotCompatible`), Pptx neither Svg nor SkiaSharp.
- Docs: *Deck previews* in the Svg page (API, the two calls, sizes, size limit, errors, build slides, fonts and
  stand-ins, relation to the HTML deck, known limits), *Slide SVGs for previews* in the Pptx page under *HTML deck*,
  *Slide SVGs* in the DocumentConversions page; README, `docs/README.md`, the Svg `.csproj` description, CHANGELOG
  under *New*, and `ai/memory.md`.

**Decisions and deviations**

- **Final build state** needed no code: the reader ignores `p:timing`, so every paragraph a step shows (exit
  builds included) is drawn. A test checks it on `presenting.md`'s build slide.
- **`DeckSvgResult.Fonts`** is `DeckScene.Faces` plus, for runs measured by estimate (no face), the Liberation Sans
  face of their weight and style, since `SvgTarget.Painting` names Liberation Sans for them. Each file once (by
  reference, so a face used in several styles is not repeated).
- **`DeckSvgResult.Pages`** is computed from `Slides` rather than set; `DeckSvgResult` is its own record (not a
  `GenerationResult`, which is sealed).
- **The contact sheet:** rows are as tall as their tallest thumbnail; with fewer slides than `Columns` the sheet is
  as narrow as the slides; the label is the slide number (question 8) in 1/20 of the thumbnail width (10–48 px)
  under the thumbnail, in `404040`; the border is a 1 px `BFBFBF` hairline; scaling uses Mitchell cubic sampling.
  An empty `DeckPreviewResult` is an `ArgumentException`. Without `LabelFont`, SkiaSharp's default typeface is
  used, and when it has no glyphs the sheet is laid out without the label line.
- **The size-limit hint** names the largest `ThumbnailWidth` that fits with the same columns, or, when no width
  does (the labels and gaps alone are too tall), says to use more columns or fewer slides.
- **Errors:** `Width` outside 1–16 384 is an `ArgumentOutOfRangeException` (an option, not the SVG); a slide that
  cannot be painted is `DocumentConversionException` (`Format = Svg`) "Slide n cannot be painted: …".
- **The embedded-face pixel test** (plan: "a glyph that only that face has") paints a sample-brand slide with code
  in Inconsolata with and without that face and requires the pixels to differ. The sample brand's Liberation Serif
  could not be used: this machine has the Liberation fonts installed system-wide, so Svg.Skia found them anyway
  (recorded in `ai/memory.md`, *Traps*). Inconsolata is no system font here or on GitHub's Linux runners.
- **The unknown-family test** sets the theme's minor font to *Made Up Sans*: the SVG names only Liberation Sans,
  every face in `Fonts` is Liberation Sans, the body text is painted, and `font-missing` names the family.
- **Goldens: our own only.** 48 images at 640 px, 1.1 MB, in `src/wwosik.DocLibs.DocumentConversions.Svg.Tests/Samples`
  (`deck` on the neutral, sample and dark templates, `layouts` on neutral, and a `features` deck with a table, a
  native chart, a Mermaid diagram, a financial chart and an SVG image on neutral). The plan listed `layouts` on all
  three templates and the E07-S02 test template too; that would be about 80 images, against its own "at most about
  40", so `layouts` is on neutral only and the test template (built inside `DeckReaderTests`, one slide of odd
  DrawingML) has none. Tolerance as planned: mean absolute difference per channel ≤ 1 % and at most 0.5 % of pixels
  off by more than 25 % (64) in some channel; `DOCLIBS_UPDATE_EXPECTED=1` rewrites them, a leftover golden for a
  slide that no longer exists fails the test, and a failure writes the actual and the expected image to
  `.tmp/review/`. The date in the footers is pinned (`TimeProvider` 15 Jan 2026), or the goldens would change daily.
- **The Svg test project links the Pptx tests' `Samples.cs` and `DarkTemplate.cs`** and their sample decks and
  images, so it generates the same decks; for that, DocumentGeneration and the Pptx package grant it
  `InternalsVisibleTo` (internal only, not visible to consumers).
- **PowerPoint references (`*.office.png`) and calibration (steps 7 and 9) are not done:** there is no Office
  here, and PowerPoint exports cannot be made in this environment. No `*.office.png` is committed, no loose-tolerance
  test exists, the text rules of E07-S02 were not adjusted, and the test project has no README step for re-exporting.
  The acceptance box for goldens stays unticked for that reason (and for the unchecked Windows run).

**Tests** (`DeckSvgTests` in the Pptx tests, `DeckPreviewTests` in the Svg tests)

- `DeckSvg`: one SVG per visible slide in points with the deck's numbers (hidden slide skipped), `Slides` picks
  slides, the build slide shows every step, a converted SVG image is a PNG `data:` URI and never `image/svg`, every
  `font-family` names a face in `Fonts` and each face is there once (neutral and sample brand), a missing family is
  Liberation Sans bold with its face in `Fonts` and a `font-missing` note, `LabelFont` is Liberation Sans, a
  non-presentation throws with `Format = Pptx`.
- `DeckPreview`: 1280 × 720 by default on 16:9, 400 × 300 at `Width = 400` on 4:3, the background colour behind a
  slide without one, the embedded face and the unknown family (above), a broken slide names its number, contact
  sheet geometry with and without labels and without `LabelFont`, fewer slides than columns, the size limit and the
  width it names (which fits, while one pixel more does not), no slides, and the goldens.
- Reviewed by eye: every golden and the five contact sheets in `.tmp/review/preview-*.png`. They show the slides as
  the HTML deck does; the dark template's cover title is dark on dark, the fixture's own XML noted in E07-S02.

**Open verification items**

- **PowerPoint comparison:** export `deck.md` and `layouts.md` on the neutral, sample and dark templates from
  PowerPoint (`.tmp/scripts/export.ps1`, `SaveAs` 18) at 640 px, compare with `.tmp/review/preview-*.png`, calibrate
  E07-S02's text rules (line pitch, first baseline, bullet positions) until line breaks match and positions are
  within 2 px at 1280 px, then commit the `*.office.png` references with a loose-tolerance test and list the
  remaining differences in the docs' known limits. This is E07-S02's open item too.
- **Windows:** run `DeckPreviewTests` once on Windows (`dotnet.exe test` from WSL, see `ai/memory.md`) to confirm
  the Linux goldens pass within the tolerance.
- **A container without fontconfig** (`SkiaSharp.NativeAssets.Linux.NoDependencies`): the labels need `LabelFont`
  there; the slides need nothing more than `DeckSvgResult.Fonts`. Not tried here.

**Loose ends**

- The HTML deck's own paint test (`HtmlDeckTests.TheSlideSvgs_PaintWithSvgSkia`) still checks only size and "not
  blank"; the goldens here cover the same slides by pixels, so it could be dropped later.
- No JPEG or WebP (question 5) and no image per build step (question 3), as decided.

**Breaking changes**

None. New API only (`SlideSvg`, `DeckSvg`, `DeckPreview` and their options and results); decks, PDFs, Word
documents, the HTML deck and `SvgConverter` are unchanged.
