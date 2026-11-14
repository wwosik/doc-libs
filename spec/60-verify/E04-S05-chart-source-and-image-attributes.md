# E04-S05 Chart source, semicolon CSV and image attributes

> Status: in verification
> Epic: [E04 Diagrams and charts](../50-todo/E04-diagrams-and-charts.md)

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

- [x] `source: Annual report` appears under the chart in a PDF and a deck, as "Source: Annual
  report" or "Quelle: Annual report" with `lang: de`; a chart without `source:` is unchanged.
- [x] A chart with a `;` header reads `12,5` as 12.5; `1.234,5` in it is an error naming the cell
  and the fix; CSV with `,` reads as before.
- [x] `fit=cover focus=top` crops a portrait photo to a landscape box from its top on a slide
  (`a:srcRect`), and in an image row of a PDF; on a single PDF image it gives `attribute-ignored`.
- [x] `.round` gives a circle: the `ellipse` preset in decks, a clipping path in PDFs.
- [x] `.grey` gives `a:grayscl` in decks and a grey image in PDFs (pixel test: R = G = B), embedded
  once when used twice.
- [x] An unknown attribute or value throws `DocumentConversionException` listing the valid ones.
- [ ] Decks pass the Open XML validator; PDFs with every attribute stay PDF/A-2b (veraPDF).
- [ ] A deck with `fit=cover`, `.round` and `.grey` (also on an SVG image) opens in PowerPoint
  without repair and looks as intended; the result is recorded in the implementation notes.
- [x] Samples without the new syntax produce the same decks and PDFs as before.
- [x] `THIRD-PARTY-NOTICES.txt` is in the PDF package; guides, docs and CHANGELOG are updated, and
  all tests pass on Linux CI.

## Open questions

1. Split: the analysis lists one "visual blocks" story. Split it into four (E04-S05 chart source,
   CSV and image attributes; E04-S06 QR codes and icons; E04-S07 timeline and Gantt; E04-S08 steps,
   Harvey balls and risk matrix) (proposed: each is a few hundred lines with its own risks and
   Office checks), or merge them back into one?

> Answered by the human (2026-11-14): as proposed.

2. `source:` prefix: written by the generator from the document's texts, "Source: …" (proposed,
   needs `CodeBlockRequest.Texts`), or shown exactly as written, so the writer types "Source:"?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Where SkiaSharp goes: referenced by the PDF package only (proposed: decks do grey and round
   natively, PDFs only need pixels for `.grey`), by the shared DocumentGeneration package for all
   generators (DOCX will want it too), or kept out of the generators behind an opt-in hook
   implemented in the SVG package, so PDF users who never grey an image carry no native library?

> Answered by the human (2026-11-14): as proposed.

4. `fit=cover` and `focus` on a single PDF image: no effect with a note (proposed), or a new
   attribute such as `ratio=16:9` that gives PDF images a box to crop to?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Decimal comma: only with `;` unless `decimal:` says otherwise (proposed), or guessed per
   number?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. `scale` in the attributes: the same meaning as `scale=` in the title, which differs between
   decks and PDFs (proposed: one syntax, no new meaning), or a new, shared meaning such as a share of
   the available width?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. `.grey` on a converted SVG in decks: rely on `a:grayscl` if PowerPoint applies it to the SVG
   (proposed, checked in Office), or always drop the SVG blip for grey images?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

- **Braces right after an image are image attributes.** `![a](x.png){.round}` no longer shows
  `{.round}` as text, and a `{…}` of classes and `key=value` pairs directly after an image that
  names anything unknown now throws `DocumentConversionException` (a space before the braces keeps
  them text).
- **The PDF package depends on SkiaSharp** (4.153.1, MIT), used only for `.grey`. On Linux an
  application that greys images adds `SkiaSharp.NativeAssets.Linux` (or `.NoDependencies`) at that
  version; without it a `.grey` image is a `DocumentConversionException` naming the package.
- **`CodeBlockRequest.Texts` is part of the request's equality.**

All three are in `CHANGELOG.md` under *Unreleased*. Output without the new syntax is unchanged (see
Verification).

### Decisions

- **Where the attributes are taken off.** `ImageAttributes.Take` (DocumentGeneration, beside
  `HeadingAttributes`) is called from both parsers' `RunCollector` when it meets an image, as the plan
  says. It joins the `LiteralInline`s after the image, because Markdig splits that text (a lone `=`
  is a literal of its own, from the `==mark==` extension), cuts the matched `{…}` off them, and keeps
  the result on the `LinkInline` (`SetData`), so a second look at the same image gets the same
  attributes instead of none. Images shown as their alt text (headings, table cells, the PDF cover's
  subtitle) also lose their braces and give `attribute-ignored`.
- **Error messages** name the block as written and its line: "The image attributes '{fit=fill}' at
  line 3 cannot be used: fit is contain or cover, not 'fill'." An unknown name lists every valid
  attribute. `scale` in both title and attributes names the image and its line.
- **`.round` with `fit=cover`** is a circle as large as the box's shorter side, centred in the box,
  rather than an ellipse filling the box: the plan says the image becomes a square, then a circle.
  `.round` without cover is the image's square (its shorter side) fitted like any image.
- **`attribute-ignored`** is given for: `focus` without `fit=cover` or `.round`; `scale` with
  `fit=cover` (the box decides the size); in PDFs `fit=cover` on an image of its own and `.caption`;
  and any attributes on an image shown as alt text. Decks honour everything else.
- **PDF crops** are one helper, `LayoutEngine.Placed`: every image is cropped to the aspect ratio of
  its placed box with `ImageAttributes.Crop`, so uncropped images (same ratio) come out exactly as
  before, without a clip. A cropped or round one is drawn whole and larger, inside `q`, a rectangle
  or four-Bézier ellipse, `W n`, … `Q` (`ImageItem.Clip`, `PdfRenderer.Clip`).
- **Rows in PDFs:** cover images take the height of the tallest other image in the row, or 3/4 of the
  cell width when all cover, capped at the page height; a round cover image is the largest circle
  in that box.
- **`.grey` in PDFs** uses an `SKColorFilter` colour matrix (Rec. 709 in R, G and B, alpha kept) on an
  `SKCanvas`, so every channel gets the same value; PNG stays PNG (with its soft mask), JPEG becomes
  JPEG at quality 90. The grey copy is cached per resource in `MarkdownParser`, so it is one
  `ImageItem.Key` and embedded once. An SVG through an image converter is greyed after conversion.
  `GreyImages.Skia` is `NoInlining`, so SkiaSharp's types load only when an image is greyed;
  `DllNotFoundException`, `TypeInitializationException`, `EntryPointNotFoundException` and
  `BadImageFormatException` become the `DocumentConversionException` that names the native package.
- **`.grey` in decks** is `a:grayscl` before the blip's extension list (schema order), also on a
  converted SVG, whose `asvg:svgBlip` stays (question 7).
- **Chart source.** `ChartDrawing` adds the line at baseline 392 (10 px, left at 8, `MaxWidth` 624)
  and makes the drawing 398 px tall; without a source it stays 640 × 380. In decks
  `DeckWriter.Chart` estimates the line with `EstimateHeight` at `MinTableSize` (10 pt), takes it off
  the frame's height, writes the text box (table font, the slide's text colour, white on painted
  dark slides) right under the frame, and returns frame plus line, so captions and fitting see it.
  The comment in `DeckWriter.Chart` marks where E01-S06's chart-source placeholder would take over.
  `DeckWriter` and `DeckParser` get the document's `GeneratedTexts` as new constructor arguments.
- **CSV.** The separator is decided by the header row; `decimal:` overrides the mark. With a decimal
  comma, any `.` in a value is an error (also `1.5`), so nothing is guessed; the error suggests
  `decimal: point`. A quoted cell may hold the separator, as before.
- **THIRD-PARTY-NOTICES.txt** is packed at the PDF package's root with SkiaSharp (MIT, its licence
  text) and Skia (BSD-3-Clause, the text from Skia's LICENSE), and points at `Fonts/OFL.txt`. The
  Svg package, which also ships SkiaSharp and Svg.Skia, was left without one: not this story's
  package.
- **Tests' images.** The deck tests make PNGs of any size themselves (`TestImages`, zlib and a CRC,
  no Skia); the PDF tests use SkiaSharp, so the PDF test project references
  `SkiaSharp.NativeAssets.Linux`, as the Svg tests do.

### Deviations from the plan

- None in behaviour. The samples (`deck.md`, `layouts.md`, `report.md`, `long.md`) were not given
  the new syntax: they are the "unchanged output" baseline, and the new syntax has its own tests and
  review files (below).

### Verification

- **Tests:** all pass, `./run.sh build` 0 warnings, 0 errors. Totals after rebasing onto E07-S01:
  DocumentConversions 104, WindowsOnly 7 (6 skipped, Windows only), DocumentGeneration 109, Charts
  78, Diagrams 71, Svg 22, Pptx 235, Pdf 153 — 779, 773 run. New: `ChartSourceTests` (Charts), `ImageAttributeTests` (Pptx and
  Pdf), one review test in `SvgConverterTests`. Guides regenerated with `DOCLIBS_UPDATE_EXPECTED=1`.
  Twice a `dotnet test` run aborted with "Internal CLR error (0x80131506)" before printing anything
  (in the SDK's own process, not a test host), while other agents were building in parallel on the
  same machine; the reruns passed. Watch for it on CI.
- **Unchanged output:** the four samples and `diagrams.md`, plus a chart-and-image-row document, in
  both built-in brands, generated with the claim commit (`b1e04f0`) and with this change: deck XML
  identical with the SDK's random relationship ids masked, PDF pages identical pixel for pixel
  (rendered with pypdfium2). Only the embedded chart workbooks differ, as they do between two runs of
  the same code.
- **Open XML validator:** clean for every deck the new tests write, cover, round, grey, grey SVG,
  rows and the chart source included.
- **PDF rendering:** `image-attributes.pdf` and `image-attributes-svg.pdf` (in `.tmp/review/`) were
  rendered with pypdfium2 and look as intended: a grey circle, a cover image cropped to its focus in
  a row, grey JPEG and grey SVG.
- **Native AOT:** a console app that greys and rounds an image in a PDF publishes with
  `PublishAot` on linux-x64 without warnings and runs. `IsAotCompatible` stays on.
- **Package:** `THIRD-PARTY-NOTICES.txt` and `Fonts/OFL.txt` are in the packed `.nupkg`, and the
  nuspec lists SkiaSharp 4.153.1.

### To verify by hand (no PowerPoint, no Java here)

1. **PowerPoint** — open `.tmp/review/image-attributes.pptx` (written by
   `SvgConverterTests.ImageAttributes_OnPhotosAndASvg_InADeckAndAPdf`) in PowerPoint 2016 or later:
   - it opens without a repair prompt;
   - slide "Cover": the portrait fills the box, cropped from its top (the green band visible);
     *Crop* in PowerPoint shows the hidden part;
   - slide "Round and grey": a red circle, a grey portrait, and the SVG (red and blue squares) in
     grey. **If the SVG shows in colour**, PowerPoint does not apply `a:grayscl` to `asvg:svgBlip`;
     then the plan's fallback applies (a grey SVG gets a grey PNG and no SVG blip) — tell the next
     agent;
   - slide "Orders": "Source: Annual report" sits right under the native chart in 10 pt and does
     not overlap its axis labels or legend.
2. **veraPDF** (PDF/A-2b) on `.tmp/review/image-attributes.pdf` and `image-attributes-svg.pdf`:
   clipping paths and re-encoded images should not affect conformance, but it was not run here.
3. **Linux without native assets:** an app without `SkiaSharp.NativeAssets.Linux` that greys an image
   should get the `DocumentConversionException` naming the package; not reproducible in this test
   setup, which has the native assets.

### Loose ends

- `financial-chart` has no `source` (the plan leaves it unchanged); its JSON could take one later.
- E01-S06 can give the chart source a placeholder in `DeckWriter.Chart`.
- E09 (DOCX) can reuse `ImageAttributes` and `ChartSpec.Source` as they are.
- A single PDF image cannot be cropped; a box-giving attribute (`ratio=16:9`, question 4) would make
  `fit=cover` work there.
