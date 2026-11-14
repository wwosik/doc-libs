# E01-S06 Template roles for decks

> Status: in verification
> Epic: [E01 Brands and templates](../50-todo/E01-brands-and-templates.md)

## Summary

A deck template can say what its layouts and placeholders are for. A brand pack or the caller's
options name the template's layouts for new **layout roles**: two content, text and picture,
picture, agenda, quote, statement, contact, chart, two charts and dark. They also name
**placeholder roles** inside a layout: subtitle, byline, chapter number, body (in order), picture,
chart title, chart unit, chart source and decorative. A template can also declare **text levels**
(`###`, paragraph and bullet → outline level), a **footer pattern** and a **content grid** taken
from the master's guides. The built-in master gains layouts for every new role, so the roles also
exist without a caller template.

This story builds the model, its discovery in the template and the three features that work on
their own: text levels, footer pattern and grid. The Markdown that puts content into the new
roles comes in [E01-S07](E01-S07-chapter-numbers-and-layouts-across-placeholders.md) (numbered
chapter dividers, `{layout="…"}` across placeholders) and E02-S05 (slide kinds).

## Context

- **Layouts today.** `DeckWriter`'s constructor (`DeckWriter.cs`) resolves six layouts:
  - `titleLayout`, `contentLayout`, `titleOnlyLayout`, `sectionLayout` and `closingLayout` come
    from `PptxLayoutNames` (`PptxTemplateOptions.cs`) through `ByName`. Failing that, by type
    (`ByType`), then by placeholder set, then by fallback. `closingLayout` also matches the name
    pattern `ClosingNamePattern`.
  - `darkLayout` is found only by the name `TemplateBuilder.DarkLayoutName` (*Title Only Dark*).
  - `ByName` matches names exactly and throws `DocumentConversionException`, listing the
    template's layouts. `LayoutOf` serves `layout="…"` (E01-S05), case-insensitively, and throws
    on the built-in master.
- **Where the names come from.** `PptxBrandTemplate` (`Brand.cs`, shared package) has
  `TitleLayout`, `ContentLayout`, `TitleOnlyLayout`, `SectionHeaderLayout`, `ClosingLayout` and
  `Tone`, and compares by content.
  - `BrandTemplateOptions.LayoutNames` copies them into a `PptxLayoutNames`.
    `CustomTemplateOptions.Layouts` is a `PptxLayoutNames` itself, with the front-matter keys
    `layouts.title` … `layouts.closing`.
  - The brand pack reads `pptx.template`, `pptx.layouts.{title,content,titleOnly,sectionHeader,closing}`
    and `pptx.tone` (`BrandManifest.PptxSection`, `LayoutsSection` in `BrandPack.cs`). It uses
    source generation with `UnmappedMemberHandling.Disallow`.
- **Placeholders today.**
  - `FindPlaceholder(layout, IsTitle)` finds the title; `IsTitle` is `title` or `ctrTitle`.
  - The body is the first placeholder where `IsBody` holds: untyped, `body` or `obj`, in tree
    order.
  - The subtitle is `subTitle`, else the next body (`WriteTitleSlide`).
  - The footer fields are `sldNum`, `dt` and `ftr` (`Footer`).
  - Nothing else is addressed: a layout's second body, its picture or chart placeholders are
    never filled. A body placeholder that only draws decoration (a coloured bar, a sticker) is
    filled like any other when it comes first.
- **Text levels.**
  - `DeckParser.Walk` writes `###` as a bold paragraph at the current level.
  - Paragraphs are level 0. A list item is level 0 plus its nesting depth.
  - In placeholders, `Paragraph(…, explicitBullets: false)` sets only `lvl`, so the template's
    level styles apply.
  - Corporate templates often use level 1 for a sub-headline, level 2 for text and level 3
    onwards for bullets. Markdown then lands one or two levels off.
  - `PptxOptions.ListBullets` (E01-S05) only adds bullets. It does not move levels.
- **Fitting** (`DeckWriter.Fitting.cs`) reads one body size from level 1 (`ReadBodySize`) and
  estimates every paragraph at it.
- **Footer.** `Footer` writes slide number, date and `PptxTemplateOptions.FooterText` (the brand's
  `FooterText`, only on a brand's own template) into their placeholders. There is no way to show
  the deck title or the current chapter there. The built-in master draws its footer text as a
  master text box (`TemplateBuilder.FooterText`), not a placeholder.
- **Content area.** `FullContentArea` is the layout's body placeholder, else the master's, else a
  fraction of the slide. Title-only layouts of corporate templates often have no body, so flow
  slides take the master body's box. Many templates mark their real grid with drawing guides on
  the master (`p15:sldGuideLst` in the master's extension list) instead.
- **Built-in master.** `TemplateBuilder` writes five layouts: `title`, `obj`, `titleOnly`,
  `secHead` and *Title Only Dark*. Their relationship ids are fixed: layouts 1–4 are `rId1`–`rId4`
  and layout 5 is `rId7` (`LayoutRelationship`, trap in `ai/memory.md`). Layout 6 onwards would be
  `rId8`….
- **Test template.** `brand-dark` (`DarkTemplate` in the PPTX tests) is built at test time from
  the built-in master, with two copied and renamed layouts. There is no binary sample.
- **Decided.**
  - In the [analysis](../10-analysis/2026-11-13%20python-fork-port.md) (answer 3): roles are
    designed for templates in general, not to cover any particular design.
  - In the epic:
    - Roles are brand-pack data with English names, and nothing here names a client's layouts or
      placeholders.
    - Every new role is opt-in, and existing decks do not move.
  - Behaviour difference 3: text levels apply only with a template that declares them.

## Proposed approach

1. **Public model** in `wwosik.DocLibs.DocumentGeneration`, next to `PptxBrandTemplate`, so that
   brands and caller templates share it. All are records whose equality compares their content,
   as `PptxBrandTemplate` already does.

   ```csharp
   public sealed record PptxTemplateRoles
   {
       public PptxLayoutRoles Layouts { get; init; } = new();
       public IReadOnlyList<PptxPlaceholderRole> Placeholders { get; init; } = [];
       public PptxTextLevels? TextLevels { get; init; }        // null: Markdown's levels as today
       public string? FooterPattern { get; init; }             // null: the footer text as today
       public PptxContentArea ContentArea { get; init; }       // Placeholders (default) or Guides
   }

   public sealed record PptxLayoutRoles   // layout names as PowerPoint shows them
   {
       public string? TwoContent { get; init; }
       public string? TextAndPicture { get; init; }
       public string? Picture { get; init; }
       public string? Agenda { get; init; }
       public string? Quote { get; init; }
       public string? Statement { get; init; }
       public string? Contact { get; init; }
       public string? Chart { get; init; }
       public string? TwoCharts { get; init; }
       public string? Dark { get; init; }
   }

   public sealed record PptxPlaceholderRole(string Layout, string Placeholder, PlaceholderRole Role);

   public enum PlaceholderRole { Subtitle, Byline, ChapterNumber, Body, Picture, ChartTitle, ChartUnit, ChartSource, Decorative }

   public sealed record PptxTextLevels(int Heading, int Paragraph, int Bullet);   // outline levels 1–9

   public enum PptxContentArea { Placeholders, Guides }
   ```

   - `PptxBrandTemplate` gains `Roles` (`PptxTemplateRoles`, default empty).
   - `CustomTemplateOptions` gains `Roles` too.
   - `PptxLayoutNames` and the existing five properties stay as they are. They are not moved into
     the new record, because that would break callers.
   - A `PptxPlaceholderRole` names its placeholder by the shape name the Selection Pane shows
     (`cNvPr/@name`), matched case-insensitively (see question 3).
   - `Body` may appear several times for one layout: their order is the order of the bodies.
   - Validation (`ResolvedBrand.Resolve` for brands, the options for caller templates) throws
     `ArgumentException` naming the property in these cases:
     - a text level outside 1–9, or `Heading` deeper than `Bullet`;
     - an unknown `{…}` in the footer pattern;
     - two roles for the same placeholder;
     - a placeholder role that is not `Body` given twice for the same layout.

2. **Brand pack.** `BrandManifest.PptxSection` gains these keys, all optional and English:

   ```json
   "pptx": {
     "template": "deck.potx",
     "layouts": { "title": "…", "twoContent": "Two Content", "textAndPicture": "…", "picture": "…",
                  "agenda": "…", "quote": "…", "statement": "…", "contact": "…",
                  "chart": "…", "twoCharts": "…", "dark": "…" },
     "placeholders": { "Quote": { "Text Placeholder 3": "byline", "Picture Placeholder 5": "decorative" } },
     "textLevels": { "heading": 1, "paragraph": 2, "bullet": 3 },
     "footer": "{footer} | {chapter}",
     "contentArea": "guides"
   }
   ```

   - `LayoutsSection` gains the ten role names.
   - `placeholders` maps each layout name to its placeholders and their roles. A role value is
     `subtitle`, `byline`, `chapterNumber`, `body`, `picture`, `chartTitle`, `chartUnit`,
     `chartSource` or `decorative`. To give several bodies an order, write `body1`, `body2`, ….
   - An unknown value throws `InvalidDataException` naming the JSON key, as `pptx.tone` does.

3. **Caller templates in front matter.** `CustomTemplateOptions.Keys` gains `layouts.twoContent`
   … `layouts.dark`, like the existing `layouts.*` keys, so a deck can name its layouts.
   Placeholder roles, text levels, footer pattern and grid stay in the options only (question 4).
   The deck guide's front-matter table lists the new keys through `KeyMeanings`.

4. **Discovery.** A new internal `TemplateRoles` is resolved once in the `DeckWriter` constructor
   from the layouts list and the declared roles.
   - **Layout roles.**
     1. **Declared name.** Matched exactly, as `ByName` does, and an unknown name throws listing
        the layouts.
     2. **Layout type.** Only where OOXML has a type for the role: `twoObj` or `twoColTx` →
        two content, `picTx` → text and picture, `chart` → chart.
     3. **Dark.** The name *Title Only Dark*, as today.
     4. **Otherwise none.** The writer falls back as each later story defines.

     Agenda, quote, statement, contact, picture and two charts are found only by name, because
     neither OOXML types nor names say this reliably (question 2).
   - **Placeholder roles** per layout:
     1. **Declared.** A declared placeholder the layout lacks throws, listing its placeholder
        names.
     2. **Undeclared.** The OOXML type decides: `subTitle` → subtitle, `pic` → picture, `chart` →
        chart, and `body`, `obj` or untyped → body.
     3. **Byline, chapter number, chart title, chart unit and chart source** are found only when
        declared.
     4. **Decorative placeholders**, and those with a declared role that is not `Body`, are never
        counted as bodies. They are not written to the slide (question 5).
     5. **Undeclared bodies** are ordered by reading order:
        - left to right when their tops are within half the height of the smaller one;
        - otherwise top to bottom.
   - **Existing lookups.** The content, title-only, section and cover lookups keep their present
     code. In particular, a content slide's body stays "the first body in tree order", so no
     existing deck moves. Only decorative placeholders are now skipped there. That is new data, so
     nothing changes for a template that declares none.

5. **Text levels.** Only for a template that declares them, and only in placeholders (content
   slides, and the placeholders of the next stories). Text boxes and flow slides keep their
   explicit formatting.
   - `TextParagraph` gains `Heading` (`init`), set by `Walk` for `###` and deeper.
   - **Mapping.**
     - A `###` becomes a paragraph at `Heading`, without the bold the parser adds. The level's
       style decides its look.
     - A plain paragraph, a quote line or a code line becomes `Paragraph`.
     - A list item goes to `Bullet` + its depth, capped at 9.
     - Numbered and task items keep their numbering and boxes.
     - `ListBullets.Always` still writes bullets, at the mapped level.
   - **Fitting.** `ReadBodySize` becomes `ReadLevelSizes`: the size of each of the nine levels,
     from the layout's body, else the master's body style. With text levels, `EstimateHeight`
     measures each paragraph at its own level's size, and `normAutofit` scales them together.
     Without text levels, nothing changes.

6. **Footer pattern.** On a brand's own template and on caller templates it fills the layout's
   `ftr` placeholder instead of the bare footer text.
   - **Fields.**
     - `{footer}`: `Brand.FooterText`, empty for caller templates.
     - `{title}`: the deck title, the one written to the core properties.
     - `{chapter}`: the text of the current `#` section divider, empty before the first.
     - `{author}`.
   - Runs of separators left dangling by empty fields (` | `, ` · `, ` – `) are trimmed at the
     ends.
   - `ShowFooter` / `showFooter` switch it off as before. The built-in master keeps its own footer
     text box and ignores the pattern.

7. **Content grid.** With `ContentArea = Guides`, `FullContentArea` of every layout is the box
   inside the master's drawing guides:
   - between the leftmost and the rightmost vertical guide;
   - from the first horizontal guide below the title area to the last one above the footer
     placeholders.

   The guides come from `p15:sldGuideLst` in the master's `p:extLst`. Their unit has to be
   confirmed in PowerPoint: it is believed to be 1/8 pt, and a test template with known guides
   pins it. Layouts' own guides are not read. A template declared with `Guides` but without at
   least two vertical and two horizontal guides throws `DocumentConversionException` naming the
   template. The default `Placeholders` keeps today's area.

8. **Built-in master.** `TemplateBuilder` gains one layout per role, appended as layouts 6–13
   (relationships `rId8`–`rId15`, so the fixed ids of layouts 1–5 do not change). They are drawn
   in the brand's design, like the existing ones:

   | Layout | Type | Placeholders |
   | ------ | ---- | ------------ |
   | *Two Content* | `twoObj` | title, two bodies side by side |
   | *Picture with Caption* | `picTx` | title, body left (40 %), picture right |
   | *Picture* | `cust` | title, one picture over the content area |
   | *Agenda* | `cust` | title, body with numbered level 1 |
   | *Quote* | `cust` | large italic body, byline below it, no title placeholder |
   | *Statement* | `cust` | title set large and centred on the brand gradient, body below |
   | *Contact* | `cust` | title, five pictures in a row, with a body under each |
   | *Chart* | `chart` | title, chart, chart title, unit and source text placeholders |

   - The *Section Header* layout gains a chapter-number placeholder (a `body` with its role
     declared by the built-in roles) above the title. It is not shown on a slide unless the slide
     fills it, which no existing deck does.
   - Two charts use *Two Content*, whose object placeholders take charts.
   - The built-in master declares these roles itself, through an internal `TemplateRoles` built
     with `TemplateBuilder`, so that discovery works the same way on every template.
   - `layout="…"` still throws on the built-in master.

9. **Authoring guides.** The deck guide's `{{templateLayouts}}` paragraph (`TemplateLayouts` in
   `PptxGenerator.cs`) says, for brand and caller templates, which roles the template has. With
   text levels it says that `###` is a sub-headline in the design's style. The expected guides
   are regenerated (`authoring-guide-custom`, `authoring-guide-sample`). The PDF guide is
   unchanged.

10. **Docs, changelog, memory.**
    - `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md`: *Templates* (roles, discovery order, text
      levels, footer pattern, grid) and *The built-in template* (the new layouts).
    - `docs/wwosik.DocLibs.DocumentGeneration.md`: `PptxTemplateRoles` and the new `brand.json`
      keys under *Brand packs*.
    - `ai/memory.md`: the relationship ids of the new layouts, and the guide unit.
    - `CHANGELOG.md`:
      - **Breaking:** decks on the built-in master carry eight more layouts. The file grows by a
        few kilobytes and PowerPoint's layout gallery lists them, but slides do not change.
      - **New:** the roles API and keys.

11. **Tests.**
    - Brand pack round trip of every new key, and an unknown role value. `PptxTemplateRoles`
      equality.
    - Discovery on a test template `brand-roles`, built at test time from the built-in master
      like `DarkTemplate`. Its layouts are renamed and their types stripped, with a decorative
      placeholder and declared roles.
    - Declared names, types, undeclared and missing roles. An unknown layout or placeholder name
      throws, listing the valid ones.
    - Reading order of bodies.
    - Text levels: `###`, a paragraph and three bullet depths land on levels 1, 2 and 3–5, and
      fitting uses the level sizes. Without text levels, all expected round trips are unchanged.
    - The footer pattern with each field, with empty fields trimmed, and with `showFooter: false`.
    - Guides: a template with known guides gives the expected area, and one without them throws.
    - The built-in master: every role is discovered, every existing sample is unchanged, and
      `Samples.AssertValid` passes on decks of every template.
    - **In PowerPoint** (`.tmp/scripts/export.ps1`): the new built-in layouts open without repair
      and look right, and the guide unit and grid are correct.

## Acceptance criteria

- [ ] Layout and placeholder roles can be declared in code (`PptxBrandTemplate.Roles`,
  `CustomTemplateOptions.Roles`) and in `brand.json`, and invalid values throw naming the property
  or key.
- [ ] Undeclared roles are found by OOXML type where one exists. Unknown declared names throw,
  listing the template's layouts or the layout's placeholders.
- [ ] Decorative placeholders are never filled, not even as a content slide's body.
- [ ] With `textLevels`, `###`, paragraphs and bullets land on the declared outline levels and
  fitting measures them at their levels' sizes. Without it, decks are unchanged.
- [ ] A footer pattern shows the footer text, deck title, chapter and author in the footer
  placeholder.
- [ ] `contentArea: guides` places flow and visual content inside the master's guides.
- [ ] The built-in master has the eight new layouts and a chapter-number placeholder on its
  section layout, and declares their roles. Its decks open in PowerPoint without repair and pass
  the Open XML validator.
- [ ] All existing samples give the same round trips, the guides, docs and CHANGELOG are updated,
  and all tests pass.

## Open questions

1. Split the fork's "template roles" item into this story (model, discovery, text levels, footer,
   grid, built-in layouts) and E01-S07 (numbered dividers, `{layout="…"}` across placeholders,
   filling picture and chart placeholders)? Proposed: yes. Each half can be reviewed on its own,
   and E02-S05 depends on both.

> Answered by the human (2026-11-14): as proposed.

2. Layout roles without an OOXML type (agenda, quote, statement, contact, picture, two charts):
   found only when declared (proposed), or also by English name patterns as the closing layout is
   (`^agenda`, `^quote`, …)? Patterns help templates nobody configured, but they fail on
   templates named in other languages and can catch the wrong layout.

> Answered by the human (2026-11-14): as proposed.

3. Placeholders are named by their shape name as the Selection Pane shows it (proposed: visible
   to whoever edits the template). The alternative is their `idx`, which is stable when a
   placeholder is renamed but visible nowhere in PowerPoint. Or accept both, with an `idx:13`
   form?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Front matter for caller templates: only the layout roles as `layouts.*` keys (proposed), or
   also placeholder roles and text levels? They describe the template, not the document, so they
   belong in the options or the brand pack.

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Decorative placeholders: never written to the slide (proposed). Or written with a single
   no-break space, so that a fill or outline the placeholder draws shows in the slide show?
   PowerPoint hides empty placeholders in the show, so this only matters for templates that put
   decoration into placeholders. To be checked in PowerPoint.

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. The built-in master: all eight new layouts (proposed: every role works without a caller
   template, and the decks stay editable in PowerPoint), or only what E02-S05 cannot draw as a
   flow slide, and none at all if every kind falls back well? All eight make built-in decks a few
   kilobytes bigger.

> Answered by the human (2026-11-14): as proposed.

7. Text levels are 1-based, as PowerPoint's *Increase list level* shows them (proposed), rather
   than OOXML's 0-based `lvl`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Footer pattern fields: `{footer}`, `{title}`, `{chapter}` and `{author}` (proposed). Should
   the date also be a field, for templates without a date placeholder, or stay in its own
   placeholder only?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

- **Decks on the built-in master carry eight more layouts** (question 6): *Two Content*, *Picture
  with Caption*, *Picture*, *Agenda*, *Quote*, *Statement*, *Contact* and *Chart*, as layouts 6–13
  (master relationships `rId8`–`rId15`), and the *Section Header* layout has a *Chapter Number*
  placeholder. A deck grows by eight layout parts (a few kilobytes) and PowerPoint's layout gallery
  lists them; slides, notes, charts, media and the ids of layouts 1–5 do not change (see
  *Verification*). In `CHANGELOG.md` under *Unreleased*, breaking changes.
- A caller who uses a deck of the built-in master as its own template sees those layouts too. Only
  one case behaves differently: a section layout **without a title placeholder** now has two
  undeclared bodies, so the subtitle goes into the chapter-number placeholder instead of a text box
  below the title (`TemplateTests.SectionLayoutWithoutTitlePlaceholder_…` removes that placeholder
  now). Also in the changelog, with what to do.
- Nothing else: brand and caller templates without declared roles give the same decks, part by part.

### Decisions

- **Public model** as planned, in `PptxTemplateRoles.cs` (shared package): `PptxTemplateRoles`,
  `PptxLayoutRoles`, `PptxPlaceholderRole`, `PlaceholderRole`, `PptxTextLevels`, `PptxContentArea`.
  `PptxTemplateRoles` compares its placeholder list by content; `PptxBrandTemplate.Roles` and
  `CustomTemplateOptions.Roles` default to an empty instance and take part in equality. Validation is
  one internal `Problem()`, called by `ResolvedBrand.Resolve` (`Pptx.Roles.<property>`) and by
  `PptxGenerator.Generate` for caller templates (`CustomTemplateOptions.Roles.<property>`), both
  `ArgumentException`. Besides the planned cases it rejects a role without layout or placeholder
  name and an undefined enum value.
- **Brand pack:** `placeholders` values `body1`, `body2`, … order a layout's bodies by number;
  plain `body` entries come first, in the file's order. `textLevels` needs all three keys (a missing
  one is `InvalidDataException` naming it); range checks are left to the brand's validation, as for
  colours.
- **Discovery** (`TemplateRoles`, Pptx package, internal): resolved once in the `DeckWriter`
  constructor, with `TemplateBuilder.Roles` on the built-in master. Every declared layout and
  placeholder is checked there, so a wrong name fails whatever the deck contains. Layout names match
  exactly (like `ByName`), placeholder names case-insensitively. `LayoutPlaceholders` holds bodies
  (declared ones first in their order, then the undeclared ones in reading order), subtitle, byline,
  chapter number, pictures, charts (by type only: `PlaceholderRole` has no chart role, as planned),
  chart title, unit and source, and `Excluded` (decorative ones and those declared with a role other
  than body). Positions for reading order come from the placeholder, else from the master's
  placeholder with the same `idx` (or type).
- **Existing lookups** keep their code; only `DeckWriter.Bodies` (content body, cover and divider
  bodies, `FullContentArea`, the body size) and the cover's `subTitle` lookup skip `Excluded`
  placeholders. The plan said "only decorative placeholders are skipped there"; skipping every
  non-body role is what step 4.4 says and what the built-in chapter-number placeholder needs.
- **Dark role:** `darkLayout` is now `roles.Layout(Dark)`: a declared dark layout (or front matter
  `layouts.dark`) takes `{.dark}` flow slides, which then keep that layout's background instead of a
  painted one. Without a declaration it is *Title Only Dark* by name, as before.
- **Text levels** (deviations in detail): `TextParagraph.Heading` is a `bool` set for `###` and
  deeper. The parser still makes heading runs bold (it merges runs, so the old output stays); under
  text levels the writer removes the bold from every run of a heading but coloured words, so a
  `**word**` inside a `###` loses its bold there. Plain paragraphs keep `buNone` and indent 0 at their
  level; a plain paragraph inside a nested list item (level > 0) goes to the bullet level plus its
  depth, so it stays under its item. Headings get only `lvl`: their look is the level's. With
  `ListBullets.Always`, list items get Markdown's bullet characters but no explicit margins: the
  level's indents apply. Numbered and task items keep their numbering and boxes, also without
  explicit margins. Text levels act on the content slide's body placeholder only (E01-S07 adds its
  placeholders); fitting measures each paragraph at its level's size, scaled like the body size.
- **Footer pattern:** the separators are `|`, `·`, `–` and `—`; besides trimming them at the ends
  (as planned), two left side by side by an empty middle field become one (`{footer} | {chapter} |
  {title}` without a chapter reads `Footer | Title`). An empty result writes nothing. `{chapter}` is
  the plain title of the latest `#` divider (closing slide excluded), the divider itself included.
  The deck title is now computed before the writer runs; it is the same value written to the core
  properties.
- **Grid:** the guides are read from any `p15:guide` in the master; the bottom limit is the top of
  the master's highest `dt`/`ftr`/`sldNum` placeholder (the slide height without one). Every master
  of the template is checked in the constructor, so a template with `Guides` and no guides throws
  before writing. `FullContentArea` (flow and visual slides, footnotes, a content slide's text box)
  uses the grid; a content slide's body placeholder stays where the layout puts it unless footnotes
  shorten it. A guide needs a `p15:clr` child to pass the validator (the test template has one).
- **Built-in layouts** as planned, all in the brand's design (`TemplateBuilder`): two content with
  `obj` bodies (`sz="half"`, which take charts); picture with caption with the body left at 40 %;
  agenda with `buAutoNum arabicPeriod` on level 1 at 24 pt; quote in 32 pt italic in the primary
  colour with an 18 pt byline below and no title; statement on the brand gradient with a centred
  44 pt white title, a white body, white footer line and the logo for dark backgrounds (sharing the
  section's image part); contact with five square pictures and five bodies (bold 14 pt, 12 pt on
  level 2); chart with chart title (bold, primary colour), unit and source (grey) around a `chart`
  placeholder. The chapter-number placeholder (`body`, `idx` 10, white bold 40 pt, above the title)
  is the last shape of the section layout, so the first body in tree order is still the subtitle's.
- **Authoring guide:** `{{templateLayouts}}` adds, for a brand's own template, the layouts its roles
  resolve to (a role naming an unknown layout is left out of the guide; generating throws), and for
  a caller's template the layout roles declared in the options (its stream is not read). With text
  levels, one sentence says `###` is a sub-headline. The caller guide lists the ten new
  `layouts.*` keys and stays under the guide test's word limit (2600 on `main` at the rebase); the
  human's raise of that limit to 3500 (2026-11-14) was not needed here, so the test is unchanged; the sample and neutral guides are unchanged.
- **Samples:** no binary samples and no change to `tools/sample-generator`. The test template
  `brand-roles` (`RolesTemplate` in the PPTX tests) is built at test time from the built-in master:
  its two-content, quote, chart and dark layouts renamed and typed `cust`, a decorative *Accent Bar*
  body first on the content layout, a footer placeholder on master and layouts, and six master
  guides (two vertical, four horizontal, two of them outside the title-to-footer band).

### Verification

- **Tests.** `./run.sh build` without new warnings (the five `CS0618` in `Pdf.Tests` are older);
  `./run.sh test` all green after rebasing onto E04-S06 and E04-S07: 200, 123, 78, 71, 22, 180, 308
  and 1 passed (983), 6 Windows-only skipped. New: `TemplateRoleTests` (19: every role of the built-in master and its fixed ids, each
  new layout taking a slide on both brands, discovery by type on a caller's copy of the built-in
  master, declared names and body order on `brand-roles`, reading order, unknown layout and
  placeholder names, invalid caller roles, decorative placeholders, the dark role from front
  matter, text levels with and without `ListBullets.Always`, fitting at level sizes, the footer
  pattern on brand and caller templates and with `showFooter: false`, guides and a template without
  them, the front-matter keys, the guide), brand-pack round trips and mistakes in
  `BrandPackTests`, validation, equality and the footer pattern in `BrandTests`.
- **Open XML validator** (`Microsoft365`): every deck the new tests write passes, on the built-in
  master (both brands), `brand-roles`, its copy without guides and the caller template with guides;
  the existing tests validate decks of every other template, now with the new layouts in them.
- **Unchanged decks.** `deck.md`, `layouts.md`, `presenting.md` and `presenting-lists.md` were
  generated before and after this change (`.tmp/baseline/`, relationship ids masked, fixed date) on
  the neutral and sample brands, on a caller template and on a brand's own template (both a deck of
  the old built-in master): on the caller and brand templates every part is identical; on the
  built-in master only `[Content_Types].xml`, the master (eight more `sldLayoutId`s), its
  relationships, the section layout (the chapter-number placeholder) and the eight new layout parts
  differ. After the rebase onto E04-S06 and E04-S07 the comparison was repeated against `origin/main`
  (`3f2e0b7`, a detached worktree) with the same result. All expected round trips are unchanged; only `authoring-guide-custom.expected.md` was
  regenerated (the ten keys).

### To verify by hand (no PowerPoint on this machine)

The tests write the decks to `.tmp/review/`.

1. **No repair prompt** when opening `roles-layouts-neutral.pptx`, `roles-layouts-sample.pptx`,
   `roles-brand.pptx`, `roles-text-levels.pptx` and `roles-guides.pptx`, and any deck of the
   built-in master (e.g. `presenting-sample.pptx`).
2. **The new layouts** (`roles-layouts-*.pptx`, one slide per layout through a caller's copy of the
   built-in master; *Home → Layout* lists all 13): each looks right in the brand's design — two
   columns, text left and picture right, the numbered agenda, the quote and byline, the statement on
   the gradient with white text and the white logo (sample brand), five contact pictures with text
   under each, the chart layout's title, unit, chart and source boxes — and the placeholders'
   prompts appear where expected. *Section Header* shows the chapter-number box above the title in
   the layout view (*View → Slide Master*) and not on slides. The text on the *Chart* slides lands in
   the chart-title box and splits: expected until E01-S07.
3. **Guide unit:** in a template, add a guide by hand (*View → Guides*, drag one to 1″ from the left
   edge), save, and check that its `p15:guide/@pos` in `ppt/slideMasters/slideMaster1.xml` is 576
   (eighths of a point). Then open `roles-guides.pptx`, show the guides (on its master: 1″ and
   12.33″ vertically, 2″ and 6.75″ horizontally inside the band), and check that the table's top left
   corner sits on the crossing of the left and the upper inner guide.
4. **Decorative placeholders** (question 5): in `roles-brand.pptx`, the yellow *Accent Bar* at the
   top of the content layout shows in normal view and in the slide show although the slide does not
   fill it, and the text is in the content placeholder below it.
5. **Footer pattern:** `roles-brand.pptx` shows *Made-up Ltd* on the first content slide and
   *Made-up Ltd | Part two* on the divider and the slide after it, in the template's footer
   placeholder.
6. **Text levels:** in `roles-text-levels.pptx`, the sub-headline is on list level 1 and not bold,
   the paragraph on level 2 without a bullet, the bullets on levels 3–5 with the built-in master's
   bullets, the numbered item numbered; *Increase/Decrease List Level* shows those levels.

### Loose ends

- The acceptance "decks open in PowerPoint without repair" and the guide unit depend on items 1 and
  3; if the unit is wrong, `DeckWriter.Guides` converts with `EmuPerPoint / 8`.
- Content slides on a layout whose first body is a chart text (the built-in *Chart* layout through
  `layout="Chart"` on a caller's copy) put their text there; filling the role placeholders is E01-S07.
- Text levels do not reach speaker notes, text boxes or flow slides, as planned.
