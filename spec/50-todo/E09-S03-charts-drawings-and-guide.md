# E09-S03 Charts, drawings and the DOCX authoring guide

> Status: todo
> Epic: [E09 DOCX generator](E09-docx-generator.md)

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
- **Decided by the human** (analysis question 7): Word drawings are SVG + PNG images, as in the
  fork, not editable shape groups. SkiaSharp is accepted as a dependency of the generators
  (question 9), but no generator references it today.
- **E09-S01** places images and captions and fits them to the width and 80 % of the height. It
  proposes shared Open XML sources in `src/Shared/OpenXml/` (its question 4). It starts
  `AuthoringGuide/Docx.md`, and E09-S02 adds the deck syntax to it.
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
     - It moves to `src/Shared/OpenXml/ChartWriter.cs`, linked into Pptx and Docx (or wherever
       E09-S01's question 4 decides).
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
   - **Captions.** A chart is a figure for captions. Its caption syntax is the one E03-S02 sets for
     code-block graphics, if any (question 7).
2. **Drawings as SVG + PNG.**
   - **The SVG.** A `DrawingGraphic`, a `ChartGraphic`'s drawing where Word needs a picture, and an
     `ImageGraphic` that is SVG are all written with `DrawingSvg.Write`, with
     `FontFamily` set to the brand family (Arial without one).
   - **The PNG** comes from `ImageConverters` (`SvgImageConverter`) at scale 2, placed at the
     reported natural size and fitted by E09-S01's rules.
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
5. **Notes.** No new codes. `code-not-drawn` gets Docx in its formats if E09-S01 has not added it,
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

- **Depends on E09-S01:** package, images, fitting, captions, the shared Open XML sources, and the
  guide skeleton. It also depends on E09-S02's guide sections. Charts reuse E04-S02's
  `ChartWriter`. SVG + PNG builds on E04-S04.
- **Depends on E03-S02** only for the caption syntax of code-block graphics (question 7). Without
  it, charts and diagrams have no captions.
- The fork's other Word visuals (icons, QR codes, timelines, Gantt) come with E04-S05 and are not
  part of this story.
