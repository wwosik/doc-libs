# Project memory

Accumulated facts about this codebase: decisions made, patterns in use, traps worth
remembering. Shared here rather than in any tool's device-local memory, so every agent on
every machine reads the same thing.

Keep it factual and current. When something recorded here stops being true, correct it in the
same change that made it untrue — a stale fact here is worse than a missing one.

## Overview

- Scaffolded on 2026-10-21 from the `projekt-init` catalog (`core`, `spec-workflow`,
  `dotnet-solution` adapted to a library layout, `tools-dotnet`; CI written for GitHub Actions).
  The code, samples and docs here are neutral, and must stay so.
- `src/wwosik.DocLibs.slnx` holds seven packages, each with a `.Tests` sibling:
  - `wwosik.DocLibs.DocumentConversions` — cross-platform: PDF, DOCX, PPTX to Markdown, JIRA
    wiki markup; PdfPig only.
  - `wwosik.DocLibs.DocumentConversions.WindowsOnly` — Office automation for `.doc`, `.rtf`,
    `.xls`, `.xlsx`.
  - `wwosik.DocLibs.DocumentGeneration` — shared by the generators: the public brand API
    (`Brand`, `BrandColors`, `BrandImage`, `BrandFonts`, `BrandFont`, `PptxBrandTemplate`,
    `BrandPack`), the drawing model (`Drawing` …) and `ICodeBlockRenderer`; internally
    `ResolvedBrand`, `BrandRegistry`, `TrueTypeInfo`, `FrontMatter`, `ImageInfo`, and what both
    generators share about slide-layout syntax (`HeadingAttributes`, `LayoutSyntax`) and the
    authoring guides (`AuthoringGuides`).
  - `wwosik.DocLibs.DocumentGeneration.Diagrams` — `MermaidRenderer`: Mermaider (MIT) writes SVG,
    `SvgDrawing` turns it into a `Drawing`. Depends only on the shared package.
  - `wwosik.DocLibs.DocumentGeneration.Charts` — `ChartRenderer`: `chart` blocks (settings, `---`,
    CSV) → `ChartGraphic(ChartSpec, Drawing)`. Depends only on the shared package.
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
- **Client designs never live here.** Plan 00001 made brands public: a client's design is a
  `Brand` built in its own code, or a brand pack (`brand.json` + files) it ships — preferably as
  its own private NuGet package with the pack embedded. The repo has only `Brand.Neutral` and the
  made-up test brand `sample`.
- **Public `Brand` vs internal `ResolvedBrand`:** callers build `Brand` (init-only, few required
  values); generators only ever see `ResolvedBrand` — validated, defaults filled in, colours
  upper-case hex without `#`, images and fonts as `byte[]`. `ResolvedBrand.Resolve` caches per
  `Brand` instance (`ConditionalWeakTable`), and `FontSet.For` caches the parsed fonts per
  `ResolvedFonts`, so a brand reused across documents is checked and parsed once. Validation
  errors are `ArgumentException` naming the property; brand pack errors `InvalidDataException`
  naming the JSON key; front-matter errors stay `DocumentConversionException`.
- **Front matter picks brands only from `BrandRegistry`:** `neutral`, the caller's template's
  brand, and `Options.Brands`. It never names a path — a deliberate safety property for
  untrusted Markdown; keep it.
- `brand.json` is read with System.Text.Json **source generation** (`BrandManifestContext`,
  `UnmappedMemberHandling.Disallow`) to stay AOT-compatible and to reject typos.
- Brand fonts must be **TrueType outlines** (PdfPig cannot embed CFF); `TrueTypeInfo` checks the
  sfnt tag and reads the family name from the `name` table itself, without PdfPig — **name ID 1
  first** (plan 00006): it is what Windows groups the four faces under and what PowerPoint finds an
  embedded font by. Font licences are not checked — documented as the caller's responsibility (the
  human's decision) — except that decks skip faces whose `fsType` is restricted.
- **Decks embed brand fonts by default** (`BrandTemplateOptions.EmbedFonts`, front matter
  `embedFonts`; plan 00006): `FontEmbedding` writes each face as an uncompressed EOT (version
  `0x00020001`, header from `OS/2`, `head`, `name`) in a font part (`application/x-fontdata`),
  listed in `p:embeddedFontLst`, with `embedTrueTypeFonts`. Text family always, table and mono
  families only when a slide's `a:latin` names them. The deck's code font is
  `PptxOptions.CodeFont` ?? the brand's mono family ?? Consolas; mono is embedded only when it is
  the code font. `CustomTemplateOptions` never embeds (no brand).
- **Slide layouts (plan 00005):** a `##` section becomes a `FlowSlide` only if it uses heading
  attributes, a container or alert anywhere, or a top-level tag paragraph — every other section
  takes the old `ContentSlide`/`VisualSlide` path unchanged, so old decks stay as they were. Keep
  it that way: new behaviour that would change old decks (code-only table cells as labels,
  code-span lists as labelled tags) is limited to flow slides or containers. `==mark==` and
  highlighted table rows are the exception, because `==` had no meaning before. `DeckParser.Parse`
  buffers each section's blocks to decide; `Walk` serves both paths through `ITarget`
  (`SectionBuilder`, `FlowCollector`). The writer measures by calling the same layout methods with
  a `null` canvas.
- Heading attributes are parsed by us (`HeadingAttributes`), not by Markdig's generic attributes,
  so `{…}` elsewhere stays literal. Container names and alert kinds are checked by `LayoutSyntax`,
  so PDF and PPTX reject the same mistakes. Both pipelines add only `CustomContainerParser`
  (block), not `UseCustomContainers()`, whose inline `::x::` form would split `std::vector::size`.
- Flow-slide colours are a `SlidePalette`: from the brand (`SlidePalette.From`), or theme-colour
  references for caller templates (`SlidePalette.Scheme`). `BrandColors.Highlight` (default theme
  accent 2) and `BrandColors.Labels` (status label colours by category) exist for slides only.
- **Status label categories** are assigned in front matter (`labels.success: Done; Delivered`).
  `FrontMatter.Apply` skips every `labels.*` key, so templates never see them; `FrontMatter.Labels()`
  checks them — both generators call it, the PDF one only to reject the same mistakes.
- **Authoring guides (plan 00008):** `PptxGenerator.AuthoringGuide(options)` and
  `PdfGenerator.AuthoringGuide(options)`. Templates are embedded `AuthoringGuide/*.md` with
  `{{placeholders}}`, filled by `AuthoringGuides` (front matter table from the template's `Keys`,
  the brands from `BrandRegistry.Expected`, characters to avoid from the fonts' `cmap` via
  `TrueTypeInfo.HasCharacter`, each with the first replacement every face has). Renderers add
  their section through the default interface member `ICodeBlockRenderer.AuthoringGuide`. A change
  to the Markdown syntax, front matter or limits updates the guides in the same change.
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
- **Diagrams are our own vector `Drawing`, never images** (plan 00003): the PDF renderer writes
  path operators straight into `PdfPageBuilder.CurrentStream.Operations` inside `q … Q` (PdfPig
  has no curve API), the PPTX writer makes a group of preset shapes, custom geometry and text
  boxes. Text in a drawing is placed by the generator in the real font (table faces), shrunk to
  `MaxWidth` down to 70 %. `RenderCodeBlock` was replaced by `CodeBlockRenderers`.
- **Mermaider's SVG is an implementation detail.** `SvgStyle`/`SvgDrawing` understand exactly
  what it writes (CSS variables, `color-mix`, `rem`, markers, `tspan`, `translate`) and throw on
  anything else. Its version is pinned; the golden `mermaid-<type>.expected.txt` files catch
  changes on an update. Types outside `MermaidRenderer.Supported` are refused by Mermaider's
  `AllowedDiagrams` with a `MermaidParseException` "… is not in the allowed set", which the
  renderer turns into "stay code" — the message match is the only way to tell it apart.
- **PDF page breaks (plan 00004):** a heading reserves `KeepHeight` of the blocks after it (the
  start of a paragraph, list, code or table; a whole visual; chained headings), and visuals below
  headings are capped at a page minus those headings (`chainTop`). `KeepHeight` must measure
  exactly what is later drawn, so paragraphs, headings and tables are replaced and wrapped once
  and cached by block reference (`paragraphs`, `headings`, `tables`) — which also keeps
  `ReplaceUnsupported` to one call per element. `Paragraph` places lines in chunks with at least
  two lines on each side of a break. `Ensure` has a 1e-6 tolerance because a capped visual fills
  the page exactly.
- **No PDF layout library** (PDFsharp/MigraDoc went AGPL with 6.5.0, QuestPDF and iText are
  paid or AGPL): the PDF generator has its own layout engine on PdfPig's writer. It works through
  `IFontMetrics`, so its tests use made-up widths.
- The PPTX generator's built-in template is one master design written as XML strings in
  `TemplateBuilder.cs`, parameterized by a `ResolvedBrand` — not shipped as a binary. Brand images
  are fitted into fixed boxes keeping their aspect ratio (`ImageBox.Fit`); the cover artwork sits
  bottom right **below the subtitle** — at 42 % of the height, as in the PDF, it covered the
  title on 16:9. Layouts are picked by type, or by name when the caller's
  `CustomTemplateOptions.Layouts` (or a brand's `PptxBrandTemplate`) says so — never by index.
- Template options are one record per template kind: `BrandTemplateOptions` and
  `CustomTemplateOptions` for PPTX, `BrandPdfTemplateOptions` for PDF — each with only its own
  features and front-matter keys (`Keys`, `With`). Their `Keys` lists are static: an instance list
  would break record equality. The PDF names carry `Pdf` so a file using both generators has no
  ambiguity. `BrandImage`, `BrandFont`, `PptxBrandTemplate` and `BrandColors` override equality to
  compare content, so options holding a brand stay comparable.
- The PDF fonts are one `FontSet`: Liberation (OFL) by default, or a brand's (`FontSet.For`), with
  Liberation Mono for mono faces the brand lacks. To change the default family: replace
  `Fonts/*.ttf` and the names in `FontSet.Liberation`. Table cells use the `Table*` faces
  (`RunStyle.Table`, set only by the layout); without brand table fonts they are the **same
  `byte[]`** as the text faces, and `PdfRenderer` keys its font copies by file (reference), so
  nothing is embedded twice. `ResolvedFonts.HasTableFonts` is that reference check. A character the font lacks throws;
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
  `DocumentConversionException`, like a missing image — not `ArgumentException`. A code-block
  renderer's exception is wrapped into one too (`CodeBlockRenderers.Render`, plan 00007).
- The main package is `IsAotCompatible`; a Native AOT app using only `JiraWikiMarkup` publishes
  without warnings. Keep it that way (no reflection, `GeneratedRegex`).
- Converters never touch the file system, except `OfficeConverter`, which must (Office opens
  paths only) and cleans up its per-call working directory.
- Release: the version is the pushed tag `v<yyMM>.<d>.<H>[.<r>]` (Europe/Berlin; SemVer was
  dropped on 2026-10-23 for the date scheme the tags already used), nuget.org trusted publishing
  (no stored API key), `--skip-duplicate` so a re-run tag does not fail. The version says nothing
  about compatibility, so `CHANGELOG.md` does (plan 00009); `release.yml` checks the tag's format
  and its changelog section and makes the section the GitHub release's notes.
- **Package READMEs** (plan 00009): `src/Directory.Build.props` packs each package's `docs/` page
  as its `README.md` (an inline `RoslynCodeTaskFactory` task), rewriting links to sibling pages to
  GitHub at the release tag (`GITHUB_REF_NAME` when `GITHUB_REF_TYPE` is `tag`, else `main`) and
  adding a line pointing to the repo. `CHANGELOG.md` is packed next to it.

- **Text is measured by `TextMetrics`** (plan 00011): ems per character class, ×1.08 bold, wrapped
  between words. Table columns are at least their longest word; the table font steps down to 10 pt
  before words break. `EstimateLines` uses it too, so every height in decks is this estimate.
- **Fitting (plan 00013):** `DeckWriter.Write` passes each slide through `Fit` (`DeckWriter.Fitting.cs`)
  before writing: measured without a canvas, shrunk by steps (content: `normAutofit` font scale to
  12 pt; visual: text and table sizes; flow: `FlowStyle.Scale` 1 → 0.75), then split into parts with
  the same title (`Pack` over units: list groups, table rows with the header, rows/tag items, whole
  blocks). A slide that fits goes through unchanged — keep it that way, existing decks must not move.
- **Dark templates (plan 00010):** `PptxBrandTemplate.Tone` (`Auto` = `TemplateTone.Detect`: master
  title colour via colour map and theme, else solid background) picks `SlidePalette.ForDark`, which
  sets `DarkSlides`: every flow slide gets `FlowStyle.Dark`, text boxes and tables an explicit white,
  fills are translucent (`SlideColor.Alpha`). Mermaid and charts get `ResolvedBrand.DarkSlideBrand`,
  so diagrams are drawn for the dark slides. `SlideColors` merge over `Colors` per property
  (`ResolvedBrand.SlideBrand`, cached per brand); PDFs never see them. The generator now parses
  after opening the template, because the tone decides the renderers' brand.
- **`[words]{.success}`** is found by `ColouredWords` (shared) in the runs' text after Markdig, with
  code masked — Markdig leaves an unresolved `[…]` as text. Both generators call it.
- **Charts (plan 00016):** `ChartWriter` writes the chart part as an XML string (schema order matters:
  the validator checks it) and the data as an embedded `.xlsx` (`Sheet1`, categories in column A).
  Bars and areas get `c:min` at zero. PDFs draw the `Drawing` the renderer made.

## Traps

- **The built-in master's relationship ids are fixed:** layouts 1–4 are `rId1`–`rId4`, the theme
  `rId5`, the master logo `rId6`; layout 5 (*Title Only Dark*) is `rId7`
  (`TemplateBuilder.LayoutRelationship`). It shares the section layout's `LogoOnDark` image part,
  so the media count and image numbering of existing decks did not change.
- **`brand-dark` is built at test time** (`DarkTemplate` in the PPTX tests) from the built-in master:
  gradient background, white text styles, a footer placeholder and two copied layouts. There is no
  binary sample for it.
- **The PowerPoint export (`.tmp/scripts/export.ps1`) hangs** while another PowerPoint instance has a
  dialog open; with no PowerPoint running it works.
- **A `---` right after a paragraph line is a setext heading,** not a thematic break: the columns of
  `::: columns` need a blank line before each `---`.
- **PdfPig's subsetter crashes on some fonts** (`ArgumentOutOfRangeException` in
  `TrueTypeGlyphTableSubsetter`): DejaVu Sans Mono does, which is why the sample brand's mono face
  is Inconsolata. A brand whose font hits it cannot be used for PDFs; decks are unaffected.

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
  The installer is `https://software.verapdf.org/releases/verapdf-installer.zip` (no `unzip` in
  WSL: extract with Python's `zipfile`); pass the target as a Windows path (`wslpath -w`).
- **Looking at a generated PDF from WSL:** there is no PDF renderer in WSL; Windows'
  `Windows.Data.Pdf` renders pages to PNG from PowerShell (a throwaway PowerShell script in `.tmp/` does it).
- **Excel cannot overwrite an existing `.xls` on a `\\wsl.localhost\...` path** ("read-only
  document", 0x800A03EC): delete `sample.xls` before running the generator with `--office`.
- **Office stamps the Windows user name** into `.doc`/`.xls` it saves ("last saved by"). The
  samples are public: regenerate them only from an account whose name may appear there.
- **Looking at decks and PDFs from WSL:** copy-able throwaway scripts in `.tmp/scripts/`
  (`export.ps1` for PowerPoint → PNG, `pdf2png.ps1` for `Windows.Data.Pdf`); they are not in git.
- **`run.sh sample-generator` is not byte-identical across runs**: it rewrites the existing
  `.pdf`/`.docx`/`.pptx`/`.zip`/`.xlsx` samples with different bytes. Commit only the samples you
  meant to change.
