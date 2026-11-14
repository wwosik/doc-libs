# wwosik.DocLibs.DocumentGeneration.Docx

Generates Word documents (`.docx`) from Markdown, on the built-in template in a brand's colours,
fonts and images, on a brand's own Word template, or on a caller's `.dotx` / `.docx`. Every
paragraph references a style of the template by name, so the document looks like the template and
stays editable the way Word users expect. Cross-platform, no Office needed. Depends on the Open XML
SDK, Markdig and [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md).

```csharp
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Docx;

var document = new MarkdownDocument(
    File.ReadAllText("report.md"),
    [],
    new Dictionary<string, string> { [MetadataKeys.Author] = "DocLibs" });

using var output = File.Create("report.docx");
DocxGenerator.Generate(document, output);                                   // neutral brand, A4 portrait

DocxGenerator.Generate(document, output, new DocxOptions
{
    Template = new BrandDocxTemplateOptions { Brand = BrandPack.Load("brands/acme"), PageSize = DocxPageSize.Letter },
    TableOfContents = true,
});

using var house = File.OpenRead("house.dotx");
DocxGenerator.Generate(document, output, new DocxOptions
{
    Template = new CustomDocxTemplateOptions(house) { Styles = new DocxStyleNames { Heading1 = "Chapter" } },
});
```

> **Not yet in Word's own form:** the slide syntax of decks (containers, alerts, labels, coloured
> words, image rows) is shown plainly, and diagrams and charts stay code. Both follow in later
> versions.

- **Stream in, stream out.** Nothing is read from or written to disk.
- **Errors:** invalid front matter, an unknown brand, a style the options or the front matter name
  that the template lacks, a damaged template, an image that is not among the document's resources
  or that is neither PNG, JPEG nor GIF nor taken by an image converter, or invalid slide syntax
  throws `DocumentConversionException` with `Format = Docx`. An invalid brand throws `ArgumentException`, a `TableOfContentsDepth` other than
  1, 2 or 3 `ArgumentOutOfRangeException`.
- **Result:** `Generate` returns a `GenerationResult` with its notes. `Pages` is 0: Word lays out
  the pages itself, so the generator does not know how many there are.
- **Core properties:** the title (front matter `title`, else metadata `title`, else the cover
  title), the author (front matter `author`, else metadata `author`), the
  [document language](wwosik.DocLibs.DocumentGeneration.md#document-language) as `dc:language`, and
  the created and modified dates from `TimeProvider`.

## For hosts that let others write the Markdown

When the Markdown comes from someone else, often an AI agent working through your service, give
them `DocxGenerator.AuthoringGuide(options)` with the **same options** you pass to `Generate`. It is
Markdown, in English, written for the writer, and starts at `##`: how headings become the cover and
the table of contents, the front matter keys of the options' template and the brands front matter
can pick, the characters the brand's fonts lack, the notes, and an example that generates. Put it
under a heading of your own and add what only you know.

## Options

| Option | Default | |
| ------ | ------- | --- |
| `Template` | `BrandDocxTemplateOptions` with `Brand.Neutral` | see [Templates](#templates) |
| `Brands` | none | brands front matter may select, see [Brands](wwosik.DocLibs.DocumentGeneration.md#brands) |
| `TableOfContents` | `false` | a table of contents after the cover, unless front matter sets `toc` |
| `TableOfContentsDepth` | `2` | the heading levels it lists, 1 to 3, unless front matter sets `tocDepth` |
| `HeadingNumbers` | `true` | numbers on Heading 1–3 of the built-in template, unless front matter sets `headingNumbers`; no effect on other templates |
| `CodeFont` | the brand's mono family, else `Consolas` | the font of code |
| `ImageConverters` | none | asked for images that are not PNG, JPEG or GIF, such as SVG (drawn at twice the size they are placed at) |
| `CodeBlockRenderers` | none | reserved for drawings: a block a renderer would draw stays code with a `code-not-drawn` note |
| `Language` | `en` | the document language, unless front matter sets `lang` |
| `Texts` | none | `GeneratedTexts` for further languages, by tag |
| `TimeProvider` | `TimeProvider.System` | the date on the cover and in the footer, and the created and modified dates |

## Front matter

The shared syntax and rules are in
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#front-matter). Keys:

| Key | Values | Templates |
| --- | ------ | --------- |
| `template` | `neutral`, the brand of `Template`, or a brand in `DocxOptions.Brands` | all |
| `title`, `author` | text | all |
| `lang` | a language tag such as `en`, `de`, `de-CH` | all |
| `toc` | `true`, `false` | all |
| `tocDepth` | `1`, `2`, `3` | all |
| `headingNumbers` | `true`, `false` | all; takes effect on the built-in template |
| `showDate`, `showTagline` | `true`, `false` | the built-in template |
| `embedFonts` | `true`, `false` | the built-in template and a brand's own template |
| `styles.<role>` | a style name as Word shows it, such as `styles.heading1: Chapter` | a caller's template |
| `transition`, `build`, `numberChapters` | as in decks | all — a deck's keys: checked, then ignored |
| `labels.<category>` | as in decks | all — checked, then ignored |

## Templates

`DocxTemplateOptions` has two kinds; each takes only the front matter keys its template has.

| Kind | What it is |
| ---- | ---------- |
| `BrandDocxTemplateOptions { Brand, ShowDate, ShowTagline, EmbedFonts, PageSize, Orientation }` | The built-in template written from the brand (the default, with `Brand.Neutral`). `PageSize` is `A4` (default), `Letter` or `A3`, `Orientation` `Portrait` (default) or `Landscape`. A brand with its own `Docx` template is built on that instead, which keeps its own page, headers and footers and takes only `embedFonts`. |
| `CustomDocxTemplateOptions(Stream Template) { Styles }` | A caller's `.dotx` or `.docx`, read from the stream's position and left open. `Styles` is a `DocxStyleNames`. Never embeds fonts (it has no brand). |

### Roles and styles

Each part of the document takes the style of its **role**: a paragraph style, or for inline code,
footnote references and links a character style, and for tables a table style. `DocxStyleNames`
(shared with `Brand.Docx`, and in a brand pack `docx.styles`) names the style of each role as Word
shows it; a role left `null` takes its default name.

| Role (`DocxStyleNames`) | Front matter | Default style | Used for |
| ----------------------- | ------------ | ------------- | -------- |
| `Title` | `styles.title` | `Title` | the cover title |
| `Subtitle` | `styles.subtitle` | `Subtitle` | each paragraph after the cover title |
| `CoverDetails` | `styles.coverDetails` | `Cover Details` | the author and the date on the cover |
| `TocHeading` | `styles.tocHeading` | `TOC Heading` | the heading of the table of contents |
| `Heading1` … `Heading6` | `styles.heading1` … `styles.heading6` | `Heading 1` … `Heading 6` | headings, the highest level after the cover as `Heading 1` |
| `Normal` | `styles.normal` | `Normal` | body text, table cells, images |
| `ListBullet` | `styles.listBullet` | `List Bullet` | items of bulleted lists |
| `ListNumber` | `styles.listNumber` | `List Number` | items of numbered lists |
| `ListTask` | `styles.listTask` | `List Task` | task items, with their check box |
| `DefinitionTerm` | `styles.definitionTerm` | `Definition Term` | the term of a definition list |
| `Definition` | `styles.definition` | `Definition` | its description |
| `Quote` | `styles.quote` | `Quote` | quotes and alerts |
| `Code` | `styles.code` | `Code` | each line of a code block |
| `CodeCharacter` | `styles.codeCharacter` | `Code Character` (character) | inline code |
| `Rule` | `styles.rule` | `Rule` | `---`, an empty paragraph with a bottom border |
| `Caption` | `styles.caption` | `caption` | captions of figures and tables |
| `TableTitle` | `styles.tableTitle` | `Table Title` | a bold paragraph right before a table |
| `Table` | `styles.table` | `Doc Table` (table) | tables |
| `FootnoteText` | `styles.footnoteText` | `footnote text` | footnote text |
| `FootnoteReference` | `styles.footnoteReference` | `footnote reference` (character) | footnote references |
| `Hyperlink` | `styles.hyperlink` | `Hyperlink` (character) | links |

The entries of the table of contents take Word's own `TOC 1` to `TOC 3`.

- **Styles are found by name** among the styles of their kind, compared case-insensitively,
  **never by id**: localized templates
  have other ids (a German template's `Heading 1` has the id `berschrift1`). The paragraph then
  references the id found. `Normal` is also the template's default paragraph style, whatever its
  name.
- **A style the template lacks** for a role nobody named is added from the built-in definition
  (sizes and colours of the neutral brand, or of the brand for its own template), a paragraph style
  based on the template's `Normal`, under a free id. Added heading styles are not numbered.
- **A name given in `Styles` or front matter that the template lacks** throws, listing the
  template's styles of that kind.

### Caller and brand templates

- **What is kept:** the template's styles, numbering, theme, settings, page setup, headers and
  footers. Its heading styles decide whether headings are numbered. Lists use the numbering of
  its `List Bullet` and `List Number` styles; where they have none, the built-in definitions are
  added. The document language goes into its document defaults; languages its own styles set stay. A `.dotx` is turned into a document (the content type of its main part changes; the part
  keeps its name).
- **The body is replaced:** everything in it goes except its final section properties (page,
  header and footer references).
- **Placeholders in headers and footers:** content controls bound to the core properties' title or
  author (`dc:title`, `dc:creator`) get the document's values as their text, so they show them
  before Word refreshes them. Simple `TITLE` and `AUTHOR` fields get the values too; such fields,
  simple or not, make the document ask Word to update its fields on opening (`w:updateFields`).

### The built-in template

Written in code from the brand, like the PPTX master, in the PDF's sizes and colours so that a
document looks related in both formats.

- **Theme:** the brand's 12 theme colours; heading and body font the brand's family, else Arial.
- **Styles:** body 10.5 pt (the document defaults, so that the table style applies in cells);
  headings 20 / 16 / 13.5 / 12 / 11 / 10.5 pt, bold, in `Primary`, kept with the next paragraph,
  outline levels 1–6; the cover title 30 pt bold in `Primary` at 28 % of the page height,
  subtitles 13 pt, the cover details in the footer colour. Links in `Link`, underlined; quotes grey
  with a 2 pt bar in `TableLine`; code 9 pt in the code font on light grey (`F3F4F6`), inline code
  9.5 pt; captions 9 pt, centred; table titles bold in `Primary`; footnotes 8 pt; `Doc Table` 9.5 pt in
  the brand's table family if it has one, the header row bold white on `TableHeader`, every second
  row in `TableBand`, grid lines in `TableLine`.
- **Numbering:** one multilevel list linked to Heading 1–3 (`1`, `1.1`, `1.1.1`), unless
  `HeadingNumbers` is off; lists get bullets `•`, `–`, `▪` and numbers `1.`, `a.`, `i.`, 18 pt a
  level.
- **Page:** the size and orientation of the options, margins 20 mm left and right and 25 mm top and
  bottom.
- **Cover** (the first page, which has a header and footer of its own): the logo top right, the
  cover artwork across the bottom (as wide as the page, at most 42 % of its height), the tagline
  bottom right (`ShowTagline`).
- **Content pages:** the document title left and the logo right in the header; the footer text,
  the date (`ShowDate`, `dd.MM.yyyy`) and `PAGE / NUMPAGES` fields in the footer.
- Compatibility mode 15 (Word 2013 and later).

## Cover

A document whose first block is a `#` heading gets a cover, as in the PDF: the heading in `Title`,
each paragraph after it (up to the next heading or image) in `Subtitle`, then the author (front
matter `author`, else the document's metadata) and the date (with `ShowDate` on the built-in
template) in `CoverDetails`. The content starts on the next page. A document that does not start
with `#` has no cover, and its first page gets the headers and footers of the others (the
section's `w:titlePg` is removed).

## Table of contents

With `toc: true` (or `TableOfContents`), the table of contents follows the cover on a page of its
own: a `TocHeading` paragraph with `GeneratedTexts.Contents` ("Contents", German "Inhalt"), then a
`TOC \o "1-<tocDepth>" \h \z \u` field.

- **Pre-filled:** the field holds one entry per heading down to `tocDepth`, in `TOC 1` to `TOC 3`,
  with its number as Word shows it (on the built-in template) and a link to the heading's bookmark,
  without page numbers, so a viewer that does not update fields still shows the headings.
- **Page numbers:** the document asks Word to update its fields when it is opened
  (`w:updateFields`); Word then fills in the page numbers. Word asks the user first.

## Fonts

With `EmbedFonts` (the default) and a brand with fonts, the brand's faces are embedded the way Word
does it: each face as an obfuscated font part (`.odttf`, ECMA-376 Part 1, 17.8.1), referenced from
the font table by `w:embedRegular`, `w:embedBold`, `w:embedItalic` and `w:embedBoldItalic` with its
key, and `w:embedTrueTypeFonts` in the settings.

- The text family always; the table and code families when the document's styles or text name
  them, which the built-in `Doc Table` and code styles do. Whole faces, each file once.
- A face whose licence restricts embedding (`fsType` 2) is left out, and its family with it if it
  is the regular face — the same check as in decks. Licences are otherwise the caller's
  responsibility.
- `embedFonts: false` only names the families. `CustomDocxTemplateOptions` never embeds.

## Markdown in Word

| Markdown | Word |
| -------- | ---- |
| `#` … `######` after the cover | `Heading 1` … `Heading 6`, shifted so that the highest level used after the cover is Heading 1; a bookmark from the heading's id (letters, digits, `_`, at most 40 characters, unique). Every `#` but the first content starts a page (`w:pageBreakBefore`). |
| Paragraph | `Normal`; a hard break is `w:br` |
| `**bold**`, `*italic*`, `~~strike~~`, `==mark==` | `w:b`, `w:i`, `w:strike` on the run, marks bold: the one run formatting besides code |
| `` `code` `` | `Code Character` |
| `[text](https://…)`, `mailto:` | `w:hyperlink` with an external relationship, in `Hyperlink` |
| `[text](#id)` | `w:hyperlink w:anchor` to the heading's bookmark; an unknown id stays text |
| `- item`, `1. item` | `List Bullet` / `List Number` with `w:numPr`, `ilvl` 0–2; each ordered list a `w:num` of its own with a `w:startOverride` of its first number, so it restarts. Deeper items stay at level 3 (`list-too-deep`). Further paragraphs of an item keep its indent without a marker. |
| `- [ ]`, `- [x]` | `List Task` with a check-box content control (`w14:checkbox`, ☐/☒ in Segoe UI Symbol) and a tab |
| Definition list | `Definition Term` (bold, kept with the next), `Definition` (indented) |
| `> quote`, nested quotes | `Quote`, flattened |
| Code block | one `Code` paragraph per line, tabs as four spaces |
| `---` | an empty `Rule` paragraph |
| Table | `Doc Table` at the text width (`w:tblW` 100 %, fixed layout), the header row repeated (`w:tblHeader`), rows kept on one page (`w:cantSplit`) unless taller than one, the Markdown alignment as `w:jc`. Column widths by the PDF's rule from `TextMetrics`: each column at least its longest word, the rest in proportion to the content, in `w:tblGrid` and `w:tcW`. |
| `**Title**` right before a table | `Table Title`, kept with the table |
| `Table: …` after a table | a `Caption` above the table, kept with it |
| `![alt](a.png "title")` | a centred `Normal` paragraph with the picture inline, at its natural size at 96 DPI times `scale=`, scaled down to the text width and 80 % of the text height, never up; the alt text in `wp:docPr/@descr`. Its caption below it, the picture kept with it. |
| `Text[^1]`, `[^1]: …` | a Word footnote: `w:footnoteReference` in `footnote reference`, the text in `footnote text` in the footnotes part (made with its separators when the template has none). Each reference gets a footnote of its own. |
| Raw HTML | text, with `html-as-text`; comments are left out |
| Slide syntax | checked as in decks, shown plainly: containers' content in sequence, alerts as `Quote` under their title in bold, labels as code, coloured words bold |

### Heading numbers

On the built-in template Heading 1–3 are numbered through its multilevel list, `1`, `1.1`,
`1.1.1`; Heading 4–6 are not. `headingNumbers: false` (or `HeadingNumbers = false`) leaves the
numbering off the heading styles. A heading ending in `{.unnumbered}` gets `w:numId` 0: no number,
and it does not count. On a caller's or a brand's own template the heading styles decide;
`{.unnumbered}` still switches a heading's number off. Word counts a skipped level as 0: a
`Heading 3` straight after a `Heading 1` is `1.0.1`, and a `Heading 2` before any `Heading 1` is
`0.1`. On `###` to `######` a trailing `{…}` is taken off only when every token in it is
`.unnumbered`; any other braces stay text.

### Captions

The [shared caption syntax](wwosik.DocLibs.DocumentGeneration.md#captions): `Table: …` after a
table, an image's title (without `scale=`; `"caption"` or `.caption` for the alt text). A caption is
a `Caption` paragraph: the label in the document language (`GeneratedTexts.Table` / `Figure`), a
`SEQ Table \* ARABIC` or `SEQ Figure \* ARABIC` field whose result is already filled in, `: ` and
the text; label and number in bold. Tables and figures are numbered separately through the whole
document. A table caption goes above the table, after its title; a figure caption below the image.
A `Table:` or `Figure:` paragraph anywhere else stays text with `caption-ignored` (until drawings
come, every `Figure:` paragraph does).

### Language

The document language goes into `w:lang` of the document defaults and `w:themeFontLang` in the
settings, so Word spell-checks in it; on every template, leaving the languages of the template's
own styles alone.

## Notes

`code-not-drawn`, `html-as-text`, `footnote-missing`, `attribute-ignored` (`fit`, `focus`,
`.round`, `.grey` on an image, or any attribute on an image shown as its alt text in a heading or
a cell), `caption-ignored`, `list-too-deep` and `language-unknown`. See
[Generation results and notes](wwosik.DocLibs.DocumentGeneration.md#generation-results-and-notes).

## Known limits

- Slide syntax is shown plainly, and diagrams, charts, QR codes (`qr:` images, shown as their alt
  text) and icons (`:icon-name:`, left as written) are not drawn.
- Images in table cells and headings are their alt text.
- A footnote referenced twice gets two footnotes with the same text.
- `DocxConverter` reads the content back without footnotes and code style, and reads the
  `Table Title` style as a heading, because its name contains "title".
- Landscape sections, history, compare, comments, review import, cross-references, abbreviations
  and accessibility checks are out of scope.
- The table of contents shows page numbers only once Word (or another program that updates fields)
  has updated it.
