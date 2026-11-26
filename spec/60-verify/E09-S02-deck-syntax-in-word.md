# E09-S02 Deck syntax in Word

> Status: in verification
> Epic: [E09 DOCX generator](../50-todo/E09-docx-generator.md)

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
- **Order.** This story comes after [E09-S01](E09-S01-docx-package-and-templates.md) (package,
  templates, styles by role) and [E09-S04](E09-S04-docx-content.md) (content: headings, lists,
  definition lists, captions, image fitting), which is implemented before it despite its number.
- **Colours come from the brand:**
  - `ResolvedBrand.Highlight` for marks and important alerts;
  - `ResolvedLabelColors` (`Success`, `Info`, `Warning`, `Danger`, `Neutral`) for labels and
    coloured words;
  - `TableBand`, `TableLine` and `Primary` for panels, rules and statements.
  - On a caller template, which has no brand, decks use theme colours (`SlidePalette.Scheme`) and
    the neutral label colours.
- **Decided in the analysis:** a yellow `==mark==` highlight and a status dot are
  not our look; ours stays (behaviour choice 7). Raw HTML stays text.
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
   writer can apply in Word), or a one-cell table per alert, which keeps nested
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
- **Not taken:** a different look (yellow marks, status dots, sizes), audience versions and
  consulting containers.

## Implementation notes

### Breaking changes

None for released output. Decks and PDFs are unchanged (verified below); the DOCX package is
unreleased. What a consumer can notice, all listed in `CHANGELOG.md`:

- `::: pagebreak`, `{.newpage}`, `classification:` and `draft:` used to throw in every generator and
  now generate; decks and PDFs accept them and show nothing of them yet (not breaking, as the plan
  says). Because they are valid now, the lists of valid containers, heading attributes and
  front-matter keys in error messages name them, in every format: code that matches those messages'
  text adjusts (recorded under *Breaking changes*, as E02-S05 did for its names).
- Added: the public enum `Classification`, `DocxOptions.Classification` and `Draft`,
  `GeneratedTexts.Internal`, `Confidential`, `Draft`, `NoteCodes.ClassificationKept`, twenty-four
  `DocxStyleNames` properties and their `docx.styles` properties in `brand.json`.
- Inside the unreleased DOCX package: `==mark==` is the `Mark` style instead of `w:b`, coloured words
  their `Colour …` styles instead of bold, alerts their own styles instead of `Quote` under a bold
  title paragraph, card headings `Container Heading` instead of numbered headings (so they left the
  table of contents and the heading shift), short cards and `::: rows` terms and descriptions, and a
  paragraph of two or more images a table.

### Decisions and deviations

- **Roles.** The 24 roles of the plan, with default names `Note`, `Tip`, `Important`, `Warning`,
  `Caution`, `Mark`, `Label Success` … `Label Neutral`, `Colour Success` … `Colour Neutral`,
  `Container Heading`, `Statement`, `Panel`, `Panel Dark`, `Tag`, `Image Row`, `Classification`,
  `Classification Cover`, and keys `styles.note` … `styles.classificationCover`. One more role without
  a key: Word's own `Strong` (character), which the plan names for the alert title; like the TOC
  levels it is found by name and added when missing, never mapped. All in `DocxRoles.All`, so the
  built-in `styles.xml`, the lookup, the keys and the guide follow.
- **Colours on a caller's template** (`StyleDesign.Theme`, `WordColor`): `w:themeColor`/`w:themeFill`
  accent 1 for the primary colour, accent 2 for the highlight (tint `26` = 15 % for marks, important
  alerts and highlighted rows), background 2 for the band, with the neutral brand's hex as the value
  Word falls back to; label colours and the table line are the neutral brand's hex, as the plan says.
  A brand's own template uses the brand's colours.
- **Direct formatting**, besides the plan's highlighted-row shading: the bar and shading of an alert
  copied onto its list items (the plan: "lists inside get the bar through the same border"), the
  cover title's `w:spacing w:before="0"` under a cover marking (the marking's style takes the space
  above the title, so the title stays at 28 % of the page), and a right indent on the header marking
  of the built-in template beside its logo (18 pt high at the right margin; 6 pt gap).
- **Containers.** Headings inside any `:::` container are `Container Heading` (no outline level, not
  numbered, not in the TOC, left out of the heading shift, bookmark kept), except in a panel, where a
  heading is a bold `Panel` paragraph so the box stays one (the layouts sample's `::: panel dark` has a
  `###`). Cards and rows that are not one list of `**bold**`-first items show their content in
  sequence, like the PDF, rather than throwing as decks do. `conclusion`, `comment` and the drawn
  containers show their content in sequence (E09-S03 draws the drawn ones). A panel's lists, tables
  and definition lists keep their own styles, as agreed (question 3), so a box with a list is broken
  there.
- **Tags** are written for any paragraph of two or more code spans, not only inside containers (decks
  read a top-level tag paragraph too); a list item that starts with a code span gets it as a tag only
  inside a container, as on slides.
- **Labels and highlighted rows** never apply to the header row, as in decks. A label run is the
  code span's text in `Label <Category>`.
- **Image rows:** `ImageRow` table style with `nil` borders and 3 pt cell margins left and right;
  each picture fitted to its cell width less the margins and 80 % of the text height (E09-S04's rule);
  the cells' picture paragraphs are `Normal`, centred directly as E09-S04's images are. A `qr:` image
  in the paragraph keeps the paragraph out of a row (QR codes are still alt text).
- **Page breaks** (`PageBreaks.Flatten`, shared): every `::: pagebreak` is replaced by its content
  right after Markdig parses, in all three generators, before sections are cut, so a heading that an
  unclosed break swallowed starts its section as usual; it returns the blocks a break comes before.
  Word: the first body element written for that block gets `w:pageBreakBefore` (a table: its first
  cell's paragraph); not when it is the first element of the body (the cover and the TOC already end
  their pages), and a break at the end does nothing, so no empty page. `{.newpage}` on any level in
  Word (`SplitDeep` takes `.newpage` with `.unnumbered`); on the cover heading it throws, as E03-S03
  decided for PDFs. Decks and PDFs drop the set: their output is identical (tested and compared).
- **Shared keys** (`classification`, `draft`) are skipped by `FrontMatter.Apply` like `lang`;
  `Marking.Resolve` applies them; PDF and PPTX call it to check the values only. `classification-kept`
  carries the front-matter key's line (`FrontMatter.LineOf`, new). Its formats are DOCX only for now;
  E03-S04 adds PDF and PPTX.
- **Marking in Word** (`DocxMarking`): content controls tagged `classification` are searched in every
  header and footer (the template's body is replaced, so "on the first page" can only mean the first
  page's header); they get the text in the control's own formatting, and then no paragraph is added.
  Otherwise a `Classification` paragraph goes at the top of the default and even headers; a template
  without a default header gets one. The cover marking is always written when there is a cover. The
  watermark run goes into the first paragraph of every header, the cover's included (a first-page
  header is added when the template has `w:titlePg` without one); shape `PowerPlusWaterMarkObject<n>`,
  `_x0000_t136` text path, 90 % of the text width, rotation 315, `#D9D9D9`, opaque, in the brand's font.
  `cp:contentStatus` through the new `CoreProperties.ContentStatus` (linked into PPTX too, unused there).
- **Guides.** `Docx.md` gets *Syntax written for slides*, *Page breaks*, *Classification*, image rows
  and the two keys in the front-matter table. The PDF and deck guides each get one sentence that page
  breaks and the marking keys are accepted and ignored (their tests require every container to be
  named); their expected guides changed accordingly (guides only, no output).
- **Test template.** `CustomTemplate` now also lacks every deck-syntax style, so the caller's-template
  tests show them added in theme colours.
- **QR codes and icons** are not in this plan, so they stay as E09-S04 left them (alt text, typed
  text) and go to E09-S03.

### Verification

- `./run.sh build`: 0 warnings, 0 errors. `./run.sh test` all green: 1 + 123 + 276 + 78 + 71 + 22 +
  109 + 208 + 372 = 1,260 passed, 6 Windows-only skipped.
- **New tests.** `Docx.Tests/DeckSyntaxTests.cs` (28 with theory rows): each alert kind's style and
  `Strong` title in English and German, lists inside with the bar; alert style colours; card headings
  without outline level or numbering and not in the TOC, the figure in `Statement`; short cards and
  rows as terms; panels, dark panels, statements, columns, tag paragraphs and tag items; marks,
  coloured words and highlighted rows; a named label and an unnamed code cell; the image row
  (style, `cantSplit`, equal widths, pictures and captions in the cells, `nil` borders); page breaks
  closed, holding content, unclosed, after a table, and `{.newpage}` on `##` and `###`; no break at the
  start or end; mistakes (`::: pagebreak now`, `{.newpage}` on the cover, unknown container and alert,
  bad `classification` and `draft`); confidential in the content header (with the logo indent, not in
  the cover header), on the cover and in `cp:contentStatus`; a German internal draft with the
  watermark in every header; precedence with `classification-kept` and its line; a tagged content
  control filled on a caller's template, else a header paragraph; the deck styles in theme colours on a
  caller's template; each classification valid on every template. `ContentTests` checks the layouts
  sample's styles. The new sample `pages.md` (page breaks closed, with content and unclosed, an image
  row, a warning with a list, labels, `classification: confidential`, `draft: true`) is generated with
  neutral, sample and custom, validated and compared through `DocxConverter`
  (`pages-<template>.roundtrip.expected.md`). `DocumentGeneration.Tests/DocumentSyntaxTests.cs`:
  `PageBreaks.Flatten` (order, the blocks after breaks, at the end, inside a container, arguments in
  every format), `.newpage` in every format and on deep headings for Word only, `Marking.Resolve` and
  its texts, the keys skipped by `Apply`. PDF and PPTX: a document with page breaks, `{.newpage}` and
  the marking keys gives the same pages' text (PDF) and the same slide XML (PPTX, relationship ids
  masked) as the one without.
- **Validator.** Every `.docx` the tests write passes `OpenXmlValidator(Microsoft365)`, VML watermark
  included.
- **Round trip.** The `layouts-*` expectations were regenerated: alerts come back as a paragraph
  starting with the title, card headings as `#` (the converter reads any style whose name contains
  "heading" as a heading), figures, marks and labels as plain text, short cards and rows as term and
  description paragraphs; image rows come back as a table of images with their captions. Left to E08.
- **Unchanged output.** All 50 decks and PDFs the PPTX and PDF tests write to `.tmp/review/`, from
  E09-S04's commit `020b5aa` (run before any change) and from this change: identical — decks part by
  part with the SDK's relationship ids masked, embedded workbooks included, PDFs byte for byte with
  `/ID` and dates masked.
- **For review** in `.tmp/review/`: `layouts-`, `pages-`, `content-`, `report-`, `long-` ×
  `neutral`, `sample`, `custom` `.docx`, and `embedded-fonts-sample.docx`.

### To verify by hand (no Word or LibreOffice on this machine)

Open `layouts-*.docx` and `pages-*.docx` in Word (and *File → Export → PDF*), and check:

1. **No repair prompt** on any file, especially `pages-custom.docx` (styles added in theme colours,
   the marking paragraph in a caller's header) and every `pages-*` (VML watermark).
2. **Alerts:** the bar on the left is continuous across the alert's paragraphs and its list
   (`pages-*`: the warning with two items); record whether the bar jogs at the list's indent.
3. **Panels** (`layouts-*`, "Fixed along the way", "Every result has a manifest"): consecutive panel
   paragraphs form one box; the dark panel is the primary colour with white text.
4. **Labels and marks** look like the decks': the `Done`/`In review`/`Next` chips in "Status and next
   steps", the marked words, the shaded rows of "Status model".
5. **Image rows** (`pages-*`, "Daily run"): the two images side by side with their captions under
   them, no borders, the row not split.
6. **Page breaks** fall where asked (`pages-*`: "Incidents", "Contacts", "Appendix" each start a page,
   no empty page).
7. **Classification:** "CONFIDENTIAL · DRAFT" right-aligned in the header of every content page, left
   of the logo on `pages-sample.docx`, in the danger colour; above the cover title, the title still
   about a quarter down; *File → Info* shows the status.
8. **Watermark:** "DRAFT" diagonally behind the text on every page, the cover included;
   *Design → Watermark* lists it, *Remove Watermark* removes it.
9. **Container headings:** the card titles are bold, unnumbered and absent from the table of contents
   after *Update field*.
10. **LibreOffice:** not installed here; the watermark and the joining of borders were not checked.

### What E03-S03 and E03-S04 still do

- **E03-S03:** read `PageBreaks.Flatten`'s set in the PDF (a `PageBreakBlock` or the heading's
  `NewPage`), never an empty page; `{.newpage}` on `###`–`######` in PDFs (`SplitDeep`); `{.newpage}`
  on the PDF cover heading an error; the guides' page-break sections; and footnotes and `justify`.
- **E03-S04:** `PdfOptions`/`PptxOptions` `Classification` and `Draft`, passed to `Marking.Resolve`
  instead of `Public`/`false`; the PDF header and cover marking and the rotated watermark; the deck
  corner label and `cp:contentStatus` (`CoreProperties.ContentStatus` is ready); `classification-kept`
  for PDF and PPTX in `NoteCodes.All`; the guides' front-matter rows and docs placement.

### Loose ends

- QR codes (`qr:` images) and icons (`:icon-name:`) are still alt text and typed text in Word: E09-S03.
- `DocxConverter` reads `Container Heading` as a heading and drops the formatting styles give (E08).
