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

Headings of the first three levels are numbered `1`, `1.1`, `1.1.1`; deeper ones are not. `headingNumbers: false` in the front matter removes the numbers.

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
- Images are PNG, JPEG or GIF. `fit=`, `focus`, `.round` and `.grey` have no effect in a Word
  document.

## Slide syntax

Markdown written for slides (`:::` containers, alerts such as `> [!NOTE]`, `==marks==`,
`[words]{.success}`, slide settings on headings) is accepted and shown plainly: the content of a
container in sequence, an alert as a quote under its title, marks and coloured words in bold.
Diagrams and charts (`mermaid`, `chart` blocks) stay code.

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
| `showDate` | `true` or `false`: today's date on the cover and in the footer. Default `true`. |
| `showTagline` | `true` or `false`: the design's tagline on the cover, if it has one. Default `true`. |
| `embedFonts` | `true` or `false`. Leave it out: by default the document carries its fonts. |
| `toc` | `true` or `false`: a table of contents after the cover. Default `false`. |
| `tocDepth` | `1`, `2` or `3`: the heading levels the table of contents lists. Default `2`. |
| `headingNumbers` | `true` or `false`: numbers on the first three heading levels (1, 1.1, 1.1.1). Default `true`. |
| `transition`, `build`, `numberChapters` | Settings for decks. A Word document accepts them and ignores them. |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | Status labels for decks, separated by `;`. A Word document accepts them. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

## Characters

Each character is shown in the document's font, or in a substitute where that font lacks it. Prefer plain characters (`•`, `–`, `→`, typographic quotes) to symbols, and leave out emoji.

## When something is wrong

Generation stops with a message that says what to fix, such as a front matter key that does not
exist or a value it does not take, or an image that is missing. Fix the Markdown and generate
again.

## Notes you may get back

The document is still written, but whoever passes it to you may also pass on notes about it, each with its line. Each names what happened:

- `code-not-drawn`: a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type.
- `html-as-text`: HTML is not interpreted: it is shown as text. Write Markdown instead.
- `footnote-missing`: a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference.
- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.
- `attribute-ignored`: an image attribute has no effect here, for example `fit=cover` on an image of its own in a PDF, or `focus` on an image that is not cropped. Remove it, or use it where it works.
- `caption-ignored`: a `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text. Move it right after its table, or write it as ordinary text.
- `list-too-deep`: a list is nested deeper than three levels; the deeper items are set at the third level. Flatten the list to three levels.

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

1. Obtain the remaining permits in the first quarter.
2. Commission the last sites.

- [x] Budget approved
- [ ] Permits for the southern sites

[^permits]: Two sites are delayed by permits.
````
