# E04-S07 Timeline and Gantt

> Status: in verification
> Epic: [E04 Diagrams and charts](../50-todo/E04-diagrams-and-charts.md)

## Summary

Two containers that draw a schedule from text. `::: timeline` turns a list of 2–8 dated entries into
a horizontal timeline, entries in the past in grey. `::: gantt` turns a table of tasks with start
and end dates into bars on a week, month or quarter grid, with milestones and a line for today. Both
are `Drawing`s, so decks get editable shapes and PDFs vectors. The story also lays the shared
ground for drawn containers that E04-S08 builds on.

## Context

- **Containers:** both parsers add Markdig's `CustomContainerParser` (block form only).
  `LayoutSyntax.CheckContainer` accepts only `cards, rows, panel, statement, columns` and throws
  "Unknown container …" for anything else, in decks and PDFs alike. `::: timeline` is therefore an
  error today, and adding it changes no working document.
  - Decks: a section with a container becomes a `FlowSlide` (`DeckParser.UsesLayouts`), and
    `DeckParser.Container` turns each container into a `FlowBlock`.
  - PDFs: `MarkdownParser.Add` checks the container and shows its children plainly.
  - Arguments are free text after the name, checked per container (`::: columns 1:2`,
    `::: panel dark`).
- **Drawings:** a `Drawing` in a flow is a `DrawingBlock` in both models. Decks fit it into its box,
  enlarged at most 1.5 times (`DeckWriter.Drawing`); PDFs scale it down to the text width
  (`LayoutEngine.Drawing`). `DrawingText.MaxWidth` shrinks a label down to 70 %, measured in the real
  font when placed.
- **Colours:** `ResolvedDiagramColors` (`Text`, `Line`, `Accent`, `Surface`, `Border`, `Palette`),
  `BrandColors.Highlight` for slides, `ResolvedBrand.DarkSlideBrand` for dark slides. Charts and
  Mermaid take the slide's brand, so they are drawn for dark backgrounds.
- **Today:** both options have a `TimeProvider` (`PptxOptions.TimeProvider`,
  `PdfOptions.TimeProvider`), used for the cover date; tests pin it.
- **Generated texts:** `GeneratedTexts` (E06-S02) holds per-language texts; there are no month
  names yet. The cover date is `dd.MM.yyyy` in every language (E06-S02, answer 5).
- E09 (DOCX) will reuse the drawings as SVG + PNG; the fork also writes the Gantt table as an
  editable table in Word, which stays for E09.

## Proposed approach

1. **Shared ground: drawn containers.**
   - A new internal `VisualContainers` in DocumentGeneration (which references Markdig since
     E06-S02): `IsVisual(name)`, `Check(name, arguments)` and
     `Draw(CustomContainer, VisualContext) → Drawing`. `VisualContext` holds the brand (the slide's
     brand in decks), today's date, the document's `GeneratedTexts`, the line and the format.
   - `LayoutSyntax.CheckContainer` hands visual names to `VisualContainers.Check`, and
     `LayoutSyntax.Containers` lists them, so both formats accept and reject the same.
   - Both parsers turn a visual container into a `DrawingBlock`. In decks it makes the section a
     flow slide like any container; in PDFs it is drawn instead of being shown plainly.
   - Inline text inside entries becomes `DrawingSpan`s (bold kept, other emphasis as plain text,
     code as mono), through one shared helper.
   - Arguments are `key=value` pairs after the name; an unknown key or value throws
     `DocumentConversionException` listing the valid ones.
2. **Dates.**
   - Written in ISO form: `2026`, `2026-Q2`, `2026-03` or `2026-03-15`. Each is a period, from its
     first to its last day. Anything else throws, naming the entry and the accepted forms.
   - Shown in the document's language: `GeneratedTexts` gains `MonthNames` (twelve short names,
     "Jan" … / "Jan." …) and `QuarterFormat` ("Q{0}"), English and German. A day is shown as
     "15 Mar 2026" / "15. März 2026", a month as "Mar 2026" / "März 2026", quarter and year as
     written. No `CultureInfo`, so invariant-globalization hosts get the same text.
   - **Today** is the `TimeProvider`'s date in UTC, or `today=2026-11-01` on the container, which
     pins it so a document looks the same whenever it is generated.
3. **`::: timeline`.**

   ```markdown
   ::: timeline
   - **2026-01** Kick-off
   - **2026-03** Design review: scope and budget agreed
   - **2026-06-30** Go-live
   :::
   ```

   - 2 to 8 list items, each starting with a bold date, then a title and an optional description
     after `:`. Fewer or more throws; items need not be in order and are sorted by date, equal dates in
     the order written.
   - Drawn as a horizontal line with one dot per entry at even spacing (not to scale, so labels
     never collide), the date above and title and description below, alternating above and below
     when there are more than five entries. Entries whose period ended before today are grey
     (diagram `Line`), the next one in the accent colour with a ring, later ones in the accent
     colour. Size 960 × 300 px.
4. **`::: gantt`.**

   ```markdown
   ::: gantt grid=month
   | Task         | Start      | End        |
   |--------------|------------|------------|
   | Analysis     | 2026-01-05 | 2026-02-13 |
   | Build        | 2026-02-16 | 2026-05-29 |
   | Go-live      | 2026-06-01 |            |
   :::
   ```

   - One pipe table; the first three columns by position are task, start and end, whatever their
     headers say. A row without an end, or with end = start, is a milestone (a diamond). 1 to 20
     rows; an end before its start throws.
   - `grid=week|month|quarter`; without it, the grid is chosen from the span: up to 10 weeks →
     week, up to 18 months → month, else quarter. The axis shows the grid's labels (week numbers
     "W12" / "KW 12" from `GeneratedTexts`, month names, quarters).
   - Bars in the diagram palette by row order; a bar that ended before today grey; a vertical line
     for today in `Highlight` when today is within the span. Task names left, in the table font,
     shrunk with `MaxWidth`. Width 960 px, 28 px per row plus the axis.
5. **Notes.** No new note codes: malformed entries throw, as other containers do.
6. **Dark slides.** The slide's brand is passed (`DarkSlideBrand`), so lines and text are light on
   dark flow slides.
7. **Dependencies and licences.** None new.
8. **Guides, docs, changelog.**
   - Both authoring guides: both containers, the date forms, `grid=` and `today=`, limits, with an
     example each. `LayoutSyntax.Containers` feeds the "valid containers" text. The expected guides
     are regenerated.
   - Docs: DocumentGeneration (`VisualContainers`, `GeneratedTexts` month names), Pptx and Pdf.
   - `CHANGELOG.md`: new: `::: timeline`, `::: gantt`, the new `GeneratedTexts` properties (a host's
     own `GeneratedTexts` built with `with { … }` from `English` gets English month names).
9. **Tests.** Date parsing and display in both languages; past and future with a pinned
   `TimeProvider` and with `today=`; entry and row limits; grid choice; milestones; dark slides; a
   deck and a PDF of each, through the Open XML validator and the PDF/A checks; the PPTX converter's
   text output of both drawings in the round-trip samples.

## Acceptance criteria

- [x] `::: timeline` with 2–8 entries draws them in date order; with today pinned, past entries are
  grey and the next one is marked.
- [x] 1 or 9 entries, or a date such as `March 2026`, throw a `DocumentConversionException` naming
  the line and the accepted forms.
- [x] `::: gantt` draws bars, a diamond for a row without end, and a today line inside the span; the
  grid follows `grid=` or the span.
- [x] `lang: de` shows German month names and "KW" week labels.
- [ ] Both containers render in decks (editable shapes on a flow slide, also dark) and PDFs
  (vectors), and decks pass the Open XML validator and open in PowerPoint without repair; PDFs stay
  PDF/A-2b. *(Validator clean and PDF/A marker present; PowerPoint and veraPDF are open items, see
  Implementation notes.)*
- [x] Samples without the new containers produce the same decks and PDFs as before.
- [x] Guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Container or code block: `::: timeline` / `::: gantt` containers drawn by the generators
   (proposed, as specified, with Markdown inside), or fenced ```` ```gantt ```` blocks handled by an
   `ICodeBlockRenderer` in the Charts package, which would keep them opt-in?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Date display: formatted per language from `GeneratedTexts` (proposed), or shown exactly as
   written, so any text such as "Spring 2026" is allowed but past entries cannot be told?

> Answered by the human (2026-11-14): as proposed.

3. Timeline spacing: even (proposed: labels never collide), or to scale by date?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Today: from the `TimeProvider`, overridable with `today=` (proposed), or always pinned by the
   writer, so the output never depends on the day it is generated?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Gantt rows: 1–20 rows (proposed), with longer plans an error, or split onto continuation slides
   like long tables?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. Gantt columns: the first three by position, headers free (proposed: works in any language), or
   recognised headers (`Task`, `Start`, `End`) and optional extra columns such as an owner or
   progress?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Week labels: ISO week numbers ("W12" / "KW 12", proposed), or the date of each week's Monday?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

None. `::: timeline` and `::: gantt` were an "Unknown container" error before, so no document that
worked changes; the error's list of valid containers now ends in `timeline, gantt`. `GeneratedTexts`
gains five init properties with English defaults (a host's `with { … }` copy of `English` keeps working
and gets English month names). Output without the new containers is unchanged (see Verification).

### Decisions

- **Shared ground for drawn containers** (`VisualContainers.cs`, internal, DocumentGeneration):
  `VisualContainers.Names` (`timeline`, `gantt`) is appended to `LayoutSyntax.Containers` (now a
  `static readonly` string built from the lists); `LayoutSyntax.CheckContainer` hands those names to
  `VisualContainers.Check`, which parses the `key=value` arguments each name takes (`Arguments(name)`:
  `today=YYYY-MM-DD` on both, `grid=week|month|quarter` on `gantt`). `Draw(CustomContainer,
  VisualContext)` applies `today=` and dispatches to `TimelineDrawing` / `GanttDrawing`.
  `VisualContext(Brand, Today, Texts, LineOf, Format)` carries what a drawing needs. `DrawingTexts`
  turns inlines into `DrawingSpan`s (bold kept, other emphasis and links plain, code as text), trims,
  splits at the first `:` outside bold text and breaks spans into lines by an estimated width (0.52 em
  a character, 0.57 bold) with "…" past the last line. E04-S08 adds its names to `Names`, `Arguments`
  and the switch in `Draw`, and reuses `DrawingTexts` and `VisualContext`.
- **Parsers:** decks in `DeckParser.Container` add a `DrawingBlock` (`DrawingPlacement.Default`: fitted
  to the width, enlarged at most 1.5×), so the section is a flow slide as with any container, and
  builds, fitting and columns treat it like a diagram. PDFs add the `DrawingBlock` in
  `MarkdownParser.Add` instead of showing the content plainly. Today is `TimeProvider.GetUtcNow()`'s
  date.
- **Dark slides:** the deck parser tracks `onDark` (a `{.dark}` heading, and inside `::: panel dark`)
  and then draws with `ResolvedBrand.DarkSlideBrand(brand)` (white text, `D1D5DB` lines, highlight as
  accent), unless the brand already is a dark one (dark templates pass it in already).
- **Dates** (`SchedulePeriod`): a regex for the four forms, validated ranges (month 1–12, the day in
  its month, year ≥ 1). Display through `GeneratedTexts.DayFormat` (`{0} {1} {2}` / `{0}. {1} {2}`),
  `MonthFormat` (`{0} {1}`), `QuarterFormat` (`Q{0}`) plus the year, or the year alone; invariant
  `string.Format`. German short months: `Jan.`, `Feb.`, `März`, `Apr.`, `Mai`, `Juni`, `Juli`, `Aug.`,
  `Sept.`, `Okt.`, `Nov.`, `Dez.` (Duden abbreviations), so a day reads "15. März 2026", "5. Jan. 2026".
  `WeekFormat` `W{0}` / `KW {0}` with `ISOWeek.GetWeekOfYear`. The two static month lists sit before
  `English` and `German` in the class: static initialisers run in order, and `English = new()` would
  otherwise read `null`.
- **Timeline drawing:** a 3 px line in diagram `Line` across the width; one slot of `960 / n` per
  entry, the dot (r 8) in its middle. Past (ended before today): dot, date, title and description in
  `Line`; the first entry not ended: a ring (r 14, 2.5 px, `Accent`), accent dot and date; later ones:
  accent dot, text in `Text`. Dates 15 px bold, titles 17 px bold (up to two lines), descriptions
  14 px (up to three), 20 px line pitch, every line with `MaxWidth` = its room (slot − 16, two slots
  when alternating). More than five entries: odd entries have their labels above and the date below.
- **Gantt drawing:** a name column of 240 px (names at 15 px, `MaxWidth` 226), the chart from 240 to
  950 px; axis 50 px (the year at the first unit and at each new year, the ISO year on week grids;
  unit labels 13 px; a rule under them), rows of 28 px, every second one banded in diagram `Surface`,
  a 0.5 px line in `Line` at each unit's start, bars 16 px with 3 px corners from the start's first to
  the end's last day (at least 2 px wide), diamonds of 9 px half-diagonal in the middle of the start
  day, the today line 2 px in `Highlight` from just above the rows to the bottom. Colours:
  `Palette[row % count]`, `Line` when the task ended before today. Grid choice: span ≤ 70 days →
  week, ≤ 18 calendar months → month, else quarter. More than 26 units show every k-th label.
- **Guides:** the same short paragraph and example in both (under *Layouts* in the PPTX guide, after
  *Syntax written for slides* in the PDF guide); details in the DocumentGeneration docs.
- **Sample:** a new `schedules.md` (PPTX tests' `Samples`, linked into the PDF tests) with both
  containers, a dark slide, eight entries and a week grid, `today=` on every container; its round
  trips through `PptxConverter` and `PdfConverter` are committed for both built-in brands. The
  existing samples are untouched (they are the unchanged-output baseline).

### Deviations from the plan

- **Timeline height:** 960 px wide and as tall as its labels need (116 px for one-line titles, about
  300 px for eight entries with descriptions) instead of a fixed 960 × 300: with five or fewer
  entries nothing is drawn above the dates, and a fixed height left a third of the drawing empty.
- **Quarters in a timeline are shown as "Q2 2026"** (`QuarterFormat` and the year), not as the
  written `2026-Q2`; a year stays "2026". The plan said "quarter and year as written"; "Q2 2026" reads
  better and is the same text as on the Gantt axis.
- **Code inside entries:** `DrawingSpan` has no mono flag, so a code span in mixed text is plain
  text; only a Gantt task that is one code span is set in the mono font (`DrawingText.Mono`). A
  per-span flag would have changed the public drawing model and every writer.
- **The PPTX guide's word limit was not raised**: the sample guide was at 2,589 of 2,600 words, so to
  fit the containers' paragraph and example, other PPTX guide prose was shortened, keeping the
  instructions: speaker notes (dropped "and on the notes pages … never changes the slide's layout" and
  "Several of them in one section are joined in order"), transitions (dropped "each in PowerPoint's
  default direction and speed"), footnotes (dropped "counted from 1 on each slide"), image attributes
  ("`.caption` and `scale=1` work as in the title"), QR codes (the `"caption"` sentence, covered by
  the image bullet), builds ("a slide that continues on another starts each from its first click").
  The PPTX sample guide is just under 2,600 words again. **For the human:** E04-S08 will add to the
  PPTX guide and has no room left: raise the limit, or say what else to cut.
- `MarkdownFoundationTests.GermanTexts_DifferFromEnglishInEveryText` now exempts `MonthFormat` and
  `QuarterFormat` (the same in German by design) and checks that the month names differ instead.
- Inline icons (`:icon-x:`), `[words]{.success}` and footnote references are not interpreted inside
  entries and cells: icons and coloured words stay as typed, footnote references are dropped.

### Verification

- **Tests:** `./run.sh build` 0 errors (the 5 known `CS0618` warnings in `Pdf.Tests`); `./run.sh test`
  all green: DocumentConversions 123, WindowsOnly 7 (6 skipped), DocumentGeneration 179, Charts 78,
  Diagrams 71, Svg 22, Pptx 289, Pdf 180 — 949, 943 run. New: `ScheduleDrawingTests`
  (DocumentGeneration: periods and their display in both languages, order, past/next with a pinned
  today and with `today=`, alternation, limits and messages with lines, arguments, bars, milestones,
  the today line, grid choice, W/KW week labels, German months, columns by position), `ScheduleTests`
  in Pptx (round trip, flow slide of shapes, the clock, dark slide text, German, errors) and in Pdf
  (PDF/A marker and round trip, a drawing instead of the content, the clock, German, errors). No test
  reads the real date: every one pins `TimeProvider` or uses `today=`.
- **Unchanged output:** every deck and PDF the suite writes to `.tmp/review/`, before this change
  (`origin/main` at `0534050`) and after it: decks part by part identical with the random relationship
  ids masked (also inside embedded chart workbooks), PDFs identical pixel for pixel (pypdfium2) — 29
  files, among them `deck`, `layouts`, `presenting`, `diagrams`, `financial-charts`,
  `image-attributes`, `qr-and-icons`, `report` and `long`.
- **Open XML validator:** clean for every deck the new tests write (`Samples.AssertValid`).
- **Rendered PDF:** page 2 of `.tmp/review/schedules-{neutral,sample}.pdf` looked at (pypdfium2):
  labels placed without overlap, the alternating eight-entry timeline, bars, diamonds, banding, today
  lines and week labels as intended.

### To verify by hand (no PowerPoint, no veraPDF here)

1. **PowerPoint** — open `.tmp/review/schedules-sample.pptx` and `schedules-neutral.pptx` (written by
   `ScheduleTests.Sample_RoundTripsThroughThePptxConverter`): no repair prompt; slide 2 a four-entry
   timeline (two grey entries, *Go-live* ringed), slide 3 a Gantt under the kicker *Plan* (grey
   *Analysis*, a today line in March, a diamond for *Go-live*), slide 4 on the dark layout with white
   text and light lines, eight entries alternating above and below the line, slide 5 a week grid
   W10–W13. Check that dates, titles and descriptions sit centred on their dots without colliding in
   PowerPoint's real font, and that the shapes can be selected and edited.
2. **veraPDF** — `.tmp/review/schedules-neutral.pdf` and `schedules-sample.pdf` validate as PDF/A-2b.
