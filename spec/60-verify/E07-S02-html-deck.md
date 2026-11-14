# E07-S02 HTML deck

> Status: in verification
> Epic: [E07 Presenting](../50-todo/E07-presenting.md)

## Summary

A deck as one self-contained HTML file that a host can show without Office: every slide drawn from
the `.pptx` itself, so splitting, fitting and errors are the deck's own. Keys move through the
slides, an overview shows them all, and the page prints one slide per sheet. The drawing work is a
reader of the deck's DrawingML into a model of positioned shapes and laid-out text, written as one
SVG per slide. E07-S03 paints the same SVGs as PNG previews, so the HTML deck and the previews show
the same thing. Builds, transitions, the notes view and the presenter window are E07-S06's.

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
  built-in master) and transitions (`p:transition`, Morph in `mc:AlternateContent`); E07-S05
  builds (`p:timing`); E07-S04 scenes with named shapes and Morph; E01-S06 more layout and
  placeholder roles (picture placeholders, cropped pictures); E04-S05 visual blocks (chevrons,
  Harvey balls, icons, QR codes, round and grey pictures); E02-S05 slide kinds (`::: comment` as a
  callout). Each adds DrawingML this reader has to know.
- **Split** (question 7): this story is the reader, the SVG and the static page. E07-S06
  (presenting the HTML deck) comes after E07-S01 and E07-S05 and adds builds, transitions, the
  notes view and the presenter window on top of this page; E07-S03 can start once this story is
  done.
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
     - `Language`: the page's `lang`, default the first run's `lang` in the deck, else `en`.
   - Markdown to HTML is two calls, documented: `PptxGenerator.Generate` into a `MemoryStream`, then
     `HtmlDeck.Write`; the host merges both results' notes.
   - The input is any `.pptx`. What the reader does not know is left out and reported (step 9); a
     package that is not a presentation throws `DocumentConversionException` with `Format = Pptx`,
     like a damaged template today.
2. **Reader and model** (internal, namespace `…Pptx.Rendering`).
   - `DeckReader` opens the package with the Open XML SDK and returns a `DeckScene`: slide size
     (`sldSz`), slides in `sldIdLst` order (hidden slides, `show="0"`, skipped as in a slide show),
     and per slide a `SlideScene`: background, a tree of `SceneItem`s in z-order and hyperlinks.
     E07-S06 adds the speaker notes, build steps and the transition.
   - `SceneItem` kinds: `SceneGroup` (transform), `SceneShape` (a resolved path in EMU, fill,
     line, optional `SceneText`), `ScenePicture` (image bytes and type, the SVG alternative if any,
     crop, clip path). Tables and charts are resolved into groups of these, so the backends know
     only four kinds.
   - Every item keeps its `cNvPr id` and name, so E07-S06 can address it for builds (E07-S05) and
     Morph (E07-S04), and each paragraph of a text keeps its index for paragraph builds.
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
   paragraph a `<g>` with `data-p`, so E07-S06's builds can show and hide them. Pictures used on several
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
   - **Keys:** Right, Down, Space, Page Down, Enter, click and swipe left: next slide; Left, Up,
     Page Up, Backspace, swipe right: previous slide; Home/End; a number then Enter: that slide;
     `F` full screen; `O` overview (all slides as a grid, Enter or click opens one); `B` or `.`
     black screen; `?` the key list; Esc leaves full screen, overview or black. The URL hash (`#7`)
     follows, so reload and links land on the same slide. E07-S06 turns next and previous into
     build steps and adds `N`, `P` and `#7.2`.
   - **Print:** `@page` the slide's size with no margins, one slide per sheet, the toolbar hidden.
   - **Script:** plain JavaScript in one inline `<script>`, kept in the package as an embedded
     resource (`HtmlDeck/deck.js`, `HtmlDeck/deck.css`) next to the authoring guides, minified by
     nothing, so it can be reviewed like code. E07-S06 extends the same two files.
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
      `drawing-unsupported` note. A later story that writes new DrawingML (E07-S01, E07-S05,
      E07-S04, E01-S06, E02-S05, E04-S05) therefore extends the reader in the same change; this
      rule goes in `ai/memory.md`.
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
      stays text), has one `<section>` per visible slide, embeds each face once.
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
      a web server: keys, overview, black screen, print to PDF, full screen.
13. **Docs and changelog.**
    - `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md`: a section *HTML deck* with the API, the keys,
      the supported DrawingML, fonts and stand-ins, security (what the CSP allows, links), size (images
      and fonts are inline), and known limits; the two note codes in the notes table of
      `docs/wwosik.DocLibs.DocumentGeneration.md`.
    - `README.md` and `docs/README.md`: the HTML deck in the package description.
    - `CHANGELOG.md` under *New*. No existing deck or PDF changes; moving the Liberation files to
      DocumentGeneration changes no output (the same bytes are embedded) and needs no breaking entry.

## Acceptance criteria

- [x] `HtmlDeck.Write` turns every sample deck, on the neutral, sample and dark templates, into one
  HTML file that loads nothing from the network and gives no `drawing-unsupported` note.
- [x] Placeholders take their position and text style from layout and master; master and layout
  decorations are drawn unless `showMasterSp="0"`; backgrounds inherit slide → layout → master.
- [ ] Line breaks of text in an embedded font or a stand-in match PowerPoint's on the sample decks,
  apart from the cases listed in the docs.
- [x] Tables (including the built-in *Medium Style 2 – Accent 1*), charts from `ChartWriter`,
  pictures with SVG alternatives, Mermaid diagrams and financial charts are drawn.
- [x] Unknown elements, presets, image formats and effects are left out and reported once per
  kind with the slide numbers; missing fonts give `font-missing`.
- [ ] Keys, overview, black screen and print work in Chrome, Firefox, Edge and Safari, from disk
  and from a server.
- [x] Slide text in the HTML is escaped, links are only `http`, `https` and `mailto`, and the CSP
  blocks anything not inline.
- [ ] The sample decks were compared with PowerPoint's exports and the remaining differences are
  documented.
- [x] The coverage test fails when a sample deck contains DrawingML the reader does not know.
- [x] Docs, the notes table and CHANGELOG are updated, and all tests pass.

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

7. Should the work split into two stories: the reader, the SVG and the static page (slides, keys,
   overview, print) first, and the presenting parts (builds, transitions, notes view, presenter
   window) after E07-S01 and E07-S05 (proposed: the first part does not wait for them, and
   E07-S03 can start after it)?

> Answered by the human (2026-11-14): as proposed.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- Split on 2026-11-14 as question 7 proposed: the presenting parts and their questions (build
  effects, Morph, notes view and presenter window) moved to E07-S06, with their answers.
- E07-S03 adds a public entry point to this story's reader, `DeckSvg.Write`, that returns the
  slide SVGs and the faces they use, because the previews live in
  `wwosik.DocLibs.DocumentConversions.Svg`, which does not reference this package (see E07-S03's
  notes). The reader and `SceneSvg` stay internal here.

## Implementation notes

Implemented by doc-3 on 2026-11-14.

**What was built**

- `HtmlDeck.Write(Stream pptx, Stream html, HtmlDeckOptions?)` and `HtmlDeckOptions` (`Title`, `Fonts`,
  `EmbedFonts`, `Language`) in `wwosik.DocLibs.DocumentGeneration.Pptx` (`HtmlDeck.cs`), as planned. A package
  that is not a presentation (not a zip, or a zip whose main part is not `p:presentation`, such as a `.docx`)
  throws `DocumentConversionException` with `Format = Pptx`.
- The reader in `Rendering/` (internal): `DeckReader` (+ `.Tables.cs`, `.Charts.cs`), `Scene.cs` (the model:
  `DeckScene`, `SlideScene`, `SceneGroup`, `SceneShape`, `ScenePicture`, text as paragraphs → lines → runs),
  `Palette` (theme, colour map, colours with modifiers, fills, lines), `Geometry` and `PresetShapes`,
  `TextLayout`, `FontLibrary`, `RenderNotes`, `SceneSvg`. The page is `HtmlDeckWriter` with
  `HtmlDeck/deck.js` and `HtmlDeck/deck.css` as embedded resources.
- `TrueTypeMetrics` (shared package): units per em, `hmtx` advances, the `cmap` glyph map, Windows ascent and
  descent, `macStyle`. `StandInFonts` (shared package) holds the Liberation files, moved with `git mv` from the
  PDF package; `FontSet.Liberation` now takes the same bytes from there, so PDFs embed exactly what they did
  (the PDF tests are unchanged and pass).
- New note codes `drawing-unsupported` and `font-missing`, in `NoteCodes.All` (formats `Pptx`, since there
  is no HTML format) and in the docs table; the PPTX authoring guide leaves them out (`AuthoringGuides.Notes`
  `without`), because they are not about the Markdown.
- Docs: a section *HTML deck* in the Pptx page with the API, keys, page, security, size, fonts, the
  supported DrawingML as a table, what is left out, the text layout rules, and a known limit; the two codes in
  the shared page; README, `docs/README.md`, the package descriptions and both `THIRD-PARTY-NOTICES.txt`.
  `ai/memory.md` has the architecture and the contract rule.

**Decisions and deviations**

- **Parts are read as XLinq** from the Open XML SDK's part streams (`PartXml`), matched by namespace, not with
  the SDK's typed classes: DrawingML inheritance is attribute-by-attribute over many element kinds, which is
  far shorter generically. The SDK still opens the package and resolves relationships.
- **The model's transforms:** each shape carries a matrix to the slide with flips (`Transform`) and one for
  its text with rotation only (`TextTransform`, plus 180° for `flipV`), as PowerPoint does not mirror text.
- **Presets are ECMA-376 definitions** (`presetShapeDefinitions.xml` form) evaluated by the same guide engine
  as custom geometry: `rect`, `roundRect`, `ellipse`, `line`, `straightConnector1`, `wedgeRoundRectCallout`
  (what the generator writes, the callout from E02-S05) plus `triangle`, `rtTriangle`, `diamond`,
  `parallelogram`, `homePlate`, `chevron` for templates. Steps, chevrons, risk matrices and Harvey balls of
  E04-S05/S07/S08 are custom geometry and glyphs, not presets.
- **`font-missing` is not given for metric-compatible stand-ins** (Arial, Helvetica, Arimo → Liberation Sans;
  Courier New, Courier, Cousine → Liberation Mono): step 9 lists "a stand-in" too, but then every neutral deck
  would carry a note whose advice (pass the font) changes nothing. Consolas → Liberation Mono is noted (other
  widths), and so are estimates and missing bold or italic faces.
- **Text boxes and table cells take `defaultTextStyle`,** placeholders the master's title, body or other
  style (dt, ftr, sldNum, hdr → other). The plan did not say which applies to text boxes; LibreOffice uses
  `otherStyle` there. To be confirmed in PowerPoint.
- **A `p:style` `fontRef` colour** sits after the shape's own `lstStyle` and before the inherited styles.
- **Placeholder matching by `idx` only among compatible types:** the built-in role layouts (E01-S06) give
  a picture placeholder and the date the same `idx="2"` (and the slide number `idx="4"`), so matching by `idx`
  alone put the contact slide's date and number into picture slots. The generator's template is unchanged;
  whether PowerPoint matches the same way is an open check (recorded in `ai/memory.md`, *Traps*).
- **Wingdings task boxes** (``, ``) and a few common Wingdings bullets are drawn as their Unicode
  equivalents (☐ ☑ ✓ ▪ …) in the text font, since no viewer has Wingdings off Windows.
- **`spcBef` is not applied to the first paragraph,** superscript (`baseline`) is drawn at two thirds of the
  size, underline and strike-through are drawn as rectangles (the same in the browser and in Svg.Skia).
- **Charts** are laid out by the reader (padding, axis steps of 1, 2 or 5, labels, legend at the bottom); the
  value axis starts at `c:min` when the chart has one, as `ChartWriter` writes for bars and areas.
- **Pictures in the page are `<symbol>`s,** every one of them (not only those used twice), stretched over a
  unit square and placed with `<use>`; a crop draws the picture larger inside a clip path; `.grey` is an SVG
  `feColorMatrix` filter (an SVG filter, not a CSS one).
- **The section's `#n` is the position in the page** (hidden slides skipped); `data-number` keeps the slide's
  number in the deck, which is also what `slidenum` fields show.
- **For E07-S03:** `SvgTarget.Painting` already exists: standalone SVG, raster pictures only, `font-family`
  the measured face (Liberation Sans for an estimate). `DeckReader.Read(pptx, fonts, slides)` takes the slide
  numbers to read, and `DeckScene.Faces` lists each face used once. E07-S03's public `DeckSvg` (and
  `SlideSvg` in the base package) was **not** added here, since its plan puts it in E07-S03; it is a thin
  wrapper over these. For E07-S06, items keep `data-id` and paragraphs `data-p`; the reader ignores
  `p:transition`, `p:timing` and notes pages without notes, ready for E07-S06 to read them.
- The test project references `wwosik.DocLibs.DocumentConversions.Svg` and `SkiaSharp.NativeAssets.Linux`
  (as planned) and paints every slide of `deck.md` on the three templates through `SvgConverter`; with no
  E07-S03 goldens yet, the check is size and "not blank", not pixels.

**Tests** (`HtmlDeckTests`, `DeckReaderTests`)

- The contract: `deck.md`, `layouts.md`, `presenting.md` (builds, transitions, notes), `schedules.md`,
  `steps-and-risks.md` (steps, risk matrix, Harvey balls), `diagrams.md` (Mermaid) and a features deck (slide
  kinds, round/grey/cropped pictures, an SVG image, a QR code, icon cards, stacked columns, a line with a
  negative value, stacked areas, a financial chart and table, a comment callout, task boxes) on the neutral,
  sample and dark templates: no `drawing-unsupported`, one section per slide. Review files go to
  `.tmp/review/html-*.html` and `.pptx`.
- The page: CSP with the script's hash, one script, no external `src`/`href` (only the deck's own `https`
  links), no `link`/`iframe`/`object`/`embed`/`foreignObject`, escaping of slide text and the title
  (`</script><img onerror>` stays text), only web and mail links, each face once and none for neutral
  decks, pictures once as symbols, hidden slides, errors for a non-presentation.
- The model: placeholder box and style from layout and master, a shape's `lstStyle` over the master's,
  colours with `lumMod`/`alpha` and a slide `clrMapOvr`, every guide formula, `roundRect` with `adj`, custom
  geometry with its own path space, `arcTo`, `quadBezTo`, `fill="none"`/`stroke="0"`, groups with
  `chOff`/`chExt`, rotation and flips, numbering per level and Wingdings boxes, autofit and line breaks (each
  line fits, the next word would not), a missing family and a passed font, a table with `gridSpan`/`hMerge`, a
  grown row and the built-in style, master decorations with `p:style` and guides hidden by
  `showMasterSp="0"`, a picture background, and an unknown preset, a shadow and an EMF picture reported once
  each (the plan's test template, built at test time from the generated deck).
- Checked by hand in headless Chromium (Playwright, not a dependency): every sample loads from disk with no
  console errors and no requests; every key of the plan's list, the overview, black screen, key list, click
  to advance, reload on `#n`; print to PDF gives one 960 × 540 pt sheet per slide.

**Open verification items**

- **No PowerPoint comparison yet** (no Office here): export `deck.md` and `layouts.md` on the neutral, sample
  and dark templates with PowerPoint (`.tmp/scripts/export.ps1`, `SaveAs` 18), compare with the HTML (the
  review files) and record remaining differences in the docs' known limits — especially line pitch and first
  baseline (the 1.2 × size rule), bullet positions, autofit, the *Medium Style 2 – Accent 1* colours, chart
  layout, text-box styles (`defaultTextStyle` vs `otherStyle`) and the duplicate-`idx` placeholder matching.
  The acceptance criterion "line breaks match PowerPoint's" is therefore only checked against the reader's
  own rule, not against PowerPoint, and its box stays unticked.
- **Browsers:** only Chromium was available. Firefox, Safari/WebKit and Edge, from disk and from a web
  server, full screen (`F`), touch swipes and print in each are open; that box stays unticked too.
- The **dark test template**'s title layout keeps a dark title colour (`1F2937` in its `lstStyle`), so its
  cover title is dark on dark in the HTML; that is the fixture's XML, which PowerPoint should show the same way.
- **Harvey balls** (Segoe UI Symbol, not bundled) are measured by estimate (`font-missing`) and drawn in
  whatever face the browser finds; in Chromium on Linux they come out small.
- Footer text and date of the sample brand sit right next to each other (`Sample Corporation14.11.2026`), as
  the template places the boxes; compare with PowerPoint.

**Loose ends**

- `TemplateTone.Detect` keeps its own colour lookup; step 3 wanted it moved into the resolver. Left alone so
  this change does not touch the generator's tone detection; `Palette` can replace it later.
- Pixel tests against E07-S03's goldens and PowerPoint exports (step 11) wait for E07-S03 and an export; the
  current paint test checks size and that slides are not blank.
- Not drawn, all reported: right-to-left and vertical text, text columns, effects, SmartArt (its `dsp`
  drawing part could be read like a shape tree), tiled picture fills, `pattFill` as a pattern.

**Breaking changes**

None. Decks, PDFs and Word documents are unchanged; the Liberation fonts moved to
`wwosik.DocLibs.DocumentGeneration` with the same bytes (the shared package is about 2.2 MB larger, the PDF
package as much smaller; noted under *New* in the changelog, as the plan decided).
