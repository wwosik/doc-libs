# E01-S06 Template roles for decks

> Status: in progress (doc-3)
> Epic: [E01 Brands and templates](../../50-todo/E01-brands-and-templates.md)

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
  - In the [analysis](../../10-analysis/2026-11-13%20python-fork-port.md) (answer 3): roles are
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
