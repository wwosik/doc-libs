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
- `==text==` marks text like a highlighter pen.
- `[words]{.success}` sets a few words bold in a status colour: `.success`, `.info`, `.warning`,
  `.danger` or `.neutral` — for a verdict inside a sentence, such as `[on schedule]{.success}`.

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
- **Rules:** attributes `{…}` on `##` headings, and on `#` only `.closing` and `layout`; in `::: cards`
  nothing before the first `###`;
  `::: rows` holds exactly one list; every item of a `cards` or `rows` list starts with
  `**bold**`.
- Layout slides that do not fit are set smaller, then continued on a further slide between their
  blocks; cards, a panel or columns stay together. Three or four cards with one or two lines each
  fill a slide.

## Diagrams

A ` ```mermaid ` code block becomes a diagram in the document's colours. Use one where a picture
says more than bullets: a process, a sequence of calls, a data model, a plan. Do not add diagrams
by default.

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
| `template` | The design: `neutral` or `sample`. |
| `showDate` | `true` or `false`: today's date in the footer. Default `true`. |
| `showTagline` | `true` or `false`: the design's tagline on the cover, if it has one. Default `true`. |
| `embedFonts` | `true` or `false`. Leave it out: by default the deck carries its fonts. |
| `listBullets` | `always` to show bullets on every list level, also where the design's first level has none; `template` (the default) leaves it to the design. |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | The [status labels](#layouts) in that category, separated by `;`: `labels.success: Done; Delivered`. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

## Characters

The deck is set in Liberation Serif, with PT Sans Narrow in tables and Inconsolata for code. A character these fonts lack is shown in a substitute font and looks out of place. Leave out emoji, and write these symbols as shown:

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

## When something is wrong

Generation stops with a message that says what to fix: an unknown container (`::: grid`), heading
attribute (`{.wide}`) or alert (`[!INFO]`), a `cards` or `rows` item without a bold start, an
image that is not among the files, a front matter key that does not exist, or a broken diagram
with its line. Fix the Markdown and generate again.

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
