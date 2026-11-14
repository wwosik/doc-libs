# wwosik.DocLibs.DocumentConversions

Converts `.pdf`, `.docx` and `.pptx` into Markdown plus the images they contain, and Markdown to
and from JIRA wiki markup. Cross-platform; PdfPig is the only dependency. The package is marked
Native AOT compatible.

## Converting a document

```csharp
using wwosik.DocLibs.DocumentConversions;

var registry = DocumentConverterRegistry.CreateDefault();
using var input = File.OpenRead("report.pdf");
var document = registry.Convert(input, "report.pdf");

File.WriteAllText("report.md", document.Markdown);
foreach (var resource in document.Resources)
{
    // resource.Path is relative, e.g. "images/page2-image1.png", exactly as the Markdown references it.
    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine("out", resource.Path))!);
    File.WriteAllBytes(Path.Combine("out", resource.Path), resource.Content.ToArray());
}
```

- **The registry** detects the format and hands the stream to the first converter that handles
  it. `CreateDefault(...)` takes additional converters, such as the `OfficeConverter` from
  [wwosik.DocLibs.DocumentConversions.WindowsOnly](wwosik.DocLibs.DocumentConversions.WindowsOnly.md).
  The converters (`PdfConverter`, `DocxConverter`, `PptxConverter`) can also be used directly.
- **Stream in, values out.** No converter in this package reads or writes files. The result
  holds the Markdown (`\n` line endings, one trailing newline, empty if there is no content), the
  resources, and metadata (keys in `MetadataKeys`: title, author, page/slide count, skipped
  images).
- **Options** (`ConversionOptions`): `ResourcePathPrefix` (default `images/`), `IncludeImages`
  (default on), `FileName`, and `WorkingDirectory`. Only converters that need files on disk use
  `WorkingDirectory`.
- **Errors:** a damaged or unsupported document throws `DocumentConversionException`, which
  carries the `Format` and a `Reason`.
- **Synchronous.** All of the work is CPU-bound.
- **`MarkdownDocument.FindResource(reference)`** finds the resource an image reference points at,
  with `%` escapes decoded, `\` as `/` and a leading `./` dropped. Generators such as
  [wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md) use it.

## Format detection

`DocumentFormatDetector.Detect(stream, fileName)` reads the content, not the extension:

- **Magic bytes:** PDF, RTF, ZIP and OLE are recognized by their first bytes.
- **ZIP files:** the parts inside decide between DOCX, XLSX, PPTX and a plain ZIP.
- **Text:** anything else without a NUL byte in its first 8 KB counts as text.
- **OLE files:** their content can't tell `.doc` from `.xls`, so the extension decides (`Doc`,
  `Xls`, `Ppt`, `Msg`). Without a known extension the result is plain `Ole`.

The stream must be seekable, and its position is restored afterwards.

## What each format produces

| Format | Structure | Images | Known limits |
| ------ | --------- | ------ | ------------ |
| `.pdf` | `## Page n` per page. Lines are joined into paragraphs (words hyphenated across lines rejoined), larger or bold-numbered lines become `###` headings. Ruled tables become pipe tables, bullets, boxes and numbered lines become lists, monospace lines code blocks, footnotes `[^n]` (see below) | JPEG data kept as `.jpg`, everything else re-encoded as `.png`, where it stands on the page | Text layer only, so scanned pages are empty. Headers and footers repeated on 3 or more pages are dropped, as is an image on every page and rotated text. An undecodable image is skipped and counted in `skippedImages`. No tables without rules, no multi-column layout, no inline code or emphasis |
| `.docx` | Title and heading styles (English and German style names) become `#` headings. Numbering becomes lists, plus tables, bold and italic, hyperlinks, line breaks, and text boxes as `> [Text box]` quotes | DrawingML and VML pictures at their position, with the alt text from the picture description | Nested tables are flattened into their cell. Footnotes, comments, headers and footers are not read |
| `.pptx` | `## <slide title>` per slide, or `## Slide n` if it has none. Then the text of the other shapes, tables and pictures, in shape order, and the slide's speaker notes (the body placeholder of its notes page) as `::: notes` … `:::`, one paragraph per line of notes | Pictures with the alt text from the shape description | Charts are not read, nor slide number, date and footer placeholders, nor transitions. Bullet levels are not kept, in speaker notes neither. Speaker notes repeated on several slides come back on each |

Plain text in all three is escaped only where it would otherwise turn into formatting (see
`MarkdownText.Escape`). Identical images are stored once and referenced wherever they appear.

### What the PDF converter reads from a page

It reads the drawn lines and shapes of a page (PdfPig's paths) as well as its words:

- **Tables.** Horizontal and vertical rules that cross into a grid of at least 2 × 2 cells, framed all
  round, are a table; rules drawn cell by cell are joined first. So are three or more horizontal rules
  with the same ends and no vertical rule between them, when the text between them falls into at
  least two columns (x ranges no word crosses): the band above the second rule is the header, then
  each band is a row — or each line, when there is only one band below the header. The first row is
  always the header. Each cell's words go to the cell holding their centre; a cell's lines are joined
  with a space. A cell spanning columns (a vertical rule missing in its row) keeps its text in the
  first column and leaves the others empty. A table continued on the next page is written on each
  page, with the rows found there. The table stands where it was on the page.
- **Lists.** A bullet glyph (`•`, `–`, …), a small filled dot or square drawn just left of a line's
  first word makes a `-` item. A small square drawn there makes `- [ ]`, or `- [x]` when it holds a
  tick or is filled; the glyphs ☐ ☑ ☒ work too. A line starting `1.` or `1)` followed by text at a
  hanging indent is an ordered item when the next item continues the numbering, or when it is nested
  in another item; a numbered line on its own stays text, or a heading when it is large or bold.
  Items are nested by where their text starts: each position further right is one level deeper.
  A line set under an item's text right after it is kept with the item as a hard line break.
- **Code.** A line whose letters are at least 90 % in a monospace font (by name: `Mono`, `Courier`,
  `Consolas`, `Code`, `Inconsolata`, or by all letters advancing the same width) is code;
  consecutive code lines, or lines on the same background, are one fenced block without a language.
  Leading spaces are rebuilt from the x positions at one character width, blank lines from the gaps.
  A line made only of inline code spans comes out as a one-line block too.
- **Superscripts.** A smaller, raised word stays on its line, directly after the word before it. A
  raised number whose note is at the foot of the same page — a line in a smaller size below the body
  text, above the footer, starting with that number — becomes `[^n]`, and the note `[^n]: …` at the
  end of the page's section. Notes listed elsewhere (the PDF generator's list at the end of the
  document) are not paired.
- **Images** are placed between the blocks above and below them, named `page<n>-image<k>` top to
  bottom.
- **Rotated text** (watermarks, side notes) is left out of the output and of the statistics that
  find the body size and the margins. Horizontal watermarks are not detected.

If the analysis of a page fails, that page falls back to plain paragraphs, and if that fails too, to
its text in content order — never to an exception.

## JIRA wiki markup

`JiraWikiMarkup.FromMarkdown(markdown)` and `JiraWikiMarkup.ToMarkdown(wiki, options)` convert
between Markdown and the wiki markup of the description field in JIRA's REST API v2.

- **Markdown → wiki:** blocks are found first and soft line breaks inside a block are folded, so
  wrapped and unwrapped Markdown give the same result. Inline code containing markup characters
  uses the `{{{}…{}}}` form. Backslash escapes survive. Plain text that looks like JIRA markup is
  escaped, but only at word boundaries, so `end-to-end` and `customfield_10682` stay as they are.
  Images become `!url!`, strikethrough `-text-`, quotes `{quote}…{quote}`, and fences
  `{code:lang}`.
- **Wiki → Markdown:** `JiraMarkdownOptions.TopLevel` sets the heading level `h1.` becomes.
  `ResolveAttachment` maps attachment file names to URLs for images and attachment links.
  `{code}`, `{noformat}` and `{quote}` may span blank lines. Issue keys and user mentions become
  text. Unknown macros lose their markup, and their content stays.
- **Limits:** no hard line breaks. HTML, footnotes, definition lists, setext headings and nested
  quotes pass through untranslated. Image alt text is lost on the way to wiki markup. The
  Atlassian Document Format of Jira Cloud's API v3 is not supported.

## Markdown helpers

`MarkdownText` holds the building blocks the converters use, public for consumers that write
Markdown themselves:

- **Escaping:** `Escape` for inline text, `EscapeLineStart` for block markers.
- **Inline pieces:** `Code` gives a code span that survives backticks; `Destination` gives a
  link target.
- **Tables:** `Cell`, `Table` and `Compact`.
- **`Normalize`:** unifies line endings, collapses blank lines, and ends the text with one
  newline. Trailing spaces are trimmed, so hard line breaks must use the backslash form.
