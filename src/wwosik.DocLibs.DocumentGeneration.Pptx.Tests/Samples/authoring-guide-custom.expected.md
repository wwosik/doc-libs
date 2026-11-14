## Writing Markdown for a deck

The deck is generated from Markdown: headings become slides, and the design (colours, fonts,
logo, footer) is applied for you. Write content, not formatting. A slide is read in seconds:
a title that states the point, then a few short lines.

## Structure

| Markdown | Becomes |
| -------- | ------- |
| The first `#` heading | The cover. The text below it, up to the next heading, is the subtitle. |
| Every later `#` heading | A section divider, with the text below it as subtitle. |
| `##` heading | One slide with that title. |
| `###` heading inside a slide | A bold line (in `::: cards`: a card's title). |
| Text before the first heading, `---`, HTML comments | Nothing: they are dropped. |

- **Keep slides short:** at most about six bullets of one line each, or one table, image or
  diagram with a sentence. What does not fit is set smaller, and what still does not fit continues
  on a further slide with the same title; long tables repeat their header row. Better: split long
  content yourself, into slides whose titles say what each one shows.
- **A table, an image or a diagram gets a slide of its own,** titled like its section; text before
  it goes above, text after it below as a caption. A second one in the same section gets a further
  slide with the same title. Slides that use [layouts](#layouts) keep several on one slide.
- Lists nest with indentation; `1.` lists are numbered. `**bold**`, `*italic*`, `~~strike~~`,
  `` `code` `` and links work; a link needs an absolute URL (`https://…`), others stay text. A
  quote (`>`) is italic. A code block is set in a monospace font.
- **Tables:** up to about six columns and eight rows; column widths follow the content, alignment
  follows the `:--`/`--:` markers. A row whose first cell is `==marked==` is highlighted.
- **Images** (PNG, JPEG, GIF) are referenced by the path given to you and fill the room they have,
  keeping their shape; `![Chart](chart.png "scale=1")` stops one from growing beyond its own size.
  **Several images in one paragraph** (`![Orders](orders.png) ![Sales](sales.png)`) stand side by
  side on one slide, equally wide: the way to show two charts together. `"caption"` in an image's
  title (`![Orders by month](orders.png "caption")`) puts its alt text under it.
- **Image attributes** in braces right after an image, without a space:
  `![Team](team.jpg){fit=cover focus=top .round .grey}`. `fit=cover` fills the image's box (the
  slide's room, or its cell in a row) and crops what sticks out; `focus=top`, `bottom`, `left`,
  `right` or `center` (the default) says which part stays. `.round` crops it to a circle, `.grey`
  shows it in grey, `.caption` and `scale=1` work as in the title. An unknown
  attribute is an error.
- **QR codes:** `![Survey](qr:https://example.com)` draws a QR code of the text after `qr:`.
- `==text==` marks text like a highlighter pen.
- `[words]{.success}` sets a few words bold in a status colour: `.success`, `.info`, `.warning`,
  `.danger` or `.neutral` — for a verdict inside a sentence, such as `[on schedule]{.success}`.
- **Footnotes:** `[^1]` in the text and `[^1]: The source.` on a line of its own anywhere below.
  The reference becomes a raised number, the note small text at the foot of its slide. Use them for
  sources. A reference without its definition stays as written.
- **Task lists:** `- [ ] open` and `- [x] done` get an empty or ticked box instead of a bullet.
- **Definition lists:** a term on a line of its own, then `:   ` and its description on the next
  line. They are shown like `::: rows`: the term in bold on the left, its description beside it.

## Layouts

A `##` slide can ask for more than bullets. It then stacks its blocks top to bottom, and several
tables or diagrams share the slide. Use layouts where they help the point; a plain bullet slide
is fine.

| Markdown | On the slide |
| -------- | ------------ |
| `## Title {kicker="Label"}` | A small label above the title: the chapter or topic. |
| `## Title {.dark}` | The slide on a dark background; both: `{.dark kicker="Label"}`. Use it for one key message, not for many slides. |
| `# Thank you {.closing}` | The last slide, on the design's closing layout. |
| `::: cards` … `:::` with one `###` per card | Two to four boxes side by side: parallel points, options, figures. A line that is only `**bold**` right after the `###` is a big figure. |
| `::: cards` around one list of `- **Title** – text` | The same, one card per item. |
| `::: rows` around one list of `- **Term** – text` | Term on the left, explanation on the right, with rules: benefits, definitions. |
| `::: panel` (or `::: panel dark`) … `:::` | A box behind its content, light or dark. |
| `::: statement` … `:::` | Its first paragraph large and bold: the one sentence to remember. |
| `::: columns` … `---` … `:::` | Two or three columns side by side, split by `---` lines (with a blank line before each): text beside a chart. `::: columns 1:2` makes the second twice as wide. |
| `> [!NOTE]` or `> [!TIP]` | A callout with a bar in the main colour. |
| `> [!IMPORTANT]`, `> [!WARNING]` or `> [!CAUTION]` | A callout with a bar in the highlight colour. |
| A line of only code spans: `` `Orders` `Invoices` `Stock` `` | Tags, as chips. At least two. |
| In a container, `` - `write-once` text `` | The code span as a bold label, the text beside it. |
| A table cell that is one code span: `` `Done` ``, on a layout slide | A status label; front matter `labels.success: Done` colours it. Categories: `success`, `info`, `warning`, `danger`, `neutral`; labels no key names are neutral. |
| `::: conclusion` … `:::`, the slide's last block | A band at the foot of the slide: the takeaway. |
| `::: comment` … `:::`, right after a block | A callout over that block's top right. |

````markdown
## Why rebuild? {kicker="Motivation"}

::: cards
### Configuration is too complicated
**26 tables**

for 12 kinds of rule.

### Large plans are too slow
**3.2 GB**

of memory per plan.
:::

> [!NOTE]
> The old system stays in place until the shadow run is done.
````

- **Nesting:** a longer fence holds a shorter one, `:::: panel` around `::: cards` … `:::` and
  `::::`.
- **Rules:** attributes `{…}` on `##` headings, and on `#` only `.closing`, `layout` and `transition`; in `::: cards`
  nothing before the first `###`;
  `::: rows` holds exactly one list; every item of a `cards` or `rows` list starts with
  `**bold**`.
- Layout slides that do not fit are set smaller, then continued on a further slide between their
  blocks; cards, a panel or columns stay together. Three or four cards with one or two lines each
  fill a slide.

**Kinds of slide:** a class on a `##` heading puts the slide on the design's layout for it.

| Markdown | On the slide |
| -------- | ------------ |
| `## Agenda {.agenda}` with a list | The list, numbered. With nothing under the heading: the deck's `#` chapters. |
| `## {.quote}` with `> quote` and a last line `— Name, role` | The quote, with the line as its byline. A heading shows only where the design has a title for it. |
| `## One message {.statement}`, text below if you like | The heading as one large statement, the text under it. |
| `## Team {.contact}` with a list, one item per person | Per item an image as the photo, the **bold** name, the other lines below; five a slide. |
| `## Revenue {.chart}` with one or two `chart` blocks, text if you like | The charts with their title, unit and source in the design's places; two a slide. |

A kind excludes the other kinds and `.dark`, goes with `kicker` and `layout`, and holds only what
the table says.

````markdown
## Agenda {.agenda}

## {.quote}

> We ship when the numbers agree.
> — Ann Example, Chief Engineer

## Team {.contact}

- **Ann Example** – Chief Engineer
  ann@example.com
- **Ben Example** – Project lead

## Where we stand

- 14 of 20 substations upgraded

::: comment
Two more by June.
:::

::: conclusion
The end date holds.
:::
````

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

## Presenting

**Speaker notes:** `::: notes` … `:::` under a heading holds what the presenter says. It is not
shown on the slide but in PowerPoint's notes pane, on every slide the section produces.

````markdown
## Where we stand

- 14 of 20 substations upgraded
- Two delays, both in permitting

::: notes
Start with the **good news**, then the delays:

- both are in permitting
- neither moves the end date
:::
````

- Speaker notes hold paragraphs and lists, with bold, italic, code and links. A table, code block,
  image or quote in them is written as plain text.
- Put `::: notes` at the top level of a section, after its heading: not inside another container,
  a list or a quote.

**Transitions:** how a slide is brought on when the deck is presented. `transition: fade` in the
front matter sets it for every slide; `transition="…"` on a heading (`## Results {transition="push"}`,
also with other attributes) for that slide alone, and `transition="none"` turns it off there. The
transitions: `none`, `fade`, `push`, `wipe`, `split`, `cover`, `uncover`, `zoom`, `cut` and
`morph`. `morph` moves the shapes that two
slides share from one to the other; viewers without it fade. Use one transition for the whole
deck, and a different one only where it means something.

**Builds:** a paragraph that is only a build marker, before a block, makes that block come in click
by click in the slide show. Leave a blank line after it.

````markdown
{.build}

- 14 of 20 substations upgraded
- Two delays, both in permitting

{.frag .exit}

The end date does not move.
````

| Marker | On a click |
| ------ | ---------- |
| `{.build}` | appears item by item: list items (with their sub-items), cards, rows, columns, images in a row; other blocks whole |
| `{.build=fly}`, `{.build=wipe}` | the same, flying in or wiped in (`=appear` is `{.build}`) |
| `{.frag}` | the whole block appears |
| `{.exit}` | the block disappears |
| `{.emphasize}` | the block pulses |

- `{.frag .exit}` and other combinations play one after the other. Clicks follow the Markdown.
- Put a marker at the top level of a section, or inside a `:::` container or alert for what follows
  it there; before a container it builds the whole container. Not before a heading, `::: notes`,
  another marker or nothing, and not in a list, quote or speaker notes.
- `build: lists` in the front matter builds every top-level list like `{.build}`, unless it has its
  own marker.
- Other braces alone in a paragraph, such as `{.note}`, are a mistake; `\{` keeps them as text.

Page breaks are for documents: a deck shows the content of `::: pagebreak` … `:::` as usual and
ignores `{.newpage}` on a heading, as it ignores `classification:` and `draft:` for now.

**Kinds of slide** are drawn as layout slides with the same content, unless the template has a layout of the right type.

**Layouts with several boxes.** On a `layout="…"` slide, lines `---` (each after a blank line) cut the text into parts, which fill the layout's text boxes in order; images fill its picture boxes, cropped to their shape, and charts its chart boxes. What has no box left continues on a further slide. Such a slide takes text, images, charts, tables and diagrams, but no `:::` containers, alerts, `.dark` or `kicker`.

````text
## Two offers {layout="Two Content"}

- Basic: 10 users

---

- Premium: unlimited
````

## Icons

`:icon-name:` draws the icon of that name of [Tabler Icons](https://tabler.io/icons) (3.52.0, outline), such as `:icon-circle-check:`. Slides show it only alone after a card's `###` (as its figure) and at the start of every item of a list (as bullets); elsewhere it is left out. An unknown name is an error.

Common: `check`, `x`, `star`, `heart`, `bolt`, `rocket`, `target`, `flag`, `bulb`, `lock`, `users`, `calendar`, `clock`, `mail`, `chart-bar`, `trending-up`, `cloud`, `search`, `settings`, `world`.

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
| `template` | The design: `neutral`. |
| `layouts.title` | The name of the template's layout for the cover. |
| `layouts.content` | The name of the template's layout for text slides. |
| `layouts.titleOnly` | The name of the template's layout for table, image and layout slides. |
| `layouts.sectionHeader` | The name of the template's layout for section dividers. |
| `layouts.closing` | The name of the template's layout for the closing slide. |
| `layouts.twoContent` | The name of the template's layout with two columns. |
| `layouts.textAndPicture` | The name of the template's layout for text beside a picture. |
| `layouts.picture` | The name of the template's layout for one picture. |
| `layouts.agenda` | The name of the template's layout for the agenda. |
| `layouts.quote` | The name of the template's layout for a quote. |
| `layouts.statement` | The name of the template's layout for one large statement. |
| `layouts.contact` | The name of the template's layout for contact people. |
| `layouts.chart` | The name of the template's layout for one chart. |
| `layouts.twoCharts` | The name of the template's layout for two charts. |
| `layouts.dark` | The name of the template's layout for `{.dark}` slides. |
| `numberChapters` | `true` or `false`: "01", "02", … on the section dividers after the cover. Default: as the design says. |
| `listBullets` | `always` to show bullets on every list level, also where the design's first level has none; `template` (the default) leaves it to the design. |
| `transition` | How every slide is brought on when presented: `none`, `fade`, `push`, `wipe`, `split`, `cover`, `uncover`, `zoom`, `cut`, `morph`. Default `none`. A heading's `transition="…"` wins; see [Presenting](#presenting). |
| `build` | `lists` builds every top-level list item by item; `none` (the default) only what has a marker. See [Presenting](#presenting). |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | The [status labels](#layouts) in that category, separated by `;`: `labels.success: Done; Delivered`. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

## Characters

Each character is shown in the deck's font, or in a substitute where that font lacks it. Prefer plain characters (`•`, `–`, `→`, typographic quotes) to symbols, and leave out emoji.

## When something is wrong

Generation stops with a message that says what to fix: an unknown container (`::: grid`), heading
attribute (`{.wide}`), alert (`[!INFO]`), transition or build marker (`{.note}`), a marker that
builds nothing, a `::: notes` inside another block, a
`cards` or `rows` item without a bold start, a kind of slide with content it does not take, a
`::: comment` first or a `::: conclusion` not last, an
image that is not among the files, a front matter key that does not exist, or a broken diagram
with its line. Fix the Markdown and generate again.

## Notes you may get back

The document is still written, but whoever passes it to you may also pass on notes about it, each with its line. Each names what happened:

- `slide-split`: the slide had too much content and continues on further slides. Shorten it, or split it into slides of your own with their own titles.
- `slide-shrunk`: the slide's text was set at the smallest size to fit. Shorten it.
- `slide-overfull`: one item, such as a long list entry or a table with very long cells, does not fit even on a slide of its own. Shorten it.
- `image-enlarged`: the image is small and was enlarged; it may look blurred. Use a larger image, or `"scale=1"` in its title to keep it at its size.
- `image-cropped`: an image was cut to fill a picture placeholder of the layout, and much of it is not shown. Use an image of the placeholder's shape, or `focus=` to say which part stays.
- `code-not-drawn`: a diagram could not be drawn, for example a diagram type that is not supported, and stays code. Use a supported type.
- `html-as-text`: HTML is not interpreted: it is shown as text. Write Markdown instead.
- `footnote-missing`: a footnote reference such as `[^1]` has no definition `[^1]: text`, so it is shown as written. Add the definition, or remove the reference.
- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.
- `notes-dropped`: a `::: notes` stands before the first heading, so it belongs to no slide and was left out. Move it under the heading of the slide it is for.
- `notes-simplified`: speaker notes hold only paragraphs and lists; a table, code block, image or quote in `::: notes` was written as plain text. Keep speaker notes to text and lists.
- `attribute-ignored`: an image attribute has no effect here, for example `fit=cover` on an image of its own in a PDF, or `focus` on an image that is not cropped. Remove it, or use it where it works.
- `icon-not-drawn`: an icon was left out where it cannot be drawn. Move it as the *Icons* section says.
- `layout-fallback`: the design has no layout for that kind of slide (`{.quote}` and the others), so it is drawn as a layout slide with the same content. Nothing to fix, unless the design's own look matters.
- `agenda-empty`: an empty `{.agenda}` found no `#` chapters to list. Add the chapters, or write the agenda as a list.

## Example

````markdown
---
title: Network expansion 2027
labels.warning: Next
labels.neutral: Later
---

# Network expansion 2027

Status and next steps

## Where we stand {kicker="Status"}

- 14 of 20 substations upgraded
- Two delays, both in permitting
- Budget on track

## What it brings {kicker="Benefits"}

::: rows
- **More capacity** – 30 % more load at peak times
- **Fewer outages** – faults are isolated automatically
:::

## Next steps {kicker="Plan"}

| Step | When | Status |
| ---- | ---- | ------ |
| Permits for the last sites | Q1 | `Next` |
| Commissioning | Q2 | `Later` |

## One message {.dark}

::: statement
The grid is ready for twice the solar power by 2027.
:::
````
