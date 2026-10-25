# 00006 Brand fonts embedded in decks

> Status: verify — implemented 2026-10-23

## Summary

Decks embed a brand's fonts by default, the way PowerPoint does itself, so a deck shows the
brand's typeface on a machine that does not have it installed. PDFs already embed brand fonts;
decks today only name the family and PowerPoint substitutes. Along the way, decks set code in the
brand's mono family when it has one, and family names are read the way PowerPoint looks fonts up.

## Context

- A brand's `BrandFonts` has four text faces plus optional mono and table faces. PDFs embed
  subsets; decks name `Family` in the theme and `TableFamily` in table cells and embed nothing
  ([docs](../../docs/wwosik.DocLibs.DocumentGeneration.md#fonts)).
- `TrueTypeInfo.FamilyName` prefers the typographic family name (name ID 16) over the family name
  (name ID 1). PowerPoint and Windows group the four faces under **name ID 1**, and an embedded
  font is found only if the deck names exactly that. For a condensed or light cut the two differ
  (e.g. `… Sans Cond` vs `… Sans Condensed`).
- `PptxOptions.CodeFont` is a `string` defaulting to `Consolas`, whatever the brand.
- The library does not read a font's embedding permissions (`OS/2 fsType`); the docs make
  licensing the caller's responsibility.
- The sample brand `sample` has Liberation Serif (text) and PT Sans Narrow (tables), all
  `fsType 0` (installable), and no mono face.

## Proposed approach

1. **EOT writer** — an internal `FontEmbedding` in the PPTX package. Each face becomes an
   uncompressed Embedded OpenType (version `0x00020001`, header filled from the font's `OS/2`,
   `head` and `name` tables, followed by the TTF) in a font part, listed in `p:embeddedFontLst`,
   with `embedTrueTypeFonts="1"`. Whole fonts, not subsets, and no EOT subset flag, so editing the
   deck with any character keeps working. `TrueTypeInfo` gains `Table`, `Name` and
   `EmbeddingFlags`, so there is one font-table reader.
2. **What is embedded:** the text family always (the theme names it); the table and mono families
   only when a slide's `a:latin` names them. Faces that fall back to another (a table italic to the
   table regular, `MonoBold` to `Mono`) are not listed as faces of their own; each file is
   embedded once. A brand's own `Pptx` template keeps its existing `p:embeddedFontLst` entries,
   except those of the same family, which are replaced. `CustomTemplateOptions` (no brand) never
   embeds.
3. **The switch:** `BrandTemplateOptions.EmbedFonts` (`bool`, default `true`) and the front matter
   key `embedFonts: true|false` — for brands on the built-in master and for brands with their own
   `Pptx` template (which today takes no keys at all). `false` names the families as today.
4. **Licensing:** before embedding into a deck, read `OS/2 fsType`. Restricted (`fsType & 0x000E
   == 0x0002`): the face is not embedded, and its whole family is skipped if it is the regular
   face — no exception, the deck still works. Preview & print (`0x0004`), editable (`0x0008`) and
   installable (`0`): embedded. PDFs stay as they are.
5. **Family names from name ID 1**, else 16, for every brand (`BrandFonts.Family` and
   `BrandTableFonts.Family` still override). For the sample brand both names are the same.
6. **Code font:** `PptxOptions.CodeFont` becomes `string?`, default `null`: the caller's font,
   else the brand's mono family (new `ResolvedFonts.MonoFamily`, read from the mono face), else
   Consolas. Callers that set `CodeFont` see no change.
7. **Tests:** a new `FontEmbeddingTests`:
   - the sample deck has the text and table families' font parts, `p:embeddedFontLst` names them,
     and the EOT headers' sizes, weights and italic bits match the faces;
   - `EmbedFonts = false` and `embedFonts: false` embed nothing; `neutral` embeds nothing;
   - each file is embedded once; the table family only when a table is on a slide;
   - a face patched to `fsType 0x0002` is skipped;
   - a brand with a mono face sets code in it and embeds it;
   - the embedded decks pass the Open XML validator;
   - the deck-size test covers both cases.
8. **Look at it:** open an embedded sample deck in PowerPoint for Windows on a machine without
   PT Sans Narrow and Liberation Serif; *File → Options → Save* lists them as embedded.
9. **Docs:** *Fonts* in `wwosik.DocLibs.DocumentGeneration.md` (decks embed by default, the
   `fsType` rule), options and front matter in `.Pptx.md`, *Known limits* (Google Slides ignores
   embedded fonts; LibreOffice needs libeot; PowerPoint Online only partly), `ai/memory.md`
   (the EOT format, name ID 1).

## Acceptance criteria

- [ ] Decks of a brand with fonts embed every family they use, whole faces, each file once, by
      default; on the built-in master and on a brand's own template.
- [ ] `EmbedFonts = false` and `embedFonts: false` turn it off.
- [ ] Restricted faces are skipped without an exception.
- [ ] Family names come from name ID 1; code uses the brand's mono family unless `CodeFont` is set.
- [ ] A sample deck shows its fonts in PowerPoint for Windows where they are not installed.
- [ ] Tests as in step 7 pass; docs and `ai/memory.md` updated; 0 warnings.

## Breaking changes

- Decks of brands with fonts grow (roughly 0.3–1 MB, depending on the families used).
- `PptxOptions.CodeFont` is `string?` and no longer always Consolas.
- A brand whose fonts have a different typographic family name names a different family in decks.

## Open questions

1. Embedding on by default for every brand with fonts (recommended), or off by default?

> Confirmed by the human (2026-10-23): on by default for every brand with fonts.

2. Should the sample brand get a mono face (an OFL mono such as Liberation Mono under its own
   family name would not test anything; a different OFL mono would), so the mono path is covered
   by a round trip?

> Confirmed by the human (2026-10-23): yes — Inconsolata 3.100 (OFL), Regular and Bold, under `fonts/mono/` with its `OFL.txt`.

3. Which viewers are the bar? Proposed: PowerPoint for Windows; PowerPoint Online and others are
   a documented limit.

> Confirmed by the human (2026-10-23): PowerPoint for Windows; the others are documented under *Known limits*.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- **Only used families are embedded:** the text family always (the theme names it); the table and
  mono families only when a slide's `a:latin` names them. A text-only sample deck embeds the four
  Liberation Serif faces; one with a table and code adds PT Sans Narrow (2) and Inconsolata (2).
- **Faces that fall back** (a table italic to the table regular, `MonoBold` to `Mono`) are not
  listed as faces of their own; each file is embedded once.
- **Restricted faces:** `fsType & 0x000E == 0x0002` is skipped, and the family too if it is the
  regular face. PDFs still do not read `fsType`.
- **A brand's own `Pptx` template** now takes the front matter key `embedFonts` (it took no keys
  before); its existing `p:embeddedFontLst` entries of the same family are replaced, others kept.
- `TrueTypeInfo` gained `Table`, `Name`, `EmbeddingFlags` (and `HasCharacter`, used by 00008), so
  there is one font-table reader. `ResolvedFonts.MonoFamily` is read from the mono face.

### Deviations from the plan

- **The sample brand's mono face is Inconsolata, not DejaVu Sans Mono**, which was tried first:
  PdfPig's subsetter crashes on it (`ArgumentOutOfRangeException` in
  `TrueTypeGlyphTableSubsetter`), so no PDF in that brand could be written. Recorded as a trap in
  `ai/memory.md`. A caller's brand with such a font fails the same way — a PdfPig limit, not new.
- Giving `sample` a mono face changed what several older tests asserted (code in Liberation Mono or
  Consolas for `sample`); they now assert Inconsolata, and a new PDF test covers a brand without
  mono (Liberation Mono).

### Breaking changes

- Decks of brands with fonts embed them (bigger files); `PptxOptions.CodeFont` is `string?` and
  defaults to the brand's mono family; family names come from name ID 1 first. In `CHANGELOG.md`.

### Verified

- `FontEmbeddingTests` (11): the EOT header and payload, the families and face counts, the names in
  the theme and slides, switches (option, front matter), `neutral`, custom templates, a brand's own
  template, fallback faces, a face patched to restricted, the code font. The embedded decks pass
  the Open XML validator. The sample PDFs with Inconsolata pass veraPDF (PDF/A-2b), and the code in
  `report-sample.pdf` shows in Inconsolata.

### To verify by hand

- **Open `.tmp/review/embedded-fonts-sample.pptx` in PowerPoint for Windows on a machine without
  Liberation Serif, PT Sans Narrow and Inconsolata** — they should show, and *File → Options →
  Save* should list them as embedded. Not done here (see 00005, *To verify by hand*).
