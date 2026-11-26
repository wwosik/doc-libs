# E10-S01 Workbook package: model, look, values, print, check and preview

> Status: in verification
> Epic: [E10 Workbook generator](../50-todo/E10-workbook-generator.md)

## Summary

This story adds the new package `wwosik.DocLibs.DocumentGeneration.Xlsx`. It writes a typed workbook
model as an `.xlsx` in the brand's look, with:

- an info sheet first;
- styled tables with a frozen, filtered header and banding that survives sorting;
- typed values in the culture's number formats;
- print setup.

It also adds `Check`, which reports every problem at once, and `Preview`, which shows the workbook as
plain HTML.

## Context

- **The generators** (`DocumentGeneration.Pptx`, `.Pdf`, `.Docx`) share these types in
  `src/wwosik.DocLibs.DocumentGeneration/`:
  - `Brand` and `ResolvedBrand`; the table colours are `TableHeader`, `TableBand` and `TableLine` in
    `Brand.cs`;
  - `GenerationResult` and `GenerationNote`;
  - `CheckResult` and `CheckOptions`;
  - `GeneratedTexts` (English and German);
  - `Marking` and `Classification`;
  - `IMarkdownGenerator`;
  - `TextMetrics` and `TrueTypeInfo`, which measure text with the brand's fonts.
- **Core properties** are written by `src/Shared/OpenXml/CoreProperties.cs`, shared source.
- **The only workbook written today** is the one embedded in a chart
  (`src/Shared/OpenXml/ChartWriter.cs`, `Workbook`). `DocumentConversions` only detects `.xlsx`
  (`DocumentFormat.Xlsx`).
- A brand's fonts cannot be embedded in a workbook. Excel finds them by name on the reader's
  machine.

## Proposed approach

1. **Package.** Add `src/wwosik.DocLibs.DocumentGeneration.Xlsx/` with its tests project. It
   references `DocumentGeneration` and links the shared `CoreProperties.cs`. Add it to the solution,
   the pack, the README's package table and a new `docs/` page.

2. **Model** (namespace `wwosik.DocLibs.DocumentGeneration.Xlsx`), all sealed records:
   - **`XlsxWorkbook`**: `Title`, `Subtitle`, `Description`, `Author`, `Date`, `Language`, `Marking`,
     `Brand` (name), `Sheets`.
   - **`XlsxSheet`**: `Name` (Excel's rules: at most 31 characters, no `[]:*?/\`, unique ignoring
     case), `Title`, `Description`, `Columns`, `Rows`.
   - **`XlsxColumn`**: `Header`; `Type`; `Decimals` (0–10); `Currency` (ISO 4217, only with
     `Currency`); `Width` (1–255 characters; automatic without); `Align` (left, center, right;
     default by type).
   - **`XlsxColumnType`**: `Text`, `Number`, `Integer`, `Currency`, `Percent`, `Date`, `DateTime`,
     `Boolean`.
   - **`XlsxCell`**: text, number, date, date-time, boolean or empty, with implicit conversions from
     `string`, `int`, `long`, `double`, `decimal`, `DateOnly`, `DateTime` and `bool`. Formulas come
     in S02.

3. **`XlsxGenerator`**:
   - `Generate(XlsxWorkbook, Stream, XlsxOptions?)` returns a `GenerationResult`. Errors throw
     `DocumentConversionException` listing the first ten problems.
   - `Check(XlsxWorkbook, XlsxOptions?)` returns a `CheckResult` with every problem, each naming its
     sheet, row, column and cell (`B7`), capped at 500 notes. `Statistics` counts sheets, tables,
     charts and words.
   - **`XlsxOptions`**: `Brand`, `Brands`, `Info` (default: on when the workbook has a title or a description), `ShowDate` (default `true`),
     `Culture` (overrides the language's culture), `Texts`, `Marking`, `TimeProvider`.

4. **Info sheet** (first, when the workbook has a title or a description, unless `Info` is
   `false`; the marking shows only here and in the print header, never as a custom property):
   - the logo, title, subtitle and description;
   - author, date and language;
   - the marking;
   - a contents list with a link to each sheet.

5. **Table sheet**:
   - title, subtitle and date above the table;
   - the header row in `TableHeader` with the brand's header text colour, frozen, with an autofilter;
   - banding in `TableBand` as a conditional format `MOD(ROW()-h,2)=0`, so it stays right after
     sorting;
   - thin lines in `TableLine`;
   - gridlines off.

6. **Values**:
   - Numbers, dates and booleans are typed cells with a number format built from the culture's
     `NumberFormatInfo` and `DateTimeFormatInfo`: decimals, thousands separator, the currency symbol
     and its position, percent, short date, date with time.
   - Text columns get the format `@`, and Excel's "number stored as text" warning is off for them, so
     `01234` stays as written.
   - Widths are measured with `TextMetrics` in the brand's font: 6 to 50 characters, unless the
     column sets one.

7. **Fonts and colours**:
   - Fonts are written by name.
   - A character the font lacks gives `character-missing`, as in the other generators.
   - A dark brand's colours are darkened onto Excel's white sheet, so text keeps a 4.5:1 contrast.

8. **Print**:
   - Landscape, one page wide, with the header row repeated (`Print_Titles`).
   - The page size comes from the options (A4 by default) and the margins from the PDF.
   - The page header shows the title, the marking and the logo, as a header picture.
   - The footer shows "Page n of m" from `GeneratedTexts`.

9. **Metadata**: title, subject, creator, description and language in the core properties.

10. **Preview**:
    - `XlsxGenerator.Preview(XlsxWorkbook, XlsxOptions?, int maxRows = 200)` returns HTML: one
      `section` per sheet, with a heading and a table.
    - Values are shown as Excel would show them, and numbers are right-aligned (`td.num`).
    - There are no styles and no scripts, and everything is HTML-encoded, so a host can embed it.

11. **Generated texts and notes**:
    - New `GeneratedTexts`: `Info`, `Author`, `Date`, `Language`, `PageOfFormat`, in English and
      German.
    - New note codes: `cell-type` (the value does not fit the column type), `sheet-name`,
      `column-invalid`, `row-too-wide`, `excel-limit` (1,048,576 rows, 16,384 columns, 32,767
      characters per cell).

## Acceptance criteria

- [ ] A workbook with three sheets of every column type opens in Excel without repair, and passes
      the Open XML validator in the tests.
- [ ] Sorting and filtering in Excel keep the banding and the header.
- [ ] Number and date formats follow `de-DE`, `en-US`, `en-GB` and `fr-FR` in tests. A currency
      column with `EUR` and one with `USD` show their own symbols.
- [ ] `01234` in a text column stays text, with no warning triangle.
- [ ] `Check` reports all problems of an invalid workbook in one result, each with sheet and cell.
- [ ] The same workbook and clock give identical bytes.
- [ ] `Preview` output contains no `<script>`, no `style` attribute and no unencoded user text.
- [ ] The package's `docs/` page, the README table and the CHANGELOG describe it.

## Open questions

1. Should the info sheet be on by default, or only when the workbook has a title or a description?

> On when the workbook has a title or a description; `info: false` turns it off. (2026-11-26)

2. Should the marking also be written as a sensitivity label in the custom properties, or only
   shown on the info sheet and in the page header?

> Only visible: on the info sheet and in the print header. No custom property. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- **Package:** `src/wwosik.DocLibs.DocumentGeneration.Xlsx/` with `…Xlsx.Tests`, in the solution, packed with its
  `docs/` page; `IsAotCompatible` (builds without trim/AOT warnings). Only dependency besides the shared package:
  the Open XML SDK (MIT, as in Docx), so no THIRD-PARTY notice file.
- **Writing:** the SDK only builds the package; every part is XML text fed into parts added with explicit
  relationship ids. The SDK's zip writes the current time on every entry, so the package is copied entry by
  entry with one fixed time (2000-01-01): with the same workbook, options and clock the bytes are identical (a
  test generates twice, 2 s apart). The logo is one image part shared by the info sheet and every header.
- **Model choices** where the story left them open: `XlsxMarking(Classification, Draft)` is the type of
  `XlsxWorkbook.Marking` and `XlsxOptions.Marking`; the workbook's can raise the host's, not lower it
  (`classification-kept`), like front matter. `XlsxWorkbook.Brand` is a name looked up among `neutral`,
  `XlsxOptions.Brand` and `XlsxOptions.Brands`; an unknown name throws (Check: `Error`). The default language
  is `en`; a neutral language becomes its main country's culture. A sheet's `Description` is the "subtitle"
  above its table; a sheet with neither title nor description starts with the header in row 1 and shows no
  date. `XlsxOptions.PageSize` (`A4`, `A3`, `Letter`) is new, as the story's print section asks.
- **Strictness:** a value of another kind than its column is `cell-type` (no silent conversion); `Date`
  accepts a `DateTime` at midnight and `DateTime` a `Date`. Decimals are allowed only on Number, Currency,
  Percent. Empty `XlsxSheet.Columns` and an empty workbook without info sheet are problems
  (`column-invalid`, `excel-limit`).
- **Problems vs notes:** the five new error codes come only as problems (Generate throws the first ten,
  Check returns all, capped at 500, with `Error` set to Generate's message); `character-missing`,
  `language-unknown`, `classification-kept` are notes. Notes have line 0; messages name sheet, row (from
  the first data row), column and cell. The new codes are listed in `NoteCodes.All` with the format `Xlsx`
  only, so no existing authoring guide changes. `character-missing` did not exist yet in the other
  generators; it is used by workbooks only.
- **Info sheet:** named `GeneratedTexts.Info`; if a data sheet already has that name, "Info 2" etc. The
  German "Info" equals the English, so the shared test that German differs in every text now excepts it.
  `GeneratedTexts.Contents` (existing) heads the contents list.
- **Widths** are measured with `TrueTypeMetrics` in the brand's table face when the brand has fonts, else with
  `TextMetrics`' estimate, over the first 10,000 rows (cost on huge sheets).
- **Formats:** the culture's patterns for currency (all 16 negative patterns), percent and dates are mapped
  to Excel codes with literals escaped. Separators cannot be fixed in an Excel format: Excel shows `,`/`.` in
  the reader's settings; documented as a known limit. A foreign currency's symbol comes from a culture of its
  country (the workbook's language first, then English, then by name), e.g. USD in `de-DE` gives
  `#,##0.00\ "$"`. Booleans are Excel booleans (TRUE/FALSE in the reader's language).
- **Shared changes, all additive:** `GeneratedTexts.Info/Author/Date/Language/PageOfFormat`,
  `CheckStatistics.Sheets` (in equality and first in `ToString` when > 0), six `NoteCodes`,
  `CoreProperties.Describe` (subject, description), `InternalsVisibleTo` for the Xlsx projects.
  `GenerationResult.Pages` of a workbook is its sheet count, info sheet included; `CheckStatistics.Pages` is 0.
- **Preview:** a truncated table ends in a `<tfoot>` "… +N" row (language-neutral); newlines become `<br>`.
- **Breaking changes:** none. Decks, PDFs, Word documents and the authoring guides are unchanged.
- **To verify in Excel** (not possible here): opening without repair, sorting/filtering keeping banding and
  header, no warning triangle on `01234`, the header picture and the printed header/footer, and how the
  description row's estimated height looks. Generated samples for review: `.tmp/review/every-type*.xlsx`
  after running the tests.

