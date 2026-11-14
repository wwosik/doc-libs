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

{{frontMatter}}

## Characters

{{characters}}

## When something is wrong

Generation stops with a message that says what to fix, such as a front matter key that does not
exist or a value it does not take. Fix the Markdown and generate again.

## Notes you may get back

{{notes}}

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
