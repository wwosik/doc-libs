# E03-S03 Footnotes at the page foot, justified text, page breaks

> Status: todo
> Epic: [E03 PDF layout](E03-pdf-layout.md)

## Summary

In PDFs, footnotes move from the end of the document to the foot of the page that references
them. Text can be justified, as an opt-in and without hyphenation. Writers can force a page break
with `::: pagebreak` or `{.newpage}` on a heading. The page-break syntax is shared, so the DOCX
generator (E09) reads it the same way.

## Context

- Split from the epic's row E03-S02 (see
  [E03-S02](E03-S02-contents-heading-numbers-and-captions.md), question 1). This story covers the
  three features that change how lines and pages are filled.
- **Footnotes today** (E06-S02):
  - `MarkdownParser.Parse` takes the `FootnoteGroup` out of the blocks. It appends a `##` heading
    `GeneratedTexts.Footnotes` ("Notes" / "Anmerkungen") and an ordered list of the definitions
    at the end of the document (`MarkdownParser.cs:59-71`).
  - A reference is a `TextRun` with `RunStyle.Superscript` whose text is Markdig's `Order`. The
    run does not link to its note.
  - E06-S02's implementation notes name page-foot notes as the loose end this story closes.
- **Lines are placed in `LayoutEngine.Line`.**
  - Each call checks the room with `Ensure`. `Paragraph` places lines in chunks, with at least
    two lines on each side of a break.
  - Table rows are placed by `Row`, with `advance: false`.
  - Every height check compares against `geometry.Bottom`, a constant per document
    (`PageGeometry`).
- **Text is left-aligned.** `Merge` joins neighbouring pieces in one face into one `TextItem`,
  spaces included. `Wrap` breaks greedily at spaces.
- **No hyphenation:** no hyphenation library with a compatible licence was found (analysis,
  dependencies table).
- **Page breaks happen only**:
  - before every `#` after the cover;
  - when content does not fit;
  - through the keep rules of E03-S01 (`KeepHeight`, `chainTop`).
- **Containers and heading attributes.**
  - `LayoutSyntax.CheckContainer` knows `cards`, `rows`, `panel`, `statement` and `columns`.
    Any other name throws in both generators.
  - In decks, any container makes its section a flow slide (`DeckParser.UsesLayouts`).
  - `HeadingAttributes` throws for unknown classes, so `{.newpage}` fails today.
- **Decks** have no pages to break: each `##` is a slide, and fitting splits slides that are too
  full (E02-S04).

## Proposed approach

1. **Footnotes at the foot of the page** (PDF):
   - **Model.**
     - `TextRun` gets `Footnote` (the note's number, or `null`), set for references, so the
       layout no longer reads numbers from text.
     - `DocumentModel` gets `Footnotes`: by number, the note's paragraphs as runs. Lists and code
       in a note become its text paragraphs, with list markers kept as text.
     - The parser no longer appends the "Notes" heading and list.
   - **Room on the page.**
     - The engine keeps the current page's notes and a `bottom`: `geometry.Bottom` less the
       notes' height. Every check against `geometry.Bottom` for the current page uses `bottom`
       instead (`Ensure`, `Paragraph`, `Table`, `VisualRoom`).
     - Checks that measure a fresh page (`fresh` in `Table`) use the full page, because a new
       page starts without notes.
   - **Placing a reference.** When `Line` places a line, or `Row` a row's lines, that holds
     references not yet placed on this page:
     - it wraps those notes at 8.5 pt across the content width;
     - it requires the line and the notes, plus the separator for the page's first note, to fit
       above `bottom`;
     - otherwise the line moves to the next page, with its notes.

     The two-line rules of `Paragraph` and the keep rules of headings measure the same way. So a
     heading is kept with a first line that carries a long note, too.
   - **Drawing.** When a page is finished (on `NewPage` and at the end of the run), its notes are
     drawn bottom up:
     - a 0.5 pt rule a third of the content width long;
     - each note with its number raised before it, in the text colour at 8.5 pt, aligned like a
       list.
   - **Long notes** (question 2). A note taller than a third of the page is split:
     - the lines that fit go on this page, and the rest continues at the foot of the next page,
       before that page's own notes;
     - a note never splits before its second line.
   - **Numbering** stays Markdig's order through the whole document, as in E06-S02 (question 3).
   - **Links.** Each reference links to its note (`LinkItem` to the note's position), and each
     note's number links back to the reference. PdfPig link annotations already get `/F` for
     PDF/A.
   - **Cover.** References on the cover put their notes at the foot of the cover.
   - **Other blocks.** References in headings, table cells and captions (E03-S02) work like
     those in body text. References inside a note are shown as their number, without a further
     note, as in decks.
   - **`GeneratedTexts.Footnotes`** is no longer used by the PDF. It stays public, so nothing
     breaks, and is documented as unused for now.
2. **Justified text** (PDF):
   - **Setting it.** A front-matter key `justify: true|false` and `PdfOptions.Justify`, default
     `false`. Decks accept the key and ignore it, as for E03-S02's document keys, and the DOCX
     generator reads it.
   - **What is justified:** paragraphs, list items, definition descriptions, quotes, alerts and
     footnotes.
   - **What stays left-aligned:** headings, the cover, table cells, captions, code and the
     contents.
   - **Lines.** `Wrap` stays as it is, so justification never changes how many lines a paragraph
     takes, and `KeepHeight` and page breaks are unaffected.
     - `Line` gets a target width. The extra room is spread evenly over the line's spaces.
     - The last line of a paragraph, a line before a hard break, and a line with one word stay
       left-aligned.
     - A line whose spaces would grow past 3× their normal width also stays left-aligned
       (question 4), so a single long word does not tear a short line apart.
   - **Drawing.** With justification, `Merge` does not join across spaces: each word is its own
     `TextItem` at its x. Link underlines and strike-through lines follow the words, and the
     gaps between linked words are underlined too, as today.
   - No hyphenation (decided), and no `Tw` word-spacing operator. `Tw` only widens single-byte
     space codes, which depends on how PdfPig encodes the embedded fonts; placing each word is
     exact either way.
3. **Page breaks** (shared syntax):
   - **Container.** `::: pagebreak` is a new container in `LayoutSyntax`. It takes no arguments.
     Its closing `:::` is optional: an unclosed container runs to the end of the document, and
     that gives the same result, a break followed by what comes after.
   - **Heading attribute.** `{.newpage}` is a new heading attribute (`HeadingAttributes.NewPage`).
     It is allowed on every heading level, and in PDFs on `###` to `######` under the rule of
     E03-S02 step 1: a trailing `{…}` comes off only when all its tokens apply there.
   - **PDF.**
     - `::: pagebreak` becomes a `PageBreakBlock`, followed by the container's content, if any.
     - The block, and a heading with `NewPage`, start a new page unless the current page is still
       empty. So no break ever produces an empty page, and two breaks in a row produce one.
     - On `#` headings `{.newpage}` changes nothing, because they already start a page. On the
       cover heading it is an error.
   - **Decks** (question 5).
     - `::: pagebreak` is accepted and shows its content as normal blocks. It does *not* make its
       section a flow slide: `UsesLayouts` skips it, so an old-style slide stays old-style.
     - `{.newpage}` is accepted and ignored.
   - **Out of scope:** landscape sections, which were a fork draft.
4. **Notes.** One new code, with advice and a docs row:
   - `footnote-split` (PDF): a footnote was too long for one page and continues on the next.
     The advice is to shorten it.

   Justification and page breaks are what the writer asked for, so they get no notes.
5. **Guides, docs, changelog.**
   - **Guides.** The PDF guide:
     - says footnotes go to the foot of the page;
     - documents `justify` in the front matter table;
     - adds a section on page breaks with both forms.

     The deck guide says `::: pagebreak` and `{.newpage}` are for documents and have no effect on
     slides. The expected guides are regenerated.
   - **Docs.** `docs/wwosik.DocLibs.DocumentGeneration.md` records the shared syntax for E09. The
     Pdf page gets *Footnotes*, *Justified text*, and its *Page breaks* section extended. The
     notes table gets `footnote-split`.
   - **CHANGELOG.** Under breaking changes: footnotes in PDFs move from a "Notes" list at the end
     to the foot of their page. A document with footnotes therefore paginates differently, and
     has no "Notes" heading or bookmark. Under new: `justify`, `::: pagebreak` and `{.newpage}`.
   - `ai/memory.md`: the per-page `bottom`, and the rule that justification never changes line
     breaks.
6. **Tests.**
   - **Layout**, with made-up metrics:
     - a note lands on the page of its reference, also when the reference is in the last line
       that would fit without the note (the line moves);
     - references in a table row and in a heading;
     - a long note that splits, with `footnote-split`;
     - notes on the cover;
     - the links in both directions.
   - **Justification:** the line count is unchanged, every inner line ends at the right margin
     within 0.01 pt, and the last lines and single-word lines are left-aligned.
   - **Page breaks:** they give no empty pages; `{.newpage}` on `###` works; an unknown class on
     `###` stays text as before.
   - **Decks:** they are unchanged by `::: pagebreak` around plain text (no flow slide), and still
     pass the Open XML validator.
   - **PDF/A:** the existing structural checks pass, and veraPDF is run by hand on a justified
     document with footnotes.
   - **Samples.** `long.md` gets footnotes on several pages, `justify: true` and a page break.
     Its roundtrip expected files are regenerated and reviewed. `report.md` without these keys is
     unchanged except where it has footnotes.

## Acceptance criteria

- [ ] Every footnote is printed at the foot of the page that holds its reference, below a short
  rule, in text and table cells alike. A note too long for one page continues on the next
  with `footnote-split`.
- [ ] References link to their notes and back; the PDF is still PDF/A-2b.
- [ ] `justify: true` justifies body text without changing any line break; the last lines, lines
  before hard breaks and single words stay left-aligned. Headings, tables and code are never
  justified.
- [ ] `::: pagebreak` (closed or not) and `{.newpage}` on any heading level start a new page,
  and never an empty one.
- [ ] Decks accept `justify`, `::: pagebreak` and `{.newpage}` without changing slides that do
  not use them.
- [ ] Guides, expected guides, docs, CHANGELOG and memory are updated, and all tests pass.

## Open questions

1. Is this the right second part of the split? Footnotes, justification and page breaks together
   (proposed: they all change how lines and pages fill). Or should footnotes be a story of their
   own?

> Answered by the human (2026-11-14): as proposed.

2. A footnote too long for the room left: split it across pages (proposed: as LaTeX does; keeping
   it whole can push a line and its paragraph a page on). Or always keep it whole and move the
   referencing line?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Footnote numbers: through the whole document (proposed: what E06-S02 does, and what Word
   does by default), or restarting on every page?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Justification: leave a line ragged when its spaces would grow past 3× (proposed). Or always
   justify, even with very wide gaps? Without hyphenation, narrow list items can get loose.

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. `::: pagebreak` in decks: accepted and ignored (proposed: a slide is already a page). Or an
   explicit split, so that the section continues on a new slide with the same title?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Should `justify` also be a brand option (`BrandPdfTemplateOptions`), so a design can justify
   by default? Or stay a document setting, `PdfOptions` plus front matter (proposed)?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. May `::: pagebreak` hold content, with its closing `:::` optional (proposed: an unclosed
   container would otherwise swallow the rest of the document, and both readings give the same
   pages)? Or must it be empty and closed, as E09-S02 currently assumes, with content an error?

> Answered by the human (2026-11-14): as proposed.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
