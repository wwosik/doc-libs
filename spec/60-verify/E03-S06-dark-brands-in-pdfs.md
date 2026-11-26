# E03-S06 Dark brands in PDFs

> Status: in verification
> Epic: [E03 PDF layout](../50-todo/E03-pdf-layout.md)

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

1. **The switch.** `BrandPdfTemplateOptions.Tone` (`Light` by default, `Dark`). The front-matter key
   `tone: dark` sets it too. A dark brand alone never makes a dark PDF, so existing PDFs stay as
   they are.
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

> Only on request: a dark brand gives a dark PDF only when the host sets `Tone = Dark` or the front matter says `tone: dark`. Existing PDFs do not change. (2026-11-26)

2. A front-matter key (`tone: dark`) as well, or host options only?

> Both: the host option and the front-matter key `tone: dark`. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- **Switch.** `BrandPdfTemplateOptions.Tone` (`PdfTone.Light` by default, `PdfTone.Dark`) and the template key
  `tone: light|dark`. A dark brand alone never gives a dark PDF. `template:` naming another brand keeps the tone
  (only the tone: `ShowDate`/`ShowTagline` still reset as before). `PdfTone` is a new enum rather than `SlideTone`,
  which has an `Auto` that means nothing for PDFs.
- **Colours** (`DarkPages`, PDF package): the background is the slide brand's primary (`ResolvedBrand.ForSlides`),
  darkened until white text reaches 7:1 (the sample brand's teal is darkened; neutral is kept). Text, headings,
  the cover title, icons, rules and task boxes are white, as on dark slides. Links, header/footer, quotes and alerts,
  status words (the dark slides' "halfway to white", then corrected) and a confidential marking are lightened to
  4.5:1 against the code box, the lightest surface text stands on. Decks' translucent veils are made opaque over
  the background: table header 40 % black, bands 8 % white, grid 35 % white, code box 10 % white; highlighted code
  is corrected on the code box (`ResolvedCodeColors.On(…, dark: true)`). Chosen where the story leaves the exact
  colours open.
- **Logo:** `LogoOnDark`, else `Logo` (a conventional fallback; a logo made for white may then be hard to see).
- **Graphics:** the parser gets `ResolvedBrand.DarkSlideBrand`, so Mermaid diagrams, charts, scenes, visual containers
  and QR codes are resolved as on dark slides. Pictures, cover artwork and tagline are unchanged.
- **Painting:** `Design.Background` adds a page-filling `RectItem` as the first item of every page in `Decorate`,
  under the watermark. The watermark is `DarkPages.Over(text, 0.15, background)`, opaque. `Design.QuoteColor` and
  `Design.CodeBackground` replace the constants they were, with the old values as defaults.
- **Light PDFs unchanged:** checked byte for byte (apart from the random `/ID`) for `report`, `long`, `invoice` and
  a document with every element, neutral and sample brand, with and without a confidential draft, before and after
  the change. `ToneIsLightByDefault_AndALightPdfIgnoresTheSlideColours` keeps the slide colours out of light PDFs.
- **Tests:** `DarkPagesTests` — every letter of a dark PDF (both brands, diagrams and highlighted code included)
  reaches 4.5:1 on the code box; every page, cover included, starts with the background, then the watermark; the
  dark logo on every page; the background comes from the slide colours; a dark draft has the PDF/A-2b markers and
  no `/SMask`, `/CA`, `/ca` or `/Group`; front matter sets and undoes the tone, a template switch keeps it, a wrong
  value throws. **veraPDF is not available** in this environment (nor in the repo's tests): validation is the same
  PdfPig and raw-content checks as the existing PDF/A tests. No page was looked at rendered (no rasteriser here):
  the review files are `.tmp/review/dark-neutral.pdf`, `dark-sample.pdf`, `dark-sample-draft.pdf`.
- **Docs:** PDF docs (new section "Dark PDFs", options, front matter, watermark), the shared docs (`SlideColors`,
  `LogoOnDark`), the `Brand` XML docs, the PDF authoring guide (a `tone` row and one sentence) and the changelog.
- **Loose end:** decks and Word documents do not know the key `tone`, so a Markdown with `tone: dark` fails there
  as an unknown key. Accepting it in other formats would touch the shared front-matter code; left for a later story.
- **Breaking changes:** none for PDFs or the API. The PDF authoring guide (and so the tool guide) gains a `tone` row
  and a sentence, recorded under breaking changes in the changelog as earlier guide changes were.
