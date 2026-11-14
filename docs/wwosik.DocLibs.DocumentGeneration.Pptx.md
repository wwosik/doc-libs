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
- **Notes:** `Generate` returns a `GenerationResult` with the slides written and notes for the
  writer — slides split, set at the smallest size or still overfull, images enlarged, diagrams left as code, HTML shown as text — each with its line. Pass them back too; the guide explains each code. See
  [Generation results and notes](wwosik.DocLibs.DocumentGeneration.md#generation-results-and-notes).
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
| `- [ ]` / `- [x]` task lists | The item's bullet is an empty or ticked box (Wingdings `U+F0A8` / `U+F0FE`), also in placeholders, numbered lists included. |
| Definition list (`Term`, then `:   description`) | Rows like `::: rows`, so the section becomes a flow slide: the term in bold on the left, its descriptions beside it. Descriptions hold text only. |
| `[^1]` footnote reference | A raised number (baseline +30 %), counted from 1 on each `##` section in the order of first reference; the footnote text goes to the foot of the slide, see below. |
| Front matter (see below) | Options, never a slide. |
| Text before the first heading, thematic breaks, HTML comments | Ignored. |

- **Images** fill the room they have, keeping their aspect ratio, scaled down or up — up to
  `PptxOptions.MaxImageUpscale` times their natural size (96 DPI; default 4). `"scale=N.N"` in the
  image title caps it per image (`scale=1`: never beyond natural size). Images are centred.
- **SVG images** need an image converter: with `ImageConverters = [new SvgImageConverter()]`
  (from [wwosik.DocLibs.DocumentConversions.Svg](wwosik.DocLibs.DocumentConversions.Svg.md)) an
  `.svg` image is placed at its natural size like any other, as a PNG drawn twice as large, with the
  SVG itself beside it in PowerPoint's SVG extension: PowerPoint 2016 and later draw the SVG, other
  viewers the PNG. Without a converter it is an error.
- **Images side by side:** a paragraph of nothing but two or more images (`![A](a.png) ![B](b.png)`)
  is one visual: the images in cells of equal width across the content width, each fitted into its
  cell; more than four wrap into further rows. `"caption"` in an image's title puts its alt text
  under it (14 pt, centred).
- **Image attributes** after an image crop and colour it; see [below](#image-attributes).
- **Tables** span the content width. Every column is at least as wide as its longest word (numbers
  never break), measured by character class and bold; the rest of the width goes to the columns by
  the length of their text. A table whose words do not fit steps its font down from 16 pt (14 pt
  from 7 rows, 12 pt from 11) to 10 pt before anything wraps inside a word. Cell alignment follows
  the Markdown, and the template's default table style applies.
- **Footnotes** of a slide are set in 10 pt at the foot of its content area, each with its number
  raised before it; the content area is shortened by their height, so fitting leaves room for them.
  A slide split by [fitting](#fitting) is measured with all its footnotes, and each part keeps those
  it refers to.
- **Language:** every run carries the [document language](wwosik.DocLibs.DocumentGeneration.md#document-language),
  for PowerPoint's spell checking.
- **Slide number, date and footer text:** every slide but the cover (and the closing slide) gets the
  layout's slide number placeholder, if it has one, and — with `ShowDate` — its date placeholder
  with the date of generation (`dd.MM.yyyy`, from `PptxOptions.TimeProvider`). On a brand's own
  template, `Brand.FooterText` goes into the layout's footer placeholder (`ShowFooter`, default on).

### Image attributes

`![Team](team.jpg){fit=cover focus=top .round .grey}` (the syntax is in
[DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#slide-layouts-in-markdown)). The picture
stays a picture PowerPoint can edit: every crop is `a:srcRect`, every shape a preset.

| Attribute | On slides |
| --------- | --------- |
| `fit=contain` | The default: the whole image, fitted into its box as above. |
| `fit=cover` | The picture takes its whole box — the visual's room on an image slide, its cell in a row, its place on a layout slide or in a card — and what sticks out is cropped (`a:srcRect`). `scale` has no effect with it. |
| `focus=top`, `bottom`, `left`, `right`, `center` | Which part stays when the image is cropped (default `center`); without `fit=cover` or `.round` it has no effect. |
| `.round` | Cropped to a square by `focus`, then drawn as the preset `ellipse`. With `fit=cover` the circle is as large as the box's shorter side. |
| `.grey` | `a:grayscl` on the picture's blip, PowerPoint's *Grayscale* colour; the image itself is unchanged. A converted SVG keeps its SVG beside the PNG. |
| `.caption` | The same as `"caption"` in the title. |
| `scale=N.N` | The same as `"scale=N.N"` in the title: an upper bound on the enlargement. |

### QR codes and icons

How codes and icons are made is in [DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#qr-codes-and-icons).
On slides:

- **`![Survey](qr:…)`** is a visual like a diagram: a group of two editable shapes (the white square
  with its quiet zone, a custom-geometry path of all modules) plus a text box for the caption. It
  is fitted like a diagram, at most 1.5 times its size and at most a third of the content width;
  with `scale=` it is drawn at its scaled size, never larger. On dark slides it stays dark on white.
- **Card figure:** in `::: cards`, a paragraph that is only `:icon-name:` right after the `###` is
  the card's figure: the icon as a group of shapes as tall as a line of 32 pt figure text, in the
  accent colour (white on a dark block of a light deck).
- **Icon list:** a list whose every item is one paragraph starting with `:icon-name:` becomes an
  icon list, a flow block (`IconListBlock`): per item a row with the icon on the left, as tall as the
  first line, in the accent colour, and the item's text in a text box beside it. Like other layouts
  it makes its section a flow slide; on splitting, its items are units, as rows are; with a build
  marker (or `build: lists`) it builds item by item, icon and text together. A list where not every
  item starts with an icon (or an item holds more than one paragraph) stays a list with its bullets.
- **Anywhere else** — inside text, in a title, a table cell, speaker notes, footnotes, or a list that
  is not an icon list — DrawingML cannot hold a shape inside a run: the icon is left out of the text,
  and the note `icon-not-drawn` gives its line.
- `PptxOptions.Icons` (default `TablerIcons.Outline`) replaces the set; `null` switches icons off,
  and `:icon-name:` stays text.

## Presenting

- **Speaker notes:** `::: notes` … `:::` at the top level of a `#` or `##` section is taken out of
  the section before the kind of slide is decided, so it never turns a text slide into a flow slide.
  Its paragraphs and lists (bullets, numbers, levels, task boxes; bold, italic, code, links, line
  breaks) go on the notes page of **every slide the section produces**: the cover or divider, each
  visual slide, and every part of a slide split by [fitting](#fitting). Several in one section are
  joined in order. Tables become lines with their cells separated by ` | `, code blocks and HTML
  their lines, quotes, alerts, headings and other containers their text, an image its alt text;
  each is reported as `notes-simplified`. A `::: notes` before the first heading is dropped with
  `notes-dropped`; one inside another container, a list, a quote or another `::: notes` throws.
- **Notes pages** have the slide image and a body placeholder (`type="body" idx="1"`) with the
  speaker notes, lists with explicit bullets and numbers, code in the deck's code font, links as
  hyperlinks of the notes page, every run in the document language. They use the template's notes
  master; a template without one (the built-in master included) gets a built-in notes master — see
  [Templates](#templates). A deck without speaker notes gets neither, so it stays as before.
- **Transitions:** front matter `transition:` (else `PptxOptions.Transition`, default
  `SlideTransition.None`) sets every slide's; `transition="…"` on a `#` or `##` heading sets that
  heading's slides', `transition="none"` included, and keeps a plain slide plain, as `layout="…"`
  does. Split parts keep their slide's. The names, each written as `p:transition` with PowerPoint's
  default direction:

  | Name | Written as | Speed |
  | ---- | ---------- | ----- |
  | `none` | nothing | |
  | `fade` | `p:fade` | medium |
  | `push` | `p:push` | slow |
  | `wipe` | `p:wipe` | slow |
  | `split` | `p:split` | slow |
  | `cover` | `p:cover` | slow |
  | `uncover` | `p:pull` | slow |
  | `zoom` | `p:zoom` | medium |
  | `cut` | `p:cut` | fast |
  | `morph` | `mc:AlternateContent`: `p159:morph` (`option="byObject"`, PowerPoint 2019 and Microsoft 365), with `p:fade` as the fallback | slow |

  An unknown name throws in both generators.
- **Builds:** a paragraph that is nothing but a build marker makes the block after it come in on
  clicks in the slide show. Only braces of classes and quoted `key="value"` pairs count, as for
  heading attributes; any other paragraph of such braces (`{.note}`, `{width="50%"}`) throws, listing
  the markers, and `\{` keeps braces as text. The markers, each PowerPoint's own preset, so the
  Animation pane names it:

  | Marker | Click | Preset |
  | ------ | ----- | ------ |
  | `{.build}`, `{.build=appear}` | the block appears, item by item | entrance Appear (`presetID` 1) |
  | `{.build=fly}` | the same, flying in | entrance Fly In from the bottom (2, subtype 4) |
  | `{.build=wipe}` | the same, wiped in | entrance Wipe from the left (22, subtype 8) |
  | `{.frag}` | the whole block appears | entrance Appear |
  | `{.exit}` | the block, shown at first, disappears | exit Disappear (1) |
  | `{.emphasize}` | the block pulses and stays as it was | emphasis Pulse (26) |

  **Item by item** means: a list by its top-level items (sub-items with their item), `cards` by card,
  `rows` by row, `columns` by column, the labelled tags of a container list by item, an image row by
  image; any other block appears whole, tables and charts too. Several classes in one marker
  (`{.frag .exit}`) are consecutive clicks in their written order.
  - **Where markers go:** at the top level of a section, or directly inside a `:::` container or an
    alert, where they build what follows them there. A marker before a container builds the whole
    container; markers inside it build their block on a later click. The blocks of `::: statement`
    follow on the slide and build with the statement, unless they have their own marker. A marker
    followed by a heading, `::: notes`, another marker, a `---` or nothing, and one inside a list, a
    quote, a footnote or speaker notes, throw with its line, in both generators.
  - **`build: lists`** in the front matter (else `PptxOptions.Build`, `DeckBuild.None` or `Lists`)
    gives every top-level list of a section without a marker `{.build}`.
  - **On the slide:** content slides build by paragraph of their body placeholder; visual slides the
    text above, the visual and the caption below; flow slides by block (every shape written for the
    block) or by item. A paragraph split at an image builds its text and the image together. Each
    part of a [split](#fitting) slide plays the steps of what it holds, from its first click.
  - **Written as** `p:timing` after `p:transition`, as the schema orders them: the main sequence,
    one click group per step, the first shape of a step `clickEffect` and the others `withEffect`;
    text by paragraph range (`p:txEl/p:pRg`); `p:bldLst` with `p:bldP build="p"` for paragraphs,
    `p:bldP animBg="1"` for whole shapes and `p:bldGraphic` with `p:bldAsOne` for tables and charts
    (pictures and groups have none). A slide without builds gets no `p:timing`, so decks without
    markers stay as before. `PptxConverter` does not read builds back.

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
| `::: cards` … `:::`, one `###` per card | Rounded boxes side by side, outlined in the table line colour, without fill, at most four to a row, each row as tall as its tallest card. The `###` is the card's title; a paragraph that is only `**bold**` right after it is a big figure, a paragraph that is only `:icon-name:` a big icon (see [QR codes and icons](#qr-codes-and-icons)). |
| A list whose every item starts with `:icon-name:` | An icon list: each item's icon on the left in the accent colour, its text beside it, instead of bullets. |
| `::: cards` around one list of `- **Title** – text` | The same, one card per item. |
| `::: rows` around one list of `- **Term** – text` | The term in bold in the left third, the text beside it, a rule above each row and below the last. |
| `::: panel` or `::: panel dark` | A box behind its blocks, in the band colour or, dark, in the primary colour with white text. |
| `::: statement` | Its first paragraph large and bold; the rest of the container follows as normal blocks. |
| `::: columns` (or `::: columns 1:2`) … `---` … `:::` | Two or three columns side by side, split at `---` lines (a blank line before each, or Markdown reads the line above as a heading); widths equal or in the ratio given. Each column stacks its blocks: text beside an image or a chart. |
| `> [!NOTE]`, `> [!TIP]` | A band with a bar on the left in the primary colour (not italic). |
| `> [!IMPORTANT]`, `> [!WARNING]`, `> [!CAUTION]` | The same with the bar in the highlight colour. |
| A paragraph of only code spans: `` `a` `b` `c` `` (at least two) | Outlined chips in the code font, flowing and wrapping. |
| In a container, a list whose items start with a code span: `` - `write-once` text `` | The span as a bold label chip, the text beside it. |
| A fenced or indented code block (no renderer took it) | Its lines on a rounded box in a light grey (the table line colour at 30 %), 14 pt, line spacing 130 %. On other slides code stays as before. |
| `==text==` | Bold, on a light tint of the highlight colour, like a highlighter pen — on every slide. |
| A table row whose first cell is all `==…==` | The row filled in a light tint of the highlight colour — on every slide. |
| A table cell that is one code span, on a flow slide | A status label: bold, its cell in a light tint of its category's colour. Front matter assigns labels to categories (`labels.success: Done`, see [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#slide-layouts-in-markdown)); others are neutral. |
| `[words]{.success}` (`.info`, `.warning`, `.danger`, `.neutral`) | The words bold in that status colour — on every slide. |
| `## Title {layout="Name"}`, `# Title {layout="Name"}` | The slide on the template's layout of that name (case-insensitive), on a brand's own or a caller's template. Plain slides stay plain: text goes into the layout's body placeholder. |
| `# Thank you {.closing}` | The closing slide: on `PptxBrandTemplate.ClosingLayout` / `Layouts.Closing`, else a layout named like *Closing*, *End* or *Thank you*, else the cover's (the section layout on the built-in master). Title and subtitle go into the layout's placeholders if it has them; no footer. |

- **What makes a flow slide:** heading attributes, a `:::` container or an alert anywhere in the
  section, or a tag paragraph as a block of its own. Code-span lists outside containers stay
  bullet lists, code-only table cells on other slides stay code.
- **Containers nest** with longer fences: `:::: panel` around `::: cards` … `:::` and `::::`.
- **Heading attributes** count only as a trailing `{…}` made of `.class` and `key="value"` tokens;
  other braces (`## Sets {a, b}`) stay in the title. `##` headings take `.dark`, `kicker` and
  `layout`; `#` headings take `layout` and `.closing`.
- **Mistakes throw** `DocumentConversionException`: an unknown attribute or a key given twice, an
  unknown container (the message lists `cards, rows, panel, statement, columns`), arguments on a
  container other than `panel dark` and a `columns` ratio, a `columns` without two or three parts,
  `layout="…"` naming no layout of the template (or used on the built-in master), `.closing` on a
  `##` or `.dark` / `kicker` on a `#`, `[words]{.x}` with an unknown colour, content before the first `###` in `::: cards`, a `::: rows` that is not
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
  statements 26 pt, kickers and chips 12 pt. Images, drawings and charts share the height the other
  blocks leave, each at least a quarter of the slide.

### Dark templates

A brand's own template whose slides are dark (`PptxBrandTemplate.Tone`; `Auto` reads it from the
master's title colour, else its background) gets the dark palette on every slide: white text on
translucent veils (white at 10 % for boxes, cards' outlines at 35 %, dark panels black at 35 %),
kickers, figures and `==marked==` text in the highlight colour, callout bars in the highlight and
the warning colour, status labels and highlighted rows as translucent fills, tables with a black
veil on the header and white veils on every other row, chips outlined in white, diagrams with white
text on nodes in a dark shade of the primary colour, charts with white text. Every text keeps a
contrast ratio of at least 4.5 over a dark background. The colours are the brand's
[slide colours](wwosik.DocLibs.DocumentGeneration.md#slide-colours).

## Fitting

Nothing the generator places ends below the content area. Each slide is measured first (text by an
estimate of each character's width, wrapped between words); what does not fit is set smaller:

| Slide | Shrinks | Then continues on further slides |
| ----- | ------- | -------------------------------- |
| Text | The body placeholder gets `normAutofit` with a font scale, 2 pt per step, to 12 pt — PowerPoint keeps it fitted when edited | Between top-level list items (a nested list stays with its item) |
| Table, image or drawing | Text 18 → 12 pt, captions to 10 pt, the table to 10 pt; an image gives up height first, to a quarter of the area | Tables between rows, the header row on each slide; text above on the first, below on the last |
| Flow | Every size by an eighth, twice (16 → 12 pt text), gaps halved | Between blocks; paragraphs, rows of `::: rows`, labelled tags and table rows split, cards, panels, callouts and columns stay whole |

Continuation slides keep the title and kicker; `PptxOptions.ContinuationSuffix` (e.g. `" (cont.)"`)
marks them. A single block too big for a slide of its own is shrunk and left as it is. Each of these
is reported in the `GenerationResult`: `slide-split`, `slide-shrunk` (only at the smallest size) and
`slide-overfull`.

## Templates

`PptxOptions.Template` takes one options record per template; each has only the features of its
template.

| Options | Template | Features |
| ------- | -------- | -------- |
| `BrandTemplateOptions` (default) | The built-in master in `Brand`'s design — by default `Brand.Neutral`: white, Arial, dark titles, blue accents, no images, no footer text | `Brand`, `ShowDate` (default on), `ShowTagline` (default on), `EmbedFonts` (default on), `ShowFooter` (default on; brand templates) |
| `CustomTemplateOptions(stream)` | A caller's `.pptx` or `.potx` | `Layouts` (layout names) |

Every template takes `PptxOptions.ListBullets`: `Template` (default) leaves the bullets of text slides
to the template's styles, `Always` writes Markdown's bullets on every level — for templates whose
level-1 style has none. Every template also takes `PptxOptions.Transition` and `PptxOptions.Build`,
see [Presenting](#presenting).

**Notes master.** A deck with [speaker notes](#presenting) uses the template's notes master as it
is. A template without one — the built-in master, a brand's own template or a caller's template —
gets a built-in notes master (`NotesMasterBuilder`), added after the slides with the next free
relationship id and listed in `p:notesMasterIdLst`: on the portrait notes page (6858000 × 9144000),
header and date at the top, the slide image in the deck's proportions, the speaker notes in 12 pt
below it (each level indented by half an inch, no bullets of its own), footer and slide number at
the foot. Its theme is a copy of the deck's theme, so on the built-in master it has the brand's
font and colours, on other templates their theme fonts.

Brands — colours, fonts, images, brand packs, and how front matter picks one from
`PptxOptions.Brands` — are described in
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#brands). A brand with
its own `Pptx` template is built on that template, exactly like `CustomTemplateOptions` with the
brand's layout names; such decks never get a date and take no `showDate` / `showTagline`, but
they get the brand's footer text, its tone and its closing layout (`PptxBrandTemplate.ClosingLayout`).

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
| `showFooter` | `true`, `false` | brands with their own `Pptx` template |
| `listBullets` | `template`, `always` | all |
| `transition` | `none`, `fade`, `push`, `wipe`, `split`, `cover`, `uncover`, `zoom`, `cut`, `morph` | all — see [Presenting](#presenting) |
| `build` | `none`, `lists` | all — see [Presenting](#presenting) |
| `layouts.title`, `layouts.content`, `layouts.titleOnly`, `layouts.sectionHeader`, `layouts.closing` | layout name | custom |
| `title`, `author` | text | all — the core properties |
| `lang` | a language tag such as `en`, `de`, `de-CH`; default `PptxOptions.Language` (`en`), with `PptxOptions.Texts` for further languages | all — see [Document language](wwosik.DocLibs.DocumentGeneration.md#document-language) |

- `template` naming the template the caller chose keeps the caller's values; naming another one
  starts from that template's defaults. The other keys then apply, whatever their order.
- An unknown key, a key the resulting template does not support, an invalid value, a duplicate
  key, a line that is not `key: value`, or a block that is never closed throws, naming the key and
  listing the keys valid for that template.

## Diagrams and charts

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
- a **`ChartGraphic`**, written as a **native PowerPoint chart** (a chart part with its data in an
  embedded workbook, so *Edit Data* works): clustered or stacked columns, lines with markers,
  overlapping or stacked areas; series in the chart's colours, text in the brand's table font, the
  value axis from zero for bars and areas, gridlines light grey (on dark slides white at 30 %). It
  fills the width it has, at most 5:8 tall. `ChartRenderer` from
  [wwosik.DocLibs.DocumentGeneration.Charts](wwosik.DocLibs.DocumentGeneration.Charts.md) makes
  them from `chart` blocks, and drawings from `financial-chart` blocks: the financial charts as
  editable shapes, drawn in the slide's brand (dark slides get a dark chart).

Drawings are enlarged at most by half, so that their text keeps a sensible size.

## Known limits

- Text heights are estimated from character widths, not measured with the font, so fitting is
  generous but not exact: a font much wider than the estimate can still overflow. Very long cover
  or divider titles are not fitted.
- The tone of a brand template is read from its colours; a photographic background is not looked
  at. A template whose title colour does not tell needs `PptxBrandTemplate.Tone`. Caller templates
  are taken as light.
- On light slides, text in text boxes takes the theme's text colour, not the layout's: a subtitle
  placed in a text box on a dark caller layout stays dark.
- The footer text sits in a box of fixed width without wrapping; a long one runs into the date.
- A caller template's own body style is used as it is; one made for hand-written slides (level 1
  bold, no bullets on levels 1–2) suits Markdown less well than the built-in template.
- No merged table cells. SVG images only through an image converter. Native
  charts are bar, line and area only; the financial charts are shapes, not charts PowerPoint can edit
  the data of.
- Embedded fonts are a PowerPoint feature. Viewers that ignore them — LibreOffice without
  libeot, Google Slides — substitute a font of their own, as for a deck without embedded fonts.
  PowerPoint Online honours them only partly.
- Relative links (`[x](other.md)`, `#anchor`) stay plain text.
