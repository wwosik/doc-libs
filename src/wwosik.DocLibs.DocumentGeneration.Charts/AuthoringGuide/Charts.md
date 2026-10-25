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
