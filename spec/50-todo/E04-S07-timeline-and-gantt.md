# E04-S07 Timeline and Gantt

> Status: todo
> Epic: [E04 Diagrams and charts](E04-diagrams-and-charts.md)

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

- [ ] `::: timeline` with 2–8 entries draws them in date order; with today pinned, past entries are
  grey and the next one is marked.
- [ ] 1 or 9 entries, or a date such as `March 2026`, throw a `DocumentConversionException` naming
  the line and the accepted forms.
- [ ] `::: gantt` draws bars, a diamond for a row without end, and a today line inside the span; the
  grid follows `grid=` or the span.
- [ ] `lang: de` shows German month names and "KW" week labels.
- [ ] Both containers render in decks (editable shapes on a flow slide, also dark) and PDFs
  (vectors), and decks pass the Open XML validator and open in PowerPoint without repair; PDFs stay
  PDF/A-2b.
- [ ] Samples without the new containers produce the same decks and PDFs as before.
- [ ] Guides, docs and CHANGELOG are updated, and all tests pass.

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
