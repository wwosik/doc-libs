# Workbooks and small additions

> Date: 2026-11-26. Subject: features doc-libs should gain next, beyond the
> [feature gaps of 2026-11-25](2026-11-25%20feature-gaps.md).

## Summary

- **The big new feature is an Excel workbook generator.** It takes a typed model, Markdown or JSON,
  and writes `.xlsx` in the brand's look:
  - an info sheet;
  - styled tables with filters and totals;
  - formulas;
  - native charts that refer to the sheet's cells;
  - print setup;
  - a preview as HTML.

  It needs no new dependency (Open XML SDK only). doc-libs today only detects `.xlsx` and writes the
  workbook embedded in a chart.
- **Three small additions:**
  - a cover image from front matter;
  - the Markdown line each HTML-deck slide starts at, plus a message that jumps the deck to a slide,
    for editor previews;
  - an AI tool that accepts structured JSON input next to Markdown.
- **Decision (2026-11-26): take all of it,** together with every gap of 2026-11-25. The stories are
  in `spec/50-todo/`.

## Workbook generator

| Part | Plan |
| ---- | ---- |
| Model | **Workbook:** title, subtitle, description, author, date, language, marking, brand, sheets.<br>**Sheet:** name, title, description, columns, rows, totals label, charts.<br>**Column:** header, type (text, number, integer, currency with an ISO code, percent, date, date-time, boolean), decimals, width, alignment, total (sum, average, count, min, max).<br>**Cell:** text, number, date, boolean, formula or empty.<br>**Chart:** category, series, kind, stacked, title, unit, source, placement beside the table or on a sheet of its own. |
| API | `Generate(workbook, stream, options)` returns a `GenerationResult`. `Check(workbook, options)` returns every problem, by sheet, row, column and cell. |
| Look | An info sheet first: logo, title, description, author, date, language, marking, and a linked contents list. Each sheet has a title block and a header row in `Colors.TableHeader`, frozen and filtered. Bands are a conditional format, so they survive sorting. Table lines are drawn and gridlines are off. |
| Values | Real values, with number formats from a `CultureInfo`. Text columns use the `@` format, so `01234` stays as written. Widths are automatic unless set. |
| Totals | `SUBTOTAL` rows with cached values, so a filter changes the totals. |
| Formulas | `[Header]` is the same row's cell in that column, and `{row}` is the row number. Function names are Excel's English ones, with the `_xlfn.` prefix where Excel needs it. A parser of the formula grammar finds unknown functions and circular references within a row. |
| Charts | Native charts on the sheet's own cells. This needs a refactor of `src/Shared/OpenXml/ChartWriter.cs`; deck and Word charts stay as they are. |
| Print | One page wide, print titles. The header has the title, marking and logo; the footer has "Page n of m". |
| Fonts | Referenced by name, because Excel cannot embed them. A character the font lacks gives `character-missing`. |
| Preview | Plain HTML, one section per sheet, with values shown as Excel would show them. |
| Markdown | `##` per sheet, and the sheet's first table is its data. Attributes in header cells (`{.currency total=sum}`) set the type and total. Types are otherwise inferred from the values in the document's language. A `chart` block names columns. |
| JSON | A schema generated from the model, and problems reported with their JSON path. `DocumentTool` accepts this structured input. |

## Small additions

- **Cover image:**
  - `coverImage` and `coverImageSize: small|medium|large` in front matter.
  - Applies to the cover of decks, HTML decks, PDFs and Word.
  - Without a cover, the note is `cover-image-ignored`.
- **Slide lines:**
  - `GenerationResult.SlideLines` gives the Markdown line each slide of an HTML deck starts at.
  - A jump-to-slide message joins the deck's existing `app: 'html-deck'` protocol.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
