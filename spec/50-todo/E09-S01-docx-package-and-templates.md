# E09-S01 DOCX package and templates

> Status: todo
> Epic: [E09 DOCX generator](E09-docx-generator.md)

## Summary

A new package, `wwosik.DocLibs.DocumentGeneration.Docx`, writes Markdown to Word (`.docx`). It works
like the PPTX generator:

- a built-in template is written in code from a `ResolvedBrand`;
- a caller's `.dotx`/`.docx` can be used with a role → style-name map;
- a brand pack can carry its own `.dotx`.

This story builds the package and its frame: the three kinds of template, the cover, header and
footer, core properties, the table of contents field and embedded fonts, and moves the code the
generators share. The body is written plainly until
[E09-S04](E09-S04-docx-content.md) writes the content (headings and numbering, lists, tables,
captions, images, code, quotes, footnotes, links, task and definition lists, `lang`). Deck syntax
([E09-S02](E09-S02-deck-syntax-in-word.md)) and charts and drawings
([E09-S03](E09-S03-charts-drawings-and-guide.md)) build on both.

## Context

- **Split.** The first plan of E09-S01 held the package and the content together; it was split as
  its question 1 proposed. This story is the former S01a; S01b is now
  [E09-S04](E09-S04-docx-content.md).
- **Order.** E09-S04 is implemented right after this story and before E09-S02 and E09-S03, despite
  its number: story IDs are allocated as the next free number.
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
  The first `#` becomes the cover, with the paragraphs after it as subtitle. Every other `#`
  starts a new page.
- **Text widths are estimated by `TextMetrics`** (internal to Pptx; it takes the Pptx `TextRun`).
  E09-S04 needs it for table column widths.
- **Shared Markdown pieces:**
  - `DocumentLanguage.Resolve` resolves the document language, and `GeneratedTexts` holds the
    generators' texts in English and German, through `Texts` (E06-S02).
  - `MarkdownSyntax.UseDocumentSyntax()` and the rest of the shared content pieces are used by
    E09-S04.
- **Notes.** `GenerationResult` and `NoteCodes.All` list, for each code, the formats it occurs in
  and the writer's advice. A test fails if a code lacks its docs row (E05-S03).
- **Authoring guides.** `AuthoringGuides.Template/Fill/FrontMatter/Characters/Notes` fill an
  embedded `AuthoringGuide/<Format>.md` (E05-S02). Expected guides are compared in tests.
- **The DOCX converter.** `DocumentConversions.OpenXml.DocxConverter` already reads `.docx` back to
  Markdown: headings by style name or id (`ClassifyHeading`: "heading N", "Title"), paragraphs,
  runs, tables, lists, hyperlinks and images.
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
- **Agreed in E03-S02** (the shared keys this story reads):
  - `toc: true|false` (default `false`) and `tocDepth: 1|2|3` (default `2`), with the host
    defaults named as in `PdfOptions` (`TableOfContents`, `TableOfContentsDepth`);
  - the `GeneratedTexts.Contents` text ("Contents" / "Inhalt").
  - Heading numbers (`headingNumbers`), captions and table titles are content, in E09-S04.
  - Page breaks and classification marking are written for Word in E09-S02.

## Proposed approach

1. **Package and project.**
   - **Projects.** `src/wwosik.DocLibs.DocumentGeneration.Docx` and its tests,
     `src/wwosik.DocLibs.DocumentGeneration.Docx.Tests`, both added to `wwosik.DocLibs.slnx`.
   - **References.** `DocumentFormat.OpenXml`, Markdig and the shared package. No new package
     versions are needed.
   - **Description:** "Generates Word documents (.docx) from Markdown, on a built-in template in a
     brand's design or on a caller's template, with numbered headings, real footnotes and a table
     of contents." (The content it names comes with E09-S04; both ship in the same release.)
   - **Resources.** `AuthoringGuide\*.md` is embedded as in Pptx, and `InternalsVisibleTo` points
     to the tests.
   - **Docs.**
     - `docs/wwosik.DocLibs.DocumentGeneration.Docx.md` is the package README, packed by
       `Directory.Build.props`.
     - The package is listed in `docs/README.md` and in the root `README.md`.
2. **Shared code** (question 2).
   - **`TextMetrics`** moves to the shared package. It works on `(string Text, bool Bold)` pieces
     instead of the Pptx `TextRun`, and Pptx calls it through a thin adapter. Decks must not
     change, and their tests prove it. E09-S04 uses it for table widths.
   - **Open XML helpers** that both generators need live in `src/Shared/OpenXml/` and are linked
     into both projects as internal source (`<Compile Include=… Link=…>`):
     - writing core properties (this story);
     - the SVG blip extension (E09-S03);
     - the `ChartWriter` part (E09-S03).
3. **API** (all public, documented, records with `init` like the PPTX ones):
   - **`DocxGenerator.Generate(MarkdownDocument, Stream, DocxOptions?)`** returns a
     `GenerationResult`. `Pages` is 0, because Word lays out the pages itself; the docs say so.
   - **`DocxGenerator.AuthoringGuide(DocxOptions?)`** (step 10).
   - **`DocxOptions`** mirrors `PdfOptions`: `Template`, `Brands`, `CodeBlockRenderers`,
     `ImageConverters`, `CodeFont`, `Language`, `Texts`, `TimeProvider`, `TableOfContents` and
     `TableOfContentsDepth`. E09-S04 adds `HeadingNumbers`, and E09-S02 `Classification` and
     `Draft`.
   - **`DocxTemplateOptions`** (abstract, internal `Name`/`Keys`/`With`/`Write`) has two kinds:
     - **`BrandDocxTemplateOptions`:**
       - `Brand` (default `Brand.Neutral`);
       - `ShowDate`, `ShowTagline` and `EmbedFonts` (all default `true`);
       - `PageSize` (`DocxPageSize.A4|Letter|A3`) and `Orientation` (`DocxOrientation`).
       - With a brand that has its own `Docx` template, it is built on that template.
       - Keys: `showDate`, `showTagline`, `embedFonts`; on a brand's own template only
         `embedFonts`.
     - **`CustomDocxTemplateOptions(Stream Template)`** with `Styles` (`DocxStyleNames`).
       - Keys: `styles.<role>` (`styles.title`, `styles.heading1`, …), as `layouts.*` works for
         decks.
   - **`DocxStyleNames`**: one property per role, each the style name as Word shows it, `null`
     for the default name. This story's roles:
     - `Title`, `Subtitle`, `CoverDetails`, `TocHeading`;
     - `Heading1`–`Heading6`, `Normal`.
     - E09-S04 adds the content roles to the same record (lists, quotes, code, captions, table
       title, table style, footnotes, links), and E09-S02 the deck-syntax roles.
   - **Brand.**
     - `Brand.Docx` holds a `DocxBrandTemplate(ReadOnlyMemory<byte> Content)` with `Styles`
       (`DocxStyleNames`), compared by content like `PptxBrandTemplate`.
     - `brand.json` gets a `docx` section: `{ "template": "template.dotx", "styles": { "heading1":
       "…" } }`. It is read through `BrandManifestContext` with typed properties, so typos are
       still rejected.
4. **Built-in template** (`DocxTemplateBuilder.Write(output, ResolvedBrand, options)`), written as
   XML strings like `TemplateBuilder`:
   - **Parts.** `styles.xml`, `theme1.xml`, `settings.xml`, `fontTable.xml`, and the header and
     footer parts. E09-S04 adds `numbering.xml`, `footnotes.xml` and the content styles.
   - **Theme.** Colours from `ThemeColors`. The major and minor font is the brand family, else
     Arial (as in decks), and the code font is the mono family, else Consolas.
   - **Styles** for this story's roles follow the PDF's sizes and colours, so a document looks
     related in both formats:
     - body 10.5 pt;
     - headings 20 / 16 / 13.5 / 12 / 11 / 10.5 pt, bold, in `Primary`, with `keepNext` and
       outline levels 0–5 (their numbering is linked in E09-S04);
     - `Title`, `Subtitle`, `CoverDetails`, `TocHeading`, and the TOC styles `TOC 1`–`TOC 3`.
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
       shared with `FontEmbedding`. See question 5.
   - Compatibility mode 15 (Word 2013+).
5. **Caller and brand templates** (`.dotx` or `.docx`):
   - **Opening.** The template is opened as a document and turned into one
     (`ChangeDocumentType(Document)`).
   - **Kept:** its styles, numbering, theme, settings, headers and footers.
   - **The body** is emptied except for the final `w:sectPr` (page setup, header and footer
     references).
   - **Styles are looked up by name** (`w:name`, case-insensitive), never by id: localized
     templates have other ids. The paragraph then references the id found. This lookup serves
     every role, including those E09-S04 and E09-S02 add.
   - **A missing style** (question 4):
     - a role whose style the template lacks gets a style created from the built-in definition,
       based on the template's `Normal`;
     - a name the caller mapped that does not exist throws `DocumentConversionException`, listing
       the template's paragraph styles.
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
     `dc:language` (from `DocumentLanguage.Resolve`), and `dcterms:created`/`modified` from
     `TimeProvider`. They are written by the shared helper of step 2.
7. **Table of contents** (question 3).
   - **Where.** With `toc: true` (E03-S02's key; off by default), the TOC goes after the cover: a
     `TocHeading` paragraph (`GeneratedTexts.Contents`: "Contents" / "Inhalt", added by whichever of
     this story and E03-S02 comes first), then a `TOC \o "1-<tocDepth>" \h \z \u` field, with
     `tocDepth` as E03-S02 defines it (default `2`).
   - **Pre-filled.** The field holds one entry per heading down to `tocDepth`, without page
     numbers, so a viewer that does not update fields still shows the headings. E09-S04 makes the
     entries hyperlinks to the headings' bookmarks and gives them their numbers.
   - **Updating.** `w:updateFields` in the settings makes Word fill in the page numbers on opening.
8. **The body until E09-S04.** So that this story's tests and samples generate, the body is written
   plainly: each heading as a paragraph in its `Heading N` style (by its Markdown level, no
   numbers, no bookmarks), and every other block as its plain text in `Normal` paragraphs. E09-S04
   replaces this with the full content.
9. **Errors and notes.**
   - `DocumentConversionException` with `DocumentFormat.Docx` is thrown for:
     - a damaged template;
     - a mapped style that does not exist;
     - a missing or unconvertible logo or artwork;
     - invalid front matter or an unknown brand.

     An invalid brand throws `ArgumentException`, as elsewhere.
   - Docx is added to the formats of `language-unknown` in `NoteCodes.All`. The content codes
     follow in E09-S04.
10. **Tests** (`Docx.Tests`, xUnit):
    - **Samples.** `report.md`, `long.md` and `layouts.md` are linked from the PDF tests'
      `Samples/`, and `brand-sample` as in the PPTX tests.
    - **Validation.** Each sample is generated with the neutral brand, the sample brand and a
      custom template built at test time from the built-in one with renamed styles (like
      `DarkTemplate`). Each output passes `OpenXmlValidator(FileFormatVersions.Microsoft365)` with
      no errors.
    - **Unit tests** (on plain paragraphs and headings):
      - style lookup by name with localized ids;
      - a missing mapped style throws, and a missing role style is created;
      - cover, header and footer parts, `w:titlePg`;
      - the TOC field with its depth and its entries;
      - core properties, `dc:language` for `lang: de`, and the German "Inhalt";
      - an embedded font that is obfuscated and restored;
      - a restricted face is skipped;
      - front-matter keys, including `styles.*`, `toc`, `tocDepth` and errors listing valid keys;
      - the brand pack `docx` section, and a typo in it.
    - **Decks unchanged.** The PPTX tests pass byte for byte after the `TextMetrics` move.
    - **For review.** Every sample is written to `.tmp/review/` for a human to open.
11. **Authoring guide, docs, changelog.**
    - **Guide.** `AuthoringGuide/Docx.md` is started: structure (cover, contents), front matter,
      characters and notes, built like `Pdf.md`, filled by `AuthoringGuides`. There are expected
      guides for neutral, sample and custom. E09-S04, E09-S02 and E09-S03 add their sections.
    - **Docs.**
      - The Docx page: options, templates and the role table, cover, header and footer, TOC,
        fonts, notes, known limits.
      - The DocumentGeneration page: `Brand.Docx`, the brand pack `docx` section, the notes table.
    - **Changelog.** `CHANGELOG.md` *New*: the package. *Breaking changes*: none for existing
      callers. Decks are unchanged after the `TextMetrics` move, and tests prove it.
    - **Memory.** `ai/memory.md` gets the facts implementers need: style lookup by name, linked
      shared sources.
12. **Verification in real Word and LibreOffice.**
    - **Word.** The samples are opened in Word on Windows through COM from WSL, with a throwaway
      `.tmp/scripts/word-export.ps1` (`Documents.Open`, `Fields.Update`, `SaveAs2` as PDF), then
      looked at page by page with `pdf2png.ps1`. E09-S04, E09-S02 and E09-S03 reuse it. The check
      confirms:
      - no repair prompt;
      - the cover, header and footer look as described;
      - the TOC fills, and Word keeps `w:updateFields` or drops it when the document is saved
        (recorded);
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
    uses its styles, headers and footers.
  - A mapped name that does not exist throws and lists the styles.
  - A missing role style is created.
- [ ] **Brand pack.** A brand pack with `docx.template` and `docx.styles` loads, and a typo in it
  throws `InvalidDataException` naming the key.
- [ ] **Table of contents.** `toc: true` gives a TOC with the headings down to `tocDepth`, which
  Word fills with page numbers when the document is opened.
- [ ] **Fonts.** Brand faces are embedded with `embedFonts` on, restricted faces are skipped.
- [ ] **Language.** `lang: de` sets `dc:language` `de` and gives the German "Inhalt".
- [ ] **Real Word.** The samples open in Word without a repair prompt and look as described, as
  checked through the Word export. LibreOffice is checked too, or recorded as not available.
- [ ] **Docs.** The authoring guide, the docs, the CHANGELOG and `ai/memory.md` are updated, and all
  tests pass.

## Open questions

1. Should this be split into two stories: S01a (package, templates, cover, header, footer,
   properties, TOC) and S01b (content)? Proposed: split, because each half is about as big as a
   PPTX story. S01a's tests can use plain paragraphs.

> Answered by the human (2026-11-14): as proposed.

2. Where does the Open XML code both generators need live (core properties, SVG blip, later
   `ChartWriter`)? Proposed: source files in `src/Shared/OpenXml/`, linked into Pptx and Docx as
   internal code, and `TextMetrics` moved into the shared package, which needs no Open XML.
   Alternatives:
   - a new package `DocumentGeneration.OpenXml`, a package only for internals;
   - Docx referencing the Pptx package;
   - the Open XML SDK in the shared package, which Charts and Diagrams would then pull in.

> Answered by the human (2026-11-14): as proposed.

3. How does the TOC get its page numbers? Proposed: entries pre-filled without numbers, plus
   `w:updateFields`, so Word asks once on opening to update fields. Alternatives:
   - no update setting, with the docs telling writers to press F9;
   - the TOC field alone, empty until updated.

   It has to be checked whether Word drops the setting when the document is saved.

> Answered by the human (2026-11-14): as proposed.

4. A caller template lacks a role's style. Proposed: create it from the built-in definition based
   on its `Normal`, as the fork does. Throw only when a name the caller mapped explicitly does not
   exist. The alternative is to throw for every missing style.

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Should brand fonts be embedded in Word documents? Proposed: yes by default, as in decks (`EmbedFonts`,
   `embedFonts`), as obfuscated `.odttf` parts, skipping restricted faces. The alternative is to
   name the families only.

> Answered by the human (2026-11-14): as proposed.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- **Order with E03-S02.** The analysis orders E09 (8) before E03-S02 (9). The shared keys `toc` and
  `tocDepth` and the `GeneratedTexts.Contents` text are agreed in the E03-S02 plan. Whichever is
  implemented first adds them to the shared package; the other reuses them.
- **Order within E09.** E09-S01 → E09-S04 → E09-S02 → E09-S03. E09-S04 got the next free number
  when the first plan of E09-S01 was split.
- **Out of scope** by the human's decision: cross-references, abbreviations, accessibility, history,
  compare, comments and review import. Landscape sections are also left out (they were a fork
  draft).
