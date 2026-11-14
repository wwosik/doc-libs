# E04-S06 QR codes and icons

> Status: in verification
> Epic: [E04 Diagrams and charts](../50-todo/E04-diagrams-and-charts.md)

## Summary

Two kinds of small vector graphics written in the Markdown. `![Survey](qr:https://example.com/s)`
draws a QR code for the address, in the brand's colour. `:icon-check:` draws an icon from the
Tabler Icons outline set (MIT), embedded whole in the shared package
`wwosik.DocLibs.DocumentGeneration` and available without any option: as a card figure and as a
list bullet in decks and PDFs, and inline in PDF text. Both are `Drawing`s, so decks get editable
shapes and PDFs vectors, as for diagrams. No new package.

## Context

- **Images:** `DeckParser.Image` and `MarkdownParser.Image` look every image URL up with
  `MarkdownDocument.FindResource` and throw "The image '…' is not among the document's resources"
  when it is missing. `qr:…` is therefore an error today, and supporting it changes no working
  document.
- **Drawings:** `Drawing` (shapes with `DrawingShapeKind`, `RoundJoins`, `FillRule`; texts) is drawn
  by `DrawingWriter.Group` in decks (presets for rectangles, custom geometry for paths) and by
  `LayoutEngine.Drawing` / `PdfRenderer.DrawShape` in PDFs. `Drawing.Validate()` runs in every
  writer (E06-S02). On slides, `DeckParser.ITarget.Visual(null, null, drawing)` places a drawing as a
  visual; on flow slides it is a `DrawingBlock`.
- **SVG paths:** `SvgGeometry.Path` (Diagrams package, internal) parses the full SVG path grammar,
  arcs and quadratics included, into `MoveTo` / `LineTo` / `CubicTo` / `ClosePath`.
- **Where an icon could go:**
  - Cards: `DeckParser.Cards` takes a paragraph that is one `**strong**` run after the `###` as the
    card's figure (`Card.Figure`), drawn large in the accent colour by `DeckWriter.Cards`
    (`FigureSize`).
  - Lists: decks write bullets as `a:buChar`, tasks as Wingdings characters (E06-S02); PDFs draw
    task boxes as shapes in place of the marker (`LayoutEngine.Box`), which is the model for a drawn
    bullet.
  - Inline: DrawingML text cannot hold a picture or shape inside a run, so an icon in the middle
    of a deck's paragraph cannot be drawn. PDF lines are laid out by `LayoutEngine` from runs with
    `IFontMetrics`, and an inline object of one em is possible there.
- **`:name:`** has no meaning today: Markdig writes `:icon-check:` as text in both generators.
- No package carries a third-party notice yet; the PDF package packs `Fonts/OFL.txt`
  (`wwosik.DocLibs.DocumentGeneration.Pdf.csproj`, `Pack="true"`). E04-S05 starts
  `THIRD-PARTY-NOTICES.txt` in the PDF package.
- **The shared package** `wwosik.DocLibs.DocumentGeneration` (brands, `Drawing`, `ColouredWords`,
  `FrontMatter`, `AuthoringGuides`, `GenerationResult` with `NoteCodes`) is referenced by every
  generator and renderer package, and is `IsAotCompatible`. It embeds nothing yet; whatever it embeds
  is downloaded and loaded by every consumer, so the icon data counts against all of them (question
  3).
- Colours: `ResolvedBrand` has `Primary`, the diagram colours (`Text`, `Accent`, `Surface`, …) and,
  for dark slides, `DarkSlideBrand`.

## Proposed approach

1. **QR codes: syntax.**
   - An image whose URL starts with `qr:` is a QR code for the rest of the URL, taken literally
     (`qr:https://example.com/survey?id=3`, `qr:mailto:…`, `qr:WIFI:…`). The alt text is its
     description (`descr` in decks), and `caption` in the title (or `.caption` from E04-S05) shows it
     under the code, as for images. Image attributes other than `scale` and `.caption` are errors.
   - Content over 1 000 bytes throws: such a code is too dense to scan from a slide.
2. **QR codes: encoding and drawing.**
   - Encoding by **QRCoder** (MIT), error correction M, byte mode, the smallest version that holds
     the text (question 1). Only its module matrix is used, none of its renderers.
   - New internal `QrDrawing.Draw(text, ResolvedBrand)` in DocumentGeneration: a white square with
     the four-module quiet zone, then one rectangle per horizontal run of dark modules, merged into
     one path with `FillRule.NonZero`, so a deck gets two shapes, not hundreds.
   - **Colour:** the brand's `Primary` when its contrast to white is at least 4.5:1, else the
     diagram text colour; on dark slides it stays dark on its white square, because scanners need
     dark on light.
   - Size: 4 px per module, so it scales like any drawing; on slides at most a third of the
     content width unless `scale` says otherwise.
3. **Icons: the set.**
   - Tabler Icons (MIT), **outline** style, version pinned. A new single-file tool
     `tools/icon-import/icon-import.cs` (`run.sh icon-import` / `run.ps1 icon-import`) reads the
     icon SVGs from a Tabler release and writes one text resource: one line per icon,
     `name<TAB>path data`, absolute `M`/`L`/`C`/`Z` only, rounded to two decimals. Circles, lines,
     polylines and arcs are turned into paths there, so the runtime needs no SVG parser. The tool
     prints the count, the raw size and the gzip-compressed size, and fails above the budget (below).
   - The whole outline set (question 2). The tool writes the resource gzip-compressed to
     `src/wwosik.DocLibs.DocumentGeneration/Icons/tabler-outline.txt.gz`, embedded in the shared
     package (question 3) as `EmbeddedResource` with `LogicalName="Icons.tabler-outline.txt.gz"`;
     the generated file is committed, so a build needs neither the tool nor the network.
   - **Loading:** the resource is read and decompressed on the first `:icon-…:` lookup only, into a
     name → path-data dictionary; each icon's `Drawing` is built when asked for and cached. A
     document without icons pays nothing at run time beyond the larger assembly.
   - **Size budget:** at most 1 MB compressed for the resource (the cut-off of question 2, now held
     against every consumer of the shared package). If a Tabler update breaks it, the tool fails and
     the set is cut to a curated list, which goes back to the human first. The measured sizes (icon
     count, compressed resource, packed `.nupkg` before and after) are recorded in the
     DocumentGeneration docs page.
   - Public API, in DocumentGeneration: an `IIconSet` interface (`Drawing? Find(string name)`,
     `IReadOnlyCollection<string> Names`) and `TablerIcons.Outline`, the embedded set. Icons are on
     by default: both generators use `TablerIcons.Outline` without any option. Options
     `PptxOptions.Icons` and `PdfOptions.Icons` (`IIconSet`, default `TablerIcons.Outline`) exist
     to override the set, for example with a caller's own icons. `Icons = null` switches icons off:
     `:icon-name:` then stays text, as today, and is not checked (the human's decision,
     2026-11-14).
   - Each icon is a 24 × 24 drawing of stroked paths: `Stroke` in the colour of its place,
     `StrokeWidth` 2, `RoundJoins`, no fill, as Tabler draws them.
4. **Icons: syntax.** `:icon-<name>:`, the name in Tabler's kebab-case (`:icon-circle-check:`).
   Recognised in text runs outside code, by a shared helper like `ColouredWords`.
   - An unknown name throws `DocumentConversionException` with up to three close names (edit
     distance, then common prefix): "Unknown icon 'chek'. Did you mean check, checks, cheese?".
5. **Icons: where they go.**
   - **Card figure:** a card whose first paragraph is a single icon gets the icon as its figure,
     drawn at the figure size in the accent colour, in decks (`Card.Figure` becomes text or icon).
     PDFs show cards plainly; there, any paragraph that is a single icon is drawn as a block of its
     own, left-aligned at the figure size.
   - **Bullet:** a list item whose text starts with an icon gets the icon instead of its bullet.
     - PDFs draw it in the marker's place at the text size, in the accent colour, as task boxes are
       drawn.
     - Decks: a list in which every item starts with an icon becomes an icon list, a new flow block
       (`IconListBlock`): each item a row, the icon drawn left as a shape group at the first line's
       height and the text in its own text box. Like other layouts it makes the section a flow
       slide, so plain slides stay as they are. A list where only some items start with an icon keeps
       its bullets, and those icons are left out with a note (point 6).
   - **Inline (PDF):** a new `RunStyle.Icon` run measured as one em plus a thin space each side,
     drawn as shapes on the baseline at cap height in the text colour, never broken from the word
     after it.
   - **Inline (decks):** cannot be drawn (see Context); the icon is left out and reported (point 6).
6. **Notes.** New `NoteCodes.IconNotDrawn` (`icon-not-drawn`): an icon in a place the format cannot
   draw it (inline text on a slide, a table cell, a heading), left out of the text. In
   `NoteCodes.All` and the docs table.
7. **Dependencies and licences.**
   - QRCoder (MIT) in `src/Directory.Packages.props`, referenced by DocumentGeneration. The shared
     package is `IsAotCompatible`: a Native AOT publish is checked for warnings; if QRCoder warns,
     the plan falls back to an own encoder (question 1).
   - One `THIRD-PARTY-NOTICES.txt` in `src/wwosik.DocLibs.DocumentGeneration/`, packed at the
     package root (`None Include=… Pack="true" PackagePath="\"`, as the PDF package packs
     `OFL.txt`): QRCoder's licence, and Tabler Icons' licence with the pinned version and the
     source release. The import tool prints the version to copy there.
   - No new package, docs page or test project; the tests go in
     `wwosik.DocLibs.DocumentGeneration.Tests` (and the Pptx / Pdf test projects for the writers).
8. **Guides, docs, changelog.**
   - The PPTX and PDF guides: QR codes; icons, where each format shows them, and a short list of
     about 40 common names with a pointer to Tabler's catalogue. The icon section is always in the
     guides, since icons are always on. The expected guides are regenerated.
   - Docs: `docs/wwosik.DocLibs.DocumentGeneration.md` (QR, `IIconSet`, `TablerIcons.Outline`, the
     option to override it, the note code, the set's version and measured size, the import tool),
     `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md` and `docs/wwosik.DocLibs.DocumentGeneration.Pdf.md`
     (where they appear).
   - `CHANGELOG.md`: new: QR codes, icons, `IIconSet` and the `Icons` options. Breaking: text of the
     form `:icon-<name>:` is now an icon, and an unknown name throws (before, it stayed text);
     DocumentGeneration depends on QRCoder and is larger by the icon data.
9. **Tests.** QR: the matrix for known texts against QRCoder's, the quiet zone, contrast fallback,
   the merged path; decks and PDFs with a QR code, read back as a drawing. Icons: the import tool on
   two sample SVGs (circle, arc), lookup and suggestions, each place in each format, `icon-not-drawn`,
   and an overriding `IIconSet` replacing the default. Open XML validator for every deck; PDF/A
   markers, and one veraPDF run recorded.
   - **Size guard** in `wwosik.DocLibs.DocumentGeneration.Tests`: reads the embedded
     `Icons.tabler-outline.txt.gz` from the assembly and fails if it is over 1 MB, naming the size
     and the budget; it also checks the icon count against the number recorded in the docs page, so
     an import that silently drops icons fails too.

## Acceptance criteria

- [ ] `![Survey](qr:https://example.com/survey "caption")` draws a scannable QR code (checked with a
  phone from a slide and from a printed PDF page) with "Survey" under it, in the brand colour or its
  fallback.
- [x] A QR code is two shapes in decks (the white square with its quiet zone, and one path of all
  modules) and two paths in PDFs, also on dark slides.
- [x] Without any option, `:icon-check:` draws the icon as a card figure in decks and PDFs, as
  bullets (an icon list in decks), and inline in PDF text; `Icons = <own set>` replaces the set.
- [x] An inline icon on a slide is left out and gives `icon-not-drawn` with its line.
- [x] `:icon-chek:` throws with suggestions.
- [ ] Every deck passes the Open XML validator and opens in PowerPoint without repair; PDFs stay
  PDF/A-2b.
- [x] The DocumentGeneration package carries `THIRD-PARTY-NOTICES.txt` with QRCoder and Tabler Icons;
  the embedded icon data is at most 1 MB compressed, guarded by a test, and the measured sizes are
  recorded in its docs page.
- [x] Samples without `qr:` images or `:icon-…:` text produce the same decks and PDFs as before.
- [x] Guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. QR encoding: QRCoder (proposed: MIT, managed, well tested; we use only its matrix and draw it
   ourselves), or an own encoder of about 500 lines without a dependency, tested against QRCoder in
   the tests only?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. How many icons: the whole Tabler outline set, about 5 000 (proposed: writers, often AI agents,
   know Tabler names, and a subset would make "unknown icon" common; the size is measured by the
   import tool and the set is cut to a curated list only if it exceeds 1 MB compressed), or a
   curated set of about 150–200 business icons as the fork has?

> Answered by the human (2026-11-14): as proposed.

3. Where the icons live: a separate package `wwosik.DocLibs.DocumentGeneration.Icons`, passed in the
   options (proposed: no size cost for callers who don't want it, and `:icon-…:` stays text without
   it), or embedded in the shared DocumentGeneration package and always on?

> Answered by the human (2026-11-14): No separate package: the icons live in the shared package `wwosik.DocLibs.DocumentGeneration` (the human chose fewer packages). The whole outline set stays (question 2), so its size counts against every consumer of the shared package — keep the compressed size in check.

4. Unknown icon names: throw with suggestions (proposed, as for unknown containers), or leave the
   text with a note?

> Answered by the human (2026-11-14): as proposed.

5. Icon bullets in decks: an icon list as a flow block with our own layout (proposed: the icon and
   its text cannot drift apart), or a picture bullet (`a:buBlip`), which stays an editable list but
   needs a PNG per icon, and so SkiaSharp in the PPTX package?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. QR colour: the brand's primary colour with a fallback to the diagram text colour below 4.5:1
   contrast (proposed), or always black on white for the safest scan?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. QR caption: the alt text under the code when the title says `caption`, as for images
   (proposed), or the title itself as the caption text, as the fork writes it
   (`"Scan for the survey"`)?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

- Revised after question 3: the icons are always on, with no option to turn them off, because the
  answer moves them into the shared package and the alternative it chose was "always on". The
  consequence is a small breaking change (point 8): a document that already contains
  `:icon-something:` as literal text now gets an icon, or an error for an unknown name. If that is
  unwanted, a `PptxOptions.Icons = null` / `PdfOptions.Icons = null` switch-off is a one-line
  addition — say so.
  - Decided by the human (2026-11-14): icons on by default, the breaking change accepted, and
    `Icons = null` switches them off (step 2). Add a test that `:icon-check:` stays text with
    `Icons = null`, and mention the switch-off in the CHANGELOG entry.
- The 1 MB budget is the cut-off from question 2, now applied to every consumer of the shared
  package; a tighter number can be set once the import tool has measured the real set.

## Implementation notes

### Breaking changes

- **`:icon-<name>:` in text is an icon now,** in both generators, without any option: text that used
  to stay as typed is drawn as the Tabler icon (or, where the format cannot draw it, left out with
  `icon-not-drawn`), and an unknown name throws `DocumentConversionException` with suggestions. Code
  spans are never read. `PptxOptions.Icons = null` / `PdfOptions.Icons = null` keeps the old behaviour.
- **`![…](qr:…)` is a QR code now** (it used to fail as a missing resource); attributes other than
  `scale` and `.caption` on it are errors.
- **`wwosik.DocLibs.DocumentGeneration` depends on QRCoder 1.8.1** (MIT). QRCoder's `net6.0` group
  depends on `System.Drawing.Common` 6.0.0, so every consumer of the shared package gets that package
  in its graph (never loaded: only the module matrix is used; no NuGet audit warning). The package is
  larger by the icon data (`.nupkg` 153 871 → 482 484 bytes, local pack).

All three are in `CHANGELOG.md` under *Unreleased*. Output without the new syntax is unchanged (see
Verification).

### Decisions

- **Tabler Icons 3.52.0**, from the npm registry tarball
  (`https://registry.npmjs.org/@tabler/icons/-/icons-3.52.0.tgz`). The import tool is a single-file
  .NET app (`tools/icon-import/icon-import.cs`) that downloads that pinned tarball itself and reads it
  with `System.Formats.Tar` (or `--source <.tgz | folder>`), so neither npm nor pnpm is needed; the
  launchers find it like any tool. The outline SVGs hold only `path` elements today; the tool also
  converts `circle`, `ellipse`, `line`, `polyline`, `polygon` and `rect`, and the full path grammar
  (relative commands, H/V, S/T, quadratics, arcs to cubics of at most 90°). It skips `stroke="none"`
  (Tabler's 24 × 24 frame) and merges each icon's elements into one path, so an icon is one shape.
  Numbers are rounded to two decimals, written without a leading zero and without a space before a
  minus. Measured: **5 491 icons, 1 369 064 bytes raw, 313 474 bytes compressed** (budget 1 000 000).
  The resource is gzip `SmallestSize`; `.gitattributes` marks `*.gz` binary.
- **Loading:** `EmbeddedIconSet` reads the resource lazily (`Lazy<Dictionary>`) on the first lookup
  and caches each built `Drawing` (`ConcurrentDictionary`). An icon is stored stroked in `000000`;
  the writers recolour every stroke and fill to the colour of the place (`IconSyntax.Colour` for PDFs,
  `DrawingWriter.Group(..., colour)` with a `SlideColor` for decks, so a caller template's theme
  accent works). A caller's own `IIconSet` is therefore drawn in one colour too.
- **`:icon-name:`** is found by `IconSyntax.Apply` per run, after `ColouredWords`, in non-code runs
  (also not in deck status labels). The icon becomes a run of its own with empty `Text` and
  `TextRun.Icon` (`InlineIcon(Name, Drawing, Line)`); both parsers' `Trim` treat such runs as not
  blank. The line is that of the paragraph's first inline. Suggestions: Levenshtein, then common
  prefix, then name: `:icon-chek:` gives "Unknown icon 'chek' at line N. Did you mean check, checks,
  chess?" (the plan's example said "cheese"; "chess" is closer).
- **QR codes** (`QrDrawing`): `CreateQrCode(byte[] UTF-8, ECCLevel.M)`, i.e. byte mode, QRCoder's
  smallest version and its four-module quiet zone. Shapes: the white square
  (`DrawingShapeKind.Rectangle`), then one non-zero path of a rectangle per horizontal run, no
  stroke. Colour: primary at ≥ 4.5:1 to white, else the diagram text colour, else black (the last
  step is new: on a dark slide brand both may be light). The decks' parser passes the renderers'
  brand (the dark slide brand on dark templates), caller templates the neutral brand.
  `scale=` multiplies the module size (4 px) in both formats.
- **The caption is part of the drawing** (a centred `DrawingText` under the square, 13 px × scale,
  diagram font and text colour), not the decks' image caption box: it then looks the same in both
  formats and moves with the group. A deck's QR group is thus three shapes with a caption, two
  without.
- **QR size on slides:** a `DrawingPlacement` on `VisualSlide` and `DrawingBlock`: by default a
  drawing may grow 1.5× and take the whole width (as before); a QR code without `scale` at most 1.5×
  and a third of the content width; with `scale` never enlarged (its own scaled size, shrunk only to
  fit). `MaxDrawingUpscale` became `DrawingPlacement.Default`.
- **Decks:**
  - Card figure: a `###` card whose first paragraph is a single icon gets `Card.FigureIcon`, drawn
    as a group as tall as a line of figure text (`LineHeight(FigureSize)`), accent colour (white on a
    dark block of a light deck, as figures). List-form cards have no figures, as before.
  - Icon list: a list whose every item is exactly one paragraph starting with an icon (judged from
    the leading literals, `IconSyntax.StartsWithIcon`) makes the section a flow slide
    (`UsesLayouts`) and becomes `IconListBlock`; the icon is one text line tall, at the text box's
    top inset, the text in its own box beside it. Fitting splits it per item (`FlowUnits`/`Combine`),
    like rows. Task items never qualify. Items with further blocks keep the normal list.
  - Every other icon run is skipped by `DeckWriter` (titles and paragraphs) and reported once per
    slide in `DeckParser.Section` (slide runs, speaker notes, footnotes) with `icon-not-drawn`.
- **PDFs:** `IconBlock` (a paragraph of one icon) 32 pt, left, primary colour; a list item starting
  with an icon gets `ListBlock.Icons[i]`, drawn at the body size where the bullet goes, primary
  colour (a task item keeps its box and its icon stays inline); inline `RunStyle.Icon` pieces are
  one em plus 1/6 em each side, the icon one em square centred at half the cap height (0.7 em) above
  the baseline, in the piece's colour (text, link or status colour), and a space right after an icon
  joins the following word so they never break apart. Headings, table cells and the cover drop
  icons with `icon-not-drawn` (`MarkdownParser.NotDrawn`).
- **Guides:** both get a QR bullet and an `{{icons}}` section from `AuthoringGuides.Icons` (where the
  format draws icons, 20 common Tabler names, or a caller's set's first 20 names). After E07-S05's
  builds the PPTX guide had 37 words left under its 2 500-word test limit, so the PPTX texts are
  short (the details are in the docs) and the PDF guide, far below its limit, keeps the fuller QR
  text.
- **Builds (E07-S05, rebased onto):** an icon list builds item by item (`canvas.Item(block.Build, i)`,
  icon and text together, as labelled tags), so `{.build}` and `build: lists` work on it; a QR code
  builds as a whole, like any drawing.
- **Notices:** `src/wwosik.DocLibs.DocumentGeneration/THIRD-PARTY-NOTICES.txt`, packed at the root:
  QRCoder 1.8.1 (its LICENSE.txt) and Tabler Icons 3.52.0 (its LICENSE, with the source tarball).

### Deviations from the plan

- **The icon section of the guides is not always there:** it is empty with `Icons = null` (the plan
  was written before the switch-off decision; a section about switched-off icons would mislead).
- **20 common icon names in the guides, not about 40, and the PPTX guide's word limit raised from
  2 500 to 2 600** (`Guide_StaysUnder2600Words`): the PPTX sample guide is at 2 589 words (custom
  2 397). Even the shortest useful QR and icon texts did not fit the 37 words E07-S05 left. The docs
  still say "about 2,500 words". **For the human:** keep 2 600, or say which guide text to cut.
- **Black as a third QR colour fallback** (above) and the **caption inside the drawing** (above).
- **`fit=contain` on a QR code is accepted silently** (it is the default); `fit=cover`, `focus`,
  `.round` and `.grey` are errors.
- The story's summary had its example mangled into a link (`../../50-todo/qr:…`) by an earlier link
  fix; restored to `![Survey](qr:https://example.com/s)`.
- No sample file got the new syntax: the samples are the "unchanged output" baseline; the new syntax
  has its own tests and review files.

### Verification

- **Tests:** `./run.sh build` 0 errors (the 5 `CS0618` warnings in
  `Pdf.Tests/ImageAttributeTests.cs` about `IPdfImage.Bounds` are not from this story); `./run.sh test`
  all green after rebasing onto E07-S05: DocumentConversions 123, WindowsOnly 7 (6 skipped),
  DocumentGeneration 132, Charts 78, Diagrams 71, Svg 22, Pptx 282, Pdf 173 — 888, 882 run. New:
  `IconTests` and `QrDrawingTests` (DocumentGeneration: size guard, count against the docs page, every
  icon parses, lookup, suggestions, `Apply`, the import tool run on a circle and an arc SVG),
  `IconAndQrTests` (Pptx and Pdf). Guides regenerated with `DOCLIBS_UPDATE_EXPECTED=1`. Two full
  runs aborted with "Internal CLR error (0x80131506)" while other agents were building; the reruns
  passed.
- **Unchanged output:** `deck.md`, `layouts.md`, `presenting.md` (decks) and `report.md`, `long.md`,
  `layouts.md` (PDFs) in both built-in brands, generated with `origin/main` (E07-S05, `963ebf6`)
  before this change and with it: deck parts identical with the random relationship ids masked, PDF pages identical pixel for
  pixel (pypdfium2).
- **Open XML validator:** clean for every deck the new tests write (`Samples.AssertValid` in each).
- **QR scannable:** page 2 of `.tmp/review/qr-and-icons.pdf`, rendered at 2× with pypdfium2, decodes
  with OpenCV's `QRCodeDetector` to `https://example.com/survey?id=3`. The rendered page looks as
  intended: code with caption, icon bullets next to a plain bullet, a 32 pt heart, inline icons in
  text, bold and link colour.
- **Native AOT:** a console app that writes a PDF and a deck with a QR code, an icon list and an inline
  icon publishes with `PublishAot` on linux-x64 without warnings and runs (the deck reports the
  inline icon). `IsAotCompatible` stays on; QRCoder raised no trim or AOT warning.
- **Package:** the `.nupkg` holds `THIRD-PARTY-NOTICES.txt`; the nuspec lists QRCoder 1.8.1; the icon
  resource is inside the DLL.

### To verify by hand (no PowerPoint, no Java, no phone here)

1. **PowerPoint** — open `.tmp/review/qr-and-icons.pptx` (written by
   `IconAndQrTests.AQrCode_IsAWhiteSquareAndOnePath_WithItsCaption_AtMostAThirdOfTheWidth` in the
   Pptx tests) in PowerPoint 2016 or later:
   - it opens without a repair prompt;
   - slide "Survey": the code with "Scan for the survey" centred under it, at most a third of the
     slide wide; **scan it with a phone** from the screen: it must open
     `https://example.com/survey?id=3`;
   - slide "On dark": the code dark on its white square on the dark background, caption readable;
   - slide "Why us": three rows, each icon (bolt, shield, heart) in the accent colour, as tall as and
     level with the first line of its text, no bullets; the icons' lines have round ends;
   - slide "Cards": each card shows its icon large under the title, then its text;
   - slide "Inline": the sentence without the icon (the generator reported it);
   - resize an icon group: it scales as one shape.
2. **Phone scan from paper:** print page 2 of `.tmp/review/qr-and-icons.pdf` and scan the code.
3. **veraPDF** (PDF/A-2b) on `.tmp/review/qr-and-icons.pdf`: only plain filled and stroked paths
   were added, so no issue is expected, but it was not run.

### Loose ends

- Icons inside slide text cannot be drawn (DrawingML); a picture-bullet variant (question 5) or
  inline images would need PNGs.
- PDFs could draw icons in table cells (the layout supports it); the plan said not to, so they are
  dropped with a note.
- E06-S04 adds its entry to the shared package's `THIRD-PARTY-NOTICES.txt`.
- If QRCoder ever gets a `net8.0` group without `System.Drawing.Common`, the transitive package goes
  away by itself; an own encoder (question 1) would remove QRCoder altogether.
