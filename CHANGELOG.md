# Changelog

What changed in the wwosik.DocLibs packages, for the code that uses them, newest first.

All packages share one version, `yyMM.d.H[.r]`: the date and hour (Europe/Berlin) of the release,
plus a counter for a further release within the same hour. The version says nothing about
compatibility, so this file does. Read the sections between your version and the new one before
updating, breaking changes first.

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
