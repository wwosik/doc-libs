# wwosik.DocLibs.DocumentGeneration.Pptx

Generates PowerPoint presentations (`.pptx`) from Markdown, on the built-in master in a brand's
design or on a caller's template. Cross-platform, no Office needed. Depends on Markdig, the Open XML SDK
and [wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md) (for `MarkdownDocument`).

```csharp
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pptx;

var document = new MarkdownDocument(
    File.ReadAllText("talk.md"),
    [new DocumentResource("chart.png", "image/png", File.ReadAllBytes("chart.png"))],
    new Dictionary<string, string> { [MetadataKeys.Author] = "DocLibs" });

using var output = File.Create("talk.pptx");
PptxGenerator.Generate(document, output);                                   // neutral brand

PptxGenerator.Generate(document, output, new PptxOptions
{
    Template = new BrandTemplateOptions { Brand = BrandPack.Load("brands/acme"), ShowDate = false },
});

using var template = File.OpenRead("corporate.potx");
PptxGenerator.Generate(document, output, new PptxOptions
{
    Template = new CustomTemplateOptions(template)
    {
        Layouts = new PptxLayoutNames { Title = "Titelfolie", Content = "Titel und Inhalt" },
    },
});
```

- **Stream in, stream out.** Nothing is read from or written to disk; images come from
  `MarkdownDocument.Resources`, found by the path the Markdown references (`./` and `%20` are
  normalized, see `MarkdownDocument.FindResource`).
- **Errors:** a missing image, an image that is not PNG, JPEG or GIF, invalid front matter, a
  layout name the template does not have, or a damaged template throws
  `DocumentConversionException` with `Format = Pptx`.
- **Metadata:** the title (front matter `title`, else metadata `title`, else the first `#`
  heading) and the author (front matter `author`, else metadata `author`) go into the core
  properties.

## Markdown to slides

| Markdown | Slide |
| -------- | ----- |
| First `#` heading | Cover (layout type `title`); the text up to the next heading is the subtitle. |
| Every later `#` heading | Section divider (layout `secHead`, else `title`), with subtitle like the cover. |
| `##` heading | One slide with that title. |
| Section with a table | Table slide (layout `titleOnly`): text before the table above it, text after it below. |
| Section with an image | Image slide (layout `titleOnly`): text before the image above it, text after it as a centred caption. |
| Anything else | Content slide (layout `obj`) with the text in the body placeholder. |
| A further table or image in the same section | A continuation slide with the same title. |
| `-` / `*` lists, nested | Bullets at their level. |
| `1.` lists, nested | Numbered, from the list's start value. |
| Plain paragraph | Text without bullet. |
| `###` and deeper | Bold paragraph. |
| Fenced or indented code | One monospace paragraph per line. |
| Block quote | Italic. |
| `**bold**`, `*italic*`, `~~strike~~`, `` `code` ``, links | Run formatting; links become hyperlinks if their target is an absolute URI. |
| Front matter (see below) | Options, never a slide. |
| Text before the first heading, thematic breaks, HTML comments | Ignored. |

- **Images** keep their natural size (96 DPI) and are only scaled down to fit. `"scale=N.N"` in
  the image title multiplies the natural size, still within the free area. Images are centred.
- **Tables** span the content width; column widths follow the length of their content, cell
  alignment follows the Markdown, and the template's default table style applies.
- **Slide number and date:** every slide but the cover gets the layout's slide number placeholder,
  if it has one, and — with `ShowDate` — its date placeholder with the date of generation
  (`dd.MM.yyyy`, from `PptxOptions.TimeProvider`).

## Templates

`PptxOptions.Template` takes one options record per template; each has only the features of its
template.

| Options | Template | Features |
| ------- | -------- | -------- |
| `BrandTemplateOptions` (default) | The built-in master in `Brand`'s design — by default `Brand.Neutral`: white, Arial, dark titles, blue accents, no images, no footer text | `Brand`, `ShowDate` (default on), `ShowTagline` (default on) |
| `CustomTemplateOptions(stream)` | A caller's `.pptx` or `.potx` | `Layouts` (layout names) |

Brands — colours, fonts, images, brand packs, and how front matter picks one from
`PptxOptions.Brands` — are described in
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#brands). A brand with
its own `Pptx` template is built on that template, exactly like `CustomTemplateOptions` with the
brand's layout names; such decks never get a date and take no `showDate` / `showTagline`.

### The built-in template

One master design, written in code (`TemplateBuilder`), parameterized by a brand (`Brand`, from
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md)):

- 16:9; margins of about 1″, title top left in 32 pt bold in the brand colour, body below it, a
  footer line with slide number, footer text and date, the logo bottom right. The theme's heading
  and body font is the brand's font family (Arial without brand fonts); it is named, not embedded.
- **Body style made for Markdown:** level 1 `•`, level 2 `–`, level 3 `•` and so on, with hanging
  indents, 20/18/16 pt, 6 pt before each paragraph. Level 1 is not bold.
- **Layouts:** `title` (cover: large title, subtitle, and — where the brand has them — the logo
  top right, the artwork in the bottom right corner below the subtitle, the tagline over it; no
  footer line), `obj`, `titleOnly`, and `secHead` (brand gradient, white title, white footer
  line, the logo for dark backgrounds).
- **Images keep their aspect ratio:** the logo, tagline and footer logo are fitted into fixed
  boxes and aligned right; the artwork is at most as wide as the slide and as high as the band
  below the subtitle. PNG and JPEG.
- **Table style** defined in `tableStyles.xml` from the brand colours: header row in the brand
  colour with white bold text, light banded rows, thin grey lines. A brand with
  [table fonts](wwosik.DocLibs.DocumentGeneration.md#table-fonts) names their family on every
  run in a table cell but code — on its own `Pptx` template too.
- **The theme's colour scheme** is the brand's 12 theme colours, see
  [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#brands).

### Caller templates

- **Layouts are found by type** (`title`, `obj` or `tx`, `titleOnly`, `secHead`), not by name.
  Missing ones fall back to a layout with a matching set of placeholders, the section divider to
  the cover layout, and finally to the content layout.
- **`Layouts` names them instead** (`cSld/@name`, as PowerPoint shows them), for templates whose
  layouts carry no type, as many corporate templates do. A named layout wins over the type; a kind left
  `null` is looked up by type. An unknown name throws, listing the template's layout names.
- A cover or section layout **without a title placeholder** gets the title in its first body
  placeholder and the subtitle in the next one, or in a text box below the title.
- The template's slides, custom shows and sections are dropped; masters, layouts, theme and table
  styles are kept. A `.potx` becomes a `.pptx`.
- In placeholders, bullet levels set only `lvl`, so the template's indents, bullets and fonts apply.
  Text above and below images and tables sits in text boxes with explicit indents.
- Caller templates never get a date.

## Front matter

Options can come from the Markdown itself, in a block at the very top. They **override** what the
caller passed.

```markdown
---
template: acme
showTagline: false
title: Network expansion 2027
author: Team Docs
---

# Network expansion 2027
```

- The syntax is shared with the PDF generator, see
  [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#front-matter).

| Key | Values | Templates |
| --- | ------ | --------- |
| `template` | `neutral`, the brand of `Template`, or a brand in `PptxOptions.Brands` | all — switches to that brand on the built-in master, also replacing a caller's custom template |
| `showDate`, `showTagline` | `true`, `false` | brands on the built-in master |
| `layouts.title`, `layouts.content`, `layouts.titleOnly`, `layouts.sectionHeader` | layout name | custom |
| `title`, `author` | text | all — the core properties |

- `template` naming the template the caller chose keeps the caller's values; naming another one
  starts from that template's defaults. The other keys then apply, whatever their order.
- An unknown key, a key the resulting template does not support, an invalid value, a duplicate
  key, a line that is not `key: value`, or a block that is never closed throws, naming the key and
  listing the keys valid for that template.

## Diagrams

`PptxOptions.CodeBlockRenderers` are asked in order about every fenced code block, and the first
graphic wins. A block no renderer takes stays code. For Mermaid, add
`new MermaidRenderer()` from
[wwosik.DocLibs.DocumentGeneration.Diagrams](wwosik.DocLibs.DocumentGeneration.Diagrams.md).
A rendered block gets a slide of its own, like a table or an image, with the text before it above
and the text after it as a caption. A renderer can return:

- a **`DrawingGraphic`**, written as a group of **editable shapes** (`p:grpSp`), fitted into the
  content area like a picture and scaled down, never up. Rectangles, rounded rectangles and
  ellipses become PowerPoint's own shapes (`rect`, `roundRect`, `ellipse`). Every other outline
  becomes custom geometry, and each line of text a text box without insets or wrapping, in the
  brand's table family (else the theme font) at a size that scales with the group. Dashes, round
  joins and colours are kept. Text is shrunk to its room by an estimate of half the font size per
  character, down to 70 %.
- an **`ImageGraphic`**, a PNG, JPEG or GIF placed like a Markdown image, with the language as
  alt text.

## Known limits

- Text heights above and below images and tables are estimated, not measured, so a long caption
  can overlap the slide's bottom edge. Overfull placeholders rely on the template's autofit, which
  PowerPoint applies only once the text is edited — very long cover or divider titles overflow.
- Text in text boxes takes the theme's text colour, not the layout's: a subtitle placed in a text
  box on a dark caller layout stays dark.
- The footer text sits in a box of fixed width without wrapping; a long one runs into the date.
- A caller template's own body style is used as it is; one made for hand-written slides (level 1
  bold, no bullets on levels 1–2) suits Markdown less well than the built-in template.
- No speaker notes, no charts, no SVG images, no merged table cells.
- Relative links (`[x](other.md)`, `#anchor`) stay plain text.
