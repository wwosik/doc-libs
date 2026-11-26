# wwosik.DocLibs.DocumentGeneration

What the generators [wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md),
[wwosik.DocLibs.DocumentGeneration.Docx](wwosik.DocLibs.DocumentGeneration.Docx.md)
and [wwosik.DocLibs.DocumentGeneration.Pdf](wwosik.DocLibs.DocumentGeneration.Pdf.md) share: the
public **brand** API (`Brand`, `BrandPack`), the **drawing** model and the code-block renderer
interface, [QR codes and icons](#qr-codes-and-icons) (`IIconSet`, `TablerIcons`),
[code highlighting](#code-highlighting) (`CodeTokenKind`, `BrandCodeColors`),
[checks](#checking-a-document), [a tool for AI agents](#a-tool-for-ai-agents) (`DocumentTool`,
`IMarkdownGenerator`), and internally front matter and image headers. It comes along as a dependency of either generator. Depends on
[wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md), Markdig, QRCoder and
ColorCode.Core; AOT-compatible. Third-party licences (QRCoder, Tabler Icons, ColorCode) are in the package's
`THIRD-PARTY-NOTICES.txt`.

## Brands

A brand is a client's or project's design: colours, fonts, footer text, images, and optionally its
own PowerPoint and Word templates. Every generator draws its one built-in design in it —
`BrandTemplateOptions` for PPTX, `BrandDocxTemplateOptions` for DOCX, `BrandPdfTemplateOptions` for
PDF — so a single `Brand` styles decks, Word documents and PDFs alike. Brands live in the client's code or package, never in this repo.

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
| `Colors.Code` | [Highlighted code](#code-highlighting): `Keyword`, `String`, `Comment`, `Number`, `Type`, `Markup`; colour only. A colour set here is used as it is | `Primary`, `Labels.Success`, `Footer`, `Labels.Warning`, `Diagram.Accent`, `Link`, each darkened (on dark slides lightened) to a 4.5:1 contrast on the code background |
| `Colors.Theme` | The 12 PowerPoint theme colours (dark 1, light 1, dark 2, light 2, accents 1–6, hyperlink, followed hyperlink) | black, white, `Primary`, `TableBand`, `Primary`, five neutral accents, `Link`, violet |
| `Fonts` | PDF text (embedded as subsets); the PPTX theme font family and code (embedded whole, see [Fonts](#fonts)) | Liberation Sans and Mono in PDFs, Arial and Consolas in decks |
| `Fonts.Table` | Table cells, typically a condensed cut (see [Table fonts](#table-fonts)) | the text faces |
| `FooterText` | Footer of every page and slide but the cover | none |
| `Logo` | Cover top right; PDF page header right; slide footer right | none |
| `LogoOnDark` | PPTX section dividers (on the gradient); dark PDFs, in its place (`Tone = Dark`) | none (dark PDFs: `Logo`) |
| `CoverArtwork` | Cover bottom right: PDF — page width, ≤ 42 % of the height; PPTX — slide width, below the subtitle | none |
| `Tagline` | Cover bottom right, over the artwork; `ShowTagline` turns it off | none |
| `CoverImage` | An image on the cover of decks, PDFs and Word documents, such as a product mark; front matter `coverImage` replaces it (see [Cover image](#cover-image)) | none |
| `Docx` | A `.dotx` / `.docx` Word documents are built on instead of the built-in template (a `DocxBrandTemplate`, compared by content), with `Styles`, a `DocxStyleNames` naming the template's style for each role (see the [DOCX docs](wwosik.DocLibs.DocumentGeneration.Docx.md#templates)) | none |
| `Pptx` | A `.pptx` / `.potx` decks are built on instead of the built-in master (see the PPTX docs); with `ClosingLayout` (the closing slide's layout by name) and `Tone` (`Auto`, `Light`, `Dark`: whether its slides are dark) | none |
| `SlideColors` | Colours of decks and dark PDFs where they differ from light PDFs (see [Slide colours](#slide-colours)) | `Colors` |

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
dark. Light PDFs never read them; a dark PDF (`BrandPdfTemplateOptions.Tone = Dark`, see the
[PDF docs](wwosik.DocLibs.DocumentGeneration.Pdf.md#dark-pdfs)) takes its colours from them. Decks use them everywhere: the built-in master, flow-slide colours,
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

`PptxOptions.Brands`, `DocxOptions.Brands` and `PdfOptions.Brands` list the brands the Markdown may pick with
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
  "images": { "logo": "logo.png", "logoOnDark": "logo-on-dark.png", "coverArtwork": "cover.png", "tagline": "tagline.png", "coverImage": "mark.png" },
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
    "layouts": { "title": "Title Slide", "content": "Title and Content", "titleOnly": "Title Only", "sectionHeader": "Section Header", "closing": "Closing",
                 "twoContent": "Two Content", "agenda": "Agenda", "quote": "Quote", "dark": "Title Only Dark" },
    "placeholders": { "Quote": { "Text Placeholder 3": "byline", "Picture Placeholder 5": "decorative" } },
    "textLevels": { "heading": 1, "paragraph": 2, "bullet": 3 },
    "footer": "{footer} | {chapter}",
    "contentArea": "guides",
    "numberChapters": true
  },
  "docx": {
    "template": "template.dotx",
    "styles": { "heading1": "Chapter", "normal": "Body Text" }
  }
}
```

Every key but `name` and `colors.primary` is optional (inside `fonts`, the four text faces are
required, and inside `fonts.table` its `regular` and `bold`); `colors` also takes `footer`, `theme` (12 colours), `chart` (the roles of `BrandChartColors` in camel case, such as `"chart": { "series": ["#0B5FFF", "#3FC1FF"], "ahead": "#16A34A" }`) and `code` (`keyword`, `string`, `comment`, `number`, `type`, `markup`, such as `"code": { "keyword": "#7C3AED" }`), as in the table above. `slideColors` takes the same keys as `colors`, all optional; `pptx.tone` is `auto` (the default), `light` or `dark`. `//` comments and trailing commas are allowed.

The rest of `pptx` holds the template's roles (`PptxBrandTemplate.Roles`, a `PptxTemplateRoles`;
what they do is in
[wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md#template-roles)):
`layouts` also takes the layout roles `twoContent`, `textAndPicture`, `picture`, `agenda`,
`quote`, `statement`, `contact`, `chart`, `twoCharts` and `dark`; `placeholders` maps a layout's
name to its placeholders' shape names and their roles: `subtitle`, `byline`, `chapterNumber`,
`body`, `picture`, `chartTitle`, `chartUnit`, `chartSource` or `decorative` — `body1`, `body2`, …
give several bodies their order, after any plain `body`; `textLevels` needs all three of `heading`,
`paragraph` and `bullet` (1–9); `footer` is the footer pattern; `contentArea` is `placeholders`
(the default) or `guides`; `numberChapters` (`true` or `false`, the default) numbers the section
dividers.

`docx` holds the brand's Word template (`Brand.Docx`): `template` is required, and `styles` names
the template's style for each role, by the property names of `DocxStyleNames` in camel case —
`title`, `subtitle`, `coverDetails`, `tocHeading`, `heading1` to `heading6`, `normal`, `listBullet`,
`listNumber`, `listTask`, `definitionTerm`, `definition`, `quote`, `code`, `codeCharacter`, `rule`,
`caption`, `tableTitle`, `table`, `footnoteText`, `footnoteReference` and `hyperlink`. A role
left out takes its default style name; a misspelt role is an unknown key.

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

## Generation results and notes

`PptxGenerator.Generate`, `DocxGenerator.Generate` and `PdfGenerator.Generate` return a
`GenerationResult`: the slides or pages written, and notes about what the generator did to the Markdown that its writer cannot see in
the result. The document is written either way; a note is not an error.

```csharp
GenerationResult result = PptxGenerator.Generate(document, output, options);
if (result.HasNotes)
{
    Console.WriteLine(result);   // "- line 12: The slide 'Costs' did not fit and continues on 1 more slide. …"
}
```

- **`GenerationResult(Notes)`**: `Pages` (slides for decks, pages for PDFs, 0 for Word documents,
  whose pages Word lays out itself), `SlideLines` (for a deck, the Markdown line each slide starts
  at, counted like a note's `Line`; `null` for PDFs and Word documents), `HasNotes`, and

- **`GenerationResult(Notes)`**: `Pages` (slides for decks, pages for PDFs, sheets for workbooks, 0 for Word documents,
  whose pages Word lays out itself), `HasNotes`, and
  `ToString()`, one `- line N: …` per note or `No notes.`.
- **`GenerationNote(Code, Line, Message)`**: `Code` is one of `NoteCodes`, stable, for filtering;
  `Line` is the Markdown line it concerns, from 1, front matter included, 0 if unknown; `Message`
  is English, names the slide or block, and says what to change. Notes are ordered by line, those
  without one last, and a note repeated word for word is given once.
- **Pass them back** to whoever wrote the Markdown, as you pass `DocumentConversionException.Reason`.
  Each generator's authoring guide lists its codes with what to change, under *Notes you may get
  back*. To refuse a document with notes, check `HasNotes`; there is no option that throws.

| Code | Format | When |
| ---- | ------ | ---- |
| `slide-split` | PPTX | A slide did not fit and continues on further slides with the same title |
| `slide-shrunk` | PPTX | A slide's text or table had to be set at the smallest size to fit; smaller steps above it are not reported, nor are the parts of a split slide |
| `slide-overfull` | PPTX | Something runs past the content area even at the smallest size: one list item, table row or block too big for a slide of its own |
| `image-enlarged` | PPTX | An image was drawn at more than twice its natural size; not when its title gives `scale=`, nor for a converted SVG, which PowerPoint draws as a vector |
| `layout-fallback` | PPTX | A slide kind (`{.agenda}`, `{.quote}`, `{.statement}`, `{.contact}`, `{.chart}`) asked for a layout the template does not have; it is drawn as a flow slide with the same content |
| `agenda-empty` | PPTX, PDF | An empty `{.agenda}` found no `#` chapters to list, so it shows its title only |
| `image-cropped` | PPTX | An image filling a layout's picture placeholder (`layout="…"`) was cut by more than a tenth of its width or height to the placeholder's shape |
| `code-not-drawn` | PPTX, DOCX, PDF | A renderer declined a block in its own language and said why, such as `MermaidRenderer` for a diagram type it does not draw; blocks in languages no renderer claims stay code silently. In Word documents also a drawing (a diagram block, a `qr:` image, a drawn container) when no image converter is registered: it stays code, alt text or content, and the message says to register `SvgImageConverter` |
| `html-as-text` | PPTX, DOCX, PDF | Raw HTML, a block or inline, was written as text; HTML comments are left out silently |
| `character-replaced` | PDF | `PdfOptions.ReplaceUnsupported` replaced a character the fonts lack; once per character, without a line |
| `footnote-missing` | PPTX, DOCX, PDF | A footnote reference such as `[^1]` has no definition, so it is shown as written |
| `footnote-split` | PDF | A footnote was too long for the foot of its page and continues at the foot of the next; with the line of its definition |
| `language-unknown` | PPTX, DOCX, PDF | The document language has no `GeneratedTexts` of its own, built in or from the options; the texts are English, the tag is kept; without a line |
| `code-language-unknown` | PPTX, DOCX, PDF | With [highlighting](#code-highlighting) on, a fenced code block names a language that neither a renderer nor the highlighter knows, so it is not highlighted; not for `text`, `plain`, `console`, `output` |
| `typography-skipped` | PPTX, DOCX, PDF | With [typography](#typography) on, a typographic character (a quote, dash, `…` or the no-break space) was not used because a face the text can be set in lacks it; the character stays as typed. Once per character, with the line of its first paragraph |
| `notes-dropped` | PPTX | A `::: notes` stands before the first heading, so it belongs to no slide and was left out |
| `notes-simplified` | PPTX | Speaker notes held a table, code block, image, quote, alert, heading or other container, written as plain text; one note per block |
| `attribute-ignored` | PPTX, DOCX, PDF | An image attribute has no effect where it is: `fit=cover` on an image of its own in a PDF, `focus` on an image that is not cropped, `scale` with `fit=cover`, `fit`, `focus`, `.round` and `.grey` in a Word document, or any attribute on an image shown as its alt text (in a heading or a table cell); or a heading attribute of another format: `{.backup}` in a PDF or Word document, `{.appendix}` in a deck |

| `attribute-ignored` | PPTX, DOCX, PDF | An image attribute has no effect where it is: `fit=cover` on an image of its own in a PDF or Word document, `focus` on an image that is not cropped, `scale` with `fit=cover`, `.grey` on an SVG in a Word document (only its grey PNG goes in), or any attribute on an image shown as its alt text (in a heading or a table cell) |
| `icon-not-drawn` | PPTX, PDF, DOCX | An icon (`:icon-name:`) stands where the format cannot draw it and was left out of the text: in decks anywhere but a card's figure or an icon list (inside text, a heading, a table cell, speaker notes, a list where not every item starts with an icon); in PDFs in a heading, a table cell or on the cover; in Word documents anywhere when no image converter is registered |
| `caption-ignored` | PPTX, DOCX, PDF | A `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text, prefix included |
| `toc-empty` | PDF | `toc: true` (or `PdfOptions.TableOfContents`) asked for a table of contents, but no heading is at the depth it lists, so the PDF has none; the line is the `toc` key's, 0 when the option asked |
| `list-too-deep` | DOCX | A list is nested deeper than three levels; the deeper items are set at the third level, once per list |
| `classification-kept` | PPTX, PDF, DOCX | The front matter's `classification` is lower than the host's (`PptxOptions`, `PdfOptions` or `DocxOptions.Classification`); the host's applies. The PDF and deck guides leave it out of their notes list: their front matter table already says when lower values have no effect |
| `audience-omitted` | PPTX, PDF, DOCX | The version generated left out parts marked for other versions (see [Audience versions](#audience-versions)): `internal` parts in the customer's version, solutions in all but the trainer's. Once per document, with the count and the lines; its line is the first of them. The deck guide leaves it out of its notes list |
| `too-many-bullets` | PPTX | Check only: a `#` or `##` section has more list items, all levels, than `CheckOptions.MaxBulletsPerSlide` |
| `too-many-words` | PPTX | Check only: a `#` or `##` section has more words than `CheckOptions.MaxWordsPerSlide`; its title, speaker notes, tables, code and drawn containers are not counted |
| `long-paragraph` | PDF, DOCX | Check only: a paragraph (not a table cell) has more words than `CheckOptions.MaxWordsPerParagraph` |
| `title-too-long` | PPTX, PDF, DOCX | Check only: a slide title (`#`, `##`) or any heading of a PDF or Word document has more text elements than `CheckOptions.MaxTitleLength`, the settings in braces at its end not counted |
| `alt-text-missing` | PPTX, PDF, DOCX | Check only: an image has empty alt text (`![](a.png)`) and `CheckOptions.RequireAltText` is on; `qr:` codes too |
| `image-source` | PPTX, PDF, DOCX | Check only: an image is not among the document's resources, or is an `http:`, `https:` or `data:` URL, which the generators never load. `Generate` throws for the first one instead |
| `drawing-unsupported` | HTML deck | `HtmlDeck.Write` left something of a slide out: an effect, a preset shape it does not know (drawn as a rectangle), a chart kind, an EMF picture and the like; one note per kind with the slide numbers, without a line |
| `cover-image-ignored` | PPTX, PDF, DOCX | Front matter `coverImage` names an image that is not drawn: the document has no cover (no leading `#`, a plain PDF, a deck without a title slide), or the cover has no room left for it (see [Cover image](#cover-image)) |
| `font-missing` | HTML deck | `HtmlDeck.Write` measured a font with a stand-in of other widths (Consolas with Liberation Mono), by estimate (a family with neither a face nor a stand-in), or a missing bold or italic face with the regular one; one note per family, without a line |
| `cell-type` | XLSX | A workbook value does not fit its column's type, such as text in a number column; see [the workbook generator](wwosik.DocLibs.DocumentGeneration.Xlsx.md#check) |
| `sheet-name` | XLSX | A sheet name breaks Excel's rules: empty, longer than 31 characters, with one of `[]:*?/\`, or the same as another ignoring case |
| `column-invalid` | XLSX | A sheet has no columns, or a column has no header, invalid decimals, a misplaced or unknown currency, or a width outside 1–255 |
| `row-too-wide` | XLSX | A row has more cells than its sheet has columns |
| `excel-limit` | XLSX | Excel cannot hold it: too many rows or columns, a text over 32,767 characters, a date before 1900, or no sheet at all |
| `character-missing` | XLSX | A character the brand's fonts lack, which Excel shows in a substitute font; once per character, without a line |
| `formula-invalid` | XLSX | A formula does not parse, names a `[Header]` the sheet does not have or two columns share, holds a bare name, refers to itself within its row, or gives a function the wrong number of arguments; with the character it happens at, see [the workbook generator](wwosik.DocLibs.DocumentGeneration.Xlsx.md#formulas) |
| `function-unknown` | XLSX | A formula calls a function the generator does not know; it is written as it is, for Excel or an add-in to know. Once per function, without a line |
| `chart-invalid` | XLSX | A workbook chart names a column the sheet does not have, takes a series from a column that is not numeric, stands on a sheet without rows, has no series, or names a chart sheet that is invalid or taken; see [the workbook generator](wwosik.DocLibs.DocumentGeneration.Xlsx.md#charts) |
| `chart-colors-repeat` | XLSX | A warning: a workbook chart has more series than the brand's diagram palette has colours, so colours repeat; without a line |

## Checking a document

`PptxGenerator.Check`, `PdfGenerator.Check` and `DocxGenerator.Check` take the same document and
options as `Generate`, write nothing, and return a `CheckResult`: everything at once, where
`Generate` stops at the first error.

```csharp
CheckResult check = PptxGenerator.Check(document, options);
if (!check.IsClean)
{
    Console.WriteLine(check);
    // - line 7: The slide 'Agenda' has 7 bullets; keep it to 6.
    // 9 slides, 85 words, 3 images, 1 tables, 0 diagrams; cover 1, section 1, text 3, visual 4; talk time 4 min.
}
```

- **`CheckResult(Notes, Statistics)`**: `Notes` are the notes `Generate` would return (the same
  codes, lines and messages) together with the notes of the check's own rules, ordered by line.
  `Error` is the `Reason` of the `DocumentConversionException` `Generate` would throw, such as an
  unknown front matter key, and `null` when the document can be generated; with an error, the notes
  and statistics are those found before it. `IsClean`: no error and no notes. `ToString()` lists the
  notes like `GenerationResult`, then `Error: …`, then one line of statistics.
- **Not thrown:** document errors. An invalid brand or `CheckOptions` still throws
  `ArgumentException`, as in `Generate`.
- **Images first.** Before generating, every image reference is checked against the document's
  resources; each missing one, and each `http:`, `https:` or `data:` URL, gives `image-source` with
  its line. Such images are replaced by their alt text for the rest of the check, so three missing
  images give three notes and the check goes on.
- **What runs.** A deck is generated in full into nothing, so fitting reports `slide-split` as
  `Generate` does; a PDF is laid out but not rendered (no font subsetting, no PDF/A); a Word
  document is generated in full into nothing.

### Limits and rules

The limits are `CheckOptions`, set on the generator options as `PptxOptions.Checks`,
`PdfOptions.Checks` and `DocxOptions.Checks` (default `CheckOptions.Default`), so the check, the
authoring guide (section *Limits*) and an AI tool see the same ones. `null` switches a rule off.
`Generate` never applies them.

| Option | Default | Rule | Formats |
| ------ | ------- | ---- | ------- |
| `MaxBulletsPerSlide` | 6 | `too-many-bullets` | PPTX |
| `MaxWordsPerSlide` | 80 | `too-many-words` | PPTX |
| `MaxTitleLength` | 60 | `title-too-long` | all |
| `MaxWordsPerParagraph` | 150 | `long-paragraph` | PDF, DOCX |
| `RequireAltText` | `true` | `alt-text-missing` | all |
| `WordsPerMinute` | 130 | talk time | PPTX |
| `MinTimePerSlide` | 30 s | talk time | PPTX |

- **Per source section.** The deck rules count per `#` or `##` section of the Markdown, at its
  heading's line: a section that fitting split counts once (`slide-split` says it was split), and
  a section that becomes a text slide and a table slide counts once too. Speaker notes are never
  counted, nor checked.
- **Words, in any language:** runs of letters, digits and combining marks between whitespace or
  punctuation, with `'`, `’` or `-` inside a word (`end-to-end` is one word). A code span counts as
  one word; code blocks, drawn containers and alt text do not count, `:icon-name:` and the
  `{.success}` of coloured words neither. Han, Hiragana, Katakana and Thai, written without spaces,
  count one word for every two characters. Nothing language-specific (stop words, spelling) is
  checked.
- **Title length** is in text elements, what a reader sees as one character (`é` written as `e` and
  a combining accent is one).

### Statistics

`CheckStatistics`: `Pages` (slides of a deck, pages of a PDF, 0 for Word documents and after an
error), `Words` (all text: tables, headings and footnotes included, speaker notes not), `Images`
(each image of a row, not `qr:` codes), `Tables`, `Diagrams` (code blocks a renderer drew, and the
drawn containers such as `::: timeline`), `Sheets` (workbooks only: the sheets, the info sheet
included; also first in `ToString()`), and for decks:

- **`SlidesByKind`**, over the written slides, split parts included, in the order of
  `CheckStatistics.SlideKinds`, kinds without a slide left out: `cover`, `section`, `text` (a text
  slide), `visual` (a table, image or diagram on a slide of its own), `layout` (a flow slide, or a
  `layout="…"` slide that fills placeholders), `agenda`, `quote`, `statement`, `contact`, `chart`
  (the slide kinds; one drawn as a flow slide because the template lacks its layout counts as
  `layout`), `scene` (a `::: scene`), `closing`, and `backup` (every slide of the deck's backup, `{.backup}`,
  whatever its kind). The names are stable, like note codes. `Words` leaves a deck's backup out.
- **`TalkTime`**, rounded to whole minutes: each content slide its speaker notes' words, or else its
  own words without the title, at `WordsPerMinute`, and at least `MinTimePerSlide`; the parts of a
  split slide share their notes, which count on the first part; cover, section and closing slides
  10 seconds each; backup slides none.

## A tool for AI agents

A host that lets an AI agent write documents gets a ready tool from `DocumentTool`: a name, a
description, the JSON Schema of the parameters, a combined authoring guide for the formats it
offers, and `Run`, which takes the agent's arguments and returns a reply in a fixed JSON shape. The
library supplies the parts, not a vendor's envelope, so it ties the host to no model API. Everything
is written with System.Text.Json's `Utf8JsonWriter` and read with `JsonDocument`, without
reflection, so the tool is trimming- and AOT-safe.

```csharp
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Pdf;
using wwosik.DocLibs.DocumentGeneration.Pptx;

var tool = new DocumentTool([PptxGenerator.ForOptions(pptxOptions), PdfGenerator.ForOptions(pdfOptions)]);

// The guide goes where the model reads instructions, e.g. the system prompt, under what only you know.
string system = tool.Guide + "\n\n## Images\n\nReference the images listed below by their path.\n";

// One vendor's tool shape, e.g. an entry of a Messages API's "tools" array:
var definition = new JsonObject
{
    ["name"] = tool.Name,
    ["description"] = tool.Description,
    ["input_schema"] = JsonNode.Parse(tool.ParametersSchema),
};

// When the model calls the tool with its arguments (the JSON object it sent):
using var buffer = new MemoryStream();
DocumentToolRun run = tool.Run(arguments, images, buffer);
if (run.Succeeded)
{
    Save(buffer.ToArray(), "document" + run.Generator!.FileExtension, run.Generator.ContentType);
}

return run.Reply;   // the tool result, as it is
```

- **The generators.** `PptxGenerator.ForOptions(options)`, `PdfGenerator.ForOptions(options)` and
  `DocxGenerator.ForOptions(options)` return an `IMarkdownGenerator`: the generator bound to its
  options, with `Format` (`pptx`, `pdf`, `docx`), `Title` (`PowerPoint deck`, `PDF document`,
  `Word document`), `FileExtension`, `ContentType`, `AuthoringGuide()`, `Generate(document,
  output)` and `Check(document)`. The options are checked when one of the methods runs. The static
  methods stay as they were. `ForAudience(audience)` returns the generator for that version, unless
  its options already set one; its default implementation returns the generator itself, so a host's
  own `IMarkdownGenerator` keeps compiling.
- **No generator in the shared package.** `DocumentTool` knows only `IMarkdownGenerator`, so a host
  pays for the formats it offers: one that offers only PDFs references no Open XML SDK.
- **`new DocumentTool(generators, options)`** offers the generators in their order. It throws
  `ArgumentException` for no generator, a `null` one, two with the same `Format`, a name that does
  not match `^[a-zA-Z0-9_-]{1,64}$`, or a blank description. `DocumentToolOptions`: `Name` (default
  `create_document`), `Description` (replaces the generated one), `OfferCheck` (default `true`: the
  `mode` parameter), `Preview` (default none: a [contact sheet](#contact-sheet) of each deck).
- **`Description`** is one paragraph naming the formats, how to call the tool, what it returns,
  and the guide by its title.
- **`Guide`** is Markdown under one `#` heading, *Writing documents with create_document* (with
  the tool's name): how to call the tool and what comes back, then every offered generator's full
  `AuthoringGuide()` under `## <Title> (format: "<format>")`, its headings moved one level down
  (headings inside code blocks are left as they are). It is built on first use.

### Parameters

`ParametersSchema` is indented JSON, an object with `"additionalProperties": false`, using only
`type`, `properties`, `enum`, `description`, `required` and `default`, which the major tool APIs
all accept:

| Parameter | Type | Meaning |
| --------- | ---- | ------- |
| `format` | string, `enum` of the offered formats | The file to create. Required when more than one format is offered; with one it may be left out and defaults to it. |
| `markdown` | string, required | The whole document, front matter included. |
| `audience` | `"customer"` (default), `"internal"` or `"trainer"` | The version (see [Audience versions](#audience-versions)), through `IMarkdownGenerator.ForAudience`. Wins over the front matter's `audience`, not over a version the generator's options set. Optional. |
| `mode` | `"generate"` (default) or `"check"` | Only with `OfferCheck`. `check` calls `Check` and writes nothing. |

### Replies

`Run(arguments, resources, output)` returns a `DocumentToolRun`: `Reply` (the JSON for the agent),
`Succeeded` (a file was written to `output`), `Generator` (the format picked, for the file's name and
type; `null` when the arguments were not a valid call), `Result` (the `GenerationResult`) and
`Check` (the `CheckResult` in check mode). The reply is indented JSON in a fixed property order,
camelCase:

```json
{
  "status": "ok",
  "format": "pptx",
  "pages": 9,
  "notes": [
    { "code": "slide-split", "line": 12, "message": "…" }
  ]
}
```

```json
{
  "status": "checked",
  "format": "pptx",
  "pages": 9,
  "notes": [ { "code": "too-many-bullets", "line": 7, "message": "…" } ],
  "statistics": {
    "words": 85, "images": 3, "tables": 1, "diagrams": 0,
    "slidesByKind": { "cover": 1, "section": 1, "text": 3, "visual": 4 },
    "talkTimeMinutes": 4
  }
}
```

```json
{
  "status": "error",
  "error": "The image 'chart.png' is not among the document's resources."
}
```

- **`ok`**: `pages` is `GenerationResult.Pages` (0 for Word documents), `notes` its notes, each with
  `code`, `line` (0 when unknown) and `message`.
- **`checked`**: `pages` and `notes` of the `CheckResult`, and `statistics`; `slidesByKind` (in the
  order of `CheckStatistics.SlideKinds`) and `talkTimeMinutes` only for decks whose check got as far
  as the slides. A document error adds `"error"` after `format`; the status stays `checked`.
- **`error`**: only `status` and `error`.
- **Stable.** The statuses and keys are a public contract; a change to the shape is a breaking change
  in the changelog. Non-ASCII letters are written as they are, not as `\u` escapes.

### Contact sheet

An agent that sees images can look at the deck it wrote. Set `DocumentToolOptions.Preview` to an
`IDeckPreview`, normally `new DeckContactSheet()` from
[wwosik.DocLibs.DocumentConversions.Svg](wwosik.DocLibs.DocumentConversions.Svg.md#contact-sheets-for-documenttool),
which paints the slides with SkiaSharp: no browser, no network, and the same deck gives the same
PNG. This package knows no painter; the hook keeps it free of SkiaSharp.

```csharp
var tool = new DocumentTool(
    [PptxGenerator.ForOptions(pptxOptions), PdfGenerator.ForOptions(pdfOptions)],
    new DocumentToolOptions { Preview = new DeckContactSheet() });

DocumentToolRun run = tool.Run(arguments, images, buffer);
// run.ContactSheet: the PNG, when a deck was written; run.Reply then has "contactSheet": "attached".
```

- **Decks only.** When a deck is written, the tool generates it into a buffer, copies it to
  `output` unchanged, and passes it with its `GenerationResult` to
  `IDeckPreview.ContactSheet(pptx, result)`. PDFs, Word documents and checks get no sheet.
- **The reply** adds `"contactSheet": "attached"` after `notes`; for a deck of more than
  `IDeckPreview.MaxSlides` (60) slides, the sheet shows the first 60 and the reply adds
  `"contactSheetNote": "The contact sheet shows slides 1 to 60 of 75."`. A deck the painter cannot
  paint (`DocumentConversionException`) is still written, and the reply has neither key.
- **The guide** gets a section *Looking at the slides*, after *What comes back*, that tells the agent
  what the image is and what to look for. Without `Preview`, replies and guide are exactly as before.
- **Pass the image with the reply**, in the same tool result. With Anthropic's Messages API, the
  `tool_result` block takes a content array:

  ```json
  { "type": "tool_result", "tool_use_id": "…", "content": [
      { "type": "text", "text": "<run.Reply>" },
      { "type": "image", "source": { "type": "base64", "media_type": "image/png", "data": "<base64 of run.ContactSheet>" } }
  ] }
  ```

  With OpenAI's Responses API, a `function_call_output` takes a list as its `output`:
  `[{ "type": "input_text", "text": "<run.Reply>" }, { "type": "input_image", "image_url":
  "data:image/png;base64,…" }]`. Where a tool result can hold text only (Chat Completions' `tool`
  message), send the reply as the tool message and the image in the next `user` message, as an
  `image_url` part with that data URL.
- **Size.** Four thumbnails of 320 pixels to a row give a sheet 1 360 pixels wide, about 200 pixels
  per row of 16:9 slides; 60 slides make it about 3 000 pixels tall. APIs scale large images down,
  so for long decks consider fewer slides per call, or `ContactSheetOptions` with more columns.

### Errors

- **The agent's mistakes are replies, not exceptions,** so it can fix its call: arguments that are
  not JSON or not an object, an unknown property, a property that is not a string, the same property
  twice, a missing or unknown `format`, an unknown `mode`, a missing or blank `markdown` each give an
  `error` reply naming the problem and the valid values. A `null` value counts as not given. `format`
  and `mode` are matched ignoring case.
- **A document error** (`DocumentConversionException`) becomes an `error` reply with its `Reason`,
  and `Succeeded` is `false`. `output` may hold partial bytes then: generate into a buffer, as above,
  and keep it only on success.
- **Host errors throw:** an `ArgumentException` (invalid options, brand or checks) and anything else
  that is not a document error, and `ArgumentNullException` for `null` arguments.
- **The document** is built from `markdown` (line endings made `\n`), the `resources` you pass (the
  images you gave the agent) and no metadata: title and author come from the front matter.
- **Keep the reply as the tool result.** It is written for the agent: the notes and errors say what
  to change, and the guide explains each code.

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
  included). `Texts` are the `GeneratedTexts` of the document language (default
  `GeneratedTexts.English`), for words a renderer writes itself, such as the "Source" under a
  chart; they are part of the request's equality. `Report(code, message)`, set by the generators, passes a note for the writer into
  their `GenerationResult`: a renderer that declines a block in its own language calls it with
  `NoteCodes.CodeNotDrawn` and the reason before it returns `null`. It may be `null` when a request
  is made by hand, and it is not part of the request's equality.
- **`AuthoringGuide`** is what a Markdown writer needs to know about the blocks the renderer
  takes, as Markdown under a `##` heading of its own. The generators' `AuthoringGuide(options)`
  includes the sections of the renderers in the options; a renderer without one adds nothing.
- **A renderer that throws** fails the generation with a `DocumentConversionException` in the
  generator's format, with the same message and the renderer's exception as `InnerException`, so
  every problem in a document has one exception type. A `DocumentConversionException` from a
  renderer is passed on as it is.
- **`CodeBlockGraphic`** is a `DrawingGraphic(Drawing)`, an `ImageGraphic(DocumentResource)` or a
  `ChartGraphic(ChartSpec, Drawing)`: a bar, line or area chart (`ChartSpec`: `Kind`, `Categories`,
  `Series` of `ChartSeries(Name, Values)`, `Stacked`, `Title`, `Unit`, `Source`, `Colors`) that decks write as
  a native chart and PDFs draw as its `Drawing`. The renderers are asked in order and the first
  graphic wins.
- **`Drawing(Width, Height, Items)`** is a vector drawing in CSS pixels (0.75 pt each) from the top
  left, with colours as `RRGGBB`. Records compare by content.
  - `DrawingShape(Path, Fill, Stroke, StrokeWidth)` has `Dash`, `RoundJoins`, and `Kind`
    (`Path`, `Rectangle`, `RoundedRectangle` with `CornerRadius`, `Ellipse`). Decks use PowerPoint's
    own shapes for the kinds; `Path` always holds the outline. A `DrawingPath` is a list of
    `MoveTo`, `LineTo`, `CubicTo` and `ClosePath`.
  - `FillRule` decides what is inside a path whose subpaths overlap: `NonZero` (the default, as in
    SVG) or `EvenOdd`, where an overlap is a hole, as for a ring drawn as two circles. SVG writes
    `fill-rule="evenodd"` and PDFs the even-odd fill operators (`f*`, `B*`). DrawingML has no fill
    rule, so decks write the path as it is and PowerPoint decides how crossing subpaths fill.
  - `DrawingText(X, Baseline, Spans, Size, Color)` is one line of `DrawingSpan(Text, Bold)` with
    `Anchor` (`Start`, `Middle`, `End`), `MaxWidth` (its room: the generator shrinks wider text down
    to 70 %) and `Mono`. It is set in the brand's table font, else its text font, and measured by
    the generator. A span's `Color` overrides the line's.
  - `Hover` on any item is a `DrawingHover(Text, X, Y)`: a tooltip for SVG output, which decks and
    PDFs ignore. `DrawingHotspot(X, Y, Radius)` is an invisible hit area that only carries one,
    with an optional `Dot` shown while hovered (a point on a line); decks and PDFs skip it.
- **Invalid drawings throw** `ArgumentException` from every writer (`DrawingSvg.Write`, decks,
  PDFs) before anything is written: a coordinate that is not finite, a negative or non-finite size,
  width, stroke width, corner radius, dash or radius, or a colour that is not six hex digits
  (`1F4E79`; not `red`, not `#1F4E79`). The message names the item by its index.

### SVG

`DrawingSvg.Write(drawing, options)` writes a drawing as a standalone SVG for web pages, `width`
and `height` in pixels with a matching `viewBox`. An invalid drawing throws `ArgumentException`.

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
slide kinds (`## Agenda {.agenda}`, `{.quote}`, `{.statement}`, `{.contact}`, `{.chart}`),
`:::` containers (`cards`, `rows`, `panel`, `statement`, `columns`, `conclusion`, `comment`, `pagebreak`
(see [Page breaks](#page-breaks)), and the drawn
[`timeline` and `gantt`](#timelines-and-gantt-charts),
[`steps` and `riskmatrix`](#steps-harvey-balls-and-risk-matrices)), GitHub alerts (`> [!NOTE]`),
tag lists of code spans, `==marked==` text, `[words]{.success}` in a status colour, and paragraphs of
only images, side by side.

**Image attributes** follow an image directly, without a space:
`![Team](team.jpg){focus=top fit=cover scale=0.8 .round .grey}`. Both generators read them the same
way (internally `ImageAttributes`): a `{…}` of classes and `key=value` pairs (the value quoted or
not) right after the image is taken off the text; any other braces stay text. Known are
`focus=top|bottom|left|right|center`, `fit=contain|cover`, `scale=<number>` (the same meaning as
`scale=` in the title, which differs between decks and PDFs), `.round`, `.grey` and `.caption` (as
`caption` in the title). An unknown name or value, a name given twice, or `scale` both in the title
and the attributes throws `DocumentConversionException` listing the valid ones. What each does is
on the [PPTX](wwosik.DocLibs.DocumentGeneration.Pptx.md#image-attributes),
[PDF](wwosik.DocLibs.DocumentGeneration.Pdf.md#image-attributes) and
[DOCX](wwosik.DocLibs.DocumentGeneration.Docx.md#image-attributes) pages; where one has no effect, the note
`attribute-ignored` says so.
The PDF generator accepts the same Markdown, so one source serves both, and shows it plainly: see
[its documentation](wwosik.DocLibs.DocumentGeneration.Pdf.md#slide-layouts). Both reject the same
mistakes — unknown heading attributes, containers and alerts, a kind of slide with content it does
not take, a misplaced `::: conclusion` or `::: comment` (internally `SlideKinds`) — with a
`DocumentConversionException` in their format, and `::: columns` with the wrong number of parts or
widths throws alike in both (`LayoutSyntax.Columns`); PDFs set the columns side by side too. Custom containers are block-level only in both: `::text::` stays text.
Word documents draw the same syntax with styles: see
[the DOCX page](wwosik.DocLibs.DocumentGeneration.Docx.md#syntax-written-for-slides).

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
labels as code. Word documents show a cell that is only a label some key names as a coloured label,
and every other code cell as code.

`[words]{.success}` — a bracketed span with exactly one category class — sets the words bold in that
category's colour, in decks, PDFs and Word documents; on dark slides the colour is lightened halfway to white. Code
is never read for it, and any other brackets and braces stay text. An unknown category throws.

## Page breaks

`::: pagebreak` starts a new page in documents; `{.newpage}` at the end of a heading starts one with
that heading. Every generator accepts both (internally `LayoutSyntax.PageBreak`,
`HeadingAttributes.NewPage`, `PageBreaks`):

```markdown
::: pagebreak
:::

## Appendix {.newpage}
```

- `::: pagebreak` takes no arguments. It may hold content, which follows the break, and its closing
  `:::` is optional: an unclosed one runs to the end of the document and gives the same pages. Each
  generator takes the containers apart right after parsing, so a heading an unclosed break holds
  starts its section as usual.
- `{.newpage}` is a heading attribute like `{.dark}` on `#` and `##` in every format. On `###` to
  `######` every format reads it together with `{.unnumbered}`: braces holding only these two come
  off, any other braces stay text.
- **Word documents** give the next paragraph or table `w:pageBreakBefore`; a break at the very start
  or end of the body does nothing, so it never makes an empty page. `{.newpage}` on the cover heading
  throws.
- **PDFs** start a new page before the block a break comes before (`PageBreakBlock`), and before a
  heading with `{.newpage}`, unless the page is still empty; a break at the end makes no page.
  `{.newpage}` on the cover heading throws. See
  [the PDF's page breaks](wwosik.DocLibs.DocumentGeneration.Pdf.md#page-breaks).
- **Decks** accept both and ignore them: a deck has no pages to break, and a `::: pagebreak` does
  not make its section a flow slide. Their output is the same as without them.

## Classification

Two front-matter keys mark how confidential a document is (E03-S04's keys):

```markdown
---
classification: confidential
draft: true
---
```

- `classification` is `public` (the default, which shows nothing), `internal` or `confidential`;
  `draft` is `true` or `false`. A host sets them as `Classification` (the public enum
  `Classification`) and `Draft` on `PptxOptions`, `PdfOptions` and `DocxOptions`.
- **Precedence.** Front matter can raise the host's classification but not lower it: a host that sets
  `Confidential` keeps it when the Markdown says `internal`, with the note `classification-kept`.
  `draft: true` from either side makes a draft; front matter cannot undo the host's.
- **Texts.** `GeneratedTexts.Internal`, `Confidential` and `Draft` ("Intern", "Vertraulich",
  "Entwurf" in German), joined with " · ": "Confidential · Draft". A host with its own scheme renames
  them through `Texts`; the levels stay three.
- **Colour:** `Labels.Danger` for confidential, the footer colour for internal and draft.
- **Errors.** An unknown value throws `DocumentConversionException` listing the valid ones, in every
  format.
- **Where it shows:**

  | Format | Marking | Draft | Metadata |
  | ------ | ------- | ----- | -------- |
  | PDF | right of the header on every page but the cover, above the cover title | a diagonal watermark under every page's content, the cover included | none (PDF/A has no field for it) |
  | Deck | a text box `Classification` in the top right corner of every slide, cover and closing slide included | only "Draft" in the label; no watermark | `cp:contentStatus` |
  | Word | the header of every content page, above the cover title | Word's own watermark on every page | `cp:contentStatus` |

  Details: [PDF](wwosik.DocLibs.DocumentGeneration.Pdf.md#classification-marking),
  [decks](wwosik.DocLibs.DocumentGeneration.Pptx.md#classification-marking),
  [Word](wwosik.DocLibs.DocumentGeneration.Docx.md#classification).
- **Converters.** The PDF converter drops the header marking as a running header on documents of
  three pages or more, and the rotated watermark always; on shorter documents the marking comes back
  as text. The PPTX converter writes the corner label as text (it is not a placeholder).

## Audience versions

One Markdown source holds several versions of a document (`Audience`): the customer's
(`Customer`, the default), the internal one (`Internal`), which adds the team's notes and parts, and
the trainer's (`Trainer`), which also adds the solutions to exercises.

```markdown
---
audience: internal
---
## Pricing {.internal}

{.internal}

Discount up to 10 % if asked.

::: exercise
Build a deck from this page.
:::

::: solution
Call `PptxGenerator.Generate`.
:::
```

- **Choosing the version:** `audience: customer | internal | trainer` in the front matter;
  `PptxOptions.Audience`, `PdfOptions.Audience` and `DocxOptions.Audience` (`null` by default) win
  over it. An unknown value throws, listing the valid ones; the front matter's value is checked even
  when the options win.
- **Marking parts:** `::: internal` … `:::` holds blocks for the internal and trainer's versions;
  a paragraph `{.internal}` marks the block after it (with a blank line between, at the top level or
  directly inside a `:::` container or alert; `{.internal}` and then a build marker mark the marker and
  its block; `{.internal .frag}` keeps `{.frag}`); `{.internal}` on a `#` or `##` heading outside any
  container marks the chapter (up to the next `#`) or the slide (up to the next `#` or `##`). The
  token is taken off the heading's attributes in every version, so `{.dark .internal}` leaves `{.dark}`.
- **Exercises:** `::: exercise` is shown in every version, after a bold label "Exercise 1"
  (`GeneratedTexts.Exercise`, German "Übung"), numbered through the document in the version's order.
  A `::: solution` right after an exercise is shown to the trainer only, labelled "Solution 1"
  (`GeneratedTexts.Solution`, "Lösung") with its exercise's number.
- **One filter before every generator:** right after parsing (and after page breaks are taken
  apart), the generators remove what the version does not show and take the three containers apart,
  so slide splitting, numbering, agendas and contents follow the version. A document without the
  syntax is unchanged. The note `audience-omitted` counts what was left out.
- **Marking:** a version other than the customer's is classified `internal` (see
  [Classification](#classification)) unless the host's options or the front matter set a
  classification: `classification: public` keeps it unmarked. The options cannot tell `Public` set on
  purpose from the default.
- **Mistakes throw** in every version, with the line: `{.internal}` on a `###` to `######` heading or
  on a heading inside a container, a `{.internal}` followed by nothing, a heading, another
  `{.internal}` or a `---`, one inside a list or quote, a `::: solution` that does not directly follow
  a `::: exercise`, and arguments on any of the three containers.
- **For hosts:** `AudienceVersions.Distinct(document)` lists the versions whose content differs, in
  order: always `Customer`, `Internal` when anything is marked internal, `Trainer` when there is a
  solution. Generate only those.
- **Converters** are not changed: a converted document is one version.
- **Not filtered:** the style checks of `Check` (words, bullets, titles) count every version's
  content.

## Front matter

Every generator reads options from a block at the very top of the Markdown; the keys each one
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
- **Shared keys:** `template` (applied first, whatever its position), `title`, `author`,
  `description`, `keywords` (comma-separated; both for the file's properties), `lang` (see
  [Document language](#document-language)), `typography` (see [Typography](#typography)), `highlight`
  (see [Code highlighting](#code-highlighting)), `coverImage`, `coverImageSize` and `coverImagePosition` (see
  [Cover image](#cover-image)),
  `classification` and `draft` (see
  [Classification](#classification)), `audience` (see [Audience versions](#audience-versions)), and `labels.<category>` (see
  [Slide layouts in Markdown](#slide-layouts-in-markdown)). `subtitle` and `date` (the cover's
  subtitle and date, as `DocxConverter` writes them back) are accepted by every generator and used
  by Word documents only; PDFs and decks ignore them. The rest are the generator's.
- **Document keys** (`FrontMatter.DocumentKeys`): `toc` (`true`/`false`), `tocDepth` (`1`–`3`),
  `headingNumbers` (`true`/`false`) and `justify` (`true`/`false`, justified body text), read by
  PDFs and Word documents (see [Contents and heading numbers](#contents-and-heading-numbers) and
  the generators' own pages). A deck accepts them, checks their values and ignores them, so one
  Markdown serves every format. `layout` (`report`/`plain`, a document without cover and header)
  is a PDF key only, not a document key: decks and Word documents reject it as unknown.
- `template` naming the template the caller chose keeps the caller's values; naming another one
  starts from that template's defaults.
- **Mistakes throw** `DocumentConversionException` with the generator's format: a line that is not
  `key: value`, a key without value, a duplicate key, a block that is never closed, an unknown key
  or one the template does not support (the message lists the valid keys), an invalid value (the
  message lists the valid ones).

## Cover image

Every generator puts an image on its cover when the front matter names one, such as a product
mark or a partner's logo; without the key, the brand's `CoverImage` is used, if it has one.

```markdown
---
coverImage: mark.svg
coverImageSize: small
coverImagePosition: right
---
```

| Key | Values |
| --- | ------ |
| `coverImage` | The path of a resource of the document (never a file on disk): PNG, JPEG or, through the image converters, SVG; GIF too in decks and Word documents |
| `coverImageSize` | `small`, `medium` (default) or `large`: a tenth, a sixth or a quarter of the cover's height |
| `coverImagePosition` | `below` (default): under the subtitle, left-aligned with the title; `right`: right of the title block, top-aligned with the title, at most 40 % of its width, the title and subtitle narrowed to leave it room |

- **Where:** decks on the title slide (the first `#`), HTML decks as drawn from that deck, PDFs on
  the cover page, Word documents on the cover — `below` inline in a paragraph of its own after the
  subtitle, `right` anchored at the right margin, the title wrapping left of it.
- It keeps its aspect ratio and **never covers the logo, the artwork or the tagline**: where
  they (or the foot of the cover) are in the way, it is made smaller, never moved. With less than 2 % of
  the cover's height left it is left out with the note `cover-image-ignored`. Word lays out
  its own pages, so there it is only kept within the text width.
- **A template's picture placeholder:** when the roles of a caller's or brand's PowerPoint template
  declare a `picture` placeholder on the title layout, the image is fitted into it (not
  cropped) instead, whatever its size key says.
- An SVG is converted by the registered image converters, sharp enough for its size: decks and Word
  documents carry the SVG beside the PNG, which PowerPoint and Word 2016 and later draw as a vector;
  PDFs embed the PNG.
- **Errors and notes:** a `coverImage` that is not among the document's resources throws
  `DocumentConversionException` naming the key and the path; an invalid size or position throws
  like any front matter value. A document without a cover (no leading `#`, a plain PDF, a deck
  without a title slide) gets `cover-image-ignored` for an image the front matter names; the
  brand's image is left out silently.

## Document language

`lang` in the front matter, else `PptxOptions.Language` / `DocxOptions.Language` /
`PdfOptions.Language` (default `en`),
is a BCP 47 tag such as `en`, `de`, `de-CH` or `en-GB`. Anything else is an error.

- **Texts.** It picks the `GeneratedTexts`: the texts a generator writes itself, today the alert
  titles (`Note`, `Tip`, `Important`, `Warning`, `Caution`), the word before a chart's source
  (`Source`; German `Quelle`) and the dates of
  [timelines and Gantt charts](#timelines-and-gantt-charts) (`MonthNames`, `DayFormat`,
  `MonthFormat`, `QuarterFormat`, `WeekFormat`) and the axis titles of
  [risk matrices](#steps-harvey-balls-and-risk-matrices) (`Likelihood`, `Impact`; German
  `Eintrittswahrscheinlichkeit`, `Auswirkung`) and the heading of a Word document's table of contents
  and of a PDF's (`Contents`; German `Inhalt`) and the labels of captions (`Figure`, `Table`; German
  `Abbildung`, `Tabelle`) and the [classification marking](#classification) (`Internal`,
  `Confidential`, `Draft`; German `Intern`, `Vertraulich`, `Entwurf`) and the alt text of a Word
  document's chart without a title (`Chart`; German `Diagramm`). Code-block
  renderers get them as `CodeBlockRequest.Texts`. `GeneratedTexts.English` and `GeneratedTexts.German` are built in; a host adds or
  replaces languages with the options' `Texts`, by tag. Every property defaults to English, so a
  new language starts from `GeneratedTexts.English with { Warning = "…" }` and stays complete when
  later versions add texts. `Footnotes` (`Notes`; German `Anmerkungen`), the heading PDFs once gave
  their footnotes at the end, is no longer used and stays for compatibility.
- **Matching.** A tag is matched in full, then by its language alone (`de-CH` → `de`), first among
  the host's `Texts`, then among the built-in ones. A tag nothing matches gets the English texts and
  the note `language-unknown`; the tag itself is still written.
- **Tags.** Decks write it on every run and paragraph end (`lang`, PowerPoint's spell checking),
  drawings and tables included, and as `dc:language` in the core properties. PDFs write it as the
  catalog's `/Lang` and as `dc:language` in the XMP metadata. Word documents write it as
  `dc:language` in the core properties, as `w:lang` in the document defaults and as
  `w:themeFontLang` in the settings, so Word spell-checks in it.
- The cover date stays `dd.MM.yyyy` in every language.

## Typography

Off by default. `typography: true` in the front matter, or `PptxOptions.Typography` /
`PdfOptions.Typography` / `DocxOptions.Typography`, sets text the way a typesetter would, in the
rules of the [document language](#document-language); `typography: false` switches it off whatever
the option says. Writers type straight quotes and hyphens; the generator does the rest.

| Typed | English | German |
| ----- | ------- | ------ |
| `"Hello"`, `'word'` | “Hello”, ‘word’ | „Hallo“, ‚Wort‘ |
| `it's`, `'90s`, `5'` | it’s, ’90s, 5’ | the same |
| `a - b`, `a -- b`, `a --- b` | a – b, a – b, a — b | the same |
| `...` | … | … |
| `10 km`, `5 %`, `z. B.`, `S. 12`, `13. November` | a no-break space (U+00A0) in place of the space | the same |

- **The pass** (internal `Typography`) runs after the coloured words and icons on every run list: a
  paragraph, heading, list item, table cell, footnote, definition term, the cover's title and
  subtitle, and the front-matter `title` and `author` (also in the file's properties). It works on the
  paragraph's text as one string, so a quote that opens before `**bold**` and closes after it pairs.
- **Only characters are replaced:** a space is never added or removed. `10%` stays without a space,
  `10 %` gets a no-break space.
- **Quotes:** a `"` or `'` after the start, a space, an opening bracket, a dash or an opening quote
  opens; otherwise it closes. A `'` between letters, after a digit, before a digit at the start of a
  word, or after a letter while no single quote is open is the apostrophe. A `"` after a digit (inches)
  stays straight.
- **Dashes:** `---` is an em dash, `--` an en dash, a hyphen with a space on either side the rules'
  `Dash` (spaces kept). Never between digits (`2020-2026`, `2026-11-14`, `2020--2026`, `5 - 3`), in
  arrows (`-->`, `<--`) or in four or more hyphens.
- **Ellipsis:** exactly three dots.
- **No-break spaces** (U+00A0): inside the rules' `Abbreviations` and between one and a number after
  it, between a number and a following unit of `Units`, and between a day (`13`, `13.`) and a month of
  `MonthNames`.
- **Never touched:** code spans and blocks, status labels, footnote references, web and e-mail
  addresses (`https://…`, `www.…`, `a@b.c`), HTML shown as text, link targets (link text is set),
  front-matter values other than `title` and `author`, and the text of drawings: Mermaid diagrams,
  charts and the drawn containers (`timeline`, `gantt`, `steps`, `riskmatrix`) keep what was typed.
- **Fonts.** A character is only used when every face the text can be set in has it: in PDFs the
  text and table faces, regular to bold italic; in decks and Word documents a brand's fonts the same
  way, while a caller's template or a brand without font files gets every character, since Office
  substitutes fonts. Otherwise the typed character stays, with the note `typography-skipped`, so a
  PDF never fails on a character the pass would have brought in.
- **`TypographyRules`** (public record): `DoubleQuotes` and `SingleQuotes` (`Opening`, `Closing`),
  `Apostrophe`, `Dash`, `Abbreviations`, `Units`, `MonthNames`. `TypographyRules.English` (“ ” ‘ ’,
  no abbreviations) and `TypographyRules.German` („ “ ‚ ‘, `z. B.`, `d. h.`, `u. a.`, `i. d. R.`,
  `o. Ä.`, `u. U.`, `v. a.`, `S.`, `Nr.`, `Abs.`, `§`) are built in; both have the units (`%`, `‰`, `°C`,
  `€`, `EUR`, `USD`, `$`, `km`, `m`, `cm`, `kg`, `h`, `min`, `MB`, `px`, `pt` and more) and their long
  month names plus `GeneratedTexts.MonthNames`. A host adds or replaces languages with the options'
  `TypographyRules`, by tag, matched like `Texts` (in full, then by language): `de-CH` gets the German
  rules unless the host gives its own, and any other language the English ones, without a note
  beyond `language-unknown`. A French profile with « » and no-break spaces inside the quotes:
  `TypographyRules.English with { DoubleQuotes = ("«\u00A0", "\u00A0»") }`.

## Code highlighting

Off by default. `highlight: true` in the front matter, or `PptxOptions.HighlightCode` /
`PdfOptions.HighlightCode` / `DocxOptions.HighlightCode`, colours fenced code blocks that name their
language after the fence; `highlight: false` switches it off whatever the option says. Any other
value is an error. With highlighting off, nothing about code changes: the files are as before.

- **Languages** (name after the fence, any case; aliases in brackets): `csharp` (`cs`, `c#`), `vb`
  (`vbnet`, `vb.net`), `fsharp` (`fs`, `f#`), `cpp` (`c`, `c++`), `java`, `javascript` (`js`),
  `typescript` (`ts`), `php`, `python` (`py`), `powershell` (`ps1`, `pwsh`), `sql`, `html` (`htm`),
  `xml` (`xaml`), `css`, `markdown` (`md`), `haskell` (`hs`), `json`, `yaml` (`yml`), `bash` (`sh`,
  `shell`, `zsh`). The guides list the same names (internal `CodeLanguages`).
- **The highlighter** (internal `CodeHighlighter`) is built in: the regular-expression languages of
  [ColorCode.Core](https://github.com/CommunityToolkit/ColorCode-Universal) (MIT, managed), with own
  definitions for `json` (ColorCode's string pattern can take exponential time on an unclosed string),
  `yaml` and `bash`, whose patterns take linear time per line. Its scopes are mapped to the public
  enum **`CodeTokenKind`**: `Keyword`, `String`, `Comment`, `Number`, `Type` (type and namespace
  names, attributes, shell variables), `Markup` (tags and attribute names, JSON and YAML keys),
  `Punctuation` (drawn as `Plain`) and `Plain`. The languages are compiled on the first highlighted
  block, into a cache of the highlighter's own: ColorCode's global registry is neither used nor
  changed. There is no public extension point for other highlighters.
- **What is highlighted:** fenced blocks with a known language that no code-block renderer took
  (`mermaid` and `chart` still go to their renderers first). Indented code blocks, fences without a
  language or with `text`, `plain`, `console` or `output`, and inline code stay as before.
- **Colours** come from `Brand.Colors.Code` (`BrandCodeColors`, brand packs `colors.code.*`), on
  slides `SlideColors.Code` merged over it; each colour left out is derived from the brand (see
  [What a brand holds](#what-a-brand-holds)) and darkened in steps until it has a contrast of at
  least 4.5:1 on the code background — PDFs and Word `F3F4F6`, decks their code box. On dark
  slides (a dark template, or a `.dark` slide's code box) it is lightened instead; where even white
  falls short of 4.5:1, such as on a mid-tone code box, it becomes white, the plain text's colour. A
  colour the brand sets is used as it is. Decks on a caller's template and Word documents on a
  caller's template take theme colours: keywords accent 1, types accent 2, strings accent 6, numbers
  accent 4, comments the text colour at 60 %, markup the hyperlink colour.
- **Colour only:** no bold, no italic, so line widths and breaks stay as they are and no face is
  needed that a font lacks. Decks keep one paragraph per line with a run per piece; PDFs draw one
  text item per piece, breaking long lines by characters across the pieces; Word writes a run per
  piece with `w:color` (and `w:themeColor` on a caller's template) as direct formatting in the
  `Code` style.
- **Note `code-language-unknown`:** with highlighting on, a fenced block names a language neither a
  renderer nor the highlighter knows (a typo such as `csharpp`); not given for the plain names above
  or when a renderer already reported on the block.
- **Size:** ColorCode.Core adds 122 000 bytes (its `netstandard2.0` assembly); a Native AOT app that
  highlights publishes without warnings.

## Footnotes, task lists and definition lists

Both generators read the Markdig extensions for footnotes, task lists and definition lists; how each
one shows them is in its own documentation.

- `[^label]` refers to a footnote defined as `[^label]: text` on a line of its own, anywhere in the
  document. A reference without a definition stays text and gives the note `footnote-missing`; a
  definition nobody refers to is left out.
- `- [ ] item` and `- [x] item` are task list items: a box instead of the bullet, empty or ticked.
- A paragraph of one line followed by `:   description` lines is a definition list: the line is the
  term, each `:` line a description. Several terms in a row share the descriptions after them.

## Contents and heading numbers

Documents (PDFs and Word documents) read the same keys and number headings by one rule
(internally `HeadingNumbering`); decks accept the keys and `{.unnumbered}` and ignore them.

- `toc: true` (`PdfOptions.TableOfContents`, `DocxOptions.TableOfContents`) puts a table of contents
  after the cover; `tocDepth: 1|2|3` (`TableOfContentsDepth`, default `2`) is how many heading levels
  it lists, counted from the highest level after the cover.
- `headingNumbers: true` (`HeadingNumbers`; default off in PDFs, on in Word documents' built-in
  template) numbers headings:
  - **Levels.** The cover heading is never numbered. Numbering starts at the highest heading level used
    after the cover: `#` chapters when there are any, otherwise `##`. It numbers three levels from there
    (`1`, `1.2`, `1.2.3`); deeper headings stay unnumbered.
  - **Skipped levels** never count as 0, in any format: a number has one part per numbered heading it
    is under. A `###` straight after a `#` is `1.1`; `##` sections before the first `#` are `1`, `2`,
    and that `#` is `3`.
  - **`{.unnumbered}`** at the end of a heading (any level) gives it no number, and it does not count.
    Headings inside a `:::` container are no chapters: no number, not in the contents.
- **Appendices** (internally `BackMatter`): `# Glossary {.appendix}` makes that chapter and every
  later `#` an appendix. Each appendix `#` (not `{.unnumbered}`, not in a container) reads
  "Appendix A Glossary", "Appendix B …" (`GeneratedTexts.Appendix`, German "Anhang"; after Z come
  AA, AB) in the text, the contents and the bookmarks, whether headings are numbered or not; with
  numbers, the headings below it are `A.1`, `A.1.1`. The attribute goes once, on a top-level `#`
  after the cover, never with `.closing`; anything else throws with the line. `{.internal}` on it
  hides every appendix from the customer's version. Decks ignore it with `attribute-ignored`, as
  documents ignore a deck's `{.backup}` (whose placement is checked alike).
- Numbers and the contents never change a heading's anchor, so `[see](#costs)` keeps working.

## Captions

The caption syntax, read alike by PDFs and Word documents (internally `Captions`). Decks show a
`Table: …` or `Figure: …` paragraph under its visual as they always did, now without the prefix and
without a number; their image titles keep their meaning (`scale=` and `caption`).

- **Image.** The title of an image is its caption, once a `scale=N` token and the word `caption`
  standing alone are taken out. A title of settings only gives none; `"caption"` alone (or the
  attribute `.caption`) uses the alt text. `![Sales](s.png "Sales by region, 2026")` and
  `![Sales by region](s.png "caption")` both get a caption.
- **Table.** A paragraph starting `Table: ` right after a table is its caption. A blank line
  between them is needed: without one, Markdown does not end the table.
- **Diagram or chart.** A paragraph starting `Figure: ` right after a fenced block a renderer drew
  is its caption.
- `Table:` and `Figure:` are syntax and stay English whatever `lang` says. The label written is the
  document language's (`GeneratedTexts.Table`, `GeneratedTexts.Figure`: "Table", "Figure"; German
  "Tabelle", "Abbildung"), and a caption reads `<label> <n>: <text>`, figures and tables numbered
  separately through the whole document. Table captions go above the table, figure captions below.
- A `Table:` or `Figure:` paragraph anywhere else stays text, with the note `caption-ignored`
  (decks too). A `Figure:` after an image, a `qr:` image or a drawn container (`::: timeline`) is one
  of those: images take their caption from the title, and a QR code's `"caption"` shows its alt text
  inside the drawing.

## Timelines and Gantt charts

Two `:::` containers draw a schedule from the Markdown inside them. Both are `Drawing`s, so decks get
editable shapes on a [flow slide](wwosik.DocLibs.DocumentGeneration.Pptx.md#layouts) and PDFs vectors,
scaled down to the text width; neither needs an option or a renderer.

````markdown
::: timeline
- **2026-01** Kick-off
- **2026-03** Design review: scope and budget agreed
- **2026-06-30** Go-live
:::

::: gantt grid=month
| Task         | Start      | End        |
|--------------|------------|------------|
| Analysis     | 2026-01-05 | 2026-02-13 |
| Build        | 2026-02-16 | 2026-05-29 |
| Go-live      | 2026-06-01 |            |
:::
````

**Dates** are written in ISO form: `2026`, `2026-Q2`, `2026-03` or `2026-03-15`. Each is a period from
its first to its last day; anything else throws `DocumentConversionException` naming the line and the
accepted forms. They are shown in the document's [language](#document-language), from
`GeneratedTexts` and without `CultureInfo` (hosts with invariant globalization show the same text):

| Written | English | German |
| ------- | ------- | ------ |
| `2026-03-15` | 15 Mar 2026 (`DayFormat` `{0} {1} {2}`) | 15. März 2026 (`{0}. {1} {2}`) |
| `2026-03` | Mar 2026 (`MonthFormat` `{0} {1}`) | März 2026 |
| `2026-Q2` | Q2 2026 (`QuarterFormat` `Q{0}`, then the year) | Q2 2026 |
| `2026` | 2026 | 2026 |
| an ISO week on a Gantt axis | W12 (`WeekFormat` `W{0}`) | KW 12 (`KW {0}`) |

`MonthNames` are twelve short names, January first: `Jan` … `Dec`, German `Jan.`, `Feb.`, `März`,
`Apr.`, `Mai`, `Juni`, `Juli`, `Aug.`, `Sept.`, `Okt.`, `Nov.`, `Dez.`. A host's own `GeneratedTexts`
built from `English` with `with { … }` keeps the English ones; a list of another length throws
`ArgumentException` when a date is shown.

**Today** is the date of `PptxOptions.TimeProvider` / `PdfOptions.TimeProvider` in UTC. `today=2026-11-01`
after the container's name pins it, so a document looks the same whenever it is generated.

**Arguments** are `key=value` pairs after the name: `today=YYYY-MM-DD` on both,
`grid=week|month|quarter` on `gantt`. An unknown key or value, a key given twice, or a word that is no
pair throws, listing the valid ones; both generators check them alike (`LayoutSyntax`).

**`::: timeline`** holds one list of 2 to 8 entries, each starting with a bold date, then a title and an
optional description after the first `:`. Fewer or more entries, another block, or an entry without
a bold date or a title throw. Entries are sorted by date (equal dates in the order written) and drawn
as a horizontal line with one dot per entry, **evenly spaced** — not to scale, so labels never collide
— the date above the line, the bold title (at most two lines) and the description (at most three, cut
with "…") below. With more than five entries every other entry has them the other way round, so its
labels have twice the room. Entries whose period ended before today are grey (diagram `Line`); the
first one that has not ended is ringed and its date in the accent colour, later ones have an accent
dot. 960 px wide and as tall as the labels need: 116 px with one-line titles and no descriptions, up to 300 px.

**`::: gantt`** holds one pipe table of 1 to 20 rows. The first three columns **by position** are task,
start and end, whatever their headers say; further columns are ignored. A row without an end, or
with the same end as start, is a milestone (a diamond on its start); an end before its start throws.
A bar runs from the start's first to the end's last day.

- **Grid:** `grid=` or, without it, from the span: up to 10 weeks weeks, up to 18 months months, else
  quarters. The axis shows the year at its start and at each new year, then each unit's label: the
  ISO week (`W12`), the month name or the quarter; with more than 26 units, every second (third, …)
  label only.
- **Drawing:** task names on the left (at most 226 px, shrunk down to 70 %), every other row banded in
  the diagram `Surface` colour, a thin line at each unit, bars and diamonds in the diagram palette by
  row, grey once they ended before today, and a line in `Highlight` for today when it lies within the
  axis. 960 px wide, 50 px of axis plus 28 px a row.

**Text** inside entries and cells keeps bold; other emphasis and links become plain text, code its
text (a task that is only code is set in the mono font). Line breaks are estimated from the
characters, since the real font is only known when the drawing is placed.

**Colours** are the brand's [diagram colours](#what-a-brand-holds). On a dark slide — a `{.dark}`
heading, a `::: panel dark`, or a brand's dark template — decks draw with the brand for dark slides
(white text, light lines, the highlight as accent), as Mermaid diagrams on dark templates.

Internally, `VisualContainers` is the ground for drawn containers: its names join `LayoutSyntax`'s, it
checks their arguments and draws them from the `CustomContainer` and a `VisualContext` (brand, today,
texts, lines, format); both parsers turn the drawing into a `DrawingBlock`. `DrawingTexts` turns inlines
into `DrawingSpan`s and breaks them into lines; `SchedulePeriod` parses and shows the dates.

## Steps, Harvey balls and risk matrices

Three graphics common in consulting decks and reports. `::: steps` and `::: riskmatrix` are drawn
containers like [timelines](#timelines-and-gantt-charts): `Drawing`s, editable shapes on a flow slide
in decks, vectors in PDFs, taking no arguments. Harvey balls live inside tables.

````markdown
::: steps
- **Analyse** – understand the current process
- **Design** – agree the target picture {.current}
- **Build**
- **Roll out**
:::

::: riskmatrix
| Risk               | Likelihood | Impact |
|--------------------|------------|--------|
| Supplier delay     | 4          | 3      |
| Key person leaves  | 2          | 5      |
:::

| Option | Cost  | Speed |
|--------|:-----:|:-----:|
| Buy    | [1/4] | [4/4] |
| Build  | [3/4] | [1/2] |
````

**`::: steps`** holds one list of 2 to 7 items, each starting with a bold title, then an optional
description after a dash or colon (as the items of `::: cards`). `{.current}` at the end of at most one
item marks the current step: the steps before it are done (accent fill, white title), the current one
is filled in `Highlight` with a bold white title, later ones are outlined (diagram `Surface` fill,
`Border` line, `Text` title). Without `{.current}` every step is alike, in the accent colour with a
bold white title. `{.current}` is the only attribute; any other `{…}` at the end of an item, a second
`{.current}`, fewer or more items, or an item without a bold title throw. Drawn 960 px wide as one row
of chevrons (the first with a flat left end), titles inside (at most two lines), descriptions below
(at most three, cut with "…"), every line shrunk to its room; 68 px tall without descriptions. Where
the highlight is the accent colour, as on dark slides, the current step also gets an outline in the
diagram text colour.

**Harvey balls:** a table cell whose whole content is `[0/4]`, `[1/4]`, `[2/4]`, `[3/4]` or `[4/4]` —
or `[1/2]`, the same as `[2/4]` — is a circle filled by that many quarters, from twelve o'clock
clockwise. A cell with anything else beside it, in code or emphasis, or with its bracket escaped
(`\[3/4]`) stays text. It applies to every table, header rows included, in decks (plain and flow
slides) and PDFs.

- **PDFs** draw it like an icon in text: a ring and a filled sector of one em (`HarveyBall.Drawing`, a
  24 × 24 px drawing), in the cell's text colour (white in the header row), at the cell's alignment.
  No character is written, so the bundled fonts, which lack ◔ ◑ ◕, are no matter.
- **Decks** write the glyph `○ ◔ ◑ ◕ ●` (U+25CB, U+25D4, U+25D1, U+25D5, U+25CF) into the cell, so it
  stays aligned with its row and editable. The run names `Segoe UI Symbol` in its Latin, East Asian
  and complex-script fonts, so PowerPoint does not depend on the theme font having the glyphs; the font
  is never embedded, and where it is missing (macOS) Office's font fallback supplies the glyphs. The
  PPTX converter reads them back as the glyphs.

**`::: riskmatrix`** holds one pipe table of 1 to 15 rows. The first three columns **by position** are
the risk, its likelihood and its impact, each a whole number from 1 to 5; headers are free, further
columns ignored. Anything else, an empty risk, or more rows throw, naming the line. It is drawn as a
5 × 5 grid, likelihood up and impact right, with the values along both axes and the axis titles from
`GeneratedTexts` (`Likelihood` above, `Impact` below). Each cell is tinted by its score, likelihood ×
impact, in fixed bands: 1–4 the brand's `Labels.Success`, 5–9 `Labels.Warning`, 10–25
`Labels.Danger`, each at 22 % on white as status labels tint cells. Each risk is a dot in its band's
full colour with its number (in the order written) in white; risks sharing a cell sit side by side,
in rows of three (four when more than nine share it). A numbered legend beside the grid names them
(at most two lines each). 960 px wide; 446 px tall, or the legend's height when that is more — the
drawing is always wider than tall, so the legend never needs to go below the grid.

## Scenes

`::: scene` is a slide drawn from named shapes on a 160 × 90 grid over the whole slide (internal
`SceneSyntax`, `Scene`, `SceneDrawing`). Every generator reads it here, so all reject the same
mistakes:

- `SceneSyntax.Check` (after `BuildMarker.Check`) puts each scene alone under its `##` heading, with
  only `::: notes` beside it, one a section, never inside another block and without a build marker.
- `SceneSyntax.Parse` reads the container's lines from the Markdown text Markdig parsed (its
  `Span`), not from Markdig's inlines: `box|round|ellipse|text name x y w h ["label"] [attributes]`
  and `arrow|line from to ["label"] [attributes]`, as the
  [PPTX page](wwosik.DocLibs.DocumentGeneration.Pptx.md#scenes) describes. Errors are
  `DocumentConversionException`s naming the scene line and its line in the original.
- A connector without `step=` takes the later step of its shapes, without `exit=` the earlier exit.
  `Scene.Sides` chooses the facing sides a connector runs between, `Scene.Clicks` the click numbers.
- `SceneDrawing.Final` is the still documents show: 960 × 540 px (6 px a unit, so a pixel is a
  point of a 16:9 slide), the shapes and connectors still shown after the last click, labels as
  `DrawingText`s centred in their shape with `MaxWidth` of its width less 4 px a side, connectors
  as lines with a filled arrow head, their labels above or beside the middle. Colours from the
  brand: the default box `TableBand` with a `Primary` line, roles `Primary`, `Link` (accent),
  `Highlight`, the label colours; text white or dark (`Diagram.Text`) by contrast.

In Word documents a scene is a picture of that drawing (through an image converter, like the drawn
containers); without one it is left out with `code-not-drawn`. A scene counts as a diagram in
`CheckStatistics.Diagrams`.

## QR codes and icons

Two kinds of small vector graphics written in the Markdown; both are `Drawing`s, so decks get
editable shapes and PDFs vectors, as for diagrams.

**QR codes.** An image whose URL starts with `qr:` is a QR code for the rest of the URL, taken
literally: `![Survey](qr:https://example.com/survey?id=3)`, `qr:mailto:…`, `qr:WIFI:…`.

- Encoded by [QRCoder](https://github.com/Shane32/QRCoder) (MIT) from the UTF-8 bytes, in byte mode
  with error correction M and the smallest version that holds them; only its module matrix is used.
  More than 1 000 bytes, or none, throws `DocumentConversionException`.
- Drawn (internally `QrDrawing`) as a white square with a quiet zone of four modules and one path of
  every dark module (a rectangle per horizontal run, non-zero fill): two shapes in a deck, two paths in
  a PDF. 4 px a module; `scale=` (title or attributes) multiplies it.
- The modules take the brand's primary colour when it has at least 4.5:1 contrast to white, else the
  diagram text colour, else black; dark on its white square on dark slides too.
- The alt text is the description; `caption` in the title or `.caption` puts it under the code. Any
  other image attribute is an error.

**Icons.** `:icon-<name>:` in running text (never in code) draws the icon of that name, in Tabler's
kebab-case: `:icon-check:`, `:icon-circle-check:`. Icons are on by default, from the embedded set:

```csharp
public interface IIconSet
{
    IReadOnlyCollection<string> Names { get; }
    Drawing? Find(string name);      // null: not in the set
}

TablerIcons.Outline   // the whole Tabler Icons outline set, version TablerIcons.Version
```

- `PptxOptions.Icons` and `PdfOptions.Icons` (default `TablerIcons.Outline`) take any `IIconSet`, such
  as a caller's own icons, which then replaces Tabler's. `Icons = null` switches icons off:
  `:icon-name:` stays text and is not checked.
- An icon is drawn in one colour, the colour of its place (accent as a figure or bullet, the text
  colour inline): every stroke and fill of its `Drawing` takes it.
- A name the set lacks throws `DocumentConversionException` with up to three close names, by edit
  distance and then common prefix: "Unknown icon 'chek' at line 3. Did you mean check, checks, chess?".
- Where each format draws icons is on the [PPTX](wwosik.DocLibs.DocumentGeneration.Pptx.md#qr-codes-and-icons)
  and [PDF](wwosik.DocLibs.DocumentGeneration.Pdf.md#qr-codes-and-icons) pages; elsewhere the note
  `icon-not-drawn` says it was left out.

**The embedded set.** [Tabler Icons](https://tabler.io/icons) 3.52.0 (MIT), outline style, from the npm
package `@tabler/icons` 3.52.0. Each icon is a 24 × 24 drawing of one path, stroked 2 px with round
ends and joins, unfilled, as Tabler draws them. The set holds 5491 icons. The data is one embedded,
gzip-compressed resource (`Icons/tabler-outline.txt.gz`, logical name `Icons.tabler-outline.txt.gz`):
one line per icon, `name<TAB>path data`, absolute `M`/`L`/`C`/`Z` only, two decimals. It is read and
decompressed on the first lookup only; each icon's `Drawing` is built when first asked for and kept.
A document without icons pays nothing at run time beyond the larger assembly.

| Measured (3.52.0) | Size |
| ----------------- | ---- |
| Icon data, raw | 1 369 064 bytes |
| Icon data, compressed (the embedded resource) | 313 474 bytes |
| Budget for the compressed resource | 1 000 000 bytes, guarded by a test |
| Packed `.nupkg` before the icons | 153 871 bytes |
| Packed `.nupkg` with the icons and QRCoder | 482 484 bytes |

**Updating the set.** `./run.sh icon-import` (or `run.ps1 icon-import`) downloads the version pinned in
`tools/icon-import/icon-import.cs` from the npm registry (or reads `--source <.tgz>`), converts every
outline SVG (circles, ellipses, lines, polylines, polygons, rectangles, arcs and quadratic curves become
cubic and straight segments there, so the library needs no SVG parser), prints the count and the raw
and compressed sizes, and fails above the budget. It writes the resource, which is committed, so a build
needs neither the tool nor the network. After an update: change `TablerIcons.Version`, the version in
`THIRD-PARTY-NOTICES.txt`, and the count and sizes above (a test checks the count against this page).
If an update breaks the budget, the set is cut to a curated list, which goes back to the human first.

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
