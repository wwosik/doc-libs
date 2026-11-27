# E10 Workbook generator

> Status: todo

## Goal

A new package, `wwosik.DocLibs.DocumentGeneration.Xlsx`, that writes Excel workbooks (`.xlsx`) in
the brand's look. It takes a typed model, Markdown or JSON. A workbook has:

- an info sheet;
- tables with real, typed values, a filter and totals;
- formulas;
- native charts on the sheet's own cells;
- print setup;
- a plain HTML preview.

## Why

Many results are tables that the reader wants to sort, filter and calculate with, not read. Today the
generators write such tables into decks, PDFs and Word, and the reader copies them into Excel by
hand. The libraries already write workbooks, but only inside native charts. The brand already has
table colours (`TableHeader`, `TableBand`, `TableLine`), a diagram palette and a marking.

## Scope

| Story | Title | State |
| ----- | ----- | ----- |
| [E10-S01](../60-verify/E10-S01-workbook-package-model-and-look.md) | Workbook package: model, look, values, print, check and preview | in verification |
| [E10-S02](../60-verify/E10-S02-totals-and-formulas.md) | Totals and formulas | in verification |
| [E10-S03](../60-verify/E10-S03-charts-on-sheet-cells.md) | Charts on sheet cells | in verification |
| [E10-S04](../60-verify/E10-S04-workbooks-from-markdown.md) | Workbooks from Markdown | in verification |
| [E10-S05](E10-S05-workbooks-as-json-and-structured-tool-input.md) | Workbooks as JSON, and structured input for the document tool | todo |
| [E10-S06](E10-S06-cross-sheet-references.md) | Cross-sheet references in formulas | to plan |

Order: S01 first; S02 and S03 in any order; S04 after S01 (the totals, formulas and charts of S02 and
S03 come into Markdown as they land); S05 last, because the JSON schema describes the whole model.

## Decisions

- **No new dependency.** The Open XML SDK writes the workbook, and System.Text.Json writes the JSON
  and its schema. The package is AOT-compatible like the other generators.
- **Values are real values.** Numbers, dates and booleans are stored typed, with an Excel number
  format. They are never written as formatted text.
- **Number formats come from a `CultureInfo`**: the document's language, or the culture the caller
  passes. There is no fixed list of locales.
- **Currency is one column type with an ISO 4217 code**: `Currency` in the model, `{.currency}` or
  `{.currency=EUR}` in Markdown, `"type": "currency", "currency": "EUR"` in JSON. Without a code, the
  culture's currency is used. The currency's symbol and position come from the culture.
- **The generator follows the others.** `Generate` returns a `GenerationResult` and throws
  `DocumentConversionException` on errors. `Check` reports every problem at once. The note codes are
  kebab-case and listed in the authoring guide.
- **Charts stay native and editable.** In a workbook they refer to the sheet's own cells instead of an
  embedded workbook. Charts in decks and Word documents do not change.
- **Output is deterministic.** The same input and the same clock give the same bytes.

## Acceptance

- Every story in the scope table is completed.
- Workbooks open in Excel without repair and pass the Open XML validator.
- Existing decks, PDFs and Word documents do not change unless a story says so and records it as a
  breaking change.

## Open questions

1. None yet.

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
