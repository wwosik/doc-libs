# E10-S03 Charts on sheet cells

> Status: in verification
> Epic: [E10 Workbook generator](../50-todo/E10-workbook-generator.md)

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

## Implementation notes

- **Model.** `XlsxSheet.Charts` of `XlsxChart` (`Category`, `Series`, `Kind` `XlsxChartKind.Column|Bar|Line|Area`,
  `Stacked`, `Title`, `Unit`, `Source`, `Placement` `XlsxChartPlacement.Beside|Sheet`, `SheetName`). `SheetName` is
  ignored beside the table; a blank one gets the default. `Stacked` has no effect on lines.
- **Shared writer.** `src/Shared/OpenXml/ChartCells.cs` (new, internal like `ChartWriter`, linked into PPTX and
  DOCX by their `*.cs` glob and now into XLSX with `ChartWriter.cs`): the sheet, the category range, a value range
  and a name cell per series, plus `CategoryNumbers`/`CategoryFormat` (numeric and date categories as `c:numRef`),
  `ValueFormats` (each series' `c:numCache` format), `Horizontal` and `Note`. `ChartWriter.Write(ChartPart, ChartSpec,
  ChartCells, text, grid, font)` feeds a part the caller added (so workbooks keep explicit relationship ids) with
  `c:f` references, caches, no `c:externalData`; the value axis is `sourceLinked` to the first series' format, the
  category axis to the categories' format when they are numbers or dates. `Add` takes the same code with
  `cells = null`: every branch falls back to its old string.
- **Deviation:** `Horizontal` (bars across) and `Note` (the source line, already worded with
  `GeneratedTexts.Source`) live on `ChartCells`, not on the public `ChartSpec`, so the shared package's API does not
  grow for one generator. `ChartSpec.Source` is still set on workbook charts but the writer uses `Note`. Bars across
  put the first category on top (`orientation maxMin`) with the value axis at the bottom (`crosses max`).
- **Unchanged decks and Word documents.** `ChartPartTests` in the PPTX (neutral, sample) and DOCX (neutral,
  sample, custom) tests compare every chart part and its embedded sheet's XML (four charts: every kind, stacked,
  title with `&`, unit, source, an empty and a negative value) with `Samples/chart-parts-*.expected.md`. Those were
  written by a commit of their own on the unchanged writer, before the refactor; the SDK's random relationship ids
  are masked.
- **Placement.** Beside: one drawing part on the table sheet as `rId2` (the header picture's VML is `rId1`), chart
  parts `rId1…` in it; `twoCellAnchor` from the column after a blank one right of the table and from the first data
  row (below the frozen header), 8 × 16 cells, 17 rows apart. Sheet: a chart sheet right after its table sheet (the
  sheets' relationship ids and `localSheetId`s count chart sheets), `zoomToFit`, landscape page setup, the table
  sheets' header text without the logo picture, and the footer; its drawing holds Excel's default absolute size.
  Default names "Chart 1", "Chart 2" (`GeneratedTexts.Chart`, "Diagramm" in German), the next free number past any
  taken name; the info sheet's own name now also avoids explicit chart sheet names.
- **Data.** Ranges cover the data rows only (not the totals row). The cache holds the typed values (dates as
  serials); formula cells and text in a series have no cached point. Colours are `ResolvedBrand.Diagram.Palette`
  in series order; more series than colours give `chart-colors-repeat` (a warning, no line).
- **Checks.** `chart-invalid` (new code, with `chart-colors-repeat` in `NoteCodes.All` and both docs tables): no or
  unknown category or series header (with the closest headers), a header two columns share, a series that is not
  `Number`/`Integer`/`Currency`/`Percent` (formula columns count by their resolved type), no series, no rows, an
  undefined kind or placement, a chart sheet name that breaks the sheet-name rules (the same rules as `sheet-name`,
  now in `NameRules`) or is taken ignoring case. Messages start `Sheet 'Sales', chart 2: …`. A chart with problems
  is left out of the plan; a null chart throws `ArgumentNullException` like a null sheet.
- **Counts.** `CheckStatistics.Diagrams` counts the charts; `Sheets` and `GenerationResult.Pages` include chart
  sheets.
- **Preview.** A chart beside the table is a `<p>` after its table, a chart sheet a `<section>` with the sheet's name
  as heading: "Chart – Column – Revenue – Revenue, Units" (`GeneratedTexts.Chart`, the kind's English name, the
  title, the series). The separator is an en dash because `WebUtility.HtmlEncode` would turn `·` into an entity.
- **Loose ends.** Bars and areas keep `c:min` from the written values, as in decks: a value made negative in Excel
  later falls below the axis until the minimum is reset. The info sheet links a chart sheet as `'Chart 1'!A1`, like
  a worksheet; whether Excel follows such a link to a chart sheet is to be checked. A deck or Word chart on cells of
  a caller's workbook is not wired up; only the writer's path exists.
- **Verification concerns:** opening in Excel (a changed value redraws the chart, date axis shows dates, the
  currency shows on the value axis, no repair prompt for both placements) was not possible here; every generated
  workbook passes the Open XML validator. For review: `.tmp/review/charts-beside.xlsx` and
  `.tmp/review/charts-sheet.xlsx` after the tests.
- **Breaking changes:** none (the workbook package is unreleased; deck and Word chart parts are unchanged by test;
  workbooks without charts are unchanged).
