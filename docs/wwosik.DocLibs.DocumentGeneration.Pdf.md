# wwosik.DocLibs.DocumentGeneration.Pdf

Generates PDF/A-2b documents from Markdown, in the built-in design in a brand's colours, fonts and
images, with embedded fonts (Liberation by default). Cross-platform, AOT-compatible, no Office or browser needed. Depends on Markdig,
PdfPig and [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md).

```csharp
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pdf;

var document = new MarkdownDocument(
    File.ReadAllText("report.md"),
    [new DocumentResource("chart.png", "image/png", File.ReadAllBytes("chart.png"))],
    new Dictionary<string, string> { [MetadataKeys.Author] = "DocLibs" });

using var output = File.Create("report.pdf");
PdfGenerator.Generate(document, output);                                    // neutral brand, A4 portrait

PdfGenerator.Generate(document, output, new PdfOptions
{
    Template = new BrandPdfTemplateOptions { Brand = BrandPack.Load("brands/acme"), ShowDate = false },
    PageSize = PdfPageSize.Letter,
    Orientation = PdfOrientation.Landscape,
});
```

- **Stream in, stream out.** Nothing is read from or written to disk; images come from
  `MarkdownDocument.Resources`, found by the path the Markdown references (see
  `MarkdownDocument.FindResource`).
- **Errors:** a missing image, an image that is not PNG or JPEG, invalid front matter, or a
  character the font has no glyph for (unless `ReplaceUnsupported` replaces it) throws
  `DocumentConversionException` with `Format = Pdf`, naming the path, key or character (with its
  code points: `'😀' (U+1F600)`, `'👍🏽' (U+1F44D U+1F3FD)`).
- **Metadata:** the title (front matter `title`, else metadata `title`, else the cover title) and
  the author (front matter `author`, else metadata `author`) go into the document info and the XMP
  metadata. The title also heads every page.

## For hosts that let others write the Markdown

When the Markdown comes from someone else, often an AI agent working through your service, give
them `PdfGenerator.AuthoringGuide(options)` with the **same options** you pass to `Generate`. It is
Markdown, in English, written for the writer rather than for you:

```csharp
var options = new PdfOptions { CodeBlockRenderers = [new MermaidRenderer()] };
string guide = PdfGenerator.AuthoringGuide(options);   // starts at "##"
```

- **What it covers:** how headings become the cover, pages and sections, how slide syntax is shown, the front matter keys of the options' design and the brands front
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

## Options

| Option | Default | |
| ------ | ------- | --- |
| `Template` | `BrandPdfTemplateOptions` with `Brand.Neutral` | `BrandPdfTemplateOptions { Brand, ShowDate, ShowTagline }`, both `true` by default |
| `Brands` | none | brands front matter may select, see [Brands](wwosik.DocLibs.DocumentGeneration.md#brands) |
| `PageSize` | `A4` | `A4`, `A3`, `Letter` |
| `Orientation` | `Portrait` | `Portrait`, `Landscape` |
| `CodeBlockRenderers` | none | see [Diagrams](#diagrams) |
| `ReplaceUnsupported` | none (throws) | see [Characters the fonts lack](#characters-the-fonts-lack) |
| `TimeProvider` | `TimeProvider.System` | the date of generation in the footer |

Margins, font sizes, header and footer are fixed by the design.

## Front matter

The shared syntax and rules are in
[wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md#front-matter). Keys:

| Key | Values | Templates |
| --- | ------ | --------- |
| `template` | `neutral`, the brand of `Template`, or a brand in `PdfOptions.Brands` | all |
| `showDate`, `showTagline` | `true`, `false` | all |
| `pageSize` | `A4`, `A3`, `Letter` | all |
| `orientation` | `portrait`, `landscape` | all |
| `title`, `author` | text | all |

## Design

- **Cover page** when the document starts with a `#` heading: the title in 30 pt bold in the brand
  colour at about a quarter of the page height, the paragraphs up to the next heading as subtitle,
  the brand's logo top right, its artwork across the bottom (at most 42 % of the page height) and
  its tagline bottom right (`ShowTagline`) — the neutral brand has none of these. No header or footer on the
  cover. Anything else in the first section (lists, tables, images) starts the content on page 2.
  A document that does not start with `#` has no cover.
- **Content pages:** margins 20 mm left and right; header with the document title (8 pt) left and
  the logo right; footer with the footer text, the date (`ShowDate`, `dd.MM.yyyy`) and "n / m"
  right. Page numbers count the cover.
- **Body text** 10.5 pt in the brand's regular face (Liberation Sans without brand fonts), line height 1.35. Headings bold in the brand colour,
  20 / 16 / 13.5 / 12 / 11 / 10.5 pt.

| Markdown | PDF |
| -------- | --- |
| First `#` (at the very start) | The cover page. |
| Every other `#` | Starts a new page. |
| `#`–`######` | Headings, kept on the same page as the start of what follows (see [Page breaks](#page-breaks)); each is a bookmark (nested by level, the cover title on top) and the target of `[x](#heading-id)` links (GitHub-style ids). |
| Paragraphs | Greedy line breaking at spaces; a word wider than the line is broken by characters; `\` hard breaks kept. Left-aligned, no hyphenation. No single line at the top or bottom of a page (see [Page breaks](#page-breaks)). |
| Lists | Hanging indent of 18 pt per level, `•` / `–` alternating by level, numbers from the list's start value. |
| Code blocks | The brand's mono face, else Liberation Mono, 9 pt on light grey, wrapped by characters. |
| Quotes | Indented, grey, with a rule on the left per quote level. |
| Tables | 9.5 pt, in the brand's table fonts if it has them; full content width. Each column gets at least its longest word, the rest is shared in proportion to the content. Header row in the brand table colour with white bold text, banded rows, thin lines, alignment from the Markdown. A row that doesn't fit moves to the next page and the header row repeats there; a row taller than a page splits at line boundaries. |
| Images | Natural size at 96 DPI, `"scale=N.N"` in the title multiplies it; scaled down to the content width and page height, never up; centred; moved to the next page if they don't fit. |
| `---` | A thin line. |
| `**bold**`, `*italic*`, `~~strike~~`, `` `code` `` | Font face; strike as a line through. |
| Links | Brand link colour, underlined, with a link annotation. Absolute URLs open; `#id` jumps to the heading; anything else stays text. |
| Raw HTML | Written out as text. HTML comments are dropped. |

## Page breaks

- **Headings stay with what follows them.** A heading (levels 2–6; `#` always starts a page)
  moves to the next page unless the start of the next block fits below it:
  - a paragraph or list: its first two lines, or the whole paragraph if it has three lines or
    fewer;
  - code: its first two lines;
  - a table: its header row and the start of its first row;
  - an image or a diagram: all of it, because they are never split;
  - another heading: that heading and the start of what follows it, so a chain of headings moves
    together.
- **A visual too tall for one page together with its headings** is scaled down further until
  they fit on one page together.
- **No widows or orphans.** Where a page break falls inside a paragraph (body text, list items,
  quotes), at least two lines stay on each side of it. A paragraph of two or three lines moves
  whole. The list marker moves with the first line. Code blocks and table rows follow their own
  rules: code breaks between any lines, and table rows as described above.

## Fonts

A brand's own fonts replace Liberation Sans, and Liberation Mono too when the brand has mono
faces; they are embedded the same way and follow the same glyph rules. Table fonts, if the brand
has them, draw table cells — see [Table fonts](wwosik.DocLibs.DocumentGeneration.md#table-fonts).
Requirements and licensing: [Brands → Fonts](wwosik.DocLibs.DocumentGeneration.md#fonts). The rest
of this chapter is about the bundled Liberation fonts, which brands without fonts (such as
`neutral`) use, and which stand in for a mono face the brand lacks.

- **Liberation Sans** (regular, bold, italic, bold italic) and **Liberation Mono** (regular, bold),
  version 2.1.5, SIL Open Font License 1.1 (`Fonts/OFL.txt`, shipped in the package). They have the
  letter widths of Arial and Courier New. Embedded resources under `Fonts/`; a PDF carries only
  the subset it uses.
- **Coverage:** Latin including Polish, Czech and German, Greek, Cyrillic, Hebrew, arrows,
  typographic quotes and dashes, `€`, common maths signs, box drawing. Not `✓` / `✗` and other
  symbols, no CJK, nothing outside the BMP (no emoji), not the variation selector `U+FE0F` — those
  throw, unless `ReplaceUnsupported` replaces them. Liberation Mono covers a little less than Sans
  (e.g. not `‖` U+2016, `ȷ` U+0237 or the spaces `U+2000`–`U+2006`), so the same
  character can be fine in body text and missing in code.
- **To change the default family:** replace the files under `Fonts/` and the six names in
  `FontSet.Liberation` (its table faces are the text faces). For one client, give its brand fonts instead.
- PdfPig writes TrueType fonts as simple fonts of at most 255 distinct characters. The renderer
  adds the same font again when a copy is full, so documents with more characters work. Copies
  are kept per file, not per face: faces sharing a file (tables without fonts of their own) share
  its embedded copies.

## PDF/A

Every PDF is **PDF/A-2b** (`PdfAStandard.A2B` in PdfPig: sRGB output intent, XMP metadata, all
fonts embedded). Two gaps in PdfPig 0.1.16 are closed after it has written the file, in place and
without moving any byte offset:

- **Link annotations** get the print flag `/F 4`, which PDF/A requires; it replaces the optional
  `/Type /Annot` in the same space.
- **XMP:** `dc:title` becomes a language alternative and `dc:creator` an ordered list, as PDF/A
  requires; PdfPig writes plain text. The packet is rewritten without PdfPig's indentation and
  toolkit name and padded to its old length.

Checked with veraPDF 1.30 (PDF/A-2b profile): the samples in both designs pass.

## Characters the fonts lack

`PdfOptions.ReplaceUnsupported` is for text nobody can fix by hand, such as what users or an AI
wrote. It is called with each **text element** (grapheme cluster, UAX #29) that has a character the
font of its text lacks, and returns the text to put there instead:

```csharp
new PdfOptions { ReplaceUnsupported = c => c switch { "✓" => "[x]", "✗" => "[ ]", _ => "?" } }
```

- **One call per text element:** `😀`, `❤️` (with `U+FE0F`), `👍🏽` (with its skin tone) and
  `👨‍👩‍👧` (joined by `U+200D`) are each one call with the whole sequence; so is a letter whose
  combining mark the font lacks. Elements the font fully covers are left alone, and the library
  drops nothing on its own.
- **Per occurrence, per face:** the face the text is drawn in decides — body, headings and table
  header in Sans (bold or italic as styled), code in Mono — so `‖` is replaced in a code block but
  not in a paragraph. The footer text and the header title are replaced once for all pages.
- **The replacement is checked too:** if the font lacks one of its characters, generation throws,
  naming the replacement and what it replaced.
- **Bookmarks and the title** (document info, XMP and header) show the replaced text, as on the
  page. **Heading anchors** stay the ids Markdig derives from the source, so `[x](#heading-id)`
  links keep working.
- Long words and code lines break, and the header title is cut with `…`, between text elements,
  never inside one.

## Diagrams

`PdfOptions.CodeBlockRenderers` are asked in order about every fenced code block, and the first
graphic wins. A block no renderer takes stays code. For Mermaid, add
`new MermaidRenderer()` from
[wwosik.DocLibs.DocumentGeneration.Diagrams](wwosik.DocLibs.DocumentGeneration.Diagrams.md). A
renderer can return:

- a **`DrawingGraphic`**, drawn as vector paths (fill, stroke, dashes, cubic curves) straight
  into the page. It is placed like an image: centred, at 0.75 pt per pixel, shrunk to the content
  width and the page height, never enlarged, and moved to the next page if it does not fit. Its
  text is set in the table faces (the text faces without table fonts), embedded and checked for
  glyphs like any other text. A line wider than its room shrinks down to 70 %. Colours are device
  RGB without transparency, so the PDF stays PDF/A-2b.
- an **`ImageGraphic`**, a PNG or JPEG placed like a Markdown image.
- a **`ChartGraphic`**: its `Drawing` is drawn like a `DrawingGraphic` (decks write the chart
  natively). `ChartRenderer` from
  [wwosik.DocLibs.DocumentGeneration.Charts](wwosik.DocLibs.DocumentGeneration.Charts.md) makes them.

The request carries the language, the code, the brand the document is generated in (after front
matter) and the block's line in the Markdown. A renderer that throws fails the generation with a
`DocumentConversionException` (`Format = Pdf`) whose `InnerException` is the renderer's.

## Slide layouts

Markdown written for the PPTX generator's
[layouts](wwosik.DocLibs.DocumentGeneration.Pptx.md#layouts) works here too, shown plainly:

- **Heading attributes** at the end of a `##` heading (`{.dark kicker="Label" layout="Name"}`) and of
  a `#` heading (`{.closing}`) are checked and dropped; the kicker is not shown.
- **Containers** (`::: cards`, `rows`, `panel`, `statement`, `columns`) show their content as normal
  blocks: a card's `###` is a heading, a short-form list a list, the columns of `::: columns` one
  after the other without the `---` between them.
- **Alerts** (`> [!NOTE]` …) are quotes that start with their kind in bold ("Note", "Warning").
- **`==marked==`** text is bold; tags and status labels stay code spans. `labels.<category>` front
  matter is checked and otherwise ignored.
- **`[words]{.success}`** sets the words bold in that status colour (`Colors.Labels`), as in decks.
- **A paragraph of only images** is a row: the images side by side in cells of equal width across
  the text width, each scaled down to its cell, the row moved to the next page whole.
- Unknown attributes, containers and alerts throw, as in decks.

## Known limits

- A `##` heading with attributes keeps them in its automatic anchor (`## Title {.dark}` gets
  `#title-dark`), as Markdig computes the anchor before they are dropped.
- No hyphenation, justification, kerning, right-to-left or Asian scripts; no footnotes; no merged
  table cells; no SVG or GIF images; alt text is not shown (an untagged PDF has nowhere to put it).
- A table wider than the page in minimum widths is squeezed, and its words break by characters.
- Reading a generated PDF back with `PdfConverter` loses the cover title (it equals the running
  header and is removed as such), puts a page's images after its text, and does not recover
  tables or list nesting.
