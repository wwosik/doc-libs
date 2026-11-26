# E03-S04 Classification marking

> Status: in verification
> Epic: [E03 PDF layout](../50-todo/E03-pdf-layout.md)

## Summary

A document can be marked *internal* or *confidential*, and as a *draft*, through two front-matter
keys or the host's options. PDFs show the marking in the header of every page and on the cover,
and a draft gets a diagonal watermark. Decks show it in a corner of every slide. The keys and
their meaning are shared, so the DOCX generator (E09) marks Word documents from the same source.

## Context

- Split from the epic's row E03-S02 (see
  [E03-S02](E03-S02-contents-heading-numbers-and-captions.md), question 1). The marking concerns
  the page furniture of every format, not the flow of text.
- **The feature plan** lists a classification marking (confidential / internal / draft): in the
  Word header, on the cover, and as a draft watermark, with the same key for decks and PDFs
  (analysis, DOCX table). Values and texts come from options and generated texts.
- **The DOCX plan (E09)** runs in parallel and assumes a front-matter key `classification:`. This
  story defines it.
- **PDF furniture.** `LayoutEngine.Decorate` (`LayoutEngine.cs:892`) writes the header and footer
  of every page but the cover:
  - the header has the title on the left at y = 38 and the logo right-aligned at y = 24;
  - the footer has the footer text, the date and `n / N`.

  The cover (`LayoutEngine.Cover`) has the title at 28 % of the height, the logo top right, the
  artwork bottom right and the tagline. Page items are `TextItem`, `LineItem`, `RectItem`,
  `ImageItem`, `ShapeItem` and `LinkItem` (`PageModel.cs`). None of them is rotated, and the
  renderer writes text through PdfPig's `AddText` (`PdfRenderer.Write`).
- **Deck furniture.**
  - The built-in master (`TemplateBuilder`) has a footer line with the slide number, the footer
    text and the date, and the logo bottom right. Its cover has the logo top right.
  - A brand's own template and caller templates have whatever their masters draw.
  - `DeckWriter` writes the slides; `PptxGenerator.WriteCoreProperties` writes the title, the
    author and `dc:language`.
- **Front matter** (`FrontMatter.Apply`) skips the shared keys `title`, `author`, `lang` and
  `labels.*` and hands the rest to the format. E03-S02 adds `FrontMatter.DocumentKeys`, which
  decks accept and ignore. The marking keys are shared like `lang`, and every format acts on
  them.
- **Texts.** `GeneratedTexts` holds the generators' own texts per language (E06-S02).

## Proposed approach

1. **Shared keys and options** (DocumentGeneration):
   - **`classification:`** takes `public`, `internal` or `confidential`. `public`, the default,
     shows nothing.
   - **`draft: true|false`** is a separate key, because a draft can also be confidential
     (question 1). Default `false`.
   - **Types.** A public enum `Classification { Public, Internal, Confidential }`. `PdfOptions`
     and `PptxOptions` get `Classification` and `Draft` for hosts.
   - **Precedence** (question 2). Front matter can raise the host's classification but not lower
     it. A host that sets `Confidential` stays confidential when the Markdown says `internal`, and
     the generator adds a note `classification-kept`. `draft` from front matter only adds; a
     host's `Draft = true` stays.
   - **Errors.** An unknown value throws `DocumentConversionException`, listing the valid values,
     like other front-matter errors.
   - **Shared code.** A shared internal `Marking` (classification, draft) is resolved once per
     `Generate` (`Marking.Resolve(frontMatter, options, notes)`). Its `Text(texts)` joins the
     parts with " · ", for example "Confidential · Draft".
2. **Generated texts.** `GeneratedTexts` gains:
   - `Internal`: "Internal" / "Intern";
   - `Confidential`: "Confidential" / "Vertraulich";
   - `Draft`: "Draft" / "Entwurf".

   A host with its own scheme ("Strictly confidential", "Restricted") changes the texts through
   `Texts`; the levels stay three (question 3).
3. **PDF:**
   - **Header.** On every page but the cover, the marking sits right-aligned in the header row,
     left of the logo, in bold at the footer size (8 pt). The header title's `Fit` width shrinks
     by the marking's width.
   - **Cover.** The marking sits above the title, at the title's left margin, in bold at 10 pt.
   - **Colour** (question 4): `Labels.Danger` for confidential, the footer colour for internal
     and draft.
   - **Watermark.** A draft gets the draft text in capitals (upper-cased in the document's
     culture), drawn diagonally across the centre of every page, cover included.
     - It is set at 45°, sized to 70 % of the page diagonal, in the regular face.
     - It is a very light tint of the text colour, opaque, drawn first so that all content lies
       over it. No transparency is used, which keeps PDF/A-2b simple.
   - **Rotated text.** `TextItem` gets an `Angle`.
     - The renderer writes a rotated text item itself, with a `Tm` text matrix in the page's
       operations, using the font resource PdfPig registered for that face. Rotated drawing text
       may need the same later.
     - That PdfPig allows this without breaking the font subset or text extraction is checked in
       a spike first. If it does not, the watermark is set horizontally across the middle of
       the page instead, and this is recorded in the implementation notes.
   - **Metadata.** The XMP and the info dictionary are unchanged: PDF/A has no standard field for
     a classification, and a custom XMP schema is not worth it.
4. **Decks:**
   - **Corner label.** Every slide gets a small text box top right, closing slide and cover
     included: the marking in bold at the footer size, in the palette's footer colour. Dark
     slides use their white text (`FlowStyle.Dark`, `SlidePalette.ForDark`).
   - **Position.** The box hangs from the top edge, inside the slide's right margin.
     - On the built-in master's cover, it sits below the logo box (`TemplateBuilder`'s cover
       logo), so the two do not overlap.
     - On other templates it uses the corner, which templates rarely fill.
   - **Watermark** (question 5). Decks get none: a draft adds "Draft" to the corner label, as
     above.
   - **Core properties.** `cp:contentStatus` is set to the marking text, so Explorer and Office
     show it. Decks without a marking keep writing none, so their output is unchanged.
   - **Shape.** The box is a plain text box (`p:sp` with `txBox="1"`), named
     `Classification`, written after the slide's other shapes so it is on top. Decks tag it with
     the document language like every run.
5. **DOCX (E09)** reads the same keys, options and texts. Word puts the marking in the header and
   on the cover, and a draft gets a Word watermark. This story only defines the shared parts.
6. **Converters.** Nothing to do here:
   - the PDF converter drops the header marking as a running header, on documents of three pages
     or more;
   - E08-S01 drops rotated text, and so the watermark;
   - the PPTX converter writes the corner label as text, since it is not a placeholder. E08-S01
     may skip a shape named `Classification` (see its notes).
7. **Notes.** One new code, with advice and a docs row:
   - `classification-kept` (both formats): front matter asked for a lower classification than the
     host set, and the host's applies.
8. **Guides, docs, changelog.**
   - **Guides.** Both guides list `classification` and `draft` in the front matter table, with
     the host's default, and say in a sentence what the reader sees. The expected guides are
     regenerated.
   - **Docs.** `docs/wwosik.DocLibs.DocumentGeneration.md` gets a *Classification* section: keys,
     options, precedence, texts. The Pdf and Pptx pages describe the placement, and the notes
     table gets the new code.
   - **CHANGELOG.** Under new: the keys, options and texts. Nothing changes for documents
     without a marking, so there is no breaking change, unless question 2 is answered otherwise.
   - `ai/memory.md`: precedence, and the rotated text item.
9. **Tests.**
   - **Layout**: the header marking on every page but the cover, the title fitted beside it,
     the cover marking, and the watermark on every page as the first item.
   - **Rendering**: text extraction (PdfPig) finds the marking and the rotated watermark.
     PDF/A-2b passes the structural checks, and veraPDF is run by hand on a confidential draft.
   - **Decks**: the corner box on every slide, cover and dark slides included, on the built-in
     master, the sample brand and a caller template. `cp:contentStatus` is set. The Open XML
     validator passes. A deck without a marking is unchanged.
   - **Front matter and options**: precedence with `classification-kept`, `draft` from either
     side, unknown values, `lang: de` texts, and host texts.
   - **Office**: a confidential draft deck is opened in PowerPoint and the corner label checked on
     the built-in master and the dark template, by hand on Windows (`.tmp/scripts/export.ps1`).

## Acceptance criteria

- [ ] `classification: confidential` puts "Confidential" in the header of every PDF page but the
  cover, above the title on the cover, and in the top right corner of every slide.
- [ ] `draft: true` adds "Draft" to the marking, and in PDFs a diagonal watermark under the
  content of every page.
- [ ] `lang: de` gives "Vertraulich", "Intern" and "Entwurf", and host texts replace them.
- [ ] Front matter cannot lower the host's classification; trying gives `classification-kept`.
  Unknown values throw `DocumentConversionException`.
- [ ] Decks carry the marking in `cp:contentStatus` and pass the Open XML validator; PDFs stay
  PDF/A-2b, and the watermark is extractable text.
- [ ] Documents and decks without a marking are unchanged.
- [ ] Guides, expected guides, docs, CHANGELOG and memory are updated, and all tests pass.

## Open questions

1. Two keys, `classification: public|internal|confidential` and `draft: true` (proposed: a draft
   can be confidential too)? Or one key with `draft` as a fourth value? E09-S02
   currently assumes the single key `classification: confidential | internal | draft`, and
   follows whichever is chosen here.

> Answered by the human (2026-11-14): as proposed.

2. Can front matter lower the classification a host set? No: the higher one wins, with a note
   (proposed: a host that marks everything confidential should not be overruled by a writer).
   Or does front matter always win, as with every other key?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Three fixed levels, renamed through `GeneratedTexts` (proposed: simple, and enough for
   public/internal/confidential schemes). Or free text (`classification: Restricted – Finance`)
   shown as written?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Colour of the marking: `Labels.Danger` for confidential and the footer colour otherwise
   (proposed). Or the footer colour for all, so it never clashes with a brand? E09-S02 proposes
   `Labels.Warning` for internal and draft; all formats should use the same rule.

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. A draft watermark on slides too (a large diagonal "DRAFT" behind the content)? Or only the
   corner label (proposed: a watermark over slides hampers presenting, and decks are rarely
   printed)?

> Answered by the human (2026-11-14): as proposed.

6. Decks: the corner label on every slide, cover and closing included (proposed)? Or on the
   cover and in the footer line only, which caller templates may not have?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Should the marking also go into the file's metadata: `cp:contentStatus` in decks and DOCX
   (proposed), and nothing in PDFs? Or nowhere, since some organisations use their own
   sensitivity labels? E09-S02 proposes a custom document property `Classification` instead;
   decks and Word should agree.

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

None for released output: documents and decks without a marking are unchanged (verified below), and
the keys, options and texts were added unreleased by E09-S02. What a consumer can notice, all in
`CHANGELOG.md`:

- Markdown with `classification:` or `draft:`, which PDFs and decks accepted and ignored since E09-S02
  (unreleased), now marks PDFs and decks.
- Added: `PdfOptions.Classification`/`Draft`, `PptxOptions.Classification`/`Draft`;
  `classification-kept` is now listed for PPTX and PDF too, and its advice text (used by the Word
  guide) is shorter: "front matter cannot lower the host's `classification`, which applies. Leave the
  key out."

### Decisions and deviations

- **Spike: rotated text works.** `TextItem.Angle`; `PdfRenderer.Rotated` writes `q`, a `cm` rotation
  matrix about the text's start, then PdfPig's ordinary `AddText` at the origin, then `Q`. The font
  subset gets the characters, PdfPig extraction returns the watermark as letters with a non-horizontal
  `TextOrientation` (45°), and the PDF converter leaves them out (E08-S01). The horizontal fallback was
  not needed. No transparency is used (tested: no `/SMask`, `/CA`, `/ca`).
- **PDF placement.** Header: the marking right-aligned at the title's baseline (38), 8 pt bold, 12 pt
  left of the logo or at the right margin; the title's `Fit` width shrinks by the marking plus 12 pt
  (without a marking the old formula, so old PDFs stay byte-identical). Cover: 10 pt bold at the
  title's left margin, baseline 4 pt above the top of the title's first line (the title does not move).
  Watermark: inserted at index 0 of every page's items in `Decorate`, after the layout, so the empty-page
  checks during layout are not affected; length 70 % of the page diagonal, centred (the baseline is
  shifted by half the cap height so the letters' middle is the page centre), the text colour at 15 %
  on white (`ResolvedBrand.Mix`, close to Word's `#D9D9D9`), regular face.
- **Deck placement.** The text box's right edge is the right edge of the content layout's title (the
  slide's right margin on every template), 0.1 in from the top; on the built-in cover below the logo
  box (`TemplateBuilder.CoverLogoBottom` + half a gap). Size: the built-in footer size (10.67 pt) on
  every template, bold, `wrap="none"`, right-aligned, no autofit, insets 0; its width estimated from
  the text length. Written after the footer, before `Tag`, so it is last in the tree and language-tagged.
- **Colours on dark slides** (the plan says "white text"): internal and draft white; confidential the
  danger colour lightened halfway to white (`ResolvedBrand.Mix(danger, 0.5)`, as coloured words on dark
  slides), so confidential stays red-ish but readable. A slide counts as dark when the palette is the
  dark-template palette, the slide is a `{.dark}` flow slide, or `TemplateTone.IsDark(layout)` (new):
  the layout's own title placeholder has light text, else the layout's own background is dark (solid or
  the average of a gradient's stops). That catches the built-in section, closing (= section) and
  statement layouts. New `SlidePalette.Footer` (brand footer colour; white on dark templates; text 1 at
  65 % on caller templates).
- **`cp:contentStatus`** is written only when there is a marking (`CoreProperties.ContentStatus`), so
  unmarked decks keep their core part byte-for-byte.
- **Guides (word limits).** The PDF guide was at 2,487 of 2,500 words and the PPTX guide at 3,473 of
  3,500. Both get the two front-matter rows (shared `AuthoringGuides.MarkingKeys`, with the host's
  defaults: "lower values have no effect" when the host sets a level, "Always `true` here" when the host
  sets a draft); the sentences saying the keys are ignored are gone. To fit, **`classification-kept` is
  left out of the PDF and PPTX guides' notes lists** (the row already says when lower values have no
  effect), its shared advice was shortened (Word guide), and the PDF guide lost "Fix the Markdown and
  generate again." Result: PDF 2,495 (sample brand) and PPTX 3,497 (caller template) words; **both are
  now practically full** — the human should decide whether to raise the limits or trim prose before
  E03-S05 adds to the PDF guide. The limits were not raised.
- **Shared code.** `Marking.Watermark(texts, language)` (culture upper-casing with an invariant fallback)
  replaces `DocxMarking.Culture`, so Word and PDF upper-case alike.
- **Converters** (plan step 6, nothing to do): the PDF converter drops the header marking as a running
  header on documents of three pages or more and the watermark always; the PPTX converter writes the
  corner label as text. E08-S01's optional "skip a shape named `Classification`" is **not done** (not in
  this plan): a converted marked deck has "Confidential · Draft" on every slide. Loose end for E08.

### Verification

- `./run.sh build`: 0 warnings, 0 errors. `./run.sh test` all green: 1 + 123 + 78 + 388 + 71 + 154 +
  35 + 281 + 474 = 1,605 passed, 6 Windows-only skipped.
- **New tests.** `Pdf.Tests/ClassificationMarkingTests.cs` (15): header marking on every page but the
  cover, left of the logo, colour, bold 8 pt; at the right margin without a logo; header title fitted
  beside it; cover marking above the title at its margin; the watermark the first item of every page,
  45°, 70 % of the diagonal, centred; nothing added without a marking; rendering: the marking and the
  rotated "DRAFT" extracted by PdfPig on every page (45°, middle near the centre), PDF/A-2b markers, all
  fonts embedded and no transparency; the watermark left out of `PdfConverter`'s Markdown; `lang: de`
  ("Vertraulich · Entwurf", "ENTWURF", "Intern"); host texts; host options with `classification-kept`
  and its line; front matter raising the host's level; unknown values; `public`/`false` identical to no
  keys. `Pptx.Tests/ClassificationMarkingTests.cs` (10): the label on every slide (cover, section, dark,
  table, closing) on the neutral and sample built-in masters, a caller template and the dark brand
  template, last in the tree, text box, bold, 10.67 pt, right-aligned, language-tagged, top right,
  validator clean, `cp:contentStatus`; below the logo on the built-in cover; colours (danger, footer,
  white and lightened danger on dark slides); German, `Intern`, host texts; host options, note and
  raising; unknown values; no label and no status without a marking. `Svg.Tests/DeckPreviewTests`: the
  label is painted in the top right corner of every slide (`.tmp/review/preview-classification.png`).
  The former "accepted and ignored" tests now check that the marking changes nothing but the
  furniture (PDF text without the marking and watermark, slide XML without the `Classification` box).
- **Unchanged output.** All 193 files the tests write to `.tmp/review/` (18 PDFs, 67 decks, 21 Word
  documents, HTML decks and PNG previews), from `a28da70` before any change and from this change:
  identical — PDFs byte for byte with `/ID` and dates masked, decks and Word documents part by part
  with relationship ids masked, embedded workbooks included.
- **Open XML validator** passes on every marked deck the tests write.
- **For review** in `.tmp/review/`: `classification-confidential-draft.pdf` (neutral, English),
  `classification-internal-sample.pdf` (sample brand, German), `classification-neutral.pptx`,
  `classification-sample.pptx`, `classification-custom.pptx`, `classification-dark.pptx`,
  `preview-classification.png`.

### To verify by hand (no veraPDF, PDF viewer, PowerPoint or Windows here)

1. **veraPDF** (PDF/A-2b profile) on `classification-confidential-draft.pdf` and
   `classification-internal-sample.pdf`: not available on this machine.
2. **The PDFs in a viewer:** the marking right in the header beside the logo, red for confidential;
   above the cover title; "DRAFT" light grey, diagonal, centred, under the text on every page including
   the cover; text selection and search still work; nothing was rendered visually here (no
   rasteriser), only checked by extraction.
3. **PowerPoint** (`.tmp/scripts/export.ps1`): open `classification-*.pptx`, no repair prompt; the label
   top right on every slide, below the logo on the built-in cover, readable on the section, dark and
   closing slides and on `classification-dark.pptx`; *File → Info* shows the status.

### Loose ends

- `PptxConverter` could skip a shape named `Classification` (E08-S01 step 8 note); not done.
- The PDF and PPTX guides are at their word limits (see above).
