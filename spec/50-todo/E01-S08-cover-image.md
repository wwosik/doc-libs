# E01-S08 Cover image

> Status: todo
> Epic: [E01 Brands and templates](E01-brands-and-templates.md)

## Summary

The front matter can put an image on the cover of decks, HTML decks, PDFs and Word documents, for
example a product mark or a partner's logo. `coverImage` names the image and `coverImageSize` sets its
size.

## Context

- **Covers today:**
  - Decks: the title slide (`src/wwosik.DocLibs.DocumentGeneration.Pptx/DeckParser.cs`, `DeckWriter.cs`,
    `TemplateBuilder.cs`), with the brand's cover artwork, logo and tagline.
  - HTML decks: drawn from the generated `.pptx` (`HtmlDeck.cs`), so they follow the deck.
  - PDFs: the cover page (`src/wwosik.DocLibs.DocumentGeneration.Pdf/LayoutEngine.cs` from line 57 and
    around 180).
  - Word documents: the cover (`src/wwosik.DocLibs.DocumentGeneration.Docx/DocxContent.cs` `DocxCover`,
    `DocxPage.cs`, `DocxWriter.cs`).
- **Images** come only from the document's resources (`MarkdownDocument`), never from disk.
- **Front matter** (`src/wwosik.DocLibs.DocumentGeneration/FrontMatter.cs`) rejects unknown keys, so
  `coverImage` is an error today.

## Proposed approach

1. **The keys.** Two front-matter keys shared by every generator:
   - `coverImage: mark.png`, a resource path;
   - `coverImageSize: small | medium | large`, giving 1/10, 1/6 (default) or 1/4 of the cover's height;
   - `coverImagePosition: below | right`: under the subtitle (default) or right of the title block.
   A brand can set a default (`Brand.CoverImage`, `coverImage` in `brand.json`), which the front
   matter overrides.
2. **The image.** It is PNG, JPEG or SVG, as other images are. SVG goes through the image converters,
   so it is a vector in PDFs and decks, and a PNG with its SVG in Word.
3. **Placement.** With `below`, the image sits under the subtitle; with `right`, it stands right of
   the title block, top-aligned with the title. Below, it is left-aligned with the title, and keeps its aspect
   ratio. It never covers the logo, the artwork or the tagline: when space runs short it is made
   smaller, never moved over them.
   - Decks: a picture on the title slide. On a caller's template, it goes into a picture placeholder of
     the title layout when the template roles name one.
   - PDFs: on the cover page.
   - Word: on the cover, inline under the subtitle.
4. **Errors and notes.**
   - A resource that is missing throws `DocumentConversionException` naming the key and the path.
   - A document without a cover (a plain document, a deck without a title slide) gets the note
     `cover-image-ignored`.
5. **Docs.** The authoring guides and the front-matter tables list both keys.

## Acceptance criteria

- [ ] `coverImage` shows the image on the cover in all four outputs, at the three sizes.
- [ ] A missing resource throws with the key's name; no cover gives `cover-image-ignored`.
- [ ] Documents without the keys are unchanged.
- [ ] Docs, guides and changelog updated.

## Open questions

1. Should a brand be able to set a default cover image (`Brand.CoverImage`), with the front matter
   overriding it?

> Yes: `Brand.CoverImage`, overridden by the front matter. (2026-11-26)

2. Placement: always under the subtitle, or a `coverImagePosition` (for example `right`)?

> Yes: `coverImagePosition` (`below`, the default, or `right`). (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
