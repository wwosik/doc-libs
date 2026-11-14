# E07-S02 HTML deck

> Status: todo
> Epic: [E07 Presenting](E07-presenting.md)

## Summary

A deck as one self-contained HTML file that a host can show without Office: every slide drawn from
the `.pptx` itself, so splitting, fitting and errors are the deck's own. Keys move through slides
and builds, a notes view and a presenter window show the speaker notes, and the page prints one
slide per sheet. The drawing work is a reader of the deck's DrawingML into a model of positioned
shapes and laid-out text, written as one SVG per slide. E07-S03 paints the same SVGs as PNG
previews, so the HTML deck and the previews show the same thing.

## Context

- **What a deck contains today.** `DeckWriter` (`DeckWriter.cs`, `DeckWriter.Fitting.cs`) and
  `DrawingWriter` write a small, known part of DrawingML:
  - **Placeholders** with no position of their own (`Placeholder`): position, size and text style
    come from the layout and the master. `normAutofit fontScale` on content slides shrunk by `Fit`.
  - **Text boxes** (`TextBoxShape`, `spAutoFit`, zero side insets), **preset shapes** `rect`,
    `roundRect` (with and without `adj`) and `ellipse` (`Shape`, `DrawingWriter.Shape`), and
    **custom geometry** without guides (moveTo, lnTo, cubicBezTo, close) for diagrams.
  - **Groups** with `chOff`/`chExt` equal to `off`/`ext` (`DrawingWriter.Group`).
  - **Colours:** `srgbClr` with `alpha` (`SlideColor.Alpha`), `schemeClr` with `lumMod`/`lumOff`
    (`SlidePalette.Scheme` for caller templates); outlines with `custDash` and round joins.
  - **Text:** `lnSpc` in percent, `spcBef` in points, character bullets (`buChar`, `buFont`
    Arial or Wingdings for task boxes), `buAutoNum arabicPeriod` with `startAt`, `marL`/`indent`,
    alignment, `b`, `i`, `strike`, `baseline="30000"` for footnote references, `highlight`,
    `latin` (code font, table font), `hlinkClick`, `a:br`, the `slidenum` field, and `lang` on every
    run (E06-S02).
  - **Pictures:** PNG, JPEG or GIF with `stretch`, and for converted SVGs the `asvg:svgBlip`
    extension beside the PNG (`DeckWriter.Blip`, E04-S04).
  - **Tables:** `a:tbl` in a graphic frame with the template's default table style, or the
    built-in *Medium Style 2 – Accent 1* by its GUID `{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}`
    (`BuiltInTableStyleId`), which is **not defined in the package**: PowerPoint knows it.
  - **Charts:** a chart part written by `ChartWriter` (bar as columns clustered or stacked, line
    with markers, area standard or stacked, title, unit, gridlines, series colours, caches of
    categories and values) with its data in an embedded workbook.
- **What the template brings.** The built-in master (`TemplateBuilder`) has a solid background,
  a linear `gradFill` on the section layout, a solid primary background on *Title Only Dark*, a logo
  picture on the master (`rId6`), `showMasterSp="0"` on the title and section layouts, title and
  body styles with `lnSpc` 80–100 %, and theme fonts (`+mn-lt`, `+mj-lt`) naming the brand family.
  A brand's `.potx` (`PptxBrandTemplate`) or a caller's template (`CustomTemplateOptions`) can bring
  anything a designer drew in PowerPoint: decorations on the master and layouts with `p:style`
  references to the theme's fill and line styles, custom geometry with guides, rotation and flips,
  picture backgrounds, effects, and often EMF logos. The dark test template (`DarkTemplate` in the
  PPTX tests) has a gradient background and a footer placeholder.
- **Fonts.** Decks embed the brand's faces as uncompressed EOT (version `0x00020001`, the TTF after
  a header) in font parts listed in `p:embeddedFontLst` (`FontEmbedding`, E01-S03). The neutral
  brand embeds nothing and names **Arial**; code is in **Consolas** by default
  (`PptxOptions.DefaultCodeFont`). The PDF package bundles Liberation Sans (metric-compatible with
  Arial) and Liberation Mono under `wwosik.DocLibs.DocumentGeneration.Pdf/Fonts/`.
  `TrueTypeInfo` reads `cmap`, `name`, `OS/2` and table offsets, but no advance widths; the PDF
  measures through PdfPig's `TrueTypeFont` (`Fonts.cs`), the deck only estimates (`TextMetrics`,
  `DrawingWriter.CharacterWidth`).
- **Reading decks today.** `PptxConverter` reads slides with XLinq (`OpenXmlPackage`) for
  Markdown, without the Open XML SDK; `TemplateTone.Detect` resolves a theme colour through the
  colour map. Nothing resolves placeholders, text styles or geometry.
- **SVG.** `DrawingSvg.Write` writes a `Drawing` as SVG (paths, text, hover tooltips); E04-S04's
  `SvgConverter` turns any SVG into PNG with Svg.Skia, behind `SvgSanitizer`.
- **Notes.** `GenerationResult`/`GenerationNote` and `NoteCodes` (E05-S03); every code needs an
  entry in `NoteCodes.All` and a row in the docs table.
- **Planned in parallel:** E07-S01 writes speaker notes (`notesSlide` parts, a notes master for the
  built-in master), builds (`p:timing`) and transitions (`p:transition`, Morph in
  `mc:AlternateContent`); E07-S04 scenes with named shapes and Morph; E01-S06 more layout and
  placeholder roles (picture placeholders, cropped pictures); E04-S05 visual blocks (chevrons,
  Harvey balls, icons, QR codes, round and grey pictures); E02-S05 slide kinds (`::: comment` as a
  callout). Each adds DrawingML this reader has to know.
- **Decided:** in the analysis, an HTML deck drawn from the generated PPTX's DrawingML, with keys,
  a notes view and print, and no new dependency; DrawingML fidelity is the named risk. In the epic,
  previews are painted with SkiaSharp, not in a browser.

## Proposed approach

1. **Package and API.**
   - It lives in `wwosik.DocLibs.DocumentGeneration.Pptx`, which already has the Open XML SDK, the
     knowledge of what the generator writes, and access to `TrueTypeInfo`. No new package reference.
   - A new public static class `HtmlDeck` in namespace `wwosik.DocLibs.DocumentGeneration.Pptx`:
     - `GenerationResult Write(Stream pptx, Stream html, HtmlDeckOptions? options = null)`.
       `Pages` is the number of slides written; the notes say what could not be drawn.
   - `HtmlDeckOptions` (record, init-only):
     - `Title`: the page title; default the deck's core-properties title, else "Presentation".
     - `Fonts`: TrueType files (`IReadOnlyList<ReadOnlyMemory<byte>>`) for families the deck names
       but does not embed, as in `SvgPngOptions.Fonts`; matched by family name (name ID 1).
     - `EmbedFonts`: default `true`; writes the faces the slides use as `@font-face`.
     - `Notes`: default `true`; `false` leaves the speaker notes out of the file (a deck handed to
       an audience).
     - `Language`: the page's `lang`, default the first run's `lang` in the deck, else `en`.
   - Markdown to HTML is two calls, documented: `PptxGenerator.Generate` into a `MemoryStream`, then
     `HtmlDeck.Write`; the host merges both results' notes.
   - The input is any `.pptx`. What the reader does not know is left out and reported (step 9); a
     package that is not a presentation throws `DocumentConversionException` with `Format = Pptx`,
     like a damaged template today.
2. **Reader and model** (internal, namespace `…Pptx.Rendering`).
   - `DeckReader` opens the package with the Open XML SDK and returns a `DeckScene`: slide size
     (`sldSz`), slides in `sldIdLst` order (hidden slides, `show="0"`, skipped as in a slide show),
     and per slide a `SlideScene`: background, a tree of `SceneItem`s in z-order, hyperlinks,
     speaker notes, build steps and the transition.
   - `SceneItem` kinds: `SceneGroup` (transform), `SceneShape` (a resolved path in EMU, fill,
     line, optional `SceneText`), `ScenePicture` (image bytes and type, the SVG alternative if any,
     crop, clip path). Tables and charts are resolved into groups of these, so the backends know
     only four kinds.
   - Every item keeps its `cNvPr id` and name, so builds (E07-S01) and Morph (E07-S04) can address
     it, and each paragraph of a text keeps its index for paragraph builds.
3. **Inheritance.** One `StyleResolver` per slide resolves everything PowerPoint inherits:
   - **Shapes drawn:** the slide's shapes, after the layout's and the master's non-placeholder
     shapes unless the layout or slide says `showMasterSp="0"`. Placeholders on layouts and masters
     are prompts and are never drawn.
   - **Placeholders:** matched to the layout's and then the master's placeholder by `idx`, else by
     type (`title` ↔ `ctrTitle`, `body`, `subTitle`, `dt`, `ftr`, `sldNum`, `pic`, `chart`, `tbl`);
     position, geometry, fill, line and `bodyPr` come from the nearest that has them.
   - **Text properties,** nearest first: run `rPr` → paragraph `pPr` → the shape's `lstStyle` level
     → the layout placeholder's → the master placeholder's → the master's `titleStyle`, `bodyStyle`
     or `otherStyle` → the presentation's `defaultTextStyle`. Theme fonts `+mj-lt`/`+mn-lt` from the
     theme's font scheme.
   - **Colours:** `srgbClr`, `schemeClr` through the slide's `clrMapOvr`, the layout's and the
     master's `clrMap` and the theme, `sysClr` by `lastClr`, `prstClr`; modifiers `alpha`, `lumMod`,
     `lumOff`, `tint`, `shade`, applied in DrawingML order. `phClr` from the referring style.
   - **Style references:** `p:style` `fillRef`/`lnRef` through the theme's `fmtScheme`;
     `bgRef` through `bgFillStyleLst`. `effectRef` and `fontRef` colour only.
   - **Backgrounds:** the slide's `bg`, else the layout's, else the master's: `solidFill`,
     `gradFill` (`lin` with angle; `path` drawn as radial), `blipFill` stretched, `noFill`, `bgRef`.
   - `TemplateTone.Detect`'s colour lookup moves into the resolver, so there is one implementation.
4. **Geometry.**
   - `xfrm` `off`/`ext`/`rot`/`flipH`/`flipV`; groups map `chOff`/`chExt` onto `off`/`ext`.
   - **Custom geometry** in full: `avLst`/`gdLst` with the formula language of ECMA-376 §20.1.9
     (`*/`, `+-`, `+/`, `?:`, `abs`, `at2`, `cat2`, `cos`, `max`, `min`, `mod`, `pin`, `sat2`,
     `sin`, `sqrt`, `tan`, `val` and the built-in names `w`, `h`, `ss`, `l`, `t`, `r`, `b`,
     `wd2`… `cd4`…), paths with their own `w`/`h`, `moveTo`, `lnTo`, `cubicBezTo`, `quadBezTo`,
     `arcTo`, `close`, `fill="none"`, `stroke="0"`.
   - **Presets** as hand-written custom geometry in a `PresetShapes` table, first exactly those the
     generator writes: `rect`, `roundRect`, `ellipse`, `line`. Each later story that writes a
     preset adds it here (step 10). An unknown preset is drawn as its bounding rectangle and
     reported.
   - **Lines:** width, solid or no fill, `prstDash` and `custDash`, `round`/`miter`/`bevel` joins,
     caps; `headEnd`/`tailEnd` `triangle` and `arrow` for connectors (`cxnSp`), as templates use
     them.
5. **Text layout.** The model holds text already broken into lines, so the SVG needs no wrapping
   and the PNG (E07-S03) breaks exactly where the HTML does.
   - **Metrics** come from TrueType files: `TrueTypeInfo` gains advance widths (`hhea`, `hmtx`),
     the vertical metrics (`OS/2` win ascent and descent, `head` units per em) and the glyph lookup
     it already has. Faces are found in order: the deck's embedded fonts (the TTF inside each EOT),
     `HtmlDeckOptions.Fonts`, then **stand-ins**: Liberation Sans for Arial and Helvetica,
     Liberation Mono for Courier New and Consolas. Liberation moves from the PDF package to
     DocumentGeneration so one copy serves both (see question 4). A family with no face falls back
     to `TextMetrics` and the note `font-missing`.
   - **Breaking:** between words at spaces and after hyphens, a word wider than the line broken
     between characters, as PowerPoint does; `wrap="none"` never breaks. No kerning, no ligatures,
     no shaping; East Asian and complex-script text and right-to-left are out of scope and
     reported.
   - **Spacing:** a line's pitch is 1.2 × the largest size on it × `lnSpc` percent, or `lnSpc` in
     points; `spcBef`/`spcAft`; an empty paragraph has the height of its `endParaRPr`. These rules
     are calibrated against PowerPoint exports of the sample decks before the backends are written
     (step 11), and the result is recorded in the docs.
   - **Body:** insets, `anchor` top, middle, bottom, `normAutofit` `fontScale` and
     `lnSpcReduction`, `spAutoFit` and `noAutofit` drawn in the box as written, text that overflows
     drawn unclipped as PowerPoint does. Bullets: `buChar` with `buFont`, `buClr`, `buSzPct`,
     `buAutoNum` (`arabicPeriod`, `arabicParenR`, `alphaLcPeriod`, `alphaUcPeriod`, `romanLcPeriod`,
     `romanUcPeriod`) counted per level, `buNone`; `marL`/`indent`.
   - **Runs:** size, bold, italic, single underline, strike, baseline shift, colour, `highlight`,
     family (a face for bold or italic falls back to the regular face with the note
     `font-missing`, never a synthesized one), `hlinkClick` (external `http`, `https`, `mailto`
     only), `a:br`, fields (`slidenum` renumbered to the slide's position, others as written).
6. **Tables, charts, pictures.**
   - **Tables:** `tblGrid`, row heights grown to fit their text as PowerPoint does, `gridSpan`,
     `rowSpan`, `hMerge`, `vMerge`, cell margins, anchor, fills and the four borders, and the
     `tblPr` flags (`firstRow`, `bandRow`, `firstCol`, `lastRow`, `lastCol`, `bandCol`) applied with
     the table style: the deck's `tableStyles.xml`, or a built-in definition of *Medium Style 2 –
     Accent 1* shipped in the reader. Another built-in style without a definition is drawn as a
     plain grid and reported.
   - **Charts:** a painter of exactly what `ChartWriter` writes, from the chart part's caches (see
     question 6): axes with gridlines, category and value labels, columns, lines with markers,
     areas, stacking, title, unit, legend, series colours and the text colour. Any other chart kind
     is drawn as an empty frame and reported.
   - **Pictures:** PNG, JPEG, GIF with `stretch` and `srcRect` crop, the geometry as a clip path (an
     ellipse picture is round), and the `svgBlip` alternative preferred in HTML. EMF, WMF and TIFF
     are left out and reported; an OLE object or a SmartArt frame is drawn from its fallback picture
     where the package has one.
   - **Not drawn, reported once per deck and kind:** effects (shadows, glow, reflection, soft
     edges), 3-D, text warps, vertical text, text columns, video and audio (their poster picture is
     drawn), ink. `mc:AlternateContent` takes the first `Choice` the reader knows, else the
     `Fallback`.
7. **SVG per slide** (`SceneSvg`): a `<svg>` with `viewBox` in EMU-derived units, the background,
   then each item: paths with fills, gradients (`linearGradient`/`radialGradient` in `<defs>`) and
   strokes, `clipPath` for pictures, `<image>` with `data:` URIs, and each text line as a `<text>`
   at its baseline with `<tspan>`s per run (`font-family`, `font-weight`, `font-style`, `fill`,
   `text-decoration`, highlight as a rectangle behind). Hyperlinks become `<a>` with
   `target="_blank" rel="noopener noreferrer"`. Each item is a `<g>` with `data-id`, each
   paragraph a `<g>` with `data-p`, so builds can show and hide them. Pictures used on several
   slides (the master logo) are written once in a hidden `<svg>` of `<symbol>`s and referenced with
   `<use>`. Only what Svg.Skia also draws is used (no `foreignObject`, no CSS filters, no
   `textLength`), so E07-S03 can paint the same SVG.
8. **The page** (`HtmlDeckWriter`), one file, nothing loaded from the network:
   - **Head:** `<meta charset>`, viewport, a Content Security Policy that allows only inline style,
     the one inline script (by hash), `data:` images and fonts; the title; `@font-face` rules with
     the faces as `data:` URIs (only faces some slide uses, each once); for stand-ins the CSS family
     list names the original first (`Arial, "Liberation Sans", Arimo, sans-serif`) and no face is
     embedded.
   - **Slides:** each SVG in a `<section>` scaled to the window with its aspect ratio, letterboxed
     on black. Text stays selectable; each section has the slide's title as `aria-label`.
   - **Keys:** Right, Down, Space, Page Down, Enter, click and swipe left: next step; Left, Up,
     Page Up, Backspace, swipe right: previous step; Home/End; a number then Enter: that slide;
     `F` full screen; `N` notes view; `P` presenter window; `O` overview (all slides as a grid,
     Enter or click opens one); `B` or `.` black screen; `?` the key list; Esc leaves full screen,
     overview or black. The URL hash (`#7`, `#7.2` for a build step) follows, so reload and links
     land on the same slide.
   - **Builds** (after E07-S01): the reader turns `p:timing`'s click sequence into steps; each step
     shows, hides or emphasizes items and paragraphs. Effects map to CSS: appear (instant), fade,
     fly in from the effect's direction, wipe (`clip-path`), exit as the reverse, emphasis as a short
     scale pulse; anything else is appear. Going back reverses a step without animation.
   - **Transitions** (after E07-S01): fade, push, wipe, cover, uncover, split and zoom as CSS
     animations of the section; cut and none instant; Morph as fade (see question 8). Speeds from
     `spd`/`dur`. `prefers-reduced-motion` turns them all into instant changes.
   - **Notes view** (`N`): the slide shrinks to the left two thirds and its speaker notes show beside
     it, as HTML paragraphs with bold, italic, links and lists. Without notes it says so.
   - **Presenter window** (`P`): opens the same file with `#presenter`, synced over
     `BroadcastChannel` (falling back to `postMessage` between the two windows when the file is
     opened from disk): current slide, next step, notes, slide number of total, elapsed time with a
     reset. Keys work in both windows.
   - **Print:** `@page` the slide's size with no margins, one slide per sheet in its final build
     state, transitions and the toolbar hidden; with the notes view on, each sheet has the slide on
     top and its notes below.
   - **Script:** plain JavaScript in one inline `<script>`, kept in the package as an embedded
     resource (`HtmlDeck/deck.js`, `HtmlDeck/deck.css`) next to the authoring guides, minified by
     nothing, so it can be reviewed like code.
9. **Notes for the caller.**
   - New codes `drawing-unsupported` (an element, preset, chart kind, image format or effect the
     reader leaves out, with the slide number and what it was) and `font-missing` (a family or
     face drawn with a stand-in or an estimate). Both with `Line = 0`, the slide numbers in the
     message, one note per kind and family, not per occurrence.
   - Both go in `NoteCodes.All` and the docs' notes table.
10. **The subset is a contract.**
    - The docs page lists the supported DrawingML (steps 3–6) as a table.
    - A **coverage test** generates every sample deck (`deck.md`, `layouts.md`, the feature samples
      of other tests) on the neutral, sample and dark templates, renders them, and fails on any
      `drawing-unsupported` note. A later story that writes new DrawingML (E07-S01, E07-S04,
      E01-S06, E02-S05, E04-S05) therefore extends the reader in the same change; this rule goes in
      `ai/memory.md`.
11. **Tests.**
    - **Model:** placeholder inheritance (position from the layout, size from the master, `lstStyle`
      overrides), colour resolution with `clrMapOvr` and modifiers, the guide formulas against
      values PowerPoint computes, `roundRect` with `adj`, groups with a child offset, rotation and
      flips, autofit, bullets and numbering, tables with merged cells and a table style.
    - **Text:** line breaks of known paragraphs in Liberation Sans match PowerPoint's (from the
      exports); a missing face gives `font-missing`.
    - **A test template** built at test time like `DarkTemplate`: master decorations with
      `p:style` references, custom geometry with guides, a rotated shape, a picture background, a
      `tableStyles.xml` style, an EMF picture (reported), a shadow (reported).
    - **HTML:** the file parses as HTML5, carries the CSP, loads nothing external (no `http` in
      `src`/`href` but the deck's own links), escapes slide text (a run `</script><img onerror>`
      stays text), has one `<section>` per visible slide, embeds each face once, leaves notes out
      with `Notes = false`.
    - **Pixels:** each slide SVG is rasterised in the tests with `SvgConverter` (the test project
      references `wwosik.DocLibs.DocumentConversions.Svg` and `SkiaSharp.NativeAssets.Linux`) and
      compared with the images from E07-S03's golden tests; until E07-S03 exists, with PowerPoint's
      exports at a loose tolerance.
    - The script's key handling is not unit-tested; it is checked by hand in browsers (below).
12. **Real-Office and browser comparison.**
    - Export `deck.md` and `layouts.md` on the neutral, sample and dark templates and the test
      template with PowerPoint (`.tmp/scripts/export.ps1`, `SaveAs` format 18), put them side by
      side with the rasterised SVGs, and record the differences that remain in the docs' known
      limits.
    - Open the HTML file in current Chrome, Firefox, Edge and Safari (or WebKit), from disk and from
      a web server: keys, overview, notes view, presenter window, print to PDF, full screen.
13. **Docs and changelog.**
    - `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md`: a section *HTML deck* with the API, the keys,
      the supported DrawingML, fonts and stand-ins, security (what the CSP allows, links), size (images
      and fonts are inline), and known limits; the two note codes in the notes table of
      `docs/wwosik.DocLibs.DocumentGeneration.md`.
    - `README.md` and `docs/README.md`: the HTML deck in the package description.
    - `CHANGELOG.md` under *New*. No existing deck or PDF changes; moving the Liberation files to
      DocumentGeneration changes no output (the same bytes are embedded) and needs no breaking entry.

## Acceptance criteria

- [ ] `HtmlDeck.Write` turns every sample deck, on the neutral, sample and dark templates, into one
  HTML file that loads nothing from the network and gives no `drawing-unsupported` note.
- [ ] Placeholders take their position and text style from layout and master; master and layout
  decorations are drawn unless `showMasterSp="0"`; backgrounds inherit slide → layout → master.
- [ ] Line breaks of text in an embedded font or a stand-in match PowerPoint's on the sample decks,
  apart from the cases listed in the docs.
- [ ] Tables (including the built-in *Medium Style 2 – Accent 1*), charts from `ChartWriter`,
  pictures with SVG alternatives, Mermaid diagrams and financial charts are drawn.
- [ ] Unknown elements, presets, image formats and effects are left out and reported once per
  kind with the slide numbers; missing fonts give `font-missing`.
- [ ] Keys, overview, black screen, notes view, presenter window and print work in Chrome, Firefox,
  Edge and Safari, from disk and from a server.
- [ ] After E07-S01: builds step through in order and back; transitions play and respect
  `prefers-reduced-motion`; notes from `::: notes` appear in the notes view and presenter window.
- [ ] Slide text in the HTML is escaped, links are only `http`, `https` and `mailto`, and the CSP
  blocks anything not inline.
- [ ] The sample decks were compared with PowerPoint's exports and the remaining differences are
  documented.
- [ ] The coverage test fails when a sample deck contains DrawingML the reader does not know.
- [ ] Docs, the notes table and CHANGELOG are updated, and all tests pass.

## Open questions

1. Which decks does it take? Any `.pptx`, best effort, with what it cannot draw left out and
   reported (proposed: caller templates bring arbitrary DrawingML anyway, so "doc-libs decks only"
   is no real boundary); or only decks the generator wrote, refusing others?

> Answered by the human (2026-11-14): as proposed.

2. Where does it live? In `wwosik.DocLibs.DocumentGeneration.Pptx` (proposed: Open XML SDK,
   `TrueTypeInfo` and the generator's own knowledge are there, no new package); a new package
   `wwosik.DocLibs.DocumentGeneration.Pptx.Html`; or in `wwosik.DocLibs.DocumentConversions` beside
   `PptxConverter`, read with XLinq?

> Answered by the human (2026-11-14): as proposed.

3. How is a slide drawn in the page? As SVG with text broken into lines by the reader (proposed:
   the same SVG becomes the PNG in E07-S03, so HTML and previews agree); or as HTML boxes with CSS
   that the browser wraps, which reflows better but breaks lines differently from both PowerPoint
   and the PNG?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Fonts for line breaking when the deck embeds none (the neutral brand's Arial, the Consolas code
   font): move the Liberation files from the PDF package to DocumentGeneration and use them as
   metric-compatible stand-ins (proposed: one copy, Pptx users download about 1.5 MB more); copy
   them into the Pptx package; or estimate with `TextMetrics` only and let callers pass fonts?

> Answered by the human (2026-11-14): as proposed.

5. Fonts in the HTML file: embed the faces the deck embeds as `@font-face` (proposed, whole faces,
   so a brand deck shows its typeface anywhere; switchable with `EmbedFonts`); or never embed and
   rely on the viewer's installed fonts?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Charts: a painter of `ChartWriter`'s subset inside the reader (proposed: it reads what is in the
   chart part, colours included, and needs no new reference); or a reference from the Pptx package
   to `wwosik.DocLibs.DocumentGeneration.Charts`, rebuilding a `ChartSpec` from the part and
   drawing it with `ChartRenderer`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Build effects in HTML: map E07-S01's effects to CSS (appear, fade, fly, wipe, exit, a pulse for
   emphasis; proposed); or show steps instantly with no effects?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Morph in HTML: fade for now, and E07-S04 adds the movement of shapes matched by name (proposed:
   scenes are E07-S04's); or interpolate matched shapes' position, size and colour here already?

> Accepted as proposed (the human, 2026-11-14, without discussion).

9. Speaker notes: a notes view beside the slide and a presenter window synced between two windows
   (proposed); or only the notes view in the same window?

> Accepted as proposed (the human, 2026-11-14, without discussion).

10. Should the work split into two stories: the reader, the SVG and the static page (slides, keys,
    overview, print) first, and the presenting parts (builds, transitions, notes view, presenter
    window) after E07-S01 (proposed: the first part does not wait for E07-S01, and E07-S03 can start
    after it)?

> Answered by the human (2026-11-14): as proposed.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
