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
  share — the neutral brand, front matter, image headers.
- [wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md): Markdown to
  `.pptx`, in the built-in neutral design or on a caller's template. Cross-platform.
- [wwosik.DocLibs.DocumentGeneration.Pdf](wwosik.DocLibs.DocumentGeneration.Pdf.md): Markdown to
  PDF/A-2b, in the built-in neutral design, with embedded Liberation fonts. Cross-platform.

## Sample files and expected output

The test projects compare each sample in their `Samples/` folder with a committed
`<sample>.expected.md`. The samples are self-made and are built by the generator in
[tools/sample-generator](../tools/sample-generator/sample-generator.cs):

```
./run.sh sample-generator             # .pdf, .docx, .pptx, .xlsx, .rtf, .txt, .zip, and the PNGs of the generator tests
run.ps1 sample-generator --office     # additionally .doc and .xls, saved by Word and Excel
```

To refresh the expected Markdown after changing a converter or a sample, run the tests with
`DOCLIBS_UPDATE_EXPECTED=1`: they then write their actual output over the `*.expected.md` files
instead of comparing. Review the diff before committing. The WindowsOnly expectations can only be
refreshed on Windows with Office; elsewhere those tests are skipped.

The generator tests work the other way round: a Markdown sample is generated into a document for
each built-in template, read back with the matching converter, and compared with
`<sample>-<template>.roundtrip.expected.md` — `deck.md` for PPTX, `report.md` and `long.md` for
PDF. The generated files are also written to `.tmp/review/` for a look.
