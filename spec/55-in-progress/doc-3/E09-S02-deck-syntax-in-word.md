# E09-S02 Deck syntax in Word

> Status: in progress (doc-3)
> Epic: [E09 DOCX generator](../../50-todo/E09-docx-generator.md)

## Summary

The DOCX generator draws the layout syntax written for decks with Word styles instead of showing
it plainly:

- containers are written in sequence with their own styles;
- alerts become note styles;
- `==mark==`, coloured words and status labels become character styles;
- a paragraph of only images becomes a borderless table.

It also adds the Word side of two features whose syntax other stories own: page breaks
(`::: pagebreak`, `{.newpage}`, owned by E03-S03) and classification marking (`classification:` and
`draft:`, owned by E03-S04) in the header, on the cover, and a draft watermark.

## Context

- **The syntax** (docs, *Slide layouts in Markdown*):
  - heading attributes (`{.dark kicker="…" layout="…"}`, `{.closing}`);
  - containers `cards`, `rows`, `panel`, `statement`, `columns`, checked by
    `LayoutSyntax.CheckContainer`;
  - alerts `NOTE`, `TIP`, `IMPORTANT`, `WARNING`, `CAUTION` (`LayoutSyntax.IsImportant`);
  - tag paragraphs of code spans, `==mark==`, and highlighted table rows (first cell all `==…==`);
  - status labels: a table cell that is one code span, given a category by front matter
    `labels.*` and `FrontMatter.Labels()`;
  - `[words]{.success}` (`ColouredWords.Apply`);
  - image rows (a paragraph of only images; the PDF's `ImageRowBlock`).
- **How the PDF shows it:** plainly. A card's `###` is a heading, the columns follow one another,
  an alert is a quote with its title in bold (`GeneratedTexts.Alert`), a mark is bold and labels
  stay code.
- **What E09-S04 does with it:** the same as the PDF, so every sample generates. This story
  replaces that with Word styles.
- **Order.** This story comes after [E09-S01](../../60-verify/E09-S01-docx-package-and-templates.md) (package,
  templates, styles by role) and [E09-S04](E09-S04-docx-content.md) (content: headings, lists,
  definition lists, captions, image fitting), which is implemented before it despite its number.
- **Colours come from the brand:**
  - `ResolvedBrand.Highlight` for marks and important alerts;
  - `ResolvedLabelColors` (`Success`, `Info`, `Warning`, `Danger`, `Neutral`) for labels and
    coloured words;
  - `TableBand`, `TableLine` and `Primary` for panels, rules and statements.
  - On a caller template, which has no brand, decks use theme colours (`SlidePalette.Scheme`) and
    the neutral label colours.
- **Decided in the analysis:** the fork's yellow `==mark==` highlight and its status dot are its
  client's look; ours stays (difference 7). Raw HTML stays text.
- **The epic's rule:** paragraphs reference template styles by name only, and there is no direct
  formatting except in code.
- **Shared syntax owned by other stories:**
  - Page breaks: `::: pagebreak` and `{.newpage}` on a heading — E03-S03. Decided there:
    `::: pagebreak` takes no arguments, may hold content, and its closing `:::` is optional.
  - Classification: two keys, `classification: public | internal | confidential` (default
    `public`, which shows nothing) and `draft: true`, decided in E03-S04. The higher of host and
    front matter wins (`classification-kept` otherwise), the texts are `GeneratedTexts.Internal`,
    `Confidential` and `Draft`, joined with " · ", the colour is `Labels.Danger` for
    confidential and the footer colour otherwise, and the file metadata is `cp:contentStatus` in
    decks and DOCX (nothing in PDFs).
  - This story writes the Word side on what those stories decided; E03-S03 and E03-S04 write
    PDF and PPTX.

## Proposed approach

1. **Roles.**
   - `DocxStyleNames` (E09-S01, with E09-S04's content roles) gains:
     - `Note`, `Tip`, `Important`, `Warning`, `Caution` (paragraph styles);
     - `Mark` (character);
     - `LabelSuccess`, `LabelInfo`, `LabelWarning`, `LabelDanger`, `LabelNeutral` (character
       styles for labels), and `ColourSuccess` … `ColourNeutral` (character styles for coloured
       words);
     - `ContainerHeading` (paragraph), `Statement` (paragraph), `Panel` (paragraph), `Tag`
       (character), `ImageRow` (table style);
     - `Classification` (paragraph) and `ClassificationCover` (paragraph).
   - **Built-in template.** They are written from the brand, and the `styles.<role>` front-matter
     keys take them.
   - **Caller templates** that lack them get them created on demand, as for every role. Their
     colours then come from the template's theme (accent 1, accent 2, background 2) and the
     neutral brand's label colours, as on decks.
2. **Containers, in sequence.**
   - **`::: cards`.**
     - Each `###` becomes a `ContainerHeading` paragraph: bold, in `Primary`, unnumbered, not in
       the TOC (no outline level), and kept with the next paragraph.
     - A paragraph of only bold text right after it is a figure, written in `Statement`.
     - The short form (a list of `- **Title** – text`) becomes a definition list (E09-S04): term,
       then description.
   - **`::: rows`.** The rows become the same definition-list look, a bold term with the text
     indented under it (question 4).
   - **`::: panel`.** Its paragraphs are written in `Panel`, with shading in `TableBand` and a
     border all round, so Word joins consecutive panel paragraphs into one box.
     - **`panel dark`:** `Panel` shaded in `Primary` with white text, as a second style,
       `PanelDark`.
     - Lists and tables inside keep their own styles (question 3).
   - **`::: statement`.** The first paragraph is in `Statement` (large, bold, `Primary`), and the
     rest follows as normal blocks.
   - **`::: columns`.** The columns follow one another, without the `---` between them
     (question 3).
   - **Tag paragraphs** of code spans become one paragraph of `Tag` runs (code font, shaded light
     grey), separated by spaces. A list item that starts with a code span inside a container gets
     that span as a `Tag`.
   - **Heading attributes** are dropped as in E09-S04, except `{.newpage}` (step 6). `layout="…"`
     means nothing in Word and is checked and dropped, as in the PDF.
3. **Alerts as note styles.**
   - **The styles.** Each kind is a paragraph style with a left bar (border) and light shading:
     - `Note` and `Tip`: the bar in `Primary`, the shading in `TableBand`;
     - `Important`, `Warning` and `Caution`: the bar in `Highlight`, the shading in a light tint of
       it.
   - **The content.** The alert's first paragraph starts with its title from
     `GeneratedTexts.Alert`, in the built-in `Strong` character style, then a space. Every
     paragraph of the alert is in the kind's style, so the bar is continuous.
   - **Lists inside** keep their list style and get the bar through the same border.
   - This is a style per kind, not a one-cell table (question 1).
4. **Marks, coloured words, status labels.**
   - **`==text==`** gets the `Mark` character style: bold, run shading in a light tint of
     `Highlight`, as decks show it. It is not Word's yellow highlighter.
   - **Highlighted table rows.** When the first cell is all `==…==`, every cell of the row is
     shaded in that tint. Cell shading cannot come from a character or paragraph style, so this is
     the one direct cell formatting (question 6).
   - **`[words]{.success}`** gets `ColourSuccess` (bold, in the category colour) and so on.
   - **Status labels.**
     - A table cell that is exactly one code span, which front matter `labels.*` names, becomes a
       `Label<Category>` run: bold, in the category colour, on a light tint of it as run shading,
       like a chip.
     - A code-only cell no key names stays code (question 5).
5. **Image rows** (a paragraph of only images, two or more):
   - **The table.** A borderless table of one row, one cell per image, in the `ImageRow` table
     style (no borders, no cell shading), with equal column widths across the text width.
   - **The images.** Each is scaled down to its cell (E09-S04's fitting rules). Its caption, from
     its title (E09-S04, E03-S02), sits under it in the same cell in `Caption`, with its own
     `SEQ Figure` number, as figure captions go below their figure.
   - **On the page.** The row does not split across pages (`w:cantSplit`).
6. **Page breaks** (the syntax from E03-S03):
   - **`::: pagebreak`** gives `w:pageBreakBefore` to the next paragraph or table — its own first
     block when it holds content, which E03-S03 allows; its content then follows as normal
     blocks. Without a closing `:::` it runs to the end of the document, with the same pages. At
     the very end of the document it does nothing.
   - **`{.newpage}`** on a heading gives that heading `w:pageBreakBefore`.
   - **Shared checks.** `LayoutSyntax.CheckContainer` accepts `pagebreak` (no arguments; content
     allowed, closing optional, as E03-S03 decided), and `HeadingAttributes` accepts `.newpage`. Whichever of this story and
     E03-S03 comes first adds them to the shared package. Until E03-S03, PDF and PPTX accept them
     and ignore them, which is not a breaking change: today they throw.
   - `w:pageBreakBefore` is a paragraph property, the same one E09-S04 uses for a later `#`
     (question 7).
7. **Classification marking** (the keys from E03-S04):
   - **The keys.** `classification` and `draft` as E03-S04 defines them, with
     `DocxOptions.Classification` and `DocxOptions.Draft` as the host's defaults and the same
     "higher one wins" rule.
   - **The texts** are the `GeneratedTexts` properties E03-S04 adds (`Internal`, `Confidential`,
     `Draft`), unless this story lands first and adds them itself, joined as E03-S04's
     `Marking.Text` does ("Confidential · Draft").
   - **In the header** of every content page, the text in `Classification`: right-aligned, bold
     caps, in the colours E03-S04 decided: `Labels.Danger` for confidential, the footer colour
     for internal and draft. `public` without a draft shows nothing.
   - **On the cover,** a `ClassificationCover` paragraph above the title, as in the PDF (E03-S04;
     the human's decision, 2026-11-14).
   - **`draft: true`** also gets a watermark, as the PDF does (decks get none): Word's own kind, a
     VML text path (`GeneratedTexts.Draft` in capitals, "DRAFT" / "ENTWURF", light grey, 45°) in
     each header, so Word shows it under *Design → Watermark* and can remove it there
     (question 9).
   - **On caller templates** (question 8):
     - a content control tagged `classification` in a header or on the first page gets the text;
     - else the text is a paragraph added at the top of each header.
   - **Document property.** `cp:contentStatus` holds the marking text, as E03-S04 decided for
     decks and DOCX (PDFs get none). A document without a marking writes none. Sensitivity
     labels of a tenant are out of scope.
   - **Precedence.** Front matter can raise the host's classification but not lower it; trying
     gives `classification-kept`, to whose formats Docx is added.
8. **Notes.** No new codes; Docx joins `classification-kept` (step 7). An image row whose images
   get too small in their cells is not reported, as in the PDF.
9. **Tests.**
   - **Samples.**
     - `layouts.md` is generated with neutral, sample and custom: validator clean, compared through
       `DocxConverter` (`layouts-<template>.roundtrip.expected.md`). Note paragraphs come back as
       paragraphs, and image rows as a table of images.
     - A new sample section, linked or added to `layouts.md`, has page breaks and each
       classification value.
   - **Unit tests:**
     - each alert kind uses its style, and the title is in `Strong` in the language's text;
     - card headings are not in the TOC (no outline level) and unnumbered;
     - `==mark==` uses `Mark`, and highlighted rows have shaded cells;
     - a label named in `labels.success` uses `LabelSuccess`, and an unnamed code cell stays code;
     - coloured words use their styles;
     - an image row is a borderless table with equal widths and captions in the cells;
     - `::: pagebreak` and `{.newpage}` set `pageBreakBefore`, and `::: pagebreak x` throws;
     - a `::: pagebreak` holding content breaks before its first block, closed or not;
     - classification text in every header, on the cover and in `cp:contentStatus`, and
       precedence with `classification-kept`;
     - a draft watermark in each header;
     - the classification fills a tagged content control in a caller template;
     - unknown containers and alerts still throw.
   - **Unchanged elsewhere.** PDF and PPTX samples are unchanged by the shared `pagebreak` and
     `.newpage` acceptance.
10. **Authoring guide, docs, changelog.**
    - **Guide.** `Docx.md` gets *Syntax written for slides* (what each piece becomes in Word),
      *Page breaks* and the `classification` key. The expected guides are regenerated.
    - **Docs.** The Docx page gets the role table additions, the deck syntax → Word table, page
      breaks and classification. The DocumentGeneration page lists `pagebreak` and `.newpage` in
      the shared syntax, unless E03-S03 did.
    - **Changelog.** `CHANGELOG.md` *New*: deck syntax in Word, page breaks, classification. If this
      lands before E03-S03, `::: pagebreak` and `{.newpage}` are new syntax that decks and PDFs
      accept and ignore for now.
11. **Verification in real Word and LibreOffice.**
    - **Word.** The layouts sample is opened through the Word export of E09-S01. The check
      confirms:
      - the note bars are continuous across paragraphs;
      - panels form one box;
      - label chips and marks look like the decks';
      - image rows sit side by side;
      - page breaks fall where asked;
      - the draft watermark shows and can be removed through *Design → Watermark*.
    - **LibreOffice.** The same is checked in LibreOffice where it is installed (watermark and
      border joining in particular), or recorded as not available.

## Acceptance criteria

- [ ] **Alerts.** Each alert is a block in its kind's style, with a continuous left bar and its
  title in the document language. Important, warning and caution use the highlight colour.
- [ ] **Cards.** A card's `###` is a container heading, not numbered and not in the TOC.
- [ ] **Other containers.**
  - `::: rows` and short-form cards read as term and description.
  - `::: panel` is one shaded box, and `panel dark` the primary colour with white text.
  - `::: statement` starts with a large bold paragraph.
  - `::: columns` follow one another.
  - Tag paragraphs are chips in the code font.
- [ ] **Marks and colours.**
  - `==text==` is bold on the highlight tint, through the `Mark` style.
  - A highlighted table row is shaded.
  - `[words]{.warning}` is bold in the warning colour.
  - A cell `` `Done` `` with `labels.success: Done` is a green chip.
- [ ] **Image rows.** A paragraph of two or more images is a borderless one-row table with equal
  cells, captions under the images, and it does not split.
- [ ] **Page breaks.** `::: pagebreak` and `## Heading {.newpage}` start a new page in Word. PDF and
  PPTX samples are unchanged.
- [ ] **Classification.**
  - `classification: confidential` shows "Confidential" in every content-page header and on the
    cover, and sets `cp:contentStatus`.
  - `draft: true` adds a watermark that Word lists as its own.
  - `lang: de` gives "Vertraulich" and "Entwurf".
- [ ] **Caller templates** get the missing styles created, and a tagged content control receives
  the classification.
- [ ] **Checks.**
  - The outputs pass the Open XML validator.
  - `layouts.md` round-trips through `DocxConverter` as committed.
  - The documents open in Word without a repair prompt. LibreOffice is checked, or recorded as not
    available.
- [ ] **Docs.** The guide, the docs, the CHANGELOG and `ai/memory.md` are updated, and all tests
  pass.

## Open questions

1. Alerts: a paragraph style per kind with a left border and shading (proposed: plain styles a
   writer can apply in Word, as the fork does), or a one-cell table per alert, which keeps nested
   lists and tables inside the box more reliably?

> Answered by the human (2026-11-14): as proposed.

2. Headings inside containers (a card's `###`): an unnumbered `ContainerHeading` outside the TOC
   (proposed: a card title is not a chapter), or ordinary numbered headings, as the PDF shows
   them?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. `::: columns` and `::: panel` with tables or lists inside: columns in sequence and panels as
   shaded paragraphs (proposed, simple and editable), or a borderless table for columns and a
   one-cell table for panels, which looks closer to the slide?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. `::: rows`: the definition-list look, with the term bold and the text under it (proposed), or a
   two-column borderless table, with the term left and the text right as on the slide?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Status labels: only code-only cells that front matter `labels.*` names (proposed: Word has no
   flow slides to limit them, and every other code cell stays code), or every code-only table cell
   as a neutral label, as on flow slides?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Highlighted table rows: shade the cells directly (proposed: the only way, and a documented
   exception to "no direct formatting"), or show the first cell's `==…==` as `Mark` text only,
   without row shading?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Page breaks: `w:pageBreakBefore` on the next paragraph (proposed: no empty paragraph, and it
   moves with the paragraph when edited), or a paragraph holding a page-break run
   (`w:br w:type="page"`), which Word users see as a visible page break mark?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Classification on caller templates: fill a content control tagged `classification`, else add a
   paragraph at the top of each header (proposed)? Or only the content control, with nothing shown
   if the template lacks one?

> Accepted as proposed (the human, 2026-11-14, without discussion).

9. The draft watermark: Word's own VML text watermark (proposed: Word manages and removes it in its
   UI, and LibreOffice shows it), or a DrawingML WordArt shape, which validates more strictly but
   Word does not treat as its watermark?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- **Depends on E09-S01:** package, templates, styles by role and their lookup and creation.
- **Depends on E09-S04:** the content roles and styles, headings, definition lists, captions and
  image fitting. E09-S04 is implemented before this story despite its number.
- **Shared with E03-S03 and E03-S04:**
  - the syntax (`::: pagebreak`, `{.newpage}`) — E03-S03;
  - the keys `classification` and `draft`, their values and texts — E03-S04.
  - The analysis orders E09 before E03, so this story will probably add the shared pieces. The
    names and rules are the ones decided in the E03 plans, and those stories then write PDF and
    PPTX on them.
- **Not taken from the fork:** its client's look (yellow marks, status dots, sizes), audience
  versions and consulting containers.
