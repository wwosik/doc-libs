# E06-S06 Audience versions

> Status: in verification
> Epic: [E06 Markdown syntax](../50-todo/E06-markdown-syntax.md)

## Summary

One Markdown source holds several versions of a document. The customer's version is the default; the
internal version adds the team's notes and parts; the trainer's version adds solutions to exercises.
The front matter or the host's options choose the version, in every generator.

## Context

- **Shared syntax.** Markdown passes through one shared syntax layer:
  - containers are checked in `src/wwosik.DocLibs.DocumentGeneration/LayoutSyntax.cs` (`Containers`,
    unknown names throw);
  - block markers (`{.build}`, `{.frag}`) are parsed in `BuildMarker.cs`;
  - heading attributes in `HeadingAttributes.cs`.
- **Classification** is `classification: public|internal|confidential` and `draft:` (`FrontMatter.cs`
  lines 22–25, `Marking.cs`). Front matter can raise the host's classification but not lower it.
- Every generator already filters or rewrites the Markdig tree before writing: decks in
  `DeckParser.cs`, PDFs in `MarkdownParser.cs`, Word in `DocxContent.cs`.

## Proposed approach

1. **The versions.** A new enum `Audience` (`Customer`, `Internal`, `Trainer`) and the front-matter
   key `audience: customer | internal | trainer`. `PptxOptions.Audience`, `PdfOptions.Audience` and
   `DocxOptions.Audience` win over the key. The default is `Customer`.
2. **The syntax:**
   - `::: internal` … `:::` holds blocks for `internal` and `trainer` only;
   - a line `{.internal}` before a block marks that block;
   - `{.internal}` on a `#` or `##` heading marks the whole chapter or slide;
   - `::: exercise` is shown to everyone, and the `::: solution` after it to `trainer` only.

   A `::: solution` that does not follow an exercise is an error. Exercises are numbered
   ("Exercise 1", `GeneratedTexts.Exercise`), and a solution carries its exercise's number.
3. **One filter before every generator.** `AudienceFilter.Apply(document, audience)` removes what the
   version does not show, before parsing into slides or pages. Generators therefore see only their
   version, and slide splitting, numbering and contents follow it.
4. **Notes.** `audience-omitted` counts what a version left out, once per document, with the lines.
5. **Marking.** A version other than the customer's is classified `internal`, unless front matter or
   options set a classification. `classification: public` turns it off for that document.
6. **Helpers and tools:**
   - `AudienceVersions.Distinct(document)` names the versions whose content differs, so a host
     generates only those.
   - `DocumentTool` gets an `audience` parameter.
   - The authoring guides get a section "Versions".
7. **Converters** are not changed: a converted document is one version.

## Acceptance criteria

- [x] The three versions of a sample differ exactly as marked, in decks, HTML decks, PDFs and Word.
- [x] Non-customer versions are marked internal unless set otherwise.
- [x] Misplaced markers (`{.internal}` on a `###`, a stray `::: solution`) throw with the line.
- [x] Documents without the syntax are unchanged.
- [x] Docs, guides, tool schema and changelog updated.

## Open questions

1. Are the names right (`customer`, `internal`, `trainer`), or should they be configurable by the host?

> Fixed names: `customer`, `internal`, `trainer`. (2026-11-26)

2. Should exercises be numbered ("Exercise 1") by the generator?

> Yes: the generator numbers `::: exercise` and gives its `::: solution` the same number. (2026-11-26)

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

Only for Markdown that failed or stayed text before, and for texts (all in `CHANGELOG.md`):

- `### Title {.internal}` (levels 3 to 6) used to stay text ("Title {.internal}") and now throws.
- The valid front-matter keys in error messages name `audience`; the valid containers end with
  `internal, exercise, solution`. The three authoring guides have a section *Versions*, an `audience`
  row and (PDF, Word) the note `audience-omitted`; the tool's schema, guide and argument errors name
  the optional parameter `audience`.
- Documents without the syntax generate exactly as before: the filter returns before changing
  anything when it finds none, and the whole existing test suite passes unchanged.

### Decisions and deviations

- **A tree filter, not a text filter.** `AudienceFilter.Apply` (`Audiences.cs` in the shared package)
  works on the Markdig tree each parser already has, right after `PageBreaks.Flatten` (`DeckParser`,
  `MarkdownParser`, and `DocxContent.Parse` through a new optional callback), so code blocks that show
  the syntax are never touched and line numbers stay the original ones. It first checks every marker
  (errors do not depend on the version), then removes chapters, then walks the containers.
- **Exercises and solutions are taken apart** like page breaks: a bold paragraph "Exercise 1" /
  "Solution 1" (new `GeneratedTexts.Exercise` and `Solution`, German "Übung", "Lösung") is inserted in
  the container's place, followed by its content. Exercises are numbered in the version's order (an
  exercise inside an internal part is not counted in the customer's version). Neither container takes
  arguments (an exercise title was not asked for).
- **`{.internal}` paragraphs** follow the build markers' placement rules (top level, directly inside a
  `:::` container or alert), with the line in every error. `{.internal .frag}` keeps the `{.frag}`;
  `{.internal}` followed by a build marker marks the marker and its block together. On a heading the
  token is removed in every version, so `{.dark .internal}` leaves `{.dark}`; a `#`/`##` with it inside
  a container throws.
- **Solutions must directly follow their exercise** (the previous sibling in the same container).
- **Options are `Audience?`,** `null` meaning "the front matter's, else customer". The front matter's
  value is validated even when the options win.
- **Marking:** `Marking.Resolve` got an optional `audience`; a non-customer version becomes `Internal`
  when the host's classification is `Public` and the front matter has no `classification`. A host
  cannot ask for "public" explicitly through the options (the enum's default is `Public`); the front
  matter's `classification: public` does.
- **The note** `audience-omitted` is one per generation, its line the first omitted line, the message
  listing count and lines. The deck guide leaves it out of its notes list (word limit); PDF and Word
  list it.
- **Tool:** `IMarkdownGenerator.ForAudience(audience)` is a new default interface method (returns the
  generator itself), implemented by the three generators' bound instances; it does not override an
  `Audience` the host set in the options. `DocumentToolRun.Generator` stays the generator offered.
- **Authoring guides:** a shared `AuthoringGuides.Versions(audience)` section before the front matter,
  and an `audience` row in the shared front-matter table (it says when the host fixes the version). The
  deck guide was at its 3,500-word limit: a few sentences were shortened (page breaks, errors,
  speaker notes, transitions, layout fitting) to make room; it is at about 3,499.
- **HTML decks** need nothing of their own: they draw the `.pptx` of the chosen version; tested.
- **Converters** are unchanged.

### Verification concerns and loose ends

- `DocumentCheck`'s style rules (words, bullets, titles, statistics) still walk the whole Markdown,
  every version's content included; only the generation run inside a check is filtered.
- A `::: pagebreak` (PDF, Word) whose following block a version removes is lost in that version, as
  `PageBreaks.Flatten` runs before the filter so that unclosed breaks keep their headings at the top
  level.
- `AudienceVersions.Distinct` reads the Markdown with a block-only pipeline and reports errors with
  `DocumentFormat.Unknown`.
- Tests: `src/wwosik.DocLibs.DocumentGeneration.Tests/AudienceTests.cs` (50 cases: all four outputs ×
  three versions read back by the converters and the HTML text, notes, front matter vs options, German
  labels, misplaced markers in every version, build markers, an internal chapter, marking, `Distinct`,
  the tool parameter). The whole solution's tests pass on Linux.
