# Changelog

What changed in the wwosik.DocLibs packages, for the code that uses them, newest first.

All packages share one version, `yyMM.d.H[.r]`: the date and hour (Europe/Berlin) of the release,
plus a counter for a further release within the same hour. The version says nothing about
compatibility, so this file does. Read the sections between your version and the new one before
updating, breaking changes first.

## Unreleased

### Breaking changes

- **The Markdown from PDFs changes shape** (`PdfConverter`). Ruled tables come out as pipe tables,
  drawn and glyph bullets, task boxes and numbered lines with a hanging indent as (nested) lists,
  monospace lines as fenced code blocks, footnotes at the foot of a page as `[^n]` with their
  definitions at the end of the page's section, and images between the paragraphs they stand
  between instead of after the page's text. Raised numbers stay on their line, and rotated text
  (watermarks) is left out. A consumer that parses the output should check it against the new shapes.
- **`:icon-<name>:` in text is an icon now,** in both generators and without any option: text such
  as `:icon-check:` that used to stay as typed is drawn as the Tabler icon of that name (or, where
  the format cannot draw it, left out with the note `icon-not-drawn`), and a name Tabler does not
  have (`:icon-chek:`) throws `DocumentConversionException` suggesting close names. Code spans are
  never read. To keep such text as typed, set `PptxOptions.Icons = null` / `PdfOptions.Icons = null`.
- **`![…](qr:…)` is a QR code now:** an image URL starting with `qr:` used to fail as a missing
  resource and is now drawn as a code; image attributes on it other than `scale` and `.caption` are
  an error.
- **`wwosik.DocLibs.DocumentGeneration` depends on QRCoder** (1.8.1, MIT), whose package brings
  `System.Drawing.Common` along as a dependency (not loaded: only QRCoder's module matrix is used),
  and is larger by the embedded icon data: 313 KB compressed in the assembly, the `.nupkg` from about
  154 KB to about 480 KB. Licences in the package's new `THIRD-PARTY-NOTICES.txt`.
- **`PptxGenerator.Generate` and `PdfGenerator.Generate` return a `GenerationResult`** instead of
  `void`. Source that calls them as a statement compiles unchanged, but the change is
  binary-breaking: rebuild everything compiled against an earlier version, and a method group
  used as an `Action<…>` no longer converts (use a lambda). The documents they write are unchanged.
- **Footnotes, task lists and definition lists are read** in both generators. Markdown that used
  `[^label]`, a list item starting `[ ]` or `[x]`, or a line starting `:   ` after a one-line
  paragraph is no longer written as typed: it becomes a footnote, a box, or a definition list (on a
  slide, rows on a flow slide). A footnote reference without a definition stays as typed, with a note.
- **Every deck run and every PDF carries a language tag**, `en` unless `lang` or the new
  `Language` option says otherwise: `lang` on runs and paragraph ends (replacing the fixed `en-US`
  of drawings), `dc:language` in the deck's core properties, `/Lang` and `dc:language` in the PDF.
  PowerPoint spell-checks in that language; nothing else in the output changes.
- **Invalid drawings throw `ArgumentException`** in `DrawingSvg.Write`, decks and PDFs: a
  coordinate or size that is not finite, a negative size or width, or a colour that is not six hex
  digits. They used to produce broken files.
- `wwosik.DocLibs.DocumentGeneration` depends on Markdig (as the generators already did).
- **`PptxConverter` reads speaker notes:** a slide with speaker notes is now followed by
  `::: notes`, the notes' paragraphs and `:::` in the Markdown, by default and with no option to turn
  it off. Code that compares or parses the converter's output sees the new lines; strip the
  `::: notes` blocks if they are not wanted.
- **Braces right after an image are read as image attributes** in both generators:
  `![a](x.png){.round}` no longer shows `{.round}` as text. A `{…}` of classes and `key=value`
  pairs directly after an image that names anything but `focus`, `fit`, `scale`, `.round`, `.grey`
  or `.caption` now throws `DocumentConversionException`; put a space before braces meant as text.
  Other braces stay text.
- **`wwosik.DocLibs.DocumentGeneration.Pdf` depends on SkiaSharp**, used only for `.grey` images.
  On Linux, an application that greys images adds `SkiaSharp.NativeAssets.Linux` (or
  `.NoDependencies`) at the same version; without it, a `.grey` image fails with a
  `DocumentConversionException` naming the package. Documents without `.grey` never load it. Its
  licence is in the package's `THIRD-PARTY-NOTICES.txt`.
- **`CodeBlockRequest` has `Texts`,** which is part of its equality: requests that differ only in
  their document language are no longer equal.
- **Paragraphs of only `{…}` are build markers:** in both generators, a paragraph that is nothing
  but `{.build}`, `{.build=appear}`, `{.build=fly}`, `{.build=wipe}`, `{.frag}`, `{.exit}` or
  `{.emphasize}` (or several of them) is no longer written as text, and any other paragraph of only
  classes and quoted `key="value"` pairs, such as `{.note}` or `{width="50%"}`, now throws
  `DocumentConversionException` listing the markers; so does a marker that builds nothing or stands
  inside a list, a quote or speaker notes. Other
  braces (`{"a": 1}`, `{x}`) stay text. Write `\{` where such a paragraph is meant as text.
- **Decks on the built-in master carry eight more layouts:** *Two Content*, *Picture with
  Caption*, *Picture*, *Agenda*, *Quote*, *Statement*, *Contact* and *Chart*, after the existing
  five, and the *Section Header* layout has a chapter-number placeholder. The file grows by a few
  kilobytes and PowerPoint's layout gallery lists the new layouts; the slides, their parts and the
  relationship ids of the existing layouts do not change. A caller that uses a deck of the built-in
  master as its own template, with a section layout that has no title placeholder, now gets the
  subtitle in the chapter-number placeholder: delete that placeholder, or declare its role.
- **A table cell that is exactly `[0/4]` … `[4/4]` or `[1/2]` is a Harvey ball now,** in both
  generators and every table: decks write the glyph ○ ◔ ◑ ◕ ● (in `Segoe UI Symbol`), PDFs draw it,
  instead of the text as typed. A cell with anything beside it stays text; write `\[3/4]` to keep
  such a cell as text.

### New

- **The PDF converter reads a page's drawn shapes** with PdfPig: table rules (full grids, merged
  cells, tables ruled only horizontally), drawn bullets and task boxes, code backgrounds. A page whose
  analysis fails falls back to plain paragraphs. The documentation lists what it reads and its limits.

- **Generation notes:** `GenerationResult` has the number of slides or pages written and
  `Notes`: `GenerationNote(Code, Line, Message)` for what the generator did to the Markdown that its
  writer cannot see: `slide-split`, `slide-shrunk` (at the smallest size), `slide-overfull`,
  `image-enlarged` (more than 2×), `code-not-drawn`, `html-as-text` and, in PDFs,
  `character-replaced`. The codes are constants in `NoteCodes`. Pass the notes back to the writer.
- **`CodeBlockRequest.Report`** lets a renderer pass such a note on; `MermaidRenderer` reports
  diagram types it does not draw, and syntax errors with `OnError = KeepCode`.
- **The authoring guides** have a section *Notes you may get back*, with each code of the format
  and what to change in the Markdown.
- **Footnotes** (`[^1]`): a raised number; in decks the note goes to the foot of the slide that
  refers to it (numbered per slide, the content area shortened to leave room), in PDFs to the end
  under "Notes". **Task lists** (`- [ ]`, `- [x]`): a box for the bullet. **Definition lists**:
  rows on slides, a bold term with an indented description in PDFs.
- **Document language:** front matter `lang` and the options `Language` and `Texts` pick the
  `GeneratedTexts` (alert titles, the footnotes heading), English and German built in, and tag the
  output. Unknown languages fall back to English with the note `language-unknown`.
- **New notes:** `footnote-missing` and `language-unknown`.
- **`DrawingShape.FillRule`**: `NonZero` (default) or `EvenOdd`, written to SVG and PDF; the
  Mermaid reader takes `fill-rule` from the SVG.
- **Speaker notes in decks:** `::: notes` … `:::` under a heading puts its paragraphs and lists on
  the notes page of every slide the section produces, split parts included, without changing the
  slide. Templates without a notes master get a built-in one; decks without speaker notes are
  unchanged. PDFs leave speaker notes out.
- **Transitions in decks:** front matter `transition`, `PptxOptions.Transition` (`SlideTransition`)
  and a heading's `transition="…"`: `none`, `fade`, `push`, `wipe`, `split`, `cover`, `uncover`,
  `zoom`, `cut` and `morph` (with a fade for viewers without Morph). PDFs accept and ignore them.
- **New notes:** `notes-dropped` (speaker notes before the first heading) and `notes-simplified`
  (a table, code block, image or quote in speaker notes, written as plain text).
- **Chart source:** `source: Annual report` in a `chart` block is shown under the chart as
  "Source: Annual report" ("Quelle: …" with `lang: de`): a text box under the native chart in decks,
  a line under the legend in PDFs. `ChartSpec.Source`, `GeneratedTexts.Source`, and
  `CodeBlockRequest.Texts` for renderers that write words of their own. Charts without a source are
  unchanged.
- **CSV with semicolons:** a `chart` block whose header row is separated by `;` is split at `;`
  throughout and reads numbers with a decimal comma (`12,5`), as spreadsheets in German locales
  export them; `1.234,5` is an error asking to remove the thousands separator. `decimal: comma|point`
  overrides the mark. CSV with commas reads as before.
- **Image attributes:** `![Team](team.jpg){fit=cover focus=top scale=0.8 .round .grey .caption}`.
  Decks crop with `a:srcRect` (`fit=cover` fills the image's box), draw `.round` as an ellipse and
  `.grey` as PowerPoint's greyscale effect; PDFs crop with clipping paths (`fit=cover` only in rows
  of images), draw `.round` as a circular clip and make `.grey` pixels grey.
- **New note:** `attribute-ignored`, for an image attribute that has no effect where it is.
- **Builds in decks:** a build marker paragraph before a block makes it appear (`{.build}`,
  `{.build=fly}`, `{.build=wipe}`: item by item; `{.frag}`: at once), disappear (`{.exit}`) or pulse
  (`{.emphasize}`) on a click, as PowerPoint's own animations: lists by item, cards, rows, columns
  and image rows one by one. Front matter `build: lists` and `PptxOptions.Build` (`DeckBuild`) build
  every top-level list item by item. Decks without markers are unchanged; PDFs leave the markers
  out and accept `build`.
- **QR codes:** `![Survey](qr:https://example.com/survey "caption")` draws a QR code for the text
  after `qr:` (byte mode, error correction M, at most 1 000 bytes) as vectors: two editable shapes in
  decks, two paths in PDFs, in the brand's primary colour (or a darker fallback) on white, with the
  alt text under it for `caption`; `scale=` multiplies its size.
- **Icons:** `:icon-name:` draws an icon of the Tabler Icons outline set (3.52.0, 5 491 icons, MIT),
  embedded in `wwosik.DocLibs.DocumentGeneration` (`TablerIcons.Outline`). Decks draw it as a card's
  figure and in icon lists (a list whose every item starts with an icon); PDFs draw it inline in text,
  in place of a list item's bullet and alone in a paragraph. `IIconSet` and the options
  `PptxOptions.Icons` / `PdfOptions.Icons` replace the set with your own, or switch icons off (`null`).
- **New note:** `icon-not-drawn`, for an icon where the format cannot draw it.
- **`tools/icon-import`** (`run.sh icon-import`) re-imports the Tabler set and checks its size budget.
- **Timelines and Gantt charts:** `::: timeline` around a list of 2–8 `- **2026-03** Title: description`
  entries draws a horizontal timeline, evenly spaced in date order, entries in the past grey and the
  next one ringed; `::: gantt` around a table of 1–20 rows (task, start, end by position; no end: a
  milestone) draws bars on a week, month or quarter grid (`grid=`, else chosen from the span) with a
  line for today. Dates are `2026`, `2026-Q2`, `2026-03` or `2026-03-15`. Today comes from the
  options' `TimeProvider`, or `today=2026-11-01` on the container pins it. Editable shapes on a flow
  slide in decks (light on dark slides), vectors in PDFs. Both containers used to be an "Unknown
  container" error, so no working document changes.
- **`GeneratedTexts` has `MonthNames`, `DayFormat`, `MonthFormat`, `QuarterFormat` and
  `WeekFormat`** for those dates, English ("15 Mar 2026", "W12") and German ("15. März 2026",
  "KW 12"). A host's own `GeneratedTexts` built from `English` with `with { … }` gets the English
  month names.
- **Template roles for decks** (`PptxTemplateRoles`, on `PptxBrandTemplate.Roles` and
  `CustomTemplateOptions.Roles`, and under `pptx` in `brand.json`): layout roles (two content,
  text and picture, picture, agenda, quote, statement, contact, chart, two charts, dark) found by
  declared name, else by OOXML type where there is one; placeholder roles by shape name (subtitle,
  byline, chapter number, body, picture, chart title, unit and source, decorative), with
  decorative placeholders never filled; text levels that put `###`, paragraphs and list items on
  the template's outline levels; a footer pattern with `{footer}`, `{title}`, `{chapter}` and
  `{author}`; and `ContentArea = Guides`, which places content inside the master's drawing guides.
  Front matter `layouts.twoContent` … `layouts.dark` names the layout roles of caller templates. A
  declared dark layout takes `{.dark}` slides. Templates that declare nothing are used as before.
- **The deck authoring guide** lists the new front-matter keys for caller templates and, for a
  brand's own template, the layouts of its roles and what text levels make of `###`.
- **Steps and risk matrices:** `::: steps` around a list of 2–7 `- **Title** – description` draws a
  row of chevrons; `{.current}` at the end of one item marks it, the steps before it done, later ones
  outlined, and without it all are alike. `::: riskmatrix` around a table of 1–15 rows (risk,
  likelihood and impact from 1 to 5, by position) draws a 5 × 5 grid, likelihood up and impact
  right, cells tinted by score (1–4 success, 5–9 warning, 10–25 danger colour), each risk a numbered
  dot with a legend beside the grid. Editable shapes on a flow slide in decks, vectors in PDFs. Both
  containers used to be an "Unknown container" error.
- **Harvey balls:** `[0/4]` to `[4/4]` and `[1/2]` alone in a table cell are circles filled by
  quarters (see Breaking changes).
- **`GeneratedTexts` has `Likelihood` and `Impact`,** the risk matrix's axis titles (German
  `Eintrittswahrscheinlichkeit`, `Auswirkung`).

## 2610.28.22

### Breaking changes

- **A drawing may hold a third kind of item,** `DrawingHotspot` (an invisible hit area that carries
  hover text). Code that switches over `DrawingItem` needs a case for it; skipping it is right for
  anything but SVG.
- **`DocumentFormat` has a new member, `Svg`,** reported by SVG conversion errors. Code that switches
  over `DocumentFormat` without a default needs a case for it.

### New

- **Financial charts** in wwosik.DocLibs.DocumentGeneration.Charts: development, growth by row,
  actual vs reference, growth trend, two-year monthly and a summary table, from typed records
  (`FinancialCharts.Draw`, `ToSvg`, `CategoryColors`) or JSON in a `financial-chart` block. Neutral
  words in the API, every printed label an option (`FinancialChartOptions.Labels`), numbers in the
  caller's culture, scale and unit, verdict colours with a configurable tolerance.
- **Chart colours in brands:** `BrandColors.Chart` (brand pack key `colors.chart`), every role
  optional and derived from the brand; dark variants on dark slides.
- **SVG from drawings:** `DrawingSvg.Write` writes any `Drawing` as a standalone SVG, with hover
  tooltips that need no script. Drawings gained `DrawingItem.Hover`, `DrawingHotspot` and a colour per
  span (`DrawingSpan.Color`).
- **New package wwosik.DocLibs.DocumentConversions.Svg:** `SvgConverter.ToPng` turns any SVG into a
  PNG (SkiaSharp and Svg.Skia; nothing outside the SVG is ever loaded), and `SvgImageConverter`
  lets decks and PDFs place `.svg` images.
- **Image converters:** `PptxOptions.ImageConverters` and `PdfOptions.ImageConverters` take
  `IImageConverter`s for images in formats the generators cannot place themselves. Decks keep a
  converted SVG beside its PNG, for PowerPoint to draw as a vector.

## 2610.25.18

### Breaking changes

- **Images in decks get bigger.** They fill the room they have, enlarged up to
  `PptxOptions.MaxImageUpscale` (default 4) times their natural size, no longer only shrunk.
  `"scale=1"` in an image's title, or `MaxImageUpscale = 1`, keeps the old size. Drawings (diagrams)
  are enlarged up to 1.5 times.
- **Two or more images in one paragraph share a slide,** side by side; before, each got a slide of
  its own. Put them in separate paragraphs to keep that.
- **Overfull slides change:** text, tables and layout slides that do not fit are now set smaller and,
  if still too long, continued on further slides with the same title. Slides that fit are unchanged.
- **Table columns and row heights in decks change** where the old estimate was off: every column is
  at least as wide as its longest word, and the font steps down before words break.
- **Decks on brand templates with dark slides change colour:** everything the generator draws is
  white on translucent fills. `PptxBrandTemplate.Tone = SlideTone.Light` keeps the old colours.
- **Decks on a brand's own template get its footer text** in the footer placeholder.
  `BrandTemplateOptions.ShowFooter = false` or front matter `showFooter: false` turns it off.
- **Markdown that generated before can now fail or look different:** `layout="…"` and `.closing` are
  heading attributes (also on `#` headings, where braces were text before); `::: columns` is a
  container; `[words]{.success}`-shaped spans are coloured, and one with an unknown category throws.
- **`ICodeBlockRenderer` may return a third graphic,** `ChartGraphic`. Code that switches over
  `CodeBlockGraphic` needs a case for it.

### New

- **Dark templates:** `PptxBrandTemplate.Tone` (`Auto` reads the template), a dark palette with a
  contrast ratio of at least 4.5 for every text, and diagrams drawn for dark slides.
- **Slide colours:** `Brand.SlideColors` (brand pack key `slideColors`) — colours for decks where they
  differ from PDFs.
- **Images side by side** from a paragraph of images, with captions on request (`"caption"` in the
  title), in decks and PDFs; **`::: columns`** for text beside an image or two charts side by side.
- **Fitting:** shrinking and continuation slides; `PptxOptions.ContinuationSuffix`.
- **Brand templates:** footer text, `layout="Name"` per slide, a closing slide from
  `# … {.closing}` (`PptxBrandTemplate.ClosingLayout`, `PptxLayoutNames.Closing`, front matter
  `layouts.closing`), and `PptxOptions.ListBullets` / front matter `listBullets: always` for bullets
  on level 1.
- **Coloured words:** `[words]{.success}`, `.info`, `.warning`, `.danger`, `.neutral`, in decks and
  PDFs.
- **New package `wwosik.DocLibs.DocumentGeneration.Charts`:** `ChartRenderer` turns `chart` blocks
  into native, editable bar, line and area charts in decks and vector charts in PDFs.
- **Fixed:** a table cell that is all `==marked==` no longer squeezes its column.

## 2610.25.13

### Breaking changes

- **Markdown that generated before can now fail.** These are now syntax, and an unknown name is an
  error (`DocumentConversionException`, naming the valid ones), in decks and in PDFs:
  - `::: name` blocks;
  - a trailing `{.class}` or `{key="value"}` on a `##` heading;
  - `> [!KIND]` alerts;
  - front matter keys `labels.<category>`.

  `==text==` is now marked text in decks and bold in PDFs.
- **Code-block renderer errors are `DocumentConversionException`.** A renderer's exception (e.g.
  `MermaidRenderer` on a diagram it cannot draw) reaches the caller of either generator wrapped, with
  the same message and the original as `InnerException`. Catch `DocumentConversionException` instead
  of `ArgumentException`.
- **Decks embed the brand's fonts** by default, for brands with fonts: every family the deck uses,
  whole faces. Decks grow by the size of those faces (roughly 0.1–0.5 MB each). Switch it off with
  `BrandTemplateOptions.EmbedFonts = false` or the front matter key `embedFonts: false`. Brands
  without fonts, such as `Brand.Neutral`, are unchanged.
- **`PptxOptions.CodeFont` is `string?`** and defaults to `null`: the brand's mono family, else
  Consolas. Decks of a brand with a mono face therefore set code in it. Set `CodeFont = "Consolas"`
  to keep the old font.
- **Brand font family names** are read from name ID 1 first, the name PowerPoint finds a font by;
  before, name ID 16 came first. A brand whose fonts have a different typographic family name (a
  condensed or light cut) now names a different family in decks. Set `BrandFonts.Family` or
  `BrandTableFonts.Family` to keep the old name.

### New

- **Slide layouts from Markdown:** a kicker and dark slides (`## Title {.dark kicker="…"}`);
  `::: cards`, `rows`, `panel`, `panel dark` and `statement`; alerts; tags and labelled tags;
  highlighted table rows and `==marked==` text; status labels coloured by front matter
  `labels.<category>`. New `BrandColors.Highlight` and `BrandColors.Labels` (`BrandLabelColors`),
  with brand pack keys `colors.highlight` and `colors.labels`. PDFs accept the same Markdown and show
  it plainly.
- **Font embedding in decks**, for any brand with fonts; faces whose licence restricts embedding
  (`OS/2 fsType`) are left out.
- **Authoring guides:** `PptxGenerator.AuthoringGuide(options)` and `PdfGenerator.AuthoringGuide(options)`
  return Markdown for whoever writes the documents, following the options: template names, front
  matter keys, diagrams, and the characters the brand's fonts lack. Renderers add their section
  through `ICodeBlockRenderer.AuthoringGuide`, a default interface member, so existing renderers
  need no change.
- Every package carries its own documentation as `README.md`, and this changelog as `CHANGELOG.md`.

## 2610.22.18 and 2610.22.18.1

The first releases: conversions to Markdown, Markdown ↔ JIRA wiki markup, the PPTX and PDF
generators, brands and brand packs, table fonts, Mermaid diagrams, and keeping headings with their
content in PDFs. There are no changelog entries for them; see `spec/` in the repository.
