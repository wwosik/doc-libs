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
| `.pdf` | `## Page n` per page. Lines are joined into paragraphs, words hyphenated across lines are rejoined, bullets become lists, larger or bold-numbered lines become `###` headings | JPEG data kept as `.jpg`, everything else re-encoded as `.png`, after the page's text, top to bottom | Text layer only, so scanned pages are empty. Headers and footers repeated on 3 or more pages are dropped, as is an image on every page. An undecodable image is skipped and counted in `skippedImages`. No tables |
| `.docx` | Title and heading styles (English and German style names) become `#` headings. Numbering becomes lists, plus tables, bold and italic, hyperlinks, line breaks, and text boxes as `> [Text box]` quotes | DrawingML and VML pictures at their position, with the alt text from the picture description | Nested tables are flattened into their cell. Footnotes, comments, headers and footers are not read |
| `.pptx` | `## <slide title>` per slide, or `## Slide n` if it has none. Then the text of the other shapes, tables and pictures, in shape order | Pictures with the alt text from the shape description | Speaker notes and charts are not read, nor slide number, date and footer placeholders. Bullet levels are not kept |

Plain text in all three is escaped only where it would otherwise turn into formatting (see
`MarkdownText.Escape`). Identical images are stored once and referenced wherever they appear.

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
