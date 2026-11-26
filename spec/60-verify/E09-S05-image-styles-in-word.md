# E09-S05 Image styles in Word

> Status: in verification
> Epic: [E09 DOCX generator](../50-todo/E09-docx-generator.md)

## Summary

Image attributes take effect in Word documents as they do in decks and PDFs:
- `.round` crops a picture to a circle;
- `fit=cover` with `focus=` crops it to its frame;
- `.grey` turns it grey.

## Context

- `src/wwosik.DocLibs.DocumentGeneration/ImageAttributes.cs` parses
  `focus=top|bottom|left|right|center`, `fit=contain|cover`, `scale=`, `.round`, `.grey` and `.caption`.
- In Word, `DocxBody.cs` (around line 1270) gives `attribute-ignored` for `fit=cover`, `focus`,
  `.round` and `.grey`, because "a Word document shows the whole image as it is". Only `scale` and
  `.caption` work. Pictures are sized by `Fit` (`DocxBody.cs` around 1291).
- The PDF generator already draws all four:
  - `.round` is a clipping path;
  - `.grey` converts the pixels with SkiaSharp (`src/wwosik.DocLibs.DocumentGeneration.Pdf/GreyImages.cs`),
    loaded only when used.

  See `docs/wwosik.DocLibs.DocumentGeneration.Pdf.md`, "Image attributes".
- Image rows are a borderless table in Word, four images a row.

## Proposed approach

1. **`fit=cover` and `focus`.** Crop with `a:srcRect` on the picture's `blipFill`, computed from the
   image's size and its frame:
   - a single image: the frame `scale` gives;
   - in an image row: the row's common shape (the shortest image's aspect).

   `focus` picks the part that stays.
2. **`.round`.** Crop to a square by `focus` (`a:srcRect`), then draw with `a:prstGeom prst="ellipse"`.
   The pixels are untouched, so Word shows the full picture again if the shape is reset.
3. **`.grey`.** Reuse the PDF's conversion: move `GreyImages` to `wwosik.DocLibs.DocumentGeneration`
   (or the shared folder) so both generators use it. A picture used several times in grey is converted
   and embedded once. SkiaSharp is loaded only by documents with `.grey`, as in PDFs.
4. **Alt text, captions and SVG pictures** behave as now. For an SVG with `.grey`, the PNG fallback is
   made grey and the SVG is dropped, with a note.
5. **The authoring guide and docs** list the attributes as working in Word; the `attribute-ignored`
   texts for them go.

## Acceptance criteria

- [x] `![a](x.png){.round}`, `{fit=cover focus=top}` and `{.grey}` look in Word as they do in a PDF of
      the same Markdown.
- [ ] The documents open in Word without repair (an Office check, not done here) and pass the Open XML validator.
- [x] Documents without these attributes are unchanged byte for byte.
- [x] The changelog lists the change (the notes are gone, the pictures look different).

## Open questions

1. SVG images with `.grey`: grey PNG only (SVG dropped), or a grey filter on the SVG too?

> Grey PNG only; the SVG is dropped for a grey image. (2026-11-26)

2. Should Word's own picture recolouring (`a:grayscl`) be used instead of converting pixels? It needs
   no SkiaSharp, but PDFs and Word would then differ in how grey looks.

> No: pixels are converted with SkiaSharp, as in PDFs, so both formats look the same. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

Yes, for Word documents that use the attributes (the DOCX package was released in 2611.15.8):
`.round`, `.grey`, `fit=cover` and `focus` now change the pictures, and the `attribute-ignored` notes
for them are gone (except where a PDF gives them too, below). The DOCX authoring guide's image section
changes. The package now depends on SkiaSharp; on Linux `.grey` needs `SkiaSharp.NativeAssets.Linux`
(or `.NoDependencies`), as for PDFs. Documents without these attributes are unchanged. All of it is in
`CHANGELOG.md`.

### Decisions and deviations

- **Sizing follows the PDF exactly, not the proposal's "shortest image's aspect".** The acceptance
  criterion is that Word looks like the PDF, so `DocxBody.RowPictures` mirrors `LayoutEngine.RowSizes`:
  an image with `fit=cover` in a row fills its cell at the height of the tallest other image in the row
  (each fitted to its own cell and to 80 % of the text height, as before), or 3/4 of the cell's width
  when every image covers; a round one that covers is the largest circle in that box. An image of its
  own has no box, so `fit=cover` there does nothing and gives `attribute-ignored` with the PDF's wording;
  `focus` without `.round` or a cover-in-a-row, and `scale` with `fit=cover` in a row, give the PDF's
  notes too.
- **`.round`**: the natural size is the square of the shorter side (times `scale`), fitted as before;
  `a:srcRect` cuts the rest by `focus` (`ImageAttributes.Crop`, the same computation decks and PDFs use)
  and `a:prstGeom prst="ellipse"` draws it. The image part keeps every pixel.
- **`.grey`**: `GreyImages` moved from the PDF package to linked source in `src/Shared/Imaging/`,
  compiled into Pdf and Docx (the shared package must not take SkiaSharp: Charts and Diagrams depend on
  it). It now takes the target format for its exceptions, and encodes everything but a JPEG as PNG, so a
  grey GIF in Word becomes a PNG (PDFs only ever pass PNG or JPEG, so they are unchanged). The grey copy
  is made once per image (`greyImages`) and embedded once per part (`greyParts`: the body and the
  footnotes each have their own image parts, as for colour images). An image used in colour too is
  embedded in both.
- **SVG with `.grey`**: the converter's PNG is made grey; no SVG part is added for that picture, and
  the note is `attribute-ignored` ("keeps only the grey PNG of the SVG …"), so no new note code was
  needed. The same SVG in colour elsewhere keeps its SVG.
- The new code is in a partial file, `DocxBody.Images.cs`; `DocxBody.cs` only lost the old notes, split
  `Picture` into `ImageOf` (reading) and placing, moved the conversion of `ImagePart` into `Pixels`, and
  `Picture(…)` (the XML) gained optional `crop` and `round` parameters. Renderers' images
  (`ImageGraphic`) take no attributes and are unchanged.
- The Docx package has its own packed `THIRD-PARTY-NOTICES.txt` (SkiaSharp, Skia), like the PDF's.

### Verification

- `ImageStyleTests` (Docx tests): round crops and the ellipse, cover in a row (height, crop from the
  top), cover plus round in a row, the notes, grey pixels for PNG and JPEG with one part per image,
  alpha kept, grey in footnotes, grey SVG without its SVG, nothing cropped without the attributes; every
  document passes the Open XML validator. The E06-S05 test that expected two notes for `{.round .grey}`
  now expects none (renamed `…_AndTheOthersApply`).
- Unchanged output: `word/document.xml` of `layouts.md` and `pages.md` on all three templates from
  `origin/main` and this branch are identical once the SDK's random relationship ids are masked; the
  round-trip expectations of every sample are unchanged.
- PDF tests pass unchanged after the move of `GreyImages`.
- **Not checked here:** opening the documents in Word (no repair prompt, circle and crop look right).
  `.tmp/review/image-styles.docx` is written by `EveryAttribute_Together_IsValid` for that check.

