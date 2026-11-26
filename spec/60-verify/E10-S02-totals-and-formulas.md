# E10-S02 Totals and formulas

> Status: in verification
> Epic: [E10 Workbook generator](../50-todo/E10-workbook-generator.md)

## Summary

A column can have a total, written as a `SUBTOTAL` row that follows the filter. A cell can hold a
formula. In a formula, `[Header]` names the cell of that column in the same row. Formulas are
checked by a parser of Excel's formula grammar before the workbook is written.

## Context

- E10-S01 writes typed values only. `XlsxCell` has no formula yet.
- Excel stores formulas in their English, invariant form (comma separators, English function names).
  Functions newer than Excel 2007 need the `_xlfn.` prefix in the file, or Excel shows `#NAME?`.
- Excel recalculates on open only when it is told to (`fullCalcOnLoad`). Without cached values, a
  viewer that does not calculate (a preview, a converter) shows empty cells.

## Proposed approach

1. **Totals**:
   - `XlsxColumn.Total`: `Sum`, `Average`, `Count`, `Min`, `Max`.
   - A sheet with any total gets a totals row under the table, written as
     `SUBTOTAL(109|101|103|105|104, range)`, so a filter changes the totals to the visible rows.
   - The totals row has cached values, styled like the header but lighter.
   - Its label is `XlsxSheet.TotalsLabel`, else `GeneratedTexts.Total` ("Total" / "Summe"), in the
     first column without a total.
   - A total on a text or boolean column is a `column-invalid` error, except `Count`.

2. **Formulas**:
   - `XlsxCell.Formula("…")` holds a formula, written without the leading `=`.
   - **References:**
     - `[Header]` is the cell of the column with that header in the same row.
     - `{row}` is the row number.
     - A1 references are allowed as written.
     - References to another sheet are E10-S06.
   - **No evaluator:** ordinary formulas are written without a cached value; Excel computes them on
     opening. Only totals carry cached values.
   - **Function names** are English. `_xlfn.` is added for functions that need it (`XLOOKUP`,
     `IFS`, `CONCAT`, `TEXTJOIN`, …) from a table in the parser.
   - `fullCalcOnLoad` is set only when a workbook has formulas other than totals.

3. **Parser** (`XlsxFormula`, internal):
   - It tokenises and parses Excel's grammar: numbers, strings, booleans, errors, references and
     ranges, operators by precedence, `%`, unary minus, function calls and array constants.
   - It knows about 200 functions with their argument counts.
   - A formula that does not parse is a `formula-invalid` error, naming the position.
   - An unknown function is a `function-unknown` warning: Excel may know it, or an add-in may.
   - A `[Header]` that names no column is an error that suggests close names.
   - A circular reference within a row (`[A]` uses `[B]`, which uses `[A]`) is an error.

4. **Values of formula columns**:
   - A formula column's type is the declared type.
   - Without a declared type, it is `Number`, or `Currency` when the formula refers only to currency
     columns.

5. **Preview**: formulas are shown in `code`, and the totals row in `tfoot` with its cached values.

## Acceptance criteria

- [ ] Filtering a table in Excel changes its totals to the visible rows.
- [ ] `[Price]*[Quantity]` gives the product per row in Excel and in the preview's cached values.
- [ ] `XLOOKUP` is written with `_xlfn.` and works in Excel. `SUM` is written without it.
- [ ] Invalid formulas, unknown columns and circular references are reported by `Check` with sheet,
      cell and position.
- [ ] A workbook without formulas other than totals has no `fullCalcOnLoad`.

## Open questions

1. Should the parser compute cached values for ordinary formulas (a small evaluator for arithmetic
   and the common functions), or leave them empty and rely on `fullCalcOnLoad`? The preview shows
   them only if computed.

> No evaluator: ordinary formulas get no cached value and the workbook sets `fullCalcOnLoad`; totals keep their cached values. The preview shows the formula. (2026-11-26)

2. Cross-sheet references `[Sheet!Header]`: include now, or only same-row references at first?

> Later: only same-row references now; cross-sheet references are E10-S06. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- **Model.** `XlsxColumn.Total` (`XlsxTotal`: `Sum`, `Average`, `Count`, `Min`, `Max`), `XlsxSheet.TotalsLabel`,
  `XlsxCell.Formula(string)` (kind `XlsxCellKind.Formula`, the formula in `Text`, a leading `=` dropped, empty throws
  `ArgumentException`), `GeneratedTexts.Total` ("Total" / "Summe"). To let "a formula column without a declared
  type" exist, `XlsxColumn.Type` became `XlsxColumnType?` (default `null` = `Text`, or `Number`/`Currency` for a
  column with formulas). The package is unreleased, so this is no breaking change; existing calls compile as before.
- **Totals.** One row right under the data, `SUBTOTAL(109|101|103|105|104,X{first}:X{last})`, outside the
  autofilter and the banding, inside the dimension. Cached values are computed from the typed values (dates as
  serials; text and booleans ignored as `SUBTOTAL` does; `Count` counts non-empty cells). A total over a column
  holding formulas has no cached value (no evaluator), except `Count`. An average of nothing is the cached error
  `#DIV/0!` (`t="e"`). Formats: the column's; `Count` a whole number; `Average` of an `Integer` column the culture's
  number format. Style: bold table font, black, on the header colour mixed 30 % on white and lightened until black
  text has 7:1 (`SheetColors.Totals`), with the table's lines. **Decision:** a sheet without rows gets no totals row
  (a range over no rows would be the header or the totals cell itself). The label goes in the first column without
  a total; when every column has one, no label.
- **Formulas.** `XlsxFormula` (internal) lexes and parses by recursive descent: numbers, strings, `TRUE`/`FALSE`,
  ten error values, A1 cells (with `$`, and `{row}` in place of the row), column ranges `B:B`, row ranges `3:3`,
  `[Header]` (with `'` as escape, as in Excel's table references), `{row}`, operators by precedence, `%`, signs,
  `:` (only between references), intersection by space, unions in parentheses, function calls with missing
  arguments, array constants with equal row lengths, nesting up to 64. The function table (`XlsxFunctions.cs`) has
  about 450 functions with argument counts: Excel 2007's without prefix, newer ones with `_xlfn.`, `FILTER`/`SORT`
  with `_xlfn._xlws.`; names inside `LET`/`LAMBDA` get `_xlpm.`. Prefixes the author wrote are normalised. Parsed
  formulas are cached per distinct text in the plan, so a million rows of the same formula parse once.
- **Checks.** All formula problems are `formula-invalid` (no further codes): parse errors with the character, a
  wrong number of arguments, an unknown `[Header]` (with up to three close headers by edit distance or containment,
  else the list of headers), a header two columns share, a bare name (suggesting `[Name]` when it is a header), and
  circular references within a row — through `[Header]` and through A1 references to the same row, one per row,
  shown as `[A] → [B] → [A]`. Over 8,192 characters is `excel-limit`. Unknown functions are the warning
  `function-unknown` (a note, once per function per workbook, with close function names); they are written as given.
- **Deviation:** references to other sheets (`Sheet!A1`, `'Sheet'!A1`) and to Excel tables (`Table[Col]`) are
  `formula-invalid` for now, rather than passed through, until E10-S06 defines them.
- **Types.** A column without a declared type and with formulas is `Number`; `Currency` when it names a currency
  itself, or when all its formulas' `[Header]`s point to `Currency` columns of one currency (spread through other
  formula columns to a fixed point). Formulas are not checked against the column type. Widths of formula columns
  leave room for a value like 123,456.78 in the column's format; totals and the label are measured too.
- **Writing.** Formula cells are `<c><f>…</f></c>` without a value; `calcPr` gets `fullCalcOnLoad="1"` only when
  any formula other than the totals exists.
- **Preview.** Formulas in `<code>` as Excel's formula bar shows them (`=B7*C7`, no prefixes; the source when it
  does not parse); the `<tfoot>` holds the "… +N" row, then the totals row with cached values, a total over formulas
  as its `SUBTOTAL` formula in `<code>`.
- **Acceptance criterion 2** says the preview shows the product per row as a cached value; per the answer to open
  question 1 (no evaluator) the preview shows the formula instead, and Excel computes it on opening.
- **Known limits:** dynamic-array functions (`FILTER`, `SORT`, `UNIQUE`, …) are written as ordinary formulas, so
  Excel shows only their first value with `@`; spilling would need array metadata. Circular references across rows
  are left to Excel.
- **Verification concerns:** opening in Excel (filter changes the totals; `XLOOKUP` works; no repair prompt) was
  not possible here; every generated workbook passes the Open XML validator (Microsoft 365). For review:
  `.tmp/review/totals-and-formulas.xlsx` after the tests.
- **Breaking changes:** none (the workbook package is unreleased; decks, PDFs and Word documents do not change).
