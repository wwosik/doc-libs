# E09-S04 DOCX content

> Status: todo
> Epic: [E09 DOCX generator](E09-docx-generator.md)

## Summary

The DOCX generator of [E09-S01](E09-S01-docx-package-and-templates.md) writes the content of
ordinary Markdown in Word: headings and their numbering, lists, tables with table titles,
numbered captions, images, code, quotes, real Word footnotes, links, task and definition lists,
and the document language. Deck syntax ([E09-S02](E09-S02-deck-syntax-in-word.md)) and charts and
drawings ([E09-S03](E09-S03-charts-drawings-and-guide.md)) build on it.

## Context

- **Split from E09-S01.** The first plan of E09-S01 held the package and the content together; it
  was split as its question 1 proposed. This story is the former S01b.
- **Order.** This story is implemented right after E09-S01 and before E09-S02 and E09-S03, despite
  its number: story IDs are allocated as the next free number.
- **What E09-S01 leaves for this story:**
  - the package, `DocxGenerator`, `DocxOptions`, the template kinds and `DocxStyleNames` with
    lookup by name and creation of missing role styles;
  - the built-in template's theme, page, heading styles, cover, header and footer;
  - a TOC field pre-filled with plain entries;
  - a plain body: headings in `Heading N` by Markdown level, everything else as `Normal` text.
- **The PDF generator is the closest to a flowing document** (`MarkdownParser`, `DocumentModel`).
  - The first `#` becomes the cover, with the paragraphs after it as subtitle. Every other `#`
    starts a new page.
  - Headings keep their Markdown level. Lists, definition lists, tasks (`ListBlock.Tasks`), code,
    quotes, tables (alignments, header row) and image rows are separate blocks.
  - The slide syntax is checked through `LayoutSyntax` and `HeadingAttributes.Split`, then shown
    plainly.
  - Images are fitted by `ImageInfo` and `"scale=N"` in the title, and converted through
    `ImageConverters.Convert` when not PNG/JPEG.
  - Column widths: each column gets at least its longest word, and the rest is shared in
    proportion to the content.
- **Text widths** come from `TextMetrics`, moved to the shared package by E09-S01.
- **Shared Markdown pieces:**
  - `MarkdownSyntax.UseDocumentSyntax()` turns on footnotes, task lists and definition lists, and
    `MarkdownSyntax.ReportMissingFootnotes` reports undefined references.
  - `DocumentLanguage.Resolve` resolves the document language, and `GeneratedTexts` holds the
    alert titles and `Footnotes` in English and German, through `Texts` (E06-S02).
  - `ColouredWords` handles coloured words, and `FrontMatter.Labels()` the status labels.
- **The DOCX converter.** `DocumentConversions.OpenXml.DocxConverter` already reads `.docx` back to
  Markdown:
  - headings by style name or id (`ClassifyHeading`: "heading N", "Title");
  - lists by `w:numPr` and the numbering's format;
  - tables, runs with bold, italic and strike, hyperlinks, and images (`a:blip`).
  - It reads no footnotes and no code style yet (the fork's analysis puts those in E08).
- **The epic's rule:** paragraphs reference template styles by name only, and there is no direct
  formatting except in code.
- **Agreed in E03-S02** (shared syntax and keys, which this story reads as defined there):
  - `headingNumbers: true|false`. Its default differs by format: off in the PDF, on in the
    built-in DOCX template (question 2). The host default is `DocxOptions.HeadingNumbers`.
  - Numbering starts at the highest heading level after the cover and covers three levels; the
    cover heading is never numbered; `{.unnumbered}` headings get no number and do not count.
  - `toc` and `tocDepth` (used by E09-S01's TOC field).
  - **Captions** through the shared `Captions` helper:
    - an image's title, after `scale=N` and a standalone `caption` are taken out; `"caption"`
      alone means "use the alt text";
    - a paragraph starting `Table: ` directly after a table (a blank line between is allowed);
    - a paragraph starting `Figure: ` after a drawn diagram or chart (E09-S03).
    - `Table:` and `Figure:` stay English; the label is in the document language
      (`GeneratedTexts.Figure` / `Table`), and a caption reads `<label> <n>: <text>`.
  - **Placement:** table captions above the table, figure captions below it.
  - **Numbering:** figures and tables separately, through the whole document.
  - A `Table:` / `Figure:` paragraph that follows no table or drawn diagram stays text and gives
    `caption-ignored`.

## Proposed approach

1. **Roles and styles.**
   - `DocxStyleNames` (E09-S01) gains the content roles:
     - `ListBullet`, `ListNumber`, `ListTask`;
     - `DefinitionTerm`, `Definition`;
     - `Quote`, `Code`, `CodeCharacter`, `Rule`;
     - `Caption`, `TableTitle`, `Table` (a table style);
     - `FootnoteText`, `FootnoteReference`, `Hyperlink`.
   - They take `styles.<role>` front-matter keys, and caller templates that lack them get them
     created, both through E09-S01's mechanism.
   - **Built-in template** (`DocxTemplateBuilder`) gains, following the PDF's sizes and colours:
     - a link character style in `Link`;
     - `Quote` grey with a left border, and `Code` in the mono family, 9 pt, on light grey;
     - `Caption`, `TableTitle`, the list and definition styles, the footnote styles, and `Rule`;
     - the table style `Doc Table`: header row bold white on `TableHeader`, banded rows in
       `TableBand`, grid lines in `TableLine`, 9 pt, in the brand's table family if it has one;
     - `numbering.xml`: one multilevel list linked to Heading 1–3 (`1`, `1.1`, `1.1.1`), a bullet
       definition (`•`, `–`, `▪`) and an ordered one (`1.`, `a.`, `i.`), three levels each;
     - `footnotes.xml` with separators.
   - **Numbering in caller templates:**
     - headings keep whatever numbering the template's heading styles have;
     - lists use the numbering of the template's list styles;
     - where a list style has none, the generator adds its own definitions.
2. **Content** (a parser of its own on the shared pipeline, modelled on `MarkdownParser`, writing
   Open XML with the SDK's typed classes). It replaces E09-S01's plain body.
3. **Headings.**
   - Markdown levels map to Heading 1–6. The highest level used after the cover becomes Heading 1
     (question 1), the same rule E03-S02 numbers PDF headings by.
   - **Numbers.** Heading 1–3 are numbered through the template's multilevel list; Heading 4–6
     are unnumbered.
     - On the built-in template numbers are on by default; `headingNumbers: false` (or
       `DocxOptions.HeadingNumbers = false`) removes them (question 2). The PDF's default stays
       off (E03-S02).
     - On caller templates their own heading styles decide.
     - A heading with `{.unnumbered}` gets no number and does not count (`w:numPr` with
       `w:numId` 0).
   - The TOC (E09-S01) lists headings down to `tocDepth`; its pre-filled entries now carry the
     numbers and are hyperlinks to the headings' bookmarks.
   - A heading from a later `#` starts a new page (`w:pageBreakBefore`), as in the PDF.
   - Heading attributes are checked and dropped through `HeadingAttributes.Split`, as in the PDF,
     including E03-S02's rule for `###`–`######`: a trailing `{…}` is taken off only when every
     token in it applies at that level.
   - Every heading gets a bookmark named from its Markdig id, made Word-safe: letters, digits and
     `_`, at most 40 characters, unique. `[x](#id)` links jump to it.
4. **Paragraphs.**
   - Paragraphs use `Normal`; hard breaks become `w:br`.
   - Inline styles: `**bold**`, `*italic*` and `~~strike~~` become `w:b`, `w:i` and `w:strike`
     on the run, as Ctrl+B does in Word. This is the one kind of run formatting besides code.
   - Inline code uses `CodeCharacter`.
   - Raw HTML stays text, with `html-as-text`.
5. **Lists.**
   - Bulleted and numbered lists to three levels use `ListBullet` and `ListNumber`, with `ilvl`
     0–2.
   - Each ordered list is a new `w:num` with a `w:startOverride` from its Markdown start, so it
     restarts.
   - Deeper items stay at level 3 and give a new note, `list-too-deep` (question 5).
   - Paragraphs inside an item continue it with the list indent and no marker.
   - **Task lists.** `- [ ]` and `- [x]` get a Word check-box content control (`w14:checkbox`,
     ☐/☒) in place of the bullet (question 4).
   - **Definition lists.** Each term is a `DefinitionTerm` paragraph (bold, `keepNext`), and each
     description a `Definition` paragraph (indented).
6. **Tables.**
   - A table spans the text width (`w:tblW` 100 %, fixed layout) and uses the `Table` style.
   - The header row repeats (`w:tblHeader`).
   - Column widths come from `TextMetrics`, by the PDF's rule: at least the longest word, the rest
     in proportion to the content. They are written to `w:tblGrid` and `w:tcW`.
   - Alignment from the Markdown becomes `w:jc` in the cell paragraphs.
   - Rows do not break across pages (`w:cantSplit`) unless they are taller than a page.
   - **Table title.** A paragraph of only bold text right before a table becomes a `TableTitle`
     paragraph with `keepNext` (question 3). The PDF goes on showing it as a bold paragraph.
7. **Captions** (the syntax agreed in E03-S02):
   - A table's `Table: …` paragraph and an image's caption from its title become `Caption`
     paragraphs: "Table " or "Figure " (`GeneratedTexts.Table` / `Figure`, in the document
     language), then a `SEQ Table \* ARABIC` or `SEQ Figure \* ARABIC` field, then ": " and the
     text. The caption paragraph of the Markdown is consumed, not written again.
   - The field's result is filled in with the number, so it is right without an update. Figures
     and tables are numbered separately through the whole document, as `SEQ` fields without
     chapter numbers do.
   - **Placement,** as in the PDF (E03-S02): a table caption goes **above** the table, kept with
     it (`keepNext`); a figure caption goes **below** the image, and the image paragraph keeps
     with it (`keepNext`).
   - A table title and a `Table:` caption on the same table: the title comes first, then the
     caption, then the table.
   - A misplaced `Table:` / `Figure:` paragraph stays text with `caption-ignored`.
8. **Images.**
   - PNG, JPEG and GIF are placed inline in a centred paragraph, at their natural size at 96 DPI
     times `scale=`.
   - They are scaled down to the text width and 80 % of the text height, never up. E09-S02's
     image rows and E09-S03's drawings reuse these fitting rules.
   - Alt text goes into `wp:docPr/@descr`.
   - Other formats go through `ImageConverters`, as in the PDF. The SVG kept beside them comes in
     E09-S03.
9. **Code, quotes, rules.**
   - **Code blocks** without a renderer become one `Code` paragraph per line, with tabs as four
     spaces. Code is the only place with direct formatting: none is needed beyond the style. A
     block a renderer draws stays code with `code-not-drawn` until E09-S03.
   - **Quotes.** Quotes use `Quote`, and nested quotes are flattened into it.
   - **Rules.** `---` becomes an empty paragraph with a bottom border, through the `Rule` style.
10. **Footnotes.**
    - They are real Word footnotes: `w:footnoteReference` runs in `FootnoteReference`, with the
      text in the footnotes part in `FootnoteText`.
    - Word numbers them. A missing definition gives `footnote-missing`, as in the other
      generators.
11. **Links.**
    - Absolute URLs become `w:hyperlink` with an external relationship in `Hyperlink`.
    - `#id` links go to the heading's bookmark (`w:anchor`).
    - Anything else stays text.
12. **Language.**
    - The tag from `DocumentLanguage.Resolve` goes into `w:lang` in the document defaults
      (`w:rPrDefault`) and `w:themeFontLang`, so Word spell-checks in that language.
    - On caller templates the same defaults are set, and their own style-level languages are left
      alone.
13. **Deck syntax until E09-S02.**
    - Containers, alerts, `==mark==`, labels, coloured words and image rows are checked by
      `LayoutSyntax` and `ColouredWords`, as in the PDF, and shown plainly:
      - container content in sequence;
      - alerts as `Quote` with the alert title in bold;
      - marks bold, labels as code, coloured words bold;
      - image rows as images one after the other.
    - So `layouts.md` already generates. E09-S02 replaces this with Word styles.
14. **Notes.**
    - Docx is added to the formats of `code-not-drawn`, `html-as-text`, `footnote-missing` and
      `caption-ignored` in `NoteCodes.All` (`caption-ignored` added by whichever of this story and
      E03-S02 comes first).
    - The new `list-too-deep` gets an entry and a docs row.
15. **Errors.** `DocumentConversionException` with `DocumentFormat.Docx` is thrown for:
    - a missing or unconvertible image;
    - invalid slide syntax.
16. **Tests** (`Docx.Tests`, xUnit):
    - **Validation.** `report.md`, `long.md` and `layouts.md` are generated with the neutral brand,
      the sample brand and the custom template of E09-S01, and pass
      `OpenXmlValidator(FileFormatVersions.Microsoft365)` with no errors.
    - **Round trip.** Each output goes through the existing `DocxConverter` and is compared with
      `<sample>-<template>.roundtrip.expected.md` (`DOCLIBS_UPDATE_EXPECTED=1` rewrites them).
      Footnotes and code style are lost in the round trip until E08; the expected files show it.
    - **Unit tests:**
      - heading level shift and page breaks at later `#`;
      - heading numbers on by default on the built-in template, off with `headingNumbers: false`,
        and none on `{.unnumbered}`;
      - bookmarks and TOC entries linking to them;
      - list restart (two ordered lists both start at 1, `3.` starts at 3) and `list-too-deep`;
      - task check boxes and definition lists;
      - table widths: the longest word fits, and the sum is the text width;
      - the header row repeats, and the table title;
      - captions: their `SEQ` numbers, table captions above, figure captions below, the
        `"caption"` title using the alt text, `scale=` alone giving none, and `caption-ignored`;
      - image fitting and alt text;
      - footnote ids and part;
      - `w:lang` for `lang: de`, and the German "Tabelle" and "Abbildung";
      - missing content-role styles created on a caller template.
    - **For review.** Every sample is written to `.tmp/review/` for a human to open.
17. **Authoring guide, docs, changelog.**
    - **Guide.** `AuthoringGuide/Docx.md` gets the content sections: headings and numbers, lists,
      tables and table titles, captions, images, footnotes, links. The expected guides are
      regenerated.
    - **Docs.** The Docx page: the content roles in the role table, the Markdown → Word table,
      heading numbers, captions, known limits.
    - **Changelog.** `CHANGELOG.md` *New*: the content of Word documents. *Breaking changes*: none.
    - **Memory.** `ai/memory.md`: the heading-level shift, and that heading numbers default on in
      DOCX and off in PDF.
18. **Verification in real Word and LibreOffice.**
    - **Word.** Through E09-S01's Word export. The check confirms:
      - no repair prompt;
      - headings are numbered and the TOC shows the numbers and page numbers;
      - numbering restarts;
      - the table header repeats, and captions sit above tables and below figures;
      - footnotes are at the page foot.
    - **LibreOffice,** where installed, or recorded as not done.

## Acceptance criteria

- [ ] **Headings.** On the built-in template they are numbered 1, 1.1, 1.1.1 on Heading 1–3 and
  unnumbered below. A document `# Title` / `## A` / `### B` gives "1 A" and "1.1 B". Every later
  `#` starts a page, `headingNumbers: false` removes the numbers, and `{.unnumbered}` headings get
  none.
- [ ] **Lists.**
  - Lists nest to three levels.
  - Every ordered list restarts at its own start number.
  - A fourth level stays at level 3 and gives `list-too-deep`.
  - Task items show check boxes, and definition lists show a bold term and an indented description.
- [ ] **Tables.**
  - Tables span the text width, and the header row repeats on the next page in Word.
  - No column is narrower than its longest word.
  - A bold paragraph before a table is a table title.
- [ ] **Captions.** `Table: …` gives "Table n: …" above the table, and an image title gives
  "Figure n: …" below the image, each with a `SEQ` field, in the document language.
- [ ] **Images.** They are fitted to the width and 80 % of the height, never enlarged, and carry
  their alt text.
- [ ] **Other content.**
  - Code is in the code style, and quotes in `Quote`.
  - Links work: external links and `#heading` links.
  - Footnotes are Word footnotes at the foot of the page.
- [ ] **Language.** `lang: de` sets `w:lang` `de` in the defaults, and gives the German "Tabelle"
  and "Abbildung".
- [ ] **Round trip.** Every sample goes through `DocxConverter` to Markdown matching the committed
  expectation: headings, lists, tables, emphasis, links and images survive.
- [ ] **Real Word.** The samples open in Word without a repair prompt and look as described.
  LibreOffice is checked too, or recorded as not available.
- [ ] **Notes.** `GenerationResult` carries the DOCX notes, and `NoteCodes.All` lists Docx where it
  applies.
- [ ] **Docs.** The authoring guide, the docs, the CHANGELOG and `ai/memory.md` are updated, and all
  tests pass.

## Open questions

1. How do Markdown levels map to Word headings? Proposed: the highest level used after the cover
   becomes Heading 1, so `# Title` + `##` sections gives "1, 2, 3" and not "0.1, 0.2". E03-S02
   should number PDF headings by the same rule. The alternative is `#` → Heading 1 always, as the
   PDF's sizes do.

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Should headings be numbered by default in Word? Proposed: yes on the built-in template, as Word
   reports usually are, with E03-S02's `headingNumbers: false` to switch it off. On caller
   templates their own heading styles decide. The alternative is off by default, as in the PDF.

> Answered by the human (2026-11-14): as proposed.

3. A paragraph of only bold text right before a table: should it become a table title? Proposed:
   yes in Word (`TableTitle`, kept with the table), next to E03-S02's captions. The PDF goes on
   showing it as a bold paragraph. The alternative is no table titles, captions only.

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Task-list boxes: should they be Word check-box content controls that can be clicked in Word?
   Proposed: yes, with ☐/☒ in the template's symbol font. The alternative is the plain characters
   ☐/☒ as the bullet, which other viewers show the same way.

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Lists deeper than three levels: stay at level 3 with a `list-too-deep` note (proposed, as the
   fork does), or use Word's levels 4–9 with the definitions continued?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- **Order with E03-S02.** The analysis orders E09 (8) before E03-S02 (9). The shared syntax and key
  names (`Table: …`, `Figure: …`, image title as caption, `headingNumbers`, `{.unnumbered}`, the
  `Captions` helper, and the `GeneratedTexts` `Figure` and `Table`) are agreed in the E03-S02
  plan. Whichever is implemented first adds them to the shared package; the other reuses them. If
  this story is first, PDF and PPTX accept a `Table: …` paragraph as plain text until E03-S02.
- **Order within E09.** E09-S01 → E09-S04 → E09-S02 → E09-S03, although the number says
  otherwise.
- **Out of scope** by the human's decision: cross-references, abbreviations and accessibility.
