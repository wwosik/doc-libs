# E06-S07 Backup slides and appendices

> Status: in verification
> Epic: [E06 Markdown syntax](../50-todo/E06-markdown-syntax.md)

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

- [x] A deck with backup slides shows them after the closing slide, out of agenda, numbering and
      progress, with the footer word, and reads back as `{.backup}`.
- [x] A PDF and a Word document with appendices letter them in text, contents and bookmarks.
- [x] Documents without the attributes are unchanged.
- [x] Docs, guides and changelog updated.

## Open questions

1. Should "Appendix A" prefix the heading text, or only the number change to the letter?

> The generated word and the letter before the title, e.g. "Appendix A Glossary". (2026-11-26)

2. Should backup slides be dropped entirely from the customer's version (E06-S06)?

> They stay in the customer's version; `{.internal}` on the backup chapter hides it. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

Only for Markdown that failed or stayed text before, and for texts (all in `CHANGELOG.md`):

- `{.backup}` / `{.appendix}` on `###` to `######` used to stay text and now throw (like on `##`, in a
  container, on the cover, twice, with `.closing`, and a `{.closing}` after the backup).
- `HeadingAttributes.Valid` (in error messages) names `.backup, .appendix`; the three guides changed
  (deck: a `{.backup}` row, a few sentences shortened for the 3,500-word limit, now about 3,494; PDF and
  Word: a paragraph *Appendices*); the advice of `attribute-ignored` names heading attributes;
  `CheckStatistics.SlideKinds` ends with `backup`; `GeneratedTexts` gains `Backup` and `Appendix`.
- Documents without the attributes generate exactly as before; the existing suites pass unchanged
  apart from the regenerated guide files.

### Decisions and deviations

- **One shared check.** `BackMatter.Check` (shared package) runs in all three parsers right after
  `PageBreaks.Flatten`, before `AudienceFilter.Apply`, so errors do not depend on the version. Both
  attributes are checked in every format; each format notes the other's attribute (`attribute-ignored`)
  and otherwise ignores it. Levels 3 to 6 throw too (the story named `##`), like `{.internal}`.
- **The cover** is a deck's first `#`; in documents a `#` that is the first block (markers, notes,
  footnotes and link definitions left aside).
- **No reordering needed.** The backup runs to the end of the document and a closing slide after it is
  an error, so the backup slides are already behind the closing slide. Without a closing slide they are
  simply the last slides.
- **Audience versions.** `{.internal}` on a backup or appendix heading removes everything to the end of
  the document in the customer's version (not only to the next `#`), since every later chapter belongs
  to it.
- **Footer word.** On a layout with a footer placeholder the text is "Footer · Backup", or "Backup"
  alone without a footer text. The built-in master draws its footer text as a text box of its own, not a
  placeholder, so there a text box named `Backup` (footer size and colour) goes on the footer line, right
  of the date and slide number (else at the foot of the content area). German keeps "Backup", the usual
  word on German slides (the test that every German text differs exempts it).
- **Section dividers in the backup** get no chapter number and stay out of an empty agenda; the footer
  pattern's `{chapter}` follows them as before.
- **HTML deck.** The reader marks every slide from the one named `Backup` (`SlideScene.Backup`); the page
  writes `data-backup` on those sections. The counter shows `n / m` over the main slides and `m + k` on
  the k-th backup slide; the presenter window says "Slide n / m" (was "n of m") for the same reason.
  The text box is plain DrawingML the reader already draws, so no reader change and no new sample.
- **Check.** Backup slides are the kind `backup` whatever their layout; `Words` skips the Markdown from
  the `{.backup}` heading on (style rules still apply there); `TalkTime` skips backup slides.
- **Appendix headings.** The prefix is `GeneratedTexts.Appendix` + letter (A–Z, then AA, AB …), shown
  whether or not headings are numbered. PDF: put before the heading's text, so contents and bookmarks
  read it too; the appendix `#` has no number in the number column (it would widen the `#` column of
  every chapter), the headings below it are `A.1` when numbered. Word with numbered headings (built-in
  template): a second `w:num` on the heading list's abstract numbering with a level-0 override
  (restart, `upperLetter`, `lvlText` "Appendix %1", space suffix, no indent), set as paragraph `numPr`
  on every numbered heading from the first appendix on; the pre-filled TOC reads "Appendix A Glossary".
  Without numbers (option off, or a caller's or brand's template) the words are the heading's own text,
  and a caller's template's own numbering, if any, still shows in front of them.

### Verification concerns and loose ends

- Not looked at in PowerPoint or Word: the built-in `Backup` text box's position next to the date, and
  Word's display of the second numbering (letters, the space suffix, TOC update) are to be confirmed.
- `HtmlDeckTests.EverySampleDeck_IsDrawn` does not include a backup; the preview golden images are
  unchanged.
- Tests: `src/wwosik.DocLibs.DocumentGeneration.Tests/BackMatterTests.cs` (deck order, agenda, numbers,
  slide name, footers, HTML marks, check statistics, audience versions, PDF text/contents/bookmarks with
  and without numbers and in German, Word numbering and text, the ignored-attribute notes, 30 mistake
  cases over three formats, letters) and
  `src/wwosik.DocLibs.DocumentGeneration.Pptx.Tests/BackupSlideTests.cs` (valid decks, the text box on
  the built-in master, the footer placeholder of a brand template, German). Every test project passes on
  Linux, run one by one.
