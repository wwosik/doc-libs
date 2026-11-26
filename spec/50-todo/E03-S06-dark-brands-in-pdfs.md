# E03-S06 Dark brands in PDFs

> Status: todo
> Epic: [E03 PDF layout](E03-pdf-layout.md)

## Summary

A brand whose design is dark gives a dark PDF: every page is painted in the dark background, with
light text, and the slide colours and the logo for dark backgrounds are used. A draft's watermark stays
valid PDF/A.

## Context

- **Brands.** `Brand.SlideColors` is a second `BrandColors` for decks
  (`docs/wwosik.DocLibs.DocumentGeneration.md`, "Slide colours"), and `Brand.LogoOnDark` is a logo for
  dark backgrounds (`src/wwosik.DocLibs.DocumentGeneration/Brand.cs`). `SlideTone` (`Brand.cs` around
  430) says whether slides are dark. `ResolvedBrand.cs` resolves dark brands for charts and diagrams.
  The docs say **PDFs never read them**.
- **The PDF generator** draws on white pages
  (`src/wwosik.DocLibs.DocumentGeneration.Pdf/LayoutEngine.cs`, `PdfRenderer.cs`) with
  `PdfAStandard.A2B`. A draft gets a diagonal watermark under the content (`LayoutEngine.cs` line 34,
  `PdfGenerator.cs` around 123). Diagrams and charts already know a dark variant for decks.

## Proposed approach

1. **The switch.** `BrandPdfTemplateOptions.Tone` (`Light` by default, `Dark`). It also follows the
   brand when the brand is dark throughout (question 1).
2. **Painting.** Every page, the cover included, starts with a rectangle filling the page in the dark
   background colour. Text, rules, table lines and footnote rules then use the slide colours. Code
   boxes, alert boxes and table bands use the dark variants that decks use. The logo is `LogoOnDark`.
3. **Graphics.** Mermaid diagrams, charts and drawings are resolved for a dark background, as on dark
   slides. Pictures are left as they are.
4. **Watermark.** The draft watermark is drawn in a colour mixed between the background and the text,
   opaque. There is no transparency, so PDF/A-2b stays valid without a transparency group.
5. **Printing.** The docs say that a dark PDF prints dark, and a host that wants a printable copy
   generates with `Tone = Light`.
6. **Tests.** A sample with a dark brand passes veraPDF (where available) and the PdfPig checks of the
   existing PDF/A tests. Light documents are unchanged byte for byte.

## Acceptance criteria

- [ ] With a dark tone, every page has the dark background and light text, and its links, footnotes,
      tables, code, alerts, diagrams and charts are legible (4.5:1 for text).
- [ ] A dark draft is valid PDF/A-2b.
- [ ] Light PDFs are unchanged.
- [ ] Docs, authoring guide and changelog describe the option.

## Open questions

1. Should a dark brand give a dark PDF by default, or only when the host sets `Tone = Dark`?

>

2. A front-matter key (`tone: dark`) as well, or host options only?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
