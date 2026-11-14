## Writing Markdown for a Word document

The Word document is generated from Markdown: the design (styles, fonts, logo, header and footer,
page numbers) comes from the template, applied for you. Write it as a document to read: full
sentences and paragraphs, headings that say what a section is about.

## Structure

| Markdown | Becomes |
| -------- | ------- |
| A `#` heading as the very first line | The cover page. The paragraphs below it, up to the next heading, are the subtitle; the author and the date follow. Without it, there is no cover. |
| The highest heading level after the cover | Heading 1, and each level below it the next heading style: with `#` chapters, `#` is Heading 1 and `##` Heading 2; with only `##` sections, `##` is Heading 1. |
| A later `#` heading | Starts a new page. |
| Text before the first heading | Normal text on the first page; the document then has no cover. |

- Every paragraph uses a style of the template. Do not try to format text by other means; bold,
  italic and strike-through are the only formatting inside a paragraph.
- Write headings in order, without skipping a level: where headings are numbered, a `###` right
  under a `#` is numbered `1.0.1`.

## Heading numbers

The template's heading styles decide whether headings are numbered; `headingNumbers` has no effect.

- `{.unnumbered}` at the end of a heading gives it no number, and it does not count:
  `## Preface {.unnumbered}`.

## Table of contents

`toc: true` in the front matter puts a table of contents after the cover, on a page of its own,
headed "Contents" in the document's language (`lang`). It lists the headings down to the level
`tocDepth` gives (default `2`), with their numbers. Each entry links to its heading. Word fills in
the page numbers when the document is opened; the headings are listed even where nothing updates
it.

## Text, lists and quotes

| Markdown | Becomes |
| -------- | ------- |
| `**bold**`, `*italic*`, `~~strike~~` | Bold, italic, struck-through text. |
| `` `code` `` | Inline code in the code font. |
| `[text](https://example.com)` | A link. `[text](#heading-id)` jumps to a heading of the document; its id is the heading text in lower case, spaces as `-`. Other links stay text. |
| `- item`, `1. item` | Bulleted and numbered lists, nested up to three levels. Each numbered list starts at its own first number. |
| `- [ ] task`, `- [x] done` | Task items with a check box that can be ticked in Word. |
| `Term` and a line `:   description` | A definition list: the term in bold, the description indented below it. |
| `> quote` | A quote; quotes inside quotes are shown as one. |
| A fenced code block | Code in the code font, line by line. |
| `---` | A horizontal rule. |
| `Text[^1]` and `[^1]: Note.` | A footnote at the foot of the page, numbered by Word. |

- Lists nested deeper than three levels are set at the third level.
- HTML is not interpreted: it is shown as text.

## Tables

- A table spans the width of the text. Each column is at least as wide as its longest word; the
  rest is shared by how much each column holds.
- The first row is the header, repeated at the top of every page the table runs onto.
- Alignment from the separator row (`:---`, `:---:`, `---:`) is kept.
- A paragraph of nothing but bold text right before a table is the table's title:
  `**Sales by region**`.
- A paragraph starting `Table: ` right after a table, after a blank line (without one the table is
  not a table), is its caption, numbered through the document and set above the table: `Table: Sales in 2026` gives
  "Table 1: Sales in 2026". The label is in the document's language; `Table:` itself stays
  English.
- A `Table: ` paragraph anywhere else is shown as text.

## Images

- `![Alt text](image.png)` places the image centred in a paragraph of its own, at its natural size,
  made smaller to fit the page, never larger. `"scale=0.5"` in the title scales it first. Always
  write alt text.
- The image's title is its caption, numbered through the document and set below it:
  `![Sales](sales.png "Sales by region, 2026")` gives "Figure 1: Sales by region, 2026".
  `"caption"` alone uses the alt text as the caption. A title of only `scale=…` gives no caption.
- Images are PNG, JPEG or GIF; an `.svg` image is an error here.
  `fit=`, `focus`, `.round` and `.grey` have no effect in a Word document.

- A paragraph of nothing but two or more images is a row of them, side by side in cells of equal
  width, each with its caption under it: `![North](north.png "North") ![South](south.png "South")`.

## Syntax written for slides

Markdown written for slides works here too, so one text can serve a deck and a document. Each piece
takes a style of its own; the content of a container follows in sequence.

| Markdown | Becomes |
| -------- | ------- |
| `> [!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]`, `[!CAUTION]` | A block with a bar on the left, starting with its title in bold in the document's language. Important, warning and caution are in the highlight colour. |
| `::: cards` with a `### Title` before each card | Each title a small bold heading, not numbered and not in the table of contents; a paragraph of only bold text right under it is the card's figure, set large. |
| `::: cards` or `::: rows` around a list of `- **Title** – text` | Each title in bold, its text indented under it, as a definition list. |
| `::: panel` … `:::` | Its paragraphs in one shaded box; `::: panel dark` in the primary colour with white text. Lists and tables inside keep their own look. |
| `::: statement` … `:::` | The first paragraph large and bold; the rest as usual. |
| `::: columns` … `:::` | The columns one after the other. |
| `` `tag` `other` `` (a paragraph of only code spans) | Tags: small grey chips in the code font. |
| `==marked==` | Bold on a light tint of the highlight colour. A table row whose first cell is all `==…==` is shaded. |
| `[words]{.success}` (`.info`, `.warning`, `.danger`, `.neutral`) | The words bold in that colour. |
| A table cell that is only `` `Done` ``, with `labels.success: Done` in the front matter | A coloured label. Code in cells no `labels.` key names stays code. |
| `::: conclusion`, `::: comment` | Their content in sequence. |
| `## Title {kicker="…" layout="…"}`, `{.dark}`, transitions, `::: notes`, build markers | Checked as in decks, then left out. |

Unknown containers, alerts and heading attributes are errors.

## Diagrams, charts and QR codes

No code block is drawn in this setup: every one stays code.

- **Diagrams and other drawings are not drawn in this setup:** a diagram block stays code, a
  `qr:` image is its alt text, and `::: timeline`, `::: gantt`, `::: steps` and
  `::: riskmatrix` show their content in sequence. Leave them out of a document meant for Word.

## Icons

`:icon-name:` (such as `:icon-circle-check:`) is not drawn in this setup: the icon is left
out. Leave icons out of a document meant for Word. An unknown name is an error.

## Page breaks

`::: pagebreak` on a line of its own, closed by `:::`, starts a new page with what follows. It may
hold content, which then starts the new page, and its closing `:::` may be left out. `{.newpage}` at
the end of a heading starts a new page with that heading: `## Appendix {.newpage}`. A later `#`
already starts a page. No break makes an empty page, and the cover heading takes no `{.newpage}`.

```markdown
::: pagebreak
:::

## Appendix
```

## Classification

`classification: internal` or `classification: confidential` in the front matter puts "Internal" or
"Confidential" (in the document's language) in the header of every page and above the cover title,
and into the document's properties. `draft: true` adds "Draft" and a watermark on every page. The
host may already mark the document; the front matter can raise its classification, not lower it.

## Front matter

Settings go in a block at the very top, before anything else. One `key: value` per line, no
nesting and no lists. Leave out what the defaults already do: every key is optional.

```markdown
---
title: Network expansion 2027
author: Planning team
---
```

| Key | Values |
| --- | ------ |
| `title` | The document title in the file's properties. Default: the first `#` heading. |
| `author` | The author in the file's properties. |
| `lang` | The document language, such as `en`, `de` or `de-CH`: of the texts the generator writes itself, and of spell checking. Default `en`. |
| `template` | The design: `neutral`. |
| `styles.normal` | The name of the template's style for body text, as Word shows it. |
| `styles.title` | The name of the template's style for the cover title, as Word shows it. |
| `styles.subtitle` | The name of the template's style for the paragraphs under the cover title, as Word shows it. |
| `styles.coverDetails` | The name of the template's style for the author and date on the cover, as Word shows it. |
| `styles.tocHeading` | The name of the template's style for the heading of the table of contents, as Word shows it. |
| `styles.heading1` | The name of the template's style for headings of level 1, as Word shows it. |
| `styles.heading2` | The name of the template's style for headings of level 2, as Word shows it. |
| `styles.heading3` | The name of the template's style for headings of level 3, as Word shows it. |
| `styles.heading4` | The name of the template's style for headings of level 4, as Word shows it. |
| `styles.heading5` | The name of the template's style for headings of level 5, as Word shows it. |
| `styles.heading6` | The name of the template's style for headings of level 6, as Word shows it. |
| `styles.listBullet` | The name of the template's style for items of bulleted lists, as Word shows it. |
| `styles.listNumber` | The name of the template's style for items of numbered lists, as Word shows it. |
| `styles.listTask` | The name of the template's style for items of task lists, which start with a check box, as Word shows it. |
| `styles.definitionTerm` | The name of the template's style for the terms of definition lists, as Word shows it. |
| `styles.definition` | The name of the template's style for the descriptions of definition lists, as Word shows it. |
| `styles.quote` | The name of the template's style for quotes and alerts, as Word shows it. |
| `styles.code` | The name of the template's style for each line of a code block, as Word shows it. |
| `styles.codeCharacter` | The name of the template's style for inline code (a character style), as Word shows it. |
| `styles.rule` | The name of the template's style for horizontal rules, an empty paragraph with a bottom border, as Word shows it. |
| `styles.caption` | The name of the template's style for the captions of figures and tables, as Word shows it. |
| `styles.tableTitle` | The name of the template's style for the title of a table, a paragraph of bold text right before it, as Word shows it. |
| `styles.table` | The name of the template's style for tables (a table style), as Word shows it. |
| `styles.footnoteText` | The name of the template's style for the text of footnotes, as Word shows it. |
| `styles.footnoteReference` | The name of the template's style for footnote references (a character style), as Word shows it. |
| `styles.hyperlink` | The name of the template's style for links (a character style), as Word shows it. |
| `styles.note` | The name of the template's style for alerts of the kind `[!NOTE]`, as Word shows it. |
| `styles.tip` | The name of the template's style for alerts of the kind `[!TIP]`, as Word shows it. |
| `styles.important` | The name of the template's style for alerts of the kind `[!IMPORTANT]`, as Word shows it. |
| `styles.warning` | The name of the template's style for alerts of the kind `[!WARNING]`, as Word shows it. |
| `styles.caution` | The name of the template's style for alerts of the kind `[!CAUTION]`, as Word shows it. |
| `styles.mark` | The name of the template's style for `==marked==` text (a character style), as Word shows it. |
| `styles.labelSuccess` | The name of the template's style for status labels of the category success (a character style), as Word shows it. |
| `styles.labelInfo` | The name of the template's style for status labels of the category info (a character style), as Word shows it. |
| `styles.labelWarning` | The name of the template's style for status labels of the category warning (a character style), as Word shows it. |
| `styles.labelDanger` | The name of the template's style for status labels of the category danger (a character style), as Word shows it. |
| `styles.labelNeutral` | The name of the template's style for status labels of the category neutral (a character style), as Word shows it. |
| `styles.colourSuccess` | The name of the template's style for `[words]{.success}` (a character style), as Word shows it. |
| `styles.colourInfo` | The name of the template's style for `[words]{.info}` (a character style), as Word shows it. |
| `styles.colourWarning` | The name of the template's style for `[words]{.warning}` (a character style), as Word shows it. |
| `styles.colourDanger` | The name of the template's style for `[words]{.danger}` (a character style), as Word shows it. |
| `styles.colourNeutral` | The name of the template's style for `[words]{.neutral}` (a character style), as Word shows it. |
| `styles.containerHeading` | The name of the template's style for headings inside containers, such as a card's `###`: unnumbered and not in the table of contents, as Word shows it. |
| `styles.statement` | The name of the template's style for the statement of `::: statement` and a card's figure, as Word shows it. |
| `styles.panel` | The name of the template's style for the paragraphs of `::: panel`, as Word shows it. |
| `styles.panelDark` | The name of the template's style for the paragraphs of `::: panel dark`, as Word shows it. |
| `styles.tag` | The name of the template's style for tags, code spans shown as chips (a character style), as Word shows it. |
| `styles.imageRow` | The name of the template's style for a row of images side by side (a table style), as Word shows it. |
| `styles.classification` | The name of the template's style for the classification marking in the header, as Word shows it. |
| `styles.classificationCover` | The name of the template's style for the classification marking above the cover title, as Word shows it. |
| `toc` | `true` or `false`: a table of contents after the cover. Default `false`. |
| `tocDepth` | `1`, `2` or `3`: the heading levels the table of contents lists. Default `2`. |
| `headingNumbers` | `true` or `false`. This template's heading styles decide the numbers, so it has no effect. |
| `classification` | `public`, `internal` or `confidential`: the marking in the header of every page and above the cover title. Default `public`, which shows nothing. |
| `draft` | `true` or `false`: `true` adds "Draft" to the marking and a watermark to every page. Default `false`. |
| `transition`, `build`, `numberChapters` | Settings for decks. A Word document accepts them and ignores them. |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | Status labels, separated by `;`: a table cell that is only one of them, as code (`` `Done` ``), is shown as a coloured label. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

## Characters

Each character is shown in the document's font, or in a substitute where that font lacks it. Prefer plain characters (`•`, `–`, `→`, typographic quotes) to symbols, and leave out emoji.

## Limits

A check before generating reports what goes beyond these: at most 150 words in a paragraph; at most 60 characters in a heading; alt text for every image. Keep to them from the start.

## When something is wrong

Generation stops with a message that says what to fix, such as a front matter key that does not
exist or a value it does not take, an image that is missing, or a broken diagram or chart with its line.
Fix the Markdown and generate again.

## Notes you may get back

The document is still written, but whoever passes it to you may also pass on notes about it, each with its line. Each names what happened:

- `code-not-drawn`: a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type. In a Word document, diagrams, QR codes and drawn containers become pictures only where an image converter is set up; without one they stay code, alt text or their content. Ask whoever generates the document to set one up.
- `html-as-text`: HTML is not interpreted: it is shown as text. Write Markdown instead.
- `footnote-missing`: a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference.
- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.
- `attribute-ignored`: an image attribute has no effect here, for example `fit=cover` on an image of its own in a PDF, or `focus` on an image that is not cropped. Remove it, or use it where it works.
- `icon-not-drawn`: an icon was left out where it cannot be drawn. Move it as the *Icons* section says. In a Word document, icons are drawn only where an image converter is set up, and are left out otherwise.
- `caption-ignored`: a `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text. Move it right after its table, or write it as ordinary text.
- `list-too-deep`: a list is nested deeper than three levels; the deeper items are set at the third level. Flatten the list to three levels.
- `classification-kept`: the front matter asks for a lower `classification` than the host set, and the host's applies. Leave the key out, or raise it.

A check before generating writes nothing and returns these notes too, and also:

- `long-paragraph`: a paragraph has more words than *Limits* allows. Split it, or make a list of it.
- `title-too-long`: a title or heading is longer than *Limits* allows. Shorten it; the detail goes in the text.
- `alt-text-missing`: an image has no alt text. Say what it shows: `![Sales by region](sales.png)`.
- `image-source`: an image is not among the files given to you, or is a web address or `data:` URL, which is never loaded. Use only the paths you were given.

## Example

````markdown
---
title: Network expansion 2027
toc: true
---

# Network expansion 2027

Status report for the steering committee, October

## Summary

The upgrade is on schedule: 14 of 20 substations are done.[^permits] The budget is on track.

## Sites

**Substations by region**

| Region | Done | Planned |
| :----- | ---: | ------: |
| North | 8 | 10 |
| South | 6 | 10 |

Table: Progress at the end of October

## Next steps

> [!IMPORTANT]
> The southern permits decide the date of commissioning.

1. Obtain the remaining permits in the first quarter.
2. Commission the last sites.

- [x] Budget approved
- [ ] Permits for the southern sites

[^permits]: Two sites are delayed by permits.
````
