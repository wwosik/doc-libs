# E02-S05 Slide kinds

> Status: in verification
> Epic: [E02 Deck layouts](../50-todo/E02-deck-layouts.md)

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
  - [E01-S06](E01-S06-template-roles-for-decks.md) gives the layout roles (agenda,
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
   layout role's name), or `{.interim}`? `.statement` on a heading means the whole
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

## Implementation notes

### Breaking changes

- **Two columns of text on a declared two-content layout** (question 3, accepted): a `##` section
  whose only block is `::: columns` with two text columns, without heading attributes, now fills
  the first two text placeholders of the `TwoContent` layout when a brand pack or
  `CustomTemplateOptions.Roles` declares that role, instead of being a flow slide. The built-in
  master and templates without the declaration are unchanged (verified below). In `CHANGELOG.md`.
- **The new classes and containers are syntax.** The plan's changelog line ("Markdown that generated
  before can fail") does not hold: `{.agenda}` and the other classes, `::: conclusion` and
  `::: comment` threw "Unknown heading attribute" / "Unknown container" before. Such Markdown now
  generates, its misuse throws its own message, and the lists of valid attributes and containers in
  the old messages name the new ones. `CHANGELOG.md` says this instead.
- Nothing else: every deck the suite and the comparison below write without the new syntax is
  unchanged.

### Decisions and deviations

- **Model and writing.** `KindSlide` (abstract, with `Kicker`) and its records `AgendaSlide`
  (`FromChapters`), `QuoteSlide`, `StatementSlide`, `ContactSlide` (`Person(Name, Lines, Photo)` with
  the list item's build mark) and `ChartSlide` (`ChartBlock`s and text), as planned. They are written
  by `WriteKindSlide` (`DeckWriter.Kinds.cs`) with E01-S07's helpers (`PlaceholderFit`,
  `PicturePlaceholder`, `Chart` with a placeholder, `PlaceholderFill.Distribute` for charts) rather
  than by turning them into a `PlaceholderSlide`: person *i* has to go into body *i* and picture *i*
  even when someone before has no photo, which first-free placement cannot do. Two text columns do
  become a `PlaceholderSlide`. Resolution happens in `Fit` (`FitKind`), per part, so a part whose
  layout is missing becomes the fallback flow slide and is fitted as one.
- **Shared checks.** `SlideKinds` (shared package) checks kinds and both containers on Markdig blocks
  for both generators, so PDFs throw the same messages. Besides the planned errors it rejects: an
  agenda with more than one list, a quote with other content than the quote and its byline, a
  statement with more than paragraphs and lists, a chart slide without a `chart` block, two kinds on
  one heading and a kind with `.dark` (both in `HeadingAttributes.Split`), and a conclusion inside
  another container (the plan said this only of comments).
- **Conclusion and comment hold text** (paragraphs and lists) — a simplification: a band or callout
  with a table or image in it has no sensible look. The conclusion's rule is 2 pt; the comment is
  outlined in the highlight colour, its text in the shape itself (14 pt, scaled with the slide), its
  pointer towards the block's centre but at most half its width plus its height from its centre.
  A comment's block becomes a block of its own (`WalkMarked` breaks the paragraph group before it),
  so it does not comment the paragraphs above. The comment builds with its own marker, else with
  its block.
- **Agenda.** Top-level `-` items are numbered; an ordered list keeps its start ("numbered unless
  the list says otherwise"); deeper items as written. Chapters are every section divider of the deck,
  before or after the agenda, but the closing slide; numbered chapters read "01  Market" with the
  number bold, else they are numbered items. A split agenda numbers on through `startAt`. PDFs list
  the `#` headings but the cover and a `{.closing}` one, wherever the agenda is — the same set as the
  deck; the plan said "that follow".
- **Quote.** The byline is the paragraph after the quote or the quote's last line, after a soft or
  hard break (soft breaks are kept as a sentinel run only there and in contact items). Without a
  byline placeholder it is the quote's last paragraph. Its heading is written only where the layout
  has a title placeholder.
- **Contact.** The name is the bold start of the item's first line (a separator after it dropped),
  else the first line; the other lines go one level down, where the built-in layout has its smaller
  style. A person without a photo leaves picture *i* unwritten. People per slide: five, or fewer when
  the layout has fewer bodies. The fallback is one `CardsBlock` (four a row) with the photo above each
  card's title (new `Card.Photo`); it fits like any cards block.
- **Charts.** One chart takes the Chart role, two the TwoCharts role (built-in: *Two Content*, whose
  `obj` bodies take the charts fitted as on flow slides, each with its own source line, since that
  layout has no source placeholder). Two charts on a layout with fewer chart or `obj` placeholders
  are split into two slides. The text goes into a text placeholder left over, else — as on the
  built-in *Chart* layout, which has none — into a text box at the top of the charts' boxes, which
  move down by its height. A chart slide whose `chart` blocks no renderer drew is the flow slide its
  blocks make, without a note.
- **Fallback.** `layout-fallback` once per slide and missing layout, with the plan's wording
  ("asks for a quote layout"; "a two-chart layout" for two charts). The statement's fallback writes
  no title at all (`FlowSlide.NoTitle`), rather than an empty title placeholder whose prompt would
  show in PowerPoint; the quote's fallback likewise when its heading is empty.
- **`brand-dark` has one kind layout:** its *Chart* layout is found by its OOXML type (E01-S06), so
  chart slides are not a fallback there; its chart title, unit and source placeholders are undeclared
  and therefore bodies, so a chart's title, unit and source stay in the chart. The plan assumed no
  kind layouts on `brand-dark`; the tests use it for the other four kinds and for two charts.
- **Kickers** on kind layouts are the flow slide's text box above the title area, in the accent
  colour. **Text levels** (E01-S06) are not applied on kind slides: their placeholders have styles of
  their own.
- **Two columns** only without heading attributes (so the sample's `{kicker="Columns"}` slide stays a
  flow slide on `brand-roles` too), on a template that is not the built-in master, with at least two
  text placeholders on the layout; the `::: columns 1:2` ratio gives way to the layout's boxes, and a
  column builds as an item when the container is marked.
- **PDF.** A kind's heading without text (`## {.quote}`) is not written; `::: conclusion` is a quote
  of its content, `::: comment` an italic quote; `agenda-empty` is given in PDFs too.
- **Sample.** `layouts.md` gains six slides using every kind (the agenda with a list: the sample has
  no chapters; the empty agenda is tested on its own) and both containers. Its `chart` block needs a
  renderer, so the PPTX round-trip and validity tests of `layouts.md` now pass `ChartRenderer`, which
  touches nothing else in the sample. `Pdf.Tests` has no chart renderer: the PDF shows the block as
  code. The PDF round trip, now four pages, no longer shows the page footer lines, which the
  converter drops as running text across them.
- **Guides.** Deck guide (words as the test counts them, letters and digits): sample about 3,130,
  custom about 3,160, both under the 3,500 limit; PDF guide about 1,960. `{{templateLayouts}}` says
  that the built-in design has a layout for every kind, lists a brand template's and a caller's
  declared kind layouts, and names the two-content layout two text columns go to.
- **Housekeeping:** the `CS8602` in E04-S08's `StepsAndRisksTests` is fixed (`Presentation!`).

### Verification

- `./run.sh build`: 0 warnings, 0 errors (also with `--no-incremental`). `./run.sh test` all green:
  1 + 123 + 251 + 78 + 71 + 22 + 207 + 371 passed (1,124), 6 Windows-only skipped.
- **New tests.** Pptx `SlideKindTests` (34): the parser's model of each kind, four byline forms, the
  agenda from chapters with and without numbers, `agenda-empty`, sixteen mistakes with their
  messages, each kind on the built-in master's layouts with every placeholder checked (both brands),
  the fallbacks on `brand-dark` with `layout-fallback`, the right-aligned byline, photo cards, two
  charts side by side and only white or highlight text outside placeholders, chart title, unit,
  source and byline on `brand-roles` with declared placeholders, two charts on the built-in
  *Two Content* keeping their titles and sources, six people and twenty agenda items
  continuing (numbering on), the conclusion at the foot and on the last part of a split slide, the
  comment overlapping its block's top right inside the area and moving with it, two text columns on
  `brand-roles` and unchanged on the built-in master and `brand-dark`, a kind with `layout` and
  `kicker`, a chart slide without renderer. Pdf `SlideKindTests` (10): the agenda's chapters without
  cover and closing, `agenda-empty`, the quote, comment and conclusion blocks, seven mistakes. The
  `layouts.md` round trips (PPTX neutral, sample, `brand-dark`; PDF neutral) and the four expected
  guides are regenerated.
- **Open XML validator** (`Microsoft365`): every new deck passes (`Samples.AssertValid`), the
  `layouts.md` decks included.
- **Unchanged output.** (1) Every file the suite writes to `.tmp/review/`, before (`origin/main` at
  `8690b1f`, from `git archive`) and after: decks part by part identical with the SDK's random
  relationship ids masked (embedded workbooks included), PDFs identical pixel for pixel (pypdfium2),
  49 files, but the `layouts` deck and PDF; in the `layouts` decks every part of the first nine slides
  is identical, only `presentation.xml`, its relationships and `[Content_Types].xml` change with the
  new slides. (2) All Pptx sample Markdown files of `8690b1f` (`deck`, `layouts`, `presenting`,
  `presenting-lists`, `schedules`, `steps-and-risks`) plus `diagrams.md` and a deck with a plain
  `---`, two text columns and a quote that is no kind, on five templates (neutral, sample,
  `brand-dark`, `brand-roles`, a caller's copy of the sample master) with Mermaid and chart
  renderers: 40 decks, all identical but the two text columns on `brand-roles` (the breaking change).

### To verify by hand (no PowerPoint on this machine)

The tests write the decks to `.tmp/review/`.

1. **No repair prompt** opening `kinds-neutral.pptx`, `kinds-sample.pptx`, `kinds-brand-dark.pptx`,
   `kinds-brand-roles.pptx`, `kinds-two-columns.pptx`, `kinds-continued.pptx`, `conclusion.pptx`,
   `comment.pptx` and `layouts-{neutral,sample,brand-dark}.pptx`.
2. **Kinds on the built-in master** (`kinds-sample.pptx`): slide 2 a numbered agenda (1., 2.) in the
   *Agenda* box; slide 3 the quote large and italic with "Ann Example, Chief Engineer" in the byline
   box and no title; slide 4 the statement white and centred on the gradient, its text below;
   slide 5 Ann's portrait photo cropped square into the first picture box (*Picture Format → Crop*
   editable), her bold name and two lines under it, Ben without photo and no empty picture prompt;
   slide 6 the chart in the chart box below the text "Up by a third.", "Revenue", "EUR m" and
   "Source: Annual report" in the title, unit and source boxes and not repeated in the chart,
   *Edit Data* works, *Reset slide* keeps chart and texts in their boxes.
3. **Callout** (`comment.pptx`, slide 2): a rounded speech-bubble callout at the top right of
   "Second paragraph.", overlapping its top, its pointer towards the paragraph, text readable on the
   pale highlight fill; the yellow handle moves the pointer.
4. **Conclusion** (`conclusion.pptx`): a band across the foot of the content area, a coloured rule on
   top, bold text, above the footer; nothing overlaps it.
5. **Fallbacks on a dark template** (`kinds-brand-dark.pptx`): every slide readable (white or amber
   text on the dark gradient); the quote as a large italic statement with the byline right-aligned;
   the statement without a title; two people as cards, Ann's photo on top of her card; two charts side
   by side on the last slide.
6. **Continuations** (`kinds-continued.pptx`): five people on the first contact slide, the sixth on
   the second; the agenda continues on further slides numbering on (e.g. 12., 13., …), not from 1.
7. **Two columns** (`kinds-two-columns.pptx`): "Before" in the right column and "After" in the left,
   as `brand-roles` declares its bodies in that order; no placeholder prompts.
8. **Kicker on the statement layout:** a `{.statement kicker="…"}` slide on the built-in master (not
   among the review files; e.g. add `kicker="Point"` to slide 4 of the sample) — check the accent
   colour reads on the gradient. If not, the kicker should be white there.
9. **LibreOffice Impress:** the chart frames of `kinds-neutral.pptx` keep their boxes.

### Loose ends

- The acceptance "opens in PowerPoint without repair" depends on item 1.
- `{.agenda .repeat}` (question 7) is not implemented, as decided.
- Two charts on a layout with two chart placeholders **and** a source placeholder write both sources
  joined by `; ` there; no test template has such a layout, so that path has no test of its own.
- On `brand-dark`'s found-by-type *Chart* layout, chart texts stay in the chart; a brand that wants
  them in its boxes declares the chart title, unit and source placeholders.
