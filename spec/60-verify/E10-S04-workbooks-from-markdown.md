# E10-S04 Workbooks from Markdown

> Status: in verification
> Epic: [E10 Workbook generator](../50-todo/E10-workbook-generator.md)

## Summary

The workbook generator also takes Markdown. Each `##` is a sheet whose first table is the data. Column
types are inferred from the values in the document's culture or set by attributes in the header
cells (`| Amount {.currency total=sum} |`). A `chart` block either carries its own CSV data as in
the other formats, or names the table's columns. The package gets an authoring guide and joins
`IMarkdownGenerator`.

## Context

- **Front matter** is parsed by `FrontMatter.cs`. The keys shared by every generator are `title`,
  `subtitle`, `author`, `date`, `lang`, `template` and `marking`. `DocumentLanguage.Resolve` in
  `GeneratedTexts.cs` picks the language.
- **Captions** (`Captions.cs`): a `Table: …` line after a table is its caption.
- **Heading attributes** (`HeadingAttributes.cs`) parse `{.class key=value key="value"}`. Table
  cells have no attributes today.
- **The `chart` block** (`DocumentGeneration.Charts/ChartParser.cs`, documented in
  `docs/wwosik.DocLibs.DocumentGeneration.Charts.md`) is settings, then `---`, then CSV, with `,` or
  `;` and a decimal point or comma.
- **`IMarkdownGenerator`** is what `DocumentTool` and the authoring guides work with.

## Proposed approach

1. **API**:
   - `XlsxGenerator.Generate(MarkdownDocument, Stream, XlsxOptions?)`,
     `Check(MarkdownDocument, XlsxOptions?)`, `Preview(MarkdownDocument, …)` and
     `AuthoringGuide(XlsxOptions?)`.
   - `XlsxGenerator.ForOptions(options)` returns an `IMarkdownGenerator` with format `xlsx`.

2. **Structure**:
   - A `#` as the first block is the title, and its paragraphs are the info sheet's description.
   - Each `## Name` is a sheet: paragraphs above its table are the sheet's description, the first
     table is the data, and `Table: …` after it is the sheet's title.
   - A second table under the same `##` becomes the sheet `Name 2` (note `sheet-split`).
   - A table before any `##` is a sheet named `GeneratedTexts.Table`.
   - Lists, quotes, code other than `chart`, images and `###` and lower headings are left out (note
     `content-ignored`).
   - Inline Markdown in cells is reduced to its text, and `\|` is a pipe.

3. **Header-cell attributes**, in the heading-attribute syntax, at the end of a header cell:
   - Types: `{.text}`, `{.number}`, `{.integer}`, `{.currency}` or `{.currency=EUR}`, `{.percent}`,
     `{.date}`, `{.datetime}`, `{.boolean}`.
   - Settings: `decimals=N`, `total=sum|average|count|min|max`, `width=N`.
   - Several together, for example `{.currency=USD decimals=0 total=sum}`.
   - An unknown attribute is a `column-invalid` error that suggests close names.
   - The separator row's `:--`, `:-:` and `--:` set the alignment.
   - A cell starting with `=` is a formula (S02).

4. **Type inference**, when a column has no type:
   - Each value is parsed with the document's `CultureInfo`: numbers, currency amounts with the
     culture's symbol or any ISO code (`1.234,50 €`, `$1,234.50`, `99 EUR`), percentages, the
     culture's short dates and ISO dates, date-times, and booleans in the document's language and in
     English.
   - A number with a leading zero, or longer than 15 digits, stays text.
   - If at least two thirds of a column share a type, that is the column's type. The other cells
     stay as written, with the warning `column-mixed`. So does a number written in another culture's
     way, such as `1,5` in an English document.
   - A column of only formulas is a number, or a currency when its formulas name only currency
     columns.

5. **The `chart` block in a workbook** belongs to the table above it. It has two forms:
   - **Column form:** settings only, which name columns: `category`, `series` (comma-separated),
     `type`, `stacked`, `title`, `unit`, `source`, and the workbook keys `placement: beside|sheet`
     and `sheet`.
   - **Data form:** settings, `---` and CSV, as in the other formats. Its data becomes a visible table
     on the chart's sheet, beside the chart, so the chart refers to cells the reader can see and edit.
   - In decks, PDFs and Word documents a `chart` block with `category` or `series` and no data is an
     error naming the workbook form, so the block means the same thing everywhere.

6. **Front matter**: the shared keys, plus `info` (`true` / `false`) and `showDate`. Keys of other
   formats are accepted and ignored, as between the other generators.

7. **Authoring guide**: `AuthoringGuide/Xlsx.md` covers the structure, attributes, inference, totals,
   formulas, charts, notes and limits, in the house style, with a word limit checked by a test. The
   `docs/` page has a feature sample generated in the tests.

## Acceptance criteria

- [ ] A Markdown sample with three sheets, every attribute, totals, a formula column and both chart
      forms gives a workbook that opens without repair, with no notes.
- [ ] Inference gives the same types for the same values written in `de` and in `en`.
- [ ] `column-mixed`, `sheet-split` and `content-ignored` come with their Markdown line.
- [ ] A `chart` block with column names in a deck throws with a message that names the workbook form.
- [ ] The authoring guide's lists of attributes, keys and note codes are tested against the code.

## Open questions

1. In the data form of the `chart` block, where should the chart's data go: a visible table on the
   chart's sheet, or a hidden sheet?

> A visible table on the chart's sheet, beside the chart. (2026-11-26)

2. Should a `###` heading be allowed to start a further table on the same sheet (several tables per
   sheet), instead of being left out?

> No: one table per sheet; a further table under the same `##` goes to the sheet `Name 2`. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- **Reader.** `XlsxMarkdown` (new, internal) reads a `MarkdownDocument` into an `XlsxWorkbook` plus the options with
  the front matter applied, the Markdown's problems (with lines) and its notes; `XlsxGenerator.Generate`, `Check` and
  `Preview` take a `MarkdownDocument` and then run the model's plan, which gets the Markdown's problems first
  (`XlsxPlan.Prepend`). `Generate`'s message names a problem's line when it has one (`line 3: …`); problems of the
  model itself (an invalid sheet name, a formula) still carry no Markdown line, only sheet, row and cell.
- **Values.** `XlsxValues` reads values in the culture (`lang`, or `XlsxOptions.Culture`): strict grouping in threes,
  so `1,5` in English and `1.234.5` in German stay text; currency by the culture's symbol, a small fixed map of
  unambiguous symbols (`€ $ £ ¥ ₹ ₩ ₺ zł`; `$` is USD) or any known ISO code; booleans in English plus a few
  languages' words (`en de fr es it nl pl`). Ties in inference go to the narrower type (Date before DateTime, a
  currency before Number); a column of whole numbers only is `Integer`; inferred and declared `Number`/`Percent`
  columns without `decimals=` take the most decimals their values have.
- **Mixed values** in an inferred column stay text in the typed column. The model needed a way to allow this: an
  internal `XlsxColumn.KeepsText`, which the plan honours instead of `cell-type`. In a declared column a value that
  does not fit is `cell-type`, reported by the reader with its line (and kept as text, so the plan does not report it
  twice).
- **Formulas** are taken from the cell's source span, not its inlines, so `[A]*[B]*2` is not read as emphasis.
- **Charts.** The column form attaches to the current table; before any table it is `chart-invalid`. The data form is
  read by the deck's `ChartParser`, linked into the package as source (no package reference to Charts), and becomes a
  table of text categories and `Number` series. Under a `##` that already has a table it goes to `Name 2` with
  `sheet-split`, like a second table. `type: bar` is upright as in the other formats; bars across are
  `type: horizontal-bar` (a name not in the plan). `sheet:` implies `placement: sheet`.
- **Decks, PDFs and Word:** `ChartParser` throws for `category`, `series`, `placement` and `sheet` with a message naming
  the workbook form, also for a block of only such settings without `---`.
- **Front matter:** `info`, `showDate`, `subtitle`, `date` (ISO) are the workbook's keys; `subtitle` and `date` are read
  with `FrontMatter.Value`, so a later shared handling of them does not conflict. Keys of other formats
  (`showTagline`, `embedFonts`, `showFooter`, `transition`, `build`, `numberChapters`, `listBullets`, `tone`, and the
  document keys) are accepted and ignored; `styles.*` and `layouts.*` are not. `XlsxOptions.Audience` is new, for the
  tool's `audience` parameter.
- **Structure details beyond the plan:** a `##` without a table is no sheet (`content-ignored` on its heading);
  paragraphs after a section's last table are `content-ignored`; a `Table:` paragraph elsewhere is `caption-ignored`
  (now also listed for workbooks, as are `language-unknown`, `classification-kept` and `audience-omitted`). Heading
  attributes on `#` and `##` are checked as in the other formats and otherwise ignored.
- **Guide.** `AuthoringGuide/Xlsx.md` (about 2,000 words, test limit 2,500) with its own front-matter table (the shared
  one describes typography, highlighting and cover images, which workbooks do not have) and the codes split into
  errors and notes. Its examples generate without notes; it is compared with `Samples/authoring-guide.expected.md`.
  The docs page shows `Samples/features.md` verbatim, checked by a test; its preview is
  `Samples/features.preview.expected.html`.
- **Not done:** `DocumentTool`'s own tests and guide do not offer `xlsx` by default (a host passes
  `XlsxGenerator.ForOptions`), so the tool guide's expected files are unchanged. Typography and coloured words are not
  applied to cells.
- **Verification concerns:** open `.tmp/review/markdown-features.xlsx` in Excel (written by the tests); check that
  text kept in a number column looks acceptable to a reader.
- **Breaking changes:** the error message of a `chart` block with `category`, `series`, `placement` or `sheet` in decks,
  PDFs and Word documents (recorded in the changelog). Workbooks from the model are unchanged.
