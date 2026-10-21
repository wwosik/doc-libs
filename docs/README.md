# Documentation

How the libraries actually work today — not how they are planned to work. Plans live in `spec/`.

Anything here that stops matching the code is a bug: update it in the same change as the code,
or delete it.

## Packages

- [wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md): `.pdf`, `.docx` and
  `.pptx` to Markdown with images, and Markdown ↔ JIRA wiki markup. Cross-platform.
- [wwosik.DocLibs.DocumentConversions.WindowsOnly](wwosik.DocLibs.DocumentConversions.WindowsOnly.md):
  `.doc`, `.rtf`, `.xls` and `.xlsx` through Office automation.
- [wwosik.DocLibs.DocumentGeneration](wwosik.DocLibs.DocumentGeneration.md): what the generators
  share — brands (`Brand`, `BrandPack`), drawings and code-block renderers, front matter, image
  headers.
- [wwosik.DocLibs.DocumentGeneration.Diagrams](wwosik.DocLibs.DocumentGeneration.Diagrams.md):
  Mermaid diagrams as vector graphics in PDFs and editable shapes in decks, through Mermaider.
- [wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md): Markdown to
  `.pptx`, on the built-in master in a brand's design or on a caller's template. Cross-platform.
- [wwosik.DocLibs.DocumentGeneration.Pdf](wwosik.DocLibs.DocumentGeneration.Pdf.md): Markdown to
  PDF/A-2b, in a brand's design, with embedded Liberation fonts. Cross-platform.

## Sample files and expected output

The test projects compare each sample in their `Samples/` folder with a committed
`<sample>.expected.md`. The samples are self-made and are built by the generator in
[tools/sample-generator](../tools/sample-generator/sample-generator.cs):

```
./run.sh sample-generator             # .pdf, .docx, .pptx, .xlsx, .rtf, .txt, .zip, the PNGs of the generator tests and of the sample brand
run.ps1 sample-generator --office     # additionally .doc and .xls, saved by Word and Excel
```

To refresh the expected Markdown after changing a converter or a sample, run the tests with
`DOCLIBS_UPDATE_EXPECTED=1`: they then write their actual output over the `*.expected.md` files
instead of comparing. Review the diff before committing. The WindowsOnly expectations can only be
refreshed on Windows with Office; elsewhere those tests are skipped.

The generator tests work the other way round: a Markdown sample is generated into a document for
each built-in template, read back with the matching converter, and compared with
`<sample>-<template>.roundtrip.expected.md` — `deck.md` for PPTX, `report.md` and `long.md` for
PDF — for the neutral brand and for `sample`, a made-up brand with every image and its own fonts.
The generated files are also written to `.tmp/review/` for a look.

The `sample` brand is a brand pack in
`src/wwosik.DocLibs.DocumentGeneration.Tests/Samples/brand-sample/`, linked into the generator
tests: `brand.json`, four PNGs made by the sample generator, and Liberation Serif 2.1.5 (OFL, with
`fonts/OFL.txt`) as its fonts — a different family from the bundled Sans, so the tests can tell
them apart — with PT Sans Narrow (OFL, `fonts/table/OFL.txt`) as its table and diagram font, and a
diagram palette.

The Diagrams tests render every Mermaid block of `diagrams.md` in the sample brand and compare
the drawing, one item per line, with `mermaid-<type>.expected.txt`. A Mermaider update that
changes its SVG shows up there. They also write `diagrams-<brand>.pdf` and `.pptx` to
`.tmp/review/`.
