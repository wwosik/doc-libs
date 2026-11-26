# E09-S05 Image styles in Word

> Status: todo
> Epic: [E09 DOCX generator](E09-docx-generator.md)

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
   made grey and the SVG is dropped, with a note, unless question 1 decides otherwise.
5. **The authoring guide and docs** list the attributes as working in Word; the `attribute-ignored`
   texts for them go.

## Acceptance criteria

- [ ] `![a](x.png){.round}`, `{fit=cover focus=top}` and `{.grey}` look in Word as they do in a PDF of
      the same Markdown.
- [ ] The documents open in Word without repair and pass the Open XML validator.
- [ ] Documents without these attributes are unchanged byte for byte.
- [ ] The changelog lists the change (the notes are gone, the pictures look different).

## Open questions

1. SVG images with `.grey`: grey PNG only (SVG dropped), or a grey filter on the SVG too?

>

2. Should Word's own picture recolouring (`a:grayscl`) be used instead of converting pixels? It needs
   no SkiaSharp, but PDFs and Word would then differ in how grey looks.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
