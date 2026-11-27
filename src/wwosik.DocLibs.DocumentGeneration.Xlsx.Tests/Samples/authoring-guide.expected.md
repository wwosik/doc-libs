## Writing Markdown for an Excel workbook

A workbook holds tables to sort, filter and calculate with. The design (colours, fonts, logo, banding,
print setup) is applied for you. Write each table with real values in the document's language;
the workbook stores them as numbers, dates and truth values, not as text.

## Structure

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

## Types

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

## Header attributes

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

## Formulas

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

## Charts

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

## Front matter

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

## Versions

One Markdown holds three versions, picked by `audience:`: `customer`, `internal`, which adds what is marked
internal, and `trainer`, which adds solutions too.

- `::: internal` … `:::`, a line `{.internal}` before a block, or `{.internal}` on a `#` or `##` heading (the
  whole chapter or slide) mark what is internal.
- `::: exercise` shows to everyone as "Exercise 1"; a `::: solution` right after it, as "Solution 1", to the
  trainer only.

Versions but `customer` are marked internal unless `classification` is set.

## Errors and notes

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
