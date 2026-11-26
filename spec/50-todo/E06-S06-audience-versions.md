# E06-S06 Audience versions

> Status: todo
> Epic: [E06 Markdown syntax](E06-markdown-syntax.md)

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

   A `::: solution` that does not follow an exercise is an error.
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

- [ ] The three versions of a sample differ exactly as marked, in decks, HTML decks, PDFs and Word.
- [ ] Non-customer versions are marked internal unless set otherwise.
- [ ] Misplaced markers (`{.internal}` on a `###`, a stray `::: solution`) throw with the line.
- [ ] Documents without the syntax are unchanged.
- [ ] Docs, guides, tool schema and changelog updated.

## Open questions

1. Are the names right (`customer`, `internal`, `trainer`), or should they be configurable by the host?

>

2. Should exercises be numbered ("Exercise 1") by the generator?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
