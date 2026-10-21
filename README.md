# DocLibs

.NET libraries for converting documents to and from Markdown — cross-platform, stream in and
stream out, no Office or browser needed (except for the legacy Office formats).

| Package | What it does |
| ------- | ------------ |
| `wwosik.DocLibs.DocumentConversions` | `.pdf`, `.docx` and `.pptx` to Markdown with their images; Markdown ↔ JIRA wiki markup. AOT-compatible. |
| `wwosik.DocLibs.DocumentConversions.WindowsOnly` | `.doc`, `.rtf`, `.xls` and `.xlsx` to Markdown through Office automation. Windows with Word and Excel only. |
| `wwosik.DocLibs.DocumentGeneration.Pptx` | Markdown to PowerPoint (`.pptx`), in the built-in design or on your own `.pptx` / `.potx` template. |
| `wwosik.DocLibs.DocumentGeneration.Pdf` | Markdown to PDF/A-2b with embedded fonts, bookmarks, links and tables. AOT-compatible. |
| `wwosik.DocLibs.DocumentGeneration` | What the two generators share. Comes along as their dependency. |

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

The generators ship one neutral design. PPTX decks can also be built on any `.pptx` / `.potx`
template you pass in. A way to plug in a company's own colours, logo and footer for both
generators — as code or as a brand pack of files — is planned; see
[spec/](https://github.com/wwosik/doc-libs/tree/main/spec).

## Building

```
./run.sh build   # build the solution
./run.sh test    # run all tests
./run.sh pack    # pack the libraries into artifacts/
```

On Windows, use `./run.ps1` with the same verbs. Needs the .NET 10 SDK.

## Releases

Pushing a tag `v<version>` (e.g. `v1.2.0`) builds, tests and publishes every package to
nuget.org with that version — see [.github/workflows/](.github/workflows/).

## License

MIT — see [LICENSE](LICENSE). The PDF generator embeds the Liberation fonts, licensed under the
SIL Open Font License 1.1 (shipped in the package as `Fonts/OFL.txt`).

## For AI assistants and contributors

Start at [AGENTS.md](AGENTS.md).
