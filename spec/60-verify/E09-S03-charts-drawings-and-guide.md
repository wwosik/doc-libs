# E09-S03 Charts, drawings and the DOCX authoring guide

> Status: in verification
> Epic: [E09 DOCX generator](../50-todo/E09-docx-generator.md)

## Summary

The DOCX generator places what code-block renderers draw:

- **Charts** become native Word charts. They use the same chart part and embedded `.xlsx` as decks,
  so *Edit Data* works.
- **Drawings**, such as Mermaid diagrams, become SVG images with a PNG fallback (`asvg:svgBlip`),
  as the human decided.
- **SVG images** in the Markdown keep their SVG the same way.

The DOCX authoring guide is completed, so a writer gets one description of everything the
generator understands.

## Context

- **Renderers return a `CodeBlockGraphic`** (`CodeBlockRenderer.cs`):
  - `DrawingGraphic(Drawing)`, such as `MermaidRenderer`;
  - `ImageGraphic(DocumentResource)`;
  - `ChartGraphic(ChartSpec, Drawing)` from `ChartRenderer`, with `ChartSpec` holding kind,
    categories, series, `Stacked`, `Title`, `Unit` and `Colors`.
  - Generators ask them through `CodeBlockRenderers.Render`, which wraps renderer exceptions. A
    renderer reports through `CodeBlockRequest.Report` (`code-not-drawn`).
- **Decks** (`DeckWriter`):
  - **Charts.** Decks call `ChartWriter.Add(SlidePart, ChartSpec, SlideColor? text, SlideColor
    grid, string? font)`. It writes the chart part as an XML string in schema order (the
    validator checks it) and the data as an embedded workbook (`Sheet1`, categories in column A),
    with `c:min` at zero for bars and areas.
    - `ChartWriter` is internal to Pptx and takes Pptx types (`SlidePart`, `SlideColor`).
  - **Drawings.** Decks draw them as editable shapes (`DrawingWriter`).
  - **Converted SVG images** are written as a PNG blip with the original SVG in the extension
    `{96DAC541-7B7A-43D3-8B79-37D633B846F1}` (`SVGBlip`, `ImagePartType.Svg`;
    `DeckWriter.cs` around the picture writer). `DeckParser` keeps the SVG as `Image.Vector`.
- **PDFs** draw a `Drawing` as vectors and a chart's `Drawing` too.
- **SVG and PNG:**
  - `DrawingSvg.Write(Drawing, DrawingSvgOptions)` writes any drawing as SVG, with a
    `FontFamily`.
  - SVG → PNG is `SvgConverter` / `SvgImageConverter` in `wwosik.DocLibs.DocumentConversions.Svg`
    (SkiaSharp, E04-S04). It reaches the generators only as an `IImageConverter` in
    `ImageConverters`, empty by default. Asked for scale 2, it reports the natural size.
  - Its fonts are its own option (`SvgImageConverter.Fonts`).
- **Decided by the human** (analysis question 7): Word drawings are SVG + PNG images, not
  editable shape groups. SkiaSharp is accepted as a dependency of the generators
  (question 9), but no generator references it today.
- **[E09-S01](E09-S01-docx-package-and-templates.md)** puts the shared Open XML sources in
  `src/Shared/OpenXml/` (decided, its question 2) and starts `AuthoringGuide/Docx.md`.
- **[E09-S04](E09-S04-docx-content.md)** places images and captions and fits images to the width
  and 80 % of the height. It is implemented before E09-S02 and this story despite its number.
  E09-S04 adds the content sections to the guide, and E09-S02 the deck syntax.
- **Captions (E03-S02).** A paragraph starting `Figure: ` directly after a fenced block that a
  renderer drew is that figure's caption. Figure captions go below the figure, are numbered with
  images through the whole document, and read "Figure n: …" in the document language. A
  misplaced one stays text with `caption-ignored`.
- **Authoring guides** (E05-S02):
  - renderers add their own section through `ICodeBlockRenderer.AuthoringGuide`
    (`Charts.md`, Mermaid);
  - `AuthoringGuides.Renderers` collects them;
  - `AuthoringGuides.Characters` lists missing characters from the fonts' `cmap`;
  - the guides' tests generate every example.
- **The DOCX converter** (`DocxConverter.Images`) reads `a:blip` pictures, so a drawing comes back
  as its PNG. It does not read charts; E08 plans charts as tables.

## Proposed approach

1. **Charts.**
   - **`ChartWriter` is made format-neutral.**
     - It moves to `src/Shared/OpenXml/ChartWriter.cs`, linked into Pptx and Docx, as E09-S01's
       question 2 decided.
     - `Add` takes the owning `OpenXmlPart` (a `SlidePart` or the `MainDocumentPart`), the text
       colour as hex or `null`, the grid colour as hex, and the font.
     - Pptx maps its `SlideColor` to those arguments. Decks must come out byte for byte as before,
       and the PPTX chart tests prove it.
   - **In Word,** a chart is an inline `w:drawing` with `a:graphicData` of the chart URI and
     `c:chart r:id`, in a centred paragraph.
     - **Size:** the text width by 9/16 of it, capped at 80 % of the text height (question 1).
     - **Look:** the gridlines are `TableLine`, the text is the theme's (`null`), and the font is
       the brand family. The series colours are those in `ChartSpec.Colors`, which the renderer
       took from the brand.
     - **On caller templates** (no brand), renderers get `Brand.Neutral` as on decks, and
       `ChartSpec.Colors` comes from there (question 3).
   - **Alt text.** `wp:docPr/@descr` is the chart's title, else "Chart" (`GeneratedTexts`, new
     `Chart`; German "Diagramm").
   - **Captions.** A chart is a figure for captions, with the syntax E03-S02 set for code-block
     graphics (question 7): a `Figure: …` paragraph right after the `chart` block becomes a
     `Caption` paragraph below the chart, with a `SEQ Figure` number, written as E09-S04 writes
     figure captions. The same holds for drawings (step 2).
2. **Drawings as SVG + PNG.**
   - **The SVG.** A `DrawingGraphic`, a `ChartGraphic`'s drawing where Word needs a picture, and an
     `ImageGraphic` that is SVG are all written with `DrawingSvg.Write`, with
     `FontFamily` set to the brand family (Arial without one).
   - **The PNG** comes from `ImageConverters` (`SvgImageConverter`) at scale 2, placed at the
     reported natural size and fitted by E09-S04's rules.
   - **The picture.** It is written as `pic:pic` with the PNG as `a:blip r:embed` and the SVG in the
     `asvg:svgBlip` extension, exactly as decks write it. The shared helper in
     `src/Shared/OpenXml/` writes both.
   - **Without an image converter** (question 2):
     - a `DrawingGraphic` stays code;
     - it gives `code-not-drawn`: "Register SvgImageConverter from
       wwosik.DocLibs.DocumentConversions.Svg to draw diagrams in Word".
     - Nothing half-written is placed.
   - **Alt text.** `wp:docPr/@descr` is the first line of the code block (for Mermaid, its type and
     title), cut at 120 characters (question 6).
   - **Fonts in the PNG** are the converter's (`SvgImageConverter.Fonts`). The docs tell hosts to
     pass the brand's fonts there, so the PNG matches the SVG (question 5).
3. **SVG images in the Markdown.**
   - `![…](x.svg)` gets the same treatment: the converter's PNG plus the original SVG in
     `asvg:svgBlip`, as on decks.
   - The SVG is written as it came. It was sanitized for the PNG by `SvgSanitizer`, and Word
     itself loads nothing external from it (question 4).
4. **Authoring guide.**
   - **Completing `AuthoringGuide/Docx.md`:**
     - *Diagrams and charts*, filled by `AuthoringGuides.Renderers` with a neutral wording;
     - *Characters*, from the brand fonts through `AuthoringGuides.Characters`, with Word's
       substitute-font behaviour as in the deck guide;
     - *Notes you may get back*, through `AuthoringGuides.Notes(DocumentFormat.Docx)`;
     - a full *Example*, which the tests generate.
   - **Renderer texts.** The renderers' `AuthoringGuide` texts must read right for Word too: the
     `Charts.md` wording "native charts in decks, vectors in PDFs" gains "native charts in Word".
     The Mermaid wording says the diagram is a picture in Word.
   - **Tests.**
     - The guide is generated for neutral, sample (with the Mermaid and chart renderers) and
       custom, and compared with `authoring-guide-<template>.expected.md`.
     - Every example in it generates without notes.
     - Its word count stays under the budget the other guides have (`AuthoringGuides.Words`).
     - Every front-matter key of the Docx templates, every container, alert and note code that
       applies to Docx appears in it. This is the coverage pattern the analysis names, applied to
       Docx.
5. **Notes.** No new codes. `code-not-drawn` gets Docx in its formats if E09-S04 has not added it,
   and its advice names the image converter.
6. **Tests.**
   - **Charts.**
     - Bar, line and area charts, stacked and not, in a document: the chart part and embedded
       workbook exist, and the validator is clean.
     - The workbook's `Sheet1` holds the categories and series.
     - The PPTX chart output is unchanged.
   - **Drawings.**
     - A Mermaid flowchart with `SvgImageConverter` gives a picture with a PNG blip and an SVG
       extension, both parts present, and the natural size is respected.
     - Without a converter it stays code with `code-not-drawn`.
   - **SVG images.** An `.svg` image gets an SVG extension, and is an error without a converter,
     as in the other generators.
   - **Financial charts** (`ImageGraphic` or `DrawingGraphic` from the Charts package) are placed.
   - **Samples.** `report.md` with the diagram renderers (as the PDF tests' `WithDiagrams`) is
     generated with neutral and sample. It is validated and round-tripped through `DocxConverter`:
     drawings come back as images, and charts as nothing until E08. It is written to `.tmp/review/`.
   - The tests reference `wwosik.DocLibs.DocumentConversions.Svg` and
     `SkiaSharp.NativeAssets.Linux`, as the Svg tests do. The Docx package itself does not.
7. **Docs and changelog.**
   - **Docs.**
     - The Docx page: *Diagrams and charts* (native charts; drawings as SVG + PNG; the converter
       needed; fonts for the PNG; which viewers show the SVG), and the known limits.
     - The Charts and Diagrams pages: one line each on Word.
     - The DocumentGeneration page: the notes table.
   - **Changelog.** `CHANGELOG.md` *New*: charts and diagrams in Word, and the DOCX authoring
     guide. *Breaking changes*: none; decks are unchanged after the `ChartWriter` move, and tests
     prove it.
8. **Verification in real Word and LibreOffice.**
   - **Word.** Through the Word export of E09-S01. The check confirms:
     - charts open in *Edit Data* with their workbook;
     - Mermaid diagrams are sharp when zoomed (the SVG is used);
     - the documents open without a repair prompt.
   - **LibreOffice,** where installed. The check confirms the charts are drawn and the PNG fallback
     (or the SVG) shows. Otherwise it is recorded as not available.
   - **Viewers.** Which viewers draw the SVG and which fall back to the PNG is recorded in the docs.

## Acceptance criteria

- [ ] **Charts.** A `chart` block becomes a native Word chart whose data opens in Word's *Edit Data*.
  Its size is the text width by 9/16, and its series take the brand's colours.
- [ ] **No change to decks.** They generate byte for byte as before after `ChartWriter` moved.
- [ ] **Drawings.** A Mermaid diagram with `SvgImageConverter` registered is a picture with a PNG
  and its SVG (`asvg:svgBlip`), which Word draws as a vector.
- [ ] **No converter.** A diagram stays code and gives `code-not-drawn` with advice naming the
  converter.
- [ ] **SVG images.** An `.svg` image in the Markdown keeps its SVG beside the PNG.
- [ ] **Alt text.** Drawings and charts carry it.
- [ ] **Checks.**
  - All outputs pass the Open XML validator.
  - The report sample round-trips through `DocxConverter` as committed.
  - The documents open in Word without a repair prompt. LibreOffice is checked, or recorded as not
    available.
- [ ] **Authoring guide.** `DocxGenerator.AuthoringGuide(options)` covers structure, front matter,
  slide syntax, diagrams and charts, characters and notes. Its examples generate without notes, it
  lists every Docx key, container, alert and note code, and it matches the committed guides.
- [ ] **Docs.** They are updated, as are the CHANGELOG and `ai/memory.md`, and all tests pass.

## Open questions

1. Chart size in Word: the text width by 9/16, capped at 80 % of the text height (proposed: the same
   ratio as a slide's chart area)? Or a size set in the `chart` block (a new `height:` setting),
   which the Charts package would have to learn for all formats?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Drawings when no image converter is registered: stay code with `code-not-drawn` (proposed: no
   broken or blank picture)? Alternatives:
   - SVG only, with a blank PNG of the right size, which shows in Word 2016+ but as an empty box
     elsewhere;
   - the Docx package referencing `DocumentConversions.Svg` itself (SkiaSharp is accepted), so
     drawings always work, but every consumer then needs SkiaSharp's native assets.

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Charts on caller templates without a brand: series colours from the neutral brand, as decks do
   (proposed)? Or theme colour references (accent 1–6), so the chart follows the template's theme?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. SVG written into the document as it came (proposed), or the sanitized SVG (`SvgSanitizer`), so
   the file carries no scripts or external links even if a viewer would follow them? Sanitizing is
   safer for documents that are passed on. It needs the sanitizer, which lives in the Svg package,
   to be callable from Docx.

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Fonts of the PNG fallback: leave them to the host's `SvgImageConverter.Fonts`, documented
   (proposed: the interface cannot pass fonts)? Or extend `IImageConverter.Convert` with the
   brand's fonts, a breaking change of a public interface?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Alt text for drawings: the first line of the code block (proposed: always there, if terse), or
   empty with a note asking for a caption, as an accessibility check would? Accessibility is out of
   scope by decision, which argues for the simple default.

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Captions for charts and diagrams: whatever E03-S02 sets for code-block graphics (proposed: one
   rule for all formats), or a Word-only default where a chart's `title` becomes its "Figure n"
   caption?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Should the guide coverage test (every key, container, alert and note code appears) be added to
   the PPTX and PDF guides too in this story? Proposed: no, Docx only here, with a technical story
   for the others, to keep this story to Word.

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- **Depends on E09-S01:** package, the shared Open XML sources, and the guide skeleton.
- **Depends on E09-S04:** images, fitting and captions. It also depends on E09-S02's guide
  sections. Charts reuse E04-S02's `ChartWriter`. SVG + PNG builds on E04-S04.
- **Shares with E03-S02** the caption syntax of code-block graphics (`Figure: …`, question 7),
  agreed there. Whichever of E03-S02 and E09-S04 is implemented first adds the shared `Captions`
  helper; this story reads it.
- The other Word visuals (icons, QR codes, timelines, Gantt) come with E04-S05 and are not
  part of this story.

## Implementation notes

### Breaking changes

One, in a released package: **`DocxConverter` no longer reads a paragraph whose style id or name
contains "table" or "tabelle" as a heading** (the E09-S04 loose end: `Table Title` came back as `#`).
A `Table Heading` or `Tabellentitel` style in someone else's document now comes back as a paragraph.
Recorded in `CHANGELOG.md` with what to do.

Nothing else breaks. Decks and PDFs are unchanged (verified below) after `ChartWriter` moved. The
DOCX package is unreleased; inside it, what used to stay code, alt text or typed text is now drawn
(with an image converter), `:icon-x:` with an unknown name now throws as in the other generators, an
icon without an image converter is left out with `icon-not-drawn` instead of staying typed, and a
renderer's own notes are passed on instead of muted. Added: `DocxOptions.Icons`,
`GeneratedTexts.Chart`; `icon-not-drawn` lists DOCX. The renderer guide sections (`Charts.md`,
`Diagrams.md`) gained a few words on Word, so the PPTX and PDF expected guides changed by one
sentence each (guides only, no output).

### Decisions and deviations

- **`ChartWriter`** is `src/Shared/OpenXml/ChartWriter.cs` (linked into Pptx and Docx, namespace
  `wwosik.DocLibs.DocumentGeneration`): `Add(OpenXmlPart owner, ChartSpec, string? text, string grid,
  string? font)`. **Deviation:** the colours are `a:solidFill` XML strings, not hex as the plan said,
  because decks pass theme colours (caller templates) and translucent white (dark slides), which hex
  cannot carry; decks pass exactly the `SlideColor.Fill().OuterXml` they wrote before, Word passes
  `ChartWriter.Solid(hex)`. Its `XmlSafe` is the deck writer's rule, copied, so titles keep their bytes.
- **`SvgBlip`** (`src/Shared/OpenXml/SvgBlip.cs`): the extension URI, `IsSvg` (moved from
  `DeckParser`, same rule) and the `a:extLst` XML Word writes. Decks use the URI and `IsSvg` but keep
  writing their blip with SDK objects, so their bytes do not change.
- **Charts in Word:** an inline `w:drawing` with `c:chart r:id` in a centred paragraph of the frame's
  style (as images), the text width by 9/16, capped at 80 % of the text height (question 1); gridlines
  in `TableLine`, text colour the theme's, font the brand's family, or the theme font where the brand
  has no font files (the plan said "the brand family"; naming Arial in a caller's theme would be
  wrong). Series colours are `ChartSpec.Colors` from the renderer, which gets `Brand.Neutral` on caller
  templates (question 3). Alt text: the title, else `GeneratedTexts.Chart` ("Chart"/"Diagramm"). A
  chart's `source:` follows in a `Caption` paragraph "Source: …" without a number (the plan did not say
  where it goes). A chart inside a footnote is drawn as its picture: a footnotes part holds no chart.
- **Drawings** (`DocxBody.Graphics.cs`): `DrawingSvg.Write` with the brand family (Arial without one)
  and the code font as mono family; **hover texts and hotspots are stripped first**, because the
  SVG's tooltips are CSS `:hover` that the PNG converter would draw. PNG at scale 2 through
  `ImageConverters`, placed at the natural size the converter reports and fitted by E09-S04's rule;
  PNG and SVG are separate image parts, the SVG in `asvg:svgBlip`. `ImageGraphic`s are placed like
  Markdown images (they need no converter). Alt text: the block's first line **with a letter or
  digit**, cut at 120 characters (question 6; the extra rule passes over a JSON block's `{`, so a
  financial chart's alt text is its `"type": …` line).
- **Without an image converter** (question 2): the block stays code with `code-not-drawn`, message
  "… Register SvgImageConverter from wwosik.DocLibs.DocumentConversions.Svg to draw diagrams in Word."
  No new note codes. `NoteCodes.FormatAdvice` (new, internal) adds Word-only advice to the guides'
  `code-not-drawn` and `icon-not-drawn` entries, so the PPTX and PDF guides keep their text.
- **SVG images** keep the SVG as it came (question 4) beside the converter's PNG; without a converter
  they throw as before.
- **Captions:** a `Figure:` paragraph right after a fenced block that was drawn (chart, picture,
  renderer image) is its numbered caption below it, the graphic (and a chart's source) kept with it;
  after a block left as code it stays text with `caption-ignored`.
- **QR codes and icons (E09-S02's leftovers), done in full:** `qr:` images go through
  `QrDrawing.ForImage` (so their attribute and length errors match the other formats) and become a
  picture; without a converter their alt text with `code-not-drawn`. Icons: `DocxOptions.Icons`
  (default `TablerIcons.Outline`, `null` = off), `IconSyntax.Apply` after the coloured words in
  `DocxRuns.Pieces`; an icon run (`DocxRun.Icon`) becomes an inline picture 10.5 pt square in the
  diagram text colour, one image pair per name and part, wherever runs are written (text, headings,
  cells, lists). Without a converter the icon is **left out** with `icon-not-drawn` (that code's
  meaning), not kept as typed text as the brief suggested. A `qr:` image still keeps a paragraph of
  images from becoming an image row, and QR codes in cells and headings stay alt text.
- **Drawn containers** (`timeline`, `gantt`, `steps`, `riskmatrix`) are pictures too (E09-S02 left
  them to this story), drawn by `VisualContainers.Draw` with the options' `TimeProvider`; their alt
  text is the first paragraph or list item of their content, or their table's first row joined by
  " · ". Without a converter their content follows in sequence with `code-not-drawn`.
- **Guide.** `Docx.md` gained *Diagrams, charts and QR codes* (`{{drawings}}`: what is drawn with the
  options' renderers and image converters, with the drawn containers' syntax and an example only when
  a converter is set up), `{{icons}}` (Tabler's section with a converter, a short "not drawn here"
  otherwise), `{{diagrams}}` (the renderers' sections), `{{imageFormats}}` (SVG with a converter), a
  `::: conclusion` / `::: comment` row and an alert in the example; characters and notes were already
  there. `Charts.md` says charts are native in decks and Word, `Diagrams.md` that a diagram is a
  picture in Word. All three guides stay under 3,500 words, the PPTX guide's budget (sample and custom
  about 3,000).
- **Coverage test** (question 8, Docx only): every key of the template plus the shared and deck keys
  and `labels.*`, every container of `LayoutSyntax.Containers`, every alert and every note code whose
  formats include DOCX appear in the guide, for neutral, sample and custom. The guide tests use the
  Mermaid and chart renderers and `SvgImageConverter` on `sample` only; every example generates valid
  and **without notes** on all three.
- **Fonts of the PNG:** left to `SvgImageConverter.Fonts` (question 5), documented on the Docx page.

### Verification

- `./run.sh build`: 0 warnings, 0 errors (one "Internal CLR error" fixed by
  `dotnet build-server shutdown`). `./run.sh test` all green: 1 + 123 + 276 + 78 + 71 + 22 + 141 +
  208 + 372 = 1,292 passed, 6 Windows-only skipped.
- **New tests** (`Docx.Tests/GraphicsTests.cs`, 24 with theory rows): bar, stacked bar, line, area,
  stacked area as native charts (chart part, relationship from the drawing, workbook `Sheet1` with
  categories and series), each valid; size, colours, gridline colour, font and alt text on the sample
  brand; the 80 % cap on a landscape page; a caller's template (neutral palette, no font, German
  "Diagramm"); source and caption after a chart; a Mermaid flowchart with `SvgImageConverter` (PNG
  blip, SVG extension and part, Arial in the SVG, alt text, natural width); without a converter
  (code, the advice, no image parts); `Figure:` after a drawn diagram and after code; an `.svg` image
  (SVG as it came, natural size) and the error without a converter; a financial chart (no tooltips in
  the SVG); a renderer's image with caption; QR codes with and without a converter and a bad
  attribute; icons drawn, left out with a note, an unknown name, `Icons = null`; a timeline with and
  without a converter; `report.md` with a stand-in diagram renderer and `ChartRenderer` (as the PDF
  tests' `WithDiagrams`: the report's `graph TD; A-->B;` is no Mermaid the real renderer takes),
  neutral and sample, validated and round-tripped (`report-diagrams-<template>.roundtrip.expected.md`:
  the diagram comes back as an image); the new sample `drawings.md` (three charts, two Mermaid diagrams
  with a caption, a financial chart, an SVG image, a QR code, icons in text and lists, all four drawn
  containers) on neutral, sample and custom: valid, without notes, round-tripped
  (`drawings-<template>.roundtrip.expected.md`: charts come back as nothing, everything else as images
  with their alt text). `ContentTests`: a `Table Title` comes back as text. `AuthoringGuideTests`
  rewritten as above.
- **Validator:** every `.docx` the tests write passes `OpenXmlValidator(Microsoft365)`, charts, SVG
  extensions and icon pictures included.
- **Unchanged output:** the 50 decks and PDFs the PPTX and PDF tests write to `.tmp/review/`, from
  `db882db` (run before any change) and from this change: identical, decks part by part with the SDK's
  relationship ids masked (embedded chart workbooks included; eight decks hold charts, dark-slide
  charts among them), PDFs byte for byte with `/ID` and dates masked.
- **Looked at:** the PNGs in `drawings-sample.docx` (flowchart, timeline, steps, QR code with its
  caption, icons) are drawn as expected.
- **For review** in `.tmp/review/`: `drawings-neutral.docx`, `drawings-sample.docx`,
  `drawings-custom.docx`, `report-diagrams-neutral.docx`, `report-diagrams-sample.docx`, besides the
  earlier stories' files.

### To verify by hand (no Word or LibreOffice on this machine)

Open `drawings-*.docx` and `report-diagrams-*.docx` in Word (and *File → Export → PDF*), and check:

1. **No repair prompt** on any file (chart parts with embedded workbooks in a Word document, SVG
   parts, icon pictures inline in text).
2. **Charts:** the three charts under "Charts" are drawn in the brand's colours with light gridlines;
   right-click → *Edit Data* opens the workbook with the months in column A. Record whether the
   chart text takes the theme font on `drawings-neutral.docx` and `drawings-custom.docx`.
3. **SVG:** zoom to 400 % on the flowchart, the timeline and an icon: sharp edges mean Word draws the
   SVG. *Graphics Format* offers *Convert to Shape* for an SVG picture.
4. **Icons** sit on the text line, about as high as the letters, without spreading the lines.
5. **Captions:** "Figure 1: Orders against the plan" under the bar chart (after "Source: Sales report
   2026"), "Figure 2: How a request is handled" under the flowchart; after *Update field* the numbers
   stay.
6. **Fonts:** the PNG fallbacks use the converter's fonts (here the system's); Word should not show
   them unless it cannot draw SVG.
7. **LibreOffice:** not installed here. Where available, `soffice --headless --convert-to pdf` of the
   same files: record whether the charts are drawn and whether the pictures show the SVG or the PNG,
   then update *Which viewers show what* in the Docx docs, which says this was not checked.

### Loose ends

- The viewers paragraph of the Docx docs stays unconfirmed until item 7 is done.
- A financial chart's alt text is its JSON's `"type"` line; a better one would need renderers to pass
  a title (a public `CodeBlockGraphic` change), not done.
- Harvey balls (`[3/4]` in a cell) are still text in Word; QR codes and images in table cells and
  headings are still alt text.
- The coverage test for the PPTX and PDF guides is a technical story still to write (question 8).
- `DocxConverter` still reads `Container Heading` as a heading and drops charts (E08).
