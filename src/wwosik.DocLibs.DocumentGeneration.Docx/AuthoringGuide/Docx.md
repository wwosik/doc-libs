## Writing Markdown for a Word document

The design (styles, fonts, logo, header and footer, page numbers) comes from the template, applied
for you. Write it as a document to read: full
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
- A skipped level never shows as 0 where headings are numbered: a `###` right under a `#` is
  `1.1`, and `##` sections before the first `#` are `1`, `2`. Still, write headings in order.

## Heading numbers

{{headingNumbers}}

- `{.unnumbered}` at the end of a heading gives it no number, and it does not count:
  `## Preface {.unnumbered}`.
- **Appendices:** `# Glossary {.appendix}` makes that chapter and every later `#` an appendix,
  headed "Appendix A Glossary", "Appendix B …" in the document's language, also in the table of
  contents; numbered sections in it are `A.1`, `A.1.1`. Once, on a `#` after the cover, outside
  containers. A deck's `{.backup}` is ignored here.

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
- {{imageFormats}}
- **Image attributes**, right after the image: `![Team](team.jpg){.round .grey}` crops it to a
  circle and shows it grey. In a row of images, `{fit=cover focus=top}` fills the row's height and
  keeps the top (or `bottom`, `left`, `right`, `center`). Unknown attributes are errors.

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
| `::: scene` | A picture of what it shows after its last step, like the drawn containers below. |
| `## Title {kicker="…" layout="…"}`, `{.dark}`, transitions, `::: notes`, build markers | Checked as in decks, then left out. |

Unknown containers, alerts and heading attributes are errors.

## Diagrams, charts and QR codes

{{drawings}}

{{icons}}

{{diagrams}}

## Page breaks

`::: pagebreak` on a line of its own, closed by `:::`, starts a new page with what follows. It may
hold content, which then starts the new page, and its closing `:::` may be left out. `{.newpage}` at
the end of any heading starts a new page with that heading: `## Appendix {.newpage}`. A later `#`
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

{{versions}}

{{frontMatter}}

## Characters

{{characters}}

{{limits}}

## When something is wrong

Generation stops with a message that says what to fix, such as a front matter key that does not
exist or a value it does not take, an image that is missing, or a broken diagram or chart with its line.
Fix the Markdown and generate again.

## Notes you may get back

{{notes}}

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
