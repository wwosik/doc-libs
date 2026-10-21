# wwosik.DocLibs.DocumentGeneration

What the generators [wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md)
and [wwosik.DocLibs.DocumentGeneration.Pdf](wwosik.DocLibs.DocumentGeneration.Pdf.md) share. A caller
never uses it directly — it comes along as their dependency. Everything in it is `internal`,
visible to the generators and their tests. Depends only on
[wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md); AOT-compatible.

## Brands

`Brand` holds what a built-in design takes from a brand: the full theme colour scheme, the primary,
link and footer colours, the gradient of the PPTX section divider, the table colours, the footer
text and the images (logo, white logo for dark backgrounds, cover artwork, cover claim). It is
`internal`; the only instance is `Brand.Neutral`:

| | Neutral |
| --- | --- |
| Primary (titles, headings) | `1F2937` |
| Links | `1D4ED8` |
| Table header / bands / lines | `1D4ED8` / `F3F4F6` / `D1D5DB` |
| Footer text | — |
| Images | — |

The generators already draw a brand's footer text and images where a brand has them; making
`Brand` something a caller can supply is plan 00001 in `spec/50-todo/`.

## Front matter

Both generators read options from a block at the very top of the Markdown; the keys each one
understands are in its own documentation. Values there **override** what the caller passed.

```markdown
---
template: neutral
title: Network expansion 2027
---
```

- The block starts with `---` on the first line, followed directly by a non-blank line, and ends
  at the next `---` or `...`. Inside: one `key: value` per line; blank lines and `#` comments are
  allowed; values may be quoted (`"…"` or `'…'`). Keys and choice values are case-insensitive. It
  looks like YAML but is only this flat subset — no nesting, no lists.
- An opening `---` followed by a blank line is a thematic break, not front matter.
- **Shared keys:** `template` (applied first, whatever its position), `title`, `author`. The rest
  are the generator's.
- `template` naming the template the caller chose keeps the caller's values; naming another one
  starts from that template's defaults.
- **Mistakes throw** `DocumentConversionException` with the generator's format: a line that is not
  `key: value`, a key without value, a duplicate key, a block that is never closed, an unknown key
  or one the template does not support (the message lists the valid keys), an invalid value (the
  message lists the valid ones).

## Image headers

`ImageInfo` reads the format (PNG, JPEG, GIF) and pixel size from an image's header, for both
generators.
