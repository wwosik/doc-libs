# wwosik.DocLibs.DocumentGeneration

What the generators [wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md)
and [wwosik.DocLibs.DocumentGeneration.Pdf](wwosik.DocLibs.DocumentGeneration.Pdf.md) share: the
public **brand** API (`Brand`, `BrandPack`), the **drawing** model and the code-block renderer
interface, and internally front matter and image headers. It
comes along as a dependency of either generator. Depends only on
[wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md); AOT-compatible.

## Brands

A brand is a client's or project's design: colours, fonts, footer text, images, and optionally its
own PowerPoint template. Both generators draw their one built-in design in it —
`BrandTemplateOptions` for PPTX, `BrandPdfTemplateOptions` for PDF — so a single `Brand` styles
decks and PDFs alike. Brands live in the client's code or package, never in this repo.

```csharp
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

var acme = new Brand
{
    Name = "acme",
    Colors = new BrandColors { Primary = "#0B5FFF" },
    FooterText = "ACME Corporation",
    Logo = new BrandImage(File.ReadAllBytes("logo.png")),
};

PdfGenerator.Generate(document, pdf, new PdfOptions { Template = new BrandPdfTemplateOptions { Brand = acme } });
PptxGenerator.Generate(document, pptx, new PptxOptions { Template = new BrandTemplateOptions { Brand = acme, ShowTagline = false } });
```

### What a brand holds

Only `Name` and `Colors.Primary` are required; everything else falls back to a default or is not
drawn.

| Property | Where it shows | Default |
| -------- | -------------- | ------- |
| `Name` | Front matter `template: <name>`; the PPTX theme name. Letters, digits, `-`; case-insensitive. | required |
| `Colors.Primary` | Titles, headings, the cover title | required |
| `Colors.Link` | Links | `Primary` |
| `Colors.Footer` | Header and footer text | `6B7280` |
| `Colors.TableHeader` / `TableBand` / `TableLine` | Header row (white bold text) / every other row / grid | `Primary` / `Primary` at 10 % on white / `D1D5DB` |
| `Colors.Gradient` | PPTX section dividers, left to right, ≥ 2 colours | `Primary` → `Link`, or → `Primary` at 60 % on white when they are equal |
| `Colors.Diagram` | Diagrams: `Text`, `Line`, `Accent`, `Surface`, `Border`, `Palette` (see [Diagrams](wwosik.DocLibs.DocumentGeneration.Diagrams.md#colours-and-font)) | text colour, `Footer`, `Primary`, `TableBand`, `Primary`, theme accents 1–6 |
| `Colors.Chart` | Financial charts (see [Charts](wwosik.DocLibs.DocumentGeneration.Charts.md#colours)): `Series` (up to three; fills are mixed from each), `Categories`, `Reference`, `ReferenceFill`, `Axis`, `Gridline`, `Band`, `Emphasis`, `Ahead`, `Level`, `Behind`, `Surface`, `TableHeader`, `TotalHeader`, `TotalFill`, `TooltipFill`, `TooltipText` | diagram palette, greys, `Labels.Success` / `Footer` / `Labels.Danger` for the verdicts, white surface (none on dark slides) |
| `Colors.Highlight` | Slides: `==marked==` text (a light tint behind it), highlighted table rows, the bar of important callouts | theme accent 2 |
| `Colors.Labels` | Slides: status labels in tables by category — `Success`, `Info`, `Warning`, `Danger`, `Neutral`; the cell gets a light tint of the colour (see [Slide layouts in Markdown](#slide-layouts-in-markdown)) | `16A34A`, `Link`, `D97706`, `DC2626`, `9CA3AF` |
| `Colors.Theme` | The 12 PowerPoint theme colours (dark 1, light 1, dark 2, light 2, accents 1–6, hyperlink, followed hyperlink) | black, white, `Primary`, `TableBand`, `Primary`, five neutral accents, `Link`, violet |
| `Fonts` | PDF text (embedded as subsets); the PPTX theme font family and code (embedded whole, see [Fonts](#fonts)) | Liberation Sans and Mono in PDFs, Arial and Consolas in decks |
| `Fonts.Table` | Table cells, typically a condensed cut (see [Table fonts](#table-fonts)) | the text faces |
| `FooterText` | Footer of every page and slide but the cover | none |
| `Logo` | Cover top right; PDF page header right; slide footer right | none |
| `LogoOnDark` | PPTX section dividers (on the gradient) | none |
| `CoverArtwork` | Cover bottom right: PDF — page width, ≤ 42 % of the height; PPTX — slide width, below the subtitle | none |
| `Tagline` | Cover bottom right, over the artwork; `ShowTagline` turns it off | none |
| `Pptx` | A `.pptx` / `.potx` decks are built on instead of the built-in master (see the PPTX docs); with `ClosingLayout` (the closing slide's layout by name) and `Tone` (`Auto`, `Light`, `Dark`: whether its slides are dark) | none |
| `SlideColors` | Colours of decks where they differ from PDFs (see [Slide colours](#slide-colours)) | `Colors` |

- **Colours** are `#RRGGBB` or `RRGGBB`. **Images** are PNG or JPEG and keep their aspect ratio:
  logos and taglines are fitted into fixed boxes, aligned right.
- **Checked when used:** a generator resolves the brand first; an invalid name, colour, theme
  length, image format or font throws `ArgumentException` naming the property
  (`The brand 'acme': Colors.Primary 'teal' is not a colour; …`).
- `Brand.Neutral` (`neutral`) is the default: dark titles, blue accents, no fonts of its own, no
  images, no footer text. Records compare by value, images and fonts by content.

### Slide colours

`Brand.SlideColors` is a second `BrandColors` for decks: every optional colour it leaves `null`
comes from `Brand.Colors`, so a brand states only what differs — typically a lighter `Primary` and
`Highlight` for a template with dark slides, where the green or blue that suits white pages is too
dark. PDFs never read them. Decks use them everywhere: the built-in master, flow-slide colours,
diagrams and charts. On dark slides, diagrams take white text and lines, nodes in a dark shade of the
slide primary and the slide highlight as accent, unless `SlideColors.Diagram` names its own.

### Fonts

`BrandFonts` takes **four required text faces** — `Regular`, `Bold`, `Italic`, `BoldItalic` — and
optional `Mono` and `MonoBold` (`MonoBold` falls back to `Mono`; without either, code stays in
Liberation Mono in PDFs and in `PptxOptions.CodeFont`, else Consolas, in decks). Nothing is
synthesized: a missing face is an error.

- **TrueType outlines only:** `.ttf`, or `.otf` with TrueType (`glyf`) outlines. Fonts with CFF
  (PostScript) outlines and font collections are rejected — convert them to TrueType first.
- **PDF:** the faces are embedded as subsets, exactly like the bundled Liberation fonts, and the
  same glyph rules hold: a character the brand's face lacks throws, unless
  `PdfOptions.ReplaceUnsupported` replaces it.
- **PPTX:** the theme's heading and body font is `Family` — by default the family name (name
  ID 1, else the typographic family name, ID 16) in the regular face's `name` table. Name ID 1
  is the name Windows groups the four faces under, and the name PowerPoint finds an embedded
  font by; for a condensed or light cut it can differ from the typographic name (e.g. `Acme Sans
  Cond`, not `Acme Sans Condensed`). Code is set in the mono family (its name ID 1) unless
  `PptxOptions.CodeFont` names another font.
- **PPTX, embedded:** by default (`EmbedFonts`, front matter `embedFonts`) a deck carries its
  brand's fonts, so it shows them where they are not installed. Each face is written as
  PowerPoint does: an uncompressed Embedded OpenType in a font part, listed in
  `p:embeddedFontLst` under its family name.
  - The text family is always embedded. The table and mono families are embedded only when a
    slide uses them, because each family adds about 0.2 MB per face before compression.
  - Faces are embedded whole, not as subsets, so the deck can be edited with any character.
    Each file is embedded once; a face that falls back to another (a table italic to the table
    regular) is not listed as a face of its own.
  - A face whose `OS/2 fsType` is *restricted licence embedding* (2) is left out, and the whole
    family with it if it is the regular face. The deck then only names the family.
  - Viewers that ignore embedded fonts substitute their own, as without embedding.
- **Licensing is yours to check.** Apart from the restricted flag in decks, the library does not
  read a font's embedding permissions (`OS/2 fsType`). Embedding a font in a PDF or a deck needs a
  licence that allows it; do not use fonts whose licence restricts embedding.

#### Table fonts

`BrandFonts.Table` (`BrandTableFonts`) gives table cells a family of their own — typically a
condensed cut of the text family, so wide tables fit without smaller text.

- **`Regular` and `Bold` are required** (cells, and header rows and bold text); `Italic` falls back
  to `Regular` and `BoldItalic` to `Bold` — to the upright face of the table family, never to the
  text italics. `Family` defaults to the name in `Regular`'s `name` table, read like the text
  family's.
- Checked like the text faces (TrueType outlines only); errors name the property
  (`Fonts.Table.Bold has CFF (PostScript) outlines…`). Table fonts need brand text fonts: they
  live inside `BrandFonts`.
- **PDF:** cells are measured and drawn in the table faces at the usual 9.5 pt; inline code in a
  cell stays mono. Each distinct file is embedded once, also when faces share one.
- **PPTX:** every run in a table cell but code names the table family (`a:latin`), which is
  embedded like the text family when a table or diagram uses it. This applies to the built-in
  master and to a brand's own `Pptx` template, never to `CustomTemplateOptions`, which has no
  brand. Row heights are still estimated by character
  count, so a condensed font only leaves rows roomier.
- **Without table fonts** tables use the text faces and decks write no table font, exactly as
  before.

### Selecting a brand in front matter

`PptxOptions.Brands` and `PdfOptions.Brands` list the brands the Markdown may pick with
`template: <name>`. Always available besides them: `neutral` and the brand of the caller's
template. Naming the caller's brand keeps the caller's `ShowDate` / `ShowTagline`; naming another
starts from its defaults. An unknown name throws, listing the known ones; two different brands
with one name throw `ArgumentException`. Front matter can only choose among these brands — it
never names a file, so untrusted Markdown cannot make the library read anything.

### Brand packs

A brand can also be kept as files — a **brand pack**: `brand.json` plus the files it names, by
paths relative to the pack.

```json
{
  "name": "acme",
  "footerText": "ACME Corporation",
  "colors": {
    "primary": "#0B5FFF",
    "link": "#0B5FFF",
    "table": { "header": "#0B5FFF", "band": "#E7EFFF", "line": "#C9D3E0" },
    "gradient": ["#0B5FFF", "#3FC1FF"],
    "diagram": { "text": "#1F2937", "line": "#6B7280", "accent": "#0B5FFF", "surface": "#E7EFFF", "border": "#0B5FFF", "palette": ["#0B5FFF", "#3FC1FF", "#F59E0B"] },
    "highlight": "#3FC1FF",
    "labels": { "success": "#16A34A", "info": "#0B5FFF", "warning": "#F59E0B", "danger": "#DC2626", "neutral": "#9CA3AF" }
  },
  "images": { "logo": "logo.png", "logoOnDark": "logo-on-dark.png", "coverArtwork": "cover.png", "tagline": "tagline.png" },
  "fonts": {
    "regular": "fonts/Sans-Regular.ttf", "bold": "fonts/Sans-Bold.ttf",
    "italic": "fonts/Sans-Italic.ttf", "boldItalic": "fonts/Sans-BoldItalic.ttf",
    "mono": "fonts/Mono-Regular.ttf", "monoBold": "fonts/Mono-Bold.ttf",
    "family": "Acme Sans",
    "table": {
      "regular": "fonts/SansCondensed-Regular.ttf", "bold": "fonts/SansCondensed-Bold.ttf",
      "italic": "fonts/SansCondensed-Italic.ttf", "boldItalic": "fonts/SansCondensed-BoldItalic.ttf",
      "family": "Acme Sans Condensed"
    }
  },
  "slideColors": { "primary": "#7FB3FF", "highlight": "#FFC94D" },
  "pptx": {
    "template": "template.potx",
    "tone": "dark",
    "layouts": { "title": "Title Slide", "content": "Title and Content", "titleOnly": "Title Only", "sectionHeader": "Section Header", "closing": "Closing" }
  }
}
```

Every key but `name` and `colors.primary` is optional (inside `fonts`, the four text faces are
required, and inside `fonts.table` its `regular` and `bold`); `colors` also takes `footer`, `theme` (12 colours) and `chart` (the roles of `BrandChartColors` in camel case, such as `"chart": { "series": ["#0B5FFF", "#3FC1FF"], "ahead": "#16A34A" }`), as in the table above. `slideColors` takes the same keys as `colors`, all optional; `pptx.tone` is `auto` (the default), `light` or `dark`. `//` comments and trailing commas are allowed.

| Load from | Call |
| --------- | ---- |
| A directory | `BrandPack.Load("brands/acme")` — files outside it, also through symbolic links, are refused |
| A zip | `BrandPack.Load(stream)` — `brand.json` at the archive's root; the stream is left open |
| Embedded resources | `BrandPack.Load(assembly, "Brand.")` — resource name = prefix + path, with `/`, `\` or `.` between folders |
| Anything else | `BrandPack.Load(path => …)` — the callback gets a `/`-separated path relative to the pack and returns a stream or `null` |

- **Mistakes throw `InvalidDataException`** naming the key: an unknown key (typos are caught), a
  missing required key, a file that is not in the pack, or a path with `..` or an absolute one.
- The files' contents (image formats, fonts) are checked when a generator uses the brand, like
  any other brand.
- The directory overload is the library's only file-system read outside `OfficeConverter`, and
  only when the caller asks for it.

### Shipping a client's brand

- **In code** — the app builds the `Brand` itself. Simplest for one app.
- **As a private NuGet package** — e.g. `Acme.DocLibs.Brand`, referencing
  `wwosik.DocLibs.DocumentGeneration` and embedding a brand pack. Recommended for a brand several
  apps share; it stays in the client's own repo and feed.

  ```xml
  <ItemGroup>
    <EmbeddedResource Include="Brand\**" LogicalName="Brand.%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
  ```

  ```csharp
  public static class AcmeBrand
  {
      public static Brand Brand { get; } = BrandPack.Load(typeof(AcmeBrand).Assembly, "Brand.");
  }
  ```

- **As files** — a brand pack folder or zip deployed with an app or kept in a document repo, for
  people who maintain the design without writing code.

## Drawings and code-block renderers

Fenced code blocks can become graphics in both generators through `ICodeBlockRenderer`, listed in
`PdfOptions.CodeBlockRenderers` / `PptxOptions.CodeBlockRenderers`:

```csharp
public interface ICodeBlockRenderer
{
    CodeBlockGraphic? Render(CodeBlockRequest request);   // null: leave the block as code
    string? AuthoringGuide => null;                       // its section of the generators' authoring guides
}
```

- **`CodeBlockRequest`**: `Language` (the info string), `Code`, `Brand` (the brand the document is
  generated in, after front matter) and `Line` (of the opening fence, from 1, front matter
  included).
- **`AuthoringGuide`** is what a Markdown writer needs to know about the blocks the renderer
  takes, as Markdown under a `##` heading of its own. The generators' `AuthoringGuide(options)`
  includes the sections of the renderers in the options; a renderer without one adds nothing.
- **A renderer that throws** fails the generation with a `DocumentConversionException` in the
  generator's format, with the same message and the renderer's exception as `InnerException`, so
  every problem in a document has one exception type. A `DocumentConversionException` from a
  renderer is passed on as it is.
- **`CodeBlockGraphic`** is a `DrawingGraphic(Drawing)`, an `ImageGraphic(DocumentResource)` or a
  `ChartGraphic(ChartSpec, Drawing)`: a bar, line or area chart (`ChartSpec`: `Kind`, `Categories`,
  `Series` of `ChartSeries(Name, Values)`, `Stacked`, `Title`, `Unit`, `Colors`) that decks write as
  a native chart and PDFs draw as its `Drawing`. The renderers are asked in order and the first
  graphic wins.
- **`Drawing(Width, Height, Items)`** is a vector drawing in CSS pixels (0.75 pt each) from the top
  left, with colours as `RRGGBB`. Records compare by content.
  - `DrawingShape(Path, Fill, Stroke, StrokeWidth)` has `Dash`, `RoundJoins`, and `Kind`
    (`Path`, `Rectangle`, `RoundedRectangle` with `CornerRadius`, `Ellipse`). Decks use PowerPoint's
    own shapes for the kinds; `Path` always holds the outline. A `DrawingPath` is a list of
    `MoveTo`, `LineTo`, `CubicTo` and `ClosePath`.
  - `DrawingText(X, Baseline, Spans, Size, Color)` is one line of `DrawingSpan(Text, Bold)` with
    `Anchor` (`Start`, `Middle`, `End`), `MaxWidth` (its room: the generator shrinks wider text down
    to 70 %) and `Mono`. It is set in the brand's table font, else its text font, and measured by
    the generator. A span's `Color` overrides the line's.
  - `Hover` on any item is a `DrawingHover(Text, X, Y)`: a tooltip for SVG output, which decks and
    PDFs ignore. `DrawingHotspot(X, Y, Radius)` is an invisible hit area that only carries one,
    with an optional `Dot` shown while hovered (a point on a line); decks and PDFs skip it.

### SVG

`DrawingSvg.Write(drawing, options)` writes a drawing as a standalone SVG for web pages, `width`
and `height` in pixels with a matching `viewBox`.

- **Tooltips without script:** an item with `Hover` becomes `<g class="chart-hoverable">` holding the
  item and a `<g class="chart-tooltip">` placed in advance: centred over the hover point, 17 px
  high, kept on the canvas. An inline `<style>` shows it on `:hover`; the class names
  (`chart-hoverable`, `chart-tooltip`, `chart-tooltip-bg`, `chart-tooltip-text`,
  `chart-hover-dot`) stay the same, so a page can restyle them.
- **Text widths are estimated** (0.58 × size per character), as the SVG is written, not measured:
  for tooltips, and to shrink a line wider than its `MaxWidth`.
- **Options** (`DrawingSvgOptions`): `FontFamily` and `MonoFamily` (CSS families), `Background`
  (default transparent), `TooltipFill` and `TooltipText`.
- For a PNG, pass the SVG to `SvgConverter` in
  [wwosik.DocLibs.DocumentConversions.Svg](wwosik.DocLibs.DocumentConversions.Svg.md).

[wwosik.DocLibs.DocumentGeneration.Diagrams](wwosik.DocLibs.DocumentGeneration.Diagrams.md)
provides `MermaidRenderer`. Any other diagram language can plug in the same way.

## Slide layouts in Markdown

The PPTX generator draws [layouts](wwosik.DocLibs.DocumentGeneration.Pptx.md#layouts) from Markdown:
heading attributes (`## Title {.dark kicker="Label" layout="Name"}`, `# Thank you {.closing}`),
`:::` containers (`cards`, `rows`, `panel`, `statement`, `columns`), GitHub alerts (`> [!NOTE]`),
tag lists of code spans, `==marked==` text, `[words]{.success}` in a status colour, and paragraphs of
only images, side by side.
The PDF generator accepts the same Markdown, so one source serves both, and shows it plainly: see
[its documentation](wwosik.DocLibs.DocumentGeneration.Pdf.md#slide-layouts). Both reject the same
mistakes — unknown heading attributes, containers and alerts — with a `DocumentConversionException`
in their format. Custom containers are block-level only in both: `::text::` stays text.

Front matter can assign status labels — table cells that are one code span — to categories, one key
per category with its labels separated by `;`:

```markdown
---
labels.success: Done; Delivered
labels.info: In review
labels.warning: Next
labels.neutral: Later
---
```

The categories are `success`, `info`, `warning`, `danger` and `neutral`; decks tint a label's cell
in the brand's colour for it (`Colors.Labels`), and a label no key names is neutral. Labels match
exactly. An unknown category or a label in two categories throws, in both generators; PDFs show the
labels as code.

`[words]{.success}` — a bracketed span with exactly one category class — sets the words bold in that
category's colour, in decks and PDFs; on dark slides the colour is lightened halfway to white. Code
is never read for it, and any other brackets and braces stay text. An unknown category throws.

## Front matter

Both generators read options from a block at the very top of the Markdown; the keys each one
understands are in its own documentation. Values there **override** what the caller passed.

```markdown
---
template: neutral
title: Network expansion 2027
---
```

- The block starts with `---` on the first line, followed directly by a non-blank line, and ends
  at the next `---` or `...`. Inside: one `key: value` per line; blank lines and `#` comments are
  allowed; values may be quoted (`"…"` or `'…'`). Keys and choice values are case-insensitive. It
  looks like YAML but is only this flat subset — no nesting, no lists.
- An opening `---` followed by a blank line is a thematic break, not front matter.
- **Shared keys:** `template` (applied first, whatever its position), `title`, `author`, and
  `labels.<category>` (see [Slide layouts in Markdown](#slide-layouts-in-markdown)). The rest are
  the generator's.
- `template` naming the template the caller chose keeps the caller's values; naming another one
  starts from that template's defaults.
- **Mistakes throw** `DocumentConversionException` with the generator's format: a line that is not
  `key: value`, a key without value, a duplicate key, a block that is never closed, an unknown key
  or one the template does not support (the message lists the valid keys), an invalid value (the
  message lists the valid ones).

## Image headers

`ImageInfo` reads the format (PNG, JPEG, GIF) and pixel size from an image's header, for both
generators.

## Image converters

An image in a format a generator cannot place — PDFs take PNG and JPEG, decks also GIF — goes to
`PdfOptions.ImageConverters` / `PptxOptions.ImageConverters`, a list of `IImageConverter` (from
wwosik.DocLibs.DocumentConversions), empty by default:

```csharp
public interface IImageConverter
{
    ConvertedImage? Convert(DocumentResource image, double scale);   // null: not a format it takes
}
```

- The converters are asked in order; the first result wins. Without a result the image stays an
  error, as without converters.
- `ConvertedImage(Content, ContentType, Width, Height)` is a PNG or JPEG drawn at `scale` pixels per
  pixel of the image's natural size (the generators ask for 2, PDFs times the image's `scale=`), and
  that natural size. The image is placed at its natural size, as a PNG of that size would be.
- A converter's exception other than `DocumentConversionException` fails the generation with a
  `DocumentConversionException` in the generator's format, with the converter's as `InnerException`.
- `SvgImageConverter` in [wwosik.DocLibs.DocumentConversions.Svg](wwosik.DocLibs.DocumentConversions.Svg.md)
  converts SVG.
