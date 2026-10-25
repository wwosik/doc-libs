# 00005 Slide layouts from Markdown

> Status: verify — implemented 2026-10-23

## Summary

Let deck Markdown ask for more than "title plus bullets, or one table, image or diagram": cards
side by side, dark slides and dark panels, a small label above the title (kicker), callouts,
term/description rows, a statement, tag lists, and highlights and status labels in tables. All of
it in syntax that already exists elsewhere (Pandoc, markdown-it, GitHub) and still reads well as
plain Markdown. Decks that use none of it come out exactly as today. The PDF generator accepts the
same Markdown and shows it plainly.

## Context

- **Parser** (`DeckParser`): Markdig with pipe tables, strikethrough and autolinks. A `##` section
  becomes a `ContentSlide` (body placeholder) or, when it holds a table, image or rendered code
  block, a `VisualSlide` (text above, the visual, text below); a second visual starts a
  continuation slide. `###` is a bold paragraph, a quote is italic. Markdig 1.4.2 already ships
  custom containers (`:::`), alert blocks (`> [!NOTE]`) and marked text (`==x==`); they are not
  switched on.
- **Writer** (`DeckWriter`): shapes are placed by estimated heights (`EstimateHeight`,
  `EstimateLines`). Visual slides use the `titleOnly` layout and text boxes with explicit bullets.
- **Template** (`TemplateBuilder`): layouts `title`, `obj`, `titleOnly`, `secHead`.
- **Brand colours** today: `Primary`, `Link`, `Footer`, `TableHeader`, `TableBand`, `TableLine`,
  `Gradient`, the 12 theme colours and the diagram colours. Nothing for highlights or status.
- The PDF generator has its own `MarkdownParser`; Markdown with the new syntax must not come out
  worse there than plain text, and must not fail.

## Proposed approach

### 1. Syntax

| Want | Markdown |
| ---- | -------- |
| Kicker | `## Our data are documents {kicker="Observation"}` |
| Dark slide | `## Processing without a broker {.dark}` |
| Cards | `::: cards` … `:::`, one `###` per card; a paragraph that is only bold text is the card's figure |
| Cards, short form | `::: cards` around a list of `- **Title** – text` |
| Rows (term / description) | `::: rows` around a list of `- **Term** – text` |
| Panel | `::: panel` or `::: panel dark` around any blocks |
| Statement | `::: statement`: the first paragraph large, the rest normal |
| Callout | `> [!NOTE]` (brand colour), `> [!IMPORTANT]` / `> [!WARNING]` (highlight) |
| Tag list | a paragraph of only code spans: `` `Requests` `Groups` `Hosts` `` |
| Labelled tags | inside a container, list items that start with a code span: `` - `write-once` results `` |
| Marked text | `==text==`: bold on a tint of the highlight colour (`a:highlight`) |
| Highlighted row | a table row whose first cell is all `==…==` gets the highlight colour at 15 % |
| Status labels | on flow slides, a table cell that is one code span; its colour comes from front matter `labels.<category>: Label; Label` |

- **Heading attributes are our own parser** (`HeadingAttributes`, shared package), not Markdig's
  generic attributes, which would also take `{…}` after links and code. Only a trailing `{…}` on a
  `##` heading, only `.dark` and `kicker="…"`. A trailing `{…}` that does not match the grammar
  stays text; a matching one with an unknown class or key throws `DocumentConversionException`.
- **Unknown container names and alert kinds throw**, naming the valid ones. The names live in
  `LayoutSyntax` (shared), so both generators give the same messages. Both pipelines add only
  Markdig's block `CustomContainerParser` (`UseCustomContainers()` would also turn `::x::` into
  inline containers).
- The separator in the short forms is the first ` – `, ` - ` or `:` after the bold run.
- **Label categories:** `success`, `info`, `warning`, `danger`, `neutral`. Unassigned labels are
  neutral. An unknown category or a label in two categories throws, listing the valid keys. The
  PDF generator checks the same keys.

### 2. When a slide uses the new layout

A `##` section becomes a new `FlowSlide` only if it has heading attributes, a container or an alert
anywhere, or a top-level tag paragraph. Everything else stays `ContentSlide` / `VisualSlide`, byte
for byte as today. Code-only table cells become status labels only on flow slides, and code-span
lists become labelled tags only inside a container — elsewhere both are common in ordinary decks.
`==mark==` and highlighted rows work on every slide, because `==` meant nothing before.

### 3. Model and writer

- `FlowSlide(Title, Kicker, Dark, Blocks)` holds a list of `FlowBlock`s: text, table, image,
  drawing, code lines, cards, rows, panel, callout, statement, tags, labelled tags. One writer
  method per block measures when given no canvas and draws when given one; cards and panels
  measure their content, draw their background, then the content. Several tables and visuals
  stack on one flow slide instead of starting continuation slides.
- **Kicker:** small bold uppercase text in `Link` at the top of the title area; the title moves
  down by its height.
- **Cards:** no fill, a `TableLine` outline, rounded corners, equal width, as tall as the tallest;
  title bold in `Primary`, figure large in `Link`. A background shape plus separate text boxes, so
  a card can hold any block. More than four wrap into a second row.
- **Rows:** a thin `TableLine` rule per item, the term bold in the left third.
- **Panel:** a rounded rectangle behind its blocks; light = `TableBand`, dark = `Primary` with
  white text.
- **Callout:** a `TableBand` band with a bar on the left in `Primary` (NOTE) or the highlight
  colour (IMPORTANT/WARNING).
- **Statement:** the first paragraph bold at about 1.6 × the text size.
- **Tags:** white chips with an outline (on dark: the darker surface, dark outline), flowing and
  wrapping; labelled tags put the chip in a fixed-width column.
- **Code blocks on flow slides:** a rounded box in `TableLine` at 30 %, 14 pt, line spacing
  130 %. Content and title-only slides are unchanged.
- **Status labels:** the cell gets its category colour at 22 %, the text bold in the normal colour.
- **Sizes:** 16 pt text, 14 pt in cards — the built-in master's content area is only about 4.8″
  high.
- **Dark slides:** the built-in template gets a fifth layout, *Title Only Dark* (no master shapes,
  `Primary` background, white title and footer, `LogoOnDark`). On caller templates: a `p:bg`
  override and explicit white text on every shape we write; the template's own master graphics
  stay (documented limit).
- **Colours** in one place (`SlidePalette`): from the brand when there is one; on a caller's
  template theme-colour references (text 2, accent 1, background 2, accent 2, accents 1–6).

### 4. Brand API

- `BrandColors.Highlight` (`string?`, default theme accent 2) and
  `BrandColors.Labels` → new `BrandLabelColors { Success, Info, Warning, Danger, Neutral }`, each
  optional (defaults green `16A34A`, `Link`, amber `D97706`, red `DC2626`, grey `9CA3AF`).
  Both take part in `BrandColors` equality.
- Brand pack keys `colors.highlight` and `colors.labels.{success,info,warning,danger,neutral}`.
- `ResolvedBrand` gains the resolved highlight and label colours; invalid ones throw
  `ArgumentException` naming the property.

### 5. PDF

The PDF parser switches on the same extensions and degrades: containers render their content as
normal blocks, card titles as `###`, heading attributes are stripped (the kicker is dropped),
alerts render as quotes with their label in bold, tags and labels stay code spans, `==x==` is bold.
`labels.*` is checked and otherwise ignored.

### 6. Tests, samples, docs

- Parser tests per construct, including unknown container and attribute errors, a trailing `{…}`
  that is not an attribute, and a section without new syntax still giving `ContentSlide` /
  `VisualSlide`.
- Writer tests: shapes, fills and text colours of cards, panels, callouts, tags, code lines;
  dark slides on the built-in and a caller template; kicker above the title; label colours.
- Brand and brand pack tests for `Highlight` and `Labels`.
- A made-up sample deck `layouts.md` using every construct, with
  `layouts-{neutral,sample}.roundtrip.expected.md` (PPTX) and a PDF round trip; the existing
  `deck-*` and `report-*` expectations stay unchanged.
- Open the generated decks in PowerPoint (COM export to PNG, see `ai/memory.md`): no repair
  prompt, and look at every slide.
- Docs: a *Layouts* chapter in `wwosik.DocLibs.DocumentGeneration.Pptx.md`, the shared syntax and
  the new brand colours in `wwosik.DocLibs.DocumentGeneration.md`, the PDF degradation in the PDF
  docs, `ai/memory.md`.

## Acceptance criteria

- [ ] Every construct in the syntax table is parsed and drawn as described.
- [ ] Markdown without the new syntax produces the same decks and PDFs as before (existing
      expected files unchanged).
- [ ] Typos in container names, alert kinds, heading attributes and label categories throw
      `DocumentConversionException` naming the valid values.
- [ ] Dark slides work on the built-in template and on a caller template.
- [ ] `BrandColors.Highlight` and `BrandColors.Labels` work in code and in brand packs.
- [ ] The PDF generator accepts the same Markdown and degrades as described.
- [ ] The sample deck opens in PowerPoint without repair.
- [ ] Docs updated; all tests pass; Release build has 0 warnings.

## Out of scope

- Measuring text instead of estimating it; splitting an overfull flow slide.
- Two columns side by side (`:::: columns`).
- Syntax colouring inside code blocks.
- A kicker derived from the enclosing `#` section.

## Open questions

1. Use the refined design (outline-only cards, the code-lines box, label categories from front
   matter), rather than a simpler first version (filled cards, label colours by order of
   appearance)?

> Confirmed by the human (2026-10-23): the refined design (outline-only cards, the code-lines box, label categories from front matter).

2. The neutral brand: keep the defaults above for highlight and labels, or give it colours of its
   own?

> Confirmed by the human (2026-10-23): the defaults.

3. Should the made-up sample brand `sample` set `colors.highlight` and `colors.labels`, so the
   brand pack path is covered by a round trip and not just a unit test?

> Confirmed by the human (2026-10-23): yes — `sample` sets `colors.highlight` (`#F59E0B`) and two of the five `colors.labels` (success, danger), so the defaults of the others are covered too.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- **When a section becomes a flow slide:** heading attributes, a `:::` container or an alert
  anywhere in it, or a tag paragraph (two or more code spans, nothing else) as a top-level block. A
  tag paragraph nested in a list does not count, so list items of code spans never switch a slide.
- **Cards** are a background shape plus separate text boxes, not one shape holding the text, so a
  card can hold any block (tags, labelled tags, tables). Still editable, but not as one object.
- **Colours** (`SlidePalette`): `Primary` for card titles, chips, statements and the dark
  background; `Link` for kickers and figures; `TableBand`/`TableLine` for boxes; `Highlight` for
  highlighted rows (15 %), `==marked==` text (bold, `a:highlight`) and the bar of important
  callouts; `Labels` for status labels (cell at 22 %, text bold in the normal colour). On a
  caller's template (no brand) theme-colour references: text 2, accent 1, background 2, accent 2,
  accents 1–6.
- **Dark slides:** the built-in template has a fifth layout, *Title Only Dark* (type `cust`, found
  by name, relationship `rId7`; it shares the section layout's `LogoOnDark` image part). Without
  it, the slide gets a `p:bg` override and white text on title, footer fields and every shape
  written.
- **Sizes** on flow slides are smaller than on content slides (16 pt text, 14 pt in cards), since
  the content area of the built-in master is only about 4.8″ high.
- The sample deck `layouts.md` is a made-up project ("Project Harbour") using every construct.

### Deviations from the plan

- None in behaviour. Two columns and fitting stay out of scope.

### Breaking changes

- Markdown that generated before can now fail: `::: name` blocks, a trailing `{…}` that matches the
  attribute grammar on a `##` heading, `> [!KIND]` alerts and `labels.*` front matter keys are
  syntax, and unknown names throw. `==text==` is marked text in decks and bold in PDFs. In
  `CHANGELOG.md`.

### Verified

- All tests pass (shared 78, PPTX 122, PDF 111, Diagrams 67, conversions 104; WindowsOnly 6
  skipped on Linux); Release build 0 warnings. No existing `*.expected.*` file changed.
- New expectations: `layouts-{neutral,sample}.roundtrip.expected.md` (PPTX),
  `layouts-neutral.roundtrip.expected.md` (PDF).
- `layouts-neutral.pdf` passes veraPDF (PDF/A-2b); I looked at its pages (`.tmp/review/`).
- One clean build crashed once with `Internal CLR error (0x80131506)`; two rebuilds succeeded with
  0 warnings. Probably the machine, but worth watching.

### To verify by hand

- **The decks were not looked at in PowerPoint.** The export through PowerPoint (COM) hung on a
  running PowerPoint instance, and access to PowerPoint's window was declined, so nothing was
  opened there. Open `.tmp/review/layouts-neutral.pptx` and `layouts-sample.pptx` (no repair
  prompt; look at every slide). They pass the Open XML validator in the tests.

### Loose ends

- Heights are estimated; a dense flow slide can run past the content area, nothing splits it.
- Two columns (`:::: columns`), a dark code panel, syntax colouring.
