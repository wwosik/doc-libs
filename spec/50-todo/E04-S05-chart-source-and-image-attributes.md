# E04-S05 Chart source, semicolon CSV and image attributes

> Status: todo
> Epic: [E04 Diagrams and charts](E04-diagrams-and-charts.md)

## Summary

Three small extensions of what decks and PDFs already draw. A `chart` block gets a `source:`
setting, drawn under the chart, and accepts CSV separated by `;` with a decimal comma, as a
spreadsheet in a German locale exports it. An image takes attributes after it,
`![Team](team.jpg){focus=top fit=cover scale=0.8 .round .grey}`, to crop it to its box, make it a
circle or show it in grey.

This is the first of four stories split from the analysis's single "visual blocks" item (see
question 1): E04-S05 (this), E04-S06 QR codes and icons, E04-S07 timeline and Gantt, E04-S08 steps,
Harvey balls and risk matrix.

## Context

- **Charts:**
  - `ChartParser.Parse` reads the settings `type`, `title`, `unit` and `stacked` above a line
    `---`, and rejects any other key ("Unknown setting … Valid settings: type, title, unit,
    stacked."). Below it, `Cells` splits each row at commas outside double quotes, and values are
    parsed with `CultureInfo.InvariantCulture`; `1,5` is an error asking for a decimal point.
  - `ChartSpec` (public, in `CodeBlockRenderer.cs`) has `Title`, `Unit`, `Stacked` and `Colors`,
    and a hand-written `Equals`.
  - `ChartRenderer.Render` returns `ChartGraphic(ChartSpec, Drawing)`: decks write the spec as a
    native chart (`ChartWriter.Add`, placed by `DeckWriter.Chart` at most 16:10 in its box), PDFs draw
    the `Drawing` from `ChartDrawing.Draw` (640 × 380 px, legend at the bottom).
  - `CodeBlockRequest` carries the fence's language, code, brand and line, but not the document
    language, so a renderer cannot write a generated text such as "Source".
- **Images:**
  - The only image setting is `scale=N` in the title (`DeckParser.ScalePattern`,
    `MarkdownParser.ScalePattern`), plus `caption` on slides (`DeckParser.CaptionPattern`). It means
    different things: on slides an upper bound on enlargement (`SlideImage.Scale`, against
    `PptxOptions.MaxImageUpscale`), in PDFs a factor on the natural size (`ImageBlock.Scale`).
  - Markdig is not given generic attributes (see the memory on `HeadingAttributes`), so
    `![a](x.png){.round}` is an image followed by the literal text `{.round}`, which both generators
    write as text today.
  - Decks: `DeckWriter.Picture` fits the image into its box keeping its aspect ratio, writes a
    `p:pic` with `a:stretch` and a rectangle preset, and adds the SVG as `asvg:svgBlip` for converted
    SVGs (`DeckWriter.Blip`). No `a:srcRect`, no other preset, no `a:grayscl`.
  - PDFs: `LayoutEngine.Image` / `ImageRow` place an `ImageItem`; `PdfRenderer` calls PdfPig's
    `AddPng` / `AddJpeg`. Drawings already write operators straight into
    `PdfPageBuilder.CurrentStream.Operations` inside `q … Q` (`PdfRenderer.DrawShape`), so a clipping
    path around an image is possible the same way.
  - SkiaSharp is used only in `wwosik.DocLibs.DocumentConversions.Svg` (E04-S04), with the trap that
    its managed version must match `SkiaSharp.NativeAssets.Linux`. The human accepted SkiaSharp as a
    dependency of the generators (analysis, answer 9).
- Notes come back through `GenerationResult` (E05-S03); generated texts per language through
  `GeneratedTexts` (E06-S02).
- E01-S06 (template roles, planned in parallel) may give a chart-source placeholder; E09 (DOCX) will
  reuse the chart spec and the image attributes.

## Proposed approach

1. **Chart `source:`.**
   - `ChartParser` accepts `source: <text>`; the error message lists it among the valid settings.
   - `ChartSpec.Source` (`string?`, init), in `Equals`.
   - **PDF:** `ChartDrawing.Draw` adds one line under the legend, left-aligned, 10 px, in the diagram
     text colour, with `MaxWidth` of the drawing, as "<Source>: <text>". The drawing grows by that
     line only when there is a source, so charts without one are unchanged.
   - **Decks:** `DeckWriter.Chart` takes one text line off the chart's height and writes a text box
     under the graphic frame in the table font at the table minimum size. When E01-S06 offers a
     chart-source placeholder on the slide's layout, the text goes there instead.
   - **Generated text:** `GeneratedTexts.Source` ("Source" / "Quelle"). Renderers get the document's
     texts through a new init property `CodeBlockRequest.Texts` (default `GeneratedTexts.English`,
     part of equality), set by both parsers. `financial-chart` is not changed in this story.
2. **CSV with `;` and decimal comma.**
   - The separator is decided by the header row: if it has a `;` outside quotes, every row is split
     at `;`, otherwise at `,` as today.
   - With `;`, numbers use a decimal comma: `1234,5` and `-0,25`. A `.` in a number is an error that
     says to remove the thousands separator, so `1.234` is never read as 1.234 or 1234 by guess. With
     `,`, nothing changes.
   - An optional setting `decimal: comma|point` overrides the default for either separator.
3. **Image attributes: syntax.**
   - A new internal `ImageAttributes.Split` in DocumentGeneration, beside `HeadingAttributes`: a
     `{…}` that directly follows an image (no space), made of classes (`.round`, `.grey`) and
     `key=value` pairs, quoted or not. Anything else in braces stays text.
   - Known: `focus=top|bottom|left|right|center`, `fit=contain|cover`, `scale=<number>`, `.round`,
     `.grey`, `.caption` (the same as `caption` in the title). An unknown name or value throws
     `DocumentConversionException` listing the valid ones; a key given twice, or `scale` both in the
     title and the attributes, throws too.
   - Both parsers take the attributes off the literal that follows the image in their
     `RunCollector`, so the braces never reach the text. `scale=` keeps the meaning it has in each
     format today; the title form keeps working.
4. **`fit` and `focus`.**
   - `contain` (default) is today's behaviour. `cover` fills the image's box and crops what sticks
     out; `focus` says which part stays (default `center`).
   - **Decks:** the box is the visual area, the cell of an image row, or the image's place on a flow
     slide or in a card. `Picture` places the frame at the box size and writes `a:srcRect` with the
     cropped percentages.
   - **PDFs:** single images have no box: `cover` and `focus` do nothing there and give the note
     `attribute-ignored`. In an image row, cover images take the row's height (that of its tallest
     uncropped image, or 3/4 of the cell width if all are cover) and are cropped by a clipping path.
5. **`.round`.** The image is cropped to a square (by `focus`), then to a circle.
   - **Decks:** `a:srcRect` for the square and the preset `ellipse` instead of `rect`; the picture
     stays a picture PowerPoint can edit.
   - **PDFs:** a circular clipping path (four Béziers, as `DrawingShapeKind.Ellipse` is drawn)
     around the image, inside `q … Q`. No pixel work.
6. **`.grey`.**
   - **Decks:** `a:grayscl` inside the `a:blip`. Whether PowerPoint also greys the `asvg:svgBlip` of
     a converted SVG is checked in Office; if not, a grey SVG image gets a grey PNG and no SVG blip.
   - **PDFs:** the pixels are converted: SkiaSharp decodes the PNG or JPEG, a luminance colour
     matrix (Rec. 709) turns it grey with its alpha kept, and it is encoded again as PNG (from PNG)
     or JPEG at quality 90 (from JPEG). The result is cached per resource, so an image used twice is
     converted and embedded once.
   - `SkiaSharp` is referenced by the PDF package only (question 3), at the same version as in
     `Directory.Packages.props`. Without native assets on Linux, the
     `DllNotFoundException`/`TypeInitializationException` becomes a `DocumentConversionException`
     that names `SkiaSharp.NativeAssets.Linux`. Documents without `.grey` never load Skia.
7. **Notes.** New `NoteCodes.AttributeIgnored` (`attribute-ignored`): an image attribute that has
   no effect in this format or this place. It is added to `NoteCodes.All` and the docs table.
8. **Dependencies and licences.**
   - SkiaSharp is MIT. The PDF package gets a `THIRD-PARTY-NOTICES.txt` packed at the package root
     (as `Fonts/OFL.txt` is for the fonts), naming SkiaSharp, its licence and its native Skia
     (BSD-3-Clause). Later stories add their entries to the same file.
   - `IsAotCompatible` stays on the PDF package; a Native AOT publish of a test app that greys an
     image is checked for warnings, as E04-S04 did for the SVG package.
9. **Guides, docs, changelog.**
   - The Charts guide section: `source:`, `;` CSV and `decimal:`. The PPTX and PDF guides: image
     attributes, with what each does per format. The expected guides are regenerated.
   - Docs: the Charts page (source, CSV), the PPTX and PDF pages (image attributes, Linux native
     assets for `.grey` in PDFs), the DocumentGeneration page (`ChartSpec.Source`,
     `CodeBlockRequest.Texts`, the new note code).
   - `CHANGELOG.md`: new: `source:`, `;` CSV, image attributes. Breaking: `{…}` of known
     attributes after an image is no longer written as text; the PDF package depends on SkiaSharp
     (native assets needed on Linux for `.grey`).

## Acceptance criteria

- [ ] `source: Annual report` appears under the chart in a PDF and a deck, as "Source: Annual
  report" or "Quelle: Annual report" with `lang: de`; a chart without `source:` is unchanged.
- [ ] A chart with a `;` header reads `12,5` as 12.5; `1.234,5` in it is an error naming the cell
  and the fix; CSV with `,` reads as before.
- [ ] `fit=cover focus=top` crops a portrait photo to a landscape box from its top on a slide
  (`a:srcRect`), and in an image row of a PDF; on a single PDF image it gives `attribute-ignored`.
- [ ] `.round` gives a circle: the `ellipse` preset in decks, a clipping path in PDFs.
- [ ] `.grey` gives `a:grayscl` in decks and a grey image in PDFs (pixel test: R = G = B), embedded
  once when used twice.
- [ ] An unknown attribute or value throws `DocumentConversionException` listing the valid ones.
- [ ] Decks pass the Open XML validator; PDFs with every attribute stay PDF/A-2b (veraPDF).
- [ ] A deck with `fit=cover`, `.round` and `.grey` (also on an SVG image) opens in PowerPoint
  without repair and looks as intended; the result is recorded in the implementation notes.
- [ ] Samples without the new syntax produce the same decks and PDFs as before.
- [ ] `THIRD-PARTY-NOTICES.txt` is in the PDF package; guides, docs and CHANGELOG are updated, and
  all tests pass on Linux CI.

## Open questions

1. Split: the analysis lists one "visual blocks" story. Split it into four (E04-S05 chart source,
   CSV and image attributes; E04-S06 QR codes and icons; E04-S07 timeline and Gantt; E04-S08 steps,
   Harvey balls and risk matrix) (proposed: each is a few hundred lines with its own risks and
   Office checks), or merge them back into one?

>

2. `source:` prefix: written by the generator from the document's texts, "Source: …" (proposed,
   needs `CodeBlockRequest.Texts`), or shown exactly as written, so the writer types "Source:"?

>

3. Where SkiaSharp goes: referenced by the PDF package only (proposed: decks do grey and round
   natively, PDFs only need pixels for `.grey`), by the shared DocumentGeneration package for all
   generators (DOCX will want it too), or kept out of the generators behind an opt-in hook
   implemented in the SVG package, so PDF users who never grey an image carry no native library?

>

4. `fit=cover` and `focus` on a single PDF image: no effect with a note (proposed), or a new
   attribute such as `ratio=16:9` that gives PDF images a box to crop to?

>

5. Decimal comma: only with `;` unless `decimal:` says otherwise (proposed), or guessed per
   number?

>

6. `scale` in the attributes: the same meaning as `scale=` in the title, which differs between
   decks and PDFs (proposed: one syntax, no new meaning), or a new, shared meaning such as a share of
   the available width?

>

7. `.grey` on a converted SVG in decks: rely on `a:grayscl` if PowerPoint applies it to the SVG
   (proposed, checked in Office), or always drop the SVG blip for grey images?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
