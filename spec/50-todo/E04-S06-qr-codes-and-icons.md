# E04-S06 QR codes and icons

> Status: todo
> Epic: [E04 Diagrams and charts](E04-diagrams-and-charts.md)

## Summary

Two kinds of small vector graphics written in the Markdown. `![Survey](qr:https://example.com/s)`
draws a QR code for the address, in the brand's colour. `:icon-check:` draws an icon from the
bundled Tabler Icons (MIT): as a card figure and as a list bullet in decks and PDFs, and inline in
PDF text. Both are `Drawing`s, so decks get editable shapes and PDFs vectors, as for diagrams.

## Context

- **Images:** `DeckParser.Image` and `MarkdownParser.Image` look every image URL up with
  `MarkdownDocument.FindResource` and throw "The image '…' is not among the document's resources"
  when it is missing. `qr:…` is therefore an error today, and supporting it changes no working
  document.
- **Drawings:** `Drawing` (shapes with `DrawingShapeKind`, `RoundJoins`, `FillRule`; texts) is drawn
  by `DrawingWriter.Group` in decks (presets for rectangles, custom geometry for paths) and by
  `LayoutEngine.Drawing` / `PdfRenderer.DrawShape` in PDFs. `Drawing.Validate()` runs in every
  writer (E06-S02). On slides, `DeckParser.ITarget.Visual(null, null, drawing)` places a drawing as a
  visual; on flow slides it is a `DrawingBlock`.
- **SVG paths:** `SvgGeometry.Path` (Diagrams package, internal) parses the full SVG path grammar,
  arcs and quadratics included, into `MoveTo` / `LineTo` / `CubicTo` / `ClosePath`.
- **Where an icon could go:**
  - Cards: `DeckParser.Cards` takes a paragraph that is one `**strong**` run after the `###` as the
    card's figure (`Card.Figure`), drawn large in the accent colour by `DeckWriter.Cards`
    (`FigureSize`).
  - Lists: decks write bullets as `a:buChar`, tasks as Wingdings characters (E06-S02); PDFs draw
    task boxes as shapes in place of the marker (`LayoutEngine.Box`), which is the model for a drawn
    bullet.
  - Inline: DrawingML text cannot hold a picture or shape inside a run, so an icon in the middle
    of a deck's paragraph cannot be drawn. PDF lines are laid out by `LayoutEngine` from runs with
    `IFontMetrics`, and an inline object of one em is possible there.
- **`:name:`** has no meaning today: Markdig writes `:icon-check:` as text in both generators.
- No package carries a third-party notice yet; the PDF package packs `Fonts/OFL.txt`. E04-S05
  starts `THIRD-PARTY-NOTICES.txt`.
- Colours: `ResolvedBrand` has `Primary`, the diagram colours (`Text`, `Accent`, `Surface`, …) and,
  for dark slides, `DarkSlideBrand`.

## Proposed approach

1. **QR codes: syntax.**
   - An image whose URL starts with `qr:` is a QR code for the rest of the URL, taken literally
     (`qr:https://example.com/survey?id=3`, `qr:mailto:…`, `qr:WIFI:…`). The alt text is its
     description (`descr` in decks), and `caption` in the title (or `.caption` from E04-S05) shows it
     under the code, as for images. Image attributes other than `scale` and `.caption` are errors.
   - Content over 1 000 bytes throws: such a code is too dense to scan from a slide.
2. **QR codes: encoding and drawing.**
   - Encoding by **QRCoder** (MIT), error correction M, byte mode, the smallest version that holds
     the text (question 1). Only its module matrix is used, none of its renderers.
   - New internal `QrDrawing.Draw(text, ResolvedBrand)` in DocumentGeneration: a white square with
     the four-module quiet zone, then one rectangle per horizontal run of dark modules, merged into
     one path with `FillRule.NonZero`, so a deck gets two shapes, not hundreds.
   - **Colour:** the brand's `Primary` when its contrast to white is at least 4.5:1, else the
     diagram text colour; on dark slides it stays dark on its white square, because scanners need
     dark on light.
   - Size: 4 px per module, so it scales like any drawing; on slides at most a third of the
     content width unless `scale` says otherwise.
3. **Icons: the set.**
   - Tabler Icons (MIT), **outline** style, version pinned. A new single-file tool
     `tools/icon-import/icon-import.cs` (`run.sh icon-import` / `run.ps1 icon-import`) reads the
     icon SVGs from a Tabler release and writes one text resource: one line per icon,
     `name<TAB>path data`, absolute `M`/`L`/`C`/`Z` only, rounded to two decimals. Circles, lines,
     polylines and arcs are turned into paths there, so the runtime needs no SVG parser. The tool
     prints the count and size.
   - The resource is gzip-compressed and embedded in a new package (question 3),
     `wwosik.DocLibs.DocumentGeneration.Icons`, with Tabler's licence in its
     `THIRD-PARTY-NOTICES.txt`.
   - Public API: an `IIconSet` interface in DocumentGeneration (`Drawing? Find(string name)`,
     `IReadOnlyCollection<string> Names`), `TablerIcons.Outline` in the new package, and options
     `PptxOptions.Icons` and `PdfOptions.Icons` (`IIconSet?`, default `null`). Without an icon set
     `:icon-…:` stays text, so nothing changes for existing callers.
   - Each icon is a 24 × 24 drawing of stroked paths: `Stroke` in the colour of its place,
     `StrokeWidth` 2, `RoundJoins`, no fill, as Tabler draws them.
4. **Icons: syntax.** `:icon-<name>:`, the name in Tabler's kebab-case (`:icon-circle-check:`).
   Recognised in text runs outside code, by a shared helper like `ColouredWords`.
   - An unknown name throws `DocumentConversionException` with up to three close names (edit
     distance, then common prefix): "Unknown icon 'chek'. Did you mean check, checks, cheese?".
5. **Icons: where they go.**
   - **Card figure:** a card whose first paragraph is a single icon gets the icon as its figure,
     drawn at the figure size in the accent colour, in decks (`Card.Figure` becomes text or icon).
     PDFs show cards plainly; there, any paragraph that is a single icon is drawn as a block of its
     own, left-aligned at the figure size.
   - **Bullet:** a list item whose text starts with an icon gets the icon instead of its bullet.
     - PDFs draw it in the marker's place at the text size, in the accent colour, as task boxes are
       drawn.
     - Decks: a list in which every item starts with an icon becomes an icon list, a new flow block
       (`IconListBlock`): each item a row, the icon drawn left as a shape group at the first line's
       height and the text in its own text box. Like other layouts it makes the section a flow
       slide, so plain slides stay as they are. A list where only some items start with an icon keeps
       its bullets, and those icons are left out with a note (point 6).
   - **Inline (PDF):** a new `RunStyle.Icon` run measured as one em plus a thin space each side,
     drawn as shapes on the baseline at cap height in the text colour, never broken from the word
     after it.
   - **Inline (decks):** cannot be drawn (see Context); the icon is left out and reported (point 6).
6. **Notes.** New `NoteCodes.IconNotDrawn` (`icon-not-drawn`): an icon in a place the format cannot
   draw it (inline text on a slide, a table cell, a heading), left out of the text. In
   `NoteCodes.All` and the docs table.
7. **Dependencies and licences.**
   - QRCoder (MIT) in `Directory.Packages.props`, referenced by DocumentGeneration. The shared
     package is `IsAotCompatible`: a Native AOT publish is checked for warnings; if QRCoder warns,
     the plan falls back to an own encoder (question 1).
   - `THIRD-PARTY-NOTICES.txt` packed in DocumentGeneration (QRCoder) and in the Icons package
     (Tabler Icons, with the version).
   - The Icons package gets its docs page, a README package-table row, and a `.Tests` sibling.
8. **Guides, docs, changelog.**
   - The PPTX and PDF guides: QR codes; icons, where each format shows them, and a short list of
     about 40 common names with a pointer to Tabler's catalogue. The Icons package adds its own guide
     section (like `ICodeBlockRenderer.AuthoringGuide`) only when its set is passed. The expected
     guides are regenerated.
   - Docs: DocumentGeneration (QR, `IIconSet`, the note code), Pptx and Pdf (where they appear), the
     new Icons page (the set, its size, the import tool).
   - `CHANGELOG.md`: new: QR codes, the Icons package and options. Breaking: none for callers who
     pass no icon set; DocumentGeneration depends on QRCoder.
9. **Tests.** QR: the matrix for known texts against QRCoder's, the quiet zone, contrast fallback,
   the merged path; decks and PDFs with a QR code, read back as a drawing. Icons: the import tool on
   two sample SVGs (circle, arc), lookup and suggestions, each place in each format, `icon-not-drawn`,
   and text unchanged without an icon set. Open XML validator for every deck; PDF/A markers, and one
   veraPDF run recorded.

## Acceptance criteria

- [ ] `![Survey](qr:https://example.com/survey "caption")` draws a scannable QR code (checked with a
  phone from a slide and from a printed PDF page) with "Survey" under it, in the brand colour or its
  fallback.
- [ ] A QR code is two shapes in decks (the white square with its quiet zone, and one path of all
  modules) and two paths in PDFs, also on dark slides.
- [ ] With `Icons = TablerIcons.Outline`, `:icon-check:` draws the icon as a card figure in decks
  and PDFs, as bullets (an icon list in decks), and inline in PDF text.
- [ ] An inline icon on a slide is left out and gives `icon-not-drawn` with its line.
- [ ] `:icon-chek:` throws with suggestions; without an icon set, `:icon-check:` stays text.
- [ ] Every deck passes the Open XML validator and opens in PowerPoint without repair; PDFs stay
  PDF/A-2b.
- [ ] Both packages carry `THIRD-PARTY-NOTICES.txt`; the Icons package's size is recorded in its docs.
- [ ] Samples without the new syntax produce the same decks and PDFs as before.
- [ ] Guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. QR encoding: QRCoder (proposed: MIT, managed, well tested; we use only its matrix and draw it
   ourselves), or an own encoder of about 500 lines without a dependency, tested against QRCoder in
   the tests only?

>

2. How many icons: the whole Tabler outline set, about 5 000 (proposed: writers, often AI agents,
   know Tabler names, and a subset would make "unknown icon" common; the size is measured by the
   import tool and the set is cut to a curated list only if it exceeds 1 MB compressed), or a
   curated set of about 150–200 business icons as the fork has?

>

3. Where the icons live: a separate package `wwosik.DocLibs.DocumentGeneration.Icons`, passed in the
   options (proposed: no size cost for callers who don't want it, and `:icon-…:` stays text without
   it), or embedded in the shared DocumentGeneration package and always on?

>

4. Unknown icon names: throw with suggestions (proposed, as for unknown containers), or leave the
   text with a note?

>

5. Icon bullets in decks: an icon list as a flow block with our own layout (proposed: the icon and
   its text cannot drift apart), or a picture bullet (`a:buBlip`), which stays an editable list but
   needs a PNG per icon, and so SkiaSharp in the PPTX package?

>

6. QR colour: the brand's primary colour with a fallback to the diagram text colour below 4.5:1
   contrast (proposed), or always black on white for the safest scan?

>

7. QR caption: the alt text under the code when the title says `caption`, as for images
   (proposed), or the title itself as the caption text, as the fork writes it
   (`"Scan for the survey"`)?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
