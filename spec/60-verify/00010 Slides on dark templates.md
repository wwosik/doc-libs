# 00010 Slides on dark templates

> Status: verify — implemented 2026-10-25

## Summary

Everything the PPTX generator draws stays readable on a brand template whose slides are dark —
alerts, panels, `==marked==` text, highlighted rows, status labels, card titles, figures,
statements, chips, code boxes and diagrams. And one brand can use different colours on slides and
on PDF pages, because a colour that suits white paper is often wrong on a dark slide.

## Context

- A brand may carry its own PowerPoint template (`Brand.Pptx`, `PptxBrandTemplate`). Corporate
  templates often have dark slides: a photographic or gradient background, with the title and body
  styles in white. The same `Brand` also makes PDFs, on white pages.
- Today `SlidePalette.From(ResolvedBrand)` assumes light slides: boxes in `TableBand`, code on
  `TableLine` at 30 %, highlights at 15 % of `Highlight`, status labels at 22 %, card titles and
  statements in `Primary`, chips white. On a dark template the template's white text then sits on
  those light fills and disappears; text drawn in `Primary` (a dark brand colour) disappears into
  the background. Only `{.dark}` flow slides and dark panels switch to white text.
- Diagrams (`DrawingWriter`) take the brand's diagram colours: light node fills (`Surface`),
  dark text — the same problem.
- `BrandColors` is one set for both generators. There is no way to say "on slides, use this green
  instead".
- `CustomTemplateOptions` (no brand) uses `SlidePalette.Scheme`: theme colour references, which
  follow the template's own colour map — dark templates that map `bg1` to a dark colour are already
  partly right there.

## Proposed approach

### 1. Slide colours separate from PDF colours

- New `Brand.SlideColors` (`BrandColors?`): colours for decks. Every property left `null` falls back
  to `Brand.Colors`, so a brand states only what differs (e.g. a lighter `Primary` and `Highlight`).
  PDFs never read it.
- Brand pack key `slideColors`, same shape as `colors`.
- `ResolvedBrand` keeps the PDF colours as today and gains `Slides` (a second resolved colour set),
  used by the built-in master, `SlidePalette` and the diagrams in decks.

### 2. Slide tone

- New `PptxBrandTemplate.Tone`: `Auto` (default), `Light`, `Dark`.
- `Auto` decides from the template: the colour of the master's title text style, through the colour
  map (light text means dark slides); failing that, the master's or the title-only layout's solid
  background. Photographic backgrounds are not inspected. The decision is logged nowhere — it is
  shown in the authoring guide (00008) and documented, and `Tone` overrides it.
- The built-in master is always `Light` (dark slides there are the `{.dark}` layout, as today).

### 3. A dark palette

`SlidePalette.ForDark(brand)`, used for every slide of a dark-toned template:

| Element | Light (today) | Dark |
| ------- | ------------- | ---- |
| Body text drawn by the generator | template's | white (`FFFFFF`) |
| Card titles, statements, headings in boxes | `Primary` | white |
| Figures, kickers | `Link` | `Highlight` of the slide colours |
| Boxes (panel, callout band, cards' fill) | `TableBand` | white at 10 % alpha (a translucent veil, so the photo shows through) |
| Box outlines, rules | `TableLine` | white at 35 % alpha |
| Dark panel | `Primary` | black at 35 % alpha |
| Code box | `TableLine` at 30 % | black at 30 % alpha, text white |
| Chips | white, coloured text | outline white at 60 %, text white |
| Callout bar | `Primary` / `Highlight` | `Highlight` / label `Warning` |
| `==marked==` text | bold on `Highlight` at 15 % | bold in `Highlight`, no fill |
| Highlighted rows | `Highlight` at 15 % | `Highlight` at 25 % alpha, text white |
| Status labels | label colour at 22 %, normal text | label colour at 45 % alpha, text white |
| Tables (built-in style) | style's | header in slide `Primary`, band rows white at 8 % alpha, text white |

Alpha fills are `a:alpha` on `a:srgbClr`, which PowerPoint, PowerPoint Online and LibreOffice draw.
Every pair of text colour and fill is checked for contrast (WCAG ratio ≥ 4.5 for text, against the
fill composited over both black and the slide colours' `Primary` as a stand-in for the photo); a
test fails if a palette breaks it.

### 4. Diagrams on dark slides

`DrawingWriter` takes a `DiagramColors` from the palette: on dark slides node fills are white at
12 % alpha, borders and lines white at 60 %, text white, the accent the slide `Highlight`. The
`Drawing` model does not change; the PDF path is untouched.

### 5. Test sample

A made-up dark brand template, `brand-dark` (no real company's design): a 16:9 `.pptx` written by
`tools/sample-generator`, with a dark gradient image as master background, white title and body
styles, a footer placeholder, and layouts for cover, title only, content, section, a two-line title
and a closing slide (the last three also serve 00012–00014). It joins `sample` and `neutral` in the
PPTX tests.

### 6. Tests, docs

- Palette tests: every element in the table above, light and dark; the contrast check.
- Tone: `Auto` on `brand-dark` (dark), on the built-in master (light), explicit `Tone` wins.
- `SlideColors`: decks use them, PDFs do not; brand pack round trip.
- `layouts.md` on `brand-dark`: round trip, validator, and the generated deck looked at in
  PowerPoint (`.tmp/review/`).
- Docs: *Slide colours* and *Dark templates* in `wwosik.DocLibs.DocumentGeneration.md` and the PPTX
  page; the authoring guide says nothing new to writers (colours are automatic).

## Acceptance criteria

- [ ] On `brand-dark`, every construct of `layouts.md` and a Mermaid diagram is readable: no text
      on a fill or background with a contrast ratio below 4.5.
- [ ] A brand with `SlideColors` uses them in decks and `Colors` in PDFs.
- [ ] Decks on the built-in master and on light templates are unchanged (expected files unchanged).
- [ ] `Tone` detection and override work; docs and tests updated; 0 warnings.

## Breaking changes

- None for light templates. Decks on templates that `Auto` detects as dark change colour (that is
  the point); `Tone = Light` restores today's look.

## Open questions

1. Translucent white veils (proposed) or solid colours derived from the slide colours for boxes on
   dark slides? Veils let the photo show through and need no extra brand colours; solid fills are
   more predictable on busy photos.

> Answered by the human (2026-10-25): translucent veils.

2. Detect the tone automatically (proposed, with `Tone` to override), or require the brand to state
   it?

> Answered by the human (2026-10-25): detect automatically, with `Tone` to override.

3. Should `SlideColors` also apply to the built-in master, or only to brand templates? Proposed:
   both — "colours for slides" means every deck.

> Answered by the human (2026-10-25): both — the built-in master and brand templates.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- `SlideColor` gained `Alpha`; `SlidePalette.ForDark` sets `DarkSlides`, the text colour, the callout
  bars, the marked-text colour and the table veils. Every flow slide on a dark template takes the
  dark style, so the existing dark paths (chips, cards, panels) serve both.
- The generator now opens the template **before** parsing: the tone decides which brand the
  code-block renderers get (`ResolvedBrand.DarkSlideBrand` on dark slides).
- `SlideColors` merge over `Colors` per property (`ResolvedBrand.SlideBrand`, cached per brand); the
  built-in master uses them too (answer to question 3).

### Deviations from the plan

- **`brand-dark` is built at test time** (`DarkTemplate` in the PPTX tests) from the built-in master
  with a gradient background, white text styles, a footer placeholder and two copied layouts —
  not written by `tools/sample-generator` as a binary sample.
- **Table header on dark slides:** a black veil at 40 %, not the slide primary — a light primary
  would leave white header text unreadable. Every other cell gets an explicit transparent fill,
  because the table style's light band would otherwise show under white text.
- **Contrast check** uses two dark backdrops (black and a dark slate) as stand-ins for a dark photo,
  not the slide primary, which can be light.
- Detection reads the master's title text colour, else its solid background; images and gradients
  are not looked at (documented; `Tone` overrides).

### Breaking changes

- Decks on brand templates detected as dark change colour. In `CHANGELOG.md`.

### Verified

- All tests pass: shared 78, PPTX 170, PDF 114, Diagrams 67, Charts 15, conversions 104
  (WindowsOnly 6 skipped on Linux); Release build 0 warnings.
- A local acceptance run with two real-world decks of a consumer (kept outside the repo) on a dark
  brand template generated without errors, nothing ran off a slide, and every text was readable in
  PowerPoint. The template used was the made-up `brand-dark`; the consumer's real corporate template
  was not available.
- `dark-everything.pptx` and `layouts-brand-dark.pptx` were exported from PowerPoint and looked at.

### To verify by hand

- Generate on your real dark corporate template and check that `Auto` detects it; if not, set
  `PptxBrandTemplate.Tone = SlideTone.Dark` (or `pptx.tone: "dark"` in the brand pack).
