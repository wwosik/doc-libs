# E09-S01 DOCX generator

> Status: todo
> Epic: [E09 DOCX generator](E09-docx-generator.md)

## Summary

A new package, `wwosik.DocLibs.DocumentGeneration.Docx`, writes Markdown to Word (`.docx`). It works
like the PPTX generator:

- a built-in template is written in code from a `ResolvedBrand`;
- a caller's `.dotx`/`.docx` can be used with a role → style-name map;
- a brand pack can carry its own `.dotx`.

The document gets a cover, header and footer, core properties and an optional table of contents.
The content covers ordinary Markdown: numbered headings, lists, tables, images, code, quotes, real
Word footnotes, links, task and definition lists, captions and the document language. Deck syntax
(E09-S02) and charts and drawings (E09-S03) build on it.

## Context

- **Generator packages.** Each generator is its own package on the shared
  `wwosik.DocLibs.DocumentGeneration` (`ai/memory.md`, *Decisions*).
  - `wwosik.DocLibs.DocumentGeneration.Pptx` references `DocumentFormat.OpenXml` 3.5.1 and
    Markdig.
  - The shared package references the main package and Markdig, and must not take on the Open XML
    SDK, because Charts and Diagrams depend on it.
  - `DocumentFormat.Docx` already exists. The converter uses it, so exceptions can carry it with no
    change to the enum.
- **The PPTX generator is the model** (`PptxGenerator.Generate`):
  - `FrontMatter.Read` → `PptxTemplateOptions.Apply` (brand registry, template keys) →
    `template.Write(buffer)` → open the package → parse → write → `WriteCoreProperties`, which
    writes `docProps/core.xml` by hand: title, creator, language.
  - Package errors become `DocumentConversionException` ("The template is damaged").
  - It returns `notes.Result(count)`.
  - Template options are one record per template kind:
    - `BrandTemplateOptions`: the built-in master, or the brand's own `PptxBrandTemplate`;
    - `CustomTemplateOptions(Stream)` with `PptxLayoutNames`.
    - Each has static `Keys` and a `With` that applies front matter (`layouts.*` on custom
      templates).
  - `TemplateBuilder.Write(output, ResolvedBrand, showTagline)` writes the master as XML strings.
- **The brand API** (`Brand.cs`):
  - `Brand` has a `PptxBrandTemplate? Pptx` (bytes plus layout names, equality by content).
  - `BrandPack` reads `brand.json` through the source-generated `BrandManifestContext`, which
    rejects unknown keys, and maps the `pptx` section to `PptxBrandTemplate`.
  - Generators only see `ResolvedBrand`. It has `Primary`, `Link`, `Footer`, `TableHeader`,
    `TableBand`, `TableLine`, `FooterText`, `Logo`, `CoverArtwork`, `Tagline` and `ResolvedFonts`
    (`Family`, `TableFamily`, `MonoFamily`, the face bytes).
- **The PDF generator is the closest to a flowing document** (`MarkdownParser`, `DocumentModel`).
  - The first `#` becomes the cover, with the paragraphs after it as subtitle. Every other `#`
    starts a new page.
  - Headings keep their Markdown level. Lists, definition lists, tasks (`ListBlock.Tasks`), code,
    quotes, tables (alignments, header row) and image rows are separate blocks.
  - The slide syntax is checked through `LayoutSyntax` and `HeadingAttributes.Split`, then shown
    plainly.
  - Images are fitted by `ImageInfo` and `"scale=N"` in the title, and converted through
    `ImageConverters.Convert` when not PNG/JPEG.
  - Column widths: each column gets at least its longest word, and the rest is shared in
    proportion to the content.
- **Text widths are estimated by `TextMetrics`** (internal to Pptx; it takes the Pptx `TextRun`).
- **Shared Markdown pieces:**
  - `MarkdownSyntax.UseDocumentSyntax()` turns on footnotes, task lists and definition lists, and
    `MarkdownSyntax.ReportMissingFootnotes` reports undefined references.
  - `DocumentLanguage.Resolve` resolves the document language, and `GeneratedTexts` holds the
    alert titles and `Footnotes` in English and German, through `Texts` (E06-S02).
  - `ColouredWords` handles coloured words, and `FrontMatter.Labels()` the status labels.
- **Notes.** `GenerationResult` and `NoteCodes.All` list, for each code, the formats it occurs in
  and the writer's advice. A test fails if a code lacks its docs row (E05-S03).
- **Authoring guides.** `AuthoringGuides.Template/Fill/FrontMatter/Characters/Notes` fill an
  embedded `AuthoringGuide/<Format>.md` (E05-S02). Expected guides are compared in tests.
- **The DOCX converter.** `DocumentConversions.OpenXml.DocxConverter` already reads `.docx` back to
  Markdown:
  - headings by style name or id (`ClassifyHeading`: "heading N", "Title");
  - lists by `w:numPr` and the numbering's format;
  - tables, runs with bold, italic and strike, hyperlinks, and images (`a:blip`).
  - It reads no footnotes and no code style yet (the fork's analysis puts those in E08).
- **Test samples.** The generator tests already have `report.md`, `long.md` and `layouts.md` and
  compare round trips through the converters (`*.roundtrip.expected.md`). The made-up brand is
  `Samples/brand-sample`.
- **Decided by the human** (analysis, 2026-11-13):
  - a DOCX generator is wanted;
  - its built-in template is written in code from a `ResolvedBrand`, and caller templates come
    with a role → style-name map;
  - history, compare, comments, review import, cross-references, abbreviations, accessibility and
    consulting containers are not wanted;
  - paragraphs reference template styles by name only, and there is no direct formatting except
    in code (epic E09).
- **Shared with E03-S02** (planned in parallel):
  - captions "Figure n" / "Table n" with numbering;
  - a TOC with page numbers;
  - heading numbers;
  - justified text, page breaks and classification marking.
  - E03-S02 agrees the syntax and key names. This plan assumes:
    - captions are a `Table: …` paragraph right after a table, and the title of an image
      (`![alt](x.png "Caption")`, without its `scale=…`);
    - the keys are `toc: true` and `headingNumbers: true|false`.
  - Page breaks and classification are written for Word in E09-S02.

## Proposed approach

1. **Package and project.**
   - **Projects.** `src/wwosik.DocLibs.DocumentGeneration.Docx` and its tests,
     `src/wwosik.DocLibs.DocumentGeneration.Docx.Tests`, both added to `wwosik.DocLibs.slnx`.
   - **References.** `DocumentFormat.OpenXml`, Markdig and the shared package. No new package
     versions are needed.
   - **Description:** "Generates Word documents (.docx) from Markdown, on a built-in template in a
     brand's design or on a caller's template, with numbered headings, real footnotes and a table
     of contents."
   - **Resources.** `AuthoringGuide\*.md` is embedded as in Pptx, and `InternalsVisibleTo` points
     to the tests.
   - **Docs.**
     - `docs/wwosik.DocLibs.DocumentGeneration.Docx.md` is the package README, packed by
       `Directory.Build.props`.
     - The package is listed in `docs/README.md` and in the root `README.md`.
2. **Shared code.**
   - **`TextMetrics`** moves to the shared package. It works on `(string Text, bool Bold)` pieces
     instead of the Pptx `TextRun`, and Pptx calls it through a thin adapter. Decks must not
     change, and their tests prove it.
   - **Open XML helpers** that both generators need live in `src/Shared/OpenXml/` and are linked
     into both projects as internal source (`<Compile Include=… Link=…>`):
     - writing core properties;
     - the SVG blip extension (E09-S03);
     - the `ChartWriter` part (E09-S03).
   - See question 4.
3. **API** (all public, documented, records with `init` like the PPTX ones):
   - **`DocxGenerator.Generate(MarkdownDocument, Stream, DocxOptions?)`** returns a
     `GenerationResult`. `Pages` is 0, because Word lays out the pages itself; the docs say so.
   - **`DocxGenerator.AuthoringGuide(DocxOptions?)`** (step 12).
   - **`DocxOptions`** mirrors `PdfOptions`: `Template`, `Brands`, `CodeBlockRenderers`,
     `ImageConverters`, `CodeFont`, `Language`, `Texts` and `TimeProvider`.
   - **`DocxTemplateOptions`** (abstract, internal `Name`/`Keys`/`With`/`Write`) has two kinds:
     - **`BrandDocxTemplateOptions`:**
       - `Brand` (default `Brand.Neutral`);
       - `ShowDate`, `ShowTagline` and `EmbedFonts` (all default `true`);
       - `PageSize` (`DocxPageSize.A4|Letter|A3`) and `Orientation` (`DocxOrientation`).
       - With a brand that has its own `Docx` template, it is built on that template.
       - Keys: `showDate`, `showTagline`, `embedFonts`; on a brand's own template only
         `embedFonts`.
     - **`CustomDocxTemplateOptions(Stream Template)`** with `Styles` (`DocxStyleNames`).
       - Keys: `styles.<role>` (`styles.heading1`, `styles.quote`, …), as `layouts.*` works for
         decks.
   - **`DocxStyleNames`**: one property per role, each the style name as Word shows it, `null`
     for the default name. The roles:
     - `Title`, `Subtitle`, `CoverDetails`, `TocHeading`;
     - `Heading1`–`Heading6`, `Normal`;
     - `ListBullet`, `ListNumber`, `ListTask`;
     - `DefinitionTerm`, `Definition`;
     - `Quote`, `Code`, `CodeCharacter`;
     - `Caption`, `TableTitle`, `Table` (a table style);
     - `FootnoteText`, `FootnoteReference`, `Hyperlink`.
     - E09-S02 adds its roles to the same record.
   - **Brand.**
     - `Brand.Docx` holds a `DocxBrandTemplate(ReadOnlyMemory<byte> Content)` with `Styles`
       (`DocxStyleNames`), compared by content like `PptxBrandTemplate`.
     - `brand.json` gets a `docx` section: `{ "template": "template.dotx", "styles": { "heading1":
       "…" } }`. It is read through `BrandManifestContext` with typed properties, so typos are
       still rejected.
4. **Built-in template** (`DocxTemplateBuilder.Write(output, ResolvedBrand, options)`), written as
   XML strings like `TemplateBuilder`:
   - **Parts.** `styles.xml`, `numbering.xml`, `theme1.xml`, `settings.xml`, `fontTable.xml`,
     `footnotes.xml` with separators, and the header and footer parts.
   - **Theme.** Colours from `ThemeColors`. The major and minor font is the brand family, else
     Arial (as in decks), and the code font is the mono family, else Consolas.
   - **Styles** follow the PDF's sizes and colours, so a document looks related in both formats:
     - body 10.5 pt;
     - headings 20 / 16 / 13.5 / 12 / 11 / 10.5 pt, bold, in `Primary`, with `keepNext`;
     - a link character style in `Link`;
     - `Quote` grey with a left border, and `Code` in the mono family, 9 pt, on light grey;
     - `Caption`, footnote styles, and the TOC styles `TOC 1`–`TOC 3`.
   - **Table style** `Doc Table`: header row bold white on `TableHeader`, banded rows in
     `TableBand`, grid lines in `TableLine`, 9 pt, in the brand's table family if it has one.
   - **Numbering.** One multilevel list is linked to Heading 1–3 (`1`, `1.1`, `1.1.1`). There is a
     bullet definition (`•`, `–`, `▪`) and an ordered one (`1.`, `a.`, `i.`), three levels each.
   - **Page.** The size from the options, margins 20 mm left and right and 25 mm top and bottom.
     `w:titlePg` gives the cover its own first-page header and footer.
   - **Header and footer.**
     - On content pages: the document title left and the logo right in the header; the footer
       text, the date (`ShowDate`, `dd.MM.yyyy`) and `PAGE / NUMPAGES` fields in the footer, as in
       the PDF.
     - On the cover: the logo top right, the cover artwork anchored across the bottom (at most
       42 % of the page height), and the tagline bottom right (`ShowTagline`).
   - **Fonts.** With `EmbedFonts`, brand faces are embedded the Word way: obfuscated `.odttf`
       font parts, `w:embedRegular`/`Bold`/`Italic`/`BoldItalic` in the font table, and
       `w:embedTrueTypeFonts`. Faces whose `fsType` restricts embedding are skipped; that check is
       shared with `FontEmbedding`. See question 7.
   - Compatibility mode 15 (Word 2013+).
5. **Caller and brand templates** (`.dotx` or `.docx`):
   - **Opening.** The template is opened as a document and turned into one
     (`ChangeDocumentType(Document)`).
   - **Kept:** its styles, numbering, theme, settings, headers and footers.
   - **The body** is emptied except for the final `w:sectPr` (page setup, header and footer
     references).
   - **Styles are looked up by name** (`w:name`, case-insensitive), never by id: localized
     templates have other ids. The paragraph then references the id found.
   - **A missing style:**
     - a role whose style the template lacks gets a style created from the built-in definition,
       based on the template's `Normal`;
     - a name the caller mapped that does not exist throws `DocumentConversionException`, listing
       the template's paragraph styles. See question 6.
   - **Numbering in caller templates:**
     - headings keep whatever numbering the template's heading styles have;
     - lists use the numbering of the template's list styles;
     - where a list style has none, the generator adds its own definitions.
   - **Placeholders in headers and footers.** Content controls bound to the core properties (title,
     author) get their cached text set, so they show the values before Word refreshes them.
     `TITLE`/`AUTHOR` fields are refreshed by `w:updateFields`, the same setting as the TOC.
6. **Cover and properties.**
   - **The cover** comes from the first `#` and the paragraphs after it, as in the PDF:
     - the title in `Title`;
     - each paragraph in `Subtitle`;
     - the author (front matter `author`, else the document's metadata) and the date in
       `CoverDetails`.
     - The content starts on the next page. A document that does not start with `#` has no cover.
   - **Core properties.** `dc:title` (front matter `title`, else the cover title), `dc:creator`,
     `dc:language`, and `dcterms:created`/`modified` from `TimeProvider`. They are written by the
     shared helper of step 2.
7. **Table of contents.**
   - **Where.** With `toc: true` (key as agreed in E03-S02; off by default), the TOC goes after the
     cover: a `TocHeading` paragraph ("Contents" / "Inhalt", a new `GeneratedTexts.Contents`),
     then a `TOC \o "1-3" \h \z \u` field.
   - **Pre-filled.** The field holds one hyperlinked entry per Heading 1–3, without page numbers, so
     a viewer that does not update fields still shows the headings.
   - **Updating.** `w:updateFields` in the settings makes Word fill in the page numbers on opening.
     See question 5.
8. **Content** (a parser of its own on the shared pipeline, modelled on `MarkdownParser`, writing
   Open XML with the SDK's typed classes):
   - **Headings.**
     - Markdown levels map to Heading 1–6. The highest level used after the cover becomes Heading 1
       (question 2).
     - Heading 1–3 are numbered and in the TOC; Heading 4–6 are unnumbered.
     - A heading from a later `#` starts a new page (`w:pageBreakBefore`), as in the PDF.
     - Heading attributes are checked and dropped through `HeadingAttributes.Split`, as in the PDF.
     - Every heading gets a bookmark named from its Markdig id, made Word-safe: letters, digits and
       `_`, at most 40 characters, unique. `[x](#id)` links jump to it.
     - Heading numbers can be switched off with the E03-S02 key (`headingNumbers: false`; on by
       default, question 3).
   - **Paragraphs.**
     - Paragraphs use `Normal`; hard breaks become `w:br`.
     - Inline styles: `**bold**`, `*italic*` and `~~strike~~` become `w:b`, `w:i` and `w:strike`
       on the run, as Ctrl+B does in Word. This is the one kind of run formatting besides code.
     - Inline code uses `CodeCharacter`.
     - Raw HTML stays text, with `html-as-text`.
   - **Lists.**
     - Bulleted and numbered lists to three levels use `ListBullet` and `ListNumber`, with `ilvl`
       0–2.
     - Each ordered list is a new `w:num` with a `w:startOverride` from its Markdown start, so it
       restarts.
     - Deeper items stay at level 3 and give a new note, `list-too-deep` (question 10).
     - Paragraphs inside an item continue it with the list indent and no marker.
   - **Task lists.** `- [ ]` and `- [x]` get a Word check-box content control (`w14:checkbox`,
     ☐/☒) in place of the bullet (question 9).
   - **Definition lists.** Each term is a `DefinitionTerm` paragraph (bold, `keepNext`), and each
     description a `Definition` paragraph (indented).
   - **Tables.**
     - A table spans the text width (`w:tblW` 100 %, fixed layout) and uses the `Table` style.
     - The header row repeats (`w:tblHeader`).
     - Column widths come from `TextMetrics`, by the PDF's rule: at least the longest word, the rest
       in proportion to the content. They are written to `w:tblGrid` and `w:tcW`.
     - Alignment from the Markdown becomes `w:jc` in the cell paragraphs.
     - Rows do not break across pages (`w:cantSplit`) unless they are taller than a page.
   - **Table title.** A paragraph of only bold text right before a table becomes a `TableTitle`
     paragraph with `keepNext` (question 8).
   - **Captions.**
     - A `Table: …` paragraph after a table, and an image's title, become `Caption` paragraphs:
       "Table " or "Figure " (`GeneratedTexts.Table` / `Figure`), then a `SEQ Table \* ARABIC` or
       `SEQ Figure \* ARABIC` field, then ": " and the text.
     - The field's result is filled in with the number, so it is right without an update.
     - Placement, above or below, is as E03-S02 decides for the PDF. Proposed there: table captions
       above, figure captions below. The caption keeps with its table or image (`keepNext`).
   - **Images.**
     - PNG, JPEG and GIF are placed inline in a centred paragraph, at their natural size at 96 DPI
       times `scale=`.
     - They are scaled down to the text width and 80 % of the text height, never up.
     - Alt text goes into `wp:docPr/@descr`.
     - Other formats go through `ImageConverters`, as in the PDF. The SVG kept beside them comes in
       E09-S03.
   - **Code blocks** without a renderer become one `Code` paragraph per line, with tabs as four
     spaces. Code is the only place with direct formatting: none is needed beyond the style.
   - **Quotes.** Quotes use `Quote`, and nested quotes are flattened into it.
   - **Rules.** `---` becomes an empty paragraph with a bottom border, through a `Rule` style.
   - **Footnotes.**
     - They are real Word footnotes: `w:footnoteReference` runs in `FootnoteReference`, with the
       text in the footnotes part in `FootnoteText`.
     - Word numbers them. A missing definition gives `footnote-missing`, as in the other
       generators.
   - **Links.**
     - Absolute URLs become `w:hyperlink` with an external relationship in `Hyperlink`.
     - `#id` links go to the heading's bookmark (`w:anchor`).
     - Anything else stays text.
   - **Language.**
     - The tag from `DocumentLanguage.Resolve` goes into `w:lang` in the document defaults
       (`w:rPrDefault`) and `w:themeFontLang`, so Word spell-checks in that language.
     - On caller templates the same defaults are set, and their own style-level languages are left
       alone.
   - **Deck syntax until E09-S02.**
     - Containers, alerts, `==mark==`, labels, coloured words and image rows are checked by
       `LayoutSyntax` and `ColouredWords`, as in the PDF, and shown plainly:
       - container content in sequence;
       - alerts as `Quote` with the alert title in bold;
       - marks bold, labels as code, coloured words bold;
       - image rows as images one after the other.
     - So `layouts.md` already generates.
9. **Notes.**
   - Docx is added to the formats of `code-not-drawn`, `html-as-text`, `footnote-missing` and
     `language-unknown` in `NoteCodes.All`.
   - The new `list-too-deep` gets an entry and a docs row.
10. **Errors.** `DocumentConversionException` with `DocumentFormat.Docx` is thrown for:
    - a damaged template;
    - a mapped style that does not exist;
    - a missing or unconvertible image;
    - invalid front matter or an unknown brand;
    - invalid slide syntax.

    An invalid brand throws `ArgumentException`, as elsewhere.
11. **Tests** (`Docx.Tests`, xUnit):
    - **Samples.** `report.md`, `long.md` and `layouts.md` are linked from the PDF tests'
      `Samples/`, and `brand-sample` as in the PPTX tests.
    - **Validation.** Each sample is generated with the neutral brand, the sample brand and a
      custom template built at test time from the built-in one with renamed styles (like
      `DarkTemplate`). Each output passes `OpenXmlValidator(FileFormatVersions.Microsoft365)` with
      no errors.
    - **Round trip.** Each output goes through the existing `DocxConverter` and is compared with
      `<sample>-<template>.roundtrip.expected.md` (`DOCLIBS_UPDATE_EXPECTED=1` rewrites them).
      Footnotes and code style are lost in the round trip until E08; the expected files show it.
    - **Unit tests:**
      - style lookup by name with localized ids;
      - a missing mapped style throws, and a missing role style is created;
      - heading level shift and page breaks at later `#`;
      - list restart (two ordered lists both start at 1, `3.` starts at 3) and `list-too-deep`;
      - table widths: the longest word fits, and the sum is the text width;
      - the header row repeats, and captions have their `SEQ` numbers;
      - footnote ids and part, the TOC field and its entries;
      - core properties, `w:lang` for `lang: de`, and German texts;
      - an embedded font that is obfuscated and restored;
      - a restricted face is skipped;
      - front-matter keys, including `styles.*` and errors listing valid keys.
    - **For review.** Every sample is written to `.tmp/review/` for a human to open.
12. **Authoring guide, docs, changelog.**
    - **Guide.** `AuthoringGuide/Docx.md` covers structure, front matter, characters, notes and an
      example, built like `Pdf.md`, filled by `AuthoringGuides`. There are expected guides for
      neutral, sample and custom. E09-S02 and E09-S03 add their sections.
    - **Docs.**
      - The Docx page: options, templates and the role table, cover, header and footer, the
        Markdown → Word table, captions, TOC, fonts, notes, known limits.
      - The DocumentGeneration page: `Brand.Docx`, the brand pack `docx` section, the notes table.
    - **Changelog.** `CHANGELOG.md` *New*: the package. *Breaking changes*: none for existing
      callers. Decks are unchanged after the `TextMetrics` move, and tests prove it.
    - **Memory.** `ai/memory.md` gets the facts implementers need: style lookup by name, linked
      shared sources, the heading-level shift.
13. **Verification in real Word and LibreOffice.**
    - **Word.** The samples are opened in Word on Windows through COM from WSL, with a throwaway
      `.tmp/scripts/word-export.ps1` (`Documents.Open`, `Fields.Update`, `SaveAs2` as PDF), then
      looked at page by page with `pdf2png.ps1`. The check confirms:
      - no repair prompt;
      - the TOC fills;
      - numbering restarts;
      - footnotes are at the page foot;
      - embedded fonts are used.
    - **LibreOffice.** The same files are converted headless (`soffice --convert-to pdf`) where
      LibreOffice is installed. It is not installed in WSL today, so it is installed in `.tmp/` or
      the check is recorded as not done.

## Acceptance criteria

- [ ] **Package.** `wwosik.DocLibs.DocumentGeneration.Docx` packs with its docs page as README and a
  description, and its tests run in CI.
- [ ] **No change to existing output.** Decks generate byte for byte as before after `TextMetrics`
  moved.
- [ ] **Built-in template.** The neutral brand and the sample brand produce valid documents (Open
  XML validator, no errors):
  - a cover with title, subtitle, author and date, and the sample brand's logo, artwork and
    tagline;
  - a header and footer on the content pages with title, logo, footer text, date and "n / N";
  - core properties with title, author and language.
- [ ] **Caller templates.**
  - A caller `.dotx` with localized style ids and a `DocxStyleNames` map produces a document that
    uses its styles, numbering, headers and footers.
  - A mapped name that does not exist throws and lists the styles.
  - A missing role style is created.
- [ ] **Brand pack.** A brand pack with `docx.template` and `docx.styles` loads, and a typo in it
  throws `InvalidDataException` naming the key.
- [ ] **Headings.** They are numbered 1, 1.1, 1.1.1 on Heading 1–3 and unnumbered below. A document
  `# Title` / `## A` / `### B` gives "1 A" and "1.1 B". Every later `#` starts a page, and
  `headingNumbers: false` removes the numbers.
- [ ] **Lists.**
  - Lists nest to three levels.
  - Every ordered list restarts at its own start number.
  - A fourth level stays at level 3 and gives `list-too-deep`.
  - Task items show check boxes, and definition lists show a bold term and an indented description.
- [ ] **Tables.**
  - Tables span the text width, and the header row repeats on the next page in Word.
  - No column is narrower than its longest word.
  - A bold paragraph before a table is a table title.
  - `Table: …` gives "Table n: …" with a `SEQ` field, and an image title gives "Figure n: …".
- [ ] **Images.** They are fitted to the width and 80 % of the height, never enlarged, and carry
  their alt text.
- [ ] **Other content.**
  - Code is in the code style, and quotes in `Quote`.
  - Links work: external links and `#heading` links.
  - Footnotes are Word footnotes at the foot of the page.
- [ ] **Table of contents.** `toc: true` gives a TOC with the headings, which Word fills with page
  numbers when the document is opened.
- [ ] **Language.** `lang: de` sets `w:lang` `de` in the defaults and `dc:language`, and gives the
  German "Inhalt", "Tabelle" and "Abbildung".
- [ ] **Round trip.** Every sample goes through `DocxConverter` to Markdown matching the committed
  expectation: headings, lists, tables, emphasis, links and images survive.
- [ ] **Real Word.** The samples open in Word without a repair prompt and look as described, as
  checked through the Word export. LibreOffice is checked too, or recorded as not available.
- [ ] **Notes.** `GenerationResult` carries the DOCX notes, and `NoteCodes.All` lists Docx where it
  applies.
- [ ] **Docs.** The authoring guide, the docs, the CHANGELOG and `ai/memory.md` are updated, and all
  tests pass.

## Open questions

1. Should this be split into two stories: S01a (package, templates, cover, header, footer,
   properties, TOC) and S01b (content)? Proposed: split, because each half is about as big as a
   PPTX story. S01a's tests can use plain paragraphs.

> Answered by the human (2026-11-14): as proposed.

2. How do Markdown levels map to Word headings? Proposed: the highest level used after the cover
   becomes Heading 1, so `# Title` + `##` sections gives "1, 2, 3" and not "0.1, 0.2". E03-S02
   should number PDF headings by the same rule. The alternative is `#` → Heading 1 always, as the
   PDF's sizes do.

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Should headings be numbered by default in Word? Proposed: yes on the built-in template, as Word
   reports usually are, with E03-S02's `headingNumbers: false` to switch it off. On caller
   templates their own heading styles decide. The alternative is off by default, as in the PDF.

> Answered by the human (2026-11-14): as proposed.

4. Where does the Open XML code both generators need live (core properties, SVG blip, later
   `ChartWriter`)? Proposed: source files in `src/Shared/OpenXml/`, linked into Pptx and Docx as
   internal code, and `TextMetrics` moved into the shared package, which needs no Open XML.
   Alternatives:
   - a new package `DocumentGeneration.OpenXml`, a package only for internals;
   - Docx referencing the Pptx package;
   - the Open XML SDK in the shared package, which Charts and Diagrams would then pull in.

> Answered by the human (2026-11-14): as proposed.

5. How does the TOC get its page numbers? Proposed: entries pre-filled without numbers, plus
   `w:updateFields`, so Word asks once on opening to update fields. Alternatives:
   - no update setting, with the docs telling writers to press F9;
   - the TOC field alone, empty until updated.

   It has to be checked whether Word drops the setting when the document is saved.

> Answered by the human (2026-11-14): as proposed.

6. A caller template lacks a role's style. Proposed: create it from the built-in definition based
   on its `Normal`, as the fork does. Throw only when a name the caller mapped explicitly does not
   exist. The alternative is to throw for every missing style.

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Should brand fonts be embedded in Word documents? Proposed: yes by default, as in decks (`EmbedFonts`,
   `embedFonts`), as obfuscated `.odttf` parts, skipping restricted faces. The alternative is to
   name the families only.

> Answered by the human (2026-11-14): as proposed.

8. A paragraph of only bold text right before a table: should it become a table title? Proposed:
   yes in Word (`TableTitle`, kept with the table), next to E03-S02's captions. The PDF goes on
   showing it as a bold paragraph. The alternative is no table titles, captions only.

> Accepted as proposed (the human, 2026-11-14, without discussion).

9. Task-list boxes: should they be Word check-box content controls that can be clicked in Word?
   Proposed: yes, with ☐/☒ in the template's symbol font. The alternative is the plain characters
   ☐/☒ as the bullet, which other viewers show the same way.

> Accepted as proposed (the human, 2026-11-14, without discussion).

10. Lists deeper than three levels: stay at level 3 with a `list-too-deep` note (proposed, as the
    fork does), or use Word's levels 4–9 with the definitions continued?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- **Order with E03-S02.** The analysis orders E09 (8) before E03-S02 (9). The shared syntax and key
  names (`Table: …`, image title as caption, `toc`, `headingNumbers`, and the `GeneratedTexts`
  `Contents`, `Figure` and `Table`) are agreed in the E03-S02 plan. Whichever is implemented first
  adds them to the shared package; the other reuses them. If E09-S01 is first, PDF and PPTX accept
  a `Table: …` paragraph as plain text until E03-S02.
- **Out of scope** by the human's decision: cross-references, abbreviations, accessibility, history,
  compare, comments and review import. Landscape sections are also left out (they were a fork
  draft).
