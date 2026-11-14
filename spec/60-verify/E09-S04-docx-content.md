# E09-S04 DOCX content

> Status: in verification
> Epic: [E09 DOCX generator](../50-todo/E09-docx-generator.md)

## Summary

The DOCX generator of [E09-S01](E09-S01-docx-package-and-templates.md) writes the content of
ordinary Markdown in Word: headings and their numbering, lists, tables with table titles,
numbered captions, images, code, quotes, real Word footnotes, links, task and definition lists,
and the document language. Deck syntax ([E09-S02](../55-in-progress/doc-3/E09-S02-deck-syntax-in-word.md)) and charts and
drawings ([E09-S03](../55-in-progress/doc-3/E09-S03-charts-drawings-and-guide.md)) build on it.

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

## Implementation notes

### Breaking changes

None for consumers. The DOCX package is unreleased (it came with E09-S01, under *Unreleased*), so its
plain body turning into real content changes no released output. Everything public is added:
`DocxOptions.HeadingNumbers`, the fifteen content roles of `DocxStyleNames` (and their `docx.styles`
properties in `brand.json`), `GeneratedTexts.Figure` and `Table`, `NoteCodes.CaptionIgnored` and
`ListTooDeep`. `CHANGELOG.md` lists them under *New*. Decks and PDFs are unchanged (verified below).
Inside the unreleased package: the built-in `Normal` style no longer sets its size and spacing (the
document defaults carry the same values), heading levels shift, and the brand's table and code
families are now always embedded, because the built-in table and code styles name them.

### Decisions and deviations

- **Body writer.** `DocxBody` replaces `PlainBody` behind `IBodyWriter` (`DocxBody.Instance` makes a new
  writer per document, since it keeps counters and ids). Inline content goes through `DocxRuns`
  (a run model like the PDF's `RunCollector`, then Word elements), lists and headings through
  `DocxNumbering`, footnotes through `DocxFootnotes`, the language through `DocxLanguage`.
- **Roles.** `StyleRole` gained `Kind` (paragraph, character, table) and `Description` (the guide's
  words, which replaced `RoleText`). `DocxStyles` looks a role up among the template's styles of its
  kind; the error for a missing mapped name lists the styles of that kind. Default names: `List Bullet`,
  `List Number`, `List Task`, `Definition Term`, `Definition`, `Quote`, `Code`, `Code Character`
  (character), `Rule`, `caption`, `Table Title`, `Doc Table` (table), `footnote text`,
  `footnote reference` (character), `Hyperlink` (character). Added character and table styles are
  based on nothing.
- **`Normal` is empty in the built-in template**, its 10.5 pt and spacing in `w:docDefaults` (where
  they already were): Word lets a paragraph style's own settings win over a table style's, so the
  table style's 9.5 pt and zero spacing would not reach the cells otherwise. The E09-S01 test that read
  the size from `Normal` now reads it from the defaults.
- **Heading numbers.** On the built-in template the Heading 1–3 styles carry `w:numPr` to the
  multilevel list `numId` 1, which the template builder writes into a new `numbering.xml`
  (`DocxNumbering.BuiltInXml`); `HeadingNumbers = false` / `headingNumbers: false` leaves the `numPr`
  out. The option reaches the template through `DocxTemplateOptions.Write(output, date, headingNumbers,
  codeFont)`. Caller and brand templates: their heading styles decide; styles added to them never get
  numbering (`StyleDesign.HeadingNumbers` false). `{.unnumbered}` is a paragraph `numPr` with `numId` 0
  on every template. `headingNumbers` is a key on every template (it has no effect but on the built-in
  one), so one Markdown serves all.
- **Skipped levels.** Word counts a level that has not occurred as 0, so a `Heading 3` straight after
  `Heading 1` reads `1.0.1`, and in `report.md` (`##` sections before a later `#`) the `##` headings read
  `0.1`, `0.2`. The TOC's pre-filled numbers copy that counting, so they match what Word shows after an
  update. This differs from E03-S02's rule for PDFs ("a skipped level is not counted as 0"); matching it
  in Word would take per-paragraph `ilvl` overrides that desynchronise style and number. The guide tells
  writers not to skip levels. **Question for the human** (see below).
- **`{.unnumbered}` in the shared `HeadingAttributes`:** `Unnumbered` and `SplitDeep` (E03-S02's rule for
  `###`–`######`: a trailing `{…}` comes off only when every token is `.unnumbered`) are known to
  `DocumentFormat.Docx` only, so PDFs and decks still reject `.unnumbered` as before until E03-S02
  enables it for them (`KnowsUnnumbered`). In Word documents `#`/`##` still go through `Split` (slide
  settings checked and dropped, as in E09-S01); on `###`–`######` other braces now stay text, where
  E09-S01's plain body dropped them.
- **Captions (shared).** `Captions` in DocumentGeneration: `OfImage` (title without `scale=` and a
  standalone `caption`; `"caption"` or `.caption` → alt text), `Kind`/`TakePrefix` for `Table: ` /
  `Figure: ` paragraphs, `Label`; `CaptionKind`. Only DOCX calls it, so decks and PDFs are unchanged,
  as is `ImageAttributes`. E03-S02 reuses it and widens `caption-ignored` to PDF and PPTX. Since no
  drawings are drawn yet, every `Figure:` paragraph gets `caption-ignored`. A `.caption` attribute on an
  image counts as asking for the alt text as caption, like the title's `caption`.
- **No blank line, no table.** The plan allows a blank line between a table and its `Table:` line;
  it is in fact required: Markdig turns a pipe table followed directly by a line without `|` into one
  paragraph. Guide and docs say so.
- **Captions' runs.** Label, `SEQ` field and its result, and the colon are bold runs (E03-S02 sets label
  and number bold); the field result is filled in. The caption style is centred, 9 pt; table captions
  get `keepNext`, a captioned image paragraph gets `keepNext`.
- **Lists.** Every item paragraph carries `w:numPr` (`ilvl` 0–2) in `List Bullet` / `List Number`;
  `DocxNumbering` uses the `numPr` of the template's list style when it has one, else adds the built-in
  abstract definitions on first use (bullets `•`, `–`, `▪`; `1.`, `a.`, `i.`; 18 pt a level). So the
  built-in template's `numbering.xml` holds the heading list, and the list definitions are added with the
  first list, not written up front as the plan said. Every ordered Markdown list is a new `w:num` with a
  `w:lvlOverride`/`w:startOverride` at its level. `list-too-deep` once per list nested four deep.
  Further paragraphs of an item keep its indent (`w:ind`), with `numId` 0 only where the style numbers
  itself.
- **Task items** are `List Task` paragraphs with a direct hanging indent of the level, a run-level
  `w:sdt` with `w14:checkbox` (☐/☒, U+2610/U+2612, in Segoe UI Symbol, the font `HarveyBall` uses),
  then a tab. `w14` and `mc:Ignorable="w14"` are declared on the document root when there is a check box
  (merged into a template's own `mc:Ignorable`).
- **Tables.** `Doc Table`: grid lines, 4 pt cell margins, header row bold white on `TableHeader`
  (`tblStylePr firstRow` with `tblHeader`), every second body row in `TableBand` (`band2Horz`); 9.5 pt
  as the PDF's table text. `w:tblW` 5000 pct, fixed layout, `w:tblLook` first row on when the Markdown
  has a header. Widths: `DocxBody.ColumnWidths` is the PDF's rule on `TextMetrics` (minimum longest
  word plus margins, the rest by content), floored to twips with the remainder on the last column, so
  the sum is the text width exactly. `w:cantSplit` unless the estimated row height (lines × 9.5 pt ×
  1.2) exceeds the text height (`long.md`'s tall row). Cells are `Normal` paragraphs with `w:jc` from
  the alignment; images in cells are their alt text.
- **Table titles** lose their run bold (the style is bold). The PDF still shows such a paragraph as bold
  text, as agreed.
- **Images** are inline in a centred `Normal` paragraph (`w:jc` direct, as the plan says), 9525 EMU per
  pixel × scale, fitted to the text width and 80 % of the text height. One image part per resource and
  part. Ids of `wp:docPr` continue after the highest id in the template's document, headers and footers
  (the built-in header pictures use 1001 on). `fit`, `focus`, `.round`, `.grey` give `attribute-ignored`;
  `scale` in both title and attributes throws, as in the PDF. `qr:` images are their alt text until
  E09-S03 draws them (no note: there is no fitting code yet; recorded as a loose end).
- **Footnotes.** The footnotes part is made with separator (−1) and continuation separator (0) and
  `w:footnotePr` in the settings only when the document has a footnote. Each reference gets its own
  Word footnote (Word cannot refer to one footnote twice), so a footnote referred to twice appears twice.
  Links in footnotes get their relationship on the footnotes part.
- **Links.** `http`, `https`, `mailto`, `ftp` URLs become external hyperlinks; `#id` an anchor when a
  heading has that Markdig id; anything else (relative paths) stays text. Bookmark names: the id with
  other characters as `_`, `h_` before a leading non-letter, at most 40 characters, `_2`, `_3` … for
  repeats (Markdig already makes ids unique, `-1` → `_1`). The TOC entries are `w:hyperlink w:anchor`
  around the entry run, with the number and a space before the text.
- **Code** blocks with a renderer that would draw them: `CodeBlockRenderers.Render` is called and its
  graphic thrown away, giving `code-not-drawn`; the renderer's own `Report` is muted. The code style
  uses `contextualSpacing` so a block has its spacing only at the edges.
- **Page breaks.** A `#` heading gets `w:pageBreakBefore` unless it is the first element of the body (the
  cover and the TOC already end with a page break).
- **Deck syntax.** Containers (`LayoutSyntax.CheckContainer`; the columns' `---` dropped), drawn
  containers (`timeline`, …) included, give their content in sequence; alerts a bold title paragraph in
  `Quote` and their content in `Quote`; slide kinds checked per section by `SlideKinds` like the PDF; an
  empty-titled kind heading (`## {.quote}`) is left out. `[words]{.success}` is bold through
  `ColouredWords`. Top-level `::: notes` are checked and left out, nested ones throw; build markers are
  checked by `BuildMarker.Check` and left out. Icons (`:icon-…:`) are not interpreted: left as typed
  (E09-S02).
- **Language.** `w:lang` in `w:rPrDefault` (created if the template has no document defaults) and
  `w:themeFontLang` in the settings (an existing element keeps its `eastAsia`/`bidi`).
- **Notes.** `code-not-drawn`, `html-as-text`, `footnote-missing`, `attribute-ignored` list DOCX now;
  `caption-ignored` and `list-too-deep` are new, DOCX only for now; docs rows added.
- **Converter round trip.** The nine `*.roundtrip.expected.md` were regenerated: headings, lists, tables,
  emphasis, links and images survive; code loses its style (each line a paragraph), footnotes vanish,
  check boxes come back as `☐`/`☒` and a tab, the `Rule` paragraph vanishes. `DocxConverter` classifies
  any style whose id or name contains "title" as a heading, so a `Table Title` paragraph would come back
  as `#`; no sample has one. Left to E08.

### Verification

- `./run.sh build`: 0 warnings, 0 errors (several runs died with "Internal CLR error";
  `dotnet build-server shutdown` and a `-m:1` build fixed it). `./run.sh test` all green:
  1 + 123 + 78 + 260 + 78 + 22 + 71 + 207 + 371 = 1,211 passed, 6 Windows-only skipped.
- **New tests.** `Docx.Tests/ContentTests.cs` (21): a full document on all three templates valid and
  written for review; heading shift and page breaks; heading numbers on by default, off from front
  matter and options, `{.unnumbered}` and the TOC numbers; bookmarks, `#id` links and TOC links;
  external links; run formatting and inline code; list restart, three levels and `list-too-deep`;
  check boxes and definition lists; code, quote and rule styles; tables (title and caption above,
  `SEQ`, style, width, header repeat, alignment, grid sum); column widths; image fitting, alt text,
  unique ids, figure captions below, `caption-ignored`; image-title captions; a missing image; footnote
  ids, part, styles and hyperlinks; `lang: de` (`w:lang`, `w:themeFontLang`, "Tabelle", "Abbildung") and
  `de-CH` on a caller's template; missing content-role styles (paragraph, character, table) added on a
  caller's template; a mapped character style looked up among character styles; deck syntax shown
  plainly; `code-not-drawn`; `html-as-text`. `DocumentGeneration.Tests/CaptionSyntaxTests.cs` (3):
  `.unnumbered` for DOCX only, `SplitDeep`, caption prefixes and labels. E09-S01's tests were updated
  for the level shift, the numbered TOC entries, the empty `Normal`, the embedded table and code
  families, and the `headingNumbers` key.
- **Validator.** Every `.docx` the tests write passes `OpenXmlValidator(Microsoft365)` with no errors:
  the samples × templates, `content-*.docx`, `embedded-fonts-sample.docx`, the guide examples.
- **Unchanged output.** Every PPTX and PDF the tests write to `.tmp/review/`, from E09-S01's commit
  `4e73924` in a separate worktree and from this change: 50 files, all identical — decks part by part
  with the SDK's relationship ids masked (embedded workbooks included), PDFs byte for byte with `/ID`
  and dates masked.
- **For review** in `.tmp/review/`: `report-`, `long-`, `layouts-` × `neutral`, `sample`, `custom`
  `.docx` (with a TOC), `content-neutral.docx`, `content-sample.docx`, `content-custom.docx` (every
  content feature), `embedded-fonts-sample.docx`.

### To verify by hand (no Word or LibreOffice on this machine)

Open the review files in Word (and *File → Export → PDF*), especially `content-*.docx` and
`report-sample.docx`, and check:

1. **No repair prompt** on any file (check boxes with `w14`, footnotes part made by the generator,
   numbering added to a caller's template in `content-custom.docx`).
2. **Headings** numbered `1`, `1.1`, `1.1.1` with a hanging number; Heading 4–6 unnumbered; "Details"
   (`{.unnumbered}`) without a number; "Second chapter" on a new page. Record what Word shows for
   `report-*.docx`'s `##` before `# Outlook` (expected `0.1`, `0.2`) and whether the pre-filled TOC
   numbers match Word's after the field update.
3. **TOC** after updating fields: numbers, dotted leaders, page numbers; clicking an entry jumps.
4. **Lists:** `1. 2.` then the second list starting at `3.`; bullets `•`, `–`, `▪` by level; the task
   boxes tick on a click; the item text aligned after the box.
5. **Tables:** full width, header bold white on the header colour and repeated on the next page
   (`long-*.docx`), banded rows, the tall row split across pages, columns no narrower than their longest
   word, right-aligned numbers; the table title and "Table 1: …" above the table.
6. **Cells:** 9.5 pt without paragraph spacing — this checks the empty `Normal` decision; if Word still
   applies 10.5 pt and 6 pt after in cells, the cell paragraphs need a style of their own.
7. **Images** centred, the large one within the margins, "Figure n: …" centred below and on the same
   page; right-click → *Update field* keeps the numbers.
8. **Footnotes** at the foot of the page, numbered 1, 2, …, with the separator line; the link in the
   footnote works.
9. **Links:** the external link opens, the `#details` link jumps.
10. **Code** grey background as one block, quotes with the bar on the left, the rule as a line.
11. **Language:** *Review → Language* shows English (German for a `lang: de` document) on body text.
12. **LibreOffice:** not installed here; `soffice --headless --convert-to pdf` of the same files not done.

### Loose ends

- **Question for the human:** heading numbers with a skipped level follow Word (`0.1`, `1.0.1`) rather
  than E03-S02's PDF rule. Keep it (documented), or should E03-S02 adopt Word's counting so PDF and
  Word agree?
- `qr:` images and icons are not drawn (E09-S03, E09-S02); `Figure:` captions wait for drawn diagrams
  (E09-S03).
- PDFs and decks do not yet read captions or `{.unnumbered}`; E03-S02 turns on `Captions` and
  `HeadingAttributes.Unnumbered` for them and widens `caption-ignored`.
- `DocxConverter` misreads `Table Title` as a heading and drops footnotes (E08).
