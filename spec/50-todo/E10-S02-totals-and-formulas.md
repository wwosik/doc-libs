# E10-S02 Totals and formulas

> Status: todo
> Epic: [E10 Workbook generator](E10-workbook-generator.md)

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
     - `[Sheet!Header]` is a column on another sheet, as a whole column range, for lookups.
     - `{row}` is the row number.
     - A1 references are allowed as written.
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

>

2. Cross-sheet references `[Sheet!Header]`: include now, or only same-row references at first?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
