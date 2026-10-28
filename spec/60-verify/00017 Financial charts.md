# 00017 Financial charts

> Status: verify — implemented 2026-10-28

## Summary

Six chart types for a monthly performance review: development, growth by row, actual vs reference
by row, growth trend, two-year monthly and a summary table. They are drawn from typed data as a
`Drawing`, and that drawing becomes an SVG for web pages, shapes in decks and vectors in PDFs. The
caller keeps the data and the month logic; the library does the drawing.

## Context

- A consumer application draws these charts today as hand-written SVG strings: each chart is 560 px
  wide, and hover tooltips are done in CSS alone. Its drawing code and stylesheet came with the
  request and are kept outside the repo. They are the reference for the port, but are not copied
  into it.
- These charts cannot reach a deck today. Pictures must be PNG, JPEG or GIF, and the `chart` block
  (00016) has no combined charts, data labels, per-point styling, reference lines, horizontal bars
  or second panel.
- **The caller keeps:**
  - reading its sources: actuals, targets, forecast and estimates;
  - choosing which charts, rows and filters;
  - any data files it writes beside a chart;
  - the month logic, i.e. which months are actuals.

  The library gets numbers, labels and flags.
- **Already in the library:**
  - `Drawing` (paths, fill, stroke, dashes, text with anchors and bold spans), which decks write as
    editable shapes and PDFs as vectors;
  - the brand's diagram colours and category colours (`success`, `danger`, `neutral`, 00015);
  - the round-step axis in `ChartDrawing.Scale`.
- **Missing from the library:** a `Drawing` → SVG writer, a colour per text span, and hover text.
- **Not native PowerPoint charts.** Two panels on one axis, a figures table beside the bars, and
  clipped bars with arrowheads have no native form. In decks they become shapes, like Mermaid
  diagrams.

## The chart types

The API uses neutral words throughout: *previous* and *current* (year), *reference* (the target),
*projection* (the forecast) and *estimate*. Every label a chart prints is an option, so a caller can
print its own words or abbreviations. The defaults are `Previous`, `Current`, `Reference`,
`Projection` and `Estimate`.

### 1. Development

Twelve month slots, with two panels on one x-axis. Each panel has its own value axis, because a
cumulative figure is an order of magnitude larger than a monthly one. The slot geometry is shared,
so a month reads straight down from a bar to the line point below it.

- **Upper panel, per month:**
  - the previous-year bar, with the reference increment stacked on it in the reference fill
    (`previous × (1 + reference rate)`);
  - the current-year bar beside it;
  - the projection as a pale dashed bar. It stands alone after the last actual month. From a given
    month onwards it shares the slot with the actual, and the shorter of the two bars goes in front.
- **Lower panel, one of two forms:**
  - *Cumulative:*
    - The actuals are a shaded area that starts at zero at the axis origin.
    - Previous year and reference are solid lines; the projection is a dashed line over the whole
      year.
    - Estimate diamonds sit at their months, optionally labelled on a patch of background.
    - End-of-line labels are pushed at least 11 px apart and kept inside the panel.
  - *Growth:* the cumulative growth rate, solid through the last actual and dashed after, with a
    flat reference line and end labels.
- Also:
  - a dashed rule where the actuals end, crossing both panels;
  - one row of month labels under the lower panel;
  - a title, a subtitle stating two figures (change at the current month and at year end), and a
    legend.
- Options: the lower panel's form, the width of the cumulative lines, and whether estimates are
  labelled.

### 2. Growth by row

Horizontal bars that diverge from zero, one row per entry.

- Each row has an actual bar (filled) and a reference bar (outline only), both drawn from zero.
- Two dashed vertical lines show the overall reference and the overall actual, labelled above the
  plot. A label slides back inside the plot when its line is near an edge.
- A figures table sits right of the plot, with no rule between them. Its columns are the change,
  coloured by verdict, and the reference in the reference colour.
- One row may be emphasised: a stronger fill and a bold label.
- Row labels wrap by words at about 17 characters.

### 3. Actual vs reference by row

- **Bands:** one per row, with a bar per measure. Measures of one series share a hue: the month is
  solid, the year to date pale. Every other band is tinted.
- **Reference:** drawn as an outline over the year-to-date bars only.
- **Axis:** set by the bars that carry a reference, and by those references. A bar without a
  reference may widen it by at most half that span. A longer bar is clipped at the plot edge with
  an arrowhead, and its label turns inward and states the true figure.
- **Row labels:**
  - Aggregate rows are bold and flush left; the other rows are right-aligned, which reads as an
    indent.
  - Each row label has its references under it, each in its series colour, which needs a colour per
    text span.
- **Value labels:** at the outer end of whichever bar is longer, the actual or the reference.
- **Display:** as growth (%) or as value (money).

### 4. Growth trend

- One line over twelve months: solid through the last actual, then a dashed projection that
  continues from the last actual point.
- A flat, solid reference line.
- End labels, pushed apart like the development chart's.

### 5. Two-year monthly

- **Lines:** 24 month slots and one line per category.
  - A month without a value breaks the line, and a single isolated value is drawn as a dot.
  - A dashed rule marks the year boundary; the lines cross it.
- **Labels:** month labels under the plot, and under them the label of each year.
- **Legend:** flows in rows and pushes the plot down.
- **Colours by line height:** categories are ranked by the average of their drawn values. The
  highest takes palette colour 1. At most 6 categories, or the chart throws.

### 6. Summary table

- **Header:** a header band, in which the total column's header is darker.
- **Blocks:** one block of rows per metric.
  - *Amount block:* value, vs previous, vs reference.
  - *Rate block:* value, current % of X, previous % of X, vs previous (pp), vs reference (pp).
- **Layout:**
  - Figures are right-aligned.
  - The total column has a pale fill, bold figures and a gap after it.
  - A dashed divider separates the blocks.
- **Colour:** only comparison rows are coloured, by verdict. A value that does not apply shows `–`.
- **Rules applied by the library**, with the thresholds as options:
  - The **rest column** (marked by the caller) is drawn only when something in it does not round to
    zero.
  - Where the rest column is under 0.5 % of the total, it shows its value but no comparisons.
  - A cell whose figures round to zero at the table's scale shows no comparisons.

## Rules every chart follows

- **A colour means a series and nothing else.** A bar behind its reference has the same colour as
  one ahead of it.
- **Verdict colours go on figures, never on bars:** ahead, level or behind. The tolerance is 0.5
  percentage points by default and configurable.
- **Line styles:**
  - A dash means projection, and only projection.
  - References and the previous year are solid.
  - References and reference lines are neutral grey.
  - Pale means year to date or projection; strong means actual.
- **Axis:**
  - It always includes zero, and only what is drawn shapes it.
  - 6 % of the range is added as padding before rounding.
  - Steps are 1, 2, 2.5 or 5 × 10ⁿ, about 5 ticks.
  - The zero gridline is drawn in the axis colour.
- **Numbers:**
  - Written in a culture the caller gives (for example `pl-PL`: `10,2%`, `109,2 M`), with a scale
    and a unit.
  - Differences are signed, with one decimal (`+1,2%`).
  - Distances between shares are in points (`-1,2pp`).
- **Hover text:**
  - On every bar, point, line and gridline, in SVG only, for example `Jul Current: 18,4 M`.
  - Line points get an invisible 10 px hit circle and a dot shown on hover.
- **Text width:** estimated as 0.58 × size × characters, for laying out labels, legends and
  tooltips. Decks and PDFs still set the text in the real font, and `MaxWidth` shrinks a line that
  does not fit.

## Colours

New chart roles on the brand (`BrandColors.Chart`, every role optional), with defaults derived from
the brand. They are resolved for light and dark slides like the diagram colours (00010).

| Role | Default | Used for |
| --- | --- | --- |
| Series 1 fill / pale / strong / stroke | primary, mixed lighter and darker | current year, the actual; pale = year to date and projection; strong = development actuals |
| Series 2 fill / pale / stroke | the second diagram palette colour | previous year, the second measure |
| Series 3 fill / pale / stroke | the third diagram palette colour | spare |
| Categorical 1–6 | the diagram palette | two-year lines, ranked by height |
| Reference line / fill | neutral grey | references, overall lines, the stacked reference increment |
| Axis / gridline / band | diagram line colour, lighter | |
| Emphasis fill | the `success` category colour | the emphasised row |
| Verdict ahead / level / behind | the `success` / `neutral` / `danger` category colours | figures only |
| Surface, title, label, subtitle text | diagram surface and text colours | |
| Table header / total header / total fill | primary, darker primary, pale primary | summary table |
| Tooltip fill / text | near-black at 94 % / white | SVG hover |

On dark slides, the surface becomes transparent (the slide shows through), text turns white, and
the pale fills become translucent.

## Proposed approach

1. **Package:** the existing `wwosik.DocLibs.DocumentGeneration.Charts`, which gets more chart
   kinds. It stays AOT-compatible, so JSON uses a source-generated `JsonSerializerContext`.
2. **API:**
   - One input record per chart type: `DevelopmentChart`, `GrowthByRowChart`,
     `ActualVsReferenceChart`, `GrowthTrendChart`, `TwoYearChart` and `SummaryTable`, with their
     month, row, measure, block and cell records.
   - `FinancialChartOptions`: culture, scale and unit, labels, verdict tolerance, rest-column share,
     width (default 560), and the brand.
   - `FinancialCharts.Draw(chart, options)` → `Drawing`.
   - The records keep everything the drawing needs and nothing the caller's sources need.
3. **Drawing model** (shared package; init properties only, so nothing breaks):
   - `DrawingSpan.Color`: a colour per span, falling back to the text's colour.
   - `DrawingItem.Hover`: the hover text and the point a tooltip is anchored to. Decks and PDFs
     ignore it.
   - `DrawingHotspot`: an invisible hit circle, with an optional dot shown on hover, for line
     points. Decks and PDFs skip it.
4. **SVG writer** (shared package, so Mermaid drawings can be exported too):
   - `DrawingSvg.Write(drawing)` → a standalone SVG with a `viewBox`.
   - Each hoverable item becomes `<g class="chart-hoverable">`, holding the shape and an
     already-positioned `<g class="chart-tooltip">` (clamped to the canvas, 17 px high, 6 px
     padding).
   - An inline `<style>` toggles only opacity on `:hover`. The class names are stable, so a page can
     override them. No script.
5. **The six charts:** port the reference drawing code into `Drawing` builders. Keep its reasoning
   as comments: it records why each rule exists.
6. **Markdown:**
   - A ```` ```financial-chart ```` code block whose body is the input record as JSON, with a
     `type` and the options: `{"type": "development", "unit": "M", "scale": 1000000, ...}`.
   - `ChartRenderer` takes it and returns a `DrawingGraphic`.
   - JSON errors throw with the block's line, like the `chart` block.
7. **Decks and PDFs:** a `Drawing` already becomes shapes and vectors. On dark slides the chart uses
   the brand's dark chart colours (the renderer gets the brand for the slide's tone).
8. **Tests:**
   - An SVG snapshot per chart type.
   - Edge cases:
     - no projection;
     - a December month;
     - all values negative;
     - a clipped bar;
     - an empty rest column;
     - 6 categories, and 7 (throws);
     - months without values;
     - a label near an edge.
   - Each type in a deck (validator, on light and dark templates) and a PDF.
   - A deck opened in PowerPoint.
9. **Docs:** the Charts package page and an authoring-guide section for the JSON block.

## Acceptance criteria

- [ ] The six chart types are drawn from typed input as a `Drawing`, as SVG, in decks and in PDFs.
- [ ] A ```` ```financial-chart ```` block with JSON draws them from Markdown.
- [ ] SVG output has hover text on every drawn element, with no script and an inline style.
- [ ] Colours come from brand roles and work on light and dark templates. The verdict tolerance is
      configurable.
- [ ] Numbers follow the culture, scale and unit the caller gives.
- [ ] Tests, docs and the authoring guide are updated.

## Breaking changes

- None. The span colour, hover text and hotspot are additions, and the chart roles are optional.

## Open questions

1. The same package as 00016 (`…DocumentGeneration.Charts`) or a new one (`…FinancialCharts`)?

> Answered by the human (2026-10-26): the same package, `wwosik.DocLibs.DocumentGeneration.Charts`.

2. Domain words in the API (previous year, current year, target, forecast, best estimate) or neutral ones?

> Answered by the human (2026-10-26): neutral words (previous, current, reference, projection,
> estimate); the printed labels stay configurable.

3. SVG hover styling: an inline `<style>` in each SVG, or classes plus a stylesheet the caller
   includes?

> Answered by the human (2026-10-26): inline `<style>`, with stable class names a page can override.

4. Text width: estimate it (0.58 × size × characters) or measure with the brand font?

> Answered by the human (2026-10-26): estimate it.

5. How does a deck or PDF ask for one: a JSON code block, the C# API only, or both?

> Answered by the human (2026-10-26): both, a ```` ```financial-chart ```` JSON block and the C# API.

6. Summary table: does the library apply the rest-column and remainder rules, or does the caller
   pass finished cells?

> Answered by the human (2026-10-26): the library, as options.

7. Compare the output with the consumer's current charts (reference SVGs and data)?

> Answered by the human (2026-10-26): no comparison; tests on made-up data and a look in PowerPoint.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- A PNG of each chart, for places that take no SVG, comes from the SVG-to-PNG converter in 00018,
  via `DrawingSvg.Write`.

## Implementation notes

### Decisions

- **API** (`wwosik.DocLibs.DocumentGeneration.Charts`): the records `DevelopmentChart`,
  `GrowthByRowChart`, `ActualVsReferenceChart`, `GrowthTrendChart`, `TwoYearChart` and `SummaryTable`
  under `FinancialChart` (title, period, subtitle), with their month, row, measure, block and cell
  records. `FinancialChartOptions` holds brand, culture, scale, unit, labels, tolerance,
  rest share and width. `FinancialCharts.Draw` returns a `Drawing`, `ToSvg` returns SVG, and
  `CategoryColors` gives the two-year chart's colour per category.
- **Drawing:** one file per chart, ported from the reference code with its reasoning kept as
  comments. `FinancialCanvas` holds what all six share: the axis, number formats, verdict colours,
  gridlines, end labels pushed apart, label wrapping, hover points.
- **Shared package:** `DrawingSpan.Color`, `DrawingItem.Hover` (`DrawingHover`), `DrawingHotspot`;
  `DrawingSvg.Write` with its options. Decks and PDFs use span colours and skip hotspots.
- **Brand:** `BrandColors.Chart` (`BrandChartColors`, brand pack `colors.chart`), resolved to
  `ResolvedChartColors`. The dark variant comes from `ResolvedBrand.DarkSlideBrand`, which marks the
  brands it makes.
- **JSON:** a `financial-chart` block, read with a source-generated context
  (`type` discriminator, camel case, unknown properties are errors, `options` read separately).
  `ChartRenderer` draws it in the request's brand, so dark slides get a dark chart.

### Deviations from the plan

- **Series colours** are given as one colour per series. The fill, pale fill and strong fill are
  mixed from it, rather than being four roles per series.
- **Captions and default subtitles** ("Per month", "Cumulative", "Actuals end after …") are English
  and not options; `Subtitle` replaces a chart's subtitle.
- **The estimate label switch** is `HideEstimateLabels` (default off) rather than `LabelEstimates`
  (default on): System.Text.Json source generation drops initializers of `init` properties, so a
  default of `true` could not survive JSON.
- **The development and growth-trend charts widen their right margin** to their longest end label,
  as labels like `Projection 218,2 M` are longer than two-letter codes.
- **The axis keeps the reference code's rule** of padding by 6 % of the range or 0.06, whichever is
  larger, so a rate panel keeps six points of headroom.

### Breaking changes

- `DrawingItem` has a third kind, `DrawingHotspot`. In `CHANGELOG.md`.

### Verified

- **Tests:** Charts 67 (an SVG snapshot per chart type; the shared rules; the edge cases of the
  plan; JSON round trips and errors; deck and PDF), shared 84, PPTX 172 (six charts on a light and
  on a dark template), PDF 114. Release build 0 warnings.
- **Native AOT:** a small app drawing a chart and reading a block publishes without warnings and
  runs.
- **Rendered:** every chart rendered to PNG and looked at.
- **Not verified:** the decks were not opened in PowerPoint, because the PowerPoint export hung in
  this session and its process could not be stopped from here (`.tmp/review/financial-charts-*.pptx`).
- **No comparison** with the consumer's current charts, as decided (question 7).

