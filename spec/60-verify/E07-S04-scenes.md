# E07-S04 Scenes

> Status: in verification
> Epic: [E07 Presenting](../50-todo/E07-presenting.md)

## Summary

`::: scene` draws a slide from named shapes placed on a grid of 160 × 90 units over the whole
slide: boxes, rounded boxes, ellipses, texts, and arrows and lines between them. Shapes can appear
or disappear step by step on clicks, using the builds of E07-S05. Consecutive scene slides with
`transition="morph"` move their shapes smoothly into place, because the shapes carry the same
names. PDFs show each scene as one still drawing.

## Context

- **Depends on E07-S01 and E07-S05.** E07-S05 brings the build model (`Build`, `BuildKind`,
  `BuildEffect`) and the `TimingWriter` that writes a slide's click sequence from the shapes
  `Canvas` records per step. E07-S01 brings the `morph` transition (`p159:morph` with a fade
  fallback) on a heading's `transition="…"`, and `::: notes`. This story uses all three and adds
  no animation XML of its own.
- **Containers.**
  - `LayoutSyntax.CheckContainer` knows `cards`, `rows`, `panel`, `statement`, `columns` (and
    `notes` after E07-S01). `::: scene` throws "Unknown container" in both formats today, so
    nothing that works today changes meaning.
  - `DeckParser.UsesLayouts` makes any section with a container a `FlowSlide`.
  - `DeckParser.Container` turns each container into a `FlowBlock`.
  - `MarkdownParser.Add` (PDF) shows a container's children one after the other.
- **Placing on the slide.**
  - Flow slides place blocks in the content area (`DeckWriter.ContentArea`), below the title,
    stacked by `Stack`/`Place`. No slide kind places anything at absolute positions over the
    whole slide.
  - The slide size comes from `presentation.SlideSize` (default 12192000 × 6858000 EMU). On
    16:9, one grid unit is 76200 EMU (6 pt) both ways.
  - On the built-in master the title placeholder spans y 256032–1444752 EMU
    (`TemplateBuilder.TitleTop`, `TitleHeight`): the top 19 units of the grid. The body starts
    at 22 units.
- **Drawings.**
  - `Drawing` (`Drawing.cs`) is the shared vector model: `DrawingShape` with
    `DrawingShapeKind.Rectangle`, `RoundedRectangle`, `Ellipse` or `Path`, and `DrawingText`
    with `MaxWidth`. Coordinates are CSS pixels.
  - Both writers shrink a text to `MaxWidth` down to 70 % (`MinLabelScale` in
    `DrawingWriter` and `LayoutEngine`).
  - `DrawingWriter.Group` writes a drawing as one group named `Diagram n`, with each text as a
    shape of its own over its outline. That suits diagrams but not Morph: Morph matches shapes
    by name (a name starting with `!!` forces the match), and a label must move with its box.
- **Colours.**
  - `SlidePalette` has the flow-slide roles: `Primary`, `Accent`, `Highlight`, `Surface`,
    `Dark`, `Text`, and the label categories through `LabelFill`/`WordColour`.
  - These are brand colours on the built-in master and theme colours on caller templates
    (`SlidePalette.Scheme`), and are dark-slide aware (`ForDark`).
  - The PDF has the same brand colours in `ResolvedBrand`.
- **Fitting.** `DeckWriter.Fit` passes any slide kind it does not know through unchanged (the
  `_ => [(slide, 0, true)]` branch). A scene slide is never shrunk or split.
- **PDF.** The PDF draws a `DrawingBlock` natively as vectors, scaled to the text width like
  diagrams. Diagrams are always vectors, never images (`ai/memory.md`), so "a still image" in
  the PDF means a still `Drawing`.
- **Converter.** `PptxConverter.RenderSlide` writes the text of every shape in shape order, so a
  scene slide comes back as its labels as paragraphs.
- **Decided** (analysis and epic, 2026-11-13):
  - The grid is 160 × 90 over the whole slide, not the content area.
  - Steps are builds.
  - Morph between slides.
  - A still image in PDF.
- **Other stories, assumed here.**
  - E01-S06 (template roles) may add a blank or scene layout role. If so, scene slides use it;
    until then they use the title-only layout.
  - E07-S02 (HTML deck) and E07-S03 (previews) draw from the PPTX, so they get scenes as
    ordinary shapes. E07-S06 (presenting the HTML deck) decides how far it plays steps and Morph. The previews paint
    every shape, as PowerPoint's normal view does.
  - E04-S05 (icons) may later offer icons as scene shapes.

## Proposed approach

1. **Syntax.** Inside `::: scene`, one item per line. Blank lines are allowed, and a line starting
   with `%` is a comment. The lines are read from the container's source text (its `Span`), not
   from Markdig's inlines, so `*`, `_` and `[` in a label stay as written.

   ```markdown
   ## How a request travels {transition="morph"}

   ::: scene
   box client 8 34 30 22 "Browser"
   box api 65 34 30 22 "API" .accent step=1
   ellipse db 122 34 30 22 "Database" .muted step=2
   arrow client api "HTTPS" step=1
   arrow api db step=2
   text caption 8 66 144 8 "Every request passes the API." .small step=2
   :::
   ```

   - **Shapes:** `box`, `round` (rounded corners), `ellipse` and `text` (no fill, no line).
     Each is written `kind name x y w h ["label"] [attributes]`.
     - Numbers are grid units, decimals allowed. `x + w ≤ 160` and `y + h ≤ 90`.
     - A label is one line. `\n` inside the quotes breaks it, and `\"` is a quote.
   - **Connectors:** `arrow` and `line`, written `kind from to ["label"] [attributes]`. The
     line runs between the nearer sides of the two named shapes. The arrow head is at `to`.
   - **Names:** letters, digits and `-`, starting with a letter, unique within the scene.
     Connectors are named `from-to` unless `name=` gives another.
   - **Attributes:**
     - colour roles `.primary`, `.accent`, `.highlight`, `.muted`, `.dark`, `.outline` (line
       only), and the label categories `.success`, `.info`, `.warning`, `.danger`,
       `.neutral`;
     - text `.bold`, `.small` (12 pt), `.large` (24 pt); the default is the flow text size,
       16 pt;
     - steps `step=N`, `exit=N`, `effect=appear|fly|wipe`;
     - `name=` on connectors.
   - **Default look:** a box has the `Surface` fill, a thin `Primary` line and the text colour.
     A role fills the shape and sets its text white or dark by contrast, as cards and panels do.
   - **Errors.** An unknown kind or attribute, a duplicate or unknown name, a shape outside the
     grid, a malformed number or an unclosed quote throws `DocumentConversionException`. The
     message names the scene line and its line in the original Markdown. `LayoutSyntax` lists
     `scene` among the containers, and it takes no arguments.
2. **Shared model and parser** (`wwosik.DocLibs.DocumentGeneration`, internal).
   - `Scene(IReadOnlyList<SceneItem> Items)`, holding `SceneShape(Name, Kind, X, Y, W, H, Label,
     Style)` and `SceneConnector(Name, From, To, Arrow, Label, Style)`.
   - `SceneStyle` holds the role, the text style, `Step`, `Exit` and `Effect`.
   - `SceneSyntax.Parse(source, firstLine, format)` reads the lines. Both generators call it, so
     they reject the same mistakes.
   - `SceneDrawing.Final(scene, colours)` turns a scene into a `Drawing` of 960 × 540 px (6 px a
     unit). It holds the shapes visible after the last step, labels as `DrawingText` centred in
     their shape with `MaxWidth` of the shape's width minus padding, and connectors as paths
     with an arrow head. It is the PDF's still.
3. **Parsing decks** (`DeckParser`).
   - A section whose top-level blocks include a `::: scene` becomes a new
     `SceneSlide(Title, Scene)`. It carries `Layout`, `Line`, `Transition` and speaker notes like
     any slide.
   - It may hold only the scene and `::: notes`. Any other block, or a second scene, throws
     (question 6).
   - A `::: scene` nested in another container throws.
   - E07-S05 build markers before a scene throw too: the scene's steps say what builds.
4. **Writing decks** (new internal `SceneWriter`, called from a `WriteSceneSlide` in `DeckWriter`).
   - **Layout.** The slide goes on the title-only layout, or the heading's `layout="…"`, and
     gets its title as other slides do.
   - **Position.** A shape at `(x, y, w, h)` goes to `x × slideWidth / 160` and
     `y × slideHeight / 90`, in EMU (question 5).
   - **Shapes.** Each shape is a top-level `p:sp` named `!!<name>`, with preset geometry
     (`rect`, `roundRect`, `ellipse`, or `rect` without fill and line for `text`) and its label
     in its own text body, centred. `Tag` gives the label the language tag.
   - **Label size.** A label starts at its size and is shrunk by `TextMetrics` to fit the shape,
     down to 70 %. A label that still does not fit gives `slide-overfull`, naming the shape.
   - **Connectors.** Each is a `p:cxnSp` named `!!<name>` (`straightConnector1`, with a
     triangle tail end for `arrow`). It is glued to both shapes with `stCxn`/`endCxn` on the
     connection sites of the facing sides, so it follows them when the shapes are moved by hand
     or by Morph. A connector label is a text shape `!!<name>-label` at its midpoint.
   - **Order.** Shapes are written in the order of the lines, so later ones cover earlier ones.
     Connectors go after the shapes.
   - **Steps.** `step=N` and `exit=N` become E07-S05 build steps:
     - an entrance on click N with `effect` (default appear) and an exit on click N;
     - clicks ordered by N, so gaps in the numbers are closed up;
     - everything on the same N starting together.

     `SceneWriter` registers each shape's id with the step in `Canvas`, and `TimingWriter`
     writes the sequence.
   - **Footer.** The footer is written as on other slides.
   - **Morph.** `transition="morph"` on the next scene slide's heading makes PowerPoint morph
     the shapes with equal names: position, size, colour and label. Shapes only on one side
     fade. Nothing else is needed from this story (question 8).
5. **PDF** (`MarkdownParser`). `::: scene` becomes a `DrawingBlock(SceneDrawing.Final(…))`, in the
   brand's colours, drawn and scaled like a diagram (question 7). A scene's `step`, `exit` and
   `effect` are checked but have no effect.
6. **Converter.** No change. A scene slide comes back as the text of its labels, in shape order.
   Rebuilding `::: scene` from shapes is left out.
7. **Guides, docs, changelog.**
   - `AuthoringGuide/Pptx.md` gets a section *Scenes* in *Presenting* (from E07-S01): the grid,
     the shapes, connectors, attributes, steps and Morph, with an example that the guide tests
     generate.
   - A new placeholder `{{sceneTitleArea}}` fills in, from the template's title-only layout, how
     many grid units the title takes ("the title takes the top 19 units; place shapes below
     22").
   - `AuthoringGuide/Pdf.md` says scenes are drawn as still pictures.
   - The expected guides are regenerated.
   - `docs/…Pptx.md` gets a section *Scenes*, and *Known limits* gains:
     - shapes are not fitted to each other;
     - labels break only at `\n`;
     - the converter reads labels only.
   - `docs/…Pdf.md` gets the still picture, and `docs/…DocumentGeneration.md` the shared scene
     syntax.
   - `CHANGELOG.md` *Unreleased* lists scenes as new. There is no breaking change:
     `::: scene` was an error before.
8. **Tests.**
   - A new sample `scenes.md` (three scene slides with steps and Morph, every shape kind and
     role, notes on one) generates with no notes, validates with the Open XML validator and
     round-trips to `scenes.roundtrip.expected.md`.
   - The PDF of the same sample is PDF/A-valid.
   - Unit tests check:
     - grid-to-EMU positions on 16:9 and on a 4:3 caller template;
     - `!!` names;
     - connector glue sites by relative position;
     - the click sequence for `step`/`exit` with gaps;
     - label shrinking and `slide-overfull`;
     - every parse error with its line;
     - the PDF drawing of the final state, as a golden text dump like `mermaid-*.expected.txt`.

## Acceptance criteria

- [ ] `::: scene` places every shape at its grid position over the whole slide, on 16:9 and on
  other slide sizes. The title is written as on other slides.
- [ ] Every shape and connector is named `!!<name>`, and connectors stay glued when a shape is
  moved in PowerPoint.
- [ ] `step=N` and `exit=N` play as clicks in order of N, with `effect=` choosing appear, fly or
  wipe. The slide passes the Open XML validator.
- [ ] Two scene slides with shapes of the same names and `transition="morph"` on the second morph
  in PowerPoint, and fade where Morph is not available.
- [ ] A label that does not fit at 70 % gives `slide-overfull` with the shape's name. Each parse
  error throws `DocumentConversionException` naming its line, in both formats.
- [ ] The PDF shows each scene as one vector drawing of its final state in the brand's colours,
  and stays PDF/A-2b.
- [ ] Decks and PDFs without `::: scene` are unchanged.
- [ ] **Checked in real PowerPoint**, and the result recorded in the implementation notes:
  - the sample opens without a repair prompt;
  - shapes sit where the grid says;
  - steps play in order;
  - Morph moves boxes, labels and glued connectors smoothly between the slides;
  - a 4:3 caller template looks as documented.
- [ ] The guides, expected guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Line syntax: positional `box api 65 34 30 22 "API" .accent` (proposed: short, and easy for
   writers and AI agents to keep aligned), or named values `box api x=65 y=34 w=30 h=22
   text="API"`?

> Answered by the human (2026-11-14): as proposed.

2. Steps: a `step=N` / `exit=N` attribute per shape (proposed: a shape can come and go at any
   step), or groups of lines separated by `---`, each group appearing on one click? A `---` would
   need a blank line before it, because of the setext-heading trap.

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Colours: only the palette roles and label categories (proposed: the brand decides, and dark
   slides and caller themes keep working), or also hex colours such as `fill=#2E7D32`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Shape kinds: `box`, `round`, `ellipse`, `text`, `arrow` and `line` (proposed). Should `image
   name x y w h path` come now as well, for logos and screenshots that move with Morph, or with the
   icons of E04-S05?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Slides that are not 16:9 (a 4:3 caller template): the grid stretches per axis, so units are not
   square and a circle becomes an ellipse (proposed, as decided: the grid covers the whole slide),
   or square units over a 16:9 band centred on the slide?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. What else a scene slide may hold: only its title, the scene and `::: notes`, with anything else
   an error (proposed), or text and other blocks in the content area under or over the scene?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. The PDF still: the final state, every shape that is visible after the last step (proposed), the
   first state, or one drawing per step?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Morph: only where a heading says `transition="morph"` (proposed: explicit, like every other
   transition), or automatically between consecutive scene slides that share a shape name?

> Accepted as proposed (the human, 2026-11-14, without discussion).

9. Each scene states all its shapes (proposed: each slide reads on its own and errors stay local),
   or a `::: scene continue` that starts from the previous slide's scene and only lists what
   changes?

> Accepted as proposed (the human, 2026-11-14, without discussion).

10. The title: keep the heading as the slide's title on the title-only layout (proposed), or allow
    a scene without a visible title (such as `{.notitle}`, with the title kept off the slide for
    the outline and accessibility)?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

Implemented by doc-3 on 2026-11-14.

### Breaking changes

None. `::: scene` was an unknown container (an error) in every generator before. Decks, PDFs and Word
documents without a scene are unchanged: `TimingWriter` writes the same XML for every build that is not a
scene's, and the 48 golden PNGs of E07-S03 pass as they were (the painting SVG gains nothing). Additive
changes a consumer may notice: `CheckStatistics.SlideKinds` lists the new kind `scene` (before `closing`),
and the error "Unknown container" lists `scene`. The HTML deck's Morph now moves shapes; the HTML deck is
itself unreleased, so that is under *New* in `CHANGELOG.md`.

### What was built

- **Shared** (`wwosik.DocLibs.DocumentGeneration`, internal): `SceneSyntax` (`Check`, `Parse`), the model
  `Scene`, `SceneNode`, `SceneConnector`, `SceneStyle`, `SceneKind`, `SceneRole`, `SceneSide`, and
  `SceneDrawing.Final`. `LayoutSyntax` knows `scene` (no arguments); `DocumentCheck` counts a scene as a
  diagram. `SceneSyntax.Check` runs right after `BuildMarker.Check` in all three generators.
- **Decks:** `SceneSlide` (`DeckParser.Slides`), written by `DeckWriter.Scenes.cs`; `Fit` passes it through.
  Shapes are `p:sp` named `!!name` (`rect`, `roundRect`, `ellipse`; `text` a text box without fill and line),
  label in their own centred text body (`wrap="none"`, `anchor="ctr"`, no autofit), shrunk by 5 % steps to
  70 % by `TextMetrics`, else `slide-overfull` naming the shape and its line. Connectors are `p:cxnSp`
  (`straightConnector1`, `tailEnd` triangle for arrows, flips for leftwards/upwards) glued with
  `stCxn`/`endCxn`; a connector label is a text shape `!!name-label` above the middle (or right of an
  up/down connector). Steps: `Canvas.Target` adds `BuildTarget`s whose `BuildMark` is `Joined` with the step
  number as `Block`; `TimingWriter.Steps` turns all joined marks of one number into one click, a group per
  build (`clickEffect`, then `withEffect`). `PptxGenerator.Check`'s kinds gain `scene`.
- **PDF:** `MarkdownParser` adds a `DrawingBlock(SceneDrawing.Final(…))` in the brand's colours.
- **Word** (not in the plan, see below): a scene is a picture of the same drawing through the image
  converter (alt text: the labels shown at the end), else left out with `code-not-drawn`.
- **HTML deck:** `SceneSvg` writes `data-name` on items named `!!…` (Html target only); `deck.js` plays
  `morph` (new `morph()`, `morphItems()`, `lay()`): the leaving slide stays on top (`.morphing`: its
  background, decorations and notes hidden), each of the new slide's shown top-level own items with a
  matching name moves and resizes from the old item's box (individual `translate`/`scale`, as the builds),
  while the old item moves to the new box and fades out; other old items fade out, other new ones fade in.
  Along an axis where either box is thinner than 10 pt or the size changes more than fivefold the item only
  moves (a line with its arrow head otherwise blew up into a band). No inline styles, the CSP is unchanged.
- **Reader fix:** `Geometry.Path` divided by a zero height for a connector straight across (`cy="0"`), which
  gave `NaN` in the page's paths (Chromium logged errors); it now keeps that axis. This is the DrawingML
  coverage this story needed; `scenes.md` joined `EverySampleDeck_IsDrawn_WithNothingLeftOut`.
- **Guides:** a short PPTX *Scenes* paragraph (see the guide-length decision below) with the new
  `{{sceneTitleArea}}` ("Shapes start at 22." on the built-in master, from the title-only layout's title; a
  caller's non-seekable template stream gives "Leave the title room."); PDF one sentence; Word one table row. All expected
  guides and `tool-guide.expected.md` regenerated.
- **Docs:** Pptx *Presenting → Scenes*, HTML deck Morph, *Known limits*; Pdf *Slide layouts*; DocumentGeneration
  *Scenes* and `SlidesByKind`; Docx graphics table; `docs/README.md`; `CHANGELOG.md`; `ai/memory.md`.

### Decisions and deviations

- **Question 4 (image shapes):** answered "as proposed", so only `box`, `round`, `ellipse`, `text`, `arrow`
  and `line`; no `image`.
- **Type names:** the shared model is `SceneNode`/`SceneConnector`/`SceneElement`, not the plan's
  `SceneShape`/`SceneItem`, which the HTML deck reader already uses in `…Pptx.Rendering`. The writer is a
  `DeckWriter` partial (`DeckWriter.Scenes.cs`), not a separate `SceneWriter`, because it needs the deck's
  palette, runs, notes and canvas.
- **Connectors inherit steps:** a connector without `step=` comes in with the later of its shapes, without
  `exit=` it leaves with the earlier one that leaves (else it would show before its shapes). An explicit step
  after an inherited exit throws.
- **Exit is always Disappear;** `effect=` is the entrance and needs `step=`; `exit` must come after `step`.
- **`.dark`, `kicker` and slide kinds on a scene heading throw**; `layout="…"` and `transition="…"` work. The
  plan did not say; dark scene slides would need their own palette handling.
- **Colours:** default box = surface fill + thin primary line + text colour; a role fills (`.muted` a light
  neutral tint with normal text, `.dark` the palette's dark colour, categories their full label colour) with
  white or dark text by WCAG contrast (theme colours on caller templates take white); `.outline` is the line
  alone; on a `text` the role colours the text; on a connector its line. `.outline` is accepted on connectors
  and means the default line.
- **Word documents** draw scenes too (E09 came after the plan): otherwise a scene would have passed
  `LayoutSyntax` and been written as its raw lines.
- **The PDF still is 960 × 540 as planned,** so the title band (top ~19 units) is empty space above the shapes.
  Cropping it to the shapes would save room on the page; left for the human to decide.
- **Guide length — a decision for the human:** the plan's *Scenes* section with an example does not fit
  the PPTX guide's 3,500-word limit, and the limit was not raised. Before rebasing, a section of ~75 words
  with an example fitted after shortening the Morph sentence under *Transitions* and cutting two details of
  other stories (the risk matrix's tint bands, "Use them for sources." under footnotes); after rebasing onto
  E03-S03/E03-S04, whose front matter keys and prose took the rest of the room, only a three-line paragraph
  without an example fits: the guides are at 3,480 (sample) / 3,497 (custom) words, the PDF guide at 2,498 of
  2,500 (its scene sentence is "`::: scene`: one still picture.", and "as normal text" became "as text").
  Proposed, if the human raises the limits (e.g. PPTX 3,600, PDF 2,550), the fuller PPTX paragraph:

  ```markdown
  **Scenes:** `::: scene` draws shapes on a 160 × 90 grid, one per line: `box`, `round`, `ellipse`
  or `text` with `name x y w h "label"`; `arrow` or `line` with `from to`.
  {{sceneTitleArea}} Attributes: a colour (`.primary`, `.accent`, `.highlight`, `.muted`, `.dark`,
  `.outline`, a label category), `.bold`, `.small`, `.large`; `step=N`, `exit=N` (`effect=fly`) for
  clicks. Only `::: notes` may join it; `transition="morph"` moves shapes of the same name.
  ```

  with the example `## Flow {transition="morph"}` / `::: scene` / `box web 8 34 30 22 "Browser"` /
  `box api 65 34 30 22 "API" .accent step=1` / `arrow web api` / `:::`, and the cut sentences restored.

### Verification

- **Tests:** `./run.sh build` 0 warnings (also `--no-incremental`); `./run.sh test` after rebasing onto
  E03-S03 and E03-S04: 1,665 passed, 6 Windows-only skipped (Pptx.Tests 513, Pdf.Tests 290, shared 399,
  Docx.Tests 155, Svg.Tests 35 with the golden PNGs unchanged). New: Pptx `SceneTests` (sample valid without notes and round-tripping on neutral and
  sample; grid positions and `!!` names on the title-only layout; a 4:3 caller template; glue sites and flips
  of four connectors; click sequence with gaps, equal numbers together, effects; connector step inheritance;
  label shrinking and `slide-overfull`; labels keep `*`, `[`, `\"` and break at `\n`; role colours; Morph
  names across slides; every parse error with its line (17); every misplacement (8); notes/footer/transition
  kept, 40 shapes never split; check counts; HTML `data-name` only in the page), Pdf `SceneTests` (PDF/A
  marker and round trip on both templates, the drawing is the final state, errors in the PDF's format, notes
  left out), shared `SceneTests` (parsing, sides, the golden still `scenes.expected.txt`, final state, colours),
  Docx (picture or note).
- **Open XML validator** clean on every generated deck (tests).
- **Chromium** (Playwright, headless, throwaway `.tmp/check-morph.js`) on `scenes.md` on the neutral, sample
  and dark templates, from disk: steps on slide 2, the Morph into slides 3 and 4 frozen at 25/50/75 %
  (`.tmp/review/e07s04/*-morph-*.png`): matched boxes, ellipse and connectors move and resize, the old ones fade
  over them, unmatched items fade; afterwards no leaving slide, no `.morphing`, no running animations; back is
  instant; `prefers-reduced-motion` gives no animation; no console errors, no requests.
- PDFs looked at as PNGs (pypdfium2): `.tmp/review/scenes-*.pdf`. veraPDF was not run (no Java here).

### Open verification items (for the human)

- **Real PowerPoint** with `.tmp/review/scenes-neutral.pptx` and `scenes-sample.pptx`: opens without a repair
  prompt; shapes sit where the grid says; steps play in order (slide 2: API, then database with arrow and
  caption; slide 3: API 2 flies in, then the caption leaves as the note appears); Morph from slide 2 to 3 and 3 to
  4 moves boxes, labels and glued connectors smoothly; dragging a shape keeps its connectors glued (the
  ellipse connection sites 0/2/4/6 are from the preset definitions, to be confirmed); a 4:3 caller template
  looks as documented. The acceptance box for real PowerPoint stays unticked.
- Whether PowerPoint wants separate `grpId`s for a shape's entrance and exit (written as E07-S05 writes
  `{.frag .exit}`: both `grpId="0"`).
- The HTML deck's Morph in other browsers, and compared with PowerPoint's (text is stretched with its shape
  while moving in the page; PowerPoint keeps the text size).

### Loose ends

- `PptxConverter` reads a scene back as its labels only; rebuilding `::: scene` is out of scope.
- Icons (E04-S06) as scene shapes, and `image` shapes, are not offered.
