# Feature plan

> Date: 2026-11-13. Subject: the features doc-libs should gain after release `2610.28.22`, what each
> one involves, and in what order they become plans.

This analysis proposes no code; the plans follow from the answers at the end.

## Summary

- The set below is roughly the size of doc-libs again. The largest single item is a **DOCX
  generator**, which doc-libs does not have. The second largest is the set of **presenting features**
  for decks: notes, builds, transitions, scenes, an HTML deck and preview images. Everything else is a
  few hundred lines per feature.
- Every feature is built as a generic mechanism, driven by `Brand` / brand-pack data, with English
  front-matter keys. No design of any one organisation is built in.
- A few features could change existing behaviour (see [Behaviour choices](#behaviour-choices)).
  Existing decks and PDFs keep their behaviour by default.

## Out of scope

| Item | Why not |
| ---- | ------- |
| Any organisation's own `.dotx`/`.potx` files, logos, colours, fonts, layout names, sizes | Design belongs in a brand package, not here |
| Front-matter keys or values in another language | doc-libs is English; keys stay English (`template`, `true`, `labels.*`) |
| A PDF that reads its styles and page frame from a Word template | Ties the PDF to one template; the PDF takes its look from the `Brand` |
| Mermaid through Node.js and a browser | Mermaider needs no external runtime and draws the same eight diagram types |
| Removing the neutral brand, the built-in master, brand packs or `CustomTemplateOptions` | They are the basis of doc-libs |
| Looking up system fonts on disk | The libraries read no files; a caller passes fonts in |
| Synthetic italic (12° skew) | A missing face stays an error (question 10) |

## Inventory

"Today" is doc-libs as of this date. Sizes are a rough guide to effort, in lines without tests.

### Foundations

| Feature | Today | Plan | Size |
| ------- | ----- | ---- | ---: |
| Generation notes: `Generate` returns a result with notes (`slide-split`, `slide-overfull`, `image-cropped`, `image-enlarged`, `raw-html`, `code-not-drawn`, `character-missing`, `notes-dropped`, …) and slide and page counts | `Generate` returns `void`; splits, drops and shrinks happen silently | A `GenerationResult` / `GenerationNote(Code, Line, Message)` record; many call sites | 250 + spread |
| Check: run the generator without output and apply style rules (bullets per slide, words per slide, title length, alt text, image source, …), with statistics (slides by kind, estimated talk time, estimated pages) | missing | Limits as options; rules not tied to one language | 840 |
| Document language: every generated string ("Page", "As of", "Table", "Figure", "Contents", "(cont.)", alert titles) from one table per language, chosen by front matter | texts are English or numeric | A `lang` key and a text table; English default, other languages as data | small |
| Footnotes, task lists, definition lists in the generators | parsed nowhere | Markdig's `UseFootnotes`, `UseTaskLists`, `UseDefinitionLists` | small |
| `Drawing`: even-odd fill rule; `DrawingSvg` rejects invalid colours and NaN | missing | Both | small |
| SVG embedded next to its PNG (`asvg:svgBlip`) | there for decks; missing for Word | With the DOCX generator | 60 |
| Authoring guides with per-name texts checked by tests against the syntax lists | the texts are not tied to the code lists | The test pattern | — |
| Feature tour: samples that cover every key, container, attribute and alert, generate with no notes, and round-trip through the converters | samples and `*.expected.md` exist; no coverage check | The coverage test | 500 tests |
| AI tool description: name, JSON Schema, a combined guide for all offered formats; `Run` and `Reply` in fixed JSON | missing | English parameter names, System.Text.Json only | 870 |
| Template preparation: strip a `.dotx`/`.potx` of slides, customXml, thumbnails, orphaned media and personal data; deterministic zip | slides and sections are dropped at run time only | A `tools/` program for brand-pack authors | 490 |
| EMF → SVG for logos taken from Office templates | brand images are PNG/JPEG | A tool, plus possibly SVG `BrandImage`s drawn as vectors | 440 |

### DOCX generator (new package)

| Feature | Plan | Size |
| ------- | ---- | ---: |
| Document on a template: cover from front matter (title, subtitle, author, location, date), header and footer, core properties, TOC field | `wwosik.DocLibs.DocumentGeneration.Docx` | 430 |
| Content: numbered Heading 1–3, unnumbered deeper headings; lists to three levels (numbered lists restart); tables at text width with a repeating header and `TextMetrics` widths; table title (`**Title**` before it); captions with `SEQ` fields (`Table: …` after a table, `"Caption"` on an image); images fitted to 80 % of the text height; code style; quotes; real footnotes; links; task and definition lists | All of it | 1 000 |
| Deck syntax in Word: containers written in sequence, alerts as note styles, `==mark==`, `[words]{.success}`, status labels, image rows as a borderless table | All of it | 350 |
| Native charts (the same chart part and `.xlsx` as decks) and drawings | Reuse `ChartWriter`; drawings as SVG + PNG (question 7) | 520 |
| DOCX authoring guide | All of it | 70 |
| Classification marking (confidential / internal / draft) in the header and on the cover, and a draft watermark; the same key for decks and PDFs | Texts and placement from brand and options | 370 |

How it builds Word:
- A **built-in template is written in code from a `ResolvedBrand`**, as `TemplateBuilder` does for
  decks.
- **Caller templates** come with a role → style-name map, as `PptxLayoutNames` maps layouts.
- A brand pack can carry a `.dotx`, the way `PptxBrandTemplate` carries a `.potx`.
- Paragraphs reference template styles by name only. Direct formatting is used in code and nowhere
  else.
- Missing styles are created on demand. The template's numbering, theme, headers and footers are
  kept.

### Decks

| Feature | Today | Plan | Size |
| ------- | ----- | ---- | ---: |
| Template roles: layouts found by name for two-content, text+picture, picture, agenda, quote, statement, contact, chart and dark; several title layouts; placeholders by role (subtitle, byline, chapter number, second body, picture, chart title/unit/source); text levels; decorative placeholders kept empty; footer pattern; content grid from the master's guides | `PptxBrandTemplate` / `CustomTemplateOptions.Layouts` know title, content, titleOnly, sectionHeader and closing, with one body placeholder | As brand-pack data, for templates in general | 1 500 |
| Numbered chapter dividers ("01", "02"); `{layout="…"}`, which splits text at `---` across the layout's text placeholders and fills its picture and chart placeholders | dividers without a number; one body placeholder | Both, opt-in | in the above |
| Slide kinds: `{.agenda}` (from a list or the chapter headings), `{.quote}`, a statement, `{.contact}` (1–5 people), chart layouts with one or two charts | missing | Each with its layout role | 540 |
| `::: conclusion` (a box at the foot of the content area), `::: comment` (a callout over the previous block); two text-only columns in the template's two-column layout | missing | All of it | 1 450 |
| Speaker notes: `::: notes` under any heading, on the notes master; the PPTX converter reads them back | missing in generator and converter | The built-in master needs a notes master | 150 + converter |
| Builds: `{.build}`, `{.frag}`, `{.build=appear\|fly\|wipe}`, `{.exit}`, `{.emphasize}`; `build: lists` in front matter | missing | All of it | 680 |
| Transitions: `transition: fade` in front matter or on a heading; none, fade, push, wipe, split, cover, uncover, zoom, cut, morph (with fallback) | missing | All of it | 100 |
| Scenes: `::: scene` with named shapes on a grid, steps as builds, Morph between slides; a still image in PDF | missing | 160 × 90 grid over the whole slide (question 13) | 930 |
| HTML deck: one self-contained file with keys, notes view and print | missing | No new dependency; DrawingML fidelity is the risk | 2 800 |
| Preview images: each slide as PNG plus a contact sheet | missing | A Skia painter (question 8) | 430 |
| Number columns right-aligned in deck tables | table style, down to 10 pt; pictures fitted | Number-column alignment | — |

### PDF

| Feature | Today | Plan |
| ------- | ----- | ---- |
| Table of contents with real page numbers | missing | All of it |
| Heading numbers (1.2) in a hanging indent, numbered bookmarks | missing | Opt-in |
| Footnotes at the page foot | missing | All of it |
| Captions "Figure n" / "Table n" | missing | Shared with DOCX |
| Justified text | missing | Opt-in; no hyphenation (no licence-compatible library) |
| Alerts as a box with a rule; drawn bullets; task lists ☐/☒ | alerts as a quote with their kind | Box style as a brand option |
| Missing characters | throws unless `ReplaceUnsupported` | A note becomes possible; the default stays (question 6) |

### Diagrams, charts, visuals

| Feature | Today | Plan | Size |
| ------- | ----- | ---- | ---: |
| `chart`: a `source:` setting drawn under the chart; CSV with `;` and decimal comma | missing | Both | small |
| QR codes: `![Survey](qr:https://… "Caption")` in the brand colour | missing | QRCoder (MIT) | 155 |
| Icons: `:icon-check:` from bundled Tabler Icons (MIT, notice shipped) as card figures, bullets, inline in Word/PDF; unknown names suggest close matches | missing | SVG paths → `Drawing` | 560 |
| Timeline (`::: timeline`, 2–8 dated entries, past ones grey) and Gantt (`::: gantt`: a table becomes bars, milestones and a today line; an editable table in Word) | missing | English values (`grid=week\|month\|quarter`) | 865 |
| Steps (`::: steps`, chevrons, `{.current}`), Harvey balls (`[3/4]` in a table cell), risk matrix (`::: riskmatrix`, 5 × 5) | missing | Harvey balls drawn, not as glyphs | 760 |
| Image attributes: `{focus=top fit=contain scale=0.8 .round .grey}` after an image | only `"scale=N"` in the title | SkiaSharp for `.grey` (question 9) | 495 |
| Typography: smart quotes per language, dashes, ellipsis, no-break spaces in abbreviations, before units and in dates | missing | Per-language profiles; Markdig's `UseSmartyPants` covers only part | 260 |
| Code highlighting in brand colours | missing | ColorCode (MIT) | 130 |

### Conversions to Markdown

| Feature | Today | Plan |
| ------- | ----- | ---- |
| PDF: tables as pipe tables (full grids and horizontally ruled), drawn bullets and checkboxes as lists, monospace lines as code fences, images in place, superscripts on their line, rotated watermarks dropped | no tables; images after the page's text | All of it; PdfPig only |
| PPTX: speaker notes as `::: notes` | not read | Now; the rest with the features |
| DOCX: native charts as tables, code style as fences, note styles as alerts, footnotes | none of these | Once the DOCX generator exists |

## Behaviour choices

Each point needs a decision. The default recommendation is that **doc-libs' current behaviour stays
the default**, because changing it moves existing users' documents.

1. **Front-matter language:** keep English keys.
2. **A later `#` in decks** stays a divider whose text becomes its subtitle. Numbering is a template
   option. Content under it is not an error.
3. **Text levels:** `###` as a sub-headline and bullets from level 3 apply only with a template that
   declares text levels.
4. **Deck tables:** keep the template's table style, down to 10 pt.
5. **A lone picture** is fitted. It is cropped only when the template declares a picture layout, with
   `focus`/`fit` to control it.
6. **Flow sizes and shapes** are brand options at most.
7. **`==mark==` and status labels:** keep our look.
8. **Raw HTML** stays written as text (question 5).
9. **Bare URLs** stay links (`UseAutoLinks`).
10. **Missing characters in PDFs:** keep the exception (question 6).
11. **Renderers and converters** stay off by default, as empty lists. A `Defaults()` helper is worth
    considering.
12. **Error messages** keep ending with `on line N`.
13. **Speaker notes on continuation slides** are repeated (question 11).
14. **Scene grid:** 160 × 90 over the whole slide (question 13).
15. **Font embedding in decks** stays on by default and can be switched off per brand.
16. **`Generate` returns a result:** a breaking change of the public API in every generator
    (question 4).

## Dependencies

| Need | .NET choice | Note |
| ---- | ----------- | ---- |
| Word | Open XML SDK (already used) | No new dependency |
| QR codes | QRCoder (MIT) | New dependency |
| Icons | Tabler Icons SVG (MIT), embedded | Licence notice in the package |
| Greyscale image / round mask | SkiaSharp (already in `DocumentConversions.Svg`) | Brings Skia into the generators; ImageSharp's licence is not free for all commercial use |
| Code highlighting | ColorCode (MIT) | Language coverage is limited |
| Text diff | own Myers/LCS or DiffPlex (Apache-2.0) | Small |
| Preview images | a Skia painter | No browser |
| Hyphenation | none | No licence-compatible library |

## Proposed order of plans

Revised after the answers below (2026-11-13). Each line becomes a story, or a few, of an epic in
`spec/50-todo/`, written and agreed one at a time. The order puts first what changes the API for
everything after it. Next comes what is small and useful on its own, and last the big new package.

1. E05-S03: **Generation result and notes.** `Generate` returns a `GenerationResult` (breaking). Note
   codes cover what already happens silently today: split, overfull, enlarged, renderer refused.
2. E06-S02: **Markdown foundations.**
   - Footnotes, task lists and definition lists.
   - Document language, with English and German generated-text tables.
   - `Drawing` even-odd fill and validation.
   - Raw HTML stays text.
3. E07-S01: **Speaker notes, builds, transitions.** Generator and converter; a notes master for the
   built-in master; notes repeated on continuation slides.
4. E01-S06: **Template roles for decks**, then numbered dividers and `{layout=…}` across placeholders.
5. E02-S05: **Slide kinds:** agenda, quote, statement, contact and chart layouts, plus
   `::: conclusion` and `::: comment`.
6. E04-S05: **Visual blocks:**
   - QR codes, icons, timeline, Gantt, steps, Harvey balls and risk matrix;
   - image attributes;
   - chart `source:` and `;` CSV.
7. E08-S01: **Converter improvements.** PDF tables, lists, code and images in place; PPTX notes.
8. E09-S01 – E09-S03: **DOCX generator.**
9. E03-S02: **PDF additions shared with DOCX:**
   - TOC with page numbers, heading numbers, captions, footnotes, justification, page breaks;
   - classification marking in all formats.
10. E05-S04, E05-S05: **Check and the AI tool description.**
11. E07-S02, E07-S03: **HTML deck, then preview images** painted from the PPTX with SkiaSharp.
12. E07-S04: **Scenes**, on a 160 × 90 grid over the whole slide.
13. E06-S03: **Typography and code highlighting.**

Out of scope by decision on 2026-11-13: audience versions, backup slides and appendix, training
schedule, handout, consulting containers, variables, includes, data files, cross-references,
abbreviations, accessibility, document history, compare, Word comments and review import. Audience
versions, backup slides and appendices were taken in again on 2026-11-26, see
[Feature gaps](2026-11-25%20feature-gaps.md).

## Risks

- **Real Office.** Every plan needs a check in real Office that the file opens without repair. The
  Open XML validator and the Windows export script help.
- **DrawingML fidelity** of an HTML deck (line spacing, presets, callouts).
- **Scope.** Plans 1–8 already double the generators' surface.
- **Dependencies.** The image library (Skia in the generators) and the preview renderer are decisions
  that affect every consumer.

## Open questions

1. Which areas are wanted at all?

> Wanted: the DOCX generator and the presenting features (notes, builds, transitions, scenes,
> template roles, slide kinds); the HTML deck and preview; visual blocks; converter improvements; PDF
> additions; check and the AI tool description.
>
> **Not wanted:** training and consulting (audience versions, backup slides, schedule, handout,
> minutes, requirements, signatures, calculating tables).

2. Is the proposed order right?

> The proposed order stands.

3. Should template roles be designed to cover one particular template?

> No. Template roles are designed for templates in general.

4. `GenerationResult`: a breaking change to `Generate`, or a new method beside the old one?

> Breaking change: `Generate` returns the `GenerationResult`. Goes in the changelog.

5. Raw HTML: drop it with a note as the new default?

> No. Raw HTML stays written as text.

6. Missing characters in PDFs: keep the exception as default, or replace with `?` and a note?

> Keep the exception as the default.

7. Word drawings: editable shape groups (vectors, consistent with decks), or SVG + PNG images?

> SVG + PNG images.

8. Preview images: Playwright + Chromium, PowerPoint export through `WindowsOnly`, or a Skia painter?

> A Skia painter of the PPTX — no browser.

9. `.grey` / `.round` images: accept SkiaSharp as a dependency of the generators?

> Accept SkiaSharp as a dependency of the generators.

10. Synthetic italic for brands whose font has no italic face?

> Keep "a missing face is an error".

11. Speaker notes on continuation slides: repeat them, or only on the first part?

> Repeat them on every part.

12. Generated texts in other languages: ship German alongside English?

> Ship English and German; `lang: de` switches.

13. Scene grid: 160 × 90 over the whole slide, or 160 × 67 over the content area?

> 160 × 90 over the whole slide.

14. Which further drafts should become plans?

> Only typography and code highlighting. Not variables, includes, data files, long-report features
> (cross-references, abbreviations, accessibility) or the Word review cycle (history, compare,
> comments).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
