## Writing Markdown for a PDF

The PDF is generated from Markdown: the design (colours, fonts, logo, header and footer, page
numbers) is applied for you. Write it as a document to read: full sentences and paragraphs,
headings that say what a section is about, lists where items really are parallel.

## Structure

| Markdown | Becomes |
| -------- | ------- |
| A `#` heading as the very first line | The cover page. The paragraphs below it, up to the next heading, are the subtitle. Without it, there is no cover. With `layout: plain`, the title at the top of page 1 (below). |
| Every other `#` heading | A heading that starts a new page. |
| `##` to `######` | Headings, in the table of contents (bookmarks), kept on a page with what follows them. |
| `---` | A thin line. |

- Paragraphs are left-aligned; a line break is `\` at the end of a line.
- Lists nest with indentation; `1.` lists are numbered from their first number.
- `**bold**`, `*italic*`, `~~strike~~`, `` `code` `` and links work. A link needs an absolute URL
  (`https://…`) or a heading anchor (`[see costs](#costs)`); anything else stays text.
- `[words]{.success}` sets a few words bold in a status colour: `.success`, `.info`, `.warning`,
  `.danger` or `.neutral`.
- A quote (`>`) is indented and grey. A code block is set in a monospace font on a grey
  background, long lines wrapped.
- **Tables** span the page width; columns get the room their content needs, alignment follows the
  `:--`/`--:` markers. Long tables continue on the next page with the header row repeated. Keep
  them to about six columns.
- **Images** (PNG or JPEG) are referenced by the path given to you, keep their size, are only
  scaled down to fit and never split; `![Chart](chart.png "scale=0.5")` halves the size. Several
  images in one paragraph stand side by side, equally wide. Other words in the title are its
  caption (below).
- **Image attributes** in braces right after an image, without a space:
  `![Team](team.jpg){.round .grey}`. `.round` crops it to a circle, `.grey` shows it in grey.
  In a row of images, `fit=cover` fills the row's height
  and crops what sticks out, and `focus=top`, `bottom`, `left`, `right` or `center` (the default)
  says which part stays; an image of its own is never cropped. An unknown attribute is an error.
- **QR codes:** `![Survey](qr:https://example.com/survey)` draws a QR code for the text after
  `qr:`, taken literally (a web address, `mailto:…`). `"caption"` in the title or
  `{.caption}` puts the alt text under it, `scale=2` doubles it; no other attributes. Shorter
  addresses scan better.
- **Footnotes:** `[^1]` in the text and `[^1]: The note.` on a line of its own anywhere below.
  The reference is a small raised number linked to its note at the foot of its page, numbered
  through the document; a long note continues on the next page.
- **Task lists:** `- [ ] open` and `- [x] done` get an empty or ticked box instead of a bullet.
- **Definition lists:** a term on a line of its own, then `:   ` and its description on the next
  line. The term is bold, the description indented.
- HTML is printed as text: do not use it. HTML comments are dropped.

## Contents, numbers and captions

- `toc: true` in the front matter puts a table of contents on a page after the cover: the headings
  of `tocDepth` levels (default 2) from the highest level after the cover, each with its page
  number. In a plain document it follows the title.
- `headingNumbers: true` numbers three heading levels from the highest one after the cover: `1`,
  `1.1`, `1.1.1`, also in the bookmarks and the contents. A skipped level does not count: a `###`
  right under a `#` is `1.1`. `{.unnumbered}` at the end of any heading gives it no number.
- **Captions** are numbered through the document, figures and tables apart, as "Figure 1: …" and
  "Table 1: …" in the document's language (`lang`):
  - an image's title, besides `scale=`, is its caption: `![Sales](s.png "Sales by region")`;
    `"caption"` alone uses the alt text;
  - a paragraph `Table: …` after a table, after a blank line, goes above the table;
  - a paragraph `Figure: …` right after a diagram block that is drawn goes below it.

  `Table:` and `Figure:` stay English in every language. Anywhere else such a paragraph is text.

```markdown
| Region | Sales |
| ------ | ----: |
| North  | 120   |

Table: Sales by region, 2026
```

## Justified text and page breaks

- `justify: true` in the front matter justifies body text and footnotes, without hyphenation;
  very loose lines stay ragged. Headings, tables and code never are.
- `::: pagebreak` alone on a line starts a new page with what follows; its closing `:::` is
  optional. `{.newpage}` at the end of any heading but the cover's starts a new page with it. A
  break never makes an empty page.

## Plain documents and columns

- `layout: plain` in the front matter is for short business documents such as invoices and
  letters: no cover and no header. A leading `#` is the title at the top of page 1, with the logo
  beside it, and the text follows right below it. The footer and page numbers stay.
- `tone: dark` in the front matter gives dark pages with light text, for reading on screen; it
  prints dark too, so leave it out for documents meant for paper.
- `::: columns` sets two or three parts side by side, equally wide or as `::: columns 1:2` says;
  a `---` after a blank line separates them. Columns that do not fit on the page move to the next
  one together, so keep them short: paragraphs, lists, small tables.

```markdown
---
layout: plain
---

# Invoice 2026-114

::: columns
**Seller**\
Example Ltd, 1 High Street

---

**Buyer**\
Client Ltd, 5 Market Square
:::

| Item | Amount |
| ---- | -----: |
| Consulting, October | 4,800.00 |
```

## Syntax written for slides

Markdown written for decks works here too and is shown plainly, so one text can serve both:
`## Title {kicker="…"}` drops the attributes, `::: cards`, `rows`, `panel` and `statement` show
their content as text, `::: columns` sets its parts side by side (above), `==marked==` text is bold, and an alert (`> [!NOTE]`, `[!TIP]`,
`[!IMPORTANT]`, `[!WARNING]`, `[!CAUTION]`) is a quote that starts with its title in bold, in the
document's language (`lang`). Speaker notes (`::: notes` … `:::`), transitions (`transition:`
in the front matter, `transition="…"` on a heading) and builds (a paragraph such as `{.build}`,
`{.frag}`, `{.exit}` or `{.emphasize}` before a block, and `build:` in the front matter) are left
out. `::: scene`: one still picture. Unknown containers,
attributes, alerts, transitions and build markers (a paragraph such as `{.note}`) are errors, as in
decks, and so are a `::: notes` inside another block and a build marker that builds nothing. Write
`\{` to show braces as text.

The kinds of slide `{.agenda}`, `{.quote}`, `{.statement}`, `{.contact}` and `{.chart}` show their
content as written; an `{.agenda}` with nothing under it lists the document's `#` chapters.
`::: conclusion` … `:::` is shown like an alert without a title, `::: comment` … `:::` after the
block it comments as an italic quote. Their mistakes are errors as in decks: a kind with other
content than it takes, a comment as the first block, a conclusion that is not the last.

**Timelines and Gantt charts:** `::: timeline` around a list of 2–8 `- **date** Title: description`;
`::: gantt` around a table of 1–20 rows of task, start and end (no end: a milestone). Dates:
`2026`, `2026-Q2`, `2026-03`, `2026-03-15`; what ended before today is grey. `today=2026-11-01`
after the name pins today; `grid=week|month|quarter` sets a Gantt's grid.

````markdown
## Plan

::: timeline
- **2026-01** Start
- **2026-06** Go-live: all regions
:::

::: gantt grid=month
| Task | Start | End |
| ---- | ----- | --- |
| Build | 2026-02 | 2026-05 |
| Launch | 2026-06 | |
:::
````

**Steps, Harvey balls and risk matrices:** `::: steps` around a list of 2–7 `- **Title** –
description` draws them as chevrons in a row; `{.current}` at the end of one item marks it, the
steps before it done, later ones outlined. A table cell that is only `[0/4]` to `[4/4]`, or
`[1/2]`, is a Harvey ball, a circle filled by quarters; `\[3/4]` keeps the text. `::: riskmatrix`
around a table of 1–15 rows of risk, likelihood and impact (each 1–5) puts numbered dots on a
5 × 5 grid, likelihood up, impact right, with a legend; cells are tinted by likelihood × impact:
1–4 in the success, 5–9 in the warning, 10–25 in the danger colour.

````markdown
## Programme

::: steps
- **Analyse** – understand the current process
- **Design** – agree the target picture {.current}
- **Build**
:::

## Risks

::: riskmatrix
| Risk | Likelihood | Impact |
| ---- | ---------- | ------ |
| Supplier delay | 4 | 3 |
| Key person leaves | 2 | 5 |
:::

## Options

| Option | Cost | Speed |
| ------ | :---: | :---: |
| Buy | [1/4] | [4/4] |
| Build | [3/4] | [1/2] |
````

## Icons

`:icon-name:` draws the icon of that name of [Tabler Icons](https://tabler.io/icons) (3.52.0, outline), such as `:icon-circle-check:`. A PDF draws it inside text, one letter wide in the text's colour; at the start of a list item, in place of its bullet; and alone in a paragraph, large. In headings, table cells and on the cover it is left out. An unknown name is an error.

Common: `check`, `x`, `star`, `heart`, `bolt`, `rocket`, `target`, `flag`, `bulb`, `lock`, `users`, `calendar`, `clock`, `mail`, `chart-bar`, `trending-up`, `cloud`, `search`, `settings`, `world`.

## Diagrams

A ` ```mermaid ` code block becomes a diagram in the document's colours; in a Word document, a
picture. Use one where a picture says more than bullets: a process, a sequence of calls, a data
model, a plan. Do not add diagrams by default.

````markdown
```mermaid
flowchart LR
    A[Request] --> B{Valid?}
    B -->|yes| C[Process]
    B -->|no| D[Reject]
```
````

- **Drawn:** `flowchart` (or `graph`, subgraphs included), `sequenceDiagram`, `classDiagram`,
  `stateDiagram-v2`, `erDiagram`, `pie`, `gantt`, `mindmap`.
- **Not drawn:** every other type (`journey`, `timeline`, `gitGraph`, C4, `xychart`, …) stays a
  code block. Do not use them.
- Keep diagrams small: up to about 12 nodes, short labels. A wide diagram is scaled down to fit,
  text included, and becomes hard to read.
- Mermaid's `click` links, icons and `classDef` styling are not drawn; colours come from the
  design.
- A diagram with a syntax error is an error naming the line of its block; fix the Mermaid and try again.

## Versions

One Markdown holds three versions, picked by `audience:`: `customer`, `internal`, which adds what is marked
internal, and `trainer`, which adds solutions too.

- `::: internal` … `:::`, a line `{.internal}` before a block, or `{.internal}` on a `#` or `##` heading (the
  whole chapter or slide) mark what is internal.
- `::: exercise` shows to everyone as "Exercise 1"; a `::: solution` right after it, as "Solution 1", to the
  trainer only.

Versions but `customer` are marked internal unless `classification` is set.

## Front matter

Settings go in a block at the very top, before anything else. One `key: value` per line, no
nesting and no lists. Leave out what the defaults already do: every key is optional.

```markdown
---
title: Network expansion 2027
author: Planning team
---
```

| Key | Values |
| --- | ------ |
| `title` | The document title in the file's properties. Default: the first `#` heading. |
| `author` | The author in the file's properties. |
| `lang` | The document language, such as `en`, `de` or `de-CH`: of the texts the generator writes itself, and of spell checking. Default `en`. |
| `typography` | `true` sets typographic quotes, dashes, `…` and no-break spaces (`10 km`) for the language; type straight quotes. Default `false`. |
| `highlight` | `true` colours fenced code in these languages: `csharp`, `vb`, `fsharp`, `cpp`, `java`, `javascript`, `typescript`, `php`, `python`, `powershell`, `sql`, `html`, `xml`, `css`, `markdown`, `haskell`, `json`, `yaml`, `bash`. Default `false`. |
| `template` | The design: `neutral` or `sample`. |
| `showDate` | `true` or `false`: today's date in the footer. Default `true`. |
| `showTagline` | `true` or `false`: the design's tagline on the cover, if it has one. Default `true`. |
| `tone` | `light` or `dark`: `dark` paints every page in the design's dark background with light text, diagrams and charts; it prints dark too. Default `light`. |
| `pageSize` | `A4`, `A3` or `Letter`. Default `A4`. |
| `orientation` | `portrait` or `landscape`. Default `portrait`. |
| `layout` | `report` or `plain`: `plain` has no cover and no header, for a short business document. Default `report`. |
| `toc` | `true` or `false`: a table of contents after the cover, with page numbers. Default `false`. |
| `tocDepth` | `1`, `2` or `3`: the heading levels the table of contents lists. Default `2`. |
| `headingNumbers` | `true` or `false`: numbers on three heading levels (1, 1.1, 1.1.1). Default `false`. |
| `justify` | `true` or `false`: body text justified, without hyphenation. Default `false`. |
| `classification` | `public`, `internal` or `confidential`: a marking in the header and on the cover. Default `public`. |
| `draft` | `true` adds "Draft" to the marking and a diagonal watermark. Default `false`. |
| `transition`, `build`, `numberChapters` | Settings for decks. A PDF accepts them and ignores them. |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | Status labels for decks, separated by `;`. A PDF accepts them and shows the labels as code. |
| `audience` | `customer`, `internal` or `trainer`: the version, see *Versions*. Default `customer`. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

## Characters

The fonts are embedded in the PDF and have no emoji and no Asian scripts; never use emoji. Any character the fonts lack stops the generation with an error naming it. Write these symbols as shown:

| Avoid | Write instead |
| ----- | ------------- |
| ✓ (fine in code) | `[x]` |
| ✔ (fine in code) | `[x]` |
| ✗ (fine in code) | `[ ]` |
| ✘ (fine in code) | `[ ]` |
| ☐ | `[ ]` |
| ☑ | `[x]` |
| ● (fine in code) | `•` |
| ○ (fine in code) | `•` |
| ■ | `•` |
| □ | `•` |
| ▪ | `•` |
| ◦ | `–` |
| ★ | `*` |
| ☆ | `*` |
| → (fine in code) | `->` |
| ← (fine in code) | `<-` |
| ↔ (fine in code) | `<->` |
| ↑ (fine in code) | `up` |
| ↓ (fine in code) | `down` |
| ▲ | `up` |
| ▼ | `down` |
| ► | `->` |
| ◄ | `<-` |
| ⇒ | `->` |
| ⇐ | `<-` |
| ⇔ | `<->` |
| ≡ | `=` |
| ‖ | `\|` |
| ─ (fine in code) | `-` |
| │ (fine in code) | `\|` |

## Limits

A check before generating reports what goes beyond these: at most 150 words in a paragraph; at most 60 characters in a heading; alt text for every image. Keep to them from the start.

## When something is wrong

Generation stops with a message that says what to fix, such as a character the fonts lack or an
image that is not among the files. Fix the Markdown and generate again.

## Notes you may get back

The document is still written, but whoever passes it to you may also pass on notes about it, each with its line. Each names what happened:

- `code-not-drawn`: a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type.
- `html-as-text`: HTML is not interpreted: it is shown as text. Write Markdown instead.
- `footnote-missing`: a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference.
- `footnote-split`: a footnote was too long for the foot of its page and continues at the foot of the next. Shorten it, or move the detail into the text.
- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.
- `attribute-ignored`: an image attribute has no effect here, for example `fit=cover` on an image of its own in a PDF, or `focus` on an image that is not cropped. Remove it, or use it where it works.
- `icon-not-drawn`: an icon was left out where it cannot be drawn. Move it as the *Icons* section says.
- `agenda-empty`: an empty `{.agenda}` found no `#` chapters to list. Add the chapters, or write the agenda as a list.
- `caption-ignored`: a `Table: …` or `Figure: …` paragraph does not directly follow a table or a drawn diagram, so it is shown as text. Move it right after its table, or write it as ordinary text.
- `toc-empty`: `toc: true` asked for a table of contents, but the document has no headings at the depth it lists, so there is none. Add headings, raise `tocDepth`, or leave `toc` out.
- `typography-skipped`: with `typography: true`, a character the fonts lack was kept as typed. Nothing to fix.
- `code-language-unknown`: with `highlight: true`, a code block's language is unknown. Check the spelling, or write `text`.
- `audience-omitted`: this version left out parts marked for other versions (*Versions*). Nothing to fix.

A check before generating writes nothing and returns these notes too, and also:

- `long-paragraph`: a paragraph has more words than *Limits* allows. Split it, or make a list of it.
- `title-too-long`: a title or heading is longer than *Limits* allows. Shorten it; the detail goes in the text.
- `alt-text-missing`: an image has no alt text. Say what it shows: `![Sales by region](sales.png)`.
- `image-source`: an image is not among the files given to you, or is a web address or `data:` URL, which is never loaded. Use only the paths you were given.

## Example

````markdown
---
title: Network expansion 2027
---

# Network expansion 2027

Status report for the steering committee, October

## Summary

The upgrade is on schedule: 14 of 20 substations are done. Two sites are delayed by permits, and
the budget is on track.

## Progress

| Site group | Done | Open |
| ---------- | ---: | ---: |
| North | 8 | 1 |
| South | 6 | 5 |

> [!WARNING]
> The permits for the last two sites are still missing. Without them, commissioning moves to Q3.

## Next steps

1. Obtain the remaining permits (Q1).
2. Commission the last sites (Q2).
````
