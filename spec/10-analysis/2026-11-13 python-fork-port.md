# Porting the Python fork's features back to .NET

> Date: 2026-11-13. Subject: a private Python fork of doc-libs, taken from release `2610.28.22`
> (commit `2206e4a`, still this repo's `HEAD`) on 2026-11-06 and developed on its own since.

This analysis establishes what the fork has that doc-libs does not, which parts of that are generic
enough to belong in this public, client-neutral repo, and in what order they could come here as
plans. It proposes no code; the plans follow once the open questions at the end are answered.

## Summary

- The fork is about **39 000 lines of Python plus 22 000 lines of tests** across 17 epics, against
  roughly 17 000 + 7 000 lines of C# here. About two thirds of its stories are implemented (and
  awaiting review there); the rest are drafts with no code.
- It was built for **one client's design only**: it writes Word and PowerPoint onto that client's own
  templates, its PDF redraws the client's Word page frame, and its front-matter keys, variant names
  and generated texts are German. None of that can come here. What can come is the **mechanism**
  behind each feature, driven by `Brand` / brand pack data, with English keys.
- Some of the fork's features were ported from another private .NET derivative of doc-libs, not from
  doc-libs itself (generation notes, speaker notes, builds, transitions, scenes, an HTML deck, an AI
  tool description). Those are missing here too.
- The largest single item is a **DOCX generator**, which doc-libs does not have. The second largest
  is the set of **presenting features** for decks (notes, builds, transitions, scenes, HTML deck,
  preview images). Everything else is a few hundred lines per feature.
- A handful of fork choices conflict with doc-libs' existing behaviour (see
  [Behaviour differences](#behaviour-differences)). The recommendation is to keep doc-libs'
  behaviour and offer the fork's as an option where it is worth having, so existing decks and PDFs do
  not move.

## What does not come over

| Fork part | Why not |
| --------- | ------- |
| The client's `.dotx`/`.potx` files, logos, colours, fonts, layout names, placeholder indices, sizes | Client design; it belongs in the client's own brand package |
| The client's four Word variants and two deck variants as front-matter values | Same; in doc-libs a variant is a brand or a template option |
| German front-matter keys and values (`vorlage`, `ja`/`nein`, `status.*`, `fassung: kunde`, `raster=monat`, …) | doc-libs is English; keys stay English (`template`, `true`, `labels.*`) |
| Reading styles and the page frame out of the Word template for the PDF | Ties the PDF to one Word template; doc-libs' PDF takes its look from the `Brand` |
| mermaid-cli (Node.js + Chromium) | doc-libs' Mermaider is better here: no external runtime. Same eight diagram types |
| The fork's PDF engine on reportlab | doc-libs has its own engine on PdfPig; only its *features* come over (see E05 below) |
| Removing the neutral brand, built-in master, brand packs, `CustomTemplateOptions` | The fork's premise; the opposite of doc-libs' |
| System-font lookup on disk | Breaks the rule that the libraries read no files; a caller can still pass fonts in |
| Synthetic italic (12° skew) | doc-libs deliberately treats a missing face as an error; could become an opt-in, see questions |
| Python scaffolding, `run.sh` verbs for the fork's own folder layout, document skeletons | Tooling of that project |

## Inventory

Status is the fork's: **impl** = implemented, **draft** = specified, no code. "Here" is doc-libs
today. Sizes are the fork's Python lines without tests, as a rough guide to effort.

### Foundations

| Feature | Fork | Here | Generic part to port | Size |
| ------- | ---- | ---- | -------------------- | ---: |
| Generation notes: `Generate` returns a result with notes (`slide-split`, `slide-overfull`, `image-cropped`, `image-enlarged`, `raw-html`, `code-not-drawn`, `character-missing`, `notes-dropped`, …), slide and page counts | impl | missing; `Generate` returns `void` and splits, drops and shrinks silently | All of it. A `GenerationResult` / `GenerationNote(Code, Line, Message)` record; many call sites | 250 + spread |
| Check: run the generator without output and apply style rules (bullets per slide, words per slide, title length, alt text, image source, …), statistics (slides by kind, estimated talk time, estimated pages) | impl | missing | All of it; limits as options, rules not tied to one language | 840 |
| Document language: every generated string ("Page", "As of", "Table", "Figure", "Contents", "(cont.)", alert titles) from one table per language, chosen by front matter | impl | missing; texts are English or numeric | A `lang` key and a text table; English default, other languages as data | small |
| Footnotes, task lists, definition lists in the generators | impl | parsed nowhere (Markdig has `UseFootnotes`, `UseTaskLists`, `UseDefinitionLists`) | All of it | small |
| Raw HTML dropped and reported instead of written as text | impl | PDF writes it as text | Behaviour change, see differences | small |
| `Drawing`: even-odd fill rule, `DrawingSvg` rejects invalid colours and NaN | impl | missing | Both | small |
| SVG embedded next to its PNG (`asvg:svgBlip`) in decks, so Office shows it sharp | impl | **there** for decks (converted SVGs keep their original); missing for Word | Only with the DOCX generator | 60 |
| Authoring guides with per-name texts checked by tests against the syntax lists | impl | guides exist; the texts are not tied to the code lists | The test pattern | — |
| Feature tour: samples that must cover every key, container, attribute and alert, generate with no notes and round-trip through the converters | impl | samples and `*.expected.md` exist; no coverage check | The coverage test | 500 tests |
| AI tool description: name, JSON Schema, combined guide for all offered formats; `Run` and `Reply` in fixed JSON | impl | missing | All of it, with English parameter names, System.Text.Json only | 870 |
| Template preparation: strip a `.dotx`/`.potx` of slides, customXml, thumbnails, orphaned media, personal data; deterministic zip | impl | the PPTX generator drops slides and sections at run time only | A `tools/` program for brand-pack authors | 490 |
| EMF → SVG for logos taken from Office templates | impl | missing; brand images are PNG/JPEG | A tool, plus possibly SVG `BrandImage`s drawn as vectors | 440 |

### DOCX generator (new package)

| Feature | Fork | Generic part to port | Size |
| ------- | ---- | -------------------- | ---: |
| Document on a template: cover from front matter (title, subtitle, author, location, date), header and footer, core properties, TOC field | impl | All of it, as `wwosik.DocLibs.DocumentGeneration.Docx` | 430 |
| Content: numbered Heading 1–3, unnumbered deeper headings, lists to three levels (numbered lists restart), tables at text width with repeating header and `TextMetrics` widths, table title (`**Title**` before), captions (`Table: …` after, `"Caption"` on an image) with `SEQ` fields, images fitted to 80 % of the text height, code style, quotes, real footnotes, links, task and definition lists | impl | All of it | 1 000 |
| Slide syntax in Word: containers written in sequence, alerts as note styles, `==mark==`, `[words]{.success}`, status labels, image rows as a borderless table | impl | All of it | 350 |
| Native charts (same chart part and `.xlsx` as decks) and drawings | impl | Charts: reuse `ChartWriter`. Drawings: the fork writes SVG + PNG images; doc-libs' rule says vectors, so a `wpg` shape group like the deck's | 520 |
| DOCX authoring guide | impl | All of it | 70 |
| Classification marking (confidential / internal / draft) in the header, on the cover, and a draft watermark; same key for decks and PDFs | impl | The mechanism; texts and placement from brand and options | 370 |
| Page breaks (`::: pagebreak`, `{.newpage}`) and landscape sections | draft | All of it | — |
| Document history table (`::: history` or rows passed by the caller) | draft | All of it; reading `git log` stays in the caller | — |
| Compare two Markdown versions → Word with tracked changes | draft | All of it; needs a diff (own Myers/LCS or DiffPlex, Apache-2.0) | — |
| Word comments from Markdown, and importing a reviewed `.docx` back (comments, insertions, deletions) | draft | All of it; reuses the compare matching | — |
| Abbreviations and glossary (`*[SLA]: …` → list of abbreviations, first-use expansion) | draft | All of it; Markdig has `UseAbbreviations` | — |
| Cross-references (`[](#costs)` → "Table 3", heading numbers, page references) as Word fields, PDF links | draft | All of it | — |
| Accessibility: alt-text checks, decorative images, document language, table captions; tagged PDF later | draft | All of it | — |
| Consulting containers: minutes (`::: decision`, `::: task`, `::: open` → lists at the end), requirements with stable IDs and a list, signature block, calculating tables (totals, VAT) | draft | The mechanisms, with English names and culture-driven numbers | — |

How the fork builds Word: it opens the client's template, empties the body, and writes paragraphs
that only reference the template's styles (by name, never by ID). It uses no direct formatting
except in code, creates missing styles (`Code`, note styles, mark, status labels) on demand, and
keeps the template's numbering, theme, headers and footers. It fills the template's placeholder
texts in headers and footers. A neutral version would mirror the PPTX generator: a **built-in
template written in code from a `ResolvedBrand`** (as `TemplateBuilder` does for decks), plus
**caller templates** with a role → style-name map (as `PptxLayoutNames` maps layouts). A brand pack
could carry a `.dotx` the way `PptxBrandTemplate` carries a `.potx`.

### Decks

| Feature | Fork | Here | Generic part to port | Size |
| ------- | ---- | ---- | -------------------- | ---: |
| More template roles: layouts found by name for two-content, text+picture, picture, agenda, quote, statement, contact, chart, dark; several title layouts to choose from; placeholders addressed by role (subtitle, byline, chapter number, second body, picture, chart title/unit/source); text levels (`###` / paragraph / bullet → outline level); placeholders kept empty because they only draw decoration; footer pattern; content grid from the master's guides | impl | `PptxBrandTemplate` / `CustomTemplateOptions.Layouts` know title, content, titleOnly, sectionHeader, closing; one body placeholder | All of it, as brand-pack data — this is what lets a client's template work as well as the fork's does, without the client being named here | 1 500 shared |
| Numbered chapter dividers ("01", "02"); `{layout="…"}` that splits text at `---` across the layout's text placeholders and fills its picture and chart placeholders | impl | dividers without number; one body placeholder | Both, opt-in | in the above |
| Slide kinds: `{.agenda}` (from a list or the chapter headings), `{.quote}`, `{.interim}` statement, `{.contact}` (1–5 people), chart layouts with one or two charts | impl | missing | All of it, each needing its layout role | 540 |
| `::: conclusion` (a box at the foot of the content area) and `::: comment` (a callout over the previous block); two text-only columns go into the template's two-column layout | impl | missing | All of it | in flow 1 450 |
| Speaker notes: `::: notes` under any heading, on the notes master; the PPTX converter reads them back as `::: notes` | impl | missing in generator and converter | All of it; the built-in master needs a notes master | 150 + converter |
| Builds: `{.build}`, `{.frag}`, `{.build=appear\|fly\|wipe}`, `{.exit}`, `{.emphasize}` before a block; `build: lists` in front matter | impl | missing | All of it | 680 |
| Transitions: `transition: fade` in front matter or on a heading; none, fade, push, wipe, split, cover, uncover, zoom, cut, morph (with fallback) | impl | missing | All of it | 100 |
| Scenes: `::: scene` with named shapes on a grid, steps as builds, Morph between slides; a still image in PDF | impl | missing | All of it; the grid differs between the fork (content area) and the other derivative (whole slide) — a choice to make | 930 |
| Audience versions: `{.internal}`, `::: internal`, `::: exercise`, `::: solution`, chosen by an option; filtering before every generator | impl | missing | All of it, English values (e.g. `public`, `internal`, `trainer`) | 370 |
| Backup slides (`# Backup {.backup}` after the closing slide; out of agenda and numbering) and appendix chapters lettered A, B, A.1 in Word and PDF | impl | missing | All of it | 150 + writers |
| Training schedule: `{duration=5}` on slides and chapters, break slides, clock times in the agenda, planned times in the trainer's notes, a schedule table | impl | missing | All of it; output as Markdown or PDF until DOCX exists | 620 |
| Participant handout: slide pictures with notes or ruled lines, built as Markdown through the DOCX generator | impl | missing | All of it; needs slide pictures (preview) and DOCX or PDF | 760 |
| HTML deck: one self-contained file drawn **from the generated PPTX's DrawingML** (so splitting and errors are the deck's), keys, notes view, print | impl | missing; the other derivative draws its HTML deck from the slide model instead | All of it; no new dependency, but DrawingML fidelity is the risk | 2 800 |
| Preview images: each slide as PNG plus a contact sheet, rendered from the HTML deck in headless Chromium | impl | missing | The API; the renderer is an open question | 430 |
| Tables shrink to 12 pt with direct formatting, header fill and right-aligned number columns; a picture cropped into a picture placeholder | impl | table style, down to 10 pt; pictures fitted | Number-column alignment; the rest is a difference, see below | — |

### PDF

| Feature | Fork | Here | Generic part to port |
| ------- | ---- | ---- | -------------------- |
| Table of contents with real page numbers (layout repeated until numbers settle, at most 4 passes) | impl | missing | All of it |
| Heading numbers (1.2) in a hanging indent, numbered bookmarks | impl | missing | Opt-in |
| Footnotes at the page foot | impl | missing | All of it (the fork's still lands some a page early) |
| Captions "Figure n" / "Table n" | impl | missing | All of it, shared with DOCX |
| Justified text | impl | missing | Opt-in; no hyphenation (no licence-compatible library found) |
| Alerts as a box with a rule; drawn bullets; task lists ☐/☒ | impl | alerts as quote with kind | Box style as a brand option |
| Missing characters: default `?` plus a note, no-break spaces borrow the space glyph | impl | throws unless `ReplaceUnsupported` | The note; the default is a difference, see below |

### Diagrams, charts, visuals

| Feature | Fork | Here | Generic part to port | Size |
| ------- | ---- | ---- | -------------------- | ---: |
| `chart`: `source:` setting drawn under the chart; CSV with `;` and decimal comma | impl | missing | Both | small |
| QR codes: `![Survey](qr:https://… "Caption")` in the brand colour | impl | missing | All of it; QRCoder (MIT) or drawn as a `Drawing` | 155 |
| Icons: `:icon-check:` from 162 bundled Tabler Icons (MIT, notice shipped) as card figures, bullets, inline in Word/PDF; unknown names suggest close matches | impl | missing | All of it; SVG paths → `Drawing` | 560 |
| Timeline (`::: timeline`, 2–8 dated entries, past ones grey) and Gantt (`::: gantt`, table → bars, milestones, today line; editable table in Word) | impl | missing | All of it, English values (`grid=week\|month\|quarter`) | 865 |
| Steps (`::: steps`, chevrons, `{.current}`), Harvey balls (`[3/4]` in a table cell), risk matrix (`::: riskmatrix`, 5 × 5) | impl | missing | All of it; Harvey balls drawn, not as Unicode glyphs (Liberation has none) | 760 |
| Image attributes: `{focus=top fit=contain scale=0.8 .round .grey}` after an image | impl | only `"scale=N"` in the title | Focus, fit, round: pure Open XML / PDF. Grey and round masks need an image library | 495 |
| Variables `{{customer}}` from front matter or options, built-ins like `today` | draft | missing | All of it, English keys | — |
| Includes: `::: include company-profile`, blocks passed in by the caller as resources | draft | missing | All of it; fits the no-file-system rule | — |
| Tables and charts from CSV/XLSX resources (`src=`, range, header rows) | draft | missing | All of it; Open XML SDK for XLSX | — |
| Typography: smart quotes per language, dashes, ellipsis, no-break spaces in abbreviations, before units, in dates | impl (Word, decks) | missing | All of it, per-language profiles; Markdig `UseSmartyPants` covers only part | 260 |
| Code highlighting in brand colours | impl (Word, decks) | missing | All of it; ColorCode (MIT) or TextMateSharp | 130 |
| Terminology rules and a sources file (`[@key]` → footnote, sources list) | draft | missing | The mechanism; the term list is the caller's data | — |

### Conversions to Markdown

| Feature | Fork | Here | Generic part to port |
| ------- | ---- | ---- | -------------------- |
| PDF: tables as pipe tables (full grids and horizontally ruled), drawn bullets and checkboxes as lists, monospace lines as code fences, images in place, superscripts on their line, rotated watermarks dropped | impl | no tables; images after the page's text | All of it; PdfPig only |
| PPTX: speaker notes as `::: notes`; Harvey balls, icons, round pictures back to their syntax | impl | notes not read | Notes now; the rest with the features |
| DOCX: native charts as tables, code style as fences (with language), note styles as alerts, footnotes, comments | impl (partly draft) | none of these | All of it, once the DOCX generator exists |
| JIRA wiki, WindowsOnly | dropped there | kept here | — |

## Behaviour differences

Where the fork deliberately does something other than doc-libs. Each needs a decision; the
default recommendation is **doc-libs' behaviour stays the default**, because changing it moves
existing users' documents.

1. **Front-matter language.** German keys and `ja`/`nein` there; English here. Keep English.
2. **Later `#` in decks:** a numbered divider, and content under it is an error. Here a divider
   whose text becomes its subtitle. Numbering could be a template option; the error should not come.
3. **Text levels:** `###` is a level-1 sub-headline and bullets start at level 3. Here `###` is a
   bold paragraph and bullets follow depth. Only with a template that declares text levels.
4. **Tables in decks:** direct formatting and 12 pt minimum there; template table style and 10 pt
   here. Keep.
5. **A lone picture:** cropped into a picture placeholder there; fitted here. Cropping only when
   the template declares a picture layout, with `focus`/`fit` to control it.
6. **Flow sizes and shapes** (18 vs 16 pt text, square cards, statement 24 vs 26 pt). Client look;
   at most brand options.
7. **`==mark==`** as a yellow highlight, status labels as a coloured dot. Client look; keep ours.
8. **Raw HTML** dropped with a note there, written as text here. Recommend adopting the fork's —
   it is what a writer expects — but it is a visible change to PDFs, so it goes in the changelog.
9. **Bare URLs** are not links there (`linkify` off); here `UseAutoLinks`. Keep ours.
10. **Missing characters in PDFs:** `?` plus a note there; an exception here. With
    `GenerationResult`, a note becomes possible; whether the default changes is a question.
11. **Renderers and converters on by default** there; empty lists here. Keep ours (explicit
    dependencies), but consider a `Defaults()` helper.
12. **Error messages** start with `Line N:` there; end with `on line N` here. Keep ours.
13. **Speaker notes repeat on continuation slides** there; the other derivative puts them on the
    first slide only.
14. **Scene grid:** 160 × 67 over the content area there, 160 × 90 over the whole slide in the
    other derivative.
15. **No font embedding** in decks there (because of the client's font licence). Here embedding is
    on by default and can be switched off per brand; nothing to change.
16. **`Generate` returning a result** instead of `void` is a breaking change of the public API in
    every generator; an overload or a new method name would avoid it.

## Dependencies

| Need | Fork | .NET choice | Note |
| ---- | ---- | ----------- | ---- |
| Word | python-docx + lxml | Open XML SDK (already used) | No new dependency |
| QR codes | segno | QRCoder (MIT) or own `Drawing` | New dependency unless drawn |
| Icons | Tabler Icons SVG (MIT) | same files, embedded | Licence notice in the package |
| Image greyscale / round mask | Pillow | SkiaSharp (already in `DocumentConversions.Svg`) | Would pull Skia into the generators; ImageSharp's licence is not free for all commercial use |
| Code highlighting | Pygments | ColorCode (MIT) or TextMateSharp | Language coverage differs |
| Text diff (compare, review import, guide merge) | difflib | own Myers/LCS or DiffPlex (Apache-2.0) | Small |
| XLSX data | openpyxl (planned) | Open XML SDK | Number formats are the risk |
| Preview images | Playwright + Chromium over the HTML deck | Microsoft.Playwright, PowerPoint COM in `WindowsOnly`, or a Skia painter | See open questions |
| Hyphenation | none (licences) | none | — |

## Proposed order of plans

Revised after the answers below (2026-11-13). Each line becomes a story (or a few) of an epic in
`spec/50-todo/`, written and agreed one at a time; the ids were allocated when the repo moved to
epics and stories the same day. The order puts first what changes the API for
everything after it, then what is small and useful on its own, then the big new package.

1. E05-S03: **Generation result and notes** — `Generate` returns a `GenerationResult` (breaking); note codes
   for what already happens silently today (split, overfull, enlarged, renderer refused).
2. E06-S02: **Markdown foundations** — footnotes, task lists, definition lists, document language with
   English and German generated-text tables, `Drawing` even-odd and validation. Raw HTML stays text.
3. E07-S01: **Speaker notes, builds, transitions** — generator and converter; notes master for the built-in
   master; notes repeated on continuation slides.
4. E01-S06: **Template roles for decks** — the extended `PptxBrandTemplate` / `CustomTemplateOptions`
   (layout roles, placeholder roles, text levels, decorative placeholders, footer pattern, grid),
   then numbered dividers and `{layout=…}` across placeholders.
5. E02-S05: **Slide kinds** — agenda, quote, statement, contact, chart layouts, `::: conclusion`,
   `::: comment`.
6. E04-S05: **Visual blocks** — QR codes, icons, timeline, Gantt, steps, Harvey balls, risk matrix, image
   attributes (SkiaSharp for `.grey` / `.round`), chart `source:` and `;` CSV.
7. E08-S01: **Converter improvements** — PDF tables, lists, code, images in place; PPTX notes.
8. E09-S01 – E09-S03: **DOCX generator** — built-in template and caller templates, content, slide syntax, charts and
   drawings (SVG + PNG), authoring guide. Likely two or three plans.
9. E03-S02: **PDF additions shared with DOCX** — TOC with page numbers, heading numbers, captions, footnotes,
   justification, page breaks, classification marking in all formats.
10. E05-S04, E05-S05: **Check and the AI tool description.**
11. E07-S02, E07-S03: **HTML deck, then preview images** painted from the PPTX with SkiaSharp.
12. E07-S04: **Scenes** — 160 × 90 grid over the whole slide.
13. E06-S03: **Typography and code highlighting.**

Out of scope by decision: audience versions, backup slides and appendix, training schedule, handout,
consulting containers, variables, includes, data files, cross-references, abbreviations,
accessibility, document history, compare, Word comments and review import.

## Risks

- **Nothing in the fork was opened in Office.** Its development machine has neither Office nor
  LibreOffice, so "opens without repair" is unchecked for builds, transitions, Morph, the sticker,
  the Word watermark and the handout. Here the Open XML validator and the Windows export script help,
  but each plan needs a check in real Office.
- **DrawingML fidelity** of an HTML deck drawn from the PPTX (line spacing, presets, callouts).
- **Scope.** All of the above is roughly the size of doc-libs again. Plans 1–8 already double the
  generators' surface.
- **The image library question** (Skia in the generators) and the **preview renderer** (a browser
  download in CI) are dependency decisions that affect every consumer.

## Open questions

1. Which areas do you want at all? In particular: the DOCX generator, the presenting features
   (notes, builds, transitions, scenes), the HTML deck and preview, the training features (audience,
   schedule, handout), the consulting containers (minutes, requirements, signatures, calculating
   tables), the AI tool description.

> Wanted: DOCX generator, presenting (notes, builds, transitions, scenes, template roles, slide kinds), HTML deck and preview, visual blocks, converter improvements, PDF additions, check and the AI tool description. **Not wanted:** training and consulting (audience versions, backup slides, schedule, handout, minutes, requirements, signatures, calculating tables).

2. Is the proposed order right, or is something more urgent (for example the DOCX generator first,
   because a consumer is waiting for it)?

> The proposed order stands.

3. Should a consumer of the fork be able to switch to doc-libs? If yes, the fork's client design
   would become a private brand package outside this repo, and the template roles of plan 4 must
   cover everything that design needs. Should that brand package be part of this effort?

> No. The fork stays independent; doc-libs takes only the generic mechanisms. Template roles are designed for templates in general, not to cover that design.

4. `GenerationResult`: a breaking change to `Generate` (it returns the result), or a new method
   beside the old one?

> Breaking change: `Generate` returns the `GenerationResult`. Goes in the changelog.

5. Raw HTML: adopt the fork's behaviour (dropped, with a note) as the new default?

> No. Raw HTML stays written as text.

6. Missing characters in PDFs: keep the exception as default, or replace with `?` and a note?

> Keep the exception as the default.

7. Word drawings: editable shape groups (vectors, consistent with decks), or SVG + PNG images as in
   the fork?

> SVG + PNG images, as in the fork.

8. Preview images: Playwright + Chromium (cross-platform, large download), PowerPoint export through
   `WindowsOnly` (exact, Windows only), or a Skia painter (no browser, much more work)?

> A Skia painter of the PPTX — no browser.

9. `.grey` / `.round` images need pixel work: accept SkiaSharp as a dependency of the generators,
   or limit image styles to what Open XML and PDF can do without it (crop, ellipse geometry)?

> Accept SkiaSharp as a dependency of the generators.

10. Synthetic italic for brands whose font has no italic face: opt-in, or keep "a missing face is
    an error"?

> Keep "a missing face is an error".

11. Speaker notes on continuation slides: repeat them, or only on the first part?

> Repeat them on every part.

12. Generated texts in other languages: ship German alongside English, or only English with the
    table open for callers?

> Ship English and German; `lang: de` switches.

13. Scene grid: 160 × 90 over the whole slide, or 160 × 67 over the content area?

> 160 × 90 over the whole slide.

14. Which of the fork's unimplemented drafts should become plans here?

> Only typography and code highlighting. Not variables, includes, data files, long-report features (cross-references, abbreviations, accessibility) or the Word review cycle (history, compare, comments).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
