# wwosik.DocLibs.DocumentGeneration.Charts

Bar, line and area charts from data in Markdown, for both generators: native, editable PowerPoint
charts in decks and vector charts in PDFs, in the brand's colours and fonts. Depends only on
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md); AOT-compatible.

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

## For writers

`ChartRenderer.AuthoringGuide` is its `## Charts` section of the generators' authoring guides: the
block's syntax with an example, the settings, and how to put two charts side by side.

## Known limits

- Bar (vertical), line and area charts only: no pie, scatter, combined or secondary-axis charts.
- No data labels, no axis titles beyond the unit, no custom number formats.
