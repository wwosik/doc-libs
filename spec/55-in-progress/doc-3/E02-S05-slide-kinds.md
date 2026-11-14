# E02-S05 Slide kinds

> Status: in progress (doc-3)
> Epic: [E02 Deck layouts](../../50-todo/E02-deck-layouts.md)

## Summary

A `##` slide can say what kind of slide it is, and the deck puts it on the template's layout for
that kind:

- `{.agenda}`, from a list or from the chapter headings;
- `{.quote}`, with a byline;
- `{.statement}`, one message;
- `{.contact}`, one to five people;
- `{.chart}`, one or two charts with their title, unit and source in the layout's placeholders.

Two new containers go on any flow slide: `::: conclusion`, a box at the foot of the content area,
and `::: comment`, a callout over the block before it. Two columns of text only go into the
template's two-content layout. A template without the layout for a kind gets a flow slide with
the same content, and a note.

## Context

- **Heading attributes.** `HeadingAttributes` (shared package) knows `.dark`, `kicker`, `layout`
  and `.closing`, and throws on anything else, listing `HeadingAttributes.Valid`. The PDF parser
  strips them (`MarkdownParser.WithoutAttributes`).
- **Containers.**
  - `LayoutSyntax.CheckContainer` knows `cards`, `rows`, `panel`, `statement` and `columns`, for
    both generators. The PDF guide test checks that every name in `LayoutSyntax.Containers`
    appears in the PDF guide.
  - `DeckParser.Container` turns them into `FlowBlock`s. `::: statement` is a `StatementBlock`:
    its first paragraph large and bold, on any flow slide.
  - The PDF shows a container's content as normal blocks (`MarkdownParser`, `case
    CustomContainer`).
- **Parsing.**
  - `DeckParser.Slides` decides per `##` section. A section using layouts (`UsesLayouts`:
    containers, alerts, definition lists, tag paragraphs) becomes a `FlowSlide`. Everything else
    becomes a `ContentSlide` or `VisualSlide`, unchanged since E02-S01.
  - `#` dividers are `TitleSlide { IsSection: true }`. With
    [E01-S07](E01-S07-chapter-numbers-and-layouts-across-placeholders.md) they carry a
    `Number` when chapters are numbered.
- **Writing.**
  - `DeckWriter.WriteFlowSlide` stacks blocks (`Stack`, `Place`) in `ContentArea`. Images,
    drawings and charts share the height left over.
  - A section with two `chart` blocks gives a continuation slide when plain (`SectionBuilder`),
    or two charts stacked when it is a flow slide.
  - Charts are written by `ChartWriter`, with `ChartSpec.Title` and `ChartSpec.Unit` inside the
    chart part. `source:` arrives with E04-S05.
- **Fitting.** `Fit` (`DeckWriter.Fitting.cs`) shrinks flow slides by `FlowStyle.Scale`, then
  splits them between units. A card row, a panel or a `columns` container moves as a whole.
- **Palette.** `SlidePalette` gives `Surface`, `Primary`, `Accent`, `Highlight` and their dark
  variants (E01-S04). Everything drawn must stay readable on dark templates.
- **What this story builds on.**
  - [E01-S06](../../60-verify/E01-S06-template-roles-for-decks.md) gives the layout roles (agenda,
    quote, statement, contact, chart, two charts, two content) and the placeholder roles (body
    order, subtitle, byline, picture, chart title, unit and source). It also gives text levels
    and the built-in master's layouts for each role.
  - [E01-S07](E01-S07-chapter-numbers-and-layouts-across-placeholders.md) gives
    `PlaceholderSlide`, which writes text, pictures (cropped) and charts into a layout's
    placeholders, and the chapter numbers.
- **Decided** in the epic: each slide kind needs its layout role from E01-S06, and existing decks
  do not move.

## Proposed approach

1. **Syntax.**

   | Markdown | Slide |
   | -------- | ----- |
   | `## Agenda {.agenda}` with a list | The list on the agenda layout |
   | `## Agenda {.agenda}` with nothing under it | The deck's chapters (`#` dividers after the cover, not the closing slide), with their numbers when chapters are numbered |
   | `## {.quote}` with `> quote` and a line `— Name, role` | The quote in the body, the line in the byline placeholder |
   | `## One message {.statement}` and optional text | The heading as the statement, the text below it |
   | `## Contact {.contact}` with a list of 1–5 people | Per person: a photo (an image in the item), the bold name, the rest of the item |
   | `## Revenue {.chart}` with one or two `chart` blocks and optional text | The charts in the layout's chart placeholders; title, unit and source in theirs |
   | `::: conclusion` … `:::` (last block of a flow slide) | A box across the foot of the content area |
   | `::: comment` … `:::` (right after a block) | A callout over the top right of that block |
   | A `##` whose only block is `::: columns` with two columns of text | Each column in a body of the two-content layout |

   - **Attributes.** The kind classes exclude each other and `.dark`. They combine with `kicker`,
     and with `layout="…"`, which picks the layout instead of the role. On `#` headings they
     throw.
   - **Containers.** `LayoutSyntax.Containers` gains `conclusion` and `comment`, which take no
     arguments.
   - **Byline.** A paragraph right after the quote, or the quote's last line, starting with `—`,
     `–` or `--`. The dash is dropped.
   - **Errors** throw `DocumentConversionException`, naming what is allowed:
     - a `{.quote}` without a quote;
     - a `{.contact}` without a list;
     - a `{.chart}` with something other than `chart` blocks and text;
     - a comment as the first block;
     - a conclusion that is not the last block, or two conclusions.

2. **Model.** `DeckParser` makes `KindSlide(Title, Kind, …)` records: `AgendaSlide`,
   `QuoteSlide`, `StatementSlide`, `ContactSlide` (with `Person(Name, Lines, Photo)`) and
   `ChartSlide`. It also makes the flow blocks `ConclusionBlock(Blocks)` and
   `CommentBlock(Blocks)`.
   - An empty agenda is filled after the whole document is parsed, from the `TitleSlide`s that
     follow and precede it.
   - An agenda with neither a list nor chapters gets a new note, `agenda-empty`, and keeps its
     title only.

3. **Layout or fallback.** Before `Fit`, the writer resolves each `KindSlide`:
   - **With a role.** If the template has the layout role, the slide becomes a `PlaceholderSlide`
     (E01-S07) on that layout:

     | Kind | Text placeholders, in order | Visual placeholders | Other roles |
     | ---- | --------------------------- | ------------------- | ----------- |
     | Agenda | the list (numbered unless the list says otherwise) | — | — |
     | Quote | the quote | — | `Byline` |
     | Statement | title placeholder: the heading; body: the text | — | — |
     | Contact | person *i*: name in bold, then their lines | person *i*'s photo, cropped | — |
     | Chart | the text | the chart(s) | `ChartTitle`, `ChartUnit`, `ChartSource` |

     - **Titles.** A quote's heading goes into a title placeholder if the layout has one, else it
       is not shown (question 6).
     - **Charts.** When the layout has a chart title or unit placeholder, that text is written
       there and left out of the chart part. With two charts, titles and units stay in the
       charts, and the source placeholder takes both sources joined by `; `.
   - **Without one.** The slide becomes a `FlowSlide` with the same content, and a new note,
     `layout-fallback`: "The slide 'Quotes' asks for a quote layout, which the template does not
     have; it is drawn as a flow slide."
     - Agenda: a numbered `TextBlock`.
     - Quote: a `StatementBlock` in italics, with the byline right-aligned below it.
     - Statement: a `StatementBlock` of the heading, with the title placeholder left empty.
     - Contact: a `CardsBlock`, one card per person, with the photo on top.
     - Chart: one `ChartBlock`, or two charts side by side in a `ColumnsBlock`.

     The built-in master has every role (E01-S06), so the fallback appears only on brand and
     caller templates that lack the layout.

4. **Too much for one slide.**
   - Agendas split between items onto further slides on the same layout. Contacts go five per
     slide, and charts two per slide.
   - Text in a placeholder shrinks with `normAutofit` to 12 pt, then gives `slide-overfull`.
   - Fallback flow slides fit as today.

5. **`::: conclusion`.** It makes the section a flow slide.
   - **Look.** A band across the full content width, at the foot of the content area:
     - a `Surface` fill with a rule in `Primary` on top, or on dark slides the dark surface and
       the `Highlight` rule;
     - its text bold, in the heading colour, at the flow text size.
   - **Layout.** `Stack` reserves its height first and places the other blocks in the area above
     it.
   - **Fitting.** On a split slide it is on the last part only, and the other parts get the whole
     area.

6. **`::: comment`.** It belongs to the block before it, and both move together when fitting.
   - **Shape.** A `wedgeRoundRectCallout` in `Highlight` at 15 % (dark: the highlight veil), its
     text at the small flow size.
   - **Size and place.** A third of the content width wide, at least 2.5″, as high as its text.
     It sits at the top right of the block before it, raised by a third of its own height, with
     the pointer towards the block's centre. It is kept inside the content area and takes no
     height of its own in the stack.
   - Only at the top level of a section. Inside cards, panels or columns it throws.

7. **Two columns of text.** A section whose only block is `::: columns` with two columns of text
   (paragraphs, lists, `###`) becomes a `PlaceholderSlide` on the two-content layout: the left
   column in body 1, the right in body 2. This happens only when the brand pack or the caller's
   options declare the `TwoContent` role (question 3). Otherwise, and on the built-in master, the
   flow slide stays as today.

8. **PDF.** The same Markdown generates, and the same mistakes throw.
   - Kind classes are stripped like the other attributes. Quote, byline, people and charts show as
     the Markdown says.
   - An empty `{.agenda}` shows its heading and the list of `#` headings that follow (question 10).
   - `::: conclusion` is shown like an alert without a title.
   - `::: comment` comes after the block it comments, as an italic quote.

9. **Guides, docs, changelog.**
   - **Deck guide.** The *Layouts* table gains the rows above, with a short example of each
     kind. `{{templateLayouts}}` (E01-S06) says which kinds have their own layout in the
     template, and that the others are drawn as flow slides. *Notes you may get back* gains
     `layout-fallback` and `agenda-empty`.
   - **PDF guide.** It gets the two containers and the kinds as they show in PDFs.
   - The expected guides are regenerated.
   - **Docs.** `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md` (*Layouts*, *Fitting*) and
     `docs/wwosik.DocLibs.DocumentGeneration.Pdf.md` describe the kinds, their roles and
     fallbacks. The notes table lists the two codes.
   - **`CHANGELOG.md`.**
     - **Breaking:** Markdown that generated before can fail. `{.agenda}`, `{.quote}`,
       `{.statement}`, `{.contact}`, `{.chart}` and `::: conclusion` / `::: comment` are syntax
       now, and their misuse throws.
     - **New:** the kinds and containers.

10. **Tests.**
    - Parser: each kind and container, the byline forms, and each error with its message.
    - Agenda from a list, and from chapters with and without numbers. An empty agenda gives
      `agenda-empty`.
    - Writer, on the built-in master: each kind on its layout, with its placeholders filled as in
      the table.
    - Writer, on `brand-dark`, which has no kind layouts: each kind's fallback, with
      `layout-fallback` and readable dark colours (the E01-S04 contrast check).
    - Writer, on the `brand-roles` test template: declared roles and chart title, unit and source
      placeholders.
    - Contact with six people continues on a second slide, and an agenda of 20 items splits.
    - Conclusion at the foot, with a split slide keeping it on the last part. A comment
      overlapping its block, inside the content area, moved with it.
    - Two text columns on a template declaring `TwoContent`, and unchanged without.
    - A new sample section in `layouts.md` using every kind and container. Its round trips are
      regenerated for neutral, sample and `brand-dark`, and every other expectation stays
      unchanged.
    - PDF: the same sample generates, and the degradations hold.
    - `Samples.AssertValid` on every deck. **In PowerPoint:** the callout shape, chart
      placeholders and cropped photos look right and open without repair.

## Acceptance criteria

- [ ] Each kind lands on its layout role with its content in the placeholders described, on the
  built-in master and on a template that declares the roles.
- [ ] A template without the role gets a flow slide with the same content and a `layout-fallback`
  note.
- [ ] An empty `{.agenda}` lists the deck's chapters, numbered when chapters are numbered.
- [ ] `::: conclusion` sits at the foot of the content area. `::: comment` overlaps the top right
  of the block before it. Both stay readable on dark templates.
- [ ] Two text columns go into a declared two-content layout, and nowhere else.
- [ ] Too many items, people or charts continue on further slides, and nothing runs off a slide.
- [ ] PDFs accept the same Markdown and degrade as described.
- [ ] Decks without the new syntax are unchanged, the guides, docs and CHANGELOG are updated, and
  all tests pass.

## Open questions

1. The statement slide's class: `{.statement}` (proposed: it matches `::: statement` and the
   layout role's name), or `{.interim}` as in the fork? `.statement` on a heading means the whole
   slide; `::: statement` stays a block on a flow slide.

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Chart slides only from `{.chart}` (proposed), or automatically for every section of nothing but
   one or two charts? Automatic would move existing decks whose sections hold only charts onto the
   chart layout.

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Two text columns in the two-content layout only when the brand pack or options declare the role
   (proposed: no existing deck moves)? Or whenever the template has the role, the built-in master
   and layouts found by type included? That moves every existing text-only `::: columns` slide,
   which would be a breaking change.

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. A kind the template has no layout for: a flow slide with a `layout-fallback` note (proposed),
   silently, or an error? The writer cannot change the template, but the note tells them the
   slide looks different from the design's own.

> Answered by the human (2026-11-14): as proposed.

5. More than five people on a `{.contact}` slide: continue on a further slide (proposed, as
   fitting does), or throw because a contact slide holds one to five?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. A quote's `##` text: the title where the layout has a title placeholder, else not shown
   (proposed), always shown (in a text box), or used as the byline when there is no `—` line?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. The agenda: should it repeat before each chapter with the current chapter highlighted? Proposed:
   not now. It needs a highlight style per template and multiplies slides; a later story can add
   `{.agenda .repeat}`.

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. `::: conclusion`: only as the last block (proposed: Markdown order matches the slide), or
   anywhere, always drawn at the foot?

> Accepted as proposed (the human, 2026-11-14, without discussion).

9. `::: comment`: always at the top right of the block before it (proposed), or with a position
   argument (`::: comment left`, `below`)?

> Accepted as proposed (the human, 2026-11-14, without discussion).

10. An empty `{.agenda}` in PDFs: list the chapters (proposed), or leave the heading out, since
    E03-S02's table of contents does the same job?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
