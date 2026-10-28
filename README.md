# DocLibs

.NET libraries for converting documents to and from Markdown — cross-platform, stream in and
stream out, no Office or browser needed (except for the legacy Office formats).

| Package | What it does |
| ------- | ------------ |
| `wwosik.DocLibs.DocumentConversions` | `.pdf`, `.docx` and `.pptx` to Markdown with their images; Markdown ↔ JIRA wiki markup. AOT-compatible. |
| `wwosik.DocLibs.DocumentConversions.Svg` | SVG to PNG with SkiaSharp, and `.svg` images in decks and PDFs. Never loads anything outside the SVG. AOT-compatible. |
| `wwosik.DocLibs.DocumentConversions.WindowsOnly` | `.doc`, `.rtf`, `.xls` and `.xlsx` to Markdown through Office automation. Windows with Word and Excel only. |
| `wwosik.DocLibs.DocumentGeneration.Pptx` | Markdown to PowerPoint (`.pptx`), in your brand's design with its fonts embedded, or on your own `.pptx` / `.potx` template — also templates with dark slides. Cards, columns, panels, callouts, tags and images side by side from plain Markdown syntax; what does not fit is shrunk or continued. |
| `wwosik.DocLibs.DocumentGeneration.Pdf` | Markdown to PDF/A-2b in your brand's design, with embedded fonts, bookmarks, links and tables. AOT-compatible. |
| `wwosik.DocLibs.DocumentGeneration.Diagrams` | Mermaid diagrams for both generators: vector graphics in PDFs, editable shapes in decks, in your brand's colours and font. AOT-compatible; built on [Mermaider](https://github.com/nullean/mermaider) (MIT). |
| `wwosik.DocLibs.DocumentGeneration.Charts` | Bar, line and area charts from CSV in a `chart` block: native, editable charts in decks, vector charts in PDFs, in your brand's colours. Financial charts (development, growth by row, actual vs reference, growth trend, two years, summary table) from typed data or JSON, also as SVG with hover tooltips. AOT-compatible. |
| `wwosik.DocLibs.DocumentGeneration` | Brands (colours, fonts, logo, footer, cover images), brand packs and the drawing model, shared by both generators. Comes along as their dependency. |

## Quick start

```
dotnet add package wwosik.DocLibs.DocumentConversions
dotnet add package wwosik.DocLibs.DocumentGeneration.Pdf
```

```csharp
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Pdf;

// Any supported document to Markdown, with its images as resources.
var registry = DocumentConverterRegistry.CreateDefault();
using var input = File.OpenRead("input.docx");
MarkdownDocument document = registry.Convert(input, "input.docx");

// Markdown back out as a PDF/A document.
using var output = File.Create("output.pdf");
PdfGenerator.Generate(document, output);
```

The full documentation of every package — options, Markdown mapping, front matter, known
limits — is in [docs/](https://github.com/wwosik/doc-libs/tree/main/docs).

## Your own design

Both generators draw one built-in design in a **brand**: colours, fonts (optionally a condensed
family for tables), footer text, logo, cover artwork and tagline — and, for decks, optionally your
own PowerPoint template. Without one they use a neutral brand. Define a brand in code, or keep it as files in a **brand pack**
(`brand.json` plus images and fonts, from a folder, a zip or embedded resources):

```csharp
using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Pdf;

var acme = BrandPack.Load("brands/acme");
PdfGenerator.Generate(document, output, new PdfOptions { Template = new BrandPdfTemplateOptions { Brand = acme } });
```

Markdown can pick among the brands you register with `template: acme` in its front matter. See
[Brands](https://github.com/wwosik/doc-libs/blob/main/docs/wwosik.DocLibs.DocumentGeneration.md#brands)
for every option and for how to ship a client's brand as its own package.

## When others write the Markdown

If people or AI agents write the Markdown your service turns into documents, hand them the
generator's authoring guide: `PptxGenerator.AuthoringGuide(options)` or
`PdfGenerator.AuthoringGuide(options)` returns Markdown describing exactly what those options
accept — headings, layouts, front matter, diagrams, and the characters the brand's fonts lack.

## Building

```
./run.sh build   # build the solution
./run.sh test    # run all tests
./run.sh pack    # pack the libraries into artifacts/
```

On Windows, use `./run.ps1` with the same verbs. Needs the .NET 10 SDK.

## Releases

Pushing a tag `v<version>` builds, tests and publishes every package to nuget.org with that
version — see [.github/workflows/](.github/workflows/). Versions are the release's date and hour,
`yyMM.d.H` (e.g. `2610.22.18`), with `.r` added for a further release in the same hour; they say
nothing about compatibility, so read [CHANGELOG.md](CHANGELOG.md) before updating, breaking
changes first.

## License

MIT — see [LICENSE](LICENSE). The Diagrams package depends on Mermaider, also MIT. The PDF
generator embeds the Liberation fonts, licensed under the SIL Open Font License 1.1 (shipped in
the package as `Fonts/OFL.txt`).

## For AI assistants and contributors

Start at [AGENTS.md](AGENTS.md).
