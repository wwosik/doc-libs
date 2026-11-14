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

> **This version writes the frame of the document:** the templates, the cover, the header and
> footer, the table of contents, the core properties and the fonts. The body is written plainly:
> each heading in its heading style, every other block as its plain text in `Normal` paragraphs —
> lists, tables, code and quotes without their formatting, images as their alt text. Lists, tables,
> captions, images, code, footnotes and links in Word's own form follow in the next version.

- **Stream in, stream out.** Nothing is read from or written to disk.
- **Errors:** invalid front matter, an unknown brand, a style the options or the front matter name
  that the template lacks, or a damaged template throws `DocumentConversionException` with
  `Format = Docx`. An invalid brand throws `ArgumentException`, a `TableOfContentsDepth` other than
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
| `CodeFont` | the brand's mono family, else `Consolas` | the font of code |
| `CodeBlockRenderers`, `ImageConverters` | none | reserved for drawings and images in Word documents; not used by this version |
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

Each part of the document takes the paragraph style of its **role**. `DocxStyleNames` (shared with
`Brand.Docx`) names the style of each role as Word shows it; a role left `null` takes its default
name.

| Role (`DocxStyleNames`) | Front matter | Default style | Used for |
| ----------------------- | ------------ | ------------- | -------- |
| `Title` | `styles.title` | `Title` | the cover title |
| `Subtitle` | `styles.subtitle` | `Subtitle` | each paragraph after the cover title |
| `CoverDetails` | `styles.coverDetails` | `Cover Details` | the author and the date on the cover |
| `TocHeading` | `styles.tocHeading` | `TOC Heading` | the heading of the table of contents |
| `Heading1` … `Heading6` | `styles.heading1` … `styles.heading6` | `Heading 1` … `Heading 6` | headings by their Markdown level |
| `Normal` | `styles.normal` | `Normal` | body text |

The entries of the table of contents take Word's own `TOC 1` to `TOC 3`.

- **Styles are found by name**, compared case-insensitively, **never by id**: localized templates
  have other ids (a German template's `Heading 1` has the id `berschrift1`). The paragraph then
  references the id found. `Normal` is also the template's default paragraph style, whatever its
  name.
- **A style the template lacks** for a role nobody named is added from the built-in definition
  (sizes and colours of the neutral brand, or of the brand for its own template), based on the
  template's `Normal`, under a free id.
- **A name given in `Styles` or front matter that the template lacks** throws, listing the
  template's paragraph styles.

### Caller and brand templates

- **What is kept:** the template's styles, numbering, theme, settings, page setup, headers and
  footers. A `.dotx` is turned into a document (the content type of its main part changes; the part
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
- **Styles:** body 10.5 pt; headings 20 / 16 / 13.5 / 12 / 11 / 10.5 pt, bold, in `Primary`, kept with
  the next paragraph, outline levels 1–6; the cover title 30 pt bold in `Primary` at 28 % of the
  page height, subtitles 13 pt, the cover details in the footer colour.
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
  without page numbers, so a viewer that does not update fields still shows the headings.
- **Page numbers:** the document asks Word to update its fields when it is opened
  (`w:updateFields`); Word then fills in the page numbers. Word asks the user first.

## Fonts

With `EmbedFonts` (the default) and a brand with fonts, the brand's faces are embedded the way Word
does it: each face as an obfuscated font part (`.odttf`, ECMA-376 Part 1, 17.8.1), referenced from
the font table by `w:embedRegular`, `w:embedBold`, `w:embedItalic` and `w:embedBoldItalic` with its
key, and `w:embedTrueTypeFonts` in the settings.

- The text family always; the table and code families only when the document's styles or text
  name them. Whole faces, each file once.
- A face whose licence restricts embedding (`fsType` 2) is left out, and its family with it if it
  is the regular face — the same check as in decks. Licences are otherwise the caller's
  responsibility.
- `embedFonts: false` only names the families. `CustomDocxTemplateOptions` never embeds.

## Notes

`language-unknown` is the one note this version gives. See
[Generation results and notes](wwosik.DocLibs.DocumentGeneration.md#generation-results-and-notes).

## Known limits

- The body is plain text (see the note at the top).
- Landscape sections, history, compare, comments, review import, cross-references, abbreviations
  and accessibility checks are out of scope.
- The table of contents shows page numbers only once Word (or another program that updates fields)
  has updated it.
