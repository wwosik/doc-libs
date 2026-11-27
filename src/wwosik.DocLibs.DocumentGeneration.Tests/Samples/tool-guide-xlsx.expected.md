# Writing documents with create_document

The tool `create_document` turns Markdown into a file: a PDF document (format "pdf") or an Excel workbook (format "xlsx"). This guide says how to call it and, below, what Markdown each format understands.

## Calling the tool

- **One call per document.** Put the whole document in `markdown`, front matter included.
- **`format`** picks the file: "pdf" or "xlsx". It is required.
- **Data instead of Markdown.** For an Excel workbook (`xlsx`), give the data as JSON in `workbook` instead of `markdown`, or attach a `.json` file and give its path in `workbookResource`; its section says how. Either one implies the format. Giving it with `markdown`, both of them, or with another `format` is an error. Each problem names the JSON path of its value, `$.sheets[0].rows[2][1]: …`.
- **`audience`** may be left out; it picks the version, `customer`, `internal` or `trainer`, as each format's section *Versions* says.
- **Check first.** With `"mode": "check"` the tool checks the Markdown and writes nothing. It returns every problem at once, where generating stops at the first error. Fix what it reports, then call again with `"mode": "generate"` or without `mode`.

## What comes back

The tool replies with JSON, in one of these shapes:

- **`"status": "ok"`**: the file was written. `pages` counts the slides or pages written, and is 0 when the program that opens the file lays out the pages, as Word does. `notes` lists what the format did to your Markdown that you cannot see, such as a slide that did not fit: each with `code`, `line` (in your Markdown, front matter included; 0 when unknown) and `message`. Each format's section *Notes you may get back* explains the codes. When a note matters, fix the Markdown and call again.
- **`"status": "checked"`**: the result of a check: `pages`, `notes` as above with the check's own rules among them, and `statistics` (`words`, `images`, `tables`, `diagrams`; for decks `slidesByKind` and `talkTimeMinutes`). When the document cannot be generated, `error` says why.
- **`"status": "error"`**: nothing was written. `error` says what to change, in the call or in the Markdown.

## PDF document (format: "pdf")

### Writing Markdown for a PDF

The PDF is generated from Markdown: the design (colours, fonts, logo, header and footer, page
numbers) is applied for you. Write it as a document to read: full sentences and paragraphs,
headings that say what a section is about, lists where items really are parallel.

### Structure

| Markdown | Becomes |
| -------- | ------- |
| A `#` heading as the very first line | The cover page. The paragraphs below it, up to the next heading, are the subtitle. Without it, there is no cover. With `layout: plain`, the title at the top of page 1 (below). |
| Every other `#` heading | A heading that starts a new page. |
| `##` to `######` | Headings, in the table of contents (bookmarks), kept on a page with what follows them. |
| `---` | A thin line. |

- Paragraphs are left-aligned; a line break is `\` at the end of a line.
- Lists nest with indentation; `1.` lists are numbered from their first number.
- `**bold**`, `*italic*`, `~~strike~~`, `` `code` `` and links work. A link needs an absolute URL
  (`https://…`) or a heading anchor (`[see costs](#costs)`); anything else stays text.
- `[words]{.success}` sets a few words bold in a status colour: `.success`, `.info`, `.warning`,
  `.danger` or `.neutral`.
- A quote (`>`) is indented and grey. A code block is set in a monospace font on a grey
  background, long lines wrapped.
- **Tables** span the page width; columns get the room their content needs, alignment follows the
  `:--`/`--:` markers. Long tables continue on the next page with the header row repeated. Keep
  them to about six columns.
- **Images** (PNG or JPEG) are referenced by the path given to you, keep their size, are only
  scaled down to fit and never split; `![Chart](chart.png "scale=0.5")` halves the size. Several
  images in one paragraph stand side by side, equally wide. Other words in the title are its
  caption (below).
- **Image attributes** in braces right after an image, without a space:
  `![Team](team.jpg){.round .grey}`. `.round` crops it to a circle, `.grey` shows it in grey.
  In a row of images, `fit=cover` fills the row's height
  and crops what sticks out, and `focus=top`, `bottom`, `left`, `right` or `center` (the default)
  says which part stays; an image of its own is never cropped. An unknown attribute is an error.
- **QR codes:** `![Survey](qr:https://example.com/survey)` draws a QR code for the text after
  `qr:`, taken literally (a web address, `mailto:…`). `"caption"` in the title or
  `{.caption}` puts the alt text under it, `scale=2` doubles it; no other attributes. Shorter
  addresses scan better.
- **Footnotes:** `[^1]` in the text and `[^1]: The note.` on a line of its own anywhere below.
  The reference is a small raised number linked to its note at the foot of its page, numbered
  through the document; a long note continues on the next page.
- **Task lists:** `- [ ] open` and `- [x] done` get an empty or ticked box instead of a bullet.
- **Definition lists:** a term on a line of its own, then `:   ` and its description on the next
  line. The term is bold, the description indented.
- HTML is printed as text: do not use it. HTML comments are dropped.

### Contents, numbers and captions

- `toc: true` in the front matter puts a table of contents on a page after the cover: the headings
  of `tocDepth` levels (default 2) from the highest level after the cover, each with its page
  number. In a plain document it follows the title.
- `headingNumbers: true` numbers three heading levels from the highest one after the cover: `1`,
  `1.1`, `1.1.1`, also in the bookmarks and the contents. A skipped level does not count: a `###`
  right under a `#` is `1.1`. `{.unnumbered}` at the end of any heading gives it no number.
- **Appendices:** `# Glossary {.appendix}` makes that chapter and every later `#` an appendix,
  headed "Appendix A Glossary", "Appendix B …" in the document's language, also in the contents and
  bookmarks; with `headingNumbers: true` its sections are `A.1`, `A.1.1`. Once, on a `#` after the
  cover, outside containers. A deck's `{.backup}` is ignored here.
- **Captions** are numbered through the document, figures and tables apart, as "Figure 1: …" and
  "Table 1: …" in the document's language (`lang`):
  - an image's title, besides `scale=`, is its caption: `![Sales](s.png "Sales by region")`;
    `"caption"` alone uses the alt text;
  - a paragraph `Table: …` after a table, after a blank line, goes above the table;
  - a paragraph `Figure: …` right after a diagram block that is drawn goes below it.

  `Table:` and `Figure:` stay English in every language. Anywhere else such a paragraph is text.

```markdown
| Region | Sales |
| ------ | ----: |
| North  | 120   |

Table: Sales by region, 2026
```

### Justified text and page breaks

- `justify: true` in the front matter justifies body text and footnotes, without hyphenation;
  very loose lines stay ragged. Headings, tables and code never are.
- `::: pagebreak` alone on a line starts a new page with what follows; its closing `:::` is
  optional. `{.newpage}` at the end of any heading but the cover's starts a new page with it. A
  break never makes an empty page.

### Plain documents and columns

- `layout: plain` in the front matter is for short business documents such as invoices and
  letters: no cover and no header. A leading `#` is the title at the top of page 1, with the logo
  beside it, and the text follows right below it. The footer and page numbers stay.
- `tone: dark` in the front matter gives dark pages with light text, for reading on screen; it
  prints dark too, so leave it out for documents meant for paper.
- `::: columns` sets two or three parts side by side, equally wide or as `::: columns 1:2` says;
  a `---` after a blank line separates them. Columns that do not fit on the page move to the next
  one together, so keep them short: paragraphs, lists, small tables.

```markdown
---
layout: plain
---

# Invoice 2026-114

::: columns
**Seller**\
Example Ltd, 1 High Street

---

**Buyer**\
Client Ltd, 5 Market Square
:::

| Item | Amount |
| ---- | -----: |
| Consulting, October | 4,800.00 |
```

### Syntax written for slides

Markdown written for decks works here too and is shown plainly, so one text can serve both:
`## Title {kicker="…"}` drops the attributes, `::: cards`, `rows`, `panel` and `statement` show
their content as text, `::: columns` sets its parts side by side (above), `==marked==` text is bold, and an alert (`> [!NOTE]`, `[!TIP]`,
`[!IMPORTANT]`, `[!WARNING]`, `[!CAUTION]`) is a quote that starts with its title in bold, in the
document's language (`lang`). Speaker notes (`::: notes` … `:::`), transitions (`transition:`
in the front matter, `transition="…"` on a heading) and builds (a paragraph such as `{.build}`,
`{.frag}`, `{.exit}` or `{.emphasize}` before a block, and `build:` in the front matter) are left
out. `::: scene`: one still picture. Unknown containers,
attributes, alerts, transitions and build markers (a paragraph such as `{.note}`) are errors, as in
decks, and so are a `::: notes` inside another block and a build marker that builds nothing. Write
`\{` to show braces as text.

The kinds of slide `{.agenda}`, `{.quote}`, `{.statement}`, `{.contact}` and `{.chart}` show their
content as written; an `{.agenda}` with nothing under it lists the document's `#` chapters.
`::: conclusion` … `:::` is shown like an alert without a title, `::: comment` … `:::` after the
block it comments as an italic quote. Their mistakes are errors as in decks: a kind with other
content than it takes, a comment as the first block, a conclusion that is not the last.

**Timelines and Gantt charts:** `::: timeline` around a list of 2–8 `- **date** Title: description`;
`::: gantt` around a table of 1–20 rows of task, start and end (no end: a milestone). Dates:
`2026`, `2026-Q2`, `2026-03`, `2026-03-15`; what ended before today is grey. `today=2026-11-01`
after the name pins today; `grid=week|month|quarter` sets a Gantt's grid.

````markdown
## Plan

::: timeline
- **2026-01** Start
- **2026-06** Go-live: all regions
:::

::: gantt grid=month
| Task | Start | End |
| ---- | ----- | --- |
| Build | 2026-02 | 2026-05 |
| Launch | 2026-06 | |
:::
````

**Steps, Harvey balls and risk matrices:** `::: steps` around a list of 2–7 `- **Title** –
description` draws them as chevrons in a row; `{.current}` at the end of one item marks it, the
steps before it done, later ones outlined. A table cell that is only `[0/4]` to `[4/4]`, or
`[1/2]`, is a Harvey ball, a circle filled by quarters; `\[3/4]` keeps the text. `::: riskmatrix`
around a table of 1–15 rows of risk, likelihood and impact (each 1–5) puts numbered dots on a
5 × 5 grid, likelihood up, impact right, with a legend; cells are tinted by likelihood × impact:
1–4 in the success, 5–9 in the warning, 10–25 in the danger colour.

````markdown
## Programme

::: steps
- **Analyse** – understand the current process
- **Design** – agree the target picture {.current}
- **Build**
:::

## Risks

::: riskmatrix
| Risk | Likelihood | Impact |
| ---- | ---------- | ------ |
| Supplier delay | 4 | 3 |
| Key person leaves | 2 | 5 |
:::

## Options

| Option | Cost | Speed |
| ------ | :---: | :---: |
| Buy | [1/4] | [4/4] |
| Build | [3/4] | [1/2] |
````

### Icons

`:icon-name:` draws the icon of that name of [Tabler Icons](https://tabler.io/icons) (3.52.0, outline), such as `:icon-circle-check:`. A PDF draws it inside text, one letter wide in the text's colour; at the start of a list item, in place of its bullet; and alone in a paragraph, large. In headings, table cells and on the cover it is left out. An unknown name is an error.

Common: `check`, `x`, `star`, `heart`, `bolt`, `rocket`, `target`, `flag`, `bulb`, `lock`, `users`, `calendar`, `clock`, `mail`, `chart-bar`, `trending-up`, `cloud`, `search`, `settings`, `world`.

### Versions

One Markdown holds three versions, picked by `audience:`: `customer`, `internal`, which adds what is marked
internal, and `trainer`, which adds solutions too.

- `::: internal` … `:::`, a line `{.internal}` before a block, or `{.internal}` on a `#` or `##` heading (the
  whole chapter or slide) mark what is internal.
- `::: exercise` shows to everyone as "Exercise 1"; a `::: solution` right after it, as "Solution 1", to the
  trainer only.

Versions but `customer` are marked internal unless `classification` is set.

### Front matter

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
| `description` | A description in the file's properties. |
| `keywords` | Keywords in the file's properties, separated by commas. |
| `lang` | The document language, such as `en`, `de` or `de-CH`: of the texts the generator writes itself, and of spell checking. Default `en`. |
| `typography` | `true` sets typographic quotes, dashes, `…` and no-break spaces (`10 km`) for the language; type straight quotes. Default `false`. |
| `highlight` | `true` colours fenced code in these languages: `csharp`, `vb`, `fsharp`, `cpp`, `java`, `javascript`, `typescript`, `php`, `python`, `powershell`, `sql`, `html`, `xml`, `css`, `markdown`, `haskell`, `json`, `yaml`, `bash`. Default `false`. |
| `template` | The design: `neutral`. |
| `coverImage`, `coverImageSize`, `coverImagePosition` | An image on the cover, one of your image paths; `small`, `medium` or `large`; `below` or `right` of the title. |
| `showDate` | `true` or `false`: today's date in the footer. Default `true`. |
| `showTagline` | `true` or `false`: the design's tagline on the cover, if it has one. Default `true`. |
| `tone` | `light` or `dark`: `dark` paints every page in the design's dark background with light text, diagrams and charts; it prints dark too. Default `light`. |
| `pageSize` | `A4`, `A3` or `Letter`. Default `A4`. |
| `orientation` | `portrait` or `landscape`. Default `portrait`. |
| `layout` | `report` or `plain`: `plain` has no cover and no header, for a short business document. Default `report`. |
| `toc` | `true` or `false`: a table of contents after the cover, with page numbers. Default `false`. |
| `tocDepth` | `1`, `2` or `3`: the heading levels the table of contents lists. Default `2`. |
| `headingNumbers` | `true` or `false`: numbers on three heading levels (1, 1.1, 1.1.1). Default `false`. |
| `justify` | `true` or `false`: body text justified, without hyphenation. Default `false`. |
| `classification` | `public`, `internal` or `confidential`: a marking in the header and on the cover. Default `public`. |
| `draft` | `true` adds "Draft" to the marking and a diagonal watermark. Default `false`. |
| `transition`, `build`, `numberChapters` | Settings for decks. A PDF accepts them and ignores them. |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | Status labels for decks, separated by `;`. A PDF accepts them and shows the labels as code. |
| `audience` | `customer`, `internal` or `trainer`: the version, see *Versions*. Default `customer`. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

### Characters

The fonts are embedded in the PDF and have no emoji and no Asian scripts; never use emoji. Any character the fonts lack stops the generation with an error naming it. Write these symbols as shown:

| Avoid | Write instead |
| ----- | ------------- |
| ✓ | `[x]` |
| ✔ | `[x]` |
| ✗ | `[ ]` |
| ✘ | `[ ]` |
| ☐ | `[ ]` |
| ☑ | `[x]` |
| ★ | `*` |
| ☆ | `*` |
| ⇒ | `→` |
| ⇐ | `←` |
| ‖ (in code only) | `\|` |

### Limits

A check before generating reports what goes beyond these: at most 150 words in a paragraph; at most 60 characters in a heading; alt text for every image. Keep to them from the start.

### When something is wrong

Generation stops with a message that says what to fix, such as a character the fonts lack or an
image that is not among the files. Fix the Markdown and generate again.

### Notes you may get back

The document is still written, but whoever passes it to you may also pass on notes about it, each with its line. Each names what happened:

- `code-not-drawn`: a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type.
- `html-as-text`: HTML is not interpreted: it is shown as text. Write Markdown instead.
- `footnote-missing`: a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference.
- `footnote-split`: a footnote was too long for the foot of its page and continues at the foot of the next. Shorten it, or move the detail into the text.
- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.
- `attribute-ignored`: an image or heading attribute has no effect here, for example `fit=cover` on an image of its own in a PDF, or `{.appendix}` in a deck. Remove it, or use it where it works.
- `icon-not-drawn`: an icon was left out where it cannot be drawn. Move it as the *Icons* section says.
- `agenda-empty`: an empty `{.agenda}` found no `#` chapters to list. Add the chapters, or write the agenda as a list.
- `caption-ignored`: a `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text. Move it right after its table, or write it as ordinary text.
- `toc-empty`: `toc: true` asked for a table of contents, but the document has no headings at the depth it lists, so there is none. Add headings, raise `tocDepth`, or leave `toc` out.
- `typography-skipped`: with `typography: true`, a character the fonts lack was kept as typed. Nothing to fix.
- `code-language-unknown`: with `highlight: true`, a code block's language is unknown. Check the spelling, or write `text`.
- `audience-omitted`: this version left out parts marked for other versions (*Versions*). Nothing to fix.
- `cover-image-ignored`: there is no cover (no leading `#`) or no room on it for `coverImage`. Start with a `#` heading, or leave it out.

A check before generating writes nothing and returns these notes too, and also:

- `long-paragraph`: a paragraph has more words than *Limits* allows. Split it, or make a list of it.
- `title-too-long`: a title or heading is longer than *Limits* allows. Shorten it; the detail goes in the text.
- `alt-text-missing`: an image has no alt text. Say what it shows: `![Sales by region](sales.png)`.
- `image-source`: an image is not among the files given to you, or is a web address or `data:` URL, which is never loaded. Use only the paths you were given.

### Example

````markdown
---
title: Network expansion 2027
---

# Network expansion 2027

Status report for the steering committee, October

## Summary

The upgrade is on schedule: 14 of 20 substations are done. Two sites are delayed by permits, and
the budget is on track.

## Progress

| Site group | Done | Open |
| ---------- | ---: | ---: |
| North | 8 | 1 |
| South | 6 | 5 |

> [!WARNING]
> The permits for the last two sites are still missing. Without them, commissioning moves to Q3.

## Next steps

1. Obtain the remaining permits (Q1).
2. Commission the last sites (Q2).
````

## Excel workbook (format: "xlsx")

### Writing Markdown for an Excel workbook

A workbook holds tables to sort, filter and calculate with. The design (colours, fonts, logo, banding,
print setup) is applied for you. Write each table with real values in the document's language;
the workbook stores them as numbers, dates and truth values, not as text.

### Structure

| Markdown | Becomes |
| -------- | ------- |
| A `#` heading as the very first line | The workbook's title on the info sheet, which comes first. The paragraphs below it, up to the next heading or table, are its description. |
| `## Name` | A sheet of that name: at most 31 characters, none of `[]:*?/\`, unique. |
| Paragraphs under `## Name`, above its table | The sheet's description, above the table. |
| The first table under `## Name` | The sheet's data: the header row, then one row per record. |
| `Table: Title` right after the table, after a blank line | The sheet's title, above the table. |
| A further table under the same `##` | A sheet of its own, `Name 2` (note `sheet-split`). Give it a `##` of its own instead. |
| A table before any `##` | A sheet named "Table". |

- One table per sheet. A `##` without a table is no sheet.
- Lists, quotes, alerts, code other than `chart`, images, rules and `###` or lower headings are left
  out (note `content-ignored`).
- In cells, `**bold**`, links and other inline Markdown are reduced to their text; `\|` is a pipe.

````markdown
# Quarterly figures

Sales and stock of the first quarter.

## Sales

Net, per region.

| Region | Units {total=sum} | Revenue {.currency=EUR total=sum} | Share |
| ------ | ----------------: | --------------------------------: | ----: |
| North  | 1,200 | 15,000.50 | 25 % |
| South  | 800   | 9,800.00  | 18 % |

Table: Sales by region
````

### Types

A column without a type takes it from its values, read in the document's language (`lang`): in
English `1,234.50`, in German `1.234,50`.

| Value | Type |
| ----- | ---- |
| `1,234.50`, `-3` | Number; whole numbers only: Integer |
| `$1,234.50`, `1,234.50 €`, `99 EUR` | Currency: the language's symbol, `€`, `$`, `£`, `¥`, or any ISO code |
| `25 %`, `12.5%` | Percent |
| `3/9/2026` (English), `09.03.2026` (German), `2026-03-09` | Date |
| `2026-03-09 14:30`, a date with the language's time | Date and time |
| `true`, `false`, `yes`, `no`, and the same words in the language (`wahr`, `ja`) | Boolean |

- A number with a leading zero (`01234`) or more than 15 digits stays text, and so does a column of
  anything else.
- When at least two thirds of a column share a type, the column has it. The other values stay as
  written, with the warning `column-mixed`. So does a number written in another language's way, such
  as `1,5` in English.
- A column whose cells are all formulas is a number, or a currency when its formulas name only
  currency columns.

### Header attributes

Attributes at the end of a header cell, in braces, set what a column is. Several go together:
`| Amount {.currency=USD decimals=0 total=sum} |`.

| Attribute | Means |
| --------- | ----- |
| `.text`, `.number`, `.integer`, `.percent`, `.date`, `.datetime`, `.boolean` | The column's type. Each value must then be one of that type: anything else is the error `cell-type`. |
| `.currency`, `.currency=EUR` | Amounts in the language's currency, or in the one named by its ISO code. A plain number is an amount too. |
| `decimals=2` | Digits after the decimal mark, 0 to 10. Default: as many as the values have; a currency's own. |
| `total=sum` | A totals row under the table: `sum`, `average`, `count`, `min` or `max`. It follows the filter. Only `count` on text. |
| `width=20` | The width in characters, 1 to 255. Default: measured. |

- In a `.percent` column, `25 %` is 25 %, and a bare `0.25` is the same.
- The separator row aligns: `:---` left, `:---:` centred, `---:` right. Default: text left, numbers
  and dates right.
- An unknown attribute is the error `column-invalid`, which suggests the closest names.

### Formulas

A cell starting with `=` is a formula in Excel's English form, with commas between arguments:

- `[Header]` is the cell of that column in the same row: `=[Price]*[Quantity]`.
- `{row}` is the row's number: `=SUM(B{row}:D{row})`. A1 references are kept as written.
- A formula that does not parse, names an unknown column or refers to itself is `formula-invalid`; an
  unknown function only the warning `function-unknown`.

````markdown
## Orders

| Item | Price {.currency} | Quantity | Amount {total=sum} |
| ---- | ----------------: | -------: | -----------------: |
| Pens | 1.50 | 10 | =[Price]*[Quantity] |
| Pads | 3.20 | 4  | =[Price]*[Quantity] |
````

### Charts

A code block with the language `chart` becomes a native chart, in one of two forms.

**Column form**: settings only, right after a table; the chart draws that table's columns, and
follows when a value changes.

````markdown
## Months

| Month | Plan | Actual |
| ----- | ---: | -----: |
| Jan   | 120  | 132    |
| Feb   | 118  | 121    |

```chart
category: Month
series: Plan, Actual
title: Orders by month
```
````

**Data form**: settings, a line `---`, then CSV as in decks and documents (a header row, then a row
per category; `,` with a decimal point, or `;` with a decimal comma). Its data becomes a visible
table on the sheet, with the chart beside it; under a `##` that already has a table, on a sheet of
its own (`Name 2`).

| Setting | Values |
| ------- | ------ |
| `category`, `series` | Column form only: the header of the category column; the series' headers, separated by commas. |
| `type` | `bar` (upright, the default), `horizontal-bar`, `line` or `area`. |
| `stacked` | `true` stacks bars or areas. |
| `title`, `unit`, `source` | The chart's title, the unit of its values, where the data comes from. |
| `placement` | `beside` the table (the default) or a chart `sheet` of its own. |
| `sheet` | The name of the chart sheet; implies `placement: sheet`. |
| `decimal` | Data form only: `comma` or `point`. |

A series takes a number, integer, currency or percent column. A mistake is the error `chart-invalid`.
In decks, PDFs and Word documents a `chart` block needs its data: `category` and `series` are an
error there.

### Front matter

Settings go in a block at the very top, before anything else. One `key: value` per line. Every
key is optional.

```markdown
---
title: Quarterly figures
lang: de
---
```

| Key | Values |
| --- | ------ |
| `title` | The title on the info sheet, in the page header and the file's properties. Default: the first `#` heading. |
| `subtitle` | A line under the title on the info sheet. |
| `author` | The author on the info sheet and in the file's properties. |
| `description` | What the workbook holds, when there is no `#` with paragraphs. |
| `keywords` | Accepted; a workbook has no keywords. |
| `date` | The date shown, such as `2026-03-09`. Default: today. |
| `lang` | The language, such as `en`, `de` or `de-CH`: of the values in the tables, the number and date formats, and the texts the generator writes. Default `en`. |
| `template` | The design: `neutral`. |
| `info` | `true` or `false`: the info sheet. Default: when there is a title or a description. |
| `showDate` | `true` or `false`: the date on the info sheet and above every table. Default `true`. |
| `classification` | `public`, `internal` or `confidential`: a marking on the info sheet and in the printed page header. Default `public`; a lower value has no effect. |
| `draft` | `true` adds "Draft" to the marking. Default `false`. |
| `audience` | `customer`, `internal` or `trainer`: the version, see *Versions*. Default `customer`. |
| `showTagline`, `embedFonts`, `showFooter`, `transition`, `build`, `numberChapters`, `listBullets`, `tone`, `typography`, `highlight` | Settings of decks and documents, accepted and ignored. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

### Versions

One Markdown holds three versions, picked by `audience:`: `customer`, `internal`, which adds what is marked
internal, and `trainer`, which adds solutions too.

- `::: internal` … `:::`, a line `{.internal}` before a block, or `{.internal}` on a `#` or `##` heading (the
  whole chapter or slide) mark what is internal.
- `::: exercise` shows to everyone as "Exercise 1"; a `::: solution` right after it, as "Solution 1", to the
  trainer only.

Versions but `customer` are marked internal unless `classification` is set.

### Errors and notes

These are errors: the workbook is not written, and the message names each with its line or cell.

- `cell-type`: a value does not fit its column's type, such as text in a number column. Give the column the right type, or fix the value.
- `sheet-name`: a sheet name is empty, longer than 31 characters, holds one of `[]:*?/\`, or repeats another ignoring case. Rename the sheet.
- `column-invalid`: a column has no header, decimals outside 0–10, a currency without the currency type, an unknown currency code or a width outside 1–255. Fix the column.
- `row-too-wide`: a row has more cells than the sheet has columns. Add the column, or remove the extra cells.
- `excel-limit`: Excel cannot hold the sheet or the value: too many rows or columns, too long a text, or a date before 1900. Split the data or shorten the text.
- `formula-invalid`: a formula does not parse, names a column the sheet does not have, refers to itself within its row, or gives a function the wrong number of arguments. Fix the formula at the position named.
- `chart-invalid`: a chart names a column the sheet does not have, takes a series from a column that is not a number, integer, currency or percent column, stands on a sheet without rows, has no series, or names a chart sheet that is invalid or taken. Fix the chart.

These are notes: the workbook is still written, and whoever passes it to you may pass them on, each
with its line.

- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.
- `caption-ignored`: a `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text. Move it right after its table, or write it as ordinary text.
- `classification-kept`: front matter cannot lower the host's `classification`, which applies. Leave the key out.
- `audience-omitted`: this version left out parts marked for other versions (*Versions*). Nothing to fix.
- `character-missing`: the brand's fonts lack a character, which Excel shows in a substitute font. Write it as a plain character, or leave it out.
- `function-unknown`: a formula calls a function the generator does not know. Check the spelling and use the English name; an add-in's function is fine as it is.
- `chart-colors-repeat`: a chart has more series than the brand's diagram palette has colours, so two series share a colour. Split the chart, or use fewer series.
- `column-mixed`: most values of a column share a type, which the column takes; the others stay as written, as text. Fix those values, or give the column a type such as `{.text}`.
- `sheet-split`: a further table, or a chart with data, under the same `##` went to a sheet of its own, named like the first with a number. Give it a `##` heading of its own.
- `content-ignored`: a list, quote, code block, image, `###` heading or other block a workbook cannot hold was left out. Remove it, or put its content in a table.

### Workbooks as JSON

A workbook can also be given as JSON: the same workbook as data instead of Markdown, which suits figures that
come from a program. It follows the JSON Schema of the workbook. Names are camelCase, values of a fixed set are
camelCase strings (`dateTime`, `sum`, `bar`), and a property the schema does not name is an error.

```json
{
  "title": "Quarterly figures",
  "date": "2026-03-31",
  "marking": { "classification": "internal" },
  "sheets": [
    {
      "name": "Orders",
      "columns": [
        { "header": "Item" },
        { "header": "Price", "type": "currency", "currency": "USD" },
        { "header": "Quantity", "type": "integer", "total": "sum" },
        { "header": "Amount", "type": "currency", "currency": "USD", "total": "sum" },
        { "header": "Ordered", "type": "date" },
        { "header": "Share", "type": "percent", "decimals": 1 }
      ],
      "rows": [
        ["Pens", 1.5, 10, { "formula": "[Price]*[Quantity]" }, "2026-01-15", 0.25],
        ["Pads", 3.2, 4, { "formula": "[Price]*[Quantity]" }, "2026-02-03", null]
      ],
      "charts": [{ "type": "bar", "category": "Item", "series": ["Quantity"] }]
    }
  ]
}
```

- **Workbook:** `sheets` is required; `title`, `subtitle`, `description`, `author`, `date`, `language`,
  `marking` (`classification`, `draft`) and `brand` are optional. A title or a description gives the info sheet.
- **Sheet:** `name` and `columns` are required; `title`, `description`, `totalsLabel`, `rows` and `charts` are
  optional.
- **Column:** `header` is required; `type` (`text`, the default, `number`, `integer`, `currency`, `percent`,
  `date`, `dateTime`, `boolean`), `currency` (an ISO 4217 code), `decimals`, `width`, `align` and `total`
  (`sum`, `average`, `count`, `min`, `max`) are optional.
- **Chart:** `category` and `series` (column headers) are required; `type` (`column`, `bar`, `line`, `area`),
  `stacked`, `title`, `unit`, `source`, `placement` (`beside`, `sheet`) and `sheetName` are optional.
- **Cells:** each row is an array with one value per column, in their order: a number (a percentage as a
  share, `0.25` for 25 %), a string (in a `date` column a date such as `"2026-03-15"`, in a `dateTime` column
  `"2026-03-15T14:30:00"`), `true` or `false`, `null` for an empty cell, or `{"formula": "…"}` with a formula
  as in *Formulas*.

Every problem names the JSON path of its value, such as `$.sheets[0].rows[2][1]`. Problems with the shape come
first, as `json-invalid`; once the shape is right, the workbook is checked as one from Markdown, with the codes
under *Errors and notes*.
