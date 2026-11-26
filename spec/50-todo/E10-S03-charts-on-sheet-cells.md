# E10-S03 Charts on sheet cells

> Status: todo
> Epic: [E10 Workbook generator](E10-workbook-generator.md)

## Summary

A sheet can carry native charts whose series refer to the table's own cells. The chart either stands
beside the table or fills a chart sheet of its own. The shared `ChartWriter` gets an overload that
takes cell references instead of an embedded workbook. Charts in decks and Word documents stay as
they are.

## Context

- `src/Shared/OpenXml/ChartWriter.cs` (internal, linked into the PPTX and DOCX generators) writes a
  `ChartSpec` (`src/wwosik.DocLibs.DocumentGeneration/CodeBlockRenderer.cs`) as a chart part with an
  embedded workbook (`Sheet1`). Its `c:cat` are always string references and its values use the
  format `General`.
- `ChartSpec` has `Kind`, `Categories` and `Series`, plus title, unit and stacking.
- Chart colours come from the brand's `Colors.Diagram` palette.

## Proposed approach

1. **Model**:
   - **`XlsxChart`**:
     - `Category` (a column header);
     - `Series` (column headers);
     - `Kind` (`Column`, `Bar`, `Line`, `Area`);
     - `Stacked`, `Title`, `Unit`, `Source`;
     - `Placement` (`Beside`, `Sheet`);
     - `SheetName` (for `Sheet`; default `GeneratedTexts.Chart` with a number).
   - `XlsxSheet.Charts` holds a sheet's charts.
   - The series must be number, integer, currency or percent columns, and the category any column.

2. **`ChartWriter` refactor**:
   - Add a record `ChartCells`: the sheet name, the category range, a value range per series, and
     the header cells for the series names.
   - Add an overload `Write(ChartPart, ChartSpec, ChartCells, …)` that writes `c:f` references with
     a value cache (`c:strCache` / `c:numCache`) and no `c:externalData`.
   - Numeric and date categories become `c:numRef` with their format.
   - The values' number format is source-linked, so the axis shows the column's format.
   - `Source` becomes a second, smaller paragraph of the chart title.
   - `ChartCells` and the overload are shared, not internal to workbooks, so decks and Word documents
     can later draw a chart from cells of a workbook the caller passes.
   - The existing `Add` keeps its output byte for byte. A test compares a deck's and a Word
     document's chart part before and after.

3. **Placement**:
   - `Beside` anchors the chart in a drawing part right of the table, 8 × 16 cells. Several charts
     stack downwards.
   - `Sheet` writes a chart sheet after the table's sheet, linked from the info sheet's contents.

4. **Colours** come from the diagram palette in series order. More series than colours give the
   warning `chart-colors-repeat`.

5. **Checks**: an unknown column, a text column as a series, an empty table or a chart sheet name
   that collides with another sheet give `chart-invalid`.

6. **Preview**: a chart is a line naming its kind, title and series. Charts are not drawn.

## Acceptance criteria

- [ ] Changing a value in the table changes the chart in Excel.
- [ ] A chart with date categories shows dates on the axis, and a currency series shows the
      currency on the value axis.
- [ ] Charts beside the table and on their own sheet both open without repair.
- [ ] Deck and Word chart parts are unchanged, by test.
- [ ] `Check` reports chart problems with the sheet and the chart's number.

## Open questions

1. Should a deck or Word document be able to use the same `ChartCells` path later, for example when
   a chart's data comes from a workbook the caller passes? Or does the overload stay internal to
   workbooks?

> Usable later by decks and Word documents too: the `ChartCells` overload is shared, not internal to workbooks. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
