# E05-S06 Contact sheet in the tool reply

> Status: todo
> Epic: [E05 Authoring feedback](E05-authoring-feedback.md)

## Summary

When `DocumentTool` generates a deck, it can hand the agent a picture of all its slides (a contact
sheet), so an agent that sees images can check its own deck. This needs no browser.

## Context

- **`DocumentTool`** (`src/wwosik.DocLibs.DocumentGeneration/DocumentTool.cs`) runs a generator from
  the agent's JSON arguments (`Run(arguments, resources, output)`) and returns a `DocumentToolRun` with
  the reply as JSON (`OkReply`, `CheckedReply`, `ErrorReply`), the result and the check.
- **Slide pictures already exist.** `DeckPreview.Render(slides, options)` and
  `DeckPreview.ContactSheet(result, options)` in `wwosik.DocLibs.DocumentConversions.Svg`
  (`DeckPreview.cs`) paint `DeckSvg`'s slide SVGs with SkiaSharp.
- `wwosik.DocLibs.DocumentGeneration` cannot reference that package, because the Svg package depends
  only on `DocumentConversions`.

## Proposed approach

1. **A hook in `DocumentGeneration`.** `IDeckPreview` with
   `byte[]? ContactSheet(ReadOnlyMemory<byte> pptx, GenerationResult result)`, set as
   `DocumentToolOptions.Preview`. Without it, nothing changes.
2. **The implementation.** It lives where `DeckSvg` and `DeckPreview` meet: a class in the Svg
   package, which already paints the slides and references the Pptx package. PDFs get no contact
   sheet for now. It renders the deck's slides and lays them out as a
   contact sheet: four columns, numbered.
3. **The run.**
   - `DocumentToolRun.ContactSheet` holds the PNG.
   - The reply adds `"contactSheet": "attached"`, so the host knows to pass the image as a tool-result
     image.
   - The guide gets a section "Looking at the slides", shown only when a preview is configured.
4. **Limits.** At most 60 slides on one sheet; above that, the first 60 and a note. Only for formats
   that are decks; PDFs and Word give none.

## Acceptance criteria

- [ ] With a preview configured, a deck run has a contact sheet and the reply says so; without one,
      replies are unchanged.
- [ ] No browser, no network; the same deck gives the same PNG.
- [ ] Docs and changelog describe the hook and how to pass the image to Anthropic's and OpenAI's APIs.

## Open questions

1. Where does the implementation live: the Pptx package with a delegate for painting, or the Svg
   package with a reference to the Pptx package?

> The Svg package, which already paints slides, provides the hook for `DocumentTool`. (2026-11-26)

2. Should PDFs get a contact sheet of their pages too (needs a PDF painter)?

> Not now: decks only. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
