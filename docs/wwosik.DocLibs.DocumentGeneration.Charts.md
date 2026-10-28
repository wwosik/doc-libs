# wwosik.DocLibs.DocumentGeneration.Charts

Charts in the brand's colours and fonts, for both generators and for web pages:

- **Bar, line and area charts** from CSV in `chart` blocks: native, editable PowerPoint charts in
  decks and vector charts in PDFs.
- **Financial charts** — development, growth by row, actual vs reference, growth trend, two-year
  monthly and a summary table — from typed data in C# or JSON in `financial-chart` blocks: shapes
  in decks, vectors in PDFs, and SVG with hover tooltips for web pages (see
  [Financial charts](#financial-charts)).

Depends only on [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md);
AOT-compatible.

```csharp
using wwosik.DocLibs.DocumentGeneration.Charts;

var options = new PptxOptions { CodeBlockRenderers = [new ChartRenderer()] };
PptxGenerator.Generate(document, output, options);
PdfGenerator.Generate(document, pdf, new PdfOptions { CodeBlockRenderers = [new ChartRenderer()] });
```

`ChartRenderer` is an `ICodeBlockRenderer`: it takes code blocks whose language is `chart` (any
case) and leaves every other block to the next renderer, so it sits next to `MermaidRenderer`.

## The block

Settings first, one `key: value` per line (a ` # comment` after a value is ignored), then a line
`---`, then the data as CSV. Without settings the block is the CSV alone.

````markdown
```chart
type: bar
title: Orders by month
unit: k
---
month, plan, actual
Jan, 120, 132
Feb, 118, 121
Mar, 125,
```
````

| Setting | Values | Default |
| ------- | ------ | ------- |
| `type` | `bar` (vertical columns), `line` (with markers), `area` | `bar` |
| `stacked` | `true`, `false` — bars or areas on top of each other; lines never stack | `false` |
| `title` | text above the chart | none |
| `unit` | text at the value axis (`k`, `%`, `EUR`) | none |

- **Data:** the header row names the category column, then one series per column; each further row
  is a category. Values are numbers with a decimal point and no thousands separator; an empty cell
  is a gap. A cell in double quotes may hold commas.
- **Colours:** the series take the brand's diagram palette (`Colors.Diagram.Palette`, by default the
  theme accents), in order, repeating. In decks the brand's slide colours apply.
- **Errors:** an unknown setting or type, a value that is not a number, a row with more cells than
  the header, or missing data throws `ArgumentException`:
  `The chart code block at line 12 cannot be drawn: <reason>`. The generators pass it on as a
  `DocumentConversionException` with the `ArgumentException` as `InnerException`.

## In decks

A `ChartGraphic` is written as a native chart part with its data in an embedded workbook, so
PowerPoint's *Edit Data* opens the numbers. It fills the width it has, at most 5:8 tall; on content
slides it gets a slide of its own like an image, on layout slides it shares the height with the other
blocks, and `::: columns` puts two side by side. Text is set in the brand's table font, else the
theme font; on dark slides it is white and the gridlines translucent white. Bars and areas start at
zero. See the [PPTX documentation](wwosik.DocLibs.DocumentGeneration.Pptx.md#diagrams-and-charts).

## In PDFs

The same chart drawn as vectors (640 × 380 px, scaled to the text width): the title, the value axis
with gridlines at about six round steps from zero (or below, for negative values), the categories,
the legend at the bottom; text in the brand's table font and diagram text colour.

## Financial charts

The charts of a monthly performance review. The caller keeps its data and decides which months are
actuals; the library draws.

```csharp
var chart = new GrowthTrendChart
{
    Title = "Orders growth",
    Period = "July 2026",
    Months = [new("Jan", 0.056), new("Feb", 0.054), /* … */ new("Dec", 0.057)],
    LastActual = 6,          // July: the months after it are a projection
    Reference = 0.05,
};
var options = new FinancialChartOptions
{
    Brand = brand,
    Culture = CultureInfo.GetCultureInfo("pl-PL"),
    Scale = 1_000_000,
    Unit = "M",
    Labels = new() { Previous = "Last year", Current = "This year", Reference = "Target", Projection = "Forecast" },
};

Drawing drawing = FinancialCharts.Draw(chart, options);   // for a generator, or a renderer of your own
string svg = FinancialCharts.ToSvg(chart, options);       // for a web page: hover tooltips, no script
```

| Chart | What it draws |
| ----- | ------------- |
| `DevelopmentChart` | Months in two panels over the same slots. Above, per month: the previous year with the reference increment stacked on it, the current year beside it, the projection as a pale dashed bar (alone after the last actual, overlapping the actual from `ProjectionComparedFrom` on, the shorter in front). Below, `Cumulative`: the actuals as an area from the origin, previous year and reference as lines, the projection dashed, estimates as diamonds; or `Growth`: the cumulative growth rate against the flat reference rate. A dashed rule where the actuals end; end labels pushed apart. |
| `GrowthByRowChart` | Horizontal bars diverging from zero: the change filled, the reference as an outline, dashed lines for the `Overall` change and reference (their labels slide back inside the plot near an edge), a table of the figures beside the plot, one row `Emphasised`. |
| `ActualVsReferenceChart` | A band per row and a bar per measure: the month solid, the year to date pale, a hue per series; the reference as an outline over the year-to-date bars. A bar without a reference widens the axis by at most half; beyond, it is clipped with an arrowhead and its label states the true figure. `Aggregate` rows bold and flush left. `Display`: growth or value. |
| `GrowthTrendChart` | One growth rate over the months, solid through `LastActual`, dashed after, against the flat reference. |
| `TwoYearChart` | One line per category over two years of month slots, broken where a month has no value, a lone value as a dot; a dashed rule at the year boundary. At most six categories, coloured by how high their lines run (`FinancialCharts.CategoryColors` says which got which). |
| `SummaryTable` | Blocks of rows per metric under a header band: amount blocks (value, vs previous, vs reference) and rate blocks (`RateOf`: value, both shares, their distances in points). The `Total` column emphasised; a `Rest` column only when it holds something, its comparisons only when it is at least `RestShare` of the total. |

**The rules every chart follows:**

- **A colour means a series and nothing else:** a bar behind its reference has the colour of one
  ahead of it.
- **Verdict colours go on figures, never on bars:** ahead, level or behind, within `Tolerance`
  (default 0.005, half a percentage point).
- **A dash means projection**, and only projection; references and the previous year are solid.
- **Value axes** include zero, are shaped only by what is drawn, are padded by 6 % of the range (at
  least 0.06) before rounding, and step in 1, 2, 2.5 or 5 × 10ⁿ, about five ticks; the zero line is
  drawn in the axis colour.
- **Numbers** are written in `Culture`, values divided by `Scale` with `Unit`; differences signed
  with one decimal, distances between shares in points (`-1,2pp`).
- **Words:** `Labels` sets what the charts print for previous, current, reference, projection,
  estimate, change, overall and value; captions such as *Per month* and default subtitles are
  English, and `Subtitle` replaces a chart's subtitle.
- **Hover text** on every bar, point, line and gridline, shown by `ToSvg` only.

Inputs that do not fit together — rows with different measures, values for more months than the
chart has, more than six categories — throw `ArgumentException`.

### Colours

The charts take `Brand.Colors.Chart` (on slides `SlideColors.Chart`), each role optional:

| Role | Default | Used for |
| ---- | ------- | -------- |
| `Series` (up to 3) | diagram palette 1–3 | current year, previous year, further measures; a fill, a pale fill (year to date, projection) and a strong fill (actuals) are mixed from each, the colour itself is the outline |
| `Categories` | diagram palette | two-year lines, by height |
| `Reference`, `ReferenceFill` | `6B7280`, `E5E7EB` | reference lines and outlines, the stacked reference increment |
| `Axis`, `Gridline`, `Band` | `9CA3AF`, `E5E7EB`, `F9FAFB` | the zero line, gridlines and rules, every other row band |
| `Emphasis` | `Labels.Success` | the emphasised row |
| `Ahead`, `Level`, `Behind` | `Labels.Success`, `Footer`, `Labels.Danger` | verdicts on figures |
| `Surface` | white | the background |
| `TableHeader`, `TotalHeader`, `TotalFill` | `TableHeader`, a darker shade of it, `TableBand` | the summary table |
| `TooltipFill`, `TooltipText` | `111827`, white | SVG tooltips |

On dark slides there is no surface (the slide shows through), text is white, fills are dark shades
of the series colours and outlines light ones.

### In Markdown

A `financial-chart` block holds the chart as JSON: its `type` (`development`, `growth-by-row`,
`actual-vs-reference`, `growth-trend`, `two-year`, `summary-table`), its properties in camel case
as the records name them, and optional `options` (`culture`, `scale`, `unit`, `labels`,
`tolerance`, `restShare`, `width`):

````markdown
```financial-chart
{
  "type": "growth-trend",
  "title": "Orders growth",
  "lastActual": 2,
  "reference": 0.05,
  "months": [{ "label": "Jan", "change": 0.056 }, { "label": "Feb", "change": 0.054 }, { "label": "Mar", "change": 0.04 }],
  "options": { "culture": "pl-PL", "labels": { "current": "This year", "previous": "Last year" } }
}
```
````

`ChartRenderer` draws it in the brand of the slide or page, so a dark slide gets a dark chart.
Unknown properties, missing types and invalid JSON throw `ArgumentException`
`The financial-chart code block at line N cannot be drawn: <reason>`. Comments and trailing
commas are allowed.

### For web pages

`ToSvg` writes the chart through `DrawingSvg` (see
[the shared package](wwosik.DocLibs.DocumentGeneration.md#svg)) in the brand's table font, else its
text font: every bar, point and line has a tooltip shown on hover by an inline style, without script.
For a PNG, pass the SVG to `SvgConverter` from
[wwosik.DocLibs.DocumentConversions.Svg](wwosik.DocLibs.DocumentConversions.Svg.md).

## For writers

`ChartRenderer.AuthoringGuide` is its `## Charts` section of the generators' authoring guides: the
`chart` block's syntax with an example, the settings, how to put two charts side by side, and the
`financial-chart` block.

## Known limits

- Bar (vertical), line and area charts only: no pie, scatter, combined or secondary-axis charts.
- Financial charts are drawings, not native charts: PowerPoint cannot edit their data. Their layout
  estimates text widths (0.58 × size per character), so a font much wider than that can crowd labels.
- No data labels, no axis titles beyond the unit, no custom number formats.
