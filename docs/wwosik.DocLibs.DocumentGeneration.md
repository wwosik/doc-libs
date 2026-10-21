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
| `Colors.Theme` | The 12 PowerPoint theme colours (dark 1, light 1, dark 2, light 2, accents 1–6, hyperlink, followed hyperlink) | black, white, `Primary`, `TableBand`, `Primary`, five neutral accents, `Link`, violet |
| `Fonts` | PDF text (embedded); the PPTX theme font family (named only) | Liberation Sans and Mono in PDFs, Arial in decks |
| `Fonts.Table` | Table cells, typically a condensed cut (see [Table fonts](#table-fonts)) | the text faces |
| `FooterText` | Footer of every page and slide but the cover | none |
| `Logo` | Cover top right; PDF page header right; slide footer right | none |
| `LogoOnDark` | PPTX section dividers (on the gradient) | none |
| `CoverArtwork` | Cover bottom right: PDF — page width, ≤ 42 % of the height; PPTX — slide width, below the subtitle | none |
| `Tagline` | Cover bottom right, over the artwork; `ShowTagline` turns it off | none |
| `Pptx` | A `.pptx` / `.potx` decks are built on instead of the built-in master (see the PPTX docs) | none |

- **Colours** are `#RRGGBB` or `RRGGBB`. **Images** are PNG or JPEG and keep their aspect ratio:
  logos and taglines are fitted into fixed boxes, aligned right.
- **Checked when used:** a generator resolves the brand first; an invalid name, colour, theme
  length, image format or font throws `ArgumentException` naming the property
  (`The brand 'acme': Colors.Primary 'teal' is not a colour; …`).
- `Brand.Neutral` (`neutral`) is the default: dark titles, blue accents, no fonts of its own, no
  images, no footer text. Records compare by value, images and fonts by content.

### Fonts

`BrandFonts` takes **four required text faces** — `Regular`, `Bold`, `Italic`, `BoldItalic` — and
optional `Mono` and `MonoBold` (`MonoBold` falls back to `Mono`; without either, code stays in
Liberation Mono). Nothing is synthesized: a missing face is an error.

- **TrueType outlines only:** `.ttf`, or `.otf` with TrueType (`glyf`) outlines. Fonts with CFF
  (PostScript) outlines and font collections are rejected — convert them to TrueType first.
- **PDF:** the faces are embedded as subsets, exactly like the bundled Liberation fonts, and the
  same glyph rules hold: a character the brand's face lacks throws, unless
  `PdfOptions.ReplaceUnsupported` replaces it.
- **PPTX:** the theme's heading and body font is `Family` — by default the typographic family
  name (else the family name) in the regular face's `name` table. Nothing is embedded;
  PowerPoint substitutes a font that is not installed.
- **Licensing is yours to check.** The library does not read a font's embedding permissions
  (`OS/2 fsType`). Embedding a font in a PDF needs a licence that allows it; do not use fonts
  whose licence restricts embedding.

#### Table fonts

`BrandFonts.Table` (`BrandTableFonts`) gives table cells a family of their own — typically a
condensed cut of the text family, so wide tables fit without smaller text.

- **`Regular` and `Bold` are required** (cells, and header rows and bold text); `Italic` falls back
  to `Regular` and `BoldItalic` to `Bold` — to the upright face of the table family, never to the
  text italics. `Family` defaults to the name in `Regular`'s `name` table.
- Checked like the text faces (TrueType outlines only); errors name the property
  (`Fonts.Table.Bold has CFF (PostScript) outlines…`). Table fonts need brand text fonts: they
  live inside `BrandFonts`.
- **PDF:** cells are measured and drawn in the table faces at the usual 9.5 pt; inline code in a
  cell stays mono. Each distinct file is embedded once, also when faces share one.
- **PPTX:** every run in a table cell but code names the table family (`a:latin`); nothing is
  embedded. This applies to the built-in master and to a brand's own `Pptx` template, never to
  `CustomTemplateOptions`, which has no brand. Row heights are still estimated by character
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
    "diagram": { "text": "#1F2937", "line": "#6B7280", "accent": "#0B5FFF", "surface": "#E7EFFF", "border": "#0B5FFF", "palette": ["#0B5FFF", "#3FC1FF", "#F59E0B"] }
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
  "pptx": { "template": "template.potx", "layouts": { "title": "Title Slide", "content": "Title and Content", "titleOnly": "Title Only", "sectionHeader": "Section Header" } }
}
```

Every key but `name` and `colors.primary` is optional (inside `fonts`, the four text faces are
required, and inside `fonts.table` its `regular` and `bold`); `colors` also takes `footer` and `theme` (12 colours), as in the table above. `//` comments and trailing commas are allowed.

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
}
```

- **`CodeBlockRequest`**: `Language` (the info string), `Code`, `Brand` (the brand the document is
  generated in, after front matter) and `Line` (of the opening fence, from 1, front matter
  included).
- **`CodeBlockGraphic`** is a `DrawingGraphic(Drawing)` or an `ImageGraphic(DocumentResource)`.
  The renderers are asked in order and the first graphic wins.
- **`Drawing(Width, Height, Items)`** is a vector drawing in CSS pixels (0.75 pt each) from the top
  left, with colours as `RRGGBB`. Records compare by content.
  - `DrawingShape(Path, Fill, Stroke, StrokeWidth)` has `Dash`, `RoundJoins`, and `Kind`
    (`Path`, `Rectangle`, `RoundedRectangle` with `CornerRadius`, `Ellipse`). Decks use PowerPoint's
    own shapes for the kinds; `Path` always holds the outline. A `DrawingPath` is a list of
    `MoveTo`, `LineTo`, `CubicTo` and `ClosePath`.
  - `DrawingText(X, Baseline, Spans, Size, Color)` is one line of `DrawingSpan(Text, Bold)` with
    `Anchor` (`Start`, `Middle`, `End`), `MaxWidth` (its room: the generator shrinks wider text down
    to 70 %) and `Mono`. It is set in the brand's table font, else its text font, and measured by
    the generator.

[wwosik.DocLibs.DocumentGeneration.Diagrams](wwosik.DocLibs.DocumentGeneration.Diagrams.md)
provides `MermaidRenderer`. Any other diagram language can plug in the same way.

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
- **Shared keys:** `template` (applied first, whatever its position), `title`, `author`. The rest
  are the generator's.
- `template` naming the template the caller chose keeps the caller's values; naming another one
  starts from that template's defaults.
- **Mistakes throw** `DocumentConversionException` with the generator's format: a line that is not
  `key: value`, a key without value, a duplicate key, a block that is never closed, an unknown key
  or one the template does not support (the message lists the valid keys), an invalid value (the
  message lists the valid ones).

## Image headers

`ImageInfo` reads the format (PNG, JPEG, GIF) and pixel size from an image's header, for both
generators.
