# 00014 Brand template features

> Status: verify — implemented 2026-10-25

## Summary

Four things a brand's own PowerPoint template needs to work like the built-in master: the brand's
footer text in the template's footer placeholder; choosing a template layout per slide from
Markdown; a closing slide on the template's closing layout; and Markdown bullets on level 1 for
templates whose level-1 paragraphs have none.

## Context

- **Footer:** the built-in master writes `Brand.FooterText` into its own master. On a brand's
  `Pptx` template, `DeckWriter.Footer` fills only the slide number and date placeholders; the
  template's footer placeholder (`ftr`) stays empty, so the footer text never appears.
- **Layouts:** a deck uses four layouts — cover, content, title only, section — picked by type or by
  `PptxBrandTemplate.TitleLayout` / `ContentLayout` / `TitleOnlyLayout` / `SectionHeaderLayout`
  (and `CustomTemplateOptions.Layouts`). Corporate templates have many more (a two-line-title
  variant, title only with a subtitle, …) that a writer cannot reach.
- **Closing slide:** templates usually have a closing layout (logo, contact line); the generator has
  no way to end on it.
- **Bullets:** content slides leave level 1 to the placeholder's style. Many corporate templates set
  level 1 without a bullet (it is meant as a lead line), so a Markdown list's first level shows no
  bullets. Flow slides write explicit bullets already.

## Proposed approach

1. **Footer text:** when the slide's layout has a footer placeholder and the brand has
   `FooterText`, fill it (as slide number and date are filled), on every slide but the cover — like
   the built-in master. `showDate`-style switch: front matter `showFooter: false` and
   `BrandTemplateOptions.ShowFooter` (default `true`).
2. **Layout per slide:** a heading attribute `layout="<layout name>"` on `##` (and `#`) picks the
   template's layout by name, case-insensitive. The generator fills the layout's title and body
   placeholders (body, or the first body-like placeholder) and places other content in the layout's
   content area as usual. An unknown name throws, listing the template's layout names. Valid on brand
   templates and custom templates; on the built-in master it throws (its five layouts are chosen
   automatically).
3. **Closing slide:** a last `#` heading with the attribute `{.closing}` becomes a slide on the
   closing layout: `PptxBrandTemplate.ClosingLayout` (name), else a layout named like *Closing* /
   *End* / *Thank you* (matched by name, documented), else the title layout. Its title and subtitle
   go into the layout's placeholders if it has them; a layout without placeholders gets nothing
   written but the slide itself. The built-in master gets a simple closing layout (logo centred on
   the brand gradient).
4. **Level-1 bullets:** `PptxOptions.ListBullets` (`Template` default, `Always`) and front matter
   `listBullets: template | always`. `Always` writes Markdown's bullets explicitly on every level of
   content slides, as flow slides do.
5. **Front matter keys** go through the template options' `Keys`, so the authoring guide (00008) lists
   them automatically; the guide also gets a short *Layouts of your template* paragraph when the
   options name a brand template (its layout names).
6. **Tests** on `brand-dark` (00010): footer text present, `showFooter: false`; `layout="…"` on a
   two-line-title layout; unknown layout error; closing slide on the named layout and by name
   matching; `listBullets: always` adds `a:buChar` on level 1.
7. **Docs:** PPTX page (front matter table, *Brand templates*), shared page (`PptxBrandTemplate`).

## Acceptance criteria

- [ ] Brand templates show `FooterText` in their footer placeholder; it can be switched off.
- [ ] A `##` with `layout="…"` uses that layout; typos throw with the valid names.
- [ ] `{.closing}` ends the deck on the closing layout.
- [ ] `listBullets: always` gives level 1 Markdown's bullets on templates that have none.
- [ ] Tests, docs and authoring guides updated.

## Breaking changes

- Decks on brand templates gain the footer text (it was missing). `showFooter: false` restores the
  old output.

## Open questions

1. Closing slide from an explicit `{.closing}` heading (proposed), or always added when the template
   has a closing layout?

> Answered by the human (2026-10-25): only from an explicit `{.closing}` heading.

2. Should `layout="…"` also be allowed on the built-in master (for its five layouts by name)?
   Proposed: no.

> Answered by the human (2026-10-25): no — it throws on the built-in master.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- Heading attributes gained `layout="…"` and `.closing`; `#` headings take `layout` and `.closing`
  (`.dark`/`kicker` there throw). A `##` whose only attribute is `layout` stays a plain text or
  visual slide, so the layout's body placeholder and its styles are used.
- `layout="…"` matches layout names case-insensitively and throws on the built-in master (answer to
  question 2).
- The closing layout: `PptxBrandTemplate.ClosingLayout` / `PptxLayoutNames.Closing` (front matter
  `layouts.closing` for caller templates), else a name starting with *Closing*, *End* or *Thank*,
  else the cover layout. The closing slide gets no footer and writes only into placeholders it has.
- `ShowFooter` / `showFooter` apply to a brand's own template; the built-in master keeps its own
  footer text as before.
- `PptxOptions.ListBullets` with front matter `listBullets` on every template (stored on the template
  options as an override, since front matter only reaches those).
- The deck guide lists a brand template's layout names for `layout="…"`.

### Deviations from the plan

- **The built-in master has no closing layout of its own:** its closing slide uses the section
  layout (brand gradient, logo for dark backgrounds), which is what the plan described.

### Breaking changes

- Brand-template decks gain their footer text. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
- `BrandTemplateFeatureTests` (11) on `brand-dark`.
