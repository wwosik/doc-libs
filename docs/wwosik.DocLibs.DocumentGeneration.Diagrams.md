# wwosik.DocLibs.DocumentGeneration.Diagrams

Draws ` ```mermaid ` code blocks as diagrams in a brand's colours and font: **vector graphics** in
PDFs and **editable PowerPoint shapes** in decks. It plugs into both generators as a code-block
renderer. Cross-platform and AOT-compatible, with no browser, Node.js or native code. Depends on
[Mermaider](https://github.com/nullean/mermaider) (MIT) and
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md).

```csharp
using wwosik.DocLibs.DocumentGeneration.Diagrams;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

PdfGenerator.Generate(document, output, new PdfOptions { CodeBlockRenderers = [new MermaidRenderer()] });
PptxGenerator.Generate(document, output, new PptxOptions { CodeBlockRenderers = [new MermaidRenderer()] });
```

Without this package, both generators leave ` ```mermaid ` blocks as code.

## Diagram types

| Drawn | Stay code |
| ----- | --------- |
| `flowchart` / `graph` (subgraphs included), `sequenceDiagram`, `classDiagram`, `stateDiagram` / `stateDiagram-v2`, `erDiagram`, `pie`, `gantt`, `mindmap` | every other Mermaid type: `journey`, `timeline`, `quadrantChart`, `gitGraph`, C4, `xychart`, `sankey`, … |

The drawn types are the ones covered by golden tests. More types will be added as they get tests.

## Colours and font

- **Colours** come from the brand's `Colors.Diagram`
  (see [Brands](wwosik.DocLibs.DocumentGeneration.md#what-a-brand-holds)):

  | Colour | Used for | Default |
  | ------ | -------- | ------- |
  | `Text` | labels | the body text colour (theme dark 1) |
  | `Line` | edges, arrows | `Colors.Footer` |
  | `Accent` | decision nodes, title markers | `Colors.Primary` |
  | `Surface` | node fill | `Colors.TableBand` |
  | `Border` | node outline | `Colors.Primary` |
  | `Palette` | pie slices, gantt sections, subgraphs, mindmap branches, in order | theme accents 1–6 |

  Mermaider derives the remaining shades by mixing these colours. The background is always
  white, with no gradients and no shadows.
- **Font:** the brand's [table font](wwosik.DocLibs.DocumentGeneration.md#table-fonts), else its
  text font. In PDFs this means the table faces (bold for `font-weight` 600 and up, mono for
  class members and ER attributes); in decks, the table family, else the theme font.
- The brand is the one the document is actually generated in, after front matter, so
  `template: acme` also switches the diagrams to that brand.

## How a diagram is drawn

1. The brand becomes Mermaider's `RenderOptions` (colours, palette, font name, `Gradient = false`,
   `Elevation = 0`, `AllowedDiagrams` limited to the drawn types). Mermaider lays the diagram out and
   writes SVG.
2. The SVG is read with DTDs prohibited and converted into a `Drawing`, the vector model of
   [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#drawings-and-code-block-renderers).
   - CSS custom properties, `var()` with fallbacks and `color-mix(in srgb, …)` are resolved to
     plain colours. Font sizes in `rem` / `em` / `px` become pixels.
   - Shapes are kept as rectangles, rounded rectangles and ellipses where they are. Quadratic
     curves and arcs become cubic curves. Arrowheads (`marker-start` / `marker-end`) are placed
     and turned along their edge.
   - Text is split into lines (`tspan` with `x`/`dy`) of bold and regular spans.
   - Opacity is mixed with the white background, and a gradient stands in as its first colour.
3. Each line of text gets the room of the node or edge label it sits in. The generator measures it
   in the real font and shrinks a line that does not fit, down to 70 % of its size. Below that
   the line overflows; it is never cut. Mermaider sizes nodes with its own text metrics, which
   may differ from the brand font.

Anything the converter does not know (an element, a CSS function, a transform) is an error,
never silently dropped.

## Errors

`MermaidRenderer.OnError` decides what happens to a block that cannot be drawn: Mermaider cannot
parse it, or its SVG has something the converter does not know.

| `OnError` | Behaviour |
| --------- | --------- |
| `Throw` (default) | `ArgumentException`: `The mermaid code block at line 12 cannot be drawn: <reason>`. The line counts from the top of the Markdown, front matter included. |
| `KeepCode` | The block stays code. |

A block of a type that is not drawn stays code in both modes.

## Known limits

- Layout and node sizes are Mermaider's. A label in a font wider than Mermaider's metrics is
  shrunk or overflows, and one in a narrower font leaves more room.
- Wide diagrams, such as a gantt chart over several months, are scaled down to the page or slide
  width, text included.
- `letter-spacing` (gantt section titles) is ignored.
- In decks, text boxes are sized by an estimate of half the font size per character, because
  decks have no font metrics.
- Mermaid's `click` links, icons (`image`) and styling by CSS class are not drawn.
