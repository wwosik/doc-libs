# E06-S07 Backup slides and appendices

> Status: in progress (port-1)
> Epic: [E06 Markdown syntax](../../50-todo/E06-markdown-syntax.md)

## Summary

- `# Backup {.backup}` makes that chapter, and every chapter after it, a deck's backup: slides kept
  behind the closing slide for questions.
- `# Glossary {.appendix}` makes that chapter, and every chapter after it, a document's appendices,
  lettered A, B, A.1 in PDFs and Word.

## Context

- **Heading attributes** (`src/wwosik.DocLibs.DocumentGeneration/HeadingAttributes.cs`) know `.dark`,
  `.closing`, `.unnumbered`, `.newpage`, `kicker=`, `layout=` and `transition=`. Others are an error.
- **Decks.** The closing slide is `# … {.closing}` (`DeckParser.cs` around 430–476).
  - An empty `{.agenda}` lists the chapters (`SlideKinds.cs`).
  - Chapter numbers come from E01-S07.
  - The footer pattern can name `{chapter}` (`DeckWriter.cs` around 81–112).
- **Documents.** Heading numbers (`HeadingNumbering.cs`, three levels) and the contents are used by
  PDFs (`LayoutEngine.cs`) and Word (`DocxNumbering.cs`, a `TOC` field).
- **The PPTX converter** (`src/wwosik.DocLibs.DocumentConversions/OpenXml/PptxConverter.cs`) writes
  `## <slide title>` per slide.

## Proposed approach

1. **`.backup` in decks.** The heading's slide and every later slide go behind the closing slide, in
   their order.
   - They are out of an empty `{.agenda}`, chapter numbering and the HTML deck's progress.
   - The footer adds " · Backup" (`GeneratedTexts.Backup`, English and German).
   - The first backup slide is named `Backup` (`p:cSld/@name`), and `PptxConverter` reads it back as
     `# … {.backup}`.
   - `Check` counts backup slides as their own kind and leaves them out of words and talk time.
   - Backup slides stay in every audience version (E06-S06); `{.internal}` on the backup chapter hides
     them from the customer's.
2. **`.appendix` in PDFs and Word.** The heading and every later `#` are appendices.
   - They are numbered `A`, `B` and so on, with sub-headings `A.1`, in the text, the contents and the
     bookmarks.
   - The appendix heading text gets the prefix "Appendix A" (`GeneratedTexts.Appendix`).
   - Word does this with a second level-1 numbering in letters on the same multi-level list.
3. **Each format ignores the other's attribute**, with the note `attribute-ignored`.
4. **Mistakes throw** with the line: either attribute twice, on a `##`, inside a container, on the
   cover, together with `.closing`; a closing slide after the backup.
5. **Docs, guides and the changelog.**

## Acceptance criteria

- [ ] A deck with backup slides shows them after the closing slide, out of agenda, numbering and
      progress, with the footer word, and reads back as `{.backup}`.
- [ ] A PDF and a Word document with appendices letter them in text, contents and bookmarks.
- [ ] Documents without the attributes are unchanged.
- [ ] Docs, guides and changelog updated.

## Open questions

1. Should "Appendix A" prefix the heading text, or only the number change to the letter?

> The generated word and the letter before the title, e.g. "Appendix A Glossary". (2026-11-26)

2. Should backup slides be dropped entirely from the customer's version (E06-S06)?

> They stay in the customer's version; `{.internal}` on the backup chapter hides it. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
