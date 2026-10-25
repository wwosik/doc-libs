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

## For hosts that let others write the Markdown

When the Markdown comes from someone else, often an AI agent working through your service, give
them `PptxGenerator.AuthoringGuide(options)` with the **same options** you pass to `Generate`. It is
Markdown, in English, written for the writer rather than for you:

```csharp
var options = new PptxOptions { CodeBlockRenderers = [new MermaidRenderer()] };
string guide = PptxGenerator.AuthoringGuide(options);   // starts at "##"
```

- **What it covers:** how headings become slides, the layouts and when to use them, the front matter keys of the options' design and the brands front
  matter can pick, the diagrams the renderers draw (each renderer adds its own section through
  `ICodeBlockRenderer.AuthoringGuide`), the characters the brand's fonts lack with what to write
  instead, the errors, and a complete example. Every example in it generates without error.
- **What it leaves to you:** put it under a `#` heading of your own and add what only you know —
  how images are referenced (the paths you resolve into `MarkdownDocument.Resources`), settings
  you fix (and refuse in front matter), size limits, how the result reaches the user.
- **Errors:** pass `DocumentConversionException.Reason` back to the writer as it is; it names
  what to fix (the key, the container, the character, the diagram's line).
- It is generated on each call, so it follows the package version, and it stays under about
  2,500 words.

## Markdown to slides

| Markdown | Slide |
| -------- | ----- |
| First `#` heading | Cover (layout type `title`); the text up to the next heading is the subtitle. |
| Every later `#` heading | Section divider (layout `secHead`, else `title`), with subtitle like the cover. |
| `##` heading | One slide with that title. |
| `##` section using [layouts](#layouts) | One flow slide (layout `titleOnly`): its blocks stacked top to bottom. |
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

## Layouts

A `##` section can ask for more than bullets or one visual. It then becomes a **flow slide**: its
blocks are stacked top to bottom in the content area, and several tables or visuals share the
slide instead of starting continuation slides. A section that uses none of this comes out exactly
as described above. The syntax is the one Pandoc, markdown-it and GitHub use, so it still reads well
as plain Markdown.

| Markdown | On the slide |
| -------- | ------------ |
| `## Title {kicker="Label"}` | A small bold uppercase label above the title, in the brand's link colour; the title moves down. |
| `## Title {.dark}` | The slide on a dark background with white text (both together: `{.dark kicker="Label"}`). |
| `::: cards` … `:::`, one `###` per card | Rounded boxes side by side, outlined in the table line colour, without fill, at most four to a row, each row as tall as its tallest card. The `###` is the card's title; a paragraph that is only `**bold**` right after it is a big figure. |
| `::: cards` around one list of `- **Title** – text` | The same, one card per item. |
| `::: rows` around one list of `- **Term** – text` | The term in bold in the left third, the text beside it, a rule above each row and below the last. |
| `::: panel` or `::: panel dark` | A box behind its blocks, in the band colour or, dark, in the primary colour with white text. |
| `::: statement` | Its first paragraph large and bold; the rest of the container follows as normal blocks. |
| `> [!NOTE]`, `> [!TIP]` | A band with a bar on the left in the primary colour (not italic). |
| `> [!IMPORTANT]`, `> [!WARNING]`, `> [!CAUTION]` | The same with the bar in the highlight colour. |
| A paragraph of only code spans: `` `a` `b` `c` `` (at least two) | Outlined chips in the code font, flowing and wrapping. |
| In a container, a list whose items start with a code span: `` - `write-once` text `` | The span as a bold label chip, the text beside it. |
| A fenced or indented code block (no renderer took it) | Its lines on a rounded box in a light grey (the table line colour at 30 %), 14 pt, line spacing 130 %. On other slides code stays as before. |
| `==text==` | Bold, on a light tint of the highlight colour, like a highlighter pen — on every slide. |
| A table row whose first cell is all `==…==` | The row filled in a light tint of the highlight colour — on every slide. |
| A table cell that is one code span, on a flow slide | A status label: bold, its cell in a light tint of its category's colour. Front matter assigns labels to categories (`labels.success: Done`, see [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#slide-layouts-in-markdown)); others are neutral. |

- **What makes a flow slide:** heading attributes, a `:::` container or an alert anywhere in the
  section, or a tag paragraph as a block of its own. Code-span lists outside containers stay
  bullet lists, code-only table cells on other slides stay code.
- **Containers nest** with longer fences: `:::: panel` around `::: cards` … `:::` and `::::`.
- **Heading attributes** count only as a trailing `{…}` on a `##` heading made of `.class` and
  `key="value"` tokens; other braces (`## Sets {a, b}`) stay in the title. On `#` headings they are
  text.
- **Mistakes throw** `DocumentConversionException`: an unknown attribute or a key given twice, an
  unknown container (the message lists `cards, rows, panel, statement`), arguments on a container
  other than `panel dark`, content before the first `###` in `::: cards`, a `::: rows` that is not
  one list, an item of `cards` or `rows` that does not start with bold text, an unknown alert.
- **Colours** come from the brand: titles of cards and rows, chips and statements in `Primary`
  (also the dark background), kickers and figures in `Link`, panels and callouts in `TableBand`,
  outlines and rules in `TableLine`, highlights in `Highlight`, status labels in `Labels`. On a
  caller's template, which has no brand, the theme colours stand in — text 2, accent 1,
  background 2, accent 2 — and the neutral brand's label colours.
- **Dark slides** use the built-in template's layout *Title Only Dark* (primary background, white
  title and footer, the logo for dark backgrounds). A template without a layout of that name gets
  the title-only layout with a background override and white text on everything the generator
  writes; the template's own master graphics stay as they are.
- **Sizes** on flow slides: text 16 pt, inside cards 14 pt, card titles 16 pt, figures 32 pt,
  statements 26 pt, kickers and chips 12 pt.

## Templates

`PptxOptions.Template` takes one options record per template; each has only the features of its
template.

| Options | Template | Features |
| ------- | -------- | -------- |
| `BrandTemplateOptions` (default) | The built-in master in `Brand`'s design — by default `Brand.Neutral`: white, Arial, dark titles, blue accents, no images, no footer text | `Brand`, `ShowDate` (default on), `ShowTagline` (default on), `EmbedFonts` (default on) |
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
  and body font is the brand's font family (Arial without brand fonts), embedded with
  `EmbedFonts` (see [Fonts](wwosik.DocLibs.DocumentGeneration.md#fonts)).
- **Body style made for Markdown:** level 1 `•`, level 2 `–`, level 3 `•` and so on, with hanging
  indents, 20/18/16 pt, 6 pt before each paragraph. Level 1 is not bold.
- **Layouts:** `title` (cover: large title, subtitle, and — where the brand has them — the logo
  top right, the artwork in the bottom right corner below the subtitle, the tagline over it; no
  footer line), `obj`, `titleOnly`, `secHead` (brand gradient, white title, white footer
  line, the logo for dark backgrounds), and *Title Only Dark* (type `cust`, found by name: the
  primary colour as background, white title and footer line, the logo for dark backgrounds — for
  [dark slides](#layouts)).
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
| `embedFonts` | `true`, `false` | brands on the built-in master or their own `Pptx` template |
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
  can overlap the slide's bottom edge. The same goes for flow slides: an overfull one runs past the
  content area, and is not split. Overfull placeholders rely on the template's autofit, which
  PowerPoint applies only once the text is edited — very long cover or divider titles overflow.
- Text in text boxes takes the theme's text colour, not the layout's: a subtitle placed in a text
  box on a dark caller layout stays dark.
- The footer text sits in a box of fixed width without wrapping; a long one runs into the date.
- A caller template's own body style is used as it is; one made for hand-written slides (level 1
  bold, no bullets on levels 1–2) suits Markdown less well than the built-in template.
- No speaker notes, no charts, no SVG images, no merged table cells.
- Embedded fonts are a PowerPoint feature. Viewers that ignore them — LibreOffice without
  libeot, Google Slides — substitute a font of their own, as for a deck without embedded fonts.
  PowerPoint Online honours them only partly.
- Relative links (`[x](other.md)`, `#anchor`) stay plain text.
