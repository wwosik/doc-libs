## Charts

A code block with the language `chart` becomes a chart in the design's colours: bars, lines or areas.
Settings come first, one `key: value` per line, then a line `---`, then the data as CSV: a header
row (the name of the category column, then one name per series) and one row per category.

````markdown
```chart
type: bar
title: Orders by month
unit: k
---
month, plan, actual
Jan, 120, 132
Feb, 118, 121
Mar, 125, 140
```
````

- **Settings:** `type` is `bar` (the default), `line` or `area`; `stacked: true` stacks bars or areas;
  `title` and `unit` are optional.
- **Values** are plain numbers with a decimal point and no thousands separator (`1250.5`); an empty
  cell leaves a gap. Up to about twelve categories and five series stay readable.
- A chart is placed like an image: it fills the room it has. Two charts side by side: `::: columns`
  with a chart in each column.
- A mistake in the settings or the data is an error naming the line of its block.

### Financial charts

A code block with the language `financial-chart` holds one of the charts of a performance review as
JSON: `development`, `growth-by-row`, `actual-vs-reference`, `growth-trend`, `two-year` or
`summary-table`. Growth rates and shares are fractions (`0.05` is 5 %); `lastActual` counts months
from 0. These blocks are usually written by a program, not by hand.

````markdown
```financial-chart
{
  "type": "growth-trend",
  "title": "Orders growth",
  "lastActual": 2,
  "reference": 0.05,
  "months": [
    { "label": "Jan", "change": 0.056 }, { "label": "Feb", "change": 0.054 }, { "label": "Mar", "change": 0.04 },
    { "label": "Apr", "change": 0.045 }, { "label": "May", "change": 0.05 }
  ],
  "options": { "labels": { "current": "This year", "previous": "Last year", "projection": "Forecast" } }
}
```
````

- **Options:** `culture` (`pl-PL` writes `5,2%`), `scale` and `unit` (`1000000` and `M`), `labels`
  (`previous`, `current`, `reference`, `projection`, `estimate`, `change`, `overall`, `value`).
- A misspelt property or invalid JSON is an error naming the line of its block.
