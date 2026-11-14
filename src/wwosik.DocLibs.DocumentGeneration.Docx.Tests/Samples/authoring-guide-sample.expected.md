## Writing Markdown for a Word document

The Word document is generated from Markdown: the design (styles, fonts, logo, header and footer,
page numbers) comes from the template, applied for you. Write it as a document to read: full
sentences and paragraphs, headings that say what a section is about.

## Structure

| Markdown | Becomes |
| -------- | ------- |
| A `#` heading as the very first line | The cover page. The paragraphs below it, up to the next heading, are the subtitle; the author and the date follow. Without it, there is no cover. |
| `#` to `######` after it | Headings in Word's heading styles, listed in the table of contents. |
| Text before the first heading | Normal text on the first page; the document then has no cover. |

- Every paragraph uses a style of the template. Do not try to format text by other means.
- In this version the body is written as plain text: lists, tables, code and quotes come out as
  paragraphs of their text, without their formatting.

## Table of contents

`toc: true` in the front matter puts a table of contents after the cover, on a page of its own,
headed "Contents" in the document's language (`lang`). It lists the headings down to the level
`tocDepth` gives (default `2`). Word fills in the page numbers when the document is opened; the
headings are listed even where nothing updates it.

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
| `template` | The design: `neutral` or `sample`. |
| `showDate` | `true` or `false`: today's date on the cover and in the footer. Default `true`. |
| `showTagline` | `true` or `false`: the design's tagline on the cover, if it has one. Default `true`. |
| `embedFonts` | `true` or `false`. Leave it out: by default the document carries its fonts. |
| `toc` | `true` or `false`: a table of contents after the cover. Default `false`. |
| `tocDepth` | `1`, `2` or `3`: the heading levels the table of contents lists. Default `2`. |
| `transition`, `build`, `numberChapters` | Settings for decks. A Word document accepts them and ignores them. |
| `labels.success`, `labels.info`, `labels.warning`, `labels.danger`, `labels.neutral` | Status labels for decks, separated by `;`. A Word document accepts them. |

An unknown key, a key given twice or an invalid value is an error that lists the valid keys.

## Characters

The document is set in Liberation Serif. A character this font lacks is shown in a substitute font and looks out of place. Leave out emoji, and write these symbols as shown:

| Avoid | Write instead |
| ----- | ------------- |
| ✓ (fine in code) | `[x]` |
| ✔ (fine in code) | `[x]` |
| ✗ (fine in code) | `[ ]` |
| ✘ (fine in code) | `[ ]` |
| ☐ | `[ ]` |
| ☑ | `[x]` |
| ■ (in code only) | `•` |
| □ (in code only) | `•` |
| ▪ (in code only) | `•` |
| ◦ (in code only) | `–` |
| ★ | `*` |
| ☆ | `*` |
| ▲ (in code only) | `↑` |
| ▼ (in code only) | `↓` |
| ► (in code only) | `→` |
| ◄ (in code only) | `←` |
| ⇒ | `→` |
| ⇐ | `←` |
| ⇔ (in code only) | `↔` |
| ≡ (in code only) | `=` |
| ‖ (in code only) | `\|` |

## When something is wrong

Generation stops with a message that says what to fix, such as a front matter key that does not
exist or a value it does not take. Fix the Markdown and generate again.

## Notes you may get back

The document is still written, but whoever passes it to you may also pass on notes about it, each with its line. Each names what happened:

- `language-unknown`: the language in `lang` has no texts of its own, so alert titles and similar texts are in English. Use `en` or `de`.

## Example

````markdown
---
title: Network expansion 2027
toc: true
---

# Network expansion 2027

Status report for the steering committee, October

## Summary

The upgrade is on schedule: 14 of 20 substations are done. Two sites are delayed by permits, and
the budget is on track.

## Next steps

Obtain the remaining permits in the first quarter, then commission the last sites.
````
