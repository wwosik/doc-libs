# E03-S04 Classification marking

> Status: in progress (doc-3)
> Epic: [E03 PDF layout](../../50-todo/E03-pdf-layout.md)

## Summary

A document can be marked *internal* or *confidential*, and as a *draft*, through two front-matter
keys or the host's options. PDFs show the marking in the header of every page and on the cover,
and a draft gets a diagonal watermark. Decks show it in a corner of every slide. The keys and
their meaning are shared, so the DOCX generator (E09) marks Word documents from the same source.

## Context

- Split from the epic's row E03-S02 (see
  [E03-S02](E03-S02-contents-heading-numbers-and-captions.md), question 1). The marking concerns
  the page furniture of every format, not the flow of text.
- **The fork**, made for one client, has a classification marking (confidential / internal /
  draft): in the Word header, on the cover, and as a draft watermark, with the same key for decks
  and PDFs (analysis, DOCX table). Its values and texts were German and client-specific. Only the
  mechanism comes here.
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
   can be confidential too)? Or one key with `draft` as a fourth value, as in the fork? E09-S02
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
