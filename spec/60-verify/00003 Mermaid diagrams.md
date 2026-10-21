# 00003 Mermaid diagrams

> Status: verify — implemented 2026-10-22

## Summary

Render ` ```mermaid ` code blocks as diagrams in the brand's colours and fonts. In PDF they are
vector graphics; in PPTX they are native, editable PowerPoint shapes. The work goes into a new,
optional package `wwosik.DocLibs.DocumentGeneration.Diagrams`, built on the MIT library
**Mermaider**. No browser, Node.js or native code is involved.

## Context

- Both generators already have a hook: `PdfOptions.RenderCodeBlock` / `PptxOptions.RenderCodeBlock`,
  `Func<string language, string code, DocumentResource?>`. A returned PNG or JPEG is placed like
  a Markdown image, and `null` keeps the code block (`MarkdownParser`, `DeckParser`). The library
  renders nothing itself; see [Diagrams in the PDF doc](../../docs/wwosik.DocLibs.DocumentGeneration.Pdf.md#diagrams).
- **.NET options, compared 2026-10-21:**

  | Library | Licence | Notes |
  | ------- | ------- | ----- |
  | **Mermaider** 0.15.1 (nullean/mermaider) | MIT | .NET 10, Native AOT compatible, 24 diagram types, ~70k downloads; deps: `Microsoft.Extensions.ObjectPool`, `Sugiyama`. SVG and ASCII output. |
  | Naiad (SimonCropp) | MIT source, but binaries under the OSMF EULA (fee for commercial use) | SVG; PNG via SkiaSharp / ImageSharp. |
  | DiagramForge | — | A deliberate Mermaid subset. |

- **Spike, in `.tmp/spike-mermaid/`:** Mermaider's SVG uses a small, fixed set of elements. Across
  flowchart, sequence, class, state, ER, pie, gantt, mindmap and subgraphs, it contains only:
  - `svg`, `g`, `rect` (with `rx`/`ry`), `line`, `path` (`M L Q C A Z`, to verify), `polygon`,
    `circle`, `ellipse`, `text`;
  - `text` carries `x`, `y`, `dy`, `text-anchor`, `font-size`, `font-weight` and `letter-spacing`;
  - `linearGradient` / `radialGradient`, `marker` (arrowheads), `stroke-dasharray`;
  - **no** `transform`, `tspan`, `foreignObject` or `image` (except icons).

  Colours and sizes are CSS: `var(--…)`, `color-mix(in srgb, …)` and `rem` font sizes inside a
  `<style>` block, which PowerPoint and SVG rasterizers do not understand.
  `RenderOptions { Gradient = false, Elevation = 0 }` removes the gradients and the
  `drop-shadow` filters. Node sizes do **not** depend on `RenderOptions.Font`: Mermaider measures
  text with its own metrics.
- **PDF:** the page model (`PageModel.cs`) has text, lines, filled rectangles, images and links.
  The PdfPig 0.1.16 builder has no Bézier API, but `PdfPageBuilder.CurrentStream.Operations` is a
  public `List<IGraphicsStateOperation>`. It accepts the path operators (`BeginNewSubpath`,
  `AppendStraightLineSegment`, `AppendDualControlPointBezierCurve`, `CloseSubpath`, fill / stroke
  operators, `SetLineDashPattern`, `SetLineJoin`).
- **PPTX:** `DeckWriter` places pictures and tables on a `Canvas`. DrawingML has group shapes
  (`p:grpSp`) and custom geometry (`a:custGeom`), which PowerPoint renders and lets users edit.
- Brand fonts and colours: [00001](../60-verify/00001%20Client%20brands%20for%20the%20generators.md).
  The table font is [00002](00002%20Table%20font.md); diagrams use it when present.

## Proposed approach

### 1. A drawing model in the shared package

`wwosik.DocLibs.DocumentGeneration` gets a small public vector model. Both generators draw it,
and any code-block renderer can produce it:

```csharp
public sealed record Drawing(double Width, double Height, IReadOnlyList<DrawingItem> Items); // CSS px, origin top left

public abstract record DrawingItem;

public sealed record DrawingShape(DrawingPath Path, string? Fill, string? Stroke, double StrokeWidth) : DrawingItem
{
    public IReadOnlyList<double>? Dash { get; init; }
    public DrawingShapeKind Kind { get; init; }            // Path, Rectangle, RoundedRectangle, Ellipse: lets PPTX use preset shapes
}

public sealed record DrawingPath(IReadOnlyList<PathSegment> Segments);   // MoveTo, LineTo, CubicTo, Close

public sealed record DrawingText(double X, double Baseline, string Text, double Size, string Color) : DrawingItem
{
    public bool Bold { get; init; }
    public TextAnchor Anchor { get; init; }                // Start, Middle, End
    public double? MaxWidth { get; init; }                 // the shape it sits in; shrink to fit, see step 5
}
```

- Arrowheads (markers), `rect`, `circle`, `ellipse`, `polygon` and `line` are converted to
  paths when the model is built. Quadratic curves and arcs become cubic curves. Colours are
  `RRGGBB`, as everywhere else.
- Equality compares by content, so a drawing can be tested against an expected value.

### 2. Code-block hook: drawings as well as images

The packages are unpublished, so the hook can change freely. It is replaced by:

```csharp
public interface ICodeBlockRenderer
{
    /// <summary>A drawing or an image for the block, or null to leave it as code.</summary>
    CodeBlockGraphic? Render(CodeBlockRequest request);
}

public sealed record CodeBlockRequest(string Language, string Code, Brand Brand);

public abstract record CodeBlockGraphic;                     // DrawingGraphic(Drawing) | ImageGraphic(DocumentResource)
```

`PdfOptions` / `PptxOptions` get `CodeBlockRenderers` (`IReadOnlyList<ICodeBlockRenderer>`,
default `[]`); the first non-null result wins. The request carries the **brand the document is
actually rendered in**, after front matter, so diagrams follow `template: acme`.

### 3. Brand: diagram colours

```csharp
public sealed record BrandColors
{
    // ... existing ...
    public BrandDiagramColors? Diagram { get; init; }
}

public sealed record BrandDiagramColors
{
    public string? Text { get; init; }          // default: the body text colour
    public string? Line { get; init; }          // default: Footer grey
    public string? Accent { get; init; }        // default: Primary
    public string? Surface { get; init; }       // node fill; default: TableBand
    public string? Border { get; init; }        // node outline; default: Primary
    public IReadOnlyList<string>? Palette { get; init; } // pie / xy / gantt series; default: theme accents 1–6
}
```

- `brand.json`: `colors.diagram.{text,line,accent,surface,border,palette}`, with the same
  validation and error messages as the other colours.
- The font is the table font (00002) if the brand has one, else the text font. Condensed cuts
  suit diagram labels. The background is always white (`Bg`), with no shadows and no gradients.

### 4. Package `wwosik.DocLibs.DocumentGeneration.Diagrams`

- References `Mermaider` (pinned in `Directory.Packages.props`) and the shared package only. It is
  `IsAotCompatible`.
- `public sealed class MermaidRenderer : ICodeBlockRenderer` handles the language `mermaid`
  (case-insensitive) and returns `null` for any other.
- Steps:
  1. Map the brand to `Mermaider.Models.RenderOptions`: `Bg`, `Fg`, `Line`, `Accent`,
     `Surface`, `Border`, `DataPalette`; `Gradient = false`, `Elevation = 0`,
     `Transparent = false`. Set `AllowedDiagrams` to the supported types (step 6), and `Limits`
     to sane sizes.
  2. `MermaidRenderer.RenderSvg(code, options)`.
  3. Read the SVG with `XmlReader` (no DTD, no external entities). Build the custom-property
     table from the root `style` attribute and the `svg { … }` rule, and resolve `var()` (with
     fallbacks) and `color-mix(in srgb, a p%, b)` to `RRGGBB`. Resolve `rem` / `px` font sizes.
     Only this grammar is supported; anything else is an error.
  4. Convert the elements to a `Drawing` (step 1). Elements the converter does not know, e.g. an
     icon `image`, make the block fail (step 7) rather than silently dropping content.
  5. Set `DrawingText.MaxWidth` from the shape the text belongs to (the `g.node` / `g.edge-label`
     group's `rect` / `polygon` / `ellipse`, less the padding).
- Mermaider's version is pinned. Its SVG grammar is an implementation detail, so the golden
  tests (step 8) exist to catch changes when it is bumped.

### 5. Drawing in the generators

- **PDF:** a new page item `DrawingItem(Drawing, X, Y, Scale)`. The renderer emits path
  operators into `CurrentStream.Operations`: `re`/`m`/`l`/`c`/`h`, `f`/`S`/`B`, dash and line
  width, with the y axis flipped. Text uses the brand's table (else text) faces through the
  existing text writer, so it is embedded, subset and glyph-checked like any other text.
  `font-weight ≥ 600` → bold.
  - Layout: placed like an image — full width at most, shrink only, kept on one page. A drawing
    taller than the page is scaled to fit the page height. 1 CSS px = 0.75 pt before scaling.
  - **Shrink to fit:** a label whose width in the real font exceeds `MaxWidth` is drawn at a
    smaller size (down to 70 %). If it still does not fit, it overflows; it is never
    truncated.
  - PDF/A-2b: only device RGB fills and strokes, no transparency, so it stays valid.
- **PPTX:** a group shape sized like a picture in the content area.
  - `Rectangle` / `RoundedRectangle` / `Ellipse` become preset shapes (`rect`, `roundRect` with
    the matching adjust value, `ellipse`); paths become `a:custGeom`; texts become text boxes
    with no insets, anchored by `Anchor`, in the brand's table (else theme) family.
  - Font sizes are scaled with the group, in 1/100 pt.
  - Shrink to fit uses the same rule as PDF, estimated from character widths like
    `EstimateLines`, because PPTX has no font metrics.
  - The result is editable in PowerPoint: move nodes, change text, recolour.

### 6. Supported diagram types

The first release supports the types the spike covered and the converter handles: flowchart,
sequence, class, state, ER, pie, gantt and mindmap. Other types (timeline, journey, quadrant, C4,
xy, sankey, …) are excluded through `AllowedDiagrams` until they get golden tests; for them, the
block stays code. The list is documented and widened in later releases.

### 7. Errors

A block that Mermaider cannot parse, or that uses an unsupported type or element, follows
`MermaidRenderer.OnError`:

- `Throw` (default): an `ArgumentException` naming the line of the Markdown code block and
  Mermaider's message.
- `KeepCode`: the block stays as code.

### 8. Tests, samples, docs

- **Diagrams tests:**
  - CSS resolution (`var` with fallbacks, nested `color-mix`, `rem`);
  - SVG → `Drawing` for each element, including markers and arcs;
  - one golden `Drawing` (serialized to JSON) per supported type, rendered in the sample
    brand;
  - brand colours flow into the drawing;
  - unsupported types and elements; the error modes.
- **PDF tests:** paths, dashes, scaling and page fitting with made-up widths; shrink to fit;
  veraPDF on a sample with diagrams.
- **PPTX tests:** group structure, preset shapes versus custom geometry, text fonts; the deck
  opens and renders in PowerPoint (visual check through the Windows render script).
- **Samples:** a `diagrams.md` sample exported to PDF and PPTX in the neutral and sample
  brands.
- **Docs:**
  - a new `docs/wwosik.DocLibs.DocumentGeneration.Diagrams.md`;
  - the Diagrams sections of the PDF and PPTX docs, rewritten for `CodeBlockRenderers`;
  - the brand colours table;
  - README: the packages table, and a note on Mermaider's MIT licence;
  - `ai/memory.md`.
- `ci.yml` / `release.yml` pick up the new project through the solution; check that they do.

## Acceptance criteria

- [x] ` ```mermaid ` blocks of the supported types render as vector graphics in PDF (PDF/A-2b
      valid) and as editable shapes in PPTX, in the brand's diagram colours and font.
- [x] `RenderCodeBlock` is replaced by `CodeBlockRenderers`, and an image-returning renderer
      still works.
- [x] `BrandColors.Diagram` and `colors.diagram` in brand packs, with defaults derived from the
      brand.
- [x] Without the Diagrams package, both generators behave as today (the code block stays code).
- [x] Errors follow `OnError`; unsupported types stay code.
- [x] The Diagrams package is AOT-compatible; Release build has 0 warnings; all tests pass.

## Out of scope

- Other diagram languages (PlantUML, Graphviz, D2). The `ICodeBlockRenderer` interface leaves
  room for them later.
- Converting diagrams back to Mermaid in `DocumentConversions`.
- Icons in diagrams (`image` elements), interactivity, links in diagrams.

## Open questions

1. Which .NET library?

> Mermaider.

2. How do diagrams get into documents?

> Our own drawing from Mermaider's SVG: vector graphics in PDF, native shapes in PPTX.

3. Where does Mermaid support live?

> A separate package, `wwosik.DocLibs.DocumentGeneration.Diagrams`.

4. Where do the diagrams' colours and font come from?

> From the brand, plus a separate `diagram` section of colours in the brand.

5. Replace `RenderCodeBlock` with `CodeBlockRenderers` (step 2), or keep the old callback alongside?

> Replace it.

6. Default error behaviour: throw, or keep the block as code?

> Throw; `OnError = KeepCode` is the opt-out.

7. Start with the eight types of step 6, or try all 24 Mermaider types and drop only those the converter cannot draw?

> The eight tested types; widen later.

## Notes

## Implementation notes

### Deviations from the plan

- **`DrawingText` holds spans, not a single string.** Mermaider writes `tspan` elements for
  multi-line labels and for inline bold (`**x**` and `<b>`). The plan's `Text` and `Bold` are now
  `Spans` of `DrawingSpan(Text, Bold)`. A convenience constructor takes one string, and `Text`
  joins the spans. Each `tspan` line becomes its own `DrawingText`.
- **`DrawingShape` also has `CornerRadius`**, needed for PowerPoint's `roundRect` adjust value,
  and `RoundJoins` (round joins and caps). **`DrawingText` also has `Mono`**, because class
  members and ER attributes are mono in Mermaider.
- **Transforms are supported, not refused.** Sequence diagrams use `<g transform="translate(…)">`
  for actor icons, which the spike had missed. `matrix`, `translate`, `scale` and `rotate` are
  understood. Rotated text throws, and rotated shapes lose their `Kind` (they become paths).
- **Opacity and gradients are tolerated.** `opacity`, `fill-opacity` and `stroke-opacity` are
  mixed with the white background, and `url(#gradient)` takes the first stop colour. Mermaider
  does not write either with our options, but this is safer than throwing on a harmless change.
- **Unsupported types are detected through Mermaider.** It gets `AllowedDiagrams` set to the
  eight types. Its `MermaidParseException` "… is not in the allowed set" means the block stays
  code; any other parse error follows `OnError`. Matching the message is fragile, but it is the
  only signal, and `OtherLanguagesAndTypes_StayCode` would catch a change. Text that is not
  Mermaid at all ("Invalid mermaid header") follows `OnError`.
- **Golden files are a line format, not JSON** (`mermaid-<type>.expected.txt`, written by
  `DrawingFormat` in the tests). It has one item per line with numbers rounded to 0.01, so a diff
  shows what moved.
- **`CodeBlockRequest.Line`** is an init property (from 1, front matter included, 0 if unknown).
  `FrontMatter.BodyLine` provides the offset.
- **`Limits` keeps Mermaider's defaults**: 5 s, 5,000 elements, 512 KB of input. They are
  already sane, and the plan's "sane sizes" needed nothing more.
- **Defaults of `BrandDiagramColors`** are as planned. `Text` is the theme's dark 1 (black in
  derived themes), which is also what the PDF uses for body text.
- `CodeBlockGraphic`, `DrawingItem` and `PathSegment` are closed hierarchies (private protected
  constructors), so generators can switch over them exhaustively.

### How the room for a label is computed

`MaxWidth` is set only for text in `g.node` and `g.edge-label` groups (mindmap nodes are
`node mindmap-node`). It comes from the group's first shape:

| Shape | Room for text |
| ----- | ------------- |
| `rect` | width − 16 px |
| `circle` / `ellipse` | diameter − 16 px |
| diamond (`polygon` with 4 points) | 70 % of its width |
| other polygons (hexagon, flag) | width − 24 px |
| `path` | width − 16 px |

Class boxes, ER entities and sequence actors get no room and are never shrunk. These rules are
heuristics, tuned on the sample; a first try of half the diamond's width shrank labels that fit.

### Sample brand

- `brand.json` of the sample now has `colors.diagram.palette` (teal, amber, light teal, violet,
  rose, grey). The derived default put two teals next to each other: the primary and the second
  neutral accent. This also exercises the brand-pack key.

### Verified

- Release build with 0 warnings, including the AOT analyzers of the new `IsAotCompatible`
  package. All tests pass: 67 in Diagrams, plus new ones in the shared, PDF and PPTX projects.
- `./run.sh pack` produces `wwosik.DocLibs.DocumentGeneration.Diagrams`. CI and release build the
  solution, so they pick it up unchanged.
- veraPDF 1.30.3, PDF/A-2b: `diagrams-sample.pdf`, `diagrams-neutral.pdf` and `report-sample.pdf`
  pass.
- Looked at as PNGs:
  - every page of `diagrams-sample.pdf`;
  - `diagrams-sample.pptx`, exported by PowerPoint, which opened it without repair (flowchart and
    class slides). The Open XML validator reports no errors on either deck.

### Worth a look during verification

- **Errors are `ArgumentException`, as planned**, while every other document problem in the
  generators is `DocumentConversionException`. Wrapping renderer exceptions in the generators
  would make that consistent; it is left as planned.
- **Wide diagrams get small.** The sample Gantt chart is 1,200 px wide and ends up with about
  6 pt text on A4 portrait. Tall diagrams on 16:9 slides get small too, as the flowchart slide
  shows.
- **A heading can end up alone at the bottom of a page** above a diagram that moves to the next
  page, exactly as above an image. Keeping a heading with a following visual would be a separate
  change to the layout.
- `letter-spacing` (gantt section titles) is ignored.
- PPTX text boxes are estimated at half the font size per character: wide enough for the
  alignment to work, never measured.
- In decks with `CustomTemplateOptions` (no brand), diagrams are drawn in the neutral brand's
  colours and the theme font.
- Mermaider writes a few hard-coded colours (slate `#64748b` for the first gantt section and
  class visibility markers). They are resolved like any other colour, but not taken from the brand.

### Loose ends

- More diagram types (timeline, journey, quadrant, xychart, …), each with a golden test, by
  widening `MermaidRenderer.Supported`.
- A landscape page or a dedicated slide size for very wide diagrams.
