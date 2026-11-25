# doc-libs compared with its two private derivatives

> Date: 2026-11-25. Subject: where doc-libs (release `2611.15.12`) stands against the private .NET
> derivative and the private Python fork that the
> [2026-11-13 analysis](2026-11-13%20python-fork-port.md) already looked at. Question asked: "are we
> behind?"

Both derivatives are named here only by their kind, as before; neither is referenced in code.

## Summary

- **Against the Python fork, doc-libs is not behind.** The fork has not changed since 2026-11-08.
  Everything the 2026-11-13 analysis chose to port is now in `spec/60-verify/` here. What the fork
  still has and doc-libs lacks is exactly what was **declined** on 2026-11-13 (audience versions,
  backup slides and appendices, training schedule, handout), plus a few converter details (below).
  Its open drafts (variables, includes, cross-references, compare, comments, …) are unimplemented
  there too.
- **Against the .NET derivative, doc-libs is level overall but behind on a few points.** The
  derivative is actively developed (last document change 2026-11-24). It is about the same size (about
  39 000 lines of library C# against about 42 000 here) and it has been taking features *from*
  doc-libs (its code cites doc-libs E02-S02, E04-S05 and E06-S04). Since 2026-11-21 it has
  added a Word generator (E16-S05/S06), marking (E16-S07), slide pictures (E16-S04), audience
  versions (E16-S02), backup slides and appendices (E16-S03) and a **Word round trip** (E16-S08).
  Most of these exist here too. The real gaps are:
  1. the Word round trip,
  2. image styles in Word,
  3. dark brands in PDFs,
  4. the two features declined on 2026-11-13.
- **doc-libs leads** in several places: its PDF converter reads tables, footnotes and code (the
  derivative reads no tables); its slide preview needs no browser (the derivative needs Playwright and
  Chromium); its Mermaid needs no Node.js (the fork needs mermaid-cli); it has public brand packs and
  template roles; and it keeps JIRA wiki markup and the Windows-only converters, which the fork
  dropped.

## Gaps against the .NET derivative

| Feature | Derivative | Here | Worth porting? |
| ------- | ---------- | ---- | -------------- |
| **Word round trip.** `DocxConverter` recognises a Word document of its own generator and gives back the Markdown it came from: front matter, alerts, conclusions, code fences with their language, captions, image rows, task, icon and numbered lists, definition lists, footnotes, `{.newpage}`, `{.unnumbered}`, status words, icons, Harvey balls, `chart` blocks. The source of diagrams and charts is kept in an ignorable namespace (`mc:Ignorable`). | impl (E16-S08) | The DOCX converter reads headings, lists, tables, emphasis, links and pictures; footnotes, comments, headers and footers are not read | **Yes.** Generic, and it completes the "DOCX: once the DOCX generator exists" item of the 2026-11-13 analysis. Use our own namespace URI. |
| Footnotes from any `.docx` | impl (generated documents) | not read | Yes, part of the above |
| `.round` and `fit=cover` in Word (circle crop, crop to the row's shape) | impl | `attribute-ignored` note | Yes, small: `a:srcRect` plus `prstGeom ellipse` |
| `.grey` in Word | not found | `attribute-ignored` | Optional: SkiaSharp already does this for PDFs |
| Dark brands in PDFs: every page painted dark | impl | PDFs never read the dark colours | Maybe, as a brand option; decide whether a dark PDF is wanted at all |
| Audience versions (`audience: customer/internal/trainer`, `::: internal`, `::: exercise`/`solution`, `{.internal}`), and versions other than the customer's marked `internal` by default | impl (E16-S02, E16-S07) | missing | **Declined 2026-11-13.** Reconsider only if a consumer of doc-libs asks |
| Backup slides (`# Backup {.backup}`) and lettered appendices (`{.appendix}` → A, B, A.1) | impl (E16-S03) | missing | **Declined 2026-11-13.** Appendices are a small, generic Word/PDF feature; they could be taken without the backup slides |
| PPTX converter: backup slide back as `{.backup}` | impl | — | Only with backup slides |
| Word core properties (`dc:title`, `dc:subject`, `dc:creator`, `dc:language`) | impl | `w:lang` and language present (`DocxLanguage`); check that all four core properties are set | Check, likely trivial |
| `make_document` replies with a contact sheet of the HTML deck | impl | `DocumentTool` exists; the deck preview (`DeckSvg` → PNG) exists | Small: wire the preview into the tool reply as an optional hook, without a browser |
| HTTP API, file store, application callers | impl | — | No: application code, not a library |

## Gaps against the Python fork

Everything implemented in the fork and not declined on 2026-11-13 is now here. What remains:

| Feature | Fork | Here | Note |
| ------- | ---- | ---- | ---- |
| Audience versions, backup slides, appendices, training schedule, handout | impl | missing | Declined 2026-11-13 |
| DOCX converter: native charts as tables, "Code" style as fences with language, note styles as alerts, typography undone (`plain_quotes`) | impl | missing | Covered by the Word round trip above |
| PDF in the look of the Word template | impl | the PDF draws the brand | Declined: ties the PDF to one Word template |
| Variables, building blocks, CSV/XLSX data, history, compare, abbreviations, cross-references, accessibility, minutes, requirements, signatures, calculating tables, Word comments and review import, page breaks/landscape sections | draft only | missing (page breaks are here) | Not implemented there either; declined 2026-11-13 |

## Where doc-libs is ahead

| Feature | Here | .NET derivative | Python fork |
| ------- | ---- | --------------- | ----------- |
| PDF → Markdown: ruled tables, lists from drawn bullets, code, footnotes, images in place | yes | no tables | tables, code; no footnotes |
| Slide previews | SkiaSharp painter, no browser | Playwright + Chromium | Playwright + Chromium |
| Mermaid | Mermaider, no runtime | Mermaider | mermaid-cli (Node.js + Chromium) |
| Brands as public brand packs, template roles for any `.potx`/`.dotx` | yes | brand packs, fewer template roles | one client's templates only |
| JIRA wiki markup, `.doc`/`.rtf`/`.xls`/`.xlsx` via Office | yes | yes | dropped |
| Published to nuget.org, AOT-compatible packages | yes | private feed | not published |

## Proposed next steps

In order of value per effort. Each would become a story; none is planned yet.

1. **E08: Word round trip.** The DOCX converter reads back a document of `DocxGenerator`: front matter,
   alerts, code language, captions, footnotes, charts, diagram sources. Footnotes are also read from
   any `.docx`. This is the one real gap.
2. **E09: image styles in Word.** `.round` and `fit=cover` (pure Open XML), optionally `.grey` with
   SkiaSharp as in PDFs.
3. **E09/E03: lettered appendices** (`{.appendix}`) in Word and PDF, if the 2026-11-13 decision is
   relaxed for this one part.
4. **E05: contact sheet in the tool reply**, from the existing Skia preview.
5. Check the Word core properties; decide whether dark brands should paint PDFs.

## Open questions

1. Is the Word round trip wanted now? It is the only gap that is neither declined nor small.
2. Should the 2026-11-13 decision against audience versions and backup slides/appendices stand, now
   that both derivatives have them? Appendices alone are generic and small.
3. Should a dark brand give a dark PDF, or are dark designs only for decks?

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
