# Project memory

Accumulated facts about this codebase: decisions made, patterns in use, traps worth
remembering. Shared here rather than in any tool's device-local memory, so every agent on
every machine reads the same thing.

Keep it factual and current. When something recorded here stops being true, correct it in the
same change that made it untrue — a stale fact here is worse than a missing one.

## Overview

- Scaffolded on 2026-10-21 from the `projekt-init` catalog (`core`, `spec-workflow`,
  `dotnet-solution` adapted to a library layout, `tools-dotnet`; CI written for GitHub Actions)
  by extracting the document libraries of an earlier internal repo. Everything client-specific
  was removed on the way — the code, samples and docs here are neutral, and must stay so.
- `src/wwosik.DocLibs.slnx` holds five packages, each with a `.Tests` sibling:
  - `wwosik.DocLibs.DocumentConversions` — cross-platform: PDF, DOCX, PPTX to Markdown, JIRA
    wiki markup; PdfPig only.
  - `wwosik.DocLibs.DocumentConversions.WindowsOnly` — Office automation for `.doc`, `.rtf`,
    `.xls`, `.xlsx`.
  - `wwosik.DocLibs.DocumentGeneration` — shared by the generators: `Brand`, `FrontMatter`,
    `ImageInfo`; all `internal`.
  - `wwosik.DocLibs.DocumentGeneration.Pptx` — Markdown → `.pptx`; Markdig and the Open XML SDK.
  - `wwosik.DocLibs.DocumentGeneration.Pdf` — Markdown → PDF/A-2b; Markdig and PdfPig, own
    layout engine, bundled Liberation fonts.
- Public on GitHub as `wwosik/doc-libs`, MIT, published to nuget.org by tag (see
  `standards.md`, *Build and deployment*).

## Decisions

- **Git, by the human's decision (2026-10-21):** stage changes, never commit — unless the human
  asks; commit, rebase and push likewise only on request. Commit messages never carry
  `Co-Authored-By` or other AI attribution. Recorded as deviations in `standards.md`.
- Package ID prefix and root namespace: **`wwosik.DocLibs`** — lowercase owner prefix on
  purpose, the human's choice (it also makes the nuget.org ID prefix reservable).
- **Client designs never live here.** The only built-in design is the neutral one. How a client
  or project plugs in its own (colours, logo, footer, cover images) is plan 00001 — public
  `Brand` API plus loadable brand packs, kept in the client's own code or package.
- Test sample documents are committed in the repo (`<Project>.Tests/Samples/`), marked binary.
  They are all self-made by `tools/sample-generator` (no third-party documents, ever). Each has
  a `<sample>.expected.md`; `DOCLIBS_UPDATE_EXPECTED=1` makes the tests rewrite them instead of
  comparing — review the diff.
- One cross-platform package rather than one per format; format code lives in namespaces
  (`.Pdf`, `.OpenXml`, `.Jira`). Only platform-bound code gets its own package (`.WindowsOnly`).
- Exception: **generators** (Markdown → document) live in `wwosik.DocLibs.DocumentGeneration.<Format>`
  packages, because their dependencies are heavy and disjoint (Open XML SDK for PPTX; Markdig and
  bundled fonts for PDF). What they share lives in `wwosik.DocLibs.DocumentGeneration`, which
  depends only on the main package. The main package must stay PdfPig-only.
- **No PDF layout library** (PDFsharp/MigraDoc went AGPL with 6.5.0, QuestPDF and iText are
  paid or AGPL): the PDF generator has its own layout engine on PdfPig's writer. It works through
  `IFontMetrics`, so its tests use made-up widths.
- The PPTX generator's built-in template is one master design written as XML strings in
  `TemplateBuilder.cs`, parameterized by a `Brand` — not shipped as a binary. It and the PDF
  layout already draw a brand's footer text, logo, white logo, cover artwork and claim when the
  brand has them; the neutral brand has none, so those paths are untested until plan 00001.
  Layouts are picked by type, or by name when the caller's `CustomTemplateOptions.Layouts` says
  so — never by index.
- Template options are one record per template (`PptxTemplateOptions`, `PdfTemplateOptions`
  subclasses), each with only its own features and its own front-matter keys (`Keys`, `With`). A
  new built-in template adds a subclass and a `Create` entry. Their `Keys` lists are static: an
  instance list would break record equality. The PDF names carry `Pdf`
  (`NeutralPdfTemplateOptions`) so a file using both generators has no ambiguity.
- The PDF fonts are one `FontSet` (Liberation, OFL) — the human plans to change the family later:
  replace `Fonts/*.ttf` and the names in `FontSet.Liberation`. A character the font lacks throws;
  nothing is replaced silently. `PdfOptions.ReplaceUnsupported` gives the caller's hook each
  grapheme cluster (not char, not code point) the face lacks, so an emoji sequence is one call.
  `LayoutEngine.Supported` replaces once per block before measuring (tables before
  `ColumnWidths`, so a cell is not replaced twice; footer and header title once for all pages);
  `CheckGlyphs` after it only checks. Anchors stay Markdig's ids from the source; bookmarks and the
  title metadata (`Layout.Title`) use the replaced text. `IFontMetrics.HasGlyph` takes a `Rune` and
  is false outside the BMP: layout and renderer work in UTF-16 chars.
- Every PDF is PDF/A-2b. PdfPig 0.1.16 gets two things wrong for PDF/A (no `/F` on link
  annotations; `dc:title`/`dc:creator` as plain text in the XMP); `PdfRenderer` fixes both in place
  after `Build()`, keeping every byte offset. Drop those fixes once PdfPig writes them right.
- Generator errors in the document or template (front matter, layout names) throw
  `DocumentConversionException`, like a missing image — not `ArgumentException`.
- The main package is `IsAotCompatible`; a Native AOT app using only `JiraWikiMarkup` publishes
  without warnings. Keep it that way (no reflection, `GeneratedRegex`).
- Converters never touch the file system, except `OfficeConverter`, which must (Office opens
  paths only) and cleans up its per-call working directory.
- Release: SemVer from the pushed tag `v<version>`, nuget.org trusted publishing (no stored API
  key), `--skip-duplicate` so a re-run tag does not fail.

## Traps

- **Office tests only run on Windows.** From WSL, run the Windows SDK (`dotnet.exe test`) against
  the repo's `\\wsl.localhost\...` path with `--artifacts-path` pointing into `.tmp/`, so the
  Windows build does not clobber the Linux `obj/`. The `.doc`/`.xls` samples are made the same
  way (`dotnet.exe run tools/sample-generator/sample-generator.cs -- --office`).
- **Late-bound Excel calls must not pass a culture** to `InvokeMember`: with
  `CultureInfo.InvariantCulture` Excel fails with `TYPE_E_INVDATAREAD` (0x80028018).
- **`MarkdownText.Normalize` trims trailing spaces**, so a two-space hard line break disappears;
  converters emit the backslash form (`\` + newline).
- **`DocumentFormat` is also the Open XML SDK's root namespace.** In files that use the SDK,
  `DocumentFormat.Pptx` resolves to the namespace, not the enum; alias the enum
  (`using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;`). Likewise Markdig has its own
  `MarkdownDocument`.
- **Looking at a generated deck from WSL:** Windows PowerPoint can export it slide by slide via
  COM (`/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe`, `Presentations.Open`, then
  `SaveAs(<folder>, 18)` for PNGs). Opening it that way also shows there is no repair prompt.
- **File-based apps with `#:project` cache their build:** after changing the referenced library,
  `dotnet run file.cs` can silently run the old code. Use `dotnet run --no-cache file.cs`.
- **PdfPig's TrueType fonts hold at most 255 distinct characters** and throw on a glyph the font
  lacks; `PdfRenderer` adds the same font again when a copy is full, `LayoutEngine` checks glyphs
  first. PdfPig ignores the content of an `XmpMetadata` you pass it.
- **Validating PDF/A from WSL:** veraPDF needs Java; Windows has one
  (`C:\Program Files\RedHat\java-15-openjdk-jre-15.0.2-1`). Install veraPDF headless into
  `.tmp/verapdf` (izpack installer with an `auto-install.xml`), then run
  `java.exe -cp ".tmp\verapdf\bin\*" org.verapdf.apps.GreenfieldCliWrapper --flavour 2b <pdf>`.
- **Looking at a generated PDF from WSL:** there is no PDF renderer in WSL; Windows'
  `Windows.Data.Pdf` renders pages to PNG from PowerShell (a throwaway PowerShell script in `.tmp/` does it).
- **Excel cannot overwrite an existing `.xls` on a `\\wsl.localhost\...` path** ("read-only
  document", 0x800A03EC): delete `sample.xls` before running the generator with `--office`.
- **Office stamps the Windows user name** into `.doc`/`.xls` it saves ("last saved by"). The
  samples are public: regenerate them only from an account whose name may appear there.
- **`run.sh sample-generator` is not byte-identical across runs**: it rewrites the existing
  `.pdf`/`.docx`/`.pptx`/`.zip`/`.xlsx` samples with different bytes. Commit only the samples you
  meant to change.
