# E09-S06 Word core properties

> Status: in progress (port-1)
> Epic: [E09 DOCX generator](../../50-todo/E09-docx-generator.md)

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

- [ ] A document with a subtitle and `description:` has `dc:subject` and `dc:description` in Word,
      PowerPoint and the PDF's metadata.
- [ ] `keywords:` is written to Word, PowerPoint and the PDF; the PDF's subject is the subtitle.
- [ ] Nothing of a caller's template's core properties survives.
- [ ] The front-matter tables in the docs and authoring guides list `description`.

## Open questions

1. Is `keywords` (`cp:keywords`, PDF `Keywords`) wanted as a front-matter key too?

> Yes: a front-matter key `keywords`, written to `cp:keywords` and the PDF's `Keywords`. (2026-11-26)

2. Should the subtitle also go into the PDF's subject, which today holds nothing?

> Yes: the PDF's subject takes the subtitle, as Word does. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
