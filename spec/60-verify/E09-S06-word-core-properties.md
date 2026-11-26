# E09-S06 Word core properties

> Status: in verification
> Epic: [E09 DOCX generator](../50-todo/E09-docx-generator.md)

## Summary

Word documents carry their subtitle as the subject and a description in the core properties, next to
the title, author and language they already have. Decks get the same where PowerPoint shows them.

## Context

- `DocxGenerator.cs` (around line 156) calls `CoreProperties.Write(core, title, author, language.Tag,
  now)` and `CoreProperties.ContentStatus(core, DocxMarking.Status(context))`
  (`src/Shared/OpenXml/CoreProperties.cs`). It writes:
  - `dc:title`, `dc:creator` and `dc:language`;
  - `dcterms:created` and `dcterms:modified`;
  - `cp:contentStatus` (classification and draft).
- It does not write:
  - `dc:subject`, although the cover has a subtitle (`DocxContent.cs`, `DocxCover.Subtitle`);
  - `dc:description`;
  - `cp:keywords`;
  - `cp:lastModifiedBy`.
- `w:lang` is set on the styles and the settings (`DocxLanguage.cs`).
- The PPTX generator uses the same `CoreProperties`.

## Proposed approach

1. `dc:subject` is the cover's subtitle as plain text (its lines joined with " – "), when there is one.
2. `dc:description` comes from a new front-matter key `description`, shared by every generator.
   - Word and decks write it into their core properties.
   - PDFs write it as XMP `dc:description` and the document information's `Subject`.
3. `cp:lastModifiedBy` is the author, so Word does not show the template's last editor.
4. A template's own core properties are replaced, never merged: no previous author, title or keywords
   survive.
5. A new front-matter key `keywords` (a comma-separated list), shared by every generator, goes to
   `cp:keywords` in Word and decks and to `Keywords` in the PDF's document information and XMP
   (`pdf:Keywords`).
6. The PDF's subject takes the subtitle as Word's does; with `description:` as well, the subject is
   the subtitle and the description goes to `dc:description`.
7. Tests read the core part of a generated document.

## Acceptance criteria

- [x] A document with a subtitle and `description:` has `dc:subject` and `dc:description` in Word,
      PowerPoint and the PDF's metadata (in the PDF see the deviation below: there the subtitle wins).
- [x] `keywords:` is written to Word, PowerPoint and the PDF; the PDF's subject is the subtitle.
- [x] Nothing of a caller's template's core properties survives.
- [x] The front-matter tables in the docs and authoring guides list `description`.

## Open questions

1. Is `keywords` (`cp:keywords`, PDF `Keywords`) wanted as a front-matter key too?

> Yes: a front-matter key `keywords`, written to `cp:keywords` and the PDF's `Keywords`. (2026-11-26)

2. Should the subtitle also go into the PDF's subject, which today holds nothing?

> Yes: the PDF's subject takes the subtitle, as Word does. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

Yes (all in `CHANGELOG.md`):

- Word documents and decks now write `dc:subject` (the cover's or title slide's subtitle) and
  `cp:lastModifiedBy` (the author); PDFs with a cover subtitle get `/Subject` and XMP `dc:description`.
- A caller's Word or deck template keeps none of its core properties in the output; a deck keeps only
  the template's `dcterms:created` and `dcterms:modified`, as it did (decks never wrote dates of their
  own, and the built-in deck template has no core part).
- Error messages listing the valid front-matter keys name `description, keywords` after `author`.
- Every authoring guide (and the tool guide) has two more front-matter rows. The deck guide was at its
  3,500-word limit, so four sentences there are shorter (the intro, the footnote line, the layouts
  intro and the `{.dark}` row).
- After rebasing onto the cover-image and appendix work, the DOCX guide passed its 3,500-word limit
  too: its intro sentence and the image-attributes bullet (E09-S05) are shorter.

### Decisions and deviations

- **PDF: the subject is the subtitle, else the description; never both.** PDF/A-2b requires the
  document information's `/Subject` and the XMP `dc:description` to hold the same value, so step 6 ("the
  subject is the subtitle and the description goes to `dc:description`") cannot hold as written: with
  a subtitle and a `description:`, the PDF carries the subtitle in both and the description is not
  written. Without a subtitle the description is the subject. **Open for the human:** if the
  description should win in PDFs instead, it is one line in `PdfGenerator.Lay`.
- **PDF XMP.** PdfPig writes `dc:description` as plain text and leaves the keywords out of the XMP
  altogether; `PdfRenderer.FixXmp` now makes `dc:description` a language alternative, adds
  `pdf:Keywords` (equal to `/Keywords`, as PDF/A wants) and each keyword as a `dc:subject` bag entry.
  The checks are string checks on the packet, as the other PDF/A tests do; veraPDF was not run here.
- **Keywords** are split at commas, trimmed, empty ones dropped, and written joined by ", " (Word,
  decks, PDF). They are not typeset. `description` and the Word subject are typeset like the title
  (with `typography: true` the subject has the cover's quotes).
- **Subject text:** the subtitle's paragraphs and hard line breaks are lines, joined by " – ". Decks
  take it only from a title slide that starts the deck (not a section divider).
- **`CoreProperties`** (linked source) gained `Reset(part, keepDates)`, `LastModifiedBy` and an
  optional `keywords` on `Describe`; `Write` is unchanged, so workbooks (which call only `Write` and
  `Describe` without keywords) write the same bytes.
- `FrontMatter.DescriptionKey` / `KeywordsKey` are shared keys, skipped by `Apply` like `title`.

### Verification

- Docx `CorePropertiesTests`: subject, description, keywords, last editor; nothing written without them;
  the subject typeset; nothing of a caller's template's properties survives (a template with title,
  creator, subject, keywords, last editor, revision and category); validator clean.
- Pptx `CorePropertiesTests`: the same for decks, and a caller's template keeps only its created date.
- Pdf `DocumentPropertiesTests`: `/Subject`, `/Keywords` read back by PdfPig, the XMP forms, the
  description as subject without a subtitle, nothing without either.
- Guides regenerated with `DOCLIBS_UPDATE_EXPECTED=1`; the deck guide is under its limit again.
- **Not checked here:** Word's and PowerPoint's file-properties dialogs, and veraPDF on a PDF with
  subject and keywords.

