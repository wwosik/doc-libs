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
- `src/wwosik.DocLibs.slnx` holds nine packages, each with a `.Tests` sibling:
  - `wwosik.DocLibs.DocumentConversions` — cross-platform: PDF, DOCX, PPTX to Markdown, JIRA
    wiki markup; PdfPig only.
  - `wwosik.DocLibs.DocumentConversions.WindowsOnly` — Office automation for `.doc`, `.rtf`,
    `.xls`, `.xlsx`.
  - `wwosik.DocLibs.DocumentConversions.Svg` — SVG to PNG with SkiaSharp, `SvgImageConverter`, and
    deck previews (`DeckPreview`, E07-S03).
  - `wwosik.DocLibs.DocumentGeneration` — shared by the generators: the public brand API
    (`Brand`, `BrandColors`, `BrandImage`, `BrandFonts`, `BrandFont`, `PptxBrandTemplate`,
    `BrandPack`), the drawing model (`Drawing` …), `ICodeBlockRenderer` and the AI tool (`DocumentTool`,
    `IMarkdownGenerator`); internally
    `ResolvedBrand`, `BrandRegistry`, `TrueTypeInfo`, `TrueTypeMetrics`, `FrontMatter`, `ImageInfo`,
    the bundled Liberation fonts (`StandInFonts`, `Fonts/*.ttf`, since E07-S02), and what both
    generators share about slide-layout syntax (`HeadingAttributes`, `LayoutSyntax`) and the
    authoring guides (`AuthoringGuides`).
  - `wwosik.DocLibs.DocumentGeneration.Diagrams` — `MermaidRenderer`: Mermaider (MIT) writes SVG,
    `SvgDrawing` turns it into a `Drawing`. Depends only on the shared package.
  - `wwosik.DocLibs.DocumentGeneration.Charts` — `ChartRenderer`: `chart` blocks (settings, `---`,
    CSV) → `ChartGraphic(ChartSpec, Drawing)`. Depends only on the shared package.
  - `wwosik.DocLibs.DocumentGeneration.Pptx` — Markdown → `.pptx`; Markdig and the Open XML SDK;
    and any `.pptx` → one HTML page (`HtmlDeck`, E07-S02).
  - `wwosik.DocLibs.DocumentGeneration.Docx` — Markdown → `.docx` (E09); Markdig and the Open XML
    SDK, like Pptx, sharing Open XML code with it as linked source (`src/Shared/OpenXml/`).
  - `wwosik.DocLibs.DocumentGeneration.Pdf` — Markdown → PDF/A-2b; Markdig and PdfPig, own
    layout engine, the Liberation fonts from the shared package; SkiaSharp only for `.grey` images (E04-S05).
- Public on GitHub as `wwosik/doc-libs`, MIT, published to nuget.org by tag (see
  `standards.md`, *Build and deployment*).

## Decisions

- **Git and spec workflow (2026-11-13):** the repo follows the template's spec workflow —
  epics `E01`, stories `E01-S01`, technical `T01-S01`, claims in `spec/55-in-progress/<agent-id>/`
  committed on `main` — and its Git rules: commit finished work, rebase and push after a major
  task. This replaced the stage-only rule of 2026-10-21. Commit messages never carry
  `Co-Authored-By` or other AI attribution. Plans 00001–00019 became stories; `spec/README.md`
  maps the old numbers.
- Package ID prefix and root namespace: **`wwosik.DocLibs`** — lowercase owner prefix on
  purpose, the human's choice (it also makes the nuget.org ID prefix reservable).
- **Client designs never live here.** E01-S01 made brands public: a client's design is a
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
  first** (E01-S03): it is what Windows groups the four faces under and what PowerPoint finds an
  embedded font by. Font licences are not checked — documented as the caller's responsibility (the
  human's decision) — except that decks skip faces whose `fsType` is restricted.
- **Decks embed brand fonts by default** (`BrandTemplateOptions.EmbedFonts`, front matter
  `embedFonts`; E01-S03): `FontEmbedding` writes each face as an uncompressed EOT (version
  `0x00020001`, header from `OS/2`, `head`, `name`) in a font part (`application/x-fontdata`),
  listed in `p:embeddedFontLst`, with `embedTrueTypeFonts`. Text family always, table and mono
  families only when a slide's `a:latin` names them. The deck's code font is
  `PptxOptions.CodeFont` ?? the brand's mono family ?? Consolas; mono is embedded only when it is
  the code font. `CustomTemplateOptions` never embeds (no brand).
- **Slide layouts (E02-S01):** a `##` section becomes a `FlowSlide` only if it uses heading
  attributes, a container or alert anywhere, or a top-level tag paragraph — every other section
  takes the old `ContentSlide`/`VisualSlide` path unchanged, so old decks stay as they were. Keep
  it that way: new behaviour that would change old decks (code-only table cells as labels,
  code-span lists as labelled tags) is limited to flow slides or containers. `==mark==` and
  highlighted table rows are the exception, because `==` had no meaning before. `DeckParser.Parse`
  buffers each section's blocks to decide; `Walk` serves both paths through `ITarget`
  (`SectionBuilder`, `FlowCollector`). The writer measures by calling the same layout methods with
  a `null` canvas.
- Heading attributes are parsed by us (`HeadingAttributes`), not by Markdig's generic attributes,
  so `{…}` elsewhere stays literal — except right after an image (E04-S05, below). Container names and alert kinds are checked by `LayoutSyntax`,
  so PDF and PPTX reject the same mistakes. Both pipelines add only `CustomContainerParser`
  (block), not `UseCustomContainers()`, whose inline `::x::` form would split `std::vector::size`.
- Flow-slide colours are a `SlidePalette`: from the brand (`SlidePalette.From`), or theme-colour
  references for caller templates (`SlidePalette.Scheme`). `BrandColors.Highlight` (default theme
  accent 2) and `BrandColors.Labels` (status label colours by category) exist for slides only.
- **Status label categories** are assigned in front matter (`labels.success: Done; Delivered`).
  `FrontMatter.Apply` skips every `labels.*` key, so templates never see them; `FrontMatter.Labels()`
  checks them — both generators call it, the PDF one only to reject the same mistakes.
- **Authoring guides (E05-S02):** `PptxGenerator.AuthoringGuide(options)` and
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
- **Diagrams are our own vector `Drawing`, never images** (E04-S01): the PDF renderer writes
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
- **PDF page breaks (E03-S01):** a heading reserves `KeepHeight` of the blocks after it (the
  start of a paragraph, list, code or table; a whole visual; chained headings), and visuals below
  headings are capped at a page minus those headings (`chainTop`). `KeepHeight` must measure
  exactly what is later drawn, so paragraphs, headings and tables are replaced and wrapped once
  and cached by block reference (`paragraphs`, `headings`, `tables`) — which also keeps
  `ReplaceUnsupported` to one call per element. `Paragraph` places lines in chunks with at least
  two lines on each side of a break. `Ensure` has a 1e-6 tolerance because a capped visual fills
  the page exactly.
- **Contents, heading numbers, captions (E03-S02):** `HeadingNumbering` (shared) is the one numbering
  rule for every format: top = highest level after the cover outside `:::` containers, three levels
  numbered, a number has one part per numbered heading still open above it (a skipped level never
  gives 0, `##` before the first `#` are `1`, `2`, that `#` is `3`). `{.unnumbered}` is known to every
  format (`SplitDeep` reads it and `.newpage` on `###`+, in every format since E03-S03).
  `FrontMatter.DocumentKeys` (`toc`, `tocDepth`, `headingNumbers`, `justify`) are checked and skipped by
  `FrontMatter.Apply` for templates that do not list them (decks). The PDF parser numbers headings
  (`HeadingBlock.Number`), collects `DocumentModel.Contents` (entries point at the `HeadingBlock`s,
  so the layout replaces each heading's text once via `headingRuns`), and attaches `Caption`s to
  `ImageBlock`/`TableBlock`/`DrawingBlock`, taking the caption paragraph (`captionParagraphs`). The
  **contents are one layout pass**: the page-number column is three widest digits wide, entries record
  a `ContentsSlot`, and `ContentsPages` writes numbers and dot leaders from `anchors` after the body,
  like `Decorate`. Caption heights are added to `KeepHeight`, `VisualRoom` and the visual's `Ensure`;
  with no caption every sum adds 0, which keeps old PDFs byte-identical. Decks strip the prefix in
  `DeckParser.WalkMarked` (`Caption`), knowing drawn fences from `drawn`. Images' standalone word
  `caption` is a setting, so a title "with a caption under it" loses that word.
- **PDF footnotes, justification, page breaks (E03-S03):** a reference is a `TextRun` with `Footnote`
  (its number, Markdig's `Order`), kept a run of its own and a `Piece` of its own (`Merge` compares
  `Footnote`); `DocumentModel.Footnotes` holds the notes as text paragraphs. The layout keeps a
  **per-page `bottom`** (`geometry.Bottom` less the current page's notes, `NotesHeight(pageNotes)`):
  every check of the current page (`Ensure`, `Gap`, `Paragraph`'s room, the table's room, `VisualRoom`
  under headings) uses `bottom`; checks of a fresh page keep `geometry.Bottom`. A line that advances
  `Ensure`s its height plus `NotesExtra(NewNotes(line))` and then `PlaceNotes` them; `Row`, `CaptionAt`
  and the cover place theirs without a check of their own; headings collect the notes of what they keep
  (`keptNotes`). Paths without notes take the old formulas (or add exactly `0`), so PDFs without
  footnotes stay byte-identical — keep it that way. A note taller than a third of the page reserves
  two lines (`Reserve`) and grows into the page's free room in `FinishPage` (called by `NewPage` and at
  the end), which draws the notes and carries the rest (`carriedNotes`) to the next page. Anchors
  `footnote:n` / `footnote-reference:n` (':' is in no Markdig id). **Justification never changes line
  breaks:** `Wrap` is untouched (`TextLine.EndsWithBreak` marks hard breaks and note paragraph ends),
  `Line(justify: width)` spreads the room over the spaces (`Stretch`, ragged past 3×) and places each
  word on its own (`Words`). `PageBreakBlock` and `HeadingBlock.NewPage` break unless `AtTop`; a trailing
  empty page from a break is removed (`brokenPage`). Markdig: a footnote definition inside a `:::`
  container (an unclosed `::: pagebreak` holds the rest of the document) is no definition.
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
  Liberation Mono for mono faces the brand lacks. The files live in DocumentGeneration's `Fonts/`
  (`StandInFonts`, one cached `byte[]` per file) since E07-S02, which also measures HTML decks with them. Table cells use the `Table*` faces
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
  renderer's exception is wrapped into one too (`CodeBlockRenderers.Render`, E05-S01).
- The main package is `IsAotCompatible`; a Native AOT app using only `JiraWikiMarkup` publishes
  without warnings. Keep it that way (no reflection, `GeneratedRegex`).
- Converters never touch the file system, except `OfficeConverter`, which must (Office opens
  paths only) and cleans up its per-call working directory.
- Release: the version is the pushed tag `v<yyMM>.<d>.<H>[.<r>]` (Europe/Berlin; SemVer was
  dropped on 2026-10-23 for the date scheme the tags already used), nuget.org trusted publishing
  (no stored API key), `--skip-duplicate` so a re-run tag does not fail. The version says nothing
  about compatibility, so `CHANGELOG.md` does (T01-S01); `release.yml` checks the tag's format
  and its changelog section and makes the section the GitHub release's notes.
- **Package READMEs** (T01-S01): `src/Directory.Build.props` packs each package's `docs/` page
  as its `README.md` (an inline `RoslynCodeTaskFactory` task), rewriting links to sibling pages to
  GitHub at the release tag (`GITHUB_REF_NAME` when `GITHUB_REF_TYPE` is `tag`, else `main`) and
  adding a line pointing to the repo. `CHANGELOG.md` is packed next to it.

- **Text is measured by `TextMetrics`** (E02-S02; in the shared package since E09-S01, on
  `(string Text, bool Bold)` pieces, `"\n"` a break, widths in EMU; decks call it through
  `RunMetrics`, which knows what of a `TextRun` is bold): ems per character class, ×1.08 bold, wrapped
  between words. Table columns are at least their longest word; the table font steps down to 10 pt
  before words break. `EstimateLines` uses it too, so every height in decks is this estimate.
- **Fitting (E02-S04):** `DeckWriter.Write` passes each slide through `Fit` (`DeckWriter.Fitting.cs`)
  before writing: measured without a canvas, shrunk by steps (content: `normAutofit` font scale to
  12 pt; visual: text and table sizes; flow: `FlowStyle.Scale` 1 → 0.75), then split into parts with
  the same title (`Pack` over units: list groups, table rows with the header, rows/tag items, whole
  blocks). A slide that fits goes through unchanged — keep it that way, existing decks must not move.
- **Dark templates (E01-S04):** `PptxBrandTemplate.Tone` (`Auto` = `TemplateTone.Detect`: master
  title colour via colour map and theme, else solid background) picks `SlidePalette.ForDark`, which
  sets `DarkSlides`: every flow slide gets `FlowStyle.Dark`, text boxes and tables an explicit white,
  fills are translucent (`SlideColor.Alpha`). Mermaid and charts get `ResolvedBrand.DarkSlideBrand`,
  so diagrams are drawn for the dark slides. `SlideColors` merge over `Colors` per property
  (`ResolvedBrand.SlideBrand`, cached per brand); PDFs never see them. The generator now parses
  after opening the template, because the tone decides the renderers' brand.
- **`[words]{.success}`** is found by `ColouredWords` (shared) in the runs' text after Markdig, with
  code masked — Markdig leaves an unresolved `[…]` as text. Both generators call it.
- **Charts (E04-S02):** `ChartWriter` writes the chart part as an XML string (schema order matters:
  the validator checks it) and the data as an embedded `.xlsx` (`Sheet1`, categories in column A).
  Bars and areas get `c:min` at zero. PDFs draw the `Drawing` the renderer made.

- **Generation notes (E05-S03):** both `Generate`s return a `GenerationResult` (`Pages`, `Notes`).
  An internal `GenerationNotes` per call is passed to parser, layout and writer; it orders notes by
  line (0 last) and drops exact repeats. Every `NoteCodes` constant needs an entry in `NoteCodes.All`
  (formats and the guides' advice) and a row in the docs table, or a test fails. A slide carries its
  heading's `Line` (set by `DeckParser.Section`, kept on split parts). Renderers report through
  `CodeBlockRequest.Report`, which is excluded from the request's equality. Notes never change the
  output: the files stay as before for the same input.
- **Check (E05-S04):** `PptxGenerator.Check`, `PdfGenerator.Check`, `DocxGenerator.Check` all go through
  `DocumentCheck.Run` (shared): front matter, then a pass over the Markdown with its own Markdig pipeline
  (image sources, style rules, counts; bad images replaced by their alt text by source span), then the
  generator's internal `Write`/`Lay` with one `GenerationNotes` and the renderers wrapped to count drawings.
  **Check never throws for document errors** (`DocumentConversionException` → `CheckResult.Error`, its
  `Reason`); `ArgumentException` still throws. **Style rules come only from Check**, never from `Generate`:
  their codes are in `NoteCodes.CheckOnly`, and the guides list them apart. The rules work on the Markdown,
  not the generators' models, so the PDF block model still has no lines. Decks collect the written slides
  through `DeckWriter.Written` (kinds, talk time); a PDF check stops before `PdfRenderer.Render`
  (`PdfGenerator.Renders` is the thread-static test seam). The limits live on the options (`Checks`), so
  the guides' *Limits* section and the AI tool (E05-S05) see the same ones.
- **AI tool (E05-S05):** `DocumentTool` lives in the shared package and knows no generator: the
  generators join through `IMarkdownGenerator` (`PptxGenerator.ForOptions`, `PdfGenerator.ForOptions`,
  `DocxGenerator.ForOptions`, each a private `Bound` class over the static methods). The shared package
  must never reference a generator (a test checks its assembly references). **The reply shape is a public
  contract** (statuses `ok`, `checked`, `error`, keys, property order): a change is a breaking change in the
  CHANGELOG. Schema and replies are written by hand with `Utf8JsonWriter` (`UnsafeRelaxedJsonEscaping`,
  `NewLine = "\n"`), arguments read with `JsonDocument`, never a reflection serializer. The combined guide
  moves each format guide's ATX headings down by one outside fenced code (`DocumentTool.HeadingsDown`).
  The shared test project references the three generators for the tool's tests and aliases the Open XML
  assemblies (`extern alias OpenXml`, target `AliasOpenXml` in its csproj) so that `DocumentFormat` stays the
  enum there. A change to any guide also changes `tool-guide.expected.md` (PPTX + PDF) in the shared tests.
- **Markdown foundations (E06-S02):** both pipelines add `UseDocumentSyntax()` (footnotes, task
  lists, definition lists; `MarkdownSyntax` in DocumentGeneration, which therefore references
  Markdig). `FootnoteGroup` is skipped where blocks are walked and handled apart: PDFs print the
  notes at the foot of their page (E03-S03, above); decks number references per `##` section
  (`DeckParser.footnotes`, reset in `Section`) and give each slide the `SlideFootnote`s its runs refer
  to (`Slide.References()` scans `RunStyle.Superscript` runs, kept unmerged). `DeckWriter.ContentArea`
  subtracts the current slide's footnote room (`footnotes` field, set in `Fit` and before writing),
  so fitting and placement agree. Language: `DocumentLanguage.Resolve` in both `Generate`s; decks tag
  every `a:rPr`/`a:endParaRPr` in `DeckWriter.Tag` after each slide; PDFs splice `/Lang` into the
  catalog and grow the XMP through `PdfRenderer.Splice`, which shifts the xref offsets.
  `Drawing.Validate()` runs in every drawing writer.
- **Typography (E06-S03):** `Typography` (shared, internal) is built per document by `Typography.For` (null = off;
  `Typography.IsOn`: front matter `typography` over the options) and handed to `DeckParser`, `MarkdownParser` and
  `DocxContext`; each parser runs it inside its `Icons(...)` wrapper, i.e. after `ColouredWords` and `IconSyntax`, on
  every run list, plus the front-matter `title`/`author` and the DOCX cover. It never changes the number or order
  of runs; masked runs (code, labels, superscripts, Harvey balls, DOCX tags and footnotes) are `\u0001` in its
  working string, and addresses and HTML tags are masked by regex. A multi-character replacement (`--`, `...`)
  must lie within one run. The font check is a `Func<int, bool>`: PDFs `FontSet.HasGlyph` over the eight text and
  table faces, decks and DOCX `ResolvedFonts.HasEveryTextFace`, everything without brand fonts. Drawings
  (renderers, `VisualContainers`) are never typeset.
- **Speaker notes and transitions (E07-S01):** `DeckParser.Section` takes top-level `::: notes` out
  of a section before the slide kind is decided and sets `Slide.SpeakerNotes` and `Slide.Transition`
  on every slide of the section; `Fit` copies both to every part. A new per-slide property must be
  copied in both places. `DeckWriter.Write` adds `p:transition` after each slide (`Transitions`, Morph
  as `mc:AlternateContent` with a fade fallback) and writes the notes pages **after** the loop, with the
  notes master (the template's, else `NotesMasterBuilder` on a copy of the deck's theme) added only
  then — so decks without notes keep their parts. `Canvas` takes the part hyperlinks go on (the notes
  page). PDFs skip top-level `::: notes` in `MarkdownParser.Parse` and accept front matter
  `transition`. `PptxConverter` reads the notes page's body placeholder as `::: notes`.
- **Builds (E07-S05):** `BuildMarker` (shared) recognises a marker paragraph from its inlines (Markdig
  releases `Lines` after inline parsing; an escaped `\{` is no marker) and `BuildMarker.Check` validates
  every marker of the document once, so both formats throw alike; the PDF then skips marker paragraphs.
  `DeckParser.WalkMarked` attaches a marker to the next block (`BuildMark`, numbered by the block's
  `Span.Start`): on `TextParagraph.Build`, `FlowBlock.Build`, `VisualSlide.VisualBuild`, set by the
  `ITarget`s from the parser's current `build`. A new per-paragraph or per-block property must survive
  `Fit`'s pieces (`with`, not `new`). The writer records targets in `Canvas` (`Building` scopes for
  shapes, `AddText` for paragraphs, `Item` for cards/rows/columns/images) and `TimingWriter` appends
  `p:timing` **after** `p:transition` (schema order: `transition`, then `timing`).
- **The SDK's relationship ids are random** (`R` + 16 hex digits), so two runs never give the same
  `.pptx` bytes; compare decks part by part with those ids masked (`PresentingTests.Parts`). PDFs have
  a random `/ID` in the trailer.

- **Image attributes (E04-S05):** `ImageAttributes.Take(link, line, format)` (shared) reads a `{…}`
  at the start of the literal text after an image, takes it off those literals (Markdig splits that
  text: a lone `=` is a literal of its own) and caches the result on the `LinkInline` (`SetData`), so
  asking twice is safe. Both parsers call it where their `RunCollector` meets an image; as alt text
  (headings, cells) the attributes only give `attribute-ignored`. `ImageAttributes.Crop` is the one
  crop computation: decks write it as `a:srcRect` (plus `ellipse` for `.round`, `a:grayscl` for
  `.grey`), PDFs draw the whole image larger inside an `ImageItem.Clip` (`q`, path, `W n`, image,
  `Q`). PDF `.grey` converts pixels in `GreyImages` (SkiaSharp, cached per resource in the parser,
  so the grey copy is one `ImageItem.Key`). Third-party licences of the PDF package are in its
  packed `THIRD-PARTY-NOTICES.txt`; a story that adds a dependency adds its entry there.
- **Template roles (E01-S06):** `PptxTemplateRoles` (shared package, validated by `Problem()` in
  `ResolvedBrand.Resolve` and in `PptxGenerator.Generate` for caller templates) is resolved once per
  deck by `TemplateRoles.Resolve` in the `DeckWriter` constructor: `Layout(LayoutRole)` and
  `Placeholders(layout)` (`LayoutPlaceholders`: bodies in order, subtitle, byline, chapter number,
  pictures, charts, chart texts, `Excluded`). The built-in master declares its roles in
  `TemplateBuilder.Roles`. The old lookups (content, title-only, section, cover) keep their code and
  only skip `Excluded` placeholders (`DeckWriter.Bodies`), so templates without declarations are
  untouched. E01-S07 fills chapter numbers and `layout="…"` slides from there, E02-S05 the slide kinds. Text levels act only in the
  content slide's body placeholder (`Paragraph(…, levels:)`, `LevelParagraph`, `MappedLevel`); `###`
  paragraphs carry `TextParagraph.Heading`, and the writer drops the parser's bold under text levels.
  Master guides (`p15:guide/@pos`) are in eighths of a point (1587.5 EMU), a guide without `orient`
  is horizontal, and each guide needs a `p15:clr` child to validate — unit to be confirmed in
  PowerPoint.
- **Chart source (E04-S05):** `ChartSpec.Source`; the word before it is `GeneratedTexts.Source`,
  reaching renderers as `CodeBlockRequest.Texts` (set by both parsers) and `DeckWriter` as a
  constructor argument. A chart with a source is 18 px taller in PDFs; in decks the frame gives up
  one line for a text box under it. A layout's chart-source placeholder (E01-S06) would go in
  `DeckWriter.Chart`.
- **QR codes and icons (E04-S06):** `qr:` image URLs become `QrDrawing.ForImage` drawings (QRCoder's
  module matrix only, byte mode, ECC M) in both parsers, before the resource lookup. Icons come from
  `IIconSet` (`PptxOptions.Icons` / `PdfOptions.Icons`, default `TablerIcons.Outline`, `null` = off);
  `IconSyntax.Apply` runs after `ColouredWords` and makes an icon a run of its own with empty `Text`
  and `TextRun.Icon` set, so both parsers' `Trim` treat icon runs as not blank. Decks draw icons only
  as `Card.FigureIcon` and in `IconListBlock` (which makes a section a flow slide, like other layouts);
  every other icon run is skipped by `DeckWriter` and reported once per slide in `DeckParser.Section`.
  PDFs draw them inline (`RunStyle.Icon`, `Piece.Icon`), as list markers (`ListBlock.Icons`) and as
  `IconBlock`; headings, cells and the cover drop them (`MarkdownParser.NotDrawn`). The Tabler data is
  `Icons/tabler-outline.txt.gz` from `tools/icon-import`, committed; a test holds it to 1 MB and its
  count to the number on the DocumentGeneration docs page. The guides' `{{icons}}` comes from
  `AuthoringGuides.Icons` and is empty with `Icons = null`. The shared package's licences are in its
  own `THIRD-PARTY-NOTICES.txt`.

- **Drawn containers (E04-S07):** `VisualContainers` (shared) owns the `:::` containers drawn as a
  `Drawing` (`timeline`, `gantt`, and since E04-S08 `steps`, `riskmatrix`, which take no arguments): `Names` joins
  `LayoutSyntax.Containers`, `CheckContainer` hands them to `VisualContainers.Check` (`key=value`
  arguments per name in `Arguments`), and both parsers call `Draw(container, VisualContext)` and add a
  `DrawingBlock` — decks in `DeckParser.Container` (a flow slide like any container), PDFs in
  `MarkdownParser.Add` instead of showing the content plainly. `VisualContext` has the brand (decks: the
  dark-slide brand when `onDark`, set by a `.dark` heading or `::: panel dark`), today (`TimeProvider`
  in UTC, `today=` wins), `GeneratedTexts`, `LineOf` and the format. `DrawingTexts` makes `DrawingSpan`s
  from inlines and breaks lines by an estimate; `SchedulePeriod` parses and shows ISO dates without
  `CultureInfo`. `GeneratedTexts`' static month lists are declared before `English`/`German` (static
  initialisation order). The PPTX guide's word limit is 3,500 (the human, 2026-11-14; it was 2,600, and
  E04-S07 had trimmed other prose to fit it).
- **Harvey balls (E04-S08):** `HarveyBall.Quarters` reads a table cell whose only inlines are unescaped
  literals spelling `[n/4]` or `[1/2]` (Markdig leaves an unresolved `[3/4]` as literals). Decks:
  `DeckParser.Cell` returns one `TextRun { Symbol = true }` with the glyph, and `DeckWriter.Run` names
  `HarveyBall.SymbolFont` (`Segoe UI Symbol`) in `a:latin`, `a:ea` and `a:cs` instead of the table
  font; it is never embedded. PDFs: `MarkdownParser.Cell` returns an icon run (`RunStyle.Icon`,
  `InlineIcon` with `HarveyBall.Drawing`), so the layout's icon-in-text path draws it one em wide in the
  text colour and at the cell's alignment — before `NotDrawn`, which drops real icons from cells.
- **PDF structure (E08-S01):** `PdfConverter` runs a structured pass per page (`PageGraphics.Read` sorts
  `page.Paths` into rules, boxes, dots, fills; tables first, their words leave the flow; then lines with
  superscripts, footnotes, markers, code) and falls back to a plain pass (the converter before E08-S01:
  paragraphs, glyph bullets, headings), then to the content-order text. `PdfConverter.AnalysisStarting`
  (internal) lets tests break a pass. Every block carries its top y; tables and images are merged into
  the flow by it. A footnote reference is a private-use placeholder (`\uE000n\uE001`) in the line text until
  the Markdown is escaped. PdfPig's `DrawRectangle(fill: true)` also strokes: filled-and-stroked
  rectangles are fills, only stroke-only rectangles give rules.

- **Placeholder slides and chapter numbers (E01-S07):** `PptxGenerator` builds the `DeckWriter` before
  parsing (the deck title goes to `Write`), because the parser asks `DeckWriter.Slots(layout)` which
  placeholders a `layout="…"` layout has, and gets `numberChapters` (front matter ?? the roles').
  `DeckParser.Placeholders` walks such a section into a `PlaceholderCollector`, which records every
  `ITarget` call with its build mark: it becomes a `PlaceholderSlide` (`PlaceholderItem`s in Markdown
  order) or is replayed into the `SectionBuilder`, so the old slides come out without a second walk
  (and without repeated notes). `PlaceholderFill.Distribute` is the one assignment (each item into the
  first free slot that takes it, an item with none starts the next slide), used by the parser's check,
  `Fit` and the writer. Untyped placeholders are `obj` (OOXML's default type). `TitleSlide.Number`
  carries the chapter number.
- **The slide that fills placeholders writes `p:pic` / `p:graphicFrame` with a `ph`,** a pic without
  `spPr` transform (it takes the layout's box), a chart frame with an explicit `xfrm` — to be confirmed
  in PowerPoint (E01-S07 verification).
- **Slide kinds (E02-S05):** `HeadingAttributes.Kind` (`.agenda` … `.chart`), checked for both generators by
  `SlideKinds.Check` / `CheckContainers` (shared package) on Markdig blocks, so PDFs throw the same
  errors. `DeckParser` makes `KindSlide`s (`AgendaSlide`, `QuoteSlide`, `StatementSlide`,
  `ContactSlide` with `Person`, `ChartSlide`) through a `KindCollector`; empty agendas are filled in
  `FillAgendas` after parsing. Soft line breaks are kept as a sentinel run (`SoftBreak`, U+2028) only
  for a quote's byline and a person's lines. `DeckWriter.Kinds.cs`: `Resolve` (before `Fit`) turns a
  flow slide of two text columns into a `PlaceholderSlide` on a declared `TwoContent` layout; `FitKind`
  chunks people and charts, writes each part on `KindLayout` or as a `Fallback` flow slide
  (`layout-fallback`). `::: comment` wraps the block before it (`CommentBlock`; `WalkMarked` breaks
  the text block a comment follows); `::: conclusion` (`ConclusionBlock`) is placed by `Stack` at the
  foot. Text levels are not applied on kind slides.

- **Open XML code both generators need is linked source,** not a package: `src/Shared/OpenXml/*.cs`
  is compiled into Pptx and Docx (`<Compile Include="..\Shared\OpenXml\*.cs" Link=…>`) as internal
  types in the namespace `wwosik.DocLibs.DocumentGeneration` (`CoreProperties`, `ChartWriter`, `SvgBlip`).
  `ChartWriter.Add(OpenXmlPart, ChartSpec, text fill, grid fill, font)` takes its colours as `a:solidFill`
  XML strings: decks pass `SlideColor.Fill().OuterXml` (theme colours, transparency) and must stay byte
  for byte as they were; Word passes `ChartWriter.Solid(hex)`. The shared package must not take the Open XML SDK, because
  Charts and Diagrams depend on it. `TrueTypeInfo.IsEmbeddingRestricted` is the one `fsType` check.
- **DOCX styles are found by name, never by id (E09-S01):** `DocxRoles.All` lists every role (key,
  default name as Word stores it, e.g. `heading 1`, built-in id, the `DocxStyleNames` property and the
  built-in definition); `DocxStyles.Resolve` maps each to the template's style id by `w:name`
  (case-insensitive; `Normal` also by `w:default`), adds a missing one from the definition based on the
  template's `Normal` under a free id, and throws for a name the caller mapped that is missing. A role has
  a `StyleKind` (paragraph, character, table) and is looked up among the styles of its kind. New roles
  (E09-S02's deck-syntax roles) are added to `DocxRoles.All` and `DocxStyleNames` the same way, with a
  `styles.<key>` front-matter key and a `brand.json` `docx.styles` property each. The body is written by
  `DocxBody` behind `IBodyWriter` (a new instance per document: it keeps numbers and ids); `DocxWriter`
  puts the cover and the TOC before it and keeps the template's final `w:sectPr`.
- **DOCX content (E09-S04):** headings are shifted so that the highest Markdown level after the cover is
  Heading 1 (`DocxBody.shift`, from `HeadingNumbering.Top`). Heading numbers default
  **on** in DOCX (built-in template only: `numPr` in the Heading 1–3 styles, `StyleDesign.HeadingNumbers`,
  numbering part written by the template builder, `DocxNumbering.HeadingNumId` = 1) and **off** in PDF;
  `{.unnumbered}` is a paragraph `numId` 0. Numbers follow the shared `HeadingNumbering` (below), not
  Word's own counting: a heading whose number's depth differs from its style level gets a paragraph
  `numPr` (`ilvl` = depth − 1, `HeadingNumId`), so Word never shows `0.1` / `1.0.1`. The built-in
  `Normal` style is empty, its size and spacing in the document defaults, because a paragraph style's own
  settings beat a table style's in cells. Lists get `w:numPr` on every item paragraph (the converter
  reads lists from it); list numbering comes from the template's list style if it has `numPr`, else
  `DocxNumbering` adds abstract definitions on first use, and every ordered list gets its own `w:num`
  with a `startOverride`. The shared `Captions` (image title, `Table:` / `Figure:` prefixes,
  `GeneratedTexts.Figure`/`Table`) is read by DOCX and PDFs alike; decks only drop the prefixes.
  A pipe table followed by a line without `|` is no table at all in Markdig: a `Table:` caption needs a
  blank line before it. Drawing ids continue after the highest `docPr`/`cNvPr` id of the template's
  parts; check boxes declare `w14` with `mc:Ignorable` on the root.
- **Deck syntax in Word (E09-S02):** `DocxBody.Container`/`Alert` write layouts in sequence with styles
  (`DocxRoles.Alerts`, `Labels`, `Colours`, `ContainerHeading`, `Panel`, …); `Frame.Container` says the
  blocks are inside one (headings become `Container Heading`, outside the TOC and the heading shift),
  `Frame.Box` copies an alert's bar onto list items. Runs carry `RunFlags.Mark`/`Tag`/`Strong` and
  `DocxRun.Category`/`IsLabel`, each mapped to one character style. The only direct formatting besides
  run bold/italic/strike: highlighted table rows' cell shading, an alert's bar on its list items, the
  cover title's `w:spacing before=0` under a marking, and the header marking's right indent beside the
  built-in logo. On a caller's template the new styles take theme colours (`StyleDesign.Theme`,
  `WordColor`).
- **Page breaks and the marking are shared syntax E09-S02 added for E03-S03/S04:** `PageBreaks.Flatten`
  takes every `::: pagebreak` apart right after Markdig parses (all three generators), returning the
  blocks a break comes before; decks ignore that set, PDFs put a `PageBreakBlock` before each (E03-S03).
  `HeadingAttributes.NewPage` on every level in every format. `classification`/`draft` are shared front-matter keys
  skipped by `FrontMatter.Apply`; `Marking.Resolve(frontMatter, host classification, host draft, …)` applies
  them in all three generators: the higher classification wins (front matter only raises it, else the note
  `classification-kept`), `draft` only adds; `Marking.Watermark` upper-cases the draft text in the document
  culture. `FrontMatter.LineOf(key)` gives a key's line. `CoreProperties.ContentStatus` writes
  `cp:contentStatus` (Word and decks, only when there is a marking).
- **Classification marking in PDFs and decks (E03-S04):** PDFs: `Design.Marking`/`MarkingColor`/`Watermark`;
  `LayoutEngine.Decorate` puts the marking left of the logo in the header (the title's `Fit` width shrinks
  by it), `Cover` above the title, and inserts the watermark `TextItem` at index 0 of every page's items
  after the layout (never during it: the empty-page checks count items). **`TextItem.Angle`** (degrees
  counter-clockwise about its start): `PdfRenderer.Rotated` writes `q`, a `cm` rotation and PdfPig's
  `AddText` at the origin, `Q` — the font subset and extraction work as for any text, letters come out with
  a non-horizontal `TextOrientation` (the spike of the plan passed; the horizontal fallback was not
  needed). Decks: `DeckWriter.Marking`/`Confidential`, `Classification(canvas, slide)` adds a text box
  named `Classification` after the footer, before `Tag`; dark when `palette.DarkSlides`, a `{.dark}` flow
  slide, or `TemplateTone.IsDark(layout)` (light title text in the layout's own title placeholder, else
  its own dark background); `SlidePalette.Footer` is its colour. Both guides leave `classification-kept`
  out of their notes list for room (word limits).
- **Charts and drawings in Word (E09-S03):** `DocxBody.Graphics.cs`. A chart is a native chart part on the
  main part (a chart in a footnote falls back to its drawing). Every other drawing (renderer drawings,
  `qr:`, icons, drawn containers) goes `DrawingSvg.Write` (hover and hotspots stripped, brand family or
  Arial) → `ImageConverters` at scale 2 → PNG part plus SVG part, `SvgBlip.Extension` inside `a:blip`.
  The Docx package draws no pixels itself (no SkiaSharp): without an image converter a drawing stays code /
  alt text / content with `code-not-drawn`, an icon is left out with `icon-not-drawn`. Icon runs are
  `DocxRun.Icon` (empty text, not blank), turned into pictures in `DocxRuns.Run`, one per name and part.
  `NoteCodes.FormatAdvice` adds per-format advice to the guides' notes list. The Docx tests reference
  Charts, Diagrams, Svg and `SkiaSharp.NativeAssets.Linux`.

- **HTML deck (E07-S02):** `HtmlDeck.Write` = `DeckReader.Read` (namespace `…Pptx.Rendering`, internal) →
  `DeckScene` (slides of `SceneGroup`/`SceneShape`/`ScenePicture`, text already broken into lines, all in
  EMU) → `SceneSvg.Write` per slide → `HtmlDeckWriter.Page` (CSP with the script's hash, `HtmlDeck/deck.js` and
  `deck.css` as embedded resources). Parts are read as XLinq (`PartXml`) from the SDK's part streams and
  matched by namespace (`Ns`). Inheritance, colours (`Palette`), geometry (`Geometry`, `PresetShapes` as
  ECMA definitions), text (`TextLayout` with `FontLibrary`: embedded EOT → TTF, passed fonts, Liberation
  stand-ins, else `TextMetrics`), tables (built-in *Medium Style 2 – Accent 1* in `DeckReader.Tables.cs`)
  and charts (`DeckReader.Charts.cs`, `ChartWriter`'s subset). `SvgTarget.Painting` writes standalone SVGs
  for Svg.Skia (raster pictures, measured face names) — E07-S03's `DeckSvg` is a thin wrapper over
  `DeckReader.Read(pptx, fonts, slides)` and `SceneSvg.Write(…, SvgTarget.Painting)` plus `DeckScene.Faces`.
  Every item keeps its `cNvPr id` (`data-id`), every paragraph its index (`data-p`) for E07-S06.
  `RenderNotes` gives one note per kind with the slide numbers, `Line` 0.
- **Presenting the HTML deck (E07-S06):** `DeckReader.Presenting.cs` fills `SlideScene.Notes` (notes page body),
  `Steps` (main click sequence of `p:timing`, effects on items the slide does not draw dropped) and `Transition`
  (init properties, so `new SlideScene(…)` stays as it was), plus `Decorations` (how many leading items are master
  and layout ones). In `SvgTarget.Html` only, `SceneSvg` wraps the slide's own items in `<g class="own">`, because
  decorations can carry the same `cNvPr id`s; the script finds a target there, outermost first (a picture with an
  outline is a group and a shape with the same id). Sections carry `data-steps` (JSON), `data-transition`,
  `data-direction`, `data-duration`; notes are an `<aside class="notes">` per section. `deck.js` plays effects
  with the Web Animations API on the individual `translate`/`scale` properties (never `transform`: a picture's
  `<g>` has a `transform` attribute) and keeps every non-current slide in its final state; print uses the
  `build-gone` class, not `beforeprint`. The painting SVG (previews) is unchanged.
- **Deck previews (E07-S03):** `DeckSvg.Write` (Pptx, public) = `DeckReader.Read` → `SceneSvg.Write(…,
  SvgTarget.Painting)` per slide, plus `Fonts` (`DeckScene.Faces` and the Liberation Sans faces of runs measured
  by estimate) and `LabelFont`; `DeckPreview.Render`/`ContactSheet` (Svg) paint them through `SvgConverter`. The
  hand-over is `SlideSvg` in the base package: Pptx and Svg must never reference each other. Golden images are
  `Samples/<sample>-<template>-<n>.expected.png` (640 px) in the Svg test project, which links the Pptx tests'
  `Samples.cs` and `DarkTemplate.cs` (hence its `InternalsVisibleTo` in DocumentGeneration and Pptx) and pins
  the date with a fixed `TimeProvider`; `DOCLIBS_UPDATE_EXPECTED=1` rewrites them. A change to the reader or
  the generator's drawing that moves pixels shows up there: review the new images, then rewrite them.
- **The DrawingML subset is a contract:** `HtmlDeckTests.EverySampleDeck_IsDrawn_WithNothingLeftOut` generates
  every sample deck plus a features deck on the neutral, sample and dark templates and fails on any
  `drawing-unsupported`. A story that makes the generator write new DrawingML (a preset, an effect, a fill,
  a graphic frame, an element in the shape tree) extends the reader in the same change and adds a sample.
  The scene's path segments are `PathMove`/`PathLine`/`PathCubic`/`PathClose`, not the shared `Drawing`'s
  `MoveTo`/`LineTo`, which tests see first.

## Traps

- **The role layouts give two placeholders the same `idx`** (e.g. a picture placeholder and the date both
  `idx="2"` on the contact layout), so the HTML deck matches a slide placeholder by `idx` only among
  compatible types. Whether PowerPoint does the same is an open check (E07-S02).
- **The built-in master's relationship ids are fixed:** layouts 1–4 are `rId1`–`rId4`, the theme
  `rId5`, the master logo `rId6`; layout 5 (*Title Only Dark*) is `rId7`, the role layouts 6–13
  (E01-S06) `rId8`–`rId15` (`TemplateBuilder.LayoutRelationship`). *Title Only Dark* and
  *Statement* share the section layout's `LogoOnDark` image part, so the media count and image
  numbering of existing decks did not change. New layouts go at the end; the section layout's
  chapter-number placeholder is last in its tree, so "the first body in tree order" stays the same.
- **`brand-roles` is built at test time** too (`RolesTemplate`): the built-in master with renamed,
  untyped role layouts, a decorative body first on the content layout, footer placeholders and
  master guides.
- **`brand-dark` is built at test time** (`DarkTemplate` in the PPTX tests) from the built-in master:
  gradient background, white text styles, a footer placeholder and two copied layouts. There is no
  binary sample for it.
- **The PowerPoint export (`.tmp/scripts/export.ps1`) hangs** while another PowerPoint instance has a
  dialog open; with no PowerPoint running it works.
- **A `:::` closes the outermost open container of three colons,** not the innermost: Markdig checks the
  parents first. A `::: pagebreak` (or any container) inside a `:::` container needs `::::` on the outer
  one, as `layouts.md` does; an unclosed `::: pagebreak` is closed by the next bare `:::`, with everything
  in between flattened in order, so the pages come out the same.
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
- **The SDK's `ChangeDocumentType` renames the main part** (`word/document2.xml`), which Word accepts
  but `DocxConverter` (it reads `word/document.xml` by path) does not. The DOCX generator turns a
  `.dotx` into a document by the content type in `[Content_Types].xml` alone (`DocxPackage.ChangeMainType`).
- **PdfPig's TrueType fonts hold at most 255 distinct characters** and throw on a glyph the font
  lacks; `PdfRenderer` adds the same font again when a copy is full, `LayoutEngine` checks glyphs
  first. PdfPig ignores the content of an `XmpMetadata` you pass it.
- **Validating PDF/A from WSL:** veraPDF needs Java; Windows has one
  (`C:\Program Files\RedHat\java-15-openjdk-jre-15.0.2-1`). Install veraPDF headless into
  `.tmp/verapdf` (izpack installer with an `auto-install.xml`), then run
  `java.exe -cp ".tmp\verapdf\bin\*" org.verapdf.apps.GreenfieldCliWrapper --flavour 2b <pdf>`.
  The installer is `https://software.verapdf.org/releases/verapdf-installer.zip` (no `unzip` in
  WSL: extract with Python's `zipfile`); pass the target as a Windows path (`wslpath -w`).
- **Looking at a generated PDF on Linux:** `python3 -m venv .tmp/venv && .tmp/venv/bin/pip install
  pypdfium2 pillow` gives a renderer (`PdfDocument(path)[i].render(scale=1).to_pil().save(...)`).
  PdfPig's PDFs are not byte-identical between runs, so "unchanged output" is checked on rendered
  pages, and decks on their XML with the SDK's random `R…` relationship ids masked.
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
- **System.Text.Json source generation drops initializers of `init` properties:** a property missing
  from the JSON is set to `default`, not to its `= …` value (labels became empty, lists `null`). The
  financial-chart records guard each such property with `init => field = value ?? …`.
- **Svg.Skia loads `<image href="file:…">` and web addresses whatever `SvgDocumentLoadOptions` or
  the global `SvgDocument.ResolveExternal*` settings say.** `SvgSanitizer` strips links before
  drawing; keep it in front of every Svg.Skia load.
- **This machine has the Liberation fonts installed system-wide** (fontconfig), so a pixel test that removes
  a Liberation face from `SvgPngOptions.Fonts` still paints it. Test "our face is used" with a face no system
  has (the sample brand's Inconsolata or PT Sans Narrow).
- **Svg.Skia's `CustomTypefaceProvider` matches width exactly,** so a condensed font is never found
  for normal-width text; `SvgConverter` has its own provider that matches by family name.
- **The managed SkiaSharp version must match its native assets** (`libSkiaSharp` refuses another
  major): pin `SkiaSharp` to the same version as `SkiaSharp.NativeAssets.Linux`, above what Svg.Skia
  asks for.
