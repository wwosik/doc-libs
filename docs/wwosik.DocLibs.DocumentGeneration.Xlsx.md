# wwosik.DocLibs.DocumentGeneration.Xlsx

Generates Excel workbooks (`.xlsx`) from a typed model, in a brand's colours and fonts: an info
sheet first, then one table per sheet with real, typed values in the culture's number formats, a
frozen and filtered header row, banding that survives sorting, totals that follow the filter,
formulas checked before they are written, and print setup. `Check` reports
every problem of a workbook at once, `Preview` shows it as plain HTML. Cross-platform, no Office
needed. Depends on the Open XML SDK and
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md). AOT-compatible.

```csharp
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Xlsx;

var workbook = new XlsxWorkbook
{
    Title = "Quarterly figures",
    Author = "Finance",
    Language = "de",
    Sheets =
    [
        new XlsxSheet
        {
            Name = "Sales",
            Title = "Sales by region",
            Columns =
            [
                new XlsxColumn("Region"),
                new XlsxColumn("Units", XlsxColumnType.Integer),
                new XlsxColumn("Revenue", XlsxColumnType.Currency) { Currency = "EUR" },
                new XlsxColumn("Share", XlsxColumnType.Percent) { Decimals = 1 },
                new XlsxColumn("Since", XlsxColumnType.Date),
                new XlsxColumn("Per unit") { Total = XlsxTotal.Average },
            ],
            Rows =
            [
                ["North", 1200, 15000.50m, 0.25, new DateOnly(2024, 1, 1), XlsxCell.Formula("[Revenue]/[Units]")],
                ["South", 800, 9800m, 0.18, new DateOnly(2025, 6, 1), XlsxCell.Formula("[Revenue]/[Units]")],
            ],
        },
    ],
};

using var output = File.Create("figures.xlsx");
GenerationResult result = XlsxGenerator.Generate(workbook, output, new XlsxOptions { Brand = BrandPack.Load("brands/acme") });
```

- **Stream in, stream out.** Nothing is read from or written to disk.
- **Errors:** a workbook with problems throws `DocumentConversionException` with `Format = Xlsx`,
  listing the first ten; so does a brand name the options do not know, or a language that is not a
  language tag. An invalid brand throws `ArgumentException`.
- **Result:** `Generate` returns a `GenerationResult`; its `Pages` is the number of sheets, the info
  sheet included, and its notes are `character-missing`, `language-unknown`,
  `classification-kept` and `function-unknown`.
- **Deterministic:** the same workbook, options and clock give the same bytes: relationship ids are
  fixed, and every entry of the package has the same time.
- **Core properties:** the title, the subtitle as subject, the author as creator, the description,
  the language, and the created and modified dates from `TimeProvider`. The marking is never a
  property.

## Model

All types are sealed records in `wwosik.DocLibs.DocumentGeneration.Xlsx`.

| Type | Holds |
| ---- | ----- |
| `XlsxWorkbook` | `Title`, `Subtitle`, `Description`, `Author`, `Date` (default: today), `Language` (BCP 47, default `en`), `Marking` (`XlsxMarking(Classification, Draft)`), `Brand` (a name), `Sheets` |
| `XlsxSheet` | `Name` (the tab), `Title` and `Description` above the table, `Columns`, `Rows`, `TotalsLabel` (default `GeneratedTexts.Total`, "Total" / "Summe") |
| `XlsxColumn(Header, Type)` | `Type` (default `Text`; a column of formulas `Number` or `Currency`, see [Formulas](#formulas)), `Total` (`Sum`, `Average`, `Count`, `Min`, `Max`), `Decimals` (0–10, for `Number`, `Currency`, `Percent`), `Currency` (ISO 4217, only on `Currency`), `Width` (1–255 characters; measured without), `Align` (`Left`, `Center`, `Right`; text left, booleans centred, numbers and dates right without) |
| `XlsxColumnType` | `Text`, `Number`, `Integer`, `Currency`, `Percent`, `Date`, `DateTime`, `Boolean` |
| `XlsxCell` | text, a number, a date, a date and time, a truth value, a formula (`XlsxCell.Formula("[Price]*[Quantity]")`), or `XlsxCell.Empty`; converts implicitly from `string`, `int`, `long`, `double`, `decimal`, `DateOnly`, `DateTime` and `bool` (a `null` or empty string is empty, as is a `null` cell) |

A sheet name follows Excel's rules: 1 to 31 characters, none of `[]:*?/\`, no apostrophe at either
end, unique ignoring case. A row has at most as many cells as there are columns; a short row is
filled with empty cells. A `Percent` value is a fraction: `0.25` is shown as 25 %. A `Date` cell
has no time; a `Date` column also takes a `DateTime` at midnight, a `DateTime` column also a `Date`.
An `Integer` value has no decimals.

## Options

| Option | Default | |
| ------ | ------- | --- |
| `Brand` | `Brand.Neutral` | the brand, unless the workbook names another |
| `Brands` | none | further brands the workbook may name |
| `Info` | `null` | the info sheet; `null`: when the workbook has a title or a description |
| `ShowDate` | `true` | the date on the info sheet and above every table |
| `Culture` | the language's | the culture of the number and date formats; a neutral language is its main country (`de` → `de-DE`) |
| `Texts` | none | `GeneratedTexts` for further languages, by tag |
| `Marking` | none | the host's marking; the workbook's can raise it, not lower it (`classification-kept`) |
| `PageSize` | `A4` | `A4`, `A3` or `Letter` when printed |
| `TimeProvider` | `TimeProvider.System` | today's date and the created and modified dates |

## Info sheet

First, named `GeneratedTexts.Info` ("Info"; numbered, "Info 2", when a sheet already has the name):
the logo, the marking, the title, subtitle and description, then author, date and language
(`GeneratedTexts.Author`, `Date`, `Language`), and the contents (`GeneratedTexts.Contents`) with a
link to each sheet and its title or description beside it. The marking shows only here and in the
printed page header.

## Table sheets

- The sheet's title, its description and the date above the table, then a blank row; a sheet with
  neither title nor description starts with the header row in row 1, without a date.
- The header row in `TableHeader` with white bold text, frozen, with an autofilter.
- Banding in `TableBand` as a conditional format `MOD(ROW()-h,2)=0` (`h` the header row), so it
  stays right after sorting; thin lines in `TableLine`; no gridlines.
- Widths are measured in the brand's table font (estimated without brand fonts), from the header
  and the shown values of the first 10,000 rows: 6 to 50 characters, unless the column sets one.

## Totals

A column with a `Total` gets it in a totals row right under the table: `Sum`, `Average`, `Count`,
`Min` or `Max`, written as `SUBTOTAL(109|101|103|105|104, range)` over the data rows, so a filter
changes the totals to the visible rows. `Count` counts the cells that are not empty, and is the only
total of a text or boolean column (others are `column-invalid`). A sum, minimum, maximum and average
have the column's format, an average of an `Integer` column the culture's decimals, a count a whole
number.

- The row is bold on a light shade of the header's colour, with the table's lines; it is outside the
  filter and the banding.
- Its label, `TotalsLabel` or `GeneratedTexts.Total`, stands in the first column without a total; when
  every column has one, there is no label.
- Every total carries its value, computed by the generator, so a viewer that does not calculate shows
  it too; Excel recalculates it anyway. A total over formulas has no value, except a count: the
  generator does not compute formulas. An average of no numbers is `#DIV/0!`, as in Excel.
- A sheet without rows has no totals row.

## Formulas

`XlsxCell.Formula("…")` holds a formula in Excel's English form, with commas between arguments and
English function names, with or without the leading `=`:

- `[Header]` is the cell of the column with that header in the same row: `[Price]*[Quantity]` in the
  third data row of a sheet whose header is row 4 is written `B7*C7`. Headers are matched ignoring
  case and the spaces around them; inside the brackets, `'` takes the next character as it is
  (`[Size '[cm']]`).
- `{row}` is the number of the row: `SUM(B{row}:D{row})`.
- A1 references (`A1`, `$B$2`, `A1:C9`, `B:B`, `3:3`) are kept as written. References to other sheets
  and to Excel tables are not supported yet (`formula-invalid`).
- Function names are written in upper case. Those newer than Excel 2007 get the prefix the file needs,
  or Excel shows `#NAME?`: `XLOOKUP` is stored as `_xlfn.XLOOKUP`, `FILTER` and `SORT` as
  `_xlfn._xlws.…`, the names of `LET` and `LAMBDA` as `_xlpm.…`; `SUM` stays `SUM`. A prefix written by
  hand is taken as it is.
- The workbook stores no value for a formula: with any formula other than the totals, it asks Excel to
  calculate everything on opening (`fullCalcOnLoad`). A viewer that does not calculate shows such cells
  empty. A workbook with no formulas but totals does not ask.

**Checks.** Before writing, a parser of Excel's grammar reads every formula: numbers, strings,
`TRUE`/`FALSE`, error values, references and ranges, operators by precedence (comparison, `&`, `+ -`,
`* /`, `^`, `%`, signs, `:`), function calls, array constants `{1,2;3,4}`, and functions nested at most
64 deep. It knows about 450 functions with their numbers of arguments. As `formula-invalid` it reports,
with the character it happens at:

- a formula that does not parse, or calls a function with too few or too many arguments;
- a `[Header]` that names no column (with the closest headers as a suggestion), or two columns;
- a bare name such as `Price*2`, which the workbook does not define (with `[Price]` as a suggestion when
  there is such a column);
- a circular reference within a row: `[A]` uses `[B]`, which uses `[A]`, or a cell's own A1 reference.
  Circles across rows are Excel's to find;
- more than 8,192 characters is `excel-limit`.

A function the generator does not know is only a warning, `function-unknown`, once per function: Excel
or an add-in may know it. It is written as given.

**Types.** A column of formulas has its declared type. Without one, it is `Number`, or `Currency` when
its formulas refer, through `[Header]`, only to `Currency` columns of one currency (also through other
formula columns), or when it names a `Currency` itself. Formulas are not checked against the column's
type.

## Values

Numbers, dates and booleans are typed cells, never formatted text. Each column's number format is
built from the culture's `NumberFormatInfo` and `DateTimeFormatInfo`: decimals, the thousands
separator, the currency symbol and its position for positive and negative amounts, the percent
sign's position, the short date, and the short date with the short time.

| Culture | Currency `EUR` | Currency `USD` | Date | Percent |
| ------- | -------------- | -------------- | ---- | ------- |
| `de-DE` | `#,##0.00\ "€"` | `#,##0.00\ "$"` | `dd\.mm\.yyyy` | `0.0\ %` |
| `fr-FR` | `#,##0.00\ "€"` | `#,##0.00\ "$"` | `dd\/mm\/yyyy` | `0.0\ %` |
| `en-US` | `"€"#,##0.00` | `"$"#,##0.00` | `m\/d\/yyyy` | `0.0%` |
| `en-GB` | `"€"#,##0.00` | `"$"#,##0.00` | `dd\/mm\/yyyy` | `0.0%` |

A currency of another country takes its symbol from a culture of that country (the workbook's
language first) and its position from the workbook's culture; without `Currency`, the culture's
own currency is used. Text columns get the format `@`, and Excel's "number stored as text" warning
is off for them, the title block and the header row, so `01234` stays as written, without a
triangle. Dates before 1900 cannot be stored (`excel-limit`).

## Fonts and colours

Fonts are written by name: Excel cannot embed them and finds them on the reader's machine. Titles
are in the brand's text family, the tables in its table family. A character the brand's fonts lack
gives `character-missing`, once per character. Every colour shown as text is darkened, where needed,
until it has a 4.5:1 contrast on Excel's white sheet (the header fill against its white text); a
dark band is lightened until black text on it has the same.

## Print

Landscape, one page wide, with the header row repeated on every page (`Print_Titles`). The page
size comes from `PageSize`, the margins from the PDF generator (20 mm at the sides, 1 in at the top,
60 pt at the bottom). The page header shows the title at the left, the marking in the centre and the
brand's logo at the right as a header picture; the footer the brand's footer text at the left and
"Page n of m" (`GeneratedTexts.PageOfFormat`) at the right.

## Check

`XlsxGenerator.Check(workbook, options)` writes nothing and returns a `CheckResult` with every
problem, each naming its sheet, row, column and cell (`Sheet 'Sales', row 3, column 'Units' (B7):
…`, the row counted from the first row of data), at most 500 notes; then the notes `Generate` would
return. With problems, `Error` is the message `Generate` would throw. `Statistics` counts `Sheets`,
`Tables`, charts (`Diagrams`, 0 until charts on sheets land) and `Words`; its `Pages` is 0.

| Code | When |
| ---- | ---- |
| `cell-type` | A value does not fit its column's type: text in a number column, a number with decimals in an `Integer` column, a date with a time in a `Date` column, a number that is not finite |
| `sheet-name` | A sheet name is empty, longer than 31 characters, holds one of `[]:*?/\` or a control character, starts or ends with `'`, or repeats another ignoring case |
| `column-invalid` | A sheet has no columns, or a column has no header, decimals outside 0–10 or on a type without them, a currency on a column that is not `Currency`, an unknown currency code, a width outside 1–255, a total other than `Count` on a text or boolean column |
| `row-too-wide` | A row has more cells than the sheet has columns |
| `excel-limit` | More than 1,048,576 rows or 16,384 columns, more than 32,767 characters in a cell or 8,192 in a formula, a date before 1900, or no sheet at all |
| `formula-invalid` | A formula does not parse, names an unknown or shared `[Header]`, holds a bare name, refers to itself within its row, or gives a function the wrong number of arguments; the message names the character |
| `function-unknown` | A warning: a formula calls a function the generator does not know |

## Preview

`XlsxGenerator.Preview(workbook, options, maxRows = 200)` returns HTML for a host to embed: one
`<section>` per sheet (the info sheet first), each with an `<h2>` and a `<table>` of at most
`maxRows` rows, and a `<tfoot>` saying how many more there are. Values are shown as Excel shows them
in the workbook's culture; numeric columns carry `class="num"` on their cells, for the host to align
them right. A formula is shown as Excel shows it in the formula bar, in `<code>` (`=B7*C7`, without
prefixes), since the generator does not compute it; the totals row is a row of the `<tfoot>`, with its
values (a total over formulas as its formula). There are no styles and no scripts, and every text is HTML-encoded. Problems are not
checked: a value of the wrong kind is shown as it is.

## Known limits

- The thousands and decimal separators are written as Excel's `,` and `.` and shown in the
  reader's own Excel settings; symbols, their position, decimals and the order of a date's parts
  come from the culture. Booleans show as TRUE or FALSE in the reader's Excel language (the preview
  writes TRUE and FALSE).
- Banding follows the row number, so rows a filter hides can put two banded rows next to each
  other.
- Formulas are written as ordinary formulas. A dynamic-array function such as `FILTER`, `SORT` or
  `UNIQUE` then shows only its first value, with `@` before it in Excel's formula bar.
- References to other sheets, charts on the sheet's cells, Markdown and JSON input come in later
  stories.
