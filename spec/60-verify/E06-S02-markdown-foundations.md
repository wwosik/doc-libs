# E06-S02 Markdown foundations

> Status: verify — implemented 2026-11-13 by doc-2
> Epic: [E06 Markdown syntax](../50-todo/E06-markdown-syntax.md)

## Summary

The PPTX and PDF generators understand four more pieces of ordinary Markdown: footnotes
(`[^1]`), task lists (`- [ ]`, `- [x]`), definition lists (`Term` / `: description`) and a
document language (`lang: de` in the front matter). The language picks the texts the generators
write themselves, in English or German, and tags the text for spell checking and accessibility.
`Drawing` also gains an even-odd fill rule and rejects invalid colours and numbers.

## Context

- Both parsers build their own Markdig pipeline: `DeckParser.BuildPipeline` and
  `MarkdownParser.BuildPipeline` use pipe tables, emphasis extras, auto links and alert blocks.
  The PDF pipeline also uses auto identifiers. Neither uses `UseFootnotes`, `UseTaskLists` or
  `UseDefinitionLists`. Today `[^1]` is written as text, `- [ ] item` keeps its brackets, and a
  definition list is a paragraph followed by a line starting with `:`.
- The generators write only a few texts of their own:
  - PDF alert titles: `MarkdownParser` writes the alert kind in title case ("Note", "Warning"…).
  - The cover date, written by both generators as `dd.MM.yyyy` with the invariant culture.
  - The PDF page numbers, written as `n / N`, which have no words.
  - The text of decks has no language tag, except drawings, which write `lang="en-US"`
    (`DrawingWriter`). PowerPoint therefore spell-checks every deck in the machine's language.
  - The PDF carries no `/Lang` and no `dc:language`.
- Front matter keys are read by `FrontMatter`. `template`, `title`, `author` and `labels.*` are
  shared; the rest are per format (`PptxGenerator.Keys`, `PdfGenerator.Keys`).
- `DrawingShape` has no fill rule:
  - SVG writes the default, `nonzero`.
  - The PDF writes `f` / `B`, which is non-zero.
  - PPTX writes one `a:path` per shape. DrawingML has no fill rule, so how PowerPoint fills
    crossing subpaths has to be checked in Office.
  - An invalid colour or a NaN coordinate goes straight into the SVG, PDF or DrawingML and gives
    a broken file.
- Decks already have a term/description look: `::: rows` around `- **Term** – text`
  (E02-S01).
- Notes come back through `GenerationResult` (E05-S03).
- Decided in the analysis:
  - English and German texts.
  - Raw HTML stays text.
  - Keys and values are English.
  - Other languages come as data.

## Proposed approach

1. **Pipelines.** Add `UseFootnotes`, `UseTaskLists` and `UseDefinitionLists` to both parsers.
   Nothing else in the existing syntax changes meaning.
2. **Footnotes.**
   - **References.** A reference becomes a superscript number. A new `RunStyle.Superscript`
     uses the same baseline shift as in Office, about 30 % up and at 65 % size.
     - PDF numbers references through the whole document.
     - A deck numbers them per slide.
   - **PDF.** The footnote texts go at the end of the document under a heading from the
     language's texts ("Notes" / "Anmerkungen"), numbered like the references. Footnotes at the
     foot of each page belong to E03-S02.
   - **PPTX.** The footnotes of a slide go in small text (the table minimum size) at the foot of
     its content area. `Fit` reserves their height. When a slide is split, each part carries the
     footnotes it references.
   - **Missing definitions.** A reference without a definition stays text, as Markdig leaves it,
     and gets a new note `footnote-missing`. A definition nobody references is left out silently.
3. **Task lists.** An item `- [ ]` or `- [x]` gets an empty or ticked box in place of its bullet,
   and its text stays as written.
   - PDF draws the box as a shape, because Liberation Sans has no ☐ and ☒.
   - PPTX uses the Wingdings bullet characters U+F0A8 (empty box) and U+F0FE (ticked box), which
     ship with Office on Windows and Mac. Nesting and numbering work as in other lists.
4. **Definition lists.**
   - PDF writes the term in bold and each description as an indented paragraph.
   - On deck slides they get the `::: rows` look: the term in bold in the left third and the
     description beside it, with rules.
   - Several descriptions of one term are separate paragraphs in the same row.
5. **Document language.**
   - **Setting it.**
     - A shared front-matter key `lang` takes a BCP 47 tag (`en`, `de`, `de-CH`, `en-GB`).
     - Hosts set the default with `PptxOptions.Language` and `PdfOptions.Language`; the default
       is `en`.
     - The key is listed in both authoring guides.
   - **Texts.** A new public record `GeneratedTexts` holds every text a generator writes: the
     five alert titles and the footnotes heading, plus whatever later stories add, such as
     "(cont.)", "Contents", "Figure" and "Table".
     - The library ships `GeneratedTexts.English` and `GeneratedTexts.German`.
     - A host can pass more languages through a new option, `Texts`
       (`IReadOnlyDictionary<string, GeneratedTexts>`).
     - A tag is matched first in full (`de-CH`), then by its language (`de`), then falls back to
       English with a new note, `language-unknown`.
   - **Tags.** Decks write `lang` on every run and `endParaRPr`, drawings included, instead of
     the fixed `en-US`. The PDF writes `/Lang` in the catalog and `dc:language` in the XMP.
   - **Cover date.** It keeps `dd.MM.yyyy` in every language (see question 5).
6. **Drawing.**
   - **Fill rule.** `DrawingShape.FillRule`, an enum of `NonZero` (the default) and `EvenOdd`.
     SVG writes `fill-rule="evenodd"`, and the PDF writes `f*` / `B*`. PPTX writes the path as
     it does today; DrawingML has no fill rule, and how PowerPoint fills it is checked in Office
     and recorded in the docs.
   - **Validation.** A new internal method `Drawing.Validate()` is called by `DrawingSvg.Write`,
     `DrawingWriter` and the PDF layout. It throws `ArgumentException` for:
     - a colour that is not six hex digits;
     - a non-finite or negative size, stroke width, corner radius or dash;
     - a non-finite coordinate.

     The message names the item's index and kind.
7. **Guides, docs, changelog.**
   - Both authoring guides get sections on footnotes, task lists and definition lists, and the
     key `lang`. The expected guides are regenerated.
   - The docs of DocumentGeneration, Pptx and Pdf describe the new syntax, `GeneratedTexts` and
     the fill rule.
   - The two new note codes go in the notes table.
   - `CHANGELOG.md` records new behaviour of existing Markdown under breaking changes:
     - `[^1]`, `- [ ]` and `:` lines no longer come out as written;
     - decks and PDFs now carry language tags;
     - invalid drawings throw.

## Acceptance criteria

- [ ] `[^1]` with its definition becomes a superscript number and a footnote:
  - in PDF, at the end of the document under the language's heading;
  - in a deck, at the foot of the slide that references it, including on the part of a split
    slide that references it.
- [ ] A reference without a definition stays text and gives `footnote-missing` with its line.
- [ ] `- [ ]` and `- [x]` show an empty and a ticked box instead of a bullet in both formats. In
  PDF, no character is replaced or reported.
- [ ] A definition list is a bold term with indented descriptions in PDF, and a row of the
  `::: rows` kind on slides.
- [ ] `lang: de` gives "Hinweis", "Tipp", "Wichtig", "Warnung", "Vorsicht" and "Anmerkungen".
  `lang: de-CH` falls back to German, and `lang: pl` falls back to English with
  `language-unknown`.
- [ ] Every run in a deck carries the document's language tag, and the PDF carries `/Lang` and
  `dc:language`. Both still validate: the Open XML validator for decks, and PDF/A for PDFs.
- [ ] A host-supplied `GeneratedTexts` for `pl` is used for `lang: pl`.
- [ ] `FillRule.EvenOdd` cuts a hole in SVG and PDF, as tests show. The PPTX behaviour has been
  checked in Office and is documented.
- [ ] A NaN coordinate or the colour `red` throws `ArgumentException` from every drawing writer.
- [ ] Samples without the new syntax produce the same decks and PDFs as before, apart from the
  language tags.
- [ ] The guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Where do footnotes go on slides? At the foot of the slide that references them (proposed);
   dropped with a note; or collected on a last "Notes" slide?

> Answered by the human (2026-11-13): At the foot of the slide that references them.

2. Where do footnotes go in PDF until E03-S02 brings page-foot notes? At the end of the
   document (proposed), or at the end of each `#` chapter?

> Answered by the human (2026-11-13): At the end of the document.

3. Task-list boxes in decks: Wingdings bullet characters (proposed: they stay real list bullets,
   which PowerPoint can edit), or drawn shapes, which look the same everywhere but do not move
   with the text?

> Answered by the human (2026-11-13): Wingdings bullet characters.

4. Definition lists on slides: the `::: rows` look (proposed), or a bold term line with the
   description indented under it, like PDF?

> Answered by the human (2026-11-13): The `::: rows` look.

5. Cover date: keep `dd.MM.yyyy` in every language (proposed: nothing moves), or format it per
   language, for example "13 November 2026" and "13. November 2026"?

> Answered by the human (2026-11-13): Keep `dd.MM.yyyy` in every language.

6. An unknown language: fall back to English with a note (proposed), or throw as for an
   unknown front-matter value?

> Answered by the human (2026-11-13): Fall back to English with a note.

7. Should hosts be able to add languages through `Texts` (proposed), or should the library only
   know English and German for now?

> Answered by the human (2026-11-13): Yes, through `Texts`.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

- Footnotes, task lists and definition lists are now read: `[^label]`, a list item starting `[ ]` or
  `[x]`, and a `:   ` line after a one-line paragraph no longer come out as typed.
- Every deck run and paragraph end carries `lang` (default `en`, replacing the fixed `en-US` of
  drawings), and decks have `dc:language` in their core properties. PDFs carry `/Lang` and
  `dc:language`. Nothing visible changes.
- Invalid drawings throw `ArgumentException` in `DrawingSvg.Write`, decks and PDFs.
- `wwosik.DocLibs.DocumentGeneration` now depends on Markdig, as the generators already did.

All four are in `CHANGELOG.md` under *Unreleased*.

### Decisions

- **Shared syntax.** The pipelines get the extensions through `MarkdownSyntax.UseDocumentSyntax()`
  in DocumentGeneration, together with the `footnote-missing` check. That is why the shared
  package now references Markdig.
- **PDF footnotes** follow the last chapter under a `##` heading, so they do not take a page of
  their own. They are an ordered list numbered by Markdig's order, through the whole document.
  References are not links.
- **Deck footnotes** are numbered per `##` section, in the order of first reference.
  - A split slide keeps the section's numbers, so a later part can show footnote 2 alone.
  - Fitting measures every part with all of the section slide's footnotes, which can be
    conservative for a part with fewer.
  - The content area shrinks by the footnotes' height, to half its height at most.
  - A footnote's paragraphs are joined by a space.
  - A reference inside a footnote is not resolved on slides.
- **Definition lists** make their section a flow slide, as containers do, because rows exist only
  there. A description with anything but text (a list, code) throws, as in `::: rows`. In PDFs a
  term is kept on its page with the start of its description.
- **Task boxes in decks** use Wingdings, declared as a symbol font (`pitchFamily`/`charset` 2),
  also in placeholders, where only the indents come from the template. In PDFs they are drawn
  shapes: a box, and a tick for `[x]`.
- **GeneratedTexts** has non-required `init` properties that default to English, so a host
  language is `GeneratedTexts.English with { … }` and stays complete when texts are added later.
  The tag is written as given. A tag that is not BCP 47 shaped is a `DocumentConversionException`.
- **PDF metadata.** PdfPig leaves no room in the XMP packet for `dc:language`, so the in-place
  rewrite turned into `PdfRenderer.Splice`.
  - It replaces bytes in an uncompressed object and moves every later offset in the
    cross-reference table, and the `startxref` value.
  - `/Lang` goes into the catalog the same way.
  - The XMP is still rewritten in place when it fits.
  - Tests check that every in-use xref entry points at its `n 0 obj`, for a short and a long title.
- **Beyond the plan:**
  - The Mermaid SVG reader takes `fill-rule` (attribute, `style`, inherited) into `FillRule`.
  - Decks also get `dc:language` in their core properties.
  - Drawing validation also covers hotspots and hover points.

### Verification

- **Tests.** All of them pass: 109, 104, 67, 21, 195, 71, 135 and 1, with 6 Windows-only tests
  skipped. The build has 0 warnings. New tests are in `MarkdownFoundationTests` of
  DocumentGeneration, Pptx and Pdf, plus fill-rule cases in `SvgDrawingTests`.
- **Unchanged output.** The four samples (deck, layouts, report, long) were generated with the
  previous code and this change, as decks and PDFs, with Mermaid kept as code.
  - PDF pages are identical letter by letter (value, position, font, size) and in their operators.
  - The decks are identical once the `lang` attributes and `dc:language` are removed.

### To verify by hand

1. **Even-odd fill in PowerPoint** (an acceptance criterion left open). DrawingML has no fill rule.
   The docs say PowerPoint decides how crossing subpaths fill. Check what it does with a ring of
   two rectangles in one path, and adjust the docs, or write holes as separate paths. No Office on
   this machine.
2. **Wingdings boxes:**
   - PowerPoint on Windows and Mac.
   - LibreOffice, which may substitute the font.
3. **Footnotes on a brand's own template**, where the content placeholder is close to the footer.
4. **veraPDF** on a PDF with `/Lang` and a grown XMP stream (not run here).

### Loose ends

- Footnotes at the foot of the page (E03-S02) can replace the end list.
- More generated texts ("(cont.)", "Contents", "Figure", "Table") belong in `GeneratedTexts` when
  the stories that write them come; `ContinuationSuffix` could then default from it.
- Adjacent references (`[^1][^2]`) are separate runs, but in a PDF they read as "12".
