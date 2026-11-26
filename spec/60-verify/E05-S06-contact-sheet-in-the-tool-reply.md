# E05-S06 Contact sheet in the tool reply

> Status: in verification
> Epic: [E05 Authoring feedback](../50-todo/E05-authoring-feedback.md)

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

- [x] With a preview configured, a deck run has a contact sheet and the reply says so; without one,
      replies are unchanged.
- [x] No browser, no network; the same deck gives the same PNG.
- [x] Docs and changelog describe the hook and how to pass the image to Anthropic's and OpenAI's APIs.

## Open questions

1. Where does the implementation live: the Pptx package with a delegate for painting, or the Svg
   package with a reference to the Pptx package?

> The Svg package, which already paints slides, provides the hook for `DocumentTool`. (2026-11-26)

2. Should PDFs get a contact sheet of their pages too (needs a PDF painter)?

> Not now: decks only. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

None. `DocumentToolOptions.Preview`, `DocumentToolRun.ContactSheet`, `IDeckPreview` and
`DeckContactSheet` are additions; without `Preview`, replies, guide and output are byte for byte as
before (the existing expected-file tests are unchanged). The Svg package gains a dependency on the
Pptx package (and so on `DocumentGeneration`, the Open XML SDK and Markdig), recorded in the changelog.

### Decisions and deviations

- **`IDeckPreview.MaxSlides` (60)** is a constant on the interface, so both sides agree on the limit:
  `DeckContactSheet` reads only slides 1 to 60 (`DeckSvgOptions.Slides`), and `DocumentTool` adds the
  note. The note is a reply key of its own, `contactSheetNote` ("The contact sheet shows slides 1 to 60
  of 75."), not a `GenerationNote`: a new note code would have changed every deck guide's list of notes,
  and the note is about the sheet, not the Markdown.
- **What is a deck:** a generator whose `ContentType` is the PresentationML MIME type or whose
  `FileExtension` is `.pptx`. `IMarkdownGenerator` is unchanged.
- **Buffering:** with a preview, a deck is generated into a `MemoryStream`, then copied to `output`
  and handed to the preview, so `output` need not be readable or seekable. The bytes written are the
  same.
- **Painting errors:** a `DocumentConversionException` from the preview leaves the run successful,
  without `contactSheet`. Any other exception (e.g. `ArgumentException` from oversized
  `ContactSheetOptions`) is a host error and propagates, as elsewhere in `Run`.
- **Quality:** slides are painted at twice the thumbnail width (640 px by default) and scaled down by
  `DeckPreview.ContactSheet`, so small text stays legible; labels in Liberation Sans unless the caller's
  `ContactSheetOptions.LabelFont` says otherwise. `new DeckContactSheet(fonts, sheet)` takes fonts for
  non-embedded families and the layout.
- **The guide section** *Looking at the slides* follows *What comes back*, and appears only when a
  preview is set and at least one offered generator writes decks.
- **Other APIs:** the docs show Anthropic's `tool_result` content array, OpenAI's Responses API
  `function_call_output` list, and, where a tool result holds text only, the image in the next user
  message. These shapes are documented from the public API shapes; they are not exercised by tests.

### Verification

- `DocumentToolTests` (fake preview): reply gets `contactSheet` as the last key, the preview sees the
  bytes written and the result, PDFs/Word/checks get nothing and do not call it, the note above 60
  slides, a failing painter, the guide section present only with a preview and a deck generator, and
  the guide otherwise identical.
- `DeckContactSheetTests` (real painting): a sheet four columns wide (1 360 px), the same PNG for the
  same deck (also called directly), a 63-slide deck giving the same sheet size as a 60-slide one, no
  sheet for a PDF, `null` for empty bytes.
- "No network" is by construction (DeckSvg and SkiaSharp read only the bytes given); there is no test
  that blocks the network.

### Loose ends

- PDFs and Word documents get no contact sheet (story decision); a PDF painter would be a story of
  its own.
- Large decks make tall sheets (about 3 000 px for 60 slides); model APIs scale them down. A host can
  pass `ContactSheetOptions` with more columns.
