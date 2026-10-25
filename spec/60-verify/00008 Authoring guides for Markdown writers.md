# 00008 Authoring guides for Markdown writers

> Status: verify — implemented 2026-10-23

## Summary

The PPTX and PDF generators return, at runtime, an authoring guide for whoever writes the
Markdown — often an AI agent working through a host app. The guide is generated from the caller's
options (template, brands, renderers, fonts), so a host can pass it on as it is, and it changes in
the same commit as the syntax it describes.

Depends on 00005 (layouts), 00006 (fonts) and 00007 (one exception type), because the guide
describes them.

## Context

- `docs/` describes the libraries for C# callers: options, brands, templates, errors. The person
  or agent writing the Markdown has no description of their own; what a heading becomes, which
  layouts exist, which front matter keys apply and what to avoid are spread across tables written
  for implementers.
- Hosts that let agents write documents end up copying the syntax into their prompts by hand, and
  those copies go stale with every feature added here.
- Part of what a writer needs depends on the caller's options:
  - diagrams exist only if a renderer such as `MermaidRenderer` is registered;
  - the valid `template` names are `neutral`, the template's brand and `Options.Brands`;
  - the valid front matter keys depend on the template (`layouts.*` on a custom template,
    `showTagline` on the built-in one);
  - which characters work depends on the brand's fonts. In PDFs a missing character is an error
    unless the host replaces it (`PdfOptions.ReplaceUnsupported`); in decks PowerPoint substitutes.
- Parts only the host knows: how images are referenced, which options it locks, its limits.

## Proposed approach

1. **API:**

   ```csharp
   string PptxGenerator.AuthoringGuide(PptxOptions? options = null);
   string PdfGenerator.AuthoringGuide(PdfOptions? options = null);
   ```

   Markdown starting at `##`, in English, so a host can put it under its own `#` and add its own
   sections. Pass the same options as to `Generate`.
2. **Templates:** one embedded template per format (`AuthoringGuide/Pptx.md`,
   `AuthoringGuide/Pdf.md`) with `{{placeholders}}`, filled by a shared internal `AuthoringGuides`
   (`Template`, `Fill`, `FrontMatter`, `Renderers`, `Characters`). Sections: structure (what `#`,
   `##`, `###` become; text, lists, tables, images, code), layouts (PPTX: each with a short example
   and when to use it; PDF: accepted and shown plainly), front matter, limits, one complete example.
   Written for the writer: imperatives and examples, no type names, error messages quoted so an
   error can be matched to its fix. Budget: under about 2,500 words per rendered guide.
3. **From the options:**
   - `template` values and front matter keys from the same lists the front matter parser uses
     (`BrandRegistry.Expected`, `Template.Keys`), with one line on what each key does.
   - **Characters to avoid** from the brand's actual faces: a fixed list of symbols writers reach
     for (`✓ ✗ ● ○ ■ □ ▲ ► → ⇒ ⇔ ≤ ≥ ≠ ≈ € … ‖ ★`, arrows, box drawing) checked against the `cmap`
     of the text, table and mono faces (new `TrueTypeInfo.HasCharacter`, cmap formats 4 and 12,
     cached per font). Each symbol has replacements in order of preference, the last one ASCII
     (`⇒` → `→`, else `->`), and the guide shows the first one every face has. Rows note
     "(fine in code)" / "(in code only)" where faces differ. PDF: emoji always listed, and the
     wording follows `ReplaceUnsupported` (error vs. replaced). PPTX: a table only when the brand
     has fonts, else a general sentence about substitution.
   - **Diagrams from the renderers:** `ICodeBlockRenderer` gets a default interface member
     `string? AuthoringGuide => null`; `MermaidRenderer` returns its `## Diagrams` section (from
     its own embedded `AuthoringGuide/Diagrams.md`: the types that are drawn, what stays code,
     common mistakes), whose error sentence follows `OnError`. Existing renderers need no change.
4. **Tests**, in the PPTX and PDF test projects (both then reference the Diagrams package):
   - every ` ```markdown ` example in each rendered guide generates (and, for decks, validates)
     for `neutral`, `sample` and a custom template, with `MermaidRenderer` registered;
   - every container name, alert kind, heading attribute and front matter key the parser accepts
     appears in the guide (the lists made `internal`, read through `InternalsVisibleTo`);
   - keys and brands follow the options; the diagram section appears only with a renderer;
   - every replacement offered is in all faces of each brand, and the last replacement of every
     symbol is ASCII; the neutral PDF guide names `✓` (Liberation lacks it);
   - the word budget;
   - snapshots `authoring-guide-<case>.expected.md` (PPTX: neutral, custom; PDF: neutral,
     sample), refreshed with `DOCLIBS_UPDATE_EXPECTED=1`, so a change to the guide shows in review.
5. **Docs and rules:** a section *For hosts that let others write the Markdown* in the PPTX and PDF
   docs (call `AuthoringGuide(options)` with the options passed to `Generate`; add what only the
   host knows; pass `DocumentConversionException.Reason` back to the writer). A rule in
   `ai/standards.md` and `docs/README.md`: a change to the Markdown syntax, front matter or limits
   updates the authoring guide in the same change.

## Acceptance criteria

- [ ] Both generators return a guide covering structure, layouts (PPTX), front matter, limits and
      an example; with `MermaidRenderer` registered also diagrams, without it not.
- [ ] Template names and front matter keys follow the options passed.
- [ ] The characters to avoid follow the brand's fonts, with replacements those fonts have.
- [ ] Tests generate every example, check that every syntax element appears, and compare the
      rendered guides with committed snapshots.
- [ ] Each rendered guide is under about 2,500 words.
- [ ] Docs and `ai/standards.md` require the guide to change together with the syntax.

## Open questions

1. Generated from the options (proposed), or one fixed text per format that hosts trim?

> Confirmed by the human (2026-10-23): generated from the options.

2. One guide per format (proposed), or one combined guide?

> Confirmed by the human (2026-10-23): one per format.

3. English only?

> Confirmed by the human (2026-10-23): English only.

4. Some guidance is judgement rather than code (about six bullets per slide, up to about twelve
   nodes per diagram, two to four cards, a dark slide for one key message only). Keep those
   numbers?

> Confirmed by the human (2026-10-23): kept — adjust them in the templates if they are off.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Deviations from the plan

- **One template per format** (`AuthoringGuide/Pptx.md`, `AuthoringGuide/Pdf.md`, plus
  `AuthoringGuide/Diagrams.md` in the Diagrams package) with `{{placeholders}}`, rather than
  section files. The front matter section is generated in code (`AuthoringGuides.FrontMatter`),
  since nearly all of it is the table of keys.
- **Snapshots:** PPTX `authoring-guide-sample` and `-custom`; PDF `authoring-guide-neutral` and
  `-sample` (the plan said PPTX neutral and custom): `sample` has fonts, so its snapshot holds the
  character table, which `neutral` decks do not have.
- In decks the code face counts only when the brand's mono family is the code font; a deck without
  brand fonts gets a general sentence and no table, because PowerPoint substitutes.
- The PPTX and PDF test projects now reference the Diagrams package, so the guide tests use
  `MermaidRenderer`.

### Worth a look during verification

- Read `authoring-guide-*.expected.md` in both test projects as a writer would. The sample's table
  is long because PT Sans Narrow has none of the symbols — true, and it shows how a narrow table
  font limits writers.
- The examples use a made-up "Network expansion 2027" report, as the docs already did.
- The guides link to their own headings (`[layouts](#layouts)`); a host that nests them keeps those
  anchors unless it renames the headings.

### Breaking changes

- None: new API, and `ICodeBlockRenderer.AuthoringGuide` is a default interface member.
