# 00007 Renderer errors as DocumentConversionException

> Status: verify — implemented 2026-10-23

## Summary

When a code-block renderer throws (e.g. `MermaidRenderer` on a diagram it cannot draw), the
generators pass it on as a `DocumentConversionException` in their format, with the renderer's
exception as `InnerException`. Every problem in a document then has one exception type, which a
host can catch and pass to the writer.

## Context

- `CodeBlockRenderers.Render` (`src/wwosik.DocLibs.DocumentGeneration/CodeBlockRenderer.cs`) calls
  each renderer and lets its exception through.
- `MermaidRenderer` with `OnError = Throw` throws `ArgumentException`
  (`The mermaid code block at line 12 cannot be drawn: <reason>`).
- Every other document problem — front matter, a missing image, layout names, a character the
  font lacks — is a `DocumentConversionException`. A host that wants to tell "the Markdown is
  wrong" from "the caller is wrong" has to catch both, and cannot tell a renderer's
  `ArgumentException` from a brand validation error.
- Test: `GenerationTests` in the Diagrams tests expects `ArgumentException` from the PDF generator.

## Proposed approach

1. `CodeBlockRenderers.Render` takes the `DocumentFormat` and wraps any exception from a renderer
   that is not already a `DocumentConversionException`:
   `throw new DocumentConversionException(format, ex.Message, ex)`. Both generators pass their
   format.
2. `MermaidRenderer` itself keeps throwing `ArgumentException` (it knows nothing of formats); its
   XML docs and `MermaidErrorMode.Throw` say the generators wrap it.
3. Tests: the Diagrams `GenerationTests` expect `DocumentConversionException` with an
   `ArgumentException` inner exception and the same message (line number included), for PDF and
   PPTX. A renderer that throws `DocumentConversionException` itself is not wrapped twice.
4. Docs: the error table in `wwosik.DocLibs.DocumentGeneration.Diagrams.md`, the
   `ICodeBlockRenderer` section in `wwosik.DocLibs.DocumentGeneration.md`.

## Acceptance criteria

- [ ] A renderer's exception reaches the caller of either generator as a
      `DocumentConversionException` with the original as `InnerException` and the same message.
- [ ] A `DocumentConversionException` from a renderer is passed on unchanged.
- [ ] Tests and docs updated; all tests pass.

## Breaking changes

- Callers catching `ArgumentException` around `PdfGenerator.Generate` / `PptxGenerator.Generate`
  for diagram errors must catch `DocumentConversionException` instead.

## Open questions

1. Wrap every exception from a renderer, or only `ArgumentException` and
   `InvalidOperationException`, letting e.g. `OutOfMemoryException` through unwrapped?

> Confirmed by the human (2026-10-23): every exception except a `DocumentConversionException`, which passes unchanged.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- `CodeBlockRenderers.Render(renderers, request, format)` wraps; `DeckParser` and the PDF
  `MarkdownParser` pass their format. `MermaidRenderer` still throws `ArgumentException`; its XML
  docs and `MermaidErrorMode.Throw` say the generators wrap it.
- The Diagrams `GenerationTests` assert the wrapped exception, the inner `ArgumentException` and the
  message with the line number.
- The PDF docs already described the wrapping before this change; the code now matches them.

### Breaking changes

- Callers catching `ArgumentException` for diagram errors must catch `DocumentConversionException`.
  In `CHANGELOG.md`.
